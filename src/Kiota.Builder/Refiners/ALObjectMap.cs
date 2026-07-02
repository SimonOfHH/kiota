using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kiota.Builder.Refiners;

/// <summary>
/// A single previously-assigned AL object, keyed by a stable, spec-derived identity string
/// (see <see cref="ALObjectMap"/>).
/// </summary>
public class ALObjectMapEntry
{
    [JsonPropertyName("objectId")]
    public int ObjectId
    {
        get; set;
    }

    [JsonPropertyName("objectType")]
    public string ObjectType { get; set; } = string.Empty;

    /// <summary>The final, emitted AL name (post prefix/suffix/dedup). Reused verbatim on a cache
    /// hit - never re-sanitized/re-deduplicated or re-concatenated with objectPrefix/objectSuffix.</summary>
    [JsonPropertyName("assignedName")]
    public string AssignedName { get; set; } = string.Empty;

    /// <summary>
    /// True when this object was not seen in the most recent generation (its source schema/path was
    /// removed from the spec). Tombstoned entries permanently keep their <see cref="ObjectId"/> and
    /// <see cref="AssignedName"/> - both stay reserved forever and are never reused by a different
    /// object (AppSource breaking-change / obsoletion rules).
    /// </summary>
    [JsonPropertyName("tombstoned")]
    public bool Tombstoned
    {
        get; set;
    }
}

/// <summary>
/// Persisted map from a stable, spec-derived object key to the AL object id/name previously
/// assigned to it, used to keep AL object ids and names stable across regenerations of an evolving
/// spec (not just an unchanged one - see <see cref="Refiners.ALRefiner"/> Phase 1 ordering fix for
/// that narrower guarantee). Opt-in via <see cref="ALConfiguration.ObjectMapPath"/>; fully inert
/// (no file I/O) when that path is not configured.
/// <para>
/// Keys are built as <c>"{Namespace.Name}::{OriginalName}"</c> (see
/// <c>ALRefiner.BuildObjectMapKey</c>) - i.e. derived from the OpenAPI schema/path naming that
/// Kiota-core already produces deterministically, not from CodeDOM traversal order.
/// </para>
/// </summary>
public class ALObjectMap
{
    private static readonly JsonSerializerOptions s_readOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
    private static readonly JsonSerializerOptions s_writeOptions = new() { WriteIndented = true };

    [JsonPropertyName("objects")]
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, ALObjectMapEntry> Objects { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Loads a map from disk. A missing file yields an empty map (first generation with the feature
    /// enabled); malformed JSON also yields an empty map rather than throwing, so a corrupted map
    /// degrades to "start fresh" instead of failing generation outright.
    /// </summary>
    public static ALObjectMap LoadFromDisk(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!File.Exists(path))
            return new ALObjectMap();
        try
        {
            var json = File.ReadAllText(path);
#pragma warning disable IL2026 // Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access
            return JsonSerializer.Deserialize<ALObjectMap>(json, s_readOptions) ?? new ALObjectMap();
#pragma warning restore IL2026
        }
        catch (JsonException)
        {
            return new ALObjectMap();
        }
    }

    /// <summary>Serializes and writes this map to <paramref name="path"/>, creating the containing
    /// directory if necessary.</summary>
    public void SaveToDisk(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);
#pragma warning disable IL2026 // Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access
        var json = JsonSerializer.Serialize(this, s_writeOptions);
#pragma warning restore IL2026
        File.WriteAllText(path, json);
    }

    /// <summary>Inserts or overwrites the (active, non-tombstoned) entry for <paramref name="key"/>.</summary>
    public void Upsert(string key, int objectId, string objectType, string assignedName)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        Objects[key] = new ALObjectMapEntry
        {
            ObjectId = objectId,
            ObjectType = objectType,
            AssignedName = assignedName,
            Tombstoned = false,
        };
    }

    /// <summary>
    /// Marks every entry not present in <paramref name="seenKeys"/> as tombstoned, and every entry
    /// present in <paramref name="seenKeys"/> as active. Never removes entries - tombstoned objects
    /// keep their id/name reserved forever.
    /// </summary>
    public void MarkTombstonesExcept(IReadOnlySet<string> seenKeys)
    {
        ArgumentNullException.ThrowIfNull(seenKeys);
        foreach (var entry in Objects)
            entry.Value.Tombstoned = !seenKeys.Contains(entry.Key);
    }
}
