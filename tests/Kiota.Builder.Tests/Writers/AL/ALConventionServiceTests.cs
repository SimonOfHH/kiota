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

    [Fact]
    public void TryGetExistingNameMissesWhenNoMapWasSeeded()
    {
        var service = CreateService();
        Assert.False(service.TryGetExistingName("Ns::Widget", out _));
    }

    [Fact]
    public void SeedFromMapMakesExistingActiveEntryResolvable()
    {
        var map = new ALObjectMap();
        map.Upsert("Ns::Widget", 50000, "codeunit", "Widget");
        var service = CreateService();

        service.SeedFromMap(map);

        Assert.True(service.TryGetExistingName("Ns::Widget", out var name));
        Assert.Equal("Widget", name);
    }

    [Fact]
    public void SeedFromMapDoesNotResolveTombstonedEntries()
    {
        var map = new ALObjectMap();
        map.Upsert("Ns::Removed", 50000, "codeunit", "Removed");
        map.MarkTombstonesExcept(new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal));
        var service = CreateService();

        service.SeedFromMap(map);

        Assert.False(service.TryGetExistingName("Ns::Removed", out _));
    }

    [Fact]
    public void SeedFromMapBlocksNewObjectFromReusingATombstonedName()
    {
        var map = new ALObjectMap();
        map.Upsert("Ns::Removed", 50000, "codeunit", "Removed");
        map.MarkTombstonesExcept(new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal));
        var service = CreateService();
        service.SeedFromMap(map);

        // A brand-new, unrelated class happens to sanitize/abbreviate down to the exact same name
        // that a tombstoned object used to have - it must not be handed that name again.
        var newClass = new CodeClass { Name = "Removed" };
        var result = service.DeduplicateName("Removed", newClass);

        Assert.NotEqual("Removed", result);
    }
}
