using Kiota.Builder.Refiners;
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

    [Fact]
    public void TryGetExistingIdMissesWhenNoMapWasSeeded()
    {
        var provider = new ALObjectIdProvider(50000);
        Assert.False(provider.TryGetExistingId("Ns::Widget", out _));
    }

    [Fact]
    public void SeedFromMapMakesExistingActiveEntryResolvable()
    {
        var map = new ALObjectMap();
        map.Upsert("Ns::Widget", 50003, "codeunit", "Widget");
        var provider = new ALObjectIdProvider(50000);

        provider.SeedFromMap(map);

        Assert.True(provider.TryGetExistingId("Ns::Widget", out var id));
        Assert.Equal(50003, id);
    }

    [Fact]
    public void SeedFromMapDoesNotResolveTombstonedEntries()
    {
        var map = new ALObjectMap();
        map.Upsert("Ns::Removed", 50003, "codeunit", "Removed");
        map.MarkTombstonesExcept(new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal));
        var provider = new ALObjectIdProvider(50000);

        provider.SeedFromMap(map);

        Assert.False(provider.TryGetExistingId("Ns::Removed", out _));
    }

    [Fact]
    public void SeedFromMapReservesIdsFromBothActiveAndTombstonedEntriesSoNewIdsSkipThem()
    {
        var map = new ALObjectMap();
        map.Upsert("Ns::Active", 50000, "codeunit", "Active");
        map.Upsert("Ns::Removed", 50001, "codeunit", "Removed");
        map.MarkTombstonesExcept(new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal) { "Ns::Active" });
        var provider = new ALObjectIdProvider(50000);

        provider.SeedFromMap(map);

        // Both 50000 (active) and 50001 (tombstoned) are reserved, so the first newly-minted id
        // must skip over the gap and land on 50002.
        Assert.Equal(50002, provider.GetNextCodeunitId());
    }
}
