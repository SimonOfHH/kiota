using Kiota.Builder.Writers.AL;

using System;

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

    [Fact]
    public void ThrowsWhenObjectIdExceedsEndRange()
    {
        var provider = new ALObjectIdProvider(50000, 50001);
        Assert.Equal(50000, provider.GetNextCodeunitId());
        Assert.Equal(50001, provider.GetNextCodeunitId());
        Assert.Throws<InvalidOperationException>(() => provider.GetNextCodeunitId());
    }

    [Fact]
    public void ThrowsWhenEndRangeIsBelowStartRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ALObjectIdProvider(50000, 49999));
    }

    [Fact]
    public void DoesNotConsumeIdWhenRangeIsExhausted()
    {
        var provider = new ALObjectIdProvider(50000, 50000);
        Assert.Equal(50000, provider.GetNextCodeunitId());
        // The counter must not advance after a failed allocation, so each retry reports the same overflow.
        Assert.Throws<InvalidOperationException>(() => provider.GetNextCodeunitId());
        Assert.Throws<InvalidOperationException>(() => provider.GetNextCodeunitId());
    }
}
