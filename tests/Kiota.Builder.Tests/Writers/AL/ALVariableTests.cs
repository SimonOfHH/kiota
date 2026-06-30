using System;
using System.IO;

using Kiota.Builder.CodeDOM;
using Kiota.Builder.Refiners;
using Kiota.Builder.Writers;
using Kiota.Builder.Writers.AL;

using Xunit;

namespace Kiota.Builder.Tests.Writers.AL;

public sealed class ALVariableTests : IDisposable
{
    private const string DefaultPath = "./";
    private const string DefaultName = "name";
    private readonly StringWriter tw;
    private readonly LanguageWriter writer;
    private readonly ALConventionService conventions;

    public ALVariableTests()
    {
        writer = LanguageWriter.GetLanguageWriter(GenerationLanguage.AL, DefaultPath, DefaultName);
        tw = new StringWriter();
        writer.SetTextWriter(tw);
        conventions = new ALConventionService(new ALConfiguration());
    }

    public void Dispose()
    {
        tw?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void WritesLabelWithValue()
    {
        var variable = new ALVariable("GreetingLbl", new CodeType { Name = "Label" }, "Default", "Hello");
        variable.Write(writer, conventions);
        Assert.Equal("GreetingLbl: Label 'Hello';", tw.ToString().Trim());
    }

    [Fact]
    public void WritesLabelWithLockedFlag()
    {
        var variable = new ALVariable("GreetingLbl", new CodeType { Name = "Label" }, "Default", "Hello", locked: true);
        variable.Write(writer, conventions);
        Assert.Equal("GreetingLbl: Label 'Hello', Locked = true;", tw.ToString().Trim());
    }

    [Fact]
    public void FallsBackToDefaultValueWhenValueEmpty()
    {
        var variable = new ALVariable("GreetingLbl", new CodeType { Name = "Label" }, "DefaultText");
        variable.Write(writer, conventions);
        Assert.Equal("GreetingLbl: Label 'DefaultText';", tw.ToString().Trim());
    }

    [Fact]
    public void WritesNonLabelVariableWithTranslatedType()
    {
        var variable = new ALVariable("Counter", new CodeType { Name = "integer" });
        variable.Write(writer, conventions);
        Assert.Equal("Counter: Integer;", tw.ToString().Trim());
    }

    [Fact]
    public void LabelsWithDifferentValuesCannotBeCombined()
    {
        var first = new ALVariable("Lbl", new CodeType { Name = "Label" }, "", "A");
        var second = new ALVariable("Lbl", new CodeType { Name = "Label" }, "", "B");
        Assert.False(first.CanBeCombined(second));
    }

    [Fact]
    public void IdenticalVariablesCanBeCombined()
    {
        var first = new ALVariable("Lbl", new CodeType { Name = "Label" }, "", "A");
        var second = new ALVariable("Lbl", new CodeType { Name = "Label" }, "", "A");
        Assert.True(first.CanBeCombined(second));
    }

    [Fact]
    public void EscapesSingleQuoteInLabelValue()
    {
        var variable = new ALVariable("GreetingLbl", new CodeType { Name = "Label" }, "Default", "O'Reilly");
        variable.Write(writer, conventions);
        var result = tw.ToString();
        // AL escapes a single quote by doubling it; the raw value must not appear unescaped.
        Assert.Contains("GreetingLbl: Label 'O''Reilly';", result);
        Assert.DoesNotContain("'O'Reilly'", result);
    }

    [Fact]
    public void EscapesSingleQuoteInLabelDefaultValue()
    {
        var variable = new ALVariable("GreetingLbl", new CodeType { Name = "Label" }, "It's a test");
        variable.Write(writer, conventions);
        Assert.Contains("GreetingLbl: Label 'It''s a test';", tw.ToString());
    }

    [Fact]
    public void NeutralizesLineBreaksInLabelValue()
    {
        var variable = new ALVariable("GreetingLbl", new CodeType { Name = "Label" }, "Default", "line1\r\nline2");
        variable.Write(writer, conventions);
        var result = tw.ToString();
        // Line breaks would otherwise break out of the AL literal; they are replaced with spaces.
        Assert.DoesNotContain("\r", result.TrimEnd('\r', '\n'));
        Assert.DoesNotContain("line1\n", result);
        Assert.Contains("GreetingLbl: Label 'line1  line2';", result);
    }
}
