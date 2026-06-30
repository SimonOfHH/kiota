using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using Kiota.Builder.CodeDOM;
using Kiota.Builder.Configuration;
using Kiota.Builder.Refiners;

using Xunit;

namespace Kiota.Builder.Tests.Refiners;

public class ALLanguageRefinerTests
{
    private readonly CodeNamespace root = CodeNamespace.InitRootNamespace();

    private static GenerationConfiguration CreateConfiguration() => new()
    {
        Language = GenerationLanguage.AL,
        // Non-existent path so ALConfiguration.LoadFromDisk falls back to defaults.
        OutputPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N")),
        ClientClassName = "ApiClient",
        ClientNamespaceName = "ApiSdk",
    };

    [Fact]
    public async Task RenamesNamespaceSegmentsStartingWithUnderscoreAsync()
    {
        var ns = root.AddNamespace("ApiSdk._internal");
        ns.AddClass(new CodeClass { Name = "Holder", Kind = CodeClassKind.Model });

        await ILanguageRefiner.RefineAsync(CreateConfiguration(), root, cancellationToken: TestContext.Current.CancellationToken);

        // The same namespace instance is renamed in place; underscore segment becomes "u"-prefixed.
        Assert.Equal("ApiSdk.uinternal", ns.Name);
    }

    [Fact]
    public async Task AssignsObjectIdsToModelClassesAsync()
    {
        var config = CreateConfiguration();
        var modelClass = TestHelper.CreateModelClassInModelsNamespace(config, root, "widget");

        await ILanguageRefiner.RefineAsync(config, root, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(modelClass.CustomData.TryGetValue("object-id", out var objectId));
        Assert.True(int.TryParse(objectId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id));
        Assert.True(id >= 50000, $"Expected object id >= 50000 but was {id}");
    }

    [Fact]
    public async Task AssignsObjectIdsToEnumsAsync()
    {
        var modelsNs = root.AddNamespace("ApiSdk.models");
        var codeEnum = modelsNs.AddEnum(new CodeEnum { Name = "color" }).First();
        codeEnum.AddOption(new CodeEnumOption { Name = "red" });

        await ILanguageRefiner.RefineAsync(CreateConfiguration(), root, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(codeEnum.CustomData.TryGetValue("object-id", out var objectId));
        Assert.True(int.TryParse(objectId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id));
        Assert.True(id >= 50000, $"Expected object id >= 50000 but was {id}");
    }

    [Fact]
    public async Task FlattensInheritanceByRemovingBaseTypeLinkAsync()
    {
        var config = CreateConfiguration();
        var derived = TestHelper.CreateModelClassInModelsNamespace(config, root, "derived", withInheritance: true);
        Assert.NotNull(derived.StartBlock.Inherits);

        await ILanguageRefiner.RefineAsync(config, root, cancellationToken: TestContext.Current.CancellationToken);

        // AL has no inheritance: the base-type link must be removed after refinement.
        Assert.Null(derived.StartBlock.Inherits);
    }

    [Fact]
    public async Task AssignsUniqueObjectIdsAcrossObjectsAsync()
    {
        var config = CreateConfiguration();
        var first = TestHelper.CreateModelClassInModelsNamespace(config, root, "first");
        var second = TestHelper.CreateModelClassInModelsNamespace(config, root, "second");

        await ILanguageRefiner.RefineAsync(config, root, cancellationToken: TestContext.Current.CancellationToken);

        first.CustomData.TryGetValue("object-id", out var firstId);
        second.CustomData.TryGetValue("object-id", out var secondId);
        Assert.NotNull(firstId);
        Assert.NotNull(secondId);
        Assert.NotEqual(firstId, secondId);
    }

    [Fact]
    public async Task HonorsObjectPrefixSuffixAndIdRangeFromConfigAsync()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempDir);
        try
        {
            // ALConfiguration.LoadFromDisk looks for al-config.json next to the output path's directory.
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(tempDir, "al-config.json"),
                "{\"objectPrefix\":\"PX\",\"objectSuffix\":\"SX\",\"objectIdRangeStart\":70000}");

            var config = new GenerationConfiguration
            {
                Language = GenerationLanguage.AL,
                OutputPath = System.IO.Path.Combine(tempDir, "output"),
                ClientClassName = "ApiClient",
                ClientNamespaceName = "ApiSdk",
            };
            var modelClass = TestHelper.CreateModelClassInModelsNamespace(config, root, "widget");

            await ILanguageRefiner.RefineAsync(config, root, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal("PXwidgetSX", modelClass.Name);
            Assert.True(modelClass.CustomData.TryGetValue("object-id", out var objectId));
            Assert.True(int.TryParse(objectId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id));
            Assert.True(id >= 70000, $"Expected object id >= 70000 but was {id}");
        }
        finally
        {
            System.IO.Directory.Delete(tempDir, true);
        }
    }
}
