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
}
