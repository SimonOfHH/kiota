using Kiota.Builder.Writers.AL;

using Xunit;

namespace Kiota.Builder.Tests.Writers.AL;

public class ALObjectIdProviderTests
{
    [Fact]
    public void StartsAtConfiguredRangeStart()
    {
        var provider = new ALObjectIdProvider(60000);
        Assert.Equal(60000, provider.GetNextObjectId("codeunit"));
    }

    [Fact]
    public void IncrementsPerObjectType()
    {
        var provider = new ALObjectIdProvider(50000);
        Assert.Equal(50000, provider.GetNextCodeunitId());
        Assert.Equal(50001, provider.GetNextCodeunitId());
        Assert.Equal(50002, provider.GetNextCodeunitId());
    }

    [Fact]
    public void TracksCountersIndependentlyPerType()
    {
        var provider = new ALObjectIdProvider(50000);
        Assert.Equal(50000, provider.GetNextCodeunitId());
        Assert.Equal(50000, provider.GetNextEnumId());
        Assert.Equal(50001, provider.GetNextCodeunitId());
        Assert.Equal(50001, provider.GetNextEnumId());
    }

    [Fact]
    public void HandlesUnknownObjectTypeStartingAtRangeStart()
    {
        var provider = new ALObjectIdProvider(70000);
        Assert.Equal(70000, provider.GetNextObjectId("page"));
        Assert.Equal(70001, provider.GetNextObjectId("page"));
    }

    [Fact]
    public void DefaultsToFiftyThousandWhenNoRangeProvided()
    {
        var provider = new ALObjectIdProvider();
        Assert.Equal(50000, provider.GetNextCodeunitId());
    }
}
