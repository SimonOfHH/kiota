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
}
