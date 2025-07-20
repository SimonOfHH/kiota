using Kiota.Builder.CodeDOM;
using Kiota.Builder.Writers.AL;

namespace Kiota.Builder.OrderComparers;

public class CodeElementOrderComparerAL : CodeElementOrderComparer
{
    protected override int methodKindWeight { get; } = 200;
    protected override int GetMethodKindFactor(CodeElement element)
    {
        if (element is CodeMethod method)
            return method.Kind switch
            {
                CodeMethodKind.ClientConstructor => 1,
                CodeMethodKind.Constructor => 2,
                CodeMethodKind.RawUrlConstructor => 3,
                CodeMethodKind.Custom => method.GetSortingValue(0),
                CodeMethodKind.RequestBuilderBackwardCompatibility => method.GetSortingValue(0),
                _ => 0,
            };
        return 0;
    }
}
