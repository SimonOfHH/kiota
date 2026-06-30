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
            // AL synthetic client/request-builder methods are modelled as Custom + an AL Source tag
            // instead of hijacking shared CodeMethodKind values. Preserve their historical weights.
            if (method.Kind == CodeMethodKind.Custom && method.TryGetData(ALCustomDataKeys.Source, out var alSource))
            {
                switch (alSource)
                {
                    case ALCustomDataKeys.Sources.ClientDefaultConfiguration:
                        return 0; // formerly Factory (fell through to default 0)
                    case ALCustomDataKeys.Sources.ClientConfiguration:
                        return 1; // formerly ClientConstructor
                    case ALCustomDataKeys.Sources.ClientInitialize:
                        return 2; // formerly Constructor
                    case ALCustomDataKeys.Sources.RequestBuilderConfiguration:
                    case ALCustomDataKeys.Sources.RequestBuilderIdentifier:
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
                CodeMethodKind.Getter => 10,
                CodeMethodKind.Setter => 10,
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
