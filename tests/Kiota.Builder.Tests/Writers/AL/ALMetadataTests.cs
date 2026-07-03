using Kiota.Builder.CodeDOM;
using Kiota.Builder.Writers.AL;

using Xunit;

namespace Kiota.Builder.Tests.Writers.AL;

public class ALMetadataTests
{
    [Fact]
    public void AppendCsvCreatesEntryWhenAbsent()
    {
        var codeClass = new CodeClass { Name = "Widget" };

        codeClass.AppendCsv(ALCustomDataKeys.Pragmas, "AA0215");

        Assert.Equal("AA0215", codeClass.GetData(ALCustomDataKeys.Pragmas));
    }

    [Fact]
    public void AppendCsvAppendsDistinctTokens()
    {
        var codeClass = new CodeClass { Name = "Widget" };

        codeClass.AppendCsv(ALCustomDataKeys.Pragmas, "AA0215");
        codeClass.AppendCsv(ALCustomDataKeys.Pragmas, "AA0137");

        Assert.Equal("AA0215,AA0137", codeClass.GetData(ALCustomDataKeys.Pragmas));
    }

    [Fact]
    public void AppendCsvIsIdempotentForTheSameToken()
    {
        // Regression: DeduplicateName's own fallback branches add the NamingConvention pragma via
        // the dedup-safe private AddPragma helper, and callers (ApplyClassNameChanges/
        // ApplyEnumNameChanges) then separately call AppendCsv for the same pragma when the final
        // name differs from the original - AppendCsv must not blindly re-append an already-present
        // token, or the emitted pragma line ends up like "#pragma warning disable AA0215,AA0215".
        var codeClass = new CodeClass { Name = "Widget" };

        codeClass.AppendCsv(ALCustomDataKeys.Pragmas, "AA0215");
        codeClass.AppendCsv(ALCustomDataKeys.Pragmas, "AA0215");

        Assert.Equal("AA0215", codeClass.GetData(ALCustomDataKeys.Pragmas));
    }

    [Fact]
    public void AppendCsvIsIdempotentCaseInsensitively()
    {
        var codeClass = new CodeClass { Name = "Widget" };

        codeClass.AppendCsv(ALCustomDataKeys.Pragmas, "aa0215");
        codeClass.AppendCsv(ALCustomDataKeys.Pragmas, "AA0215");

        Assert.Equal("aa0215", codeClass.GetData(ALCustomDataKeys.Pragmas));
    }
}
