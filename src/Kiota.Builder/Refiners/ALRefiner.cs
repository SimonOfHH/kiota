using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kiota.Builder.CodeDOM;
using Kiota.Builder.Configuration;
using Kiota.Builder.Extensions;
using Kiota.Builder.Writers.AL;

namespace Kiota.Builder.Refiners;

public class ALRefiner : CommonLanguageRefiner, ILanguageRefiner
{
    private static ALReservedNamesProvider ReservedNamesProvider { get; } = new();
    public ALRefiner(GenerationConfiguration configuration) : base(configuration) { }
    public override Task RefineAsync(CodeNamespace generatedCode, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var objectIdProvider = InitializeObjectIdProvider(_configuration);
            cancellationToken.ThrowIfCancellationRequested();
            ModifyNamespaces(generatedCode);
            ConvertUnionTypesToWrapper(generatedCode, // copied from CSharpRefiner, slightly modified
                _configuration.UsesBackingStore,
                static s => s,
                true,
                "",
                ""
            );
            RemoveUnusedMethods(generatedCode);
            RemoveAdditionalDataProperty(generatedCode); // we don't support additional data in AL (yet?)
            RemoveNotSupportedParameters(generatedCode);
            MarkMethodsToSkip(generatedCode);
            SetObjectIdsOnClassesAndEnums(generatedCode, objectIdProvider);
            ModifyClassNames(generatedCode, _configuration);
            ModifyEnumNames(generatedCode);
            cancellationToken.ThrowIfCancellationRequested();
            UpdateApiClientClass(generatedCode, _configuration);
            cancellationToken.ThrowIfCancellationRequested();
            MovePropertiesToMethods(generatedCode);
            ModifyGetterSetterMethodName(generatedCode);
            cancellationToken.ThrowIfCancellationRequested();
            UpdateModelClasses(generatedCode);
            UpdateRequestBuilderClasses(generatedCode, _configuration);
            UpdateRequestExecutorMethods(generatedCode);
            cancellationToken.ThrowIfCancellationRequested();
            AddObjectProperties(generatedCode);
            cancellationToken.ThrowIfCancellationRequested();
            AddAppJsonAsCodeFunction(generatedCode, _configuration);
            ModifyOverloadMethodNames(generatedCode);
            UpdateMethodParameters(generatedCode);
        }, cancellationToken);
    }
    protected static ALObjectIdProvider InitializeObjectIdProvider(GenerationConfiguration configuration)
    {
        var startRange = ALConfigurationHelper.GetObjectIdRangeStart(configuration);
        var objectIdProvider = new ALObjectIdProvider();
        if (startRange > 0)
            objectIdProvider.StartRange = startRange;
        return objectIdProvider;
    }
    protected static void UpdateApiClientClass(CodeElement currentElement, GenerationConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (currentElement is CodeClass currentClass)
        {
            if (currentClass.Name.Equals(configuration.ClientClassName, StringComparison.OrdinalIgnoreCase))
            {
                var baseUrl = ALConfigurationHelper.GetBaseUrl(currentElement, configuration);
                ArgumentNullException.ThrowIfNull(baseUrl);
                currentClass.AddUsing(new CodeUsing { Name = "SimonOfHH.Kiota.Client" });
                currentClass.AddUsing(new CodeUsing { Name = "SimonOfHH.Kiota.Definitions" });
                currentClass.StartBlock.AddImplements(new CodeType { Name = "Kiota IApiClient SOHH", IsExternal = true });
                currentClass.AddVariable(ALVariableProvider.GetGlobalVariable("ReqConfig", new CodeType { Name = "codeunit SimonOfHH.Kiota.Client.\"Kiota ClientConfig SOHH\"", IsExternal = true }, "").ToVariable());
                currentClass.AddVariable(new ALVariable("APIAuthorization", new CodeType { Name = "codeunit SimonOfHH.Kiota.Client.\"Kiota API Authorization SOHH\"", IsExternal = true }, "", ""));
                currentClass.AddVariable(new ALVariable("StoredResponse", new CodeType { Name = "codeunit System.RestClient.\"Http Response Message\"", IsExternal = true }, "", ""));
                currentClass.AddVariable(new ALVariable("BaseUrlLbl", new CodeType { Name = "Label" }, "", baseUrl, true));
                currentClass.AddVariable(new ALVariable("ConfigSet", new CodeType { Name = "Boolean" }, "", ""));
                currentClass.AddVariable(new ALVariable("AuthorizationNotInitializedErr", new CodeType { Name = "Label" }, "", "Authorization is uninitialized."));
                currentClass.AddMethod(ALVariableProvider.GetApiClientInitializerMethod(currentClass));
                currentClass.AddMethod(ALVariableProvider.GetApiClientConfigurationMethod(currentClass));
                currentClass.AddMethod(ALVariableProvider.GetApiClientConfigurationWithParameterMethod(currentClass));
                currentClass.AddMethod(ALVariableProvider.GetApiClientDefaultConfigurationMethod(currentClass));
                currentClass.AddMethod(ALVariableProvider.GetDefaultIApiClientMethods(currentClass).ToArray());
                currentClass.AddCustomProperty("client-class", "true");
            }
        }
        CrawlTree(currentElement, childElement => UpdateApiClientClass(childElement, configuration));
    }
    protected static void ModifyNamespaces(CodeElement currentElement)
    {
        if (currentElement is CodeNamespace currentNamespace)
        {
            // we don't support namespaces starting with an underscore in AL
            if (currentNamespace.Name.Contains('_'.ToString(), StringComparison.CurrentCulture))
            {
                var splitParts = currentNamespace.Name.Split('.', StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < splitParts.Length; i++)
                    if (splitParts[i].StartsWith('_'.ToString(), StringComparison.CurrentCulture))
                        splitParts[i] = splitParts[i].Insert(0, "u");
                currentNamespace.Name = string.Join('.', splitParts);
            }
        }
        CrawlTree(currentElement, ModifyNamespaces);
    }
    protected static void SetObjectIdsOnClassesAndEnums(CodeElement currentElement, ALObjectIdProvider objectIdProvider)
    {
        ArgumentNullException.ThrowIfNull(objectIdProvider);
        if (currentElement is CodeClass currentClass)
        {
            currentClass.SetObjectId(objectIdProvider.GetNextCodeunitId());
        }
        if (currentElement is CodeEnum currentEnum)
        {
            currentEnum.SetObjectId(objectIdProvider.GetNextEnumId());
        }
        CrawlTree(currentElement, childElement => SetObjectIdsOnClassesAndEnums(childElement, objectIdProvider));
    }
    /// <summary>
    // In AL object names have some limits; they need to be unique even across different namespaces (as long as they are in the same app module)
    // and they need to be shorter than 30 characters. That's why we need to modify the class names here to comply with this
    /// </summary>
    protected static void ModifyClassNames(CodeElement currentElement, GenerationConfiguration configuration)
    {
        int maxLength = 30 - ALConfigurationHelper.GetPrefixAndSuffixLength(configuration);
        if (currentElement is CodeClass currentClass)
        {
            var occurences = ALConventionService.CountClassNameOccurences(currentClass, currentClass.Name);
            if ((occurences <= 1) && (currentClass.Name.Length <= maxLength))
                return; // no need to modify the class name
            var newName = currentClass.Name;
            currentClass.AddCustomProperty("original-name", currentClass.Name);
            if (newName.Length < maxLength)
                newName = AppendNumberToClassName(currentClass, newName, occurences, maxLength);
            if (CanReturnWithNewNameSet(currentClass, newName, configuration))
                return; // we can return here, since the name is unique now
            while (((newName.Length > maxLength) && ALConventionService.CanAbbreviate(newName)) || (ALConventionService.CanAbbreviate(newName) && (occurences > 0)))
            {
                newName = ALConventionService.AbbreviateName(newName);
                occurences = ALConventionService.CountClassNameOccurences(currentClass, newName);
            }
            if (newName.Length > maxLength)
                newName = newName.Substring(0, maxLength);
            if (CanReturnWithNewNameSet(currentClass, newName, configuration))
                return; // we can return here, since the name is unique now
            var namespacePrefix = GetNamespacePrefixForClass(currentClass, newName);
            newName = $"{newName}{namespacePrefix.ToFirstCharacterUpperCase()}"; // we append the last part of the namespace to the class name
            if (newName.Length > maxLength)
                newName = newName.Substring(0, maxLength);
            if (CanReturnWithNewNameSet(currentClass, newName, configuration))
                return; // we can return here, since the name is unique now
            // if we still have more than one occurence, we cut away another character and just append a number
            newName = AppendNumberToClassName(currentClass, newName, occurences, maxLength);
            SetNewName(currentClass, newName, configuration);
        }
        CrawlTree(currentElement, childElement => ModifyClassNames(childElement, configuration));
    }
    private static bool CanReturnWithNewNameSet(CodeClass currentClass, string currentName, GenerationConfiguration configuration)
    {
        int occurences = ALConventionService.CountClassNameOccurences(currentClass, currentName);
        if (occurences > 0)
            return false;
        SetNewName(currentClass, currentName, configuration);
        return true;
    }
    private static void SetNewName(CodeClass currentClass, string currentName, GenerationConfiguration configuration)
    {
        currentName = $"{ALConfigurationHelper.GetObjectPrefix(configuration)}{currentName}{ALConfigurationHelper.GetObjectSuffix(configuration)}";
        currentClass.SetPragmas(new System.Collections.ObjectModel.Collection<string> { "AA0215" });
        currentClass.AddToDocumentation($"This class was renamed from {currentClass.Name} to {currentName} to comply with AL naming conventions.");
        currentClass.Name = currentName;
    }
    private static string AppendNumberToClassName(CodeClass currentClass, string currentName, int occurences, int maxLength)
    {
        var number = 0;
        while (occurences > 0)
        {
            number++;
            var numberLength = number.ToString(CultureInfo.InvariantCulture).Length;
            if (currentName.Length >= maxLength)
                currentName = currentName.Substring(0, currentName.Length - numberLength);
            occurences = ALConventionService.CountClassNameOccurences(currentClass, currentName + number);
        }
        currentName = currentName + number;
        return currentName;
    }
    private static String GetNamespacePrefixForClass(CodeClass currentClass, string currentName)
    {
        ArgumentNullException.ThrowIfNull(currentClass);
        ArgumentNullException.ThrowIfNull(currentName);
        var parentNamespace = GetNamespaceForClassForPrefix(currentClass);
        var namespaceParts = parentNamespace.Name.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var namespacePrefix = namespaceParts.Last();
        if ((currentName.Length + namespacePrefix.Length) > 30)
            namespacePrefix = ALConventionService.AbbreviateName(namespacePrefix);
        return namespacePrefix;
    }
    private static CodeNamespace GetNamespaceForClassForPrefix(CodeClass currentClass)
    {
        ArgumentNullException.ThrowIfNull(currentClass);
        var classNamespace = currentClass.GetImmediateParentOfType<CodeNamespace>();
        if (classNamespace is null)
            throw new InvalidOperationException($"The provided code class {currentClass.Name} does not have a parent namespace.");
        var parentNamespace = classNamespace.Parent?.GetImmediateParentOfType<CodeNamespace>();
        if (parentNamespace is null)
            parentNamespace = classNamespace; // if there is no parent namespace, we use the current namespace
        return parentNamespace;
    }
    protected static void ModifyEnumNames(CodeElement currentElement)
    {
        if (currentElement is CodeEnum currentEnum)
        {
            var occurences = ALConventionService.CountEnumNameOccurences(currentEnum, currentEnum.Name);
            if (occurences <= 1)
                return; // no need to modify the class name
            var classNamespace = currentEnum.GetImmediateParentOfType<CodeNamespace>();
            if (classNamespace is null)
                throw new InvalidOperationException($"The provided enum {currentEnum.Name} does not have a parent namespace.");
            var namespaceParts = classNamespace.Name.Split('.', StringSplitOptions.RemoveEmptyEntries);
            var newName = $"{namespaceParts.Last().ToFirstCharacterUpperCase()}{currentEnum.Name}";
            currentEnum.Name = newName;
        }
        CrawlTree(currentElement, ModifyEnumNames);
    }
    /// <summary>
    /// This method is used to remove unused methods from the current element.
    /// </summary>
    /// <param name="currentElement"></param>
    protected static void RemoveUnusedMethods(CodeElement currentElement)
    {
        if (currentElement is CodeClass codeClass)
        {
            codeClass.RemoveMethodByKinds(new[] { CodeMethodKind.Serializer, CodeMethodKind.Deserializer }); // we want to explicitly remove these methods and create our own
        }
        CrawlTree(currentElement, RemoveUnusedMethods);
    }

    protected static void AddAppJsonAsCodeFunction(CodeElement currentElement, GenerationConfiguration configuration)
    {
        if (currentElement is CodeNamespace currentNamespace)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            if (currentNamespace.Namespaces.First().Name.Contains('.', StringComparison.CurrentCulture))
                return;
            var childTypeCount = currentNamespace.GetTypeCounts();
            var function = new CodeFunction(new CodeMethod
            {
                Name = "AppJson",
                Access = AccessModifier.Public,
                IsStatic = true,
                Parent = new CodeClass
                {
                    Name = "Test",
                    Access = AccessModifier.Internal,
                    Parent = currentNamespace
                },
                ReturnType = new CodeType
                {
                    Name = "Text"
                },
            });
            function.AddUsing(new CodeUsing { Name = $"Name={configuration.ClientNamespaceName}" });
            function.AddUsing(new CodeUsing { Name = $"Publisher={ALConfigurationHelper.GetAppPublisherName(configuration)}" });
            function.AddUsing(new CodeUsing { Name = $"Brief={ALConfigurationHelper.GetAppBrief(configuration)}" });
            function.AddUsing(new CodeUsing { Name = $"Description={ALConfigurationHelper.GetAppDescription(configuration)}" });
            function.AddUsing(new CodeUsing { Name = $"Version={ALConfigurationHelper.GetAppVersion(configuration)}" });
            function.AddUsing(new CodeUsing { Name = $"IDRangeStart={ALConfigurationHelper.GetObjectIdRangeStart(configuration)}" });
            function.AddUsing(new CodeUsing { Name = $"IDRangeEnd={ALConfigurationHelper.GetObjectIdRangeEnd(configuration)}" });
            currentNamespace.AddFunction(function);
        }
        CrawlTree(currentElement, childElement => AddAppJsonAsCodeFunction(childElement, configuration));
    }
    /// <summary>
    /// This method is used to update the method parameters of the current element.
    /// It checks if the parameter name is a reserved name and adds an underscore prefix if it is.
    /// </summary>
    /// <param name="currentElement"></param>
    protected static void UpdateMethodParameters(CodeElement currentElement)
    {
        if (currentElement is CodeMethod currentMethod)
        {
            foreach (var parameter in currentMethod.Parameters)
            {
                if (ReservedNamesProvider.ReservedNames.Contains(parameter.Name, StringComparer.OrdinalIgnoreCase))
                    parameter.Name = $"{parameter.Name}_";
            }
        }
        CrawlTree(currentElement, UpdateMethodParameters);
    }

    // Since we can't add methods with the same name to the DOM, in a previous step we added a "-overload" suffix to the method name.
    // This method removes the suffix from the method name.
    protected static void ModifyOverloadMethodNames(CodeElement currentElement)
    {
        if (currentElement is CodeMethod currentMethod)
        {
            if (currentMethod.Name.Contains("-overload", StringComparison.CurrentCulture))
                currentMethod.Name = currentMethod.Name.Replace("-overload", "", StringComparison.CurrentCulture);
        }
        CrawlTree(currentElement, ModifyOverloadMethodNames);
    }
    protected static void RemoveAdditionalDataProperty(CodeElement currentElement)
    {
        if (currentElement is CodeClass currentClass)
        {
            var additionalDataProperty = currentClass.Properties.FirstOrDefault(x => x.IsOfKind(CodePropertyKind.AdditionalData));
            if (additionalDataProperty != null)
                currentClass.RemoveChildElement(additionalDataProperty);
        }
        CrawlTree(currentElement, RemoveAdditionalDataProperty);
    }
    protected static void ConvertUnionTypesToWrapper(CodeElement currentElement)
    {

    }
    /// <summary>
    /// In AL, properties are not supported. We need to move them to getter/setter methods.
    /// </summary>
    /// <param name="currentElement"></param>
    protected static void MovePropertiesToMethods(CodeElement currentElement)
    {
        if (currentElement is CodeClass currentClass)
        {
            var propertiesToMove = currentClass.Properties
                                                .Where(x => x.IsOfKind(CodePropertyKind.Custom) && x.GetCustomProperty("locked") != "true")
                                                .ToList();
            foreach (var property in propertiesToMove)
            {
                currentClass.RemoveChildElement(property);
                currentClass.AddMethod(property.ToGetterCodeMethod());
                currentClass.AddMethod(property.ToSetterCodeMethod());
            }
        }
        CrawlTree(currentElement, MovePropertiesToMethods);
    }
    protected static void ModifyGetterSetterMethodName(CodeElement currentElement)
    {
        if (currentElement is CodeMethod currentMethod)
        {
            if (currentMethod.IsGetterMethod())
                currentMethod.Name = currentMethod.Name.Replace("Get-", String.Empty, StringComparison.CurrentCulture);
            if (currentMethod.IsSetterMethod())
                currentMethod.Name = currentMethod.Name.Replace("Set-", String.Empty, StringComparison.CurrentCulture);
        }
        CrawlTree(currentElement, ModifyGetterSetterMethodName);
    }
    protected static void RemoveNotSupportedParameters(CodeElement currentElement)
    {
        if (currentElement is CodeMethod currentMethod)
        {
            currentMethod.RemoveParametersByKind(new[] { CodeParameterKind.Cancellation, CodeParameterKind.RequestConfiguration });
        }
        CrawlTree(currentElement, RemoveNotSupportedParameters);
    }
    protected static void MarkMethodsToSkip(CodeElement currentElement)
    {
        if (currentElement is CodeMethod currentMethod)
        {
            if (currentMethod.IsOfKind(CodeMethodKind.ClientConstructor, CodeMethodKind.RawUrlBuilder, CodeMethodKind.RawUrlConstructor, CodeMethodKind.RequestGenerator, CodeMethodKind.Constructor, CodeMethodKind.Factory))
            {
                currentMethod.AddCustomProperty("skip", "true");
            }
        }
        CrawlTree(currentElement, MarkMethodsToSkip);
    }
    protected static void AddObjectProperties(CodeElement currentElement)
    {
        if (currentElement is CodeClass currentClass)
        {
            var defaults = ALVariableProvider.GetDefaultObjectProperties(currentClass).ToArray();
            if (defaults.Length != 0)
                currentClass.AddProperty(defaults);
        }
        if (currentElement is CodeEnum currentEnum)
        {
            var defaults = ALVariableProvider.GetDefaultObjectProperties(currentEnum).ToArray();
            if (defaults.Length != 0)
                currentEnum.AddOption(defaults);
        }
        CrawlTree(currentElement, AddObjectProperties);
    }
    protected static void UpdateModelClasses(CodeElement currentElement)
    {
        if (currentElement is CodeClass currentClass)
        {
            if (currentClass.Kind != CodeClassKind.Model)
                return;
            currentClass.AddDefaultImplements();
            currentClass.RemoveInherits();
            currentClass.AddProperty(ALVariableProvider.GetDefaultGlobals(currentClass).ToArray());
            currentClass.AddUsing(new CodeUsing { Name = "SimonOfHH.Kiota.Definitions" });
            currentClass.AddUsing(new CodeUsing { Name = "SimonOfHH.Kiota.Utilities" });
            currentClass.AddMethod(ALVariableProvider.GetDefaultModelCodeunitMethods(currentClass).ToArray());
        }
        CrawlTree(currentElement, UpdateModelClasses);
    }
    protected static void UpdateRequestExecutorMethods(CodeElement currentElement)
    {
        if (currentElement is CodeMethod method)
        {
            if (method.IsOfKind(CodeMethodKind.RequestExecutor))
            {
                if (method.HttpMethod is not null)
                {
                    var conventionService = new ALConventionService();
                    if (conventionService.IsCodeunitType(method.ReturnType.GetTypeFromBase()))
                        method.AddCustomProperty("return-variable-name", "Target");
                    method.AddParameter(ALVariableProvider.GetLocalVariableP("RequestHandler", new CodeType { Name = "codeunit \"Kiota RequestHandler SoHH\"", IsExternal = true }, ""));
                }
                var codeClass = (CodeClass?)method.Parent;
                if (codeClass is not null)
                {
                    var requestConf = codeClass.InnerClasses.FirstOrDefault(c => c.Kind == CodeClassKind.QueryParameters && c.Name == $"{codeClass.Name}{method.Name}QueryParameters");
                    if (requestConf is not null)
                    {
                        foreach (var prop in requestConf.Properties)
                        {
                            method.AddParameter(prop.ToCodeParameter());
                        }
                    }
                }
            }
        }
        CrawlTree(currentElement, UpdateRequestExecutorMethods);
    }
    protected static void UpdateRequestBuilderClasses(CodeElement currentElement, GenerationConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var clientNamespace = ALConfigurationHelper.GetClientNamespace(currentElement, configuration);
        var modelNameSpace = ALConfigurationHelper.GetModelNamespace(currentElement, configuration);
        if (currentElement is CodeClass currentClass)
        {
            if (currentClass.Kind != CodeClassKind.RequestBuilder)
                return;
            currentClass.AddUsing(new CodeUsing { Name = modelNameSpace.Name });
            currentClass.AddUsing(new CodeUsing { Name = "SimonOfHH.Kiota.Client" });
            if (currentClass.Name != "ApiClient")
            {
                currentClass.AddVariable(ALVariableProvider.GetGlobalVariable("ReqConfig", new CodeType { Name = "codeunit SimonOfHH.Kiota.Client.\"Kiota ClientConfig SOHH\"", IsExternal = true }, "").ToVariable());
                currentClass.AddMethod(ALVariableProvider.GetSetConfigurationMethod(currentClass));
            }
            if (IsIndexerClass(currentClass, out CodeIndexer? indexer))
            {
                currentClass.AddVariable(ALVariableProvider.GetGlobalVariable("Identifier", new CodeType { TypeDefinition = indexer?.IndexParameter.Type }, "").ToVariable());
                currentClass.AddMethod(ALVariableProvider.GetIndexerClassSetIdentifierMethod(currentClass, indexer));
            }
            if (currentClass.Indexer is not null)
            {
                currentClass.AddMethod(currentClass.Indexer.ToCodeMethod());
                currentClass.AddUsing(new CodeUsing { Name = currentClass.Indexer.ReturnType.GetNamespaceName() });
            }
            foreach (var property in currentClass.Properties.Where(p => p.IsOfKind(CodePropertyKind.RequestBuilder)))
            {
                var propertyTypeNamespace = ((CodeType)property.Type).TypeDefinition?.GetImmediateParentOfType<CodeNamespace>();
                ArgumentNullException.ThrowIfNull(propertyTypeNamespace);
                if (!currentClass.Usings.Any(x => x.Name.Equals(propertyTypeNamespace.Name, StringComparison.OrdinalIgnoreCase)))
                    currentClass.AddUsing(new CodeUsing { Name = propertyTypeNamespace.Name });
                currentClass.RemoveChildElement(property);
                currentClass.AddMethod(property.ToGetterCodeMethod());
            }
        }
        CrawlTree(currentElement, childElement => UpdateRequestBuilderClasses(childElement, configuration));
    }
    private static bool IsIndexerClass(CodeClass currentClass, out CodeIndexer? indexer)
    {
        indexer = null;
        if (currentClass.Kind != CodeClassKind.RequestBuilder)
            return false;
        if (currentClass.Parent is null)
            return false;
        if (currentClass.Parent.Parent is null) // first parent is the <item>namespace, second parent is the actual namespace
            return false;
        foreach (var child in currentClass.Parent.Parent.GetChildElements(true))
        {
            if (child is CodeClass codeClass)
            {
                if (codeClass.Indexer != null)
                {
                    if (((CodeType)codeClass.Indexer.ReturnType).TypeDefinition == currentClass)
                    {
                        indexer = codeClass.Indexer;
                        return true;
                    }
                }
            }
        }
        return false;
    }
}
