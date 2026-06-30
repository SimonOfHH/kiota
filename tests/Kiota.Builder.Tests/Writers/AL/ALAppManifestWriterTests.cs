using System;
using System.IO;
using System.Linq;

using Kiota.Builder.CodeDOM;
using Kiota.Builder.Writers;

using Xunit;

namespace Kiota.Builder.Tests.Writers.AL;

public sealed class ALAppManifestWriterTests : IDisposable
{
    private const string DefaultPath = "./";
    private const string DefaultName = "name";
    private readonly StringWriter tw;
    private readonly LanguageWriter writer;
    private readonly CodeNamespace root;

    public ALAppManifestWriterTests()
    {
        writer = LanguageWriter.GetLanguageWriter(GenerationLanguage.AL, DefaultPath, DefaultName);
        tw = new StringWriter();
        writer.SetTextWriter(tw);
        root = CodeNamespace.InitRootNamespace();
    }

    public void Dispose()
    {
        tw?.Dispose();
        GC.SuppressFinalize(this);
    }

    private CodeFunction CreateConfigFunction(string functionName, params (string Key, string Value)[] config)
    {
        var holder = new CodeClass { Name = "_Holder" };
        root.AddClass(holder);
        var method = new CodeMethod
        {
            Name = functionName,
            Kind = CodeMethodKind.Custom,
            IsStatic = true,
            ReturnType = new CodeType { Name = "void", IsExternal = true },
        };
        holder.AddMethod(method);
        var function = new CodeFunction(method);
        foreach (var (key, value) in config)
            function.AddUsing(new CodeUsing { Name = $"{key}={value}" });
        return function;
    }

    [Fact]
    public void WritesAppJsonWithProvidedValues()
    {
        var function = CreateConfigFunction("AppJson",
            ("Publisher", "Acme Corp"),
            ("Version", "1.2.3.4"),
            ("IDRangeStart", "60000"),
            ("IDRangeEnd", "70000"));
        writer.Write(function);
        var result = tw.ToString();
        Assert.Contains("\"publisher\": \"Acme Corp\"", result);
        Assert.Contains("\"version\": \"1.2.3.4\"", result);
        Assert.Contains("\"from\": 60000", result);
        Assert.Contains("\"to\": 70000", result);
    }

    [Fact]
    public void PreservesUppercaseEulaProperty()
    {
        var function = CreateConfigFunction("AppJson", ("EulaUrl", "https://example.com/eula"));
        writer.Write(function);
        var result = tw.ToString();
        // The AL compiler requires the EULA property to remain uppercase.
        Assert.Contains("\"EULA\":", result);
    }

    [Fact]
    public void UsesDefaultsForMissingConfigValues()
    {
        var function = CreateConfigFunction("AppJson");
        writer.Write(function);
        var result = tw.ToString();
        Assert.Contains("\"platform\": \"27.0.0.0\"", result);
        Assert.Contains("\"runtime\": \"16.0\"", result);
        Assert.Contains("c24a2609-e5c2-4702-b734-db13e5a6594c", result);
    }

    [Fact]
    public void WritesReadmeMarkdown()
    {
        var function = CreateConfigFunction("Readme",
            ("Language", "AL"),
            ("ClientClassName", "PetClient"));
        writer.Write(function);
        var result = tw.ToString();
        Assert.Contains("# Auto-Generated AL Client", result);
        Assert.Contains("| Language | `AL` |", result);
        Assert.Contains("| Client class name | `PetClient` |", result);
    }

    [Fact]
    public void IgnoresUnknownFunctionNames()
    {
        var function = CreateConfigFunction("SomethingElse", ("Publisher", "Acme"));
        writer.Write(function);
        Assert.Empty(tw.ToString());
    }
}
