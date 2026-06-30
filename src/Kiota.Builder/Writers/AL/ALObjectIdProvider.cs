using System;
using System.Collections.Generic;

namespace Kiota.Builder.Writers.AL;

public class ALObjectIdProvider
{
    private readonly int _startRange;
    private readonly int _endRange;
    private readonly Dictionary<string, int> _counters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["codeunit"] = 0,
        ["enum"] = 0,
        ["page"] = 0,
        ["table"] = 0,
        ["report"] = 0,
        ["xmlport"] = 0,
        ["query"] = 0,
    };

    public ALObjectIdProvider(int startRange = 50000, int endRange = 99999)
    {
        if (endRange < startRange)
            throw new ArgumentOutOfRangeException(nameof(endRange), $"AL object id end range ({endRange}) must be greater than or equal to the start range ({startRange}).");
        _startRange = startRange;
        _endRange = endRange;
    }

    public int GetNextObjectId(string objectType)
    {
        ArgumentNullException.ThrowIfNull(objectType);
        var key = objectType.ToLowerInvariant();
        if (!_counters.ContainsKey(key))
            _counters[key] = 0;
        var id = _counters[key] + _startRange;
        if (id > _endRange)
            throw new InvalidOperationException($"AL object id {id} for type '{key}' exceeds the configured id range end ({_endRange}). Increase 'objectIdRangeEnd' in al-config.json.");
        _counters[key]++;
        return id;
    }

    public int GetNextCodeunitId() => GetNextObjectId("codeunit");
    public int GetNextEnumId() => GetNextObjectId("enum");
}
