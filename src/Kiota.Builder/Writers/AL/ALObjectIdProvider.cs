using System;
using System.Collections.Generic;

namespace Kiota.Builder.Writers.AL;

public class ALObjectIdProvider
{
    private readonly int _startRange;
    private readonly int _endRange;

    /// <summary>Next candidate id to try per object type. Absent == start scanning at <see cref="_startRange"/>.</summary>
    private readonly Dictionary<string, int> _nextCandidate = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ids that must not be minted again, per object type - seeded from a persisted
    /// <see cref="Kiota.Builder.Refiners.ALObjectMap"/> (both active and tombstoned entries) plus every id minted
    /// during this run.</summary>
    private readonly Dictionary<string, HashSet<int>> _reservedIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Stable object-map key -> previously-assigned id, for active (non-tombstoned) entries
    /// only. A tombstoned entry's id is reserved (never reused) but never handed back out via
    /// <see cref="TryGetExistingId"/> - the object it belonged to is gone.</summary>
    private readonly Dictionary<string, int> _existingIdsByKey = new(StringComparer.Ordinal);

    public ALObjectIdProvider(int startRange = 50000, int endRange = 99999)
    {
        if (endRange < startRange)
            throw new ArgumentOutOfRangeException(nameof(endRange), $"AL object id end range ({endRange}) must be greater than or equal to the start range ({startRange}).");
        _startRange = startRange;
        _endRange = endRange;
    }

    /// <summary>
    /// Seeds this provider with previously-assigned ids from a persisted object map, before any new
    /// ids are minted this run. Every id in the map (active or tombstoned) reserves its slot so it is
    /// never handed out to a different object; only active entries become resolvable via
    /// <see cref="TryGetExistingId"/>.
    /// </summary>
    public void SeedFromMap(Kiota.Builder.Refiners.ALObjectMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        foreach (var (key, entry) in map.Objects)
        {
            ReserveId(entry.ObjectType, entry.ObjectId);
            if (!entry.Tombstoned)
                _existingIdsByKey[key] = entry.ObjectId;
        }
    }

    /// <summary>Returns the previously-assigned id for <paramref name="key"/>, if this object was
    /// seen (and not removed) in a prior generation covered by the seeded map.</summary>
    public bool TryGetExistingId(string key, out int id)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return _existingIdsByKey.TryGetValue(key, out id);
    }

    private void ReserveId(string objectType, int id)
    {
        var key = objectType.ToLowerInvariant();
        if (!_reservedIds.TryGetValue(key, out var reserved))
        {
            reserved = new HashSet<int>();
            _reservedIds[key] = reserved;
        }
        reserved.Add(id);
    }

    public int GetNextObjectId(string objectType)
    {
        ArgumentNullException.ThrowIfNull(objectType);
        var key = objectType.ToLowerInvariant();
        if (!_nextCandidate.TryGetValue(key, out var candidate))
            candidate = _startRange;
        _reservedIds.TryGetValue(key, out var reserved);
        while (reserved is not null && reserved.Contains(candidate))
            candidate++;
        if (candidate > _endRange)
            throw new InvalidOperationException($"AL object id {candidate} for type '{key}' exceeds the configured id range end ({_endRange}). Increase 'objectIdRangeEnd' in al-config.json.");
        ReserveId(key, candidate);
        _nextCandidate[key] = candidate + 1;
        return candidate;
    }

    public int GetNextCodeunitId() => GetNextObjectId("codeunit");
    public int GetNextEnumId() => GetNextObjectId("enum");
}
