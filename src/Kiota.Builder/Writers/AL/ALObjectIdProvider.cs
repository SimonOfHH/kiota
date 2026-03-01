using System;
using System.Collections.Generic;

namespace Kiota.Builder.Writers.AL;

public class ALObjectIdProvider
{
    private readonly int _startRange;
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

    public ALObjectIdProvider(int startRange = 50000)
    {
        _startRange = startRange;
    }

    public int GetNextObjectId(string objectType)
    {
        ArgumentNullException.ThrowIfNull(objectType);
        var key = objectType.ToLowerInvariant();
        if (!_counters.ContainsKey(key))
            _counters[key] = 0;
        return _counters[key]++ + _startRange;
    }

    public int GetNextCodeunitId() => GetNextObjectId("codeunit");
    public int GetNextEnumId() => GetNextObjectId("enum");
}
