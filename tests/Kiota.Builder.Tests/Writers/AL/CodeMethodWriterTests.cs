using System;
using System.IO;
using System.Linq;

using Kiota.Builder.CodeDOM;
using Kiota.Builder.Writers;

using Xunit;

namespace Kiota.Builder.Tests.Writers.AL;

public sealed class CodeMethodWriterTests : IDisposable
{
    private const string DefaultPath = "./";
    private const string DefaultName = "name";
    private readonly StringWriter tw;
    private readonly LanguageWriter writer;
    private readonly CodeClass parentClass;

    public CodeMethodWriterTests()
    {
        writer = LanguageWriter.GetLanguageWriter(GenerationLanguage.AL, DefaultPath, DefaultName);
        tw = new StringWriter();
        writer.SetTextWriter(tw);
        var root = CodeNamespace.InitRootNamespace();
        var ns = root.AddNamespace("Vendor.Api");
        parentClass = ns.AddClass(new CodeClass
        {
            Name = "SomeClient",
            Kind = CodeClassKind.Custom,
        }).First();
    }

    public void Dispose()
    {
        tw?.Dispose();
        GC.SuppressFinalize(this);
    }

    private CodeMethod AddMethod(AccessModifier access = AccessModifier.Public, string name = "DoThing")
    {
        return parentClass.AddMethod(new CodeMethod
        {
            Name = name,
            SimpleName = name,
            Kind = CodeMethodKind.Custom,
            Access = access,
            ReturnType = new CodeType { Name = "void" },
        }).First();
    }

    [Fact]
    public void WritesPublicProcedurePrototypeAndBlock()
    {
        AddMethod();
        writer.Write(parentClass.Methods.First());
        var result = tw.ToString();
        Assert.Contains("procedure DoThing()", result);
        Assert.Contains("begin", result);
        Assert.Contains("end;", result);
        Assert.DoesNotContain("local procedure", result);
        Assert.DoesNotContain("internal procedure", result);
    }

    [Fact]
    public void WritesLocalProcedureForPrivateAccess()
    {
        AddMethod(AccessModifier.Private);
        writer.Write(parentClass.Methods.First());
        var result = tw.ToString();
        Assert.Contains("local procedure DoThing()", result);
    }

    [Fact]
    public void WritesInternalProcedureForProtectedAccess()
    {
        AddMethod(AccessModifier.Protected);
        writer.Write(parentClass.Methods.First());
        var result = tw.ToString();
        Assert.Contains("internal procedure DoThing()", result);
    }

    [Fact]
    public void WritesParameterWithTranslatedType()
    {
        var method = AddMethod();
        method.AddParameter(new CodeParameter { Name = "id", Type = new CodeType { Name = "string" } });
        writer.Write(parentClass.Methods.First());
        var result = tw.ToString();
        Assert.Contains("procedure DoThing(id: Text)", result);
    }

    [Fact]
    public void WritesByRefParameterWithVarKeyword()
    {
        var method = AddMethod();
        var param = new CodeParameter { Name = "id", Type = new CodeType { Name = "string" } };
        param.CustomData["by-ref"] = "true";
        method.AddParameter(param);
        writer.Write(parentClass.Methods.First());
        var result = tw.ToString();
        Assert.Contains("var id: Text", result);
    }

    [Fact]
    public void WritesReturnClause()
    {
        var method = AddMethod();
        method.ReturnType = new CodeType { Name = "string" };
        writer.Write(parentClass.Methods.First());
        var result = tw.ToString();
        Assert.Contains("procedure DoThing(): Text", result);
    }

    [Fact]
    public void WritesNamedReturnVariable()
    {
        var method = AddMethod();
        method.ReturnType = new CodeType { Name = "string" };
        method.CustomData["return-variable-name"] = "result";
        writer.Write(parentClass.Methods.First());
        var result = tw.ToString();
        Assert.Contains("procedure DoThing() result: Text", result);
    }

    [Fact]
    public void SkipsMethodMarkedWithSkipCustomData()
    {
        var method = AddMethod();
        method.CustomData["skip"] = "true";
        writer.Write(parentClass.Methods.First());
        var result = tw.ToString();
        Assert.Empty(result);
    }

    [Fact]
    public void WritesTodoForUnsourcedCustomMethod()
    {
        AddMethod();
        writer.Write(parentClass.Methods.First());
        var result = tw.ToString();
        Assert.Contains("// TODO: Implement custom method body", result);
    }

    [Fact]
    public void DispatchesResponseGetterBodyFromSourceCustomData()
    {
        var method = AddMethod(name: "GetResponse");
        method.CustomData["source"] = "response-getter";
        writer.Write(parentClass.Methods.First());
        var result = tw.ToString();
        Assert.Contains("exit(StoredResponse);", result);
    }
}
