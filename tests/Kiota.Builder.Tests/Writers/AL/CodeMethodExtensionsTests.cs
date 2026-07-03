using System.Collections.Generic;
using System.Linq;

using Kiota.Builder.CodeDOM;
using Kiota.Builder.Writers.AL;

using Xunit;

namespace Kiota.Builder.Tests.Writers.AL;

public class CodeMethodExtensionsTests
{
    [Fact]
    public void SingularizesPluralName()
    {
        var result = CodeMethodExtensions.GetSingularName("Orders", Enumerable.Empty<CodeParameter>());
        Assert.Equal("Order", result);
    }

    [Fact]
    public void DeduplicatesAgainstExistingParameterNames()
    {
        var existing = new List<CodeParameter>
        {
            new() { Name = "Order", Type = new CodeType { Name = "string" } },
        };
        var result = CodeMethodExtensions.GetSingularName("Orders", existing);
        Assert.Equal("Order2", result);
    }

    [Fact]
    public void DeduplicationIsCaseInsensitive()
    {
        var existing = new List<CodeParameter>
        {
            new() { Name = "order", Type = new CodeType { Name = "string" } },
        };
        var result = CodeMethodExtensions.GetSingularName("Orders", existing);
        Assert.Equal("Order2", result);
    }

    [Fact]
    public void OrdersSingularCodeunitVariablesBeforeListOfCodeunitVariables()
    {
        var root = CodeNamespace.InitRootNamespace();
        var ns = root.AddNamespace("Vendor.Api");
        var modelClass = ns.AddClass(new CodeClass { Name = "SomeModel", Kind = CodeClassKind.Model }).First();
        var method = new CodeMethod { Name = "DoThing", Kind = CodeMethodKind.Custom, ReturnType = new CodeType { Name = "void" } };

        // Declared in the "wrong" order: a List of [Codeunit "SomeModel"] variable first, then a
        // singular Codeunit variable - AA0021 requires the singular Codeunit variable to come first,
        // since "List of [...]" is not one of the linter's ordered keywords.
        var listOfCodeunitParam = new CodeParameter
        {
            Name = "Items",
            Kind = CodeParameterKind.Custom,
            Type = new CodeType { Name = "SomeModel", TypeDefinition = modelClass, CollectionKind = CodeTypeBase.CodeTypeCollectionKind.Complex },
        };
        listOfCodeunitParam.SetFlag(ALCustomDataKeys.LocalVariable);
        method.AddParameter(listOfCodeunitParam);

        var codeunitParam = new CodeParameter
        {
            Name = "Item",
            Kind = CodeParameterKind.Custom,
            Type = new CodeType { Name = "SomeModel", TypeDefinition = modelClass },
        };
        codeunitParam.SetFlag(ALCustomDataKeys.LocalVariable);
        method.AddParameter(codeunitParam);

        var result = method.Variables().ToList();

        Assert.Equal(2, result.Count);
        Assert.Equal("Item", result[0].Name);
        Assert.Equal("Items", result[1].Name);
    }
}
