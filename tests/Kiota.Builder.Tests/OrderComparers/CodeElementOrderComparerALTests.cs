using Kiota.Builder.CodeDOM;
using Kiota.Builder.OrderComparers;
using Kiota.Builder.Writers.AL;

using Xunit;

namespace Kiota.Builder.Tests.OrderComparers;

public class CodeElementOrderComparerALTests
{
    private static CodeMethod CreateToJsonOverload(int overloadRank)
    {
        var method = new CodeMethod
        {
            Name = "ToJson",
            Kind = CodeMethodKind.Serializer,
            ReturnType = new CodeType { Name = "JsonObject", IsExternal = true },
        };
        method.AddParameter(new CodeParameter { Name = "foo", Kind = CodeParameterKind.Custom, Type = new CodeType { Name = "string" } });
        if (overloadRank > 0)
            method.SetData(ALCustomDataKeys.OverloadRank, overloadRank.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return method;
    }

    [Fact]
    public void BreaksFullTiesBetweenSameNameOverloadsByOverloadRank()
    {
        var comparer = new CodeElementOrderComparerAL();
        var current = CreateToJsonOverload(0);
        var legacy1 = CreateToJsonOverload(1);
        var legacy2 = CreateToJsonOverload(2);

        Assert.True(comparer.Compare(current, legacy1) < 0);
        Assert.True(comparer.Compare(legacy1, legacy2) < 0);
        Assert.True(comparer.Compare(legacy2, legacy1) > 0);
        Assert.Equal(0, comparer.Compare(legacy1, CreateToJsonOverload(1)));
    }
}
