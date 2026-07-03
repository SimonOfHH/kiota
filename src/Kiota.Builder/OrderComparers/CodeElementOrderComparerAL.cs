using System;
using Kiota.Builder.CodeDOM;
using Kiota.Builder.Writers.AL;

namespace Kiota.Builder.OrderComparers;

public class CodeElementOrderComparerAL : CodeElementOrderComparer
{
    protected override int methodKindWeight => 200;

    protected override int GetMethodKindFactor(CodeElement element)
    {
        if (element is CodeMethod method)
        {
            // AL synthetic client/request-builder methods are modelled as Custom + a typed AL method
            // category instead of hijacking shared CodeMethodKind values. Preserve their historical weights.
            if (method.Kind == CodeMethodKind.Custom)
            {
                switch (method.GetCategory())
                {
                    case ALMethodCategory.ClientDefaultConfiguration:
                        return 0; // formerly Factory (fell through to default 0)
                    case ALMethodCategory.ClientConfiguration:
                        return 1; // formerly ClientConstructor
                    case ALMethodCategory.ClientInitialize:
                        return 2; // formerly Constructor
                    case ALMethodCategory.RequestBuilderConfiguration:
                    case ALMethodCategory.RequestBuilderIdentifier:
                        return 3; // formerly RawUrlBuilder
                }
            }
            return method.Kind switch
            {
                CodeMethodKind.ClientConstructor => 1,
                CodeMethodKind.Constructor => 2,
                CodeMethodKind.RawUrlConstructor => 3,
                CodeMethodKind.RawUrlBuilder => 3,
                CodeMethodKind.Deserializer => 4,
                CodeMethodKind.Serializer => 50,
                CodeMethodKind.Custom => GetSortingValue(method, 0),
                // Getter and Setter must NOT share a weight: when every other comparer factor also
                // ties (same Name after ModifyGetterSetterMethodName, same Parameters.Count() - which
                // happens for e.g. plain Codeunit or List of [Codeunit...] typed properties, since both
                // the getter's and the setter's synthetic local variables happen to add up to the same
                // count), CodeElementOrderComparer.Compare returns 0 for the pair. A tie falls back to
                // whatever order the two methods happen to enumerate from CodeBlock.InnerChildElements
                // (a ConcurrentDictionary), which is not guaranteed stable across processes/platforms
                // (bucket layout depends on randomized string-hash seeding). That let the getter/setter
                // order for those specific property types flip between a Windows and a Linux run of the
                // very same spec. Giving Setter a distinct weight removes the tie so getter always sorts
                // before setter, deterministically, everywhere.
                CodeMethodKind.Getter => 10,
                CodeMethodKind.Setter => 11,
                CodeMethodKind.RequestExecutor => 20,
                CodeMethodKind.RequestBuilderBackwardCompatibility => GetSortingValue(method, 0),
                _ => 0,
            };
        }
        return 0;
    }

    private static int GetSortingValue(CodeMethod method, int defaultValue)
    {
        return method.GetInt(ALCustomDataKeys.SortingValue, defaultValue);
    }
}
