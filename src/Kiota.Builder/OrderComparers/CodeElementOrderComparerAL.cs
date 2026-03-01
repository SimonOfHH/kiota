using System;
using Kiota.Builder.CodeDOM;

namespace Kiota.Builder.OrderComparers;

public class CodeElementOrderComparerAL : CodeElementOrderComparer
{
    protected override int methodKindWeight => 200;

    protected override int GetMethodKindFactor(CodeElement element)
    {
        if (element is CodeMethod method)
        {
            return method.Kind switch
            {
                CodeMethodKind.ClientConstructor => 1,
                CodeMethodKind.Constructor => 2,
                CodeMethodKind.RawUrlConstructor => 3,
                CodeMethodKind.Custom => GetSortingValue(method, 0),
                CodeMethodKind.RequestBuilderBackwardCompatibility => GetSortingValue(method, 0),
                _ => 0,
            };
        }
        return 0;
    }

    private static int GetSortingValue(CodeMethod method, int defaultValue)
    {
        return method.CustomData.TryGetValue("sorting-value", out var val)
            && int.TryParse(val, out var sortVal) ? sortVal : defaultValue;
    }
}
