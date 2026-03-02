using System;
using System.Collections.Generic;
using Kiota.Builder.CodeDOM;

namespace Kiota.Builder.Writers.AL;

internal static class CodeElementExtensions
{
    public static bool ParentIsSkipped(this CodeElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(element.Parent);
        return element.Parent.CustomData.TryGetValue("skip", out var skip) && skip.Equals("true", StringComparison.OrdinalIgnoreCase);
    }
}
