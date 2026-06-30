using Kiota.Builder.CodeDOM;
using Kiota.Builder.Refiners;
using Kiota.Builder.Writers.AL;

using Xunit;

namespace Kiota.Builder.Tests.Writers.AL;

public class ALConventionServiceTests
{
    private static ALConventionService CreateService() => new(new ALConfiguration());

    [Theory]
    [InlineData("string", "Text")]
    [InlineData("integer", "Integer")]
    [InlineData("boolean", "Boolean")]
    [InlineData("int64", "BigInteger")]
    [InlineData("float", "Decimal")]
    [InlineData("double", "Decimal")]
    [InlineData("decimal", "Decimal")]
    [InlineData("date", "Date")]
    [InlineData("dateonly", "Date")]
    [InlineData("time", "Time")]
    [InlineData("datetime", "DateTime")]
    [InlineData("datetimeoffset", "DateTime")]
    [InlineData("guid", "Guid")]
    [InlineData("timespan", "Duration")]
    [InlineData("untypednode", "Text")]
    public void TranslatesPrimitiveTypesToALTypes(string input, string expected)
    {
        var service = CreateService();
        Assert.Equal(expected, service.TranslateType(new CodeType { Name = input }));
    }

    [Fact]
    public void TranslatesVoidToEmptyString()
    {
        var service = CreateService();
        Assert.Equal(string.Empty, service.TranslateType(new CodeType { Name = "void" }));
    }

    [Theory]
    [InlineData(AccessModifier.Internal, "internal ")]
    [InlineData(AccessModifier.Public, "")]
    [InlineData(AccessModifier.Private, "local ")]
    public void MapsAccessModifiers(AccessModifier access, string expected)
    {
        var service = CreateService();
        Assert.Equal(expected, service.GetAccessModifier(access));
    }

    [Fact]
    public void WrapsCollectionTypesInListOf()
    {
        var service = CreateService();
        var target = new CodeClass { Name = "Owner" };
        var collection = new CodeType
        {
            Name = "string",
            CollectionKind = CodeTypeBase.CodeTypeCollectionKind.Array,
        };
        Assert.Equal("List of [Text]", service.GetTypeString(collection, target));
    }

    [Fact]
    public void ComposedTypesMapToJsonToken()
    {
        var service = CreateService();
        var target = new CodeClass { Name = "Owner" };
        var union = new CodeUnionType { Name = "union" };
        Assert.Equal("JsonToken", service.GetTypeString(union, target));
    }

    [Fact]
    public void ExposesALSpecificTypeOverrides()
    {
        var service = CreateService();
        Assert.Equal("HttpContent", service.StreamTypeName);
        Assert.Equal(string.Empty, service.VoidTypeName);
        Assert.Equal("QueryParameters", service.TempDictionaryVarName);
        Assert.Equal("JsonObject", service.ParseNodeInterfaceName);
    }
}
