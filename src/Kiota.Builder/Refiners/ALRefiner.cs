using System;
using System.Collections.Generic;
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
    public ALRefiner(GenerationConfiguration configuration) : base(configuration) { }

    public override Task RefineAsync(CodeNamespace generatedCode, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var alConfig = ALConfiguration.LoadFromDisk(_configuration.OutputPath);
            var objectIdProvider = new ALObjectIdProvider(alConfig.ObjectIdRangeStart);
            var conventionService = new ALConventionService(alConfig);

            cancellationToken.ThrowIfCancellationRequested();

            // Step 1: Pre-processing
            ModifyNamespaces(generatedCode);
            ConvertUnionTypesToWrapper(generatedCode, false, static s => s, true);
            ReplaceIndexersByMethodsWithParameter(generatedCode, false,
                static s => "ById", static s => "ById", GenerationLanguage.AL);
            RemoveCancellationParameter(generatedCode);

            // Step 2: AL-specific method removal/recreation
            RemoveUnusedMethods(generatedCode);
            RemoveAdditionalDataProperty(generatedCode);
            RemoveNotSupportedParameters(generatedCode);
            MarkMethodsToSkip(generatedCode);

            // Step 3: Name management
            SetObjectIdsOnClassesAndEnums(generatedCode, objectIdProvider);
            ModifyClassNames(generatedCode, alConfig, conventionService);
            ModifyEnumNames(generatedCode, alConfig, conventionService);

            // Step 4: Property → Method conversion
            MovePropertiesToMethods(generatedCode, alConfig);
            ModifyGetterSetterMethodName(generatedCode);

            // Step 5: Class augmentation
            UpdateApiClientClass(generatedCode, alConfig, conventionService, _configuration);
            UpdateModelClasses(generatedCode, alConfig, conventionService);
            UpdateRequestBuilderClasses(generatedCode, alConfig, conventionService);

            // Step 6: Request executor enhancement
            UpdateRequestExecutorMethods(generatedCode, alConfig, conventionService, objectIdProvider);

            // Step 7: Interface generation
            AddCodeInterfacesForInheritedTypes(generatedCode, alConfig);

            // Step 8: Manifest and finalization
            AddAppJsonAsCodeFunction(generatedCode, alConfig, _configuration);
            ModifyOverloadMethodNames(generatedCode);
            UpdateMethodParameters(generatedCode);
            AssignPragmas(generatedCode);

        }, cancellationToken);
    }

    #region Step 1: ModifyNamespaces
    private static void ModifyNamespaces(CodeElement generatedCode)
    {
        DeepCrawlTree(generatedCode, element =>
        {
            if (element is CodeNamespace ns && ns.Name.Split('.').Any(s => s.StartsWith('_')))
            {
                ns.Name = string.Join('.', ns.Name.Split('.').Select(s =>
                    s.StartsWith('_') ? "u" + s.TrimStart('_') : s));
            }
        });
    }
    #endregion

    #region Step 2: Remove/Mark methods
    private static void RemoveUnusedMethods(CodeElement generatedCode)
    {
        DeepCrawlTree(generatedCode, element =>
        {
            if (element is CodeMethod method &&
                method.IsOfKind(CodeMethodKind.Serializer, CodeMethodKind.Deserializer) &&
                method.Parent is CodeClass parentClass)
            {
                parentClass.RemoveChildElement(method);
            }
        });
    }

    private static void RemoveAdditionalDataProperty(CodeElement generatedCode)
    {
        DeepCrawlTree(generatedCode, element =>
        {
            if (element is CodeProperty prop &&
                prop.Kind == CodePropertyKind.AdditionalData &&
                prop.Parent is CodeClass parentClass)
            {
                parentClass.RemoveChildElement(prop);
            }
        });
    }

    private static void RemoveNotSupportedParameters(CodeElement generatedCode)
    {
        DeepCrawlTree(generatedCode, element =>
        {
            if (element is CodeMethod method)
            {
                var toRemove = method.Parameters
                    .Where(p => p.Kind == CodeParameterKind.Cancellation ||
                                p.Kind == CodeParameterKind.RequestConfiguration)
                    .ToList();
                foreach (var param in toRemove)
                    method.RemoveParametersByKind(param.Kind);
            }
        });
    }

    private static void MarkMethodsToSkip(CodeElement generatedCode)
    {
        DeepCrawlTree(generatedCode, element =>
        {
            if (element is CodeMethod method &&
                method.IsOfKind(CodeMethodKind.ClientConstructor, CodeMethodKind.RawUrlBuilder,
                    CodeMethodKind.RawUrlConstructor, CodeMethodKind.RequestGenerator,
                    CodeMethodKind.Constructor, CodeMethodKind.Factory))
            {
                method.CustomData["skip"] = "true";
            }
        });
    }
    #endregion

    #region Step 3: Object IDs and Name Management
    private static void SetObjectIdsOnClassesAndEnums(CodeElement generatedCode, ALObjectIdProvider objectIdProvider)
    {
        DeepCrawlTree(generatedCode, element =>
        {
            if (element is CodeClass c)
            {
                var id = objectIdProvider.GetNextCodeunitId().ToString(CultureInfo.InvariantCulture);
                c.CustomData["object-id"] = id;
            }
            else if (element is CodeEnum e)
            {
                var id = objectIdProvider.GetNextEnumId().ToString(CultureInfo.InvariantCulture);
                e.CustomData["object-id"] = id;
            }
        });
    }

    private static void ModifyClassNames(CodeElement generatedCode, ALConfiguration alConfig, ALConventionService conventionService)
    {
        // Collect all class names to detect duplicates
        var classNames = new Dictionary<string, List<CodeClass>>(StringComparer.OrdinalIgnoreCase);
        DeepCrawlTree(generatedCode, element =>
        {
            if (element is CodeClass c)
            {
                if (!classNames.ContainsKey(c.Name))
                    classNames[c.Name] = new List<CodeClass>();
                classNames[c.Name].Add(c);
            }
        });

        var maxLength = 30 - alConfig.ObjectPrefix.Length - alConfig.ObjectSuffix.Length;
        if (maxLength <= 0) maxLength = 30;

        DeepCrawlTree(generatedCode, element =>
        {
            if (element is CodeClass c)
            {
                var originalName = c.Name;
                var hasDuplicate = classNames.TryGetValue(originalName, out var list) && list!.Count > 1;

                if (!hasDuplicate && originalName.Length <= maxLength)
                {
                    if (!string.IsNullOrEmpty(alConfig.ObjectPrefix) || !string.IsNullOrEmpty(alConfig.ObjectSuffix))
                        c.Name = $"{alConfig.ObjectPrefix}{c.Name}{alConfig.ObjectSuffix}";
                    return;
                }

                // Need abbreviation/deduplication
                var processedName = conventionService.SanitizeName(originalName, c, 30);

                // Get parent namespace segment for deduplication
                string? parentSegment = null;
                try
                {
                    var ns = c.GetImmediateParentOfType<CodeNamespace>();
                    var segments = ns.Name.Split('.');
                    parentSegment = segments.Length > 0 ? segments[^1] : null;
                }
                catch (InvalidOperationException) { }

                processedName = conventionService.DeduplicateName(processedName, c, parentSegment, 30);

                c.Name = $"{alConfig.ObjectPrefix}{processedName}{alConfig.ObjectSuffix}";
                if (!c.Name.Equals($"{alConfig.ObjectPrefix}{originalName}{alConfig.ObjectSuffix}", StringComparison.Ordinal))
                {
                    if (!c.CustomData.ContainsKey("original-name"))
                        c.CustomData["original-name"] = originalName;
                }
            }
        });
    }

    private static void ModifyEnumNames(CodeElement generatedCode, ALConfiguration alConfig, ALConventionService conventionService)
    {
        var enumNames = new Dictionary<string, List<CodeEnum>>(StringComparer.OrdinalIgnoreCase);
        DeepCrawlTree(generatedCode, element =>
        {
            if (element is CodeEnum e)
            {
                if (!enumNames.ContainsKey(e.Name))
                    enumNames[e.Name] = new List<CodeEnum>();
                enumNames[e.Name].Add(e);
            }
        });

        DeepCrawlTree(generatedCode, element =>
        {
            if (element is CodeEnum e)
            {
                var originalName = e.Name;
                var hasDuplicate = enumNames.TryGetValue(originalName, out var list) && list!.Count > 1;

                if (hasDuplicate)
                {
                    string? parentSegment = null;
                    try
                    {
                        var ns = e.GetImmediateParentOfType<CodeNamespace>();
                        var segments = ns.Name.Split('.');
                        parentSegment = segments.Length > 0 ? segments[^1] : null;
                    }
                    catch (InvalidOperationException) { }

                    if (!string.IsNullOrEmpty(parentSegment))
                    {
                        e.Name = parentSegment.ToFirstCharacterUpperCase() + e.Name;
                        if (!e.CustomData.ContainsKey("original-name"))
                            e.CustomData["original-name"] = originalName;
                    }
                }

                e.Name = $"{alConfig.ObjectPrefix}{e.Name}{alConfig.ObjectSuffix}";
            }
        });
    }
    #endregion

    #region Step 4: Property → Method Conversion
    private static void MovePropertiesToMethods(CodeElement generatedCode, ALConfiguration alConfig)
    {
        DeepCrawlTree(generatedCode, element =>
        {
            if (element is CodeClass codeClass)
            {
                var properties = codeClass.Properties
                    .Where(p => p.Kind == CodePropertyKind.Custom &&
                                (!p.CustomData.TryGetValue("locked", out var locked) || !locked.Equals("true", StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                foreach (var prop in properties)
                {
                    codeClass.RemoveChildElement(prop);
                    var getter = ToGetterCodeMethod(prop, alConfig);
                    var setter = ToSetterCodeMethod(prop, alConfig);
                    codeClass.AddMethod(getter);
                    codeClass.AddMethod(setter);
                }
            }
        });
    }

    private static CodeMethod ToGetterCodeMethod(CodeProperty property, ALConfiguration alConfig)
    {
        var method = new CodeMethod
        {
            Name = $"Get-{property.Name.ToFirstCharacterUpperCase()}",
            Kind = CodeMethodKind.Getter,
            ReturnType = (CodeTypeBase)property.Type.Clone(),
            Access = AccessModifier.Public,
            Documentation = (CodeDocumentation)property.Documentation.Clone(),
            Parent = property.Parent,
        };
        method.SimpleName = property.Name.ToFirstCharacterUpperCase();
        method.CustomData["method-type"] = "Getter";
        method.CustomData["source"] = "from property";
        if (!string.IsNullOrEmpty(property.SerializationName))
            method.CustomData["serialization-name"] = property.SerializationName;

        // Check if this is a request builder property
        if (property.Type is CodeType ct && ct.TypeDefinition is CodeClass targetClass &&
            targetClass.IsOfKind(CodeClassKind.RequestBuilder))
        {
            method.Kind = CodeMethodKind.RequestBuilderBackwardCompatibility;
            method.CustomData["source"] = "from request-builder";
            method.CustomData["sorting-value"] = "99";
            method.CustomData["return-variable-name"] = "Rqst";
            return method;
        }

        // Add local variables based on return type
        AddGetterLocalVariables(method, property, alConfig);

        return method;
    }

    private static void AddGetterLocalVariables(CodeMethod method, CodeProperty property, ALConfiguration alConfig)
    {
        var isCollection = property.Type.CollectionKind != CodeTypeBase.CodeTypeCollectionKind.None;
        var isEnum = property.Type is CodeType { TypeDefinition: CodeEnum };
        var isCodeunit = property.Type is CodeType { TypeDefinition: CodeClass };

        if (isCollection)
        {
            method.CustomData["source-type"] = "List";
            method.CustomData["return-variable-name"] = "ReturnList";

            // Add JArray, JToken
            var jArrayParam = new CodeParameter
            {
                Name = "JArray",
                Kind = CodeParameterKind.Custom,
                Type = new CodeType { Name = "JsonArray", IsExternal = true },
            };
            jArrayParam.CustomData["local-variable"] = "true";
            method.AddParameter(jArrayParam);

            var jTokenParam = new CodeParameter
            {
                Name = "JToken",
                Kind = CodeParameterKind.Custom,
                Type = new CodeType { Name = "JsonToken", IsExternal = true },
            };
            jTokenParam.CustomData["local-variable"] = "true";
            method.AddParameter(jTokenParam);

            if (isCodeunit)
            {
                var elementType = (CodeTypeBase)property.Type.Clone();
                elementType.CollectionKind = CodeTypeBase.CodeTypeCollectionKind.None;
                var targetParam = new CodeParameter
                {
                    Name = "TargetCodeunit",
                    Kind = CodeParameterKind.Custom,
                    Type = elementType,
                };
                targetParam.CustomData["local-variable"] = "true";
                method.AddParameter(targetParam);
            }
            else if (isEnum)
            {
                var enumType = (CodeTypeBase)property.Type.Clone();
                enumType.CollectionKind = CodeTypeBase.CodeTypeCollectionKind.None;
                var valueParam = new CodeParameter
                {
                    Name = "value",
                    Kind = CodeParameterKind.Custom,
                    Type = enumType,
                };
                valueParam.CustomData["local-variable"] = "true";
                method.AddParameter(valueParam);
            }
        }
        else if (isEnum)
        {
            var enumType = (CodeTypeBase)property.Type.Clone();
            var enumValueParam = new CodeParameter
            {
                Name = "enumValue",
                Kind = CodeParameterKind.Custom,
                Type = enumType,
            };
            enumValueParam.CustomData["local-variable"] = "true";
            method.AddParameter(enumValueParam);

            var ordinalsParam = new CodeParameter
            {
                Name = "Ordinals",
                Kind = CodeParameterKind.Custom,
                Type = new CodeType { Name = "List of [Integer]", IsExternal = true },
            };
            ordinalsParam.CustomData["local-variable"] = "true";
            method.AddParameter(ordinalsParam);

            var ordinalParam = new CodeParameter
            {
                Name = "Ordinal",
                Kind = CodeParameterKind.Custom,
                Type = new CodeType { Name = "Integer", IsExternal = true },
            };
            ordinalParam.CustomData["local-variable"] = "true";
            method.AddParameter(ordinalParam);
        }
        else if (isCodeunit)
        {
            var codeunitType = (CodeTypeBase)property.Type.Clone();
            var targetParam = new CodeParameter
            {
                Name = "TargetCodeunit",
                Kind = CodeParameterKind.Custom,
                Type = codeunitType,
            };
            targetParam.CustomData["local-variable"] = "true";
            method.AddParameter(targetParam);
        }
    }

    private static CodeMethod ToSetterCodeMethod(CodeProperty property, ALConfiguration alConfig)
    {
        var method = new CodeMethod
        {
            Name = $"Set-{property.Name.ToFirstCharacterUpperCase()}",
            Kind = CodeMethodKind.Setter,
            ReturnType = new CodeType { Name = "void", IsExternal = true },
            Access = AccessModifier.Public,
            Documentation = (CodeDocumentation)property.Documentation.Clone(),
            Parent = property.Parent,
        };
        method.SimpleName = property.Name.ToFirstCharacterUpperCase();
        method.CustomData["method-type"] = "Setter";
        method.CustomData["source"] = "from property";
        if (!string.IsNullOrEmpty(property.SerializationName))
            method.CustomData["serialization-name"] = property.SerializationName;

        // Add the value parameter
        var valueParam = new CodeParameter
        {
            Name = "p",
            Kind = CodeParameterKind.Custom,
            Type = (CodeTypeBase)property.Type.Clone(),
        };
        method.AddParameter(valueParam);

        // For collection types, add iteration helpers
        var isCollection = property.Type.CollectionKind != CodeTypeBase.CodeTypeCollectionKind.None;
        if (isCollection)
        {
            var elementType = (CodeTypeBase)property.Type.Clone();
            elementType.CollectionKind = CodeTypeBase.CodeTypeCollectionKind.None;
            var vParam = new CodeParameter
            {
                Name = "v",
                Kind = CodeParameterKind.Custom,
                Type = elementType,
            };
            vParam.CustomData["local-variable"] = "true";
            method.AddParameter(vParam);

            var jArrayParam = new CodeParameter
            {
                Name = "JArray",
                Kind = CodeParameterKind.Custom,
                Type = new CodeType { Name = "JsonArray", IsExternal = true },
            };
            jArrayParam.CustomData["local-variable"] = "true";
            method.AddParameter(jArrayParam);
        }

        return method;
    }

    private static void ModifyGetterSetterMethodName(CodeElement generatedCode)
    {
        DeepCrawlTree(generatedCode, element =>
        {
            if (element is CodeMethod method)
            {
                if (method.Name.StartsWith("Get-", StringComparison.Ordinal))
                    method.Name = method.Name[4..];
                else if (method.Name.StartsWith("Set-", StringComparison.Ordinal))
                    method.Name = method.Name[4..];
            }
        });
    }
    #endregion

    #region Step 5: Class Augmentation
    private static void UpdateApiClientClass(CodeElement generatedCode, ALConfiguration alConfig, ALConventionService conventionService, GenerationConfiguration configuration)
    {
        DeepCrawlTree(generatedCode, element =>
        {
            if (element is CodeClass codeClass &&
                codeClass.Name.Equals(configuration.ClientClassName, StringComparison.OrdinalIgnoreCase))
            {
                codeClass.CustomData["client-class"] = "true";

                // Add usings
                AddUsing(codeClass, alConfig.ClientNamespace);
                AddUsing(codeClass, alConfig.DefinitionsNamespace);

                // Add implements
                codeClass.StartBlock.AddImplements(new CodeType
                {
                    Name = "Kiota IApiClient",
                    IsExternal = true,
                });

                // Extract base URL from existing ClientConstructor
                var baseUrl = string.Empty;
                foreach (var method in codeClass.Methods)
                {
                    if (method.Kind == CodeMethodKind.ClientConstructor && !string.IsNullOrEmpty(method.BaseUrl))
                    {
                        baseUrl = method.BaseUrl;
                        break;
                    }
                }

                // Add global variables as properties
                AddGlobalVariable(codeClass, "ReqConfig", $"Codeunit \"Kiota ClientConfig\"", "1", "AA0137");
                AddGlobalVariable(codeClass, "APIAuthorization", $"Codeunit \"Kiota API Authorization\"", "1", "AA0137");
                AddGlobalVariable(codeClass, "StoredResponse", "Codeunit System.RestClient.\"Http Response Message\"", "1", "AA0137");
                AddLabel(codeClass, "BaseUrlLbl", baseUrl, true, "1");
                AddGlobalVariable(codeClass, "ConfigSet", "Boolean", "4");
                AddLabel(codeClass, "AuthorizationNotInitializedErr", "Authorization is uninitialized.", false, "4");

                // Add Initialize method
                var initMethod = new CodeMethod
                {
                    Name = "Initialize",
                    Kind = CodeMethodKind.Constructor,
                    Access = AccessModifier.Public,
                    ReturnType = new CodeType { Name = "void", IsExternal = true },
                    Parent = codeClass,
                };
                initMethod.CustomData["skip"] = "false"; // Override the skip set earlier
                initMethod.CustomData["sorting-value"] = "1";
                var authParam = new CodeParameter
                {
                    Name = "NewAPIAuthorization",
                    Kind = CodeParameterKind.Custom,
                    Type = new CodeType { Name = $"Codeunit \"Kiota API Authorization\"", IsExternal = true },
                };
                initMethod.AddParameter(authParam);
                codeClass.AddMethod(initMethod);

                // Add Configuration getter
                var configGetter = new CodeMethod
                {
                    Name = "Configuration",
                    Kind = CodeMethodKind.ClientConstructor,
                    Access = AccessModifier.Public,
                    ReturnType = new CodeType { Name = $"Codeunit \"Kiota ClientConfig\"", IsExternal = true },
                    Parent = codeClass,
                };
                configGetter.CustomData["skip"] = "false";
                configGetter.CustomData["sorting-value"] = "27";
                codeClass.AddMethod(configGetter);

                // Add Configuration setter (overload)
                var configSetter = new CodeMethod
                {
                    Name = "Configuration-overload",
                    Kind = CodeMethodKind.ClientConstructor,
                    Access = AccessModifier.Public,
                    ReturnType = new CodeType { Name = "void", IsExternal = true },
                    Parent = codeClass,
                };
                configSetter.SimpleName = "Configuration";
                configSetter.CustomData["skip"] = "false";
                configSetter.CustomData["sorting-value"] = "28";
                var configParam = new CodeParameter
                {
                    Name = "config",
                    Kind = CodeParameterKind.Custom,
                    Type = new CodeType { Name = $"Codeunit \"Kiota ClientConfig\"", IsExternal = true },
                };
                configSetter.AddParameter(configParam);
                codeClass.AddMethod(configSetter);

                // Add DefaultConfiguration
                var defaultConfig = new CodeMethod
                {
                    Name = "DefaultConfiguration",
                    Kind = CodeMethodKind.Factory,
                    Access = AccessModifier.Private,
                    ReturnType = new CodeType { Name = $"Codeunit \"Kiota ClientConfig\"", IsExternal = true },
                    Parent = codeClass,
                };
                defaultConfig.CustomData["skip"] = "false";
                defaultConfig.CustomData["sorting-value"] = "29";
                codeClass.AddMethod(defaultConfig);

                // Add Response getter
                var responseGetter = new CodeMethod
                {
                    Name = "Response",
                    Kind = CodeMethodKind.Custom,
                    Access = AccessModifier.Public,
                    ReturnType = new CodeType { Name = "Codeunit System.RestClient.\"Http Response Message\"", IsExternal = true },
                    Parent = codeClass,
                };
                responseGetter.CustomData["sorting-value"] = "50";
                responseGetter.CustomData["source"] = "response-getter";
                codeClass.AddMethod(responseGetter);

                // Add Response setter
                var responseSetter = new CodeMethod
                {
                    Name = "Response-overload",
                    Kind = CodeMethodKind.Custom,
                    Access = AccessModifier.Public,
                    ReturnType = new CodeType { Name = "void", IsExternal = true },
                    Parent = codeClass,
                };
                responseSetter.SimpleName = "Response";
                responseSetter.CustomData["sorting-value"] = "51";
                responseSetter.CustomData["source"] = "response-setter";
                var responseParam = new CodeParameter
                {
                    Name = "var ApiResponse", // 'var' to pass it by reference in AL
                    Kind = CodeParameterKind.Custom,
                    Type = new CodeType { Name = "Codeunit System.RestClient.\"Http Response Message\"", IsExternal = true },
                };
                responseSetter.AddParameter(responseParam);
                codeClass.AddMethod(responseSetter);

                foreach (var property in codeClass.Properties.Where(p => p.IsOfKind(CodePropertyKind.RequestBuilder)))
                {
                    codeClass.RemoveChildElement(property);
                    codeClass.AddMethod(ToGetterCodeMethod(property, alConfig));
                }
            }
        });
    }

    private static void UpdateModelClasses(CodeElement generatedCode, ALConfiguration alConfig, ALConventionService conventionService)
    {
        DeepCrawlTree(generatedCode, element =>
        {
            if (element is CodeClass codeClass && codeClass.IsOfKind(CodeClassKind.Model))
            {
                // Remove existing implements and replace with IModelClass
                foreach (var impl in codeClass.StartBlock.Implements.ToList())
                    codeClass.StartBlock.RemoveImplements(impl);
                codeClass.StartBlock.AddImplements(new CodeType
                {
                    Name = "Kiota IModelClass",
                    IsExternal = true,
                });

                // Remove inheritance
                if (codeClass.StartBlock.Inherits is not null)
                {
                    codeClass.StartBlock.Inherits = null;
                }

                // Add usings
                AddUsing(codeClass, alConfig.UtilitiesNamespace);
                AddUsing(codeClass, alConfig.DefinitionsNamespace);

                // Add global variables
                AddGlobalVariable(codeClass, "JSONHelper", $"Codeunit \"JSON Helper\"", "2", "AA0137");
                AddGlobalVariable(codeClass, "DebugCall", "Boolean", "4");
                AddGlobalVariable(codeClass, "JsonBody", "JsonObject", "3");
                AddGlobalVariable(codeClass, "SubToken", "JsonToken", "3");

                // Add SetBody (1 param)
                var setBody1 = new CodeMethod
                {
                    Name = "SetBody",
                    Kind = CodeMethodKind.Deserializer,
                    Access = AccessModifier.Public,
                    ReturnType = new CodeType { Name = "void", IsExternal = true },
                    Parent = codeClass,
                };
                setBody1.CustomData["sorting-value"] = "100";
                var jsonBodyParam1 = new CodeParameter
                {
                    Name = "NewJsonBody",
                    Kind = CodeParameterKind.Custom,
                    Type = new CodeType { Name = "JsonObject", IsExternal = true },
                };
                setBody1.AddParameter(jsonBodyParam1);
                codeClass.AddMethod(setBody1);

                // Add SetBody (2 params - overload)
                var setBody2 = new CodeMethod
                {
                    Name = "SetBody-overload",
                    Kind = CodeMethodKind.Deserializer,
                    Access = AccessModifier.Public,
                    ReturnType = new CodeType { Name = "void", IsExternal = true },
                    Parent = codeClass,
                };
                setBody2.SimpleName = "SetBody";
                setBody2.CustomData["sorting-value"] = "101";
                var jsonBodyParam2 = new CodeParameter
                {
                    Name = "NewJsonBody",
                    Kind = CodeParameterKind.Custom,
                    Type = new CodeType { Name = "JsonObject", IsExternal = true },
                    DefaultValue = "1",
                };
                setBody2.AddParameter(jsonBodyParam2);
                var debugParam = new CodeParameter
                {
                    Name = "Debug",
                    Kind = CodeParameterKind.Custom,
                    Type = new CodeType { Name = "Boolean", IsExternal = true },
                    DefaultValue = "2",
                };
                setBody2.AddParameter(debugParam);
                codeClass.AddMethod(setBody2);

                // Add ValidateBody
                var validateBody = new CodeMethod
                {
                    Name = "ValidateBody",
                    Kind = CodeMethodKind.Custom,
                    Access = AccessModifier.Private,
                    ReturnType = new CodeType { Name = "void", IsExternal = true },
                    Parent = codeClass,
                };
                validateBody.CustomData["pragmas-variables"] = "AA0202";
                // Add local variables; we need to add variables for each property in the class to validate presence of required properties; the properties were previously added as getter- and setter-methods, so we can find them by looking for getter methods with source "from property"
                var gettersHelper = codeClass.Methods.Where(m => m.IsGetterMethod() && m.CustomData.TryGetValue("source", out var source) && source.Equals("from property", StringComparison.Ordinal)).ToList();
                foreach (var getter in gettersHelper)
                {
                    var propName = getter.SimpleName.ToFirstCharacterLowerCase() ?? getter.Name.ToFirstCharacterLowerCase();
                    var varName = $"{propName}";
                    var localVar = new CodeParameter
                    {
                        Name = varName,
                        Kind = CodeParameterKind.Custom,
                        Type = (CodeTypeBase)getter.ReturnType.Clone(),
                    };
                    localVar.CustomData["local-variable"] = "true";
                    validateBody.AddParameter(localVar);
                }
                validateBody.CustomData["sorting-value"] = "102";
                validateBody.CustomData["source"] = "validate-body";
                codeClass.AddMethod(validateBody);

                // Add ToJson (simple)
                var toJson1 = new CodeMethod
                {
                    Name = "ToJson",
                    Kind = CodeMethodKind.Serializer,
                    Access = AccessModifier.Public,
                    ReturnType = new CodeType { Name = "JsonObject", IsExternal = true },
                    Parent = codeClass,
                };
                toJson1.CustomData["sorting-value"] = "103";
                codeClass.AddMethod(toJson1);

                // Add ToJson (with parameters - overload)
                var getters = codeClass.Methods.Where(m => m.IsGetterMethod()).ToList();
                if (getters.Count != 0)
                {
                    var toJson2 = new CodeMethod
                    {
                        Name = "ToJson-overload",
                        Kind = CodeMethodKind.Serializer,
                        Access = AccessModifier.Public,
                        ReturnType = new CodeType { Name = "JsonObject", IsExternal = true },
                        Parent = codeClass,
                    };
                    toJson2.SimpleName = "ToJson";
                    toJson2.CustomData["sorting-value"] = "104";

                    // Add TargetJson local var
                    var targetJsonParam = new CodeParameter
                    {
                        Name = "TargetJson",
                        Kind = CodeParameterKind.Custom,
                        Type = new CodeType { Name = "JsonObject", IsExternal = true },
                    };
                    targetJsonParam.CustomData["local-variable"] = "true";
                    toJson2.AddParameter(targetJsonParam);

                    foreach (var getter in getters)
                    {
                        var paramType = (CodeTypeBase)getter.ReturnType.Clone();
                        var paramName = getter.SimpleName.ToFirstCharacterLowerCase() ?? getter.Name.ToFirstCharacterLowerCase();
                        var param = new CodeParameter
                        {
                            Name = paramName,
                            Kind = CodeParameterKind.Custom,
                            Type = paramType,
                        };

                        // For codeunit collections, add array + foreach vars
                        if (paramType.CollectionKind != CodeTypeBase.CodeTypeCollectionKind.None &&
                            paramType is CodeType pt && pt.TypeDefinition is CodeClass)
                        {
                            var singularName = CodeMethodExtensions.GetSingularName(paramName, toJson2.Parameters);
                            param.CustomData["foreach-variable"] = singularName;

                            var arrayName = $"{paramName}Array";
                            param.CustomData["corresponding-array"] = arrayName;

                            var arrayParam = new CodeParameter
                            {
                                Name = arrayName,
                                Kind = CodeParameterKind.Custom,
                                Type = new CodeType { Name = "JsonArray", IsExternal = true },
                            };
                            arrayParam.CustomData["local-variable"] = "true";
                            toJson2.AddParameter(arrayParam);

                            var elementType = (CodeTypeBase)paramType.Clone();
                            elementType.CollectionKind = CodeTypeBase.CodeTypeCollectionKind.None;
                            var foreachParam = new CodeParameter
                            {
                                Name = singularName,
                                Kind = CodeParameterKind.Custom,
                                Type = elementType,
                            };
                            foreachParam.CustomData["local-variable"] = "true";
                            toJson2.AddParameter(foreachParam);
                        }

                        toJson2.AddParameter(param);
                    }

                    codeClass.AddMethod(toJson2);
                }
            }
        });
    }

    private static void UpdateRequestBuilderClasses(CodeElement generatedCode, ALConfiguration alConfig, ALConventionService conventionService)
    {
        DeepCrawlTree(generatedCode, element =>
        {
            if (element is CodeClass codeClass &&
                codeClass.IsOfKind(CodeClassKind.RequestBuilder) &&
                !codeClass.CustomData.ContainsKey("client-class"))
            {
                // Add usings
                AddUsing(codeClass, alConfig.ClientNamespace);

                // Add ReqConfig global variable
                AddGlobalVariable(codeClass, "ReqConfig", $"Codeunit {alConfig.ClientNamespace}.\"Kiota ClientConfig\"", "1", "AA0137");

                // Add SetConfiguration method
                var setConfig = new CodeMethod
                {
                    Name = "SetConfiguration",
                    Kind = CodeMethodKind.RawUrlBuilder,
                    Access = AccessModifier.Public,
                    ReturnType = new CodeType { Name = "void", IsExternal = true },
                    Parent = codeClass,
                };
                setConfig.CustomData["skip"] = "false";
                setConfig.CustomData["sorting-value"] = "2";
                var configParam = new CodeParameter
                {
                    Name = "NewReqConfig",
                    Kind = CodeParameterKind.Custom,
                    Type = new CodeType { Name = $"Codeunit {alConfig.ClientNamespace}.\"Kiota ClientConfig\"", IsExternal = true },
                };
                setConfig.AddParameter(configParam);
                codeClass.AddMethod(setConfig);

                // Handle indexers — check for methods converted from indexers
                var indexerMethods = codeClass.Methods
                    .Where(m => m.Kind == CodeMethodKind.IndexerBackwardCompatibility && m.OriginalIndexer is not null)
                    .ToList();

                if (indexerMethods.Count != 0)
                {
                    var indexerMethod = indexerMethods[0];
                    var indexerParamType = indexerMethod.Parameters.FirstOrDefault()?.Type;
                    var indexerTypeAsClass = ((Kiota.Builder.CodeDOM.CodeType)indexerMethod.ReturnType).TypeDefinition as CodeClass;

                    if (indexerParamType is not null)
                    {
                        // Add SetIdentifier method
                        var setId = new CodeMethod
                        {
                            Name = "SetIdentifier",
                            Kind = CodeMethodKind.RawUrlBuilder,
                            Access = AccessModifier.Public,
                            ReturnType = new CodeType { Name = "void", IsExternal = true },
                            Parent = codeClass,
                        };
                        setId.CustomData["skip"] = "false";
                        setId.CustomData["sorting-value"] = "3";
                        var idParam = new CodeParameter
                        {
                            Name = "NewIdentifier",
                            Kind = CodeParameterKind.Custom,
                            Type = (CodeTypeBase)indexerParamType.Clone(),
                        };
                        setId.AddParameter(idParam);
                        indexerTypeAsClass?.AddMethod(setId);
                        // Add Identifier global variable
                        if (indexerTypeAsClass != null)
                            AddGlobalVariable(indexerTypeAsClass, "Identifier", indexerParamType.Name, "2");

                        // Update the indexer method to be Item_Idx
                        indexerMethod.Name = "Item_Idx";
                        indexerMethod.Kind = CodeMethodKind.Custom;
                        indexerMethod.CustomData["source"] = "from indexer";
                        indexerMethod.CustomData["sorting-value"] = "4";
                        indexerMethod.CustomData["return-variable-name"] = "Rqst";
                    }
                }
            }
        });
    }
    #endregion

    #region Step 6: Request Executor Enhancement
    private static void UpdateRequestExecutorMethods(CodeElement generatedCode, ALConfiguration alConfig, ALConventionService conventionService, ALObjectIdProvider objectIdProvider)
    {
        DeepCrawlTree(generatedCode, element =>
        {
            if (element is CodeMethod method && method.Kind == CodeMethodKind.RequestExecutor &&
                method.Parent is CodeClass parentClass)
            {
                // Add RequestHandler local variable
                var reqHandlerParam = new CodeParameter
                {
                    Name = "RequestHandler",
                    Kind = CodeParameterKind.Custom,
                    Type = new CodeType { Name = $"Codeunit {alConfig.ClientNamespace}.\"Kiota RequestHandler\"", IsExternal = true },
                };
                reqHandlerParam.CustomData["local-variable"] = "true";
                method.AddParameter(reqHandlerParam);

                // if the method has a body, that is a collection type of model-codeunits, we need to add 2 additional local variables
                // BodyElement: Codeunit <model-codeunit>
                // BodyObjects: List of [Interface SimonOfHH.Kiota.Definitions."Kiota IModelClass"]
                if (method.Parameters.Any(p => p.Kind == CodeParameterKind.RequestBody))
                {
                    var bodyParam = method.Parameters.First(p => p.Kind == CodeParameterKind.RequestBody);
                    if (bodyParam.Type.IsCollection && bodyParam.Type is CodeType bodyType && bodyType.TypeDefinition is CodeClass bodyClass && bodyClass.IsOfKind(CodeClassKind.Model))
                    {
                        var bodyElementType = new CodeType
                        {
                            Name = bodyClass.Name,
                            TypeDefinition = bodyClass,
                            IsExternal = false,
                        };
                        var bodyElementParam = new CodeParameter
                        {
                            Name = "BodyElement",
                            Kind = CodeParameterKind.Custom,
                            Type = bodyElementType,
                        };
                        bodyElementParam.CustomData["local-variable"] = "true";
                        method.AddParameter(bodyElementParam);

                        var bodyObjectsParam = new CodeParameter
                        {
                            Name = "BodyObjects",
                            Kind = CodeParameterKind.Custom,
                            Type = new CodeType { Name = $"List of [Interface \"Kiota IModelClass\"]", IsExternal = true },
                        };
                        bodyObjectsParam.CustomData["local-variable"] = "true";
                        method.AddParameter(bodyObjectsParam);

                        AddUsing(parentClass, alConfig.DefinitionsNamespace);
                    }
                }
                // Handle return type
                var returnType = method.ReturnType;
                if (returnType is CodeType ct)
                {
                    if (returnType.CollectionKind != CodeTypeBase.CodeTypeCollectionKind.None)
                    {
                        // Collection return
                        method.CustomData["return-variable-name"] = "Target";
                        method.CustomData["source-type"] = "List";

                        var elementType = (CodeTypeBase)returnType.Clone();
                        elementType.CollectionKind = CodeTypeBase.CodeTypeCollectionKind.None;
                        var targetTypeParam = new CodeParameter
                        {
                            Name = "TargetType",
                            Kind = CodeParameterKind.Custom,
                            Type = elementType,
                        };
                        targetTypeParam.CustomData["local-variable"] = "true";
                        method.AddParameter(targetTypeParam);

                        var subTokenParam = new CodeParameter
                        {
                            Name = "SubToken",
                            Kind = CodeParameterKind.Custom,
                            Type = new CodeType { Name = "JsonToken", IsExternal = true },
                        };
                        subTokenParam.CustomData["local-variable"] = "true";
                        method.AddParameter(subTokenParam);

                        var responseArrayParam = new CodeParameter
                        {
                            Name = "ResponseAsArray",
                            Kind = CodeParameterKind.Custom,
                            Type = new CodeType { Name = "JsonArray", IsExternal = true },
                        };
                        responseArrayParam.CustomData["local-variable"] = "true";
                        method.AddParameter(responseArrayParam);

                        var iParam = new CodeParameter
                        {
                            Name = "i",
                            Kind = CodeParameterKind.Custom,
                            Type = new CodeType { Name = "Integer", IsExternal = true },
                        };
                        iParam.CustomData["local-variable"] = "true";
                        method.AddParameter(iParam);
                    }
                    else if (ct.TypeDefinition is CodeClass)
                    {
                        // Single codeunit return
                        method.CustomData["return-variable-name"] = "Target";
                    }
                }

                // Handle query parameters → parameter codeunit
                var queryParamsClass = parentClass.InnerClasses
                    .FirstOrDefault(c => c.Name.EndsWith("QueryParameters", StringComparison.OrdinalIgnoreCase));

                if (queryParamsClass is not null)
                {
                    var queryParams = queryParamsClass.Properties
                        .Where(p => p.Kind == CodePropertyKind.QueryParameter)
                        .Select(p => $"{p.Name}:{(p.Type is CodeType pct ? pct.Name : "Text")}")
                        .ToList();

                    if (queryParams.Count > 0)
                    {
                        // Create parameter codeunit class
                        var paramClassName = $"{parentClass.Name}{method.Name}Parameters";
                        paramClassName = conventionService.SanitizeName(paramClassName, null, 30);

                        try
                        {
                            var parentNs = parentClass.GetImmediateParentOfType<CodeNamespace>();
                            var paramClass = new CodeClass
                            {
                                Name = paramClassName,
                                Kind = CodeClassKind.QueryParameters,
                            };
                            paramClass.CustomData["parameter-codeunit"] = "true";
                            paramClass.CustomData["query-parameters"] = string.Join(",", queryParams);
                            paramClass.CustomData["object-id"] = objectIdProvider.GetNextCodeunitId().ToString(CultureInfo.InvariantCulture);
                            parentNs.AddClass(paramClass);

                            // Add parameter to executor method
                            var paramsParam = new CodeParameter
                            {
                                Name = "Parameters",
                                Kind = CodeParameterKind.Custom,
                                Type = new CodeType
                                {
                                    Name = paramClassName,
                                    TypeDefinition = paramClass,
                                    IsExternal = false,
                                },
                            };
                            method.AddParameter(paramsParam);
                            method.CustomData["use-parameter-codeunit"] = "true";
                        }
                        catch (InvalidOperationException) { }
                    }
                }
            }
        });
    }
    #endregion

    #region Step 7: Interface Generation
    private static void AddCodeInterfacesForInheritedTypes(CodeElement generatedCode, ALConfiguration alConfig)
    {
        // For model classes with DiscriminatorInformation, create interfaces
        DeepCrawlTree(generatedCode, element =>
        {
            if (element is CodeClass codeClass &&
                codeClass.IsOfKind(CodeClassKind.Model) &&
                codeClass.DiscriminatorInformation?.DiscriminatorPropertyName is not null)
            {
                try
                {
                    var parentNs = codeClass.GetImmediateParentOfType<CodeNamespace>();
                    var interfaceName = $"I{codeClass.Name}";

                    var iface = new CodeInterface
                    {
                        Name = interfaceName,
                        OriginalClass = codeClass,
                    };
                    parentNs.AddInterface(iface);

                    // Add discriminator method signatures
                    // This is a placeholder for future extension
                }
                catch (InvalidOperationException) { }
            }
        });
    }
    #endregion

    #region Step 8: Manifest and Finalization
    private static void AddAppJsonAsCodeFunction(CodeElement generatedCode, ALConfiguration alConfig, GenerationConfiguration configuration)
    {
        if (generatedCode is CodeNamespace rootNs)
        {
            // CodeFunction requires the method to be a member of a CodeClass
            var holderClass = new CodeClass { Name = "_AppJsonHolder" };
            rootNs.AddClass(holderClass);

            var method = new CodeMethod
            {
                Name = "AppJson",
                Kind = CodeMethodKind.Custom,
                IsStatic = true,
                ReturnType = new CodeType { Name = "void", IsExternal = true },
            };
            holderClass.AddMethod(method);

            var appJsonFunc = new CodeFunction(method);

            // Encode configuration as usings (data carrier pattern)
            var usings = new List<CodeUsing>();
            AddConfigUsing(usings, "Name", configuration.ClientNamespaceName);
            AddConfigUsing(usings, "Publisher", alConfig.AppPublisherName);
            AddConfigUsing(usings, "Brief", alConfig.AppBrief);
            AddConfigUsing(usings, "Description", alConfig.AppDescription);
            AddConfigUsing(usings, "Version", alConfig.AppVersion);
            AddConfigUsing(usings, "IDRangeStart", alConfig.ObjectIdRangeStart.ToString(CultureInfo.InvariantCulture));
            AddConfigUsing(usings, "IDRangeEnd", alConfig.ObjectIdRangeEnd.ToString(CultureInfo.InvariantCulture));
            AddConfigUsing(usings, "CompanionAppId", alConfig.CompanionAppId);
            AddConfigUsing(usings, "CompanionAppName", alConfig.CompanionAppName);
            AddConfigUsing(usings, "CompanionPublisher", alConfig.CompanionPublisher);
            AddConfigUsing(usings, "CompanionAppVersion", alConfig.CompanionAppVersion);
            AddConfigUsing(usings, "PrivacyStatementUrl", alConfig.PrivacyStatementUrl);
            AddConfigUsing(usings, "EulaUrl", alConfig.EulaUrl);
            AddConfigUsing(usings, "HelpUrl", alConfig.HelpUrl);
            AddConfigUsing(usings, "AppUrl", alConfig.AppUrl);

            foreach (var u in usings)
                appJsonFunc.AddUsing(u);

            rootNs.AddFunction(appJsonFunc);
            rootNs.RemoveChildElement(holderClass);
        }
    }

    private static void AddConfigUsing(List<CodeUsing> usings, string key, string value)
    {
        usings.Add(new CodeUsing
        {
            Name = $"{key}={value}",
        });
    }

    private static void ModifyOverloadMethodNames(CodeElement generatedCode)
    {
        DeepCrawlTree(generatedCode, element =>
        {
            if (element is CodeMethod method && method.Name.Contains("-overload", StringComparison.OrdinalIgnoreCase))
            {
                method.Name = method.Name.Replace("-overload", string.Empty, StringComparison.OrdinalIgnoreCase);
            }
        });
    }

    private static void UpdateMethodParameters(CodeElement generatedCode)
    {
        var reservedNames = new ALReservedNamesProvider();
        DeepCrawlTree(generatedCode, element =>
        {
            if (element is CodeParameter param && reservedNames.ReservedNames.Contains(param.Name))
            {
                param.Name += "_";
            }
        });
    }

    private static void AssignPragmas(CodeElement generatedCode)
    {
        DeepCrawlTree(generatedCode, element =>
        {
            if (element is CodeClass codeClass)
            {
                // Check if class has global variables
                var hasGlobals = codeClass.Properties.Any(p =>
                    p.CustomData.ContainsKey("global-variable") ||
                    p.Kind == CodePropertyKind.Custom);

                if (hasGlobals || codeClass.IsOfKind(CodeClassKind.Model) ||
                    codeClass.CustomData.ContainsKey("client-class"))
                {
                    codeClass.CustomData["pragmas-variables"] = "AA0021,AA0202";
                }
            }
        });
    }
    #endregion

    #region Helpers
    /// <summary>
    /// Recursively walks the entire CodeDOM tree, invoking the callback on every descendant element.
    /// Unlike CrawlTree (which only visits direct children), this reaches all nested elements.
    /// </summary>
    private static void DeepCrawlTree(CodeElement currentElement, Action<CodeElement> function)
    {
        ArgumentNullException.ThrowIfNull(currentElement);
        ArgumentNullException.ThrowIfNull(function);
        var children = currentElement.GetChildElements(true).ToArray();
        foreach (var childElement in children)
        {
            function.Invoke(childElement);
            DeepCrawlTree(childElement, function);
        }
    }

    private static void AddUsing(CodeClass codeClass, string namespaceName)
    {
        codeClass.AddUsing(new CodeUsing
        {
            Name = namespaceName,
        });
    }

    private static void AddGlobalVariable(CodeClass codeClass, string name, string typeName, string defaultValue, string? pragmas = null)
    {
        var prop = new CodeProperty
        {
            Name = name,
            Kind = CodePropertyKind.Custom,
            Type = new CodeType { Name = typeName, IsExternal = true },
            DefaultValue = defaultValue,
        };
        prop.CustomData["global-variable"] = "true";
        prop.CustomData["locked"] = "true";
        if (!string.IsNullOrEmpty(pragmas))
            prop.CustomData["pragmas"] = pragmas;
        codeClass.AddProperty(prop);
    }

    private static void AddLabel(CodeClass codeClass, string name, string value, bool locked, string defaultValue)
    {
        var prop = new CodeProperty
        {
            Name = name,
            Kind = CodePropertyKind.Custom,
            Type = new CodeType { Name = "Label", IsExternal = true },
            DefaultValue = defaultValue,
        };
        prop.CustomData["global-variable"] = "true";
        prop.CustomData["locked"] = "true";
        prop.CustomData["value"] = value;
        if (locked)
            prop.CustomData["locked-label"] = "true";
        codeClass.AddProperty(prop);
    }
    #endregion
}
