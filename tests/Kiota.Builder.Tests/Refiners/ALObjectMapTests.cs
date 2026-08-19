using System.Collections.Generic;
using System.IO;

using Kiota.Builder.Refiners;

using Xunit;

namespace Kiota.Builder.Tests.Refiners;

public class ALObjectMapTests
{
    [Fact]
    public void LoadFromDiskReturnsEmptyMapWhenFileMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), System.Guid.NewGuid().ToString("N") + ".json");
        var map = ALObjectMap.LoadFromDisk(path);
        Assert.Empty(map.Objects);
    }

    [Fact]
    public void LoadFromDiskReturnsEmptyMapWhenFileIsMalformed()
    {
        var path = Path.Combine(Path.GetTempPath(), System.Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, "{ not valid json");
        try
        {
            var map = ALObjectMap.LoadFromDisk(path);
            Assert.Empty(map.Objects);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SaveThenLoadRoundTripsEntries()
    {
        var path = Path.Combine(Path.GetTempPath(), System.Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var map = new ALObjectMap();
            map.Upsert("Fps.Kiota.Client::Pet", 50000, "codeunit", "Pet");
            map.Upsert("Fps.Kiota.Client::PetStatus", 50001, "enum", "PetStatus");

            map.SaveToDisk(path);
            var reloaded = ALObjectMap.LoadFromDisk(path);

            Assert.Equal(2, reloaded.Objects.Count);
            Assert.Equal(50000, reloaded.Objects["Fps.Kiota.Client::Pet"].ObjectId);
            Assert.Equal("codeunit", reloaded.Objects["Fps.Kiota.Client::Pet"].ObjectType);
            Assert.Equal("Pet", reloaded.Objects["Fps.Kiota.Client::Pet"].AssignedName);
            Assert.False(reloaded.Objects["Fps.Kiota.Client::Pet"].Tombstoned);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SaveToDiskCreatesMissingDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "map.json");
        try
        {
            var map = new ALObjectMap();
            map.Upsert("Ns::Widget", 50000, "codeunit", "Widget");
            map.SaveToDisk(path);

            Assert.True(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void MarkTombstonesExceptKeepsSeenEntriesActiveAndTombstonesTheRest()
    {
        var map = new ALObjectMap();
        map.Upsert("Ns::Seen", 50000, "codeunit", "Seen");
        map.Upsert("Ns::Removed", 50001, "codeunit", "Removed");

        map.MarkTombstonesExcept(new HashSet<string>(System.StringComparer.Ordinal) { "Ns::Seen" });

        Assert.False(map.Objects["Ns::Seen"].Tombstoned);
        Assert.True(map.Objects["Ns::Removed"].Tombstoned);
        // The tombstoned entry's id/name must be preserved, never freed/changed.
        Assert.Equal(50001, map.Objects["Ns::Removed"].ObjectId);
        Assert.Equal("Removed", map.Objects["Ns::Removed"].AssignedName);
    }

    private static ALObjectMap CreateMapWithEntry(string key, int objectId = 1, string assignedName = "Foo", bool tombstoned = false)
    {
        var map = new ALObjectMap();
        map.Upsert(key, objectId, "codeunit", assignedName);
        if (tombstoned)
            map.Objects[key].Tombstoned = true;
        return map;
    }

    [Fact]
    public void TryAdoptLegacyKeyMatchesTheSingleContentSuffixedCandidate()
    {
        var map = CreateMapWithEntry("ApiSdk.models::Alpha::foo", 123, "Alpha");

        var adopted = map.TryAdoptLegacyKey("ApiSdk.models::Alpha", null, out var legacyKey);

        Assert.True(adopted);
        Assert.Equal("ApiSdk.models::Alpha::foo", legacyKey);
    }

    [Fact]
    public void TryAdoptLegacyKeyIgnoresParameterCodeunitKeysWhenResolvingItsRequestBuilder()
    {
        // The param-codeunit's legacy key nests an additional "::{Method}Parameters" segment on top of
        // the request-builder's own disambiguator - its remainder therefore contains a further "::"
        // and must never match the request-builder's own primary key lookup.
        var map = new ALObjectMap();
        map.Upsert("ApiSdk.users::usersRequestBuilder::pathParameters,requestAdapter,urlTemplate", 1, "codeunit", "usersRequestBuilder");
        map.Upsert("ApiSdk.users::usersRequestBuilder::pathParameters,requestAdapter,urlTemplate::GetParameters", 2, "codeunit", "usersRequestBuilderGetParams");

        var adopted = map.TryAdoptLegacyKey("ApiSdk.users::usersRequestBuilder", null, out var legacyKey);

        Assert.True(adopted);
        Assert.Equal("ApiSdk.users::usersRequestBuilder::pathParameters,requestAdapter,urlTemplate", legacyKey);
    }

    [Fact]
    public void TryAdoptLegacyKeyRequiresAnExactDisambiguatorMatchWhenSeveralCandidatesExist()
    {
        var map = new ALObjectMap();
        map.Upsert("ApiSdk.models::Alpha::foo", 1, "codeunit", "Alpha");
        map.Upsert("ApiSdk.models::Alpha::bar", 2, "codeunit", "Alpha2");

        var adoptedForFoo = map.TryAdoptLegacyKey("ApiSdk.models::Alpha", "foo", out var legacyKeyForFoo);
        Assert.True(adoptedForFoo);
        Assert.Equal("ApiSdk.models::Alpha::foo", legacyKeyForFoo);

        var map2 = new ALObjectMap();
        map2.Upsert("ApiSdk.models::Alpha::foo", 1, "codeunit", "Alpha");
        map2.Upsert("ApiSdk.models::Alpha::bar", 2, "codeunit", "Alpha2");
        var adoptedForBaz = map2.TryAdoptLegacyKey("ApiSdk.models::Alpha", "baz", out _);
        Assert.False(adoptedForBaz);
    }

    [Fact]
    public void TryAdoptLegacyKeyAdoptsEachLegacyEntryAtMostOnce()
    {
        var map = CreateMapWithEntry("ApiSdk.models::Alpha::foo", 123, "Alpha");

        var firstAdoption = map.TryAdoptLegacyKey("ApiSdk.models::Alpha", null, out var legacyKey1);
        Assert.True(firstAdoption);
        Assert.Equal("ApiSdk.models::Alpha::foo", legacyKey1);

        // A second, unrelated attempt to resolve the SAME primary key must not re-adopt the entry
        // (it was already claimed) - simulates a corrupt/stale map, or a second lookup for the same
        // element by mistake.
        var secondAdoption = map.TryAdoptLegacyKey("ApiSdk.models::Alpha", null, out _);
        Assert.False(secondAdoption);
    }

    [Fact]
    public void TryAdoptLegacyKeySkipsTombstonedEntries()
    {
        var map = CreateMapWithEntry("ApiSdk.models::Alpha::foo", 123, "Alpha", tombstoned: true);

        var adopted = map.TryAdoptLegacyKey("ApiSdk.models::Alpha", null, out _);

        Assert.False(adopted);
    }

    [Fact]
    public void TryAdoptLegacyKeyIsDisabledOnceFormatVersionIsTwo()
    {
        var map = CreateMapWithEntry("ApiSdk.models::Alpha::foo", 123, "Alpha");
        map.FormatVersion = 2;

        var adopted = map.TryAdoptLegacyKey("ApiSdk.models::Alpha", null, out _);

        Assert.False(adopted);
    }

    [Fact]
    public void TryAdoptExactLegacyKeyMatchesAnExactParameterCodeunitKey()
    {
        var map = CreateMapWithEntry("ApiSdk.users::usersRequestBuilder::pathParameters,requestAdapter,urlTemplate::GetParameters", 60002, "usersRequestBuilderGetParams");

        var adopted = map.TryAdoptExactLegacyKey("ApiSdk.users::usersRequestBuilder::pathParameters,requestAdapter,urlTemplate::GetParameters");

        Assert.True(adopted);
    }

    [Fact]
    public void TryAdoptExactLegacyKeyDoesNotMatchAPrefixOnlyCandidate()
    {
        // Unlike TryAdoptLegacyKey, this is an exact lookup - a key that only shares a PREFIX must
        // not be adopted.
        var map = CreateMapWithEntry("ApiSdk.users::usersRequestBuilder::pathParameters,requestAdapter,urlTemplate::GetParameters", 60002, "usersRequestBuilderGetParams");

        var adopted = map.TryAdoptExactLegacyKey("ApiSdk.users::usersRequestBuilder::pathParameters,requestAdapter,urlTemplate");

        Assert.False(adopted);
    }

    [Fact]
    public void TryAdoptExactLegacyKeyCanOnlyBeClaimedOnce()
    {
        var map = CreateMapWithEntry("ApiSdk.users::usersRequestBuilder::pathParameters,requestAdapter,urlTemplate::GetParameters", 60002, "usersRequestBuilderGetParams");

        Assert.True(map.TryAdoptExactLegacyKey("ApiSdk.users::usersRequestBuilder::pathParameters,requestAdapter,urlTemplate::GetParameters"));
        Assert.False(map.TryAdoptExactLegacyKey("ApiSdk.users::usersRequestBuilder::pathParameters,requestAdapter,urlTemplate::GetParameters"));
    }

    [Fact]
    public void TryAdoptExactLegacyKeyIsDisabledOnceFormatVersionIsTwo()
    {
        var map = CreateMapWithEntry("ApiSdk.users::usersRequestBuilder::pathParameters,requestAdapter,urlTemplate::GetParameters", 60002, "usersRequestBuilderGetParams");
        map.FormatVersion = 2;

        Assert.False(map.TryAdoptExactLegacyKey("ApiSdk.users::usersRequestBuilder::pathParameters,requestAdapter,urlTemplate::GetParameters"));
    }

    [Fact]
    public void RekeyMovesTheEntryAndLeavesNoStaleKey()
    {
        var map = CreateMapWithEntry("ApiSdk.models::Alpha::foo", 123, "Alpha");

        map.Rekey("ApiSdk.models::Alpha::foo", "ApiSdk.models::Alpha");

        Assert.False(map.Objects.ContainsKey("ApiSdk.models::Alpha::foo"));
        Assert.True(map.Objects.ContainsKey("ApiSdk.models::Alpha"));
        var entry = map.Objects["ApiSdk.models::Alpha"];
        Assert.Equal(123, entry.ObjectId);
        Assert.Equal("Alpha", entry.AssignedName);
        Assert.False(entry.Tombstoned);
        Assert.Single(map.Objects);
    }

    [Fact]
    public void RekeyDoesNotOverwriteAnExistingEntryAtTheNewKey()
    {
        var map = new ALObjectMap();
        map.Upsert("ApiSdk.models::Alpha::foo", 1, "codeunit", "AlphaOld");
        map.Upsert("ApiSdk.models::Alpha", 2, "codeunit", "AlphaNew");

        map.Rekey("ApiSdk.models::Alpha::foo", "ApiSdk.models::Alpha");

        // Both entries survive unchanged - Rekey refuses to clobber a live entry at the destination key.
        Assert.Equal(2, map.Objects.Count);
        Assert.Equal(1, map.Objects["ApiSdk.models::Alpha::foo"].ObjectId);
        Assert.Equal(2, map.Objects["ApiSdk.models::Alpha"].ObjectId);
    }
}
