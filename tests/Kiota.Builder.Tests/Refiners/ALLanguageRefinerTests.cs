using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

using Kiota.Builder.CodeDOM;
using Kiota.Builder.Configuration;
using Kiota.Builder.Refiners;
using Kiota.Builder.Writers.AL;

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

    [Fact]
    public async Task AddsRawUrlPaginationMethodsToRequestBuilderAsync()
    {
        var config = CreateConfiguration();
        var ns = root.AddNamespace("ApiSdk.users");
        var requestBuilder = ns.AddClass(new CodeClass
        {
            Name = "usersRequestBuilder",
            Kind = CodeClassKind.RequestBuilder,
        }).First();
        var withUrl = new CodeMethod
        {
            Name = "WithUrl",
            Kind = CodeMethodKind.RawUrlBuilder,
            ReturnType = new CodeType { Name = "usersRequestBuilder", TypeDefinition = requestBuilder },
        };
        withUrl.AddParameter(new CodeParameter
        {
            Name = "rawUrl",
            Kind = CodeParameterKind.RawUrl,
            Type = new CodeType { Name = "string", IsExternal = true },
        });
        requestBuilder.AddMethod(withUrl);

        await ILanguageRefiner.RefineAsync(config, root, cancellationToken: TestContext.Current.CancellationToken);

        // WithUrl keeps its upstream RawUrlBuilder kind (dispatched by kind, like the other language writers),
        // is un-skipped, returns a "Rqst" sibling builder, and has its parameter renamed.
        Assert.Equal(CodeMethodKind.RawUrlBuilder, withUrl.Kind);
        Assert.False(withUrl.CustomData.TryGetValue("skip", out var skip) && skip == "true");
        Assert.True(withUrl.CustomData.TryGetValue("return-variable-name", out var returnVar));
        Assert.Equal("Rqst", returnVar);
        Assert.Equal("RawUrl", withUrl.Parameters.First().Name);

        // A SetConfigurationRaw helper is added to back WithUrl.
        var setConfigRaw = requestBuilder.Methods.FirstOrDefault(m => m.Name.Equals("SetConfigurationRaw", System.StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(setConfigRaw);
        Assert.Equal(ALMethodCategory.RequestBuilderRawConfiguration, setConfigRaw!.GetCategory());
        Assert.Equal(2, setConfigRaw.Parameters.Count());
    }

    [Fact]
    public async Task DoesNotAssignObjectIdToInnerQueryParametersClassAsync()
    {
        // Mirrors KiotaBuilder.CreateOperationParameterClass: the built-in "QueryParameters" class is
        // added as an inner class of the request builder, not as a top-level namespace member. The AL
        // renderer never emits inner classes as separate objects (ALRefiner creates its own top-level
        // parameter codeunit later), so this inner class must not consume an object id - otherwise the
        // allocated id range has gaps that never appear in the generated output.
        var config = CreateConfiguration();
        var ns = root.AddNamespace("ApiSdk.users");
        var requestBuilder = ns.AddClass(new CodeClass
        {
            Name = "usersRequestBuilder",
            Kind = CodeClassKind.RequestBuilder,
        }).First();

        var queryParametersClass = requestBuilder.AddInnerClass(new CodeClass
        {
            Name = "usersRequestBuilderGetQueryParameters",
            Kind = CodeClassKind.QueryParameters,
        }).First();
        queryParametersClass.AddProperty(new CodeProperty
        {
            Name = "filter",
            Kind = CodePropertyKind.QueryParameter,
            Type = new CodeType { Name = "string", IsExternal = true },
        });

        var executor = new CodeMethod
        {
            Name = "Get",
            Kind = CodeMethodKind.RequestExecutor,
            ReturnType = new CodeType { Name = "void", IsExternal = true },
        };
        executor.AddParameter(new CodeParameter
        {
            Name = "requestConfiguration",
            Kind = CodeParameterKind.RequestConfiguration,
            Type = new CodeType { Name = "usersRequestBuilderGetQueryParameters", TypeDefinition = queryParametersClass },
        });
        requestBuilder.AddMethod(executor);

        // Another top-level model class right after, to detect a gap in the allocated ids.
        var modelClass = TestHelper.CreateModelClassInModelsNamespace(config, root, "widget");

        await ILanguageRefiner.RefineAsync(config, root, cancellationToken: TestContext.Current.CancellationToken);

        // The inner QueryParameters class must not have received an object id.
        Assert.False(queryParametersClass.CustomData.TryGetValue("object-id", out _));

        // The request builder and the model class must have received two DISTINCT ids that are
        // adjacent as a set (no id was reserved-and-wasted for the inner class in between). Traversal
        // order across sibling namespaces is not guaranteed, so don't assume which one comes first.
        Assert.True(requestBuilder.CustomData.TryGetValue("object-id", out var requestBuilderId));
        Assert.True(modelClass.CustomData.TryGetValue("object-id", out var modelClassId));
        Assert.True(int.TryParse(requestBuilderId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var requestBuilderIdValue));
        Assert.True(int.TryParse(modelClassId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var modelClassIdValue));
        Assert.NotEqual(requestBuilderIdValue, modelClassIdValue);
        Assert.Equal(1, System.Math.Abs(requestBuilderIdValue - modelClassIdValue));
    }
}
