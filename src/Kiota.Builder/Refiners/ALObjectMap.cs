using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
/// Keys are built as <c>"{Namespace.Name}::{OriginalName}"</c>, with a content disambiguator suffix
/// (<c>"::{sorted,comma,joined,member,names}"</c>) appended ONLY when two distinct top-level
/// classes/enums share that identity in the same generation run (see
/// <c>ALRefiner.BuildObjectMapKey</c>). Param-codeunit keys use a
/// <c>"::params::{MethodName}Parameters"</c> suffix, which can never collide with a content
/// disambiguator (disambiguators are plain comma-joined identifiers and never contain <c>"::"</c>).
/// </para>
/// <para>
/// <b>Legacy key format (formatVersion &lt; 2):</b> earlier versions of this feature always appended
/// the content disambiguator, so every entry's key ends in
/// <c>"::{sorted,comma,joined,member,names}"</c> even for objects with a globally-unique identity.
/// Consequently, evolving a schema (adding/removing a property or enum option) changed that object's
/// key, causing a spurious id/name reassignment even though the object itself did not change identity.
/// <see cref="TryAdoptLegacyKey"/> resolves a miss on the new, identity-only key against exactly one
/// matching legacy entry so the caller can re-key it (via <see cref="Rekey"/>) instead of minting a
/// new id/name. This one-time migration is disabled once <see cref="FormatVersion"/> reaches 2 (the
/// value this class always writes), so the prefix-scan cost is paid only once per map.
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

    /// <summary>
    /// Schema version of this map's key format. Absent (deserializes as 0) or 1 means every key was
    /// built with the always-on content disambiguator (see class remarks); 2 means keys are
    /// identity-only unless a genuine name collision required disambiguation, and legacy-key adoption
    /// via <see cref="TryAdoptLegacyKey"/> is permanently disabled for this map. Always written as 2
    /// by <see cref="SaveToDisk"/>-calling code once the migration pass has run (see
    /// <c>ALRefiner.RefineAsync</c>).
    /// </summary>
    [JsonPropertyName("formatVersion")]
    public int FormatVersion
    {
        get; set;
    }

    [JsonPropertyName("objects")]
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Dictionary<string, ALObjectMapEntry> Objects { get; } = new(StringComparer.Ordinal);

    /// <summary>Legacy keys already handed out via <see cref="TryAdoptLegacyKey"/> during this run -
    /// each legacy entry may be adopted by at most one new-format key, guarding against a corrupt/
    /// stale map where two different legacy entries could otherwise both match the same prefix.</summary>
    private readonly HashSet<string> _adoptedLegacyKeys = new(StringComparer.Ordinal);

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

    /// <summary>
    /// One-time migration lookup for an EXACT legacy key, used only for param-codeunit keys (see
    /// <c>ALRefiner.UpdateRequestExecutorMethods</c>): unlike <see cref="TryAdoptLegacyKey"/>, no
    /// prefix scan is performed - the caller already knows the exact old-format key it expects (the
    /// parent request-builder's own resolved legacy/primary key plus
    /// <c>"::{MethodName}Parameters"</c>, with no <c>"::params::"</c> marker). Returns
    /// <see langword="false"/> when <see cref="FormatVersion"/> is 2 or greater, when no such entry
    /// exists, when it is tombstoned, or when it was already adopted this run.
    /// </summary>
    public bool TryAdoptExactLegacyKey(string legacyKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(legacyKey);
        if (FormatVersion >= 2)
            return false;
        if (!Objects.TryGetValue(legacyKey, out var entry) || entry.Tombstoned)
            return false;
        return _adoptedLegacyKeys.Add(legacyKey);
    }

    /// <summary>
    /// One-time migration lookup: resolves a miss on the new-format <paramref name="primaryKey"/>
    /// (identity-only, or identity + disambiguator - see class remarks) against this map's legacy
    /// (formatVersion &lt; 2) entries, so the caller can <see cref="Rekey"/> the match instead of
    /// minting a brand-new id/name for an object that only looks new because the key format changed.
    /// Always returns <see langword="false"/> once <see cref="FormatVersion"/> is 2 or greater.
    /// <para>
    /// A candidate legacy entry's key must start with <c>"{primaryKey}::"</c> AND its remainder must
    /// contain no further <c>"::"</c> - this excludes a param-codeunit's legacy key (which nests an
    /// additional <c>"::{MethodName}Parameters"</c> segment) from matching its own parent request-
    /// builder's primary key. Must be non-tombstoned and not already adopted this run.
    /// </para>
    /// <para>
    /// Exactly one candidate is adopted unconditionally. With multiple candidates (a stale/corrupt map
    /// that predates the 2026-07-03 same-name-different-shape disambiguation fix, or a genuine
    /// same-run collision), only the candidate whose remainder exactly equals
    /// <paramref name="currentDisambiguator"/> is adopted; otherwise no adoption occurs and the caller
    /// falls through to a fresh mint.
    /// </para>
    /// </summary>
    public bool TryAdoptLegacyKey(string primaryKey, string? currentDisambiguator, out string legacyKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(primaryKey);
        legacyKey = string.Empty;
        if (FormatVersion >= 2)
            return false;

        var prefix = primaryKey + "::";
        List<string>? candidates = null;
        foreach (var (key, entry) in Objects)
        {
            if (entry.Tombstoned)
                continue;
            if (_adoptedLegacyKeys.Contains(key))
                continue;
            if (!key.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            var remainder = key[prefix.Length..];
            if (remainder.Contains("::", StringComparison.Ordinal))
                continue;
            (candidates ??= []).Add(key);
        }
        if (candidates is null || candidates.Count == 0)
            return false;

        string? chosen = candidates.Count == 1
            ? candidates[0]
            : (!string.IsNullOrEmpty(currentDisambiguator)
                ? candidates.FirstOrDefault(k => string.Equals(k[prefix.Length..], currentDisambiguator, StringComparison.Ordinal))
                : null);
        if (chosen is null)
            return false;

        legacyKey = chosen;
        _adoptedLegacyKeys.Add(chosen);
        return true;
    }

    /// <summary>
    /// Moves the entry at <paramref name="oldKey"/> to <paramref name="newKey"/>, preserving its id/
    /// name/type/tombstoned state exactly. No-op if <paramref name="oldKey"/> has no entry, if the
    /// keys are identical, or if <paramref name="newKey"/> already has an entry (never silently
    /// overwrites a live object's record).
    /// </summary>
    public void Rekey(string oldKey, string newKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(oldKey);
        ArgumentException.ThrowIfNullOrEmpty(newKey);
        if (string.Equals(oldKey, newKey, StringComparison.Ordinal))
            return;
        if (Objects.ContainsKey(newKey))
            return;
        if (!Objects.Remove(oldKey, out var entry))
            return;
        Objects[newKey] = entry;
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
