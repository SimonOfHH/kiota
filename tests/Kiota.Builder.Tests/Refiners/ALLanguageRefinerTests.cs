using System;
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
    public async Task AbbreviatesEnumNamesLongerThan30CharactersAsync()
    {
        // AL0659: enum identifiers longer than 30 characters may collide at runtime with another
        // enum sharing the same first 30 characters. Long enum names must be abbreviated/truncated,
        // mirroring the existing class-name-length handling in ApplyClassNameChanges.
        var modelsNs = root.AddNamespace("ApiSdk.models");
        var codeEnum = modelsNs.AddEnum(new CodeEnum { Name = "PostDatanormDestinationVersionQueryParameterType" }).First();
        codeEnum.AddOption(new CodeEnumOption { Name = "value1" });

        await ILanguageRefiner.RefineAsync(CreateConfiguration(), root, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(codeEnum.Name.Length <= 30, $"Expected enum name <= 30 characters but was '{codeEnum.Name}' ({codeEnum.Name.Length})");
        Assert.True(codeEnum.CustomData.TryGetValue(ALCustomDataKeys.OriginalName, out var originalName));
        Assert.Equal("PostDatanormDestinationVersionQueryParameterType", originalName);
        Assert.True(codeEnum.CustomData.TryGetValue(ALCustomDataKeys.Pragmas, out var pragmas));
        Assert.Contains(ALCustomDataKeys.PragmaCodes.NamingConvention, pragmas!.Split(','));
    }

    [Fact]
    public async Task DeduplicatesEnumNamesThatCollideOnlyAfterAbbreviationAsync()
    {
        // Two DIFFERENT original enum names that abbreviate down to the identical 30-character
        // string must still end up with distinct final names - the same collision risk AL0659 warns
        // about, just triggered by truncation instead of an exact original-name match.
        var modelsNs = root.AddNamespace("ApiSdk.models");
        var longNameA = "PostDatanormDestinationVersionQueryParameterTypeAlpha";
        var longNameB = "PostDatanormDestinationVersionQueryParameterTypeBeta";
        var enumA = modelsNs.AddEnum(new CodeEnum { Name = longNameA }).First();
        enumA.AddOption(new CodeEnumOption { Name = "value1" });
        var enumB = modelsNs.AddEnum(new CodeEnum { Name = longNameB }).First();
        enumB.AddOption(new CodeEnumOption { Name = "value1" });

        await ILanguageRefiner.RefineAsync(CreateConfiguration(), root, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(enumA.Name.Length <= 30, $"Expected enum name <= 30 characters but was '{enumA.Name}' ({enumA.Name.Length})");
        Assert.True(enumB.Name.Length <= 30, $"Expected enum name <= 30 characters but was '{enumB.Name}' ({enumB.Name.Length})");
        Assert.NotEqual(enumA.Name, enumB.Name);
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

    [Fact]
    public async Task SuppressesUnusedVariableWarningOnModelCodeunitGlobalVariablesAsync()
    {
        // A Model codeunit with no properties never emits the property-iteration code that
        // references SubToken/JSONHelper, so those default global variables would otherwise trigger
        // AA0137 (unused variable). The whole global-var block must be wrapped in the pragma.
        var config = CreateConfiguration();
        var modelClass = TestHelper.CreateModelClassInModelsNamespace(config, root, "widget");

        await ILanguageRefiner.RefineAsync(config, root, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(modelClass.CustomData.TryGetValue(ALCustomDataKeys.PragmasVariables, out var pragmas));
        Assert.Contains(ALCustomDataKeys.PragmaCodes.UnusedVariable, pragmas!.Split(','));
    }

    [Fact]
    public async Task OmitsQueryParamFormatterWhenAllQueryParametersAreTextOrEnumAsync()
    {
        // QueryParamFormatter is only referenced by the typed setter of a "primitive"-category query
        // parameter (see CodeMethodWriter.WriteQueryParamTypedSetterBody). If every query parameter
        // is text-typed (or enum-typed), the variable is never referenced and would trigger AA0137.
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

        await ILanguageRefiner.RefineAsync(config, root, cancellationToken: TestContext.Current.CancellationToken);

        var paramCodeunit = ns.Classes.Single(c => c.GetFlag(ALCustomDataKeys.ParameterCodeunit));
        var globalVarNames = paramCodeunit.Properties
            .Where(p => p.HasData(ALCustomDataKeys.GlobalVariable))
            .Select(p => p.Name)
            .ToList();

        Assert.DoesNotContain("QueryParamFormatter", globalVarNames);
        Assert.Contains("QueryParameters", globalVarNames);
    }

    [Fact]
    public async Task AssignsSameObjectIdsRegardlessOfInsertionOrderAsync()
    {
        // Same logical tree, built via two different insertion-call orders. Object id assignment
        // must not depend on that order (CodeDOM child collections have no enumeration-order
        // guarantee - see CrawlTreeOrdered).
        var rootForward = CodeNamespace.InitRootNamespace();
        var forwardNs = rootForward.AddNamespace("ApiSdk.models");
        var alphaForward = forwardNs.AddClass(new CodeClass { Name = "Alpha", Kind = CodeClassKind.Model }).First();
        var bravoForward = forwardNs.AddClass(new CodeClass { Name = "Bravo", Kind = CodeClassKind.Model }).First();
        var charlieForward = forwardNs.AddEnum(new CodeEnum { Name = "Charlie" }).First();
        charlieForward.AddOption(new CodeEnumOption { Name = "one" });

        var rootReversed = CodeNamespace.InitRootNamespace();
        var reversedNs = rootReversed.AddNamespace("ApiSdk.models");
        var charlieReversed = reversedNs.AddEnum(new CodeEnum { Name = "Charlie" }).First();
        charlieReversed.AddOption(new CodeEnumOption { Name = "one" });
        var bravoReversed = reversedNs.AddClass(new CodeClass { Name = "Bravo", Kind = CodeClassKind.Model }).First();
        var alphaReversed = reversedNs.AddClass(new CodeClass { Name = "Alpha", Kind = CodeClassKind.Model }).First();

        await ILanguageRefiner.RefineAsync(CreateConfiguration(), rootForward, cancellationToken: TestContext.Current.CancellationToken);
        await ILanguageRefiner.RefineAsync(CreateConfiguration(), rootReversed, cancellationToken: TestContext.Current.CancellationToken);

        static string IdOf(CodeElement e)
        {
            Assert.True(e.CustomData.TryGetValue("object-id", out var id));
            return id!;
        }

        Assert.Equal(IdOf(alphaForward), IdOf(alphaReversed));
        Assert.Equal(IdOf(bravoForward), IdOf(bravoReversed));
        Assert.Equal(IdOf(charlieForward), IdOf(charlieReversed));
    }

    [Fact]
    public async Task DeduplicatesCollidingClassNamesTheSameWayRegardlessOfInsertionOrderAsync()
    {
        // Two model classes named "Widget" in different namespaces (structurally different, so
        // DeduplicateObjects does not merge them) force ALConventionService.DeduplicateName's
        // first-come-first-served registry. Which one keeps the plain name and which one gets the
        // namespace-prefixed name must be the same regardless of insertion order.
        var rootForward = CodeNamespace.InitRootNamespace();
        var moduleAForward = rootForward.AddNamespace("ApiSdk.moduleA");
        var widgetAForward = moduleAForward.AddClass(new CodeClass { Name = "Widget", Kind = CodeClassKind.Model }).First();
        widgetAForward.AddProperty(new CodeProperty { Name = "foo", Kind = CodePropertyKind.Custom, Type = new CodeType { Name = "string", IsExternal = true } });
        var moduleBForward = rootForward.AddNamespace("ApiSdk.moduleB");
        var widgetBForward = moduleBForward.AddClass(new CodeClass { Name = "Widget", Kind = CodeClassKind.Model }).First();
        widgetBForward.AddProperty(new CodeProperty { Name = "bar", Kind = CodePropertyKind.Custom, Type = new CodeType { Name = "string", IsExternal = true } });

        var rootReversed = CodeNamespace.InitRootNamespace();
        var moduleBReversed = rootReversed.AddNamespace("ApiSdk.moduleB");
        var widgetBReversed = moduleBReversed.AddClass(new CodeClass { Name = "Widget", Kind = CodeClassKind.Model }).First();
        widgetBReversed.AddProperty(new CodeProperty { Name = "bar", Kind = CodePropertyKind.Custom, Type = new CodeType { Name = "string", IsExternal = true } });
        var moduleAReversed = rootReversed.AddNamespace("ApiSdk.moduleA");
        var widgetAReversed = moduleAReversed.AddClass(new CodeClass { Name = "Widget", Kind = CodeClassKind.Model }).First();
        widgetAReversed.AddProperty(new CodeProperty { Name = "foo", Kind = CodePropertyKind.Custom, Type = new CodeType { Name = "string", IsExternal = true } });

        await ILanguageRefiner.RefineAsync(CreateConfiguration(), rootForward, cancellationToken: TestContext.Current.CancellationToken);
        await ILanguageRefiner.RefineAsync(CreateConfiguration(), rootReversed, cancellationToken: TestContext.Current.CancellationToken);

        // The exact same original class must end up with the exact same final AL name in both runs.
        Assert.Equal(widgetAForward.Name, widgetAReversed.Name);
        Assert.Equal(widgetBForward.Name, widgetBReversed.Name);
        Assert.NotEqual(widgetAForward.Name, widgetBForward.Name);
    }

    [Fact]
    public async Task PersistedObjectMapKeepsIdsAndNamesStableAcrossAnEvolvingSpecAsync()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempDir);
        try
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(tempDir, "al-config.json"),
                "{\"objectMapPath\":\"obj-map.json\"}");
            var mapPath = System.IO.Path.Combine(tempDir, "obj-map.json");

            GenerationConfiguration CreateConfig() => new()
            {
                Language = GenerationLanguage.AL,
                OutputPath = System.IO.Path.Combine(tempDir, "output"),
                ClientClassName = "ApiClient",
                ClientNamespaceName = "ApiSdk",
            };

            // Run 1: spec has "Alpha" and "Beta".
            var run1Root = CodeNamespace.InitRootNamespace();
            var config1 = CreateConfig();
            var alpha1 = TestHelper.CreateModelClassInModelsNamespace(config1, run1Root, "Alpha");
            var beta1 = TestHelper.CreateModelClassInModelsNamespace(config1, run1Root, "Beta");
            await ILanguageRefiner.RefineAsync(config1, run1Root, cancellationToken: TestContext.Current.CancellationToken);

            Assert.True(System.IO.File.Exists(mapPath));
            alpha1.CustomData.TryGetValue("object-id", out var alphaId1);
            beta1.CustomData.TryGetValue("object-id", out var betaId1);
            Assert.NotNull(alphaId1);
            Assert.NotNull(betaId1);

            // Run 2: "Beta" was removed from the spec, "Gamma" was added. "Alpha" is unchanged.
            var run2Root = CodeNamespace.InitRootNamespace();
            var config2 = CreateConfig();
            var alpha2 = TestHelper.CreateModelClassInModelsNamespace(config2, run2Root, "Alpha");
            var gamma2 = TestHelper.CreateModelClassInModelsNamespace(config2, run2Root, "Gamma");
            await ILanguageRefiner.RefineAsync(config2, run2Root, cancellationToken: TestContext.Current.CancellationToken);

            alpha2.CustomData.TryGetValue("object-id", out var alphaId2);
            gamma2.CustomData.TryGetValue("object-id", out var gammaId2);

            // Alpha (seen in both runs) keeps its exact id and name.
            Assert.Equal(alphaId1, alphaId2);
            Assert.Equal(alpha1.Name, alpha2.Name);

            // Gamma (new in run 2) gets a fresh id that doesn't collide with anything already mapped.
            Assert.NotNull(gammaId2);
            Assert.NotEqual(betaId1, gammaId2);
            Assert.NotEqual(alphaId2, gammaId2);

            // Beta (removed in run 2) is tombstoned in the saved map, keeping its original id/name.
            var savedMap = ALObjectMap.LoadFromDisk(mapPath);
            var betaEntry = Assert.Single(savedMap.Objects.Values, e => e.AssignedName == beta1.Name);
            Assert.True(betaEntry.Tombstoned);
            Assert.Equal(int.Parse(betaId1, CultureInfo.InvariantCulture), betaEntry.ObjectId);
        }
        finally
        {
            System.IO.Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PersistedObjectMapKeepsNamingConventionPragmaOnARenamedClassAsync()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempDir);
        try
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(tempDir, "al-config.json"),
                "{\"objectMapPath\":\"obj-map.json\"}");

            GenerationConfiguration CreateConfig() => new()
            {
                Language = GenerationLanguage.AL,
                OutputPath = System.IO.Path.Combine(tempDir, "output"),
                ClientClassName = "ApiClient",
                ClientNamespaceName = "ApiSdk",
            };

            // Two colliding class names ("Widget" in two different, structurally-different modules)
            // force ApplyClassNameChanges into its dedup branch, which renames one of them and tags
            // it with the AA0215 naming-convention pragma so the object/file name mismatch doesn't
            // trigger a warning.
            static (CodeClass a, CodeClass b) CreateWidgets(CodeNamespace root)
            {
                var moduleA = root.AddNamespace("ApiSdk.moduleA");
                var widgetA = moduleA.AddClass(new CodeClass { Name = "Widget", Kind = CodeClassKind.Model }).First();
                widgetA.AddProperty(new CodeProperty { Name = "foo", Kind = CodePropertyKind.Custom, Type = new CodeType { Name = "string", IsExternal = true } });
                var moduleB = root.AddNamespace("ApiSdk.moduleB");
                var widgetB = moduleB.AddClass(new CodeClass { Name = "Widget", Kind = CodeClassKind.Model }).First();
                widgetB.AddProperty(new CodeProperty { Name = "bar", Kind = CodePropertyKind.Custom, Type = new CodeType { Name = "string", IsExternal = true } });
                return (widgetA, widgetB);
            }

            // Run 1: two colliding "Widget" classes across two modules - one gets renamed + pragma-tagged.
            var run1Root = CodeNamespace.InitRootNamespace();
            var config1 = CreateConfig();
            var (widgetA1, widgetB1) = CreateWidgets(run1Root);
            await ILanguageRefiner.RefineAsync(config1, run1Root, cancellationToken: TestContext.Current.CancellationToken);

            var renamed1 = widgetA1.Name != "Widget" ? widgetA1 : widgetB1;
            Assert.NotEqual("Widget", renamed1.Name);
            renamed1.CustomData.TryGetValue(ALCustomDataKeys.Pragmas, out var pragmas1);
            Assert.NotNull(pragmas1);
            Assert.Contains(ALCustomDataKeys.PragmaCodes.NamingConvention, (string)pragmas1!, StringComparison.Ordinal);

            // Run 2: same two classes again - the renamed one should hit the persisted map, reuse the
            // exact same (still-differing) name, and still carry the pragma.
            var run2Root = CodeNamespace.InitRootNamespace();
            var config2 = CreateConfig();
            var (widgetA2, widgetB2) = CreateWidgets(run2Root);
            await ILanguageRefiner.RefineAsync(config2, run2Root, cancellationToken: TestContext.Current.CancellationToken);

            var renamed2 = renamed1 == widgetA1 ? widgetA2 : widgetB2;
            Assert.Equal(renamed1.Name, renamed2.Name);
            renamed2.CustomData.TryGetValue(ALCustomDataKeys.Pragmas, out var pragmas2);
            Assert.NotNull(pragmas2);
            Assert.Contains(ALCustomDataKeys.PragmaCodes.NamingConvention, (string)pragmas2!, StringComparison.Ordinal);
        }
        finally
        {
            System.IO.Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PersistedObjectMapKeepsEnumNameStableAcrossAbbreviatedRunsAsync()
    {
        // Regression: ApplyEnumNameChanges used to skip the persisted-map name-reuse check that
        // ApplyClassNameChanges already had, so an enum that needed abbreviation/deduplication in run
        // 1 was silently re-abbreviated/re-deduplicated from scratch on every later run instead of
        // reusing its mapped name. That meant the AL name could drift (e.g. after a change to
        // ALConventionService's abbreviation table) even though the object map was configured
        // specifically to keep names stable.
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempDir);
        try
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(tempDir, "al-config.json"),
                "{\"objectMapPath\":\"obj-map.json\"}");

            GenerationConfiguration CreateConfig() => new()
            {
                Language = GenerationLanguage.AL,
                OutputPath = System.IO.Path.Combine(tempDir, "output"),
                ClientClassName = "ApiClient",
                ClientNamespaceName = "ApiSdk",
            };

            const string longName = "PostDatanormDestinationVersionQueryParameterType";

            static CodeEnum CreateLongEnum(CodeNamespace root)
            {
                var modelsNs = root.AddNamespace("ApiSdk.models");
                var codeEnum = modelsNs.AddEnum(new CodeEnum { Name = longName }).First();
                codeEnum.AddOption(new CodeEnumOption { Name = "value1" });
                return codeEnum;
            }

            // Run 1: name is too long -> gets abbreviated/dedup-processed and tagged with the
            // naming-convention pragma.
            var run1Root = CodeNamespace.InitRootNamespace();
            var config1 = CreateConfig();
            var enum1 = CreateLongEnum(run1Root);
            await ILanguageRefiner.RefineAsync(config1, run1Root, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotEqual(longName, enum1.Name);
            enum1.CustomData.TryGetValue(ALCustomDataKeys.Pragmas, out var pragmas1);
            Assert.NotNull(pragmas1);
            Assert.Contains(ALCustomDataKeys.PragmaCodes.NamingConvention, (string)pragmas1!, StringComparison.Ordinal);

            // Run 2: same enum again - must hit the persisted map and reuse the exact same name
            // verbatim (not re-run SanitizeName/DeduplicateName), still carrying the pragma.
            var run2Root = CodeNamespace.InitRootNamespace();
            var config2 = CreateConfig();
            var enum2 = CreateLongEnum(run2Root);
            await ILanguageRefiner.RefineAsync(config2, run2Root, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(enum1.Name, enum2.Name);
            enum2.CustomData.TryGetValue(ALCustomDataKeys.Pragmas, out var pragmas2);
            Assert.NotNull(pragmas2);
            Assert.Contains(ALCustomDataKeys.PragmaCodes.NamingConvention, (string)pragmas2!, StringComparison.Ordinal);
        }
        finally
        {
            System.IO.Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PersistedObjectMapKeepsDistinctIdsAndNamesForSameNameSameNamespaceClassesAsync()
    {
        // Regression for: two structurally-different classes sharing both the same original name AND
        // the same namespace (DeduplicateObjects only merges classes with IDENTICAL members, so these
        // two survive as distinct objects) used to collapse onto the same object-map key
        // ("namespace::name"), which made TryGetExistingId/TryGetExistingName hand BOTH of them the
        // exact same id/name on a second run - invalid duplicate AL object names/ids.
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempDir);
        try
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(tempDir, "al-config.json"),
                "{\"objectMapPath\":\"obj-map.json\"}");

            GenerationConfiguration CreateConfig() => new()
            {
                Language = GenerationLanguage.AL,
                OutputPath = System.IO.Path.Combine(tempDir, "output"),
                ClientClassName = "ApiClient",
                ClientNamespaceName = "ApiSdk",
            };

            static (CodeClass a, CodeClass b) CreateWidgets(CodeNamespace root)
            {
                var modelsNs = root.AddNamespace("ApiSdk.models");
                var widgetA = modelsNs.AddClass(new CodeClass { Name = "Widget", Kind = CodeClassKind.Model }).First();
                widgetA.AddProperty(new CodeProperty { Name = "foo", Kind = CodePropertyKind.Custom, Type = new CodeType { Name = "string", IsExternal = true } });
                var widgetB = modelsNs.AddClass(new CodeClass { Name = "Widget2", Kind = CodeClassKind.Model }).First();
                widgetB.Name = "Widget"; // force the same final name as widgetA without colliding in the child collection key
                widgetB.AddProperty(new CodeProperty { Name = "bar", Kind = CodePropertyKind.Custom, Type = new CodeType { Name = "string", IsExternal = true } });
                return (widgetA, widgetB);
            }

            // Run 1: establish the map with both same-name, same-namespace, structurally-different classes.
            var run1Root = CodeNamespace.InitRootNamespace();
            var config1 = CreateConfig();
            var (widgetA1, widgetB1) = CreateWidgets(run1Root);
            await ILanguageRefiner.RefineAsync(config1, run1Root, cancellationToken: TestContext.Current.CancellationToken);

            Assert.NotEqual(widgetA1.Name, widgetB1.Name);
            widgetA1.CustomData.TryGetValue("object-id", out var idA1);
            widgetB1.CustomData.TryGetValue("object-id", out var idB1);
            Assert.NotEqual(idA1, idB1);

            // Run 2: same two classes again - both must still resolve to distinct ids/names, reusing
            // (not swapping or collapsing) their respective run-1 identities.
            var run2Root = CodeNamespace.InitRootNamespace();
            var config2 = CreateConfig();
            var (widgetA2, widgetB2) = CreateWidgets(run2Root);
            await ILanguageRefiner.RefineAsync(config2, run2Root, cancellationToken: TestContext.Current.CancellationToken);

            widgetA2.CustomData.TryGetValue("object-id", out var idA2);
            widgetB2.CustomData.TryGetValue("object-id", out var idB2);

            Assert.NotEqual(widgetA2.Name, widgetB2.Name);
            Assert.NotEqual(idA2, idB2);
            Assert.Equal(widgetA1.Name, widgetA2.Name);
            Assert.Equal(widgetB1.Name, widgetB2.Name);
            Assert.Equal(idA1, idA2);
            Assert.Equal(idB1, idB2);
        }
        finally
        {
            System.IO.Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PersistedObjectMapKeepsQueryParameterCodeunitIdAndNameStableAcrossRunsAsync()
    {
        // Regression for: the generated top-level query-parameter codeunit (built in
        // UpdateRequestExecutorMethods/Step 6) used its own key format
        // ("namespace::parentOriginalName+methodName+Parameters") instead of going through
        // BuildObjectMapKey/the cached ALCustomDataKeys.ObjectMapKey. After BuildObjectMapKey grew a
        // content disambiguator, CollectObjectMapEntries's generic fallback recomputed a DIFFERENT key
        // for the parameter codeunit at save time than the one used to look it up on the next run
        // (it was never cached on the element), so the parameter codeunit's id/name were never
        // actually reused - a fresh id/name was minted on every single run.
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempDir);
        try
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(tempDir, "al-config.json"),
                "{\"objectMapPath\":\"obj-map.json\"}");

            GenerationConfiguration CreateConfig() => new()
            {
                Language = GenerationLanguage.AL,
                OutputPath = System.IO.Path.Combine(tempDir, "output"),
                ClientClassName = "ApiClient",
                ClientNamespaceName = "ApiSdk",
            };

            static CodeClass CreateRequestBuilderWithQueryParameters(CodeNamespace root)
            {
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
                return requestBuilder;
            }

            static CodeClass GetGeneratedParamCodeunit(CodeNamespace root) =>
                root.FindNamespaceByName("ApiSdk.users")!.Classes.Single(c => c.IsOfKind(CodeClassKind.QueryParameters) && c.Parent is CodeNamespace);

            // Run 1: establish the map.
            var run1Root = CodeNamespace.InitRootNamespace();
            var config1 = CreateConfig();
            CreateRequestBuilderWithQueryParameters(run1Root);
            await ILanguageRefiner.RefineAsync(config1, run1Root, cancellationToken: TestContext.Current.CancellationToken);

            var paramClass1 = GetGeneratedParamCodeunit(run1Root);
            paramClass1.CustomData.TryGetValue("object-id", out var paramId1);
            Assert.NotNull(paramId1);

            // Run 2: identical spec - the parameter codeunit must reuse the exact same id and name.
            var run2Root = CodeNamespace.InitRootNamespace();
            var config2 = CreateConfig();
            CreateRequestBuilderWithQueryParameters(run2Root);
            await ILanguageRefiner.RefineAsync(config2, run2Root, cancellationToken: TestContext.Current.CancellationToken);

            var paramClass2 = GetGeneratedParamCodeunit(run2Root);
            paramClass2.CustomData.TryGetValue("object-id", out var paramId2);

            Assert.Equal(paramId1, paramId2);
            Assert.Equal(paramClass1.Name, paramClass2.Name);
        }
        finally
        {
            System.IO.Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PersistedObjectMapKeepsNamingConventionPragmaOnAnAbbreviatedParameterCodeunitAsync()
    {
        // Regression for: the parameter codeunit's name-mismatch pragma (AA0215) was only ever added
        // via ALConventionService.DeduplicateName's internal fallback branches (which only fire on an
        // actual name COLLISION) - never via an explicit "does the final name differ from the base
        // name" check the way ApplyClassNameChanges does for ordinary classes/enums. So a parameter
        // codeunit whose name was merely ABBREVIATED for length (SanitizeName, no collision at all)
        // got no pragma on the very first run, and - once a persisted map existed - permanently took
        // the map-reuse branch (which never touched pragmas either), so it never got the pragma on any
        // subsequent run.
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempDir);
        try
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(tempDir, "al-config.json"),
                "{\"objectMapPath\":\"obj-map.json\"}");

            GenerationConfiguration CreateConfig() => new()
            {
                Language = GenerationLanguage.AL,
                OutputPath = System.IO.Path.Combine(tempDir, "output"),
                ClientClassName = "ApiClient",
                ClientNamespaceName = "ApiSdk",
            };

            static void CreateRequestBuilderWithLongNameAndQueryParameters(CodeNamespace root)
            {
                var ns = root.AddNamespace("ApiSdk.users");
                var requestBuilder = ns.AddClass(new CodeClass
                {
                    // Long enough that "{name}GetParameters" exceeds the 30-char AL object name limit,
                    // forcing SanitizeName to abbreviate the parameter codeunit's name (with no name
                    // collision at all - the mismatch is purely a length truncation).
                    Name = "ExtremelyLongRequestBuilderNameForTesting",
                    Kind = CodeClassKind.RequestBuilder,
                }).First();

                var queryParametersClass = requestBuilder.AddInnerClass(new CodeClass
                {
                    Name = "ExtremelyLongRequestBuilderNameForTestingGetQueryParameters",
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
                    Type = new CodeType { Name = "ExtremelyLongRequestBuilderNameForTestingGetQueryParameters", TypeDefinition = queryParametersClass },
                });
                requestBuilder.AddMethod(executor);
            }

            static CodeClass GetGeneratedParamCodeunit(CodeNamespace root) =>
                root.FindNamespaceByName("ApiSdk.users")!.Classes.Single(c => c.IsOfKind(CodeClassKind.QueryParameters) && c.Parent is CodeNamespace);

            // Run 1: fresh mint - the name is abbreviated for length, no collision involved.
            var run1Root = CodeNamespace.InitRootNamespace();
            var config1 = CreateConfig();
            CreateRequestBuilderWithLongNameAndQueryParameters(run1Root);
            await ILanguageRefiner.RefineAsync(config1, run1Root, cancellationToken: TestContext.Current.CancellationToken);

            var paramClass1 = GetGeneratedParamCodeunit(run1Root);
            Assert.NotEqual("ExtremelyLongRequestBuilderNameForTestingGetParameters", paramClass1.Name);
            paramClass1.CustomData.TryGetValue(ALCustomDataKeys.Pragmas, out var pragmas1);
            Assert.NotNull(pragmas1);
            Assert.Contains(ALCustomDataKeys.PragmaCodes.NamingConvention, (string)pragmas1!, StringComparison.Ordinal);

            // Run 2: identical spec - the parameter codeunit now hits the persisted-map reuse branch;
            // it must still carry the pragma.
            var run2Root = CodeNamespace.InitRootNamespace();
            var config2 = CreateConfig();
            CreateRequestBuilderWithLongNameAndQueryParameters(run2Root);
            await ILanguageRefiner.RefineAsync(config2, run2Root, cancellationToken: TestContext.Current.CancellationToken);

            var paramClass2 = GetGeneratedParamCodeunit(run2Root);
            Assert.Equal(paramClass1.Name, paramClass2.Name);
            paramClass2.CustomData.TryGetValue(ALCustomDataKeys.Pragmas, out var pragmas2);
            Assert.NotNull(pragmas2);
            Assert.Contains(ALCustomDataKeys.PragmaCodes.NamingConvention, (string)pragmas2!, StringComparison.Ordinal);
        }
        finally
        {
            System.IO.Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task GivesDistinctNamesToParameterCodeunitsFromDifferentlyNamespacedRequestBuildersWithTheSameNameAsync()
    {
        // Regression for: two request-builder classes that share the same original name (a common
        // pattern when a spec mirrors the same nested structure under multiple parent paths, e.g.
        // ".conversion.ava.ugl" and ".conversion.flatAva.ugl" both containing an "ugl" builder) each
        // have a "Post" executor with query parameters. UpdateRequestExecutorMethods derived the
        // parameter-codeunit name purely via SanitizeName (abbreviation only) and NEVER routed it
        // through DeduplicateName/the shared name registry, so both ended up with the byte-identical
        // AL object name (e.g. "uglRequestBuilderPostParams") even though they got distinct object
        // ids - i.e. two different codeunits with the same name, which AL will not compile (object
        // names must be unique across the whole app, unlike ids which are just unique numbers).
        static CodeClass CreateUglRequestBuilderWithPost(CodeNamespace root, string namespaceName)
        {
            var ns = root.AddNamespace(namespaceName);
            var requestBuilder = ns.AddClass(new CodeClass
            {
                Name = "uglRequestBuilder",
                Kind = CodeClassKind.RequestBuilder,
            }).First();

            var queryParametersClass = requestBuilder.AddInnerClass(new CodeClass
            {
                Name = "uglRequestBuilderPostQueryParameters",
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
                Name = "Post",
                Kind = CodeMethodKind.RequestExecutor,
                ReturnType = new CodeType { Name = "void", IsExternal = true },
            };
            executor.AddParameter(new CodeParameter
            {
                Name = "requestConfiguration",
                Kind = CodeParameterKind.RequestConfiguration,
                Type = new CodeType { Name = "uglRequestBuilderPostQueryParameters", TypeDefinition = queryParametersClass },
            });
            requestBuilder.AddMethod(executor);
            return requestBuilder;
        }

        var config = CreateConfiguration();
        CreateUglRequestBuilderWithPost(root, "ApiSdk.conversion.ava.ugl");
        CreateUglRequestBuilderWithPost(root, "ApiSdk.conversion.flatAva.ugl");

        await ILanguageRefiner.RefineAsync(config, root, cancellationToken: TestContext.Current.CancellationToken);

        var paramClasses = root.FindNamespaceByName("ApiSdk.conversion.ava.ugl")!.Classes
            .Concat(root.FindNamespaceByName("ApiSdk.conversion.flatAva.ugl")!.Classes)
            .Where(c => c.IsOfKind(CodeClassKind.QueryParameters) && c.Parent is CodeNamespace)
            .ToList();

        Assert.Equal(2, paramClasses.Count);
        Assert.NotEqual(paramClasses[0].Name, paramClasses[1].Name);

        // Object ids must also, obviously, still be distinct.
        paramClasses[0].CustomData.TryGetValue("object-id", out var id0);
        paramClasses[1].CustomData.TryGetValue("object-id", out var id1);
        Assert.NotEqual(id0, id1);
    }

    [Fact]
    public async Task PersistedObjectMapKeepsIdAndNameWhenAClassGainsAPropertyAsync()
    {
        // Headline regression for the identity-only key format: a class gaining a property must NOT
        // renumber or rename, and must NOT tombstone the old entry (there is no "old" entry - it's the
        // same object, same key, just with an updated member list).
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempDir);
        try
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(tempDir, "al-config.json"),
                "{\"objectMapPath\":\"obj-map.json\"}");
            var mapPath = System.IO.Path.Combine(tempDir, "obj-map.json");

            GenerationConfiguration CreateConfig() => new()
            {
                Language = GenerationLanguage.AL,
                OutputPath = System.IO.Path.Combine(tempDir, "output"),
                ClientClassName = "ApiClient",
                ClientNamespaceName = "ApiSdk",
            };

            // Run 1: Alpha has one property.
            var run1Root = CodeNamespace.InitRootNamespace();
            var config1 = CreateConfig();
            var alpha1 = TestHelper.CreateModelClassInModelsNamespace(config1, run1Root, "Alpha");
            alpha1.AddProperty(new CodeProperty { Name = "foo", Kind = CodePropertyKind.Custom, Type = new CodeType { Name = "string", IsExternal = true } });
            await ILanguageRefiner.RefineAsync(config1, run1Root, cancellationToken: TestContext.Current.CancellationToken);

            alpha1.CustomData.TryGetValue("object-id", out var alphaId1);
            Assert.NotNull(alphaId1);

            // Run 2: Alpha gained a second property - identity is unchanged, only its content grew.
            var run2Root = CodeNamespace.InitRootNamespace();
            var config2 = CreateConfig();
            var alpha2 = TestHelper.CreateModelClassInModelsNamespace(config2, run2Root, "Alpha");
            alpha2.AddProperty(new CodeProperty { Name = "foo", Kind = CodePropertyKind.Custom, Type = new CodeType { Name = "string", IsExternal = true } });
            alpha2.AddProperty(new CodeProperty { Name = "bar", Kind = CodePropertyKind.Custom, Type = new CodeType { Name = "string", IsExternal = true } });
            await ILanguageRefiner.RefineAsync(config2, run2Root, cancellationToken: TestContext.Current.CancellationToken);

            alpha2.CustomData.TryGetValue("object-id", out var alphaId2);

            Assert.Equal(alphaId1, alphaId2);
            Assert.Equal(alpha1.Name, alpha2.Name);

            var savedMap = ALObjectMap.LoadFromDisk(mapPath);
            var alphaEntry = Assert.Single(savedMap.Objects.Values, e => e.AssignedName == "Alpha");
            Assert.False(alphaEntry.Tombstoned);
            Assert.DoesNotContain(savedMap.Objects.Values, e => e.Tombstoned);
            Assert.Equal(2, savedMap.FormatVersion);
        }
        finally
        {
            System.IO.Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PersistedObjectMapKeepsIdAndNameWhenAnEnumGainsAnOptionAsync()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempDir);
        try
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(tempDir, "al-config.json"),
                "{\"objectMapPath\":\"obj-map.json\"}");

            GenerationConfiguration CreateConfig() => new()
            {
                Language = GenerationLanguage.AL,
                OutputPath = System.IO.Path.Combine(tempDir, "output"),
                ClientClassName = "ApiClient",
                ClientNamespaceName = "ApiSdk",
            };

            static CodeEnum CreateColorEnum(CodeNamespace root, params string[] optionNames)
            {
                var modelsNs = root.FindNamespaceByName("ApiSdk.models") ?? root.AddNamespace("ApiSdk.models");
                var codeEnum = modelsNs.AddEnum(new CodeEnum { Name = "Color" }).First();
                foreach (var name in optionNames)
                    codeEnum.AddOption(new CodeEnumOption { Name = name });
                return codeEnum;
            }

            // Run 1: Color has one option.
            var run1Root = CodeNamespace.InitRootNamespace();
            var config1 = CreateConfig();
            var color1 = CreateColorEnum(run1Root, "red");
            await ILanguageRefiner.RefineAsync(config1, run1Root, cancellationToken: TestContext.Current.CancellationToken);
            color1.CustomData.TryGetValue("object-id", out var colorId1);
            Assert.NotNull(colorId1);

            // Run 2: Color gained a second option.
            var run2Root = CodeNamespace.InitRootNamespace();
            var config2 = CreateConfig();
            var color2 = CreateColorEnum(run2Root, "red", "blue");
            await ILanguageRefiner.RefineAsync(config2, run2Root, cancellationToken: TestContext.Current.CancellationToken);
            color2.CustomData.TryGetValue("object-id", out var colorId2);

            Assert.Equal(colorId1, colorId2);
            Assert.Equal(color1.Name, color2.Name);
        }
        finally
        {
            System.IO.Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PersistedObjectMapKeepsIdAndNameWhenAClassLosesAPropertyAsync()
    {
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempDir);
        try
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(tempDir, "al-config.json"),
                "{\"objectMapPath\":\"obj-map.json\"}");

            GenerationConfiguration CreateConfig() => new()
            {
                Language = GenerationLanguage.AL,
                OutputPath = System.IO.Path.Combine(tempDir, "output"),
                ClientClassName = "ApiClient",
                ClientNamespaceName = "ApiSdk",
            };

            // Run 1: Alpha has two properties.
            var run1Root = CodeNamespace.InitRootNamespace();
            var config1 = CreateConfig();
            var alpha1 = TestHelper.CreateModelClassInModelsNamespace(config1, run1Root, "Alpha");
            alpha1.AddProperty(new CodeProperty { Name = "foo", Kind = CodePropertyKind.Custom, Type = new CodeType { Name = "string", IsExternal = true } });
            alpha1.AddProperty(new CodeProperty { Name = "bar", Kind = CodePropertyKind.Custom, Type = new CodeType { Name = "string", IsExternal = true } });
            await ILanguageRefiner.RefineAsync(config1, run1Root, cancellationToken: TestContext.Current.CancellationToken);
            alpha1.CustomData.TryGetValue("object-id", out var alphaId1);
            Assert.NotNull(alphaId1);

            // Run 2: Alpha lost "bar".
            var run2Root = CodeNamespace.InitRootNamespace();
            var config2 = CreateConfig();
            var alpha2 = TestHelper.CreateModelClassInModelsNamespace(config2, run2Root, "Alpha");
            alpha2.AddProperty(new CodeProperty { Name = "foo", Kind = CodePropertyKind.Custom, Type = new CodeType { Name = "string", IsExternal = true } });
            await ILanguageRefiner.RefineAsync(config2, run2Root, cancellationToken: TestContext.Current.CancellationToken);
            alpha2.CustomData.TryGetValue("object-id", out var alphaId2);

            Assert.Equal(alphaId1, alphaId2);
            Assert.Equal(alpha1.Name, alpha2.Name);
        }
        finally
        {
            System.IO.Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PersistedObjectMapMigratesALegacyContentSuffixedKeyWithoutRenumberingAsync()
    {
        // A hand-written v1 (formatVersion < 2) map, built with the always-on content disambiguator,
        // must resolve against the new identity-only key on the very first run under the new code -
        // no renumbering, and the legacy key must be gone (rekeyed, not tombstoned) afterwards.
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempDir);
        try
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(tempDir, "al-config.json"),
                "{\"objectMapPath\":\"obj-map.json\"}");
            var mapPath = System.IO.Path.Combine(tempDir, "obj-map.json");
            const string legacyKey = "ApiSdk.models::Alpha::foo";
            System.IO.File.WriteAllText(mapPath,
                "{\"objects\":{\"" + legacyKey + "\":{\"objectId\":50123,\"objectType\":\"codeunit\",\"assignedName\":\"Alpha\",\"tombstoned\":false}}}");

            var config = new GenerationConfiguration
            {
                Language = GenerationLanguage.AL,
                OutputPath = System.IO.Path.Combine(tempDir, "output"),
                ClientClassName = "ApiClient",
                ClientNamespaceName = "ApiSdk",
            };
            var runRoot = CodeNamespace.InitRootNamespace();
            var alpha = TestHelper.CreateModelClassInModelsNamespace(config, runRoot, "Alpha");
            alpha.AddProperty(new CodeProperty { Name = "foo", Kind = CodePropertyKind.Custom, Type = new CodeType { Name = "string", IsExternal = true } });
            alpha.AddProperty(new CodeProperty { Name = "bar", Kind = CodePropertyKind.Custom, Type = new CodeType { Name = "string", IsExternal = true } });

            await ILanguageRefiner.RefineAsync(config, runRoot, cancellationToken: TestContext.Current.CancellationToken);

            alpha.CustomData.TryGetValue("object-id", out var alphaId);
            Assert.Equal("50123", alphaId);
            Assert.Equal("Alpha", alpha.Name);

            var savedMap = ALObjectMap.LoadFromDisk(mapPath);
            Assert.Equal(2, savedMap.FormatVersion);
            Assert.False(savedMap.Objects.ContainsKey(legacyKey));
            Assert.True(savedMap.Objects.ContainsKey("ApiSdk.models::Alpha"));
            Assert.Single(savedMap.Objects);
        }
        finally
        {
            System.IO.Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PersistedObjectMapMigratesALegacyQueryParameterCodeunitKeyAsync()
    {
        // A hand-written v1 map containing both a request-builder entry and its parameter-codeunit
        // entry (in the OLD, "::params::"-less shape) must migrate both without renumbering.
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempDir);
        try
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(tempDir, "al-config.json"),
                "{\"objectMapPath\":\"obj-map.json\"}");
            var mapPath = System.IO.Path.Combine(tempDir, "obj-map.json");
            const string builderLegacyKey = "ApiSdk.users::usersRequestBuilder::pathParameters,requestAdapter,urlTemplate";
            const string paramLegacyKey = "ApiSdk.users::usersRequestBuilder::pathParameters,requestAdapter,urlTemplate::GetParameters";
            System.IO.File.WriteAllText(mapPath,
                "{\"objects\":{" +
                "\"" + builderLegacyKey + "\":{\"objectId\":60001,\"objectType\":\"codeunit\",\"assignedName\":\"usersRequestBuilder\",\"tombstoned\":false}," +
                "\"" + paramLegacyKey + "\":{\"objectId\":60002,\"objectType\":\"codeunit\",\"assignedName\":\"usersRequestBuilderGetParams\",\"tombstoned\":false}" +
                "}}");

            var config = new GenerationConfiguration
            {
                Language = GenerationLanguage.AL,
                OutputPath = System.IO.Path.Combine(tempDir, "output"),
                ClientClassName = "ApiClient",
                ClientNamespaceName = "ApiSdk",
            };
            var runRoot = CodeNamespace.InitRootNamespace();
            var ns = runRoot.AddNamespace("ApiSdk.users");
            var requestBuilder = ns.AddClass(new CodeClass
            {
                Name = "usersRequestBuilder",
                Kind = CodeClassKind.RequestBuilder,
            }).First();
            requestBuilder.AddProperty(new CodeProperty { Name = "pathParameters", Kind = CodePropertyKind.PathParameters, Type = new CodeType { Name = "string", IsExternal = true } });
            requestBuilder.AddProperty(new CodeProperty { Name = "requestAdapter", Kind = CodePropertyKind.RequestAdapter, Type = new CodeType { Name = "string", IsExternal = true } });
            requestBuilder.AddProperty(new CodeProperty { Name = "urlTemplate", Kind = CodePropertyKind.UrlTemplate, Type = new CodeType { Name = "string", IsExternal = true } });

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

            await ILanguageRefiner.RefineAsync(config, runRoot, cancellationToken: TestContext.Current.CancellationToken);

            requestBuilder.CustomData.TryGetValue("object-id", out var builderId);
            Assert.Equal("60001", builderId);

            var paramClass = runRoot.FindNamespaceByName("ApiSdk.users")!.Classes
                .Single(c => c.IsOfKind(CodeClassKind.QueryParameters) && c.Parent is CodeNamespace);
            paramClass.CustomData.TryGetValue("object-id", out var paramId);
            Assert.Equal("60002", paramId);
            Assert.Equal("usersRequestBuilderGetParams", paramClass.Name);

            var savedMap = ALObjectMap.LoadFromDisk(mapPath);
            Assert.Equal(2, savedMap.FormatVersion);
            Assert.False(savedMap.Objects.ContainsKey(builderLegacyKey));
            Assert.False(savedMap.Objects.ContainsKey(paramLegacyKey));
            Assert.True(savedMap.Objects.ContainsKey("ApiSdk.users::usersRequestBuilder"));
            Assert.True(savedMap.Objects.ContainsKey("ApiSdk.users::usersRequestBuilder::params::GetParameters"));
        }
        finally
        {
            System.IO.Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PersistedObjectMapDoesNotAdoptAnAmbiguousLegacyKeyAsync()
    {
        // Two legacy entries share the same identity prefix but neither's disambiguator matches the
        // current run's content - no adoption must occur, and a fresh id/name must be minted instead
        // of guessing.
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempDir);
        try
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(tempDir, "al-config.json"),
                "{\"objectMapPath\":\"obj-map.json\"}");
            var mapPath = System.IO.Path.Combine(tempDir, "obj-map.json");
            System.IO.File.WriteAllText(mapPath,
                """
                {"objects":{
                  "ApiSdk.models::Alpha::foo":{"objectId":70001,"objectType":"codeunit","assignedName":"Alpha","tombstoned":false},
                  "ApiSdk.models::Alpha::bar":{"objectId":70002,"objectType":"codeunit","assignedName":"Alpha2","tombstoned":false}
                }}
                """);

            var config = new GenerationConfiguration
            {
                Language = GenerationLanguage.AL,
                OutputPath = System.IO.Path.Combine(tempDir, "output"),
                ClientClassName = "ApiClient",
                ClientNamespaceName = "ApiSdk",
            };
            var runRoot = CodeNamespace.InitRootNamespace();
            var alpha = TestHelper.CreateModelClassInModelsNamespace(config, runRoot, "Alpha");
            // Current content is neither "foo" nor "bar" alone - "baz" - so neither legacy candidate's
            // disambiguator matches.
            alpha.AddProperty(new CodeProperty { Name = "baz", Kind = CodePropertyKind.Custom, Type = new CodeType { Name = "string", IsExternal = true } });

            await ILanguageRefiner.RefineAsync(config, runRoot, cancellationToken: TestContext.Current.CancellationToken);

            alpha.CustomData.TryGetValue("object-id", out var alphaId);
            Assert.NotEqual("70001", alphaId);
            Assert.NotEqual("70002", alphaId);
        }
        finally
        {
            System.IO.Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task PersistedObjectMapKeepsNamingConventionPragmaWhenMigratingALegacyKeyAsync()
    {
        // Mirrors PersistedObjectMapKeepsNamingConventionPragmaOnARenamedClassAsync, but starting from
        // a legacy-format map entry that must be migrated: the reused name still differs from the
        // schema name, so the AA0215-suppressing pragma must still be applied on the migration path.
        var tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tempDir);
        try
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(tempDir, "al-config.json"),
                "{\"objectMapPath\":\"obj-map.json\"}");
            var mapPath = System.IO.Path.Combine(tempDir, "obj-map.json");
            const string legacyKey = "ApiSdk.models::ExtremelyLongModelClassNameForTesting::foo";
            System.IO.File.WriteAllText(mapPath,
                "{\"objects\":{\"" + legacyKey + "\":{\"objectId\":80001,\"objectType\":\"codeunit\",\"assignedName\":\"ExtremelyLongModelClassNameFor\",\"tombstoned\":false}}}");

            var config = new GenerationConfiguration
            {
                Language = GenerationLanguage.AL,
                OutputPath = System.IO.Path.Combine(tempDir, "output"),
                ClientClassName = "ApiClient",
                ClientNamespaceName = "ApiSdk",
            };
            var runRoot = CodeNamespace.InitRootNamespace();
            var model = TestHelper.CreateModelClassInModelsNamespace(config, runRoot, "ExtremelyLongModelClassNameForTesting");
            model.AddProperty(new CodeProperty { Name = "foo", Kind = CodePropertyKind.Custom, Type = new CodeType { Name = "string", IsExternal = true } });

            await ILanguageRefiner.RefineAsync(config, runRoot, cancellationToken: TestContext.Current.CancellationToken);

            model.CustomData.TryGetValue("object-id", out var modelId);
            Assert.Equal("80001", modelId);
            Assert.Equal("ExtremelyLongModelClassNameFor", model.Name);
            model.CustomData.TryGetValue(ALCustomDataKeys.Pragmas, out var pragmas);
            Assert.NotNull(pragmas);
            Assert.Contains(ALCustomDataKeys.PragmaCodes.NamingConvention, (string)pragmas!, StringComparison.Ordinal);
        }
        finally
        {
            System.IO.Directory.Delete(tempDir, true);
        }
    }
}

