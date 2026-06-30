using System;
using System.IO;
using System.Linq;

using Kiota.Builder.CodeDOM;
using Kiota.Builder.Writers;

using Xunit;

namespace Kiota.Builder.Tests.Writers.AL;

public sealed class CodePropertyWriterTests : IDisposable
{
    private const string DefaultPath = "./";
    private const string DefaultName = "name";
    private readonly StringWriter tw;
    private readonly LanguageWriter writer;
    private readonly CodeClass parentClass;

    public CodePropertyWriterTests()
    {
        writer = LanguageWriter.GetLanguageWriter(GenerationLanguage.AL, DefaultPath, DefaultName);
        tw = new StringWriter();
        writer.SetTextWriter(tw);
        var root = CodeNamespace.InitRootNamespace();
        var ns = root.AddNamespace("Vendor.Api");
        parentClass = ns.AddClass(new CodeClass { Name = "SomeClient", Kind = CodeClassKind.Custom }).First();
    }

    public void Dispose()
    {
        tw?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void WritesNothingForCustomProperty()
    {
        // AL has no member fields; properties are converted to methods by the refiner,
        // so the property writer itself emits nothing.
        var property = parentClass.AddProperty(new CodeProperty
        {
            Name = "dummy",
            Type = new CodeType { Name = "string" },
            Kind = CodePropertyKind.Custom,
        }).First();
        writer.Write(property);
        Assert.Empty(tw.ToString());
    }

    [Fact]
    public void WritesNothingForGlobalVariableProperty()
    {
        var property = parentClass.AddProperty(new CodeProperty
        {
            Name = "global",
            Type = new CodeType { Name = "string" },
            Kind = CodePropertyKind.Custom,
        }).First();
        property.CustomData["global-variable"] = "true";
        writer.Write(property);
        Assert.Empty(tw.ToString());
    }
}
