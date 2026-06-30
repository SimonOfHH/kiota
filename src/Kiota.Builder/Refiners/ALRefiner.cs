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

            // Step 1.5: Flatten inherited properties (AL has no inheritance)
            FlattenInheritedProperties(generatedCode);

            // Step 1.6: Deduplicate identical enums/model classes across namespaces
            DeduplicateObjects(generatedCode);

            // Step 1.7: Convert pure-dictionary properties (type:object + additionalProperties)
            // Must run after deduplication (canonical types are settled) but before name management
            // (so TypeDefinition references survive renaming automatically).
            ConvertPureDictionaryProperties(generatedCode);

            // Step 2: AL-specific method removal/recreation
            RemoveUnusedMethods(generatedCode);
            RemoveAdditionalDataProperty(generatedCode);
            RemoveNotSupportedParameters(generatedCode);
            MarkMethodsToSkip(generatedCode);

            // Step 3: Name management
            SetObjectIdsOnClassesAndEnums(generatedCode, objectIdProvider);
            ModifyClassNames(generatedCode, alConfig, conventionService);
            ModifyEnumNames(generatedCode, alConfig, conventionService);
            SetDefaultObjectProperties(generatedCode, alConfig);

            // Step 4: Property → Method conversion
            MovePropertiesToMethods(generatedCode, alConfig);
            ModifyGetterSetterMethodName(generatedCode);

            // Step 5: Class augmentation
            UpdateApiClientClass(generatedCode, alConfig, conventionService, _configuration);
            UpdateModelClasses(generatedCode, alConfig, conventionService);
            UpdateRequestBuilderClasses(generatedCode, alConfig, conventionService);

            // Step 5.5: Value-wrapper convenience overloads
            AddValueWrapperConvenienceOverloads(generatedCode);

            // Step 6: Request executor enhancement
            UpdateRequestExecutorMethods(generatedCode, alConfig, conventionService, objectIdProvider);

            // Step 5.6: Multipart body convenience overloads
            AddMultipartBodyConvenienceOverloads(generatedCode);

            // Step 7: Interface generation
            AddCodeInterfacesForInheritedTypes(generatedCode, alConfig);

            // Step 8: Manifest and finalization
            AddAppJsonAsCodeFunction(generatedCode, alConfig, _configuration);
            AddReadmeAsCodeFunction(generatedCode, alConfig, _configuration);
            ModifyOverloadMethodNames(generatedCode);
            UpdateMethodParameters(generatedCode);

            AddDefaultPragmas(generatedCode, alConfig, conventionService);
        }, cancellationToken);
    }

    #region Step 1: ModifyNamespaces
    private static void ModifyNamespaces(CodeElement generatedCode)
    {
        var reservedNames = new ALReservedNamesProvider();
        DeepCrawlTree(generatedCode, element =>
        {
            if (element is CodeNamespace)
            {
                // Replace any namespace segments that start with an underscore, which is not recommended in AL and would trigger warnings. This is done as a first step to ensure we catch any namespaces that start with underscores before we do any other modifications.
                var ns = (CodeNamespace)element;
                if (ns.Name.Split('.').Any(s => s.StartsWith('_')))
                {
                    ns.Name = string.Join('.', ns.Name.Split('.').Select(s =>
                        s.StartsWith('_') ? "u" + s.TrimStart('_') : s));
                }
                // Also check if any part of the namespace is a reserved name, and if so, append an underscore to it. This is done after the underscore replacement to ensure we catch any namespaces that become reserved names after the first modification.
                var segments = ns.Name.Split('.');
                for (int i = 0; i < segments.Length; i++)
                {
                    if (reservedNames.ReservedNames.Contains(segments[i]))
                        segments[i] += "_";
                }
                ns.Name = string.Join('.', segments);
            }
        });
    }
    #endregion

    #region Step 1.5: Flatten Inherited Properties
    /// <summary>
    /// AL does not support class inheritance. For every model class that has a base type
    /// (StartBlock.Inherits), copy all properties from the full ancestor chain into the
    /// derived class, then remove the inheritance link.
    /// </summary>
    private static void FlattenInheritedProperties(CodeElement generatedCode)
    {
        // Collect all model classes that have an inheritance relationship
        var classesWithInheritance = new List<CodeClass>();
        DeepCrawlTree(generatedCode, element =>
        {
            if (element is CodeClass c && c.IsOfKind(CodeClassKind.Model) && c.StartBlock.Inherits is not null)
            {
                classesWithInheritance.Add(c);
            }
        });

        foreach (var derivedClass in classesWithInheritance)
        {
            // Walk up the full inheritance chain and collect all ancestor properties
            var ancestorProperties = GetAllAncestorProperties(derivedClass);
            var existingPropertyNames = new HashSet<string>(
                derivedClass.Properties.Select(p => p.Name),
                StringComparer.OrdinalIgnoreCase);

            foreach (var ancestorProp in ancestorProperties)
            {
                // Skip if the derived class already defines a property with the same name
                if (existingPropertyNames.Contains(ancestorProp.Name))
                    continue;

                // Clone the property so we don't share object references across classes
                var clonedProp = new CodeProperty
                {
                    Name = ancestorProp.Name,
                    Kind = ancestorProp.Kind,
                    Type = (CodeTypeBase)ancestorProp.Type.Clone(),
                    Access = ancestorProp.Access,
                    DefaultValue = ancestorProp.DefaultValue,
                    SerializationName = ancestorProp.SerializationName,
                    Documentation = (CodeDocumentation)ancestorProp.Documentation.Clone(),
                };

                // Carry over any custom data (e.g., "global-variable", "locked")
                foreach (var kvp in ancestorProp.CustomData)
                    clonedProp.CustomData[kvp.Key] = kvp.Value;

                // Mark it so we know it was inherited (useful for debugging / future logic)
                clonedProp.CustomData["inherited-from"] = ancestorProp.Parent?.Name ?? "unknown";

                derivedClass.AddProperty(clonedProp);
                existingPropertyNames.Add(clonedProp.Name);
            }

            // Remove the inheritance link — AL codeunits don't extend other codeunits
            derivedClass.StartBlock.Inherits = null;
        }
    }

    /// <summary>
    /// Walks up the inheritance chain and returns all properties from all ancestors,
    /// ordered from the most-base class down to the immediate parent.
    /// </summary>
    private static List<CodeProperty> GetAllAncestorProperties(CodeClass derivedClass)
    {
        var result = new List<CodeProperty>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = derivedClass.StartBlock.Inherits?.TypeDefinition as CodeClass;

        while (current is not null && !visited.Contains(current.Name))
        {
            visited.Add(current.Name); // guard against circular references
            // Insert at the beginning so base-most properties come first
            result.InsertRange(0, current.Properties);
            current = current.StartBlock.Inherits?.TypeDefinition as CodeClass;
        }

        return result;
    }
    #endregion

    #region Step 1.6: Deduplicate objects across namespaces
    /// <summary>
    /// Detects enums and model classes that appear more than once (identical name + identical
    /// members) across different namespaces.  For each duplicate group the copy that lives in
    /// the shallowest namespace (fewest dots) is kept as the canonical one; every other copy
    /// is removed and every <see cref="CodeType.TypeDefinition"/> that pointed to a removed
    /// copy is re-wired to the canonical copy.
    /// </summary>
    private static void DeduplicateObjects(CodeElement generatedCode)
    {
        DeduplicateEnums(generatedCode);
        DeduplicateModelClasses(generatedCode);
    }

    private static void DeduplicateEnums(CodeElement generatedCode)
    {
        // Collect every enum together with its parent namespace and namespace depth
        var allEnums = new List<(CodeEnum Enum, CodeNamespace Namespace, int Depth)>();
        DeepCrawlTree(generatedCode, element =>
        {
            if (element is CodeEnum e && e.Parent is CodeNamespace ns)
                allEnums.Add((e, ns, ns.Name.Split('.').Length));
        });

        // Group by name; only care about groups with more than one member
        var groups = allEnums
            .GroupBy(x => x.Enum.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var group in groups)
        {
            var items = group.ToList();
            // The canonical copy lives in the shallowest (shortest) namespace
            var canonical = items
                .OrderBy(x => x.Depth)
                .ThenBy(x => x.Namespace.Name, StringComparer.OrdinalIgnoreCase)
                .First();

            var canonicalOptions = GetEnumOptionNames(canonical.Enum);

            foreach (var dup in items.Where(x => x != canonical))
            {
                // Only deduplicate when the option sets are actually identical
                if (!GetEnumOptionNames(dup.Enum).SetEquals(canonicalOptions))
                    continue;
                canonical.Enum.CustomData["deduplicated"] = "true";
                RewriteTypeDefinitionReferences(generatedCode, dup.Enum, canonical.Enum);
                dup.Namespace.RemoveChildElement(dup.Enum);
            }
        }
    }

    private static HashSet<string> GetEnumOptionNames(CodeEnum e) =>
        new(e.Options
               .Where(o => !o.CustomData.ContainsKey("object-property"))
               .Select(o => o.Name),
           StringComparer.OrdinalIgnoreCase);

    private static void DeduplicateModelClasses(CodeElement generatedCode)
    {
        var allClasses = new List<(CodeClass Class, CodeNamespace Namespace, int Depth)>();
        DeepCrawlTree(generatedCode, element =>
        {
            if (element is CodeClass c &&
                c.IsOfKind(CodeClassKind.Model) &&
                c.Parent is CodeNamespace ns)
                allClasses.Add((c, ns, ns.Name.Split('.').Length));
        });

        var groups = allClasses
            .GroupBy(x => x.Class.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1);

        foreach (var group in groups)
        {
            var items = group.ToList();
            var canonical = items
                .OrderBy(x => x.Depth)
                .ThenBy(x => x.Namespace.Name, StringComparer.OrdinalIgnoreCase)
                .First();

            var canonicalProps = GetModelPropertyNames(canonical.Class);

            foreach (var dup in items.Where(x => x != canonical))
            {
                if (!GetModelPropertyNames(dup.Class).SetEquals(canonicalProps))
                    continue;
                canonical.Class.CustomData["deduplicated"] = "true";
                RewriteTypeDefinitionReferences(generatedCode, dup.Class, canonical.Class);
                dup.Namespace.RemoveChildElement(dup.Class);
            }
        }
    }

    private static HashSet<string> GetModelPropertyNames(CodeClass c) =>
        new(c.Properties
               .Where(p => !p.CustomData.ContainsKey("global-variable") &&
                           !p.CustomData.ContainsKey("object-property"))
               .Select(p => p.Name),
           StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Walks the entire CodeDOM tree and replaces every <see cref="CodeType.TypeDefinition"/>
    /// that points at <paramref name="oldDef"/> with <paramref name="newDef"/>.
    /// </summary>
    private static void RewriteTypeDefinitionReferences(
        CodeElement root, CodeElement oldDef, CodeElement newDef)
    {
        DeepCrawlTree(root, element =>
        {
            switch (element)
            {
                case CodeMethod m:
                    RewireCodeTypeBase(m.ReturnType, oldDef, newDef);
                    foreach (var p in m.Parameters)
                        RewireCodeTypeBase(p.Type, oldDef, newDef);
                    break;

                case CodeProperty prop:
                    RewireCodeTypeBase(prop.Type, oldDef, newDef);
                    break;

                case CodeParameter param:
                    RewireCodeTypeBase(param.Type, oldDef, newDef);
                    break;

                case CodeClass c:
                    if (c.StartBlock.Inherits is CodeType inheritType)
                        RewireCodeTypeBase(inheritType, oldDef, newDef);
                    foreach (var impl in c.StartBlock.Implements)
                        RewireCodeTypeBase(impl, oldDef, newDef);
                    break;
            }
        });
    }

    private static void RewireCodeTypeBase(CodeTypeBase? typeBase, CodeElement oldDef, CodeElement newDef)
    {
        if (typeBase is CodeType ct && ReferenceEquals(ct.TypeDefinition, oldDef))
            ct.TypeDefinition = newDef;
    }
    #endregion

    #region Step 1.7: Convert pure-dictionary properties
    /// <summary>
    /// Finds every CodeClass that was generated from a pure-dictionary OpenAPI schema
    /// (type:object + additionalProperties only, no named properties) and replaces the
    /// CodeProperty types that point at it with a CodeType whose TypeDefinition is the
    /// actual value type (enum or class) referenced by additionalProperties.
    /// The AL writer will then render that property as "Dictionary of [Text, &lt;valueType&gt;]".
    /// </summary>
    private static void ConvertPureDictionaryProperties(CodeElement generatedCode)
    {
        // Collect all pure-dictionary classes together with their parent namespace.
        // Detection strategy:
        //   - a Model class with ONLY an AdditionalData property and no
        //     Custom properties is structurally a pure-dict wrapper 
        var pureDictClasses = new List<(CodeClass Class, CodeNamespace Namespace, string ValueTypeName)>();
        DeepCrawlTree(generatedCode, element =>
        {
            if (element is not CodeClass c || c.Parent is not CodeNamespace ns)
                return;

            // Structural fallback: Model with only AdditionalData, no Custom properties
            if (c.Kind == CodeClassKind.Model &&
                c.Properties.Any(p => p.Kind == CodePropertyKind.AdditionalData) &&
                !c.Properties.Any(p => p.Kind == CodePropertyKind.Custom))
            {
                var additionalDataProp = c.Properties.First(p => p.Kind == CodePropertyKind.AdditionalData);
                var additionalDataPropType = additionalDataProp.Type as CodeType;
                pureDictClasses.Add((c, ns, additionalDataPropType?.TypeDefinition?.Name ?? string.Empty));
            }
        });

        foreach (var (dictClass, ns, valueTypeName) in pureDictClasses)
        {

            // Find the actual CodeElement that the additionalProperties $ref resolves to
            CodeElement? valueTypeElement = null;
            DeepCrawlTree(generatedCode, element =>
            {
                if (valueTypeElement is not null) return;
                if (element is CodeEnum e && e.Name.Equals(valueTypeName, StringComparison.OrdinalIgnoreCase))
                    valueTypeElement = e;
                else if (element is CodeClass c2 &&
                         !ReferenceEquals(c2, dictClass) &&
                         c2.Name.Equals(valueTypeName, StringComparison.OrdinalIgnoreCase))
                    valueTypeElement = c2;
            });

            if (valueTypeElement is null) continue; // Cannot resolve — leave as-is

            // Replace every CodeProperty that currently points to this pure-dictionary class
            DeepCrawlTree(generatedCode, element =>
            {
                if (element is CodeProperty prop &&
                    prop.Type is CodeType ct &&
                    ReferenceEquals(ct.TypeDefinition, dictClass))
                {
                    var dictType = new CodeType { TypeDefinition = valueTypeElement };
                    dictType.CustomData["al-dictionary"] = "true";
                    prop.Type = dictType;
                }
            });

            // Remove the now-redundant wrapper class
            ns.RemoveChildElement(dictClass);
        }
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
                    c.CustomData["pragmas"] = c.CustomData.TryGetValue("pragmas", out var existingPragmas) // Filename is original, but classname differs -> add pragma to suppress warning about that
                        ? $"{existingPragmas},AA0215" : "AA0215";
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

    #region Step 3.5: Default Object Properties
    /// <summary>
    /// Adds default AL object properties (e.g., Access = Internal) to all
    /// codeunit classes and enum objects that will be generated.
    /// </summary>
    private static void SetDefaultObjectProperties(CodeElement generatedCode, ALConfiguration alConfig)
    {
        DeepCrawlTree(generatedCode, element =>
        {
            switch (element)
            {
                case CodeClass c:
                    {
                        if (alConfig.MarkInternal)
                            AddObjectProperty(c, "Access", "Internal");
                        break;
                    }
                case CodeEnum e:
                    {
                        if (alConfig.MarkInternal)
                            AddObjectOption(e, "Access", "Internal");
                        AddObjectOption(e, "Extensible", "false");
                        break;
                    }
            }
        });
    }
    #endregion

    #region Step 3.75: Add pragmas
    private static void AddDefaultPragmas(CodeElement generatedCode, ALConfiguration alConfig, ALConventionService conventionService)
    {
        AddPragmasForSpecificCharactersInDocumentation(generatedCode, alConfig, conventionService);
        AddPragmasForUnderscoreNames(generatedCode, alConfig, conventionService);
    }
    private static void AddPragmasForSpecificCharactersInDocumentation(CodeElement generatedCode, ALConfiguration alConfig, ALConventionService conventionService)
    {
        DeepCrawlTree(generatedCode, element =>
        {
            if (element is CodeClass c)
            {
                if (c.Documentation?.DescriptionAvailable == true)
                {
                    var description = c.Documentation.GetDescription(static t => t.Name);
                    if (!string.IsNullOrEmpty(description) && ContainsNotAllowedCharacters(description))
                    {
                        c.CustomData["documentation-pragmas"] = c.CustomData.TryGetValue("documentation-pragmas", out var existingPragmas)
                            ? $"{existingPragmas},AL0640" : "AL0640";
                    }
                }
            }
            else if (element is CodeEnum e)
            {
                if (e.Documentation?.DescriptionAvailable == true)
                {
                    var description = e.Documentation.GetDescription(static t => t.Name);
                    if (!string.IsNullOrEmpty(description) && ContainsNotAllowedCharacters(description))
                    {
                        e.CustomData["documentation-pragmas"] = e.CustomData.TryGetValue("documentation-pragmas", out var existingPragmas)
                            ? $"{existingPragmas},AL0640" : "AL0640";
                    }
                }
            }
            else if (element is CodeMethod m)
            {
                if (m.Documentation?.DescriptionAvailable == true)
                {
                    var description = m.Documentation.GetDescription(static t => t.Name);
                    if (!string.IsNullOrEmpty(description) && ContainsNotAllowedCharacters(description))
                    {
                        m.CustomData["documentation-pragmas"] = m.CustomData.TryGetValue("documentation-pragmas", out var existingPragmas)
                            ? $"{existingPragmas},AL0640" : "AL0640";
                    }
                }
            }
        });
    }
    private static bool ContainsNotAllowedCharacters(string input)
    {
        // AL does not allow certain characters in XML comments, such as & and <
        // This method checks for those characters.
        return input.Contains('&', StringComparison.Ordinal) || input.Contains('<', StringComparison.Ordinal);
    }
    /// <summary>
    /// Adds necessary pragmas to classes that have names with underscores, which are not recommended in AL and would trigger warnings. This is done as a last step in name management to ensure we catch any classes that end up with underscores after all modifications.
    /// </summary>
    private static void AddPragmasForUnderscoreNames(CodeElement generatedCode, ALConfiguration alConfig, ALConventionService conventionService)
    {
        DeepCrawlTree(generatedCode, element =>
        {
            if (element is CodeClass c)
            {
                if (c.Name.Contains('_', StringComparison.Ordinal))
                {
                    c.CustomData["pragmas"] = c.CustomData.TryGetValue("pragmas", out var existingPragmas)
                        ? $"{existingPragmas},AA0215" : "AA0215";
                }
            }
            else if (element is CodeEnum e)
            {
                if (e.Name.Contains('_', StringComparison.Ordinal))
                {
                    e.CustomData["pragmas"] = e.CustomData.TryGetValue("pragmas", out var existingPragmas)
                        ? $"{existingPragmas},AA0215" : "AA0215";
                }
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
        var usedName = property.Name;
        var reservedNames = new ALReservedNamesProvider();
        if (reservedNames.ReservedNames.Contains(usedName))
            usedName += "_";
        var method = CreateMethod($"Get-{usedName.ToFirstCharacterUpperCase()}", CodeMethodKind.Getter, (CodeClass)property.Parent!, (CodeTypeBase)property.Type.Clone());
        method.Documentation = (CodeDocumentation)property.Documentation.Clone();
        method.SimpleName = usedName.ToFirstCharacterUpperCase();
        method.CustomData["method-type"] = "Getter";
        method.CustomData["source"] = "from property";
        method.CustomData["property-name"] = property.Name;
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
        var isDictionary = property.Type.IsDictionaryType();
        var isCollection = property.Type.CollectionKind != CodeTypeBase.CodeTypeCollectionKind.None;
        var isEnum = property.Type is CodeType { TypeDefinition: CodeEnum };
        var isCodeunit = property.Type is CodeType { TypeDefinition: CodeClass };

        if (isDictionary)
        {
            method.CustomData["source-type"] = "Dictionary";
            method.CustomData["return-variable-name"] = "ReturnDict";

            // JObject for iterating the JSON object keys
            AddLocalVariable(method, "JObject", "JsonObject");
            // JToken for reading each value
            AddLocalVariable(method, "JToken", "JsonToken");
            // KeyText for the dictionary key
            AddLocalVariable(method, "KeyText", "Text");

            // Value-type-specific local variable (without the al-dictionary marker)
            if (isEnum)
                AddLocalVariable(method, "EnumValue", property, true);
            else if (isCodeunit)
                AddLocalVariable(method, "TargetCodeunit", property, true);
            else
                AddLocalVariable(method, "Value", property, true);
        }
        else if (isCollection)
        {
            method.CustomData["source-type"] = "List";
            method.CustomData["return-variable-name"] = "ReturnList";

            // Add JArray, JToken
            AddLocalVariable(method, "JArray", "JsonArray");
            AddLocalVariable(method, "JToken", "JsonToken");

            if (isCodeunit)
                AddLocalVariable(method, "TargetCodeunit", property, true);
            else if (isEnum)
                AddLocalVariable(method, "value", property, true);
        }
        else if (isEnum)
        {
            AddLocalVariable(method, "enumValue", property, false);
            AddLocalVariable(method, "Ordinals", "List of [Integer]");
            AddLocalVariable(method, "Ordinal", "Integer");
        }
        else if (isCodeunit)
        {
            AddLocalVariable(method, "TargetCodeunit", property, false);
        }
    }

    private static CodeMethod ToSetterCodeMethod(CodeProperty property, ALConfiguration alConfig)
    {
        var usedName = property.Name;
        var reservedNames = new ALReservedNamesProvider();
        if (reservedNames.ReservedNames.Contains(usedName))
            usedName += "_";
        var method = CreateVoidMethod($"Set-{usedName.ToFirstCharacterUpperCase()}", CodeMethodKind.Setter, (CodeClass)property.Parent!);
        method.Documentation = (CodeDocumentation)property.Documentation.Clone();
        method.SimpleName = usedName.ToFirstCharacterUpperCase();
        method.CustomData["method-type"] = "Setter";
        method.CustomData["source"] = "from property";
        method.CustomData["property-name"] = property.Name;
        if (!string.IsNullOrEmpty(property.SerializationName))
            method.CustomData["serialization-name"] = property.SerializationName;

        // Add the value parameter
        AddParameter(method, "p", (CodeTypeBase)property.Type.Clone());

        // For dictionary types, add JSON-object helpers for serialization
        var isDictionary = property.Type.IsDictionaryType();
        // For collection types, add iteration helpers
        var isCollection = property.Type.CollectionKind != CodeTypeBase.CodeTypeCollectionKind.None;

        if (isDictionary)
        {
            var isEnum = property.Type is CodeType { TypeDefinition: CodeEnum };
            var isCodeunit = property.Type is CodeType { TypeDefinition: CodeClass };

            AddLocalVariable(method, "JObject", "JsonObject");
            AddLocalVariable(method, "KeyText", "Text");

            if (isEnum)
                AddLocalVariable(method, "EnumValue", property, true);
            else if (isCodeunit)
                AddLocalVariable(method, "TargetCodeunit", property, true);
            else
                AddLocalVariable(method, "Value", property, true);
        }
        else if (isCollection)
        {
            AddLocalVariable(method, "v", property, true);
            AddLocalVariable(method, "JArray", "JsonArray");
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
                codeClass.AddMethod(CreateDefaultInitMethod(codeClass));

                // Add Configuration getter, setter and DefaultConfiguration
                AddConfigurationMethods(codeClass);

                // Add Response getter and setter
                codeClass.AddMethod(CreateResponseGetterMethod(codeClass));
                codeClass.AddMethod(CreateResponseSetterMethod(codeClass));

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

                // Add SetBody overloads
                AddSetBodyMethods(codeClass);

                // Add ValidateBody
                var validateBody = CreateVoidMethod("ValidateBody", CodeMethodKind.Custom, codeClass, AccessModifier.Private);
                validateBody.CustomData["pragmas-variables"] = "AA0202";
                // Add local variables; we need to add variables for each property in the class to validate presence of required properties; the properties were previously added as getter- and setter-methods, so we can find them by looking for getter methods with source "from property"
                var gettersHelper = codeClass.Methods.Where(m => m.IsGetterMethod() && m.CustomData.TryGetValue("source", out var source) && source.Equals("from property", StringComparison.Ordinal)).ToList();
                foreach (var getter in gettersHelper)
                {
                    var propName = getter.SimpleName.ToFirstCharacterLowerCase() ?? getter.Name.ToFirstCharacterLowerCase();
                    var varName = $"{propName}";
                    AddLocalVariable(validateBody, varName, (CodeTypeBase)getter.ReturnType.Clone());
                }
                validateBody.CustomData["sorting-value"] = "6";
                validateBody.CustomData["source"] = "validate-body";
                codeClass.AddMethod(validateBody);

                // Add ToJson (simple)
                var toJson1 = CreateMethod("ToJson", CodeMethodKind.Serializer, codeClass, "JsonObject");
                toJson1.CustomData["sorting-value"] = "103";
                codeClass.AddMethod(toJson1);

                // Add ToJson (with parameters - overload)
                var getters = codeClass.Methods.Where(m => m.IsGetterMethod()).ToList();
                if (getters.Count != 0)
                {
                    var toJson2 = CreateMethod("ToJson-overload", CodeMethodKind.Serializer, codeClass, "JsonObject");
                    toJson2.SimpleName = "ToJson";
                    toJson2.CustomData["sorting-value"] = "104";
                    toJson2.CustomData["pragmas"] = "AA0245";

                    // Add TargetJson local var
                    AddLocalVariable(toJson2, "TargetJson", "JsonObject");

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
                        param.CustomData["property-name"] = getter.CustomData["property-name"];
                        if (paramType.IsDictionaryType())
                        {
                            var singularName = CodeMethodExtensions.GetSingularName(paramName, toJson2.Parameters);
                            param.CustomData["key-variable"] = $"{singularName}Key";
                            param.CustomData["value-variable"] = $"{singularName}Value";
                            param.CustomData["object-variable"] = $"{paramName}Object";

                            AddLocalVariable(toJson2, $"{singularName}Key", "Text");

                            var type = (CodeTypeBase)getter.ReturnType.Clone();
                            if (type is CodeType et) et.CustomData.Remove("al-dictionary");
                            AddLocalVariable(toJson2, $"{singularName}Value", type);

                            AddLocalVariable(toJson2, $"{paramName}Object", "JsonObject");
                        }
                        // For codeunit collections, add array + foreach vars
                        else if (paramType.CollectionKind != CodeTypeBase.CodeTypeCollectionKind.None &&
                            paramType is CodeType pt && pt.TypeDefinition is CodeClass)
                        {
                            var singularName = CodeMethodExtensions.GetSingularName(paramName, toJson2.Parameters);
                            param.CustomData["foreach-variable"] = singularName;

                            var arrayName = $"{paramName}Array";
                            param.CustomData["corresponding-array"] = arrayName;

                            AddLocalVariable(toJson2, arrayName, "JsonArray");

                            var elementType = (CodeTypeBase)paramType.Clone();
                            elementType.CollectionKind = CodeTypeBase.CodeTypeCollectionKind.None;
                            AddLocalVariable(toJson2, singularName, elementType);
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
                var setConfig = CreateSkippableMethod("SetConfiguration", CodeMethodKind.RawUrlBuilder, codeClass, "void", "2");
                AddParameter(setConfig, "NewReqConfig", $"Codeunit {alConfig.ClientNamespace}.\"Kiota ClientConfig\"");
                codeClass.AddMethod(setConfig);

                // Handle indexers — check for methods converted from indexers
                var indexerMethods = codeClass.Methods
                    .Where(m => m.Kind == CodeMethodKind.IndexerBackwardCompatibility && m.OriginalIndexer is not null)
                    .ToList();

                foreach (var property in codeClass.Properties.Where(p => p.IsOfKind(CodePropertyKind.RequestBuilder)))
                {
                    var propertyTypeNamespace = ((CodeType)property.Type).TypeDefinition?.GetImmediateParentOfType<CodeNamespace>();
                    ArgumentNullException.ThrowIfNull(propertyTypeNamespace);
                    if (!codeClass.Usings.Any(x => x.Name.Equals(propertyTypeNamespace.Name, StringComparison.OrdinalIgnoreCase)))
                        codeClass.AddUsing(new CodeUsing { Name = propertyTypeNamespace.Name });
                    codeClass.RemoveChildElement(property);
                    codeClass.AddMethod(ToGetterCodeMethod(property, alConfig));
                }

                if (indexerMethods.Count != 0)
                {
                    var indexerMethod = indexerMethods[0];
                    var indexerParamType = indexerMethod.Parameters.FirstOrDefault()?.Type;
                    var indexerTypeAsClass = ((Kiota.Builder.CodeDOM.CodeType)indexerMethod.ReturnType).TypeDefinition as CodeClass;

                    if (indexerParamType is not null)
                    {
                        // Add SetIdentifier method
                        var setId = CreateSkippableMethod("SetIdentifier", CodeMethodKind.RawUrlBuilder, codeClass, "void", "3");
                        AddParameter(setId, "NewIdentifier", (CodeTypeBase)indexerParamType.Clone());
                        var idParam = setId.Parameters.First(p => p.Name == "NewIdentifier");
                        if (idParam.Type is CodeType ct && conventionService.GetTypeString(ct, setId).Equals("Guid", StringComparison.OrdinalIgnoreCase))
                        {
                            AddLocalVariable(setId, "JsonHelper", CodeTypeBaseExtensions.CreateExternal($"Codeunit {alConfig.CompanionNamespace}.Utilities.\"JSON Helper\""));
                        }
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

    #region Step 5.5: Value-wrapper convenience overloads
    /// <summary>
    /// Detects model classes that are "value wrappers" (single primitive property named "value")
    /// and adds convenience getter/setter overloads to parent classes that reference them.
    /// The getter overload has a "_Value" suffix; the setter overload takes the primitive type directly.
    /// </summary>
    private static void AddValueWrapperConvenienceOverloads(CodeElement generatedCode)
    {
        // Phase 1: Collect all value-wrapper classes
        var wrapperClasses = new HashSet<CodeClass>();
        DeepCrawlTree(generatedCode, element =>
        {
            if (element is CodeClass c && IsValueWrapperClass(c))
                wrapperClasses.Add(c);
        });

        if (wrapperClasses.Count == 0) return;

        // Phase 2: For each model class, find getters/setters that return a wrapper and add convenience overloads
        DeepCrawlTree(generatedCode, element =>
        {
            if (element is not CodeClass parentClass || !parentClass.IsOfKind(CodeClassKind.Model))
                return;

            var methodsToAdd = new List<CodeMethod>();

            foreach (var getter in parentClass.Methods.Where(m => m.IsGetterMethod()).ToList())
            {
                if (getter.ReturnType is not CodeType ct || ct.TypeDefinition is not CodeClass wrapperClass)
                    continue;
                if (!wrapperClasses.Contains(wrapperClass))
                    continue;

                // Find the inner "value" getter to determine the primitive type
                var innerGetter = wrapperClass.Methods.FirstOrDefault(m => m.IsGetterMethod());
                if (innerGetter is null) continue;

                var primitiveType = (CodeTypeBase)innerGetter.ReturnType.Clone();
                var methodBaseName = getter.SimpleName ?? getter.Name;

                // Convenience getter: FirstName_Value() : Text
                var convenienceGetter = CreateMethod($"{methodBaseName}_Value", CodeMethodKind.Getter, parentClass, primitiveType);
                convenienceGetter.Documentation = new CodeDocumentation
                {
                    DescriptionTemplate = $"Convenience helper that unwraps the value from the {methodBaseName} wrapper object.",
                };
                convenienceGetter.SimpleName = $"{methodBaseName}_Value";
                convenienceGetter.CustomData["method-type"] = "Getter";
                convenienceGetter.CustomData["source"] = "value-wrapper-getter";
                convenienceGetter.CustomData["wrapper-getter-name"] = methodBaseName;
                if (getter.CustomData.TryGetValue("serialization-name", out var serName))
                    convenienceGetter.CustomData["serialization-name"] = serName;
                methodsToAdd.Add(convenienceGetter);

                // Convenience setter: FirstName(p: Text)
                var convenienceSetter = CreateVoidMethod($"{methodBaseName}", CodeMethodKind.Setter, parentClass);
                convenienceSetter.Documentation = new CodeDocumentation
                {
                    DescriptionTemplate = $"Convenience helper that wraps the value into a {methodBaseName} wrapper object.",
                };
                convenienceSetter.SimpleName = $"{methodBaseName}";
                convenienceSetter.CustomData["method-type"] = "Setter";
                convenienceSetter.CustomData["source"] = "value-wrapper-setter";
                convenienceSetter.CustomData["wrapper-getter-name"] = methodBaseName;
                convenienceSetter.CustomData["wrapper-class-name"] = wrapperClass.Name;

                AddParameter(convenienceSetter, "p", (CodeTypeBase)primitiveType.Clone());

                // Add a local variable for the wrapper codeunit
                AddLocalVariable(convenienceSetter, "Wrapper", (CodeTypeBase)getter.ReturnType.Clone());

                methodsToAdd.Add(convenienceSetter);
            }

            foreach (var m in methodsToAdd)
                parentClass.AddMethod(m);
        });
    }

    /// <summary>
    /// A value-wrapper class is a model with exactly one getter that returns a primitive type
    /// and whose serialization name (or simple name) is "value".
    /// </summary>
    private static bool IsValueWrapperClass(CodeClass c)
    {
        if (!c.IsOfKind(CodeClassKind.Model)) return false;
        var getters = c.Methods.Where(m => m.IsGetterMethod()).ToList();
        if (getters.Count != 1) return false;
        var getter = getters[0];
        var serName = getter.CustomData.TryGetValue("serialization-name", out var sn) ? sn : getter.SimpleName ?? getter.Name;
        return string.Equals(serName, "value", StringComparison.OrdinalIgnoreCase)
            && getter.ReturnType is CodeType ct
            && ct.TypeDefinition is null; // primitive type — no TypeDefinition
    }
    #endregion

    #region Step 5.6: Multipart body convenience overloads
    /// <summary>
    /// For every <see cref="CodeMethodKind.RequestExecutor"/> with a matching 
    /// <c>multipart-file-fields</c> entry on the request-body parameter, this step adds one
    /// additional overload per file field.  Each overload replaces the generic
    /// <c>Codeunit "Kiota File Body"</c> parameter with a <c>InStream</c> parameter so callers
    /// can pass a stream directly without having to construct the wrapper manually.
    ///
    /// The overloads are named with a <c>-overload</c> suffix (stripped by
    /// <see cref="ModifyOverloadMethodNames"/> in Step 8) and carry two pieces of
    /// <see cref="CodeElement.CustomData"/>:
    /// <list type="bullet">
    ///   <item><c>multipart-field-name</c> — the exact form-field name from the OpenAPI spec
    ///         (e.g. <c>rebFile</c>) that the AL writer should pass to <c>SetFileBody</c>.</item>
    ///   <item><c>source</c> — <c>multipart-overload</c>, used by the writer to emit the correct body.</item>
    /// </list>
    /// </summary>
    private static void AddMultipartBodyConvenienceOverloads(CodeElement generatedCode)
    {
        var methodsToAdd = new List<(CodeClass Parent, CodeMethod Method)>();

        DeepCrawlTree(generatedCode, element =>
        {
            if (element is not CodeMethod executor || executor.Kind != CodeMethodKind.RequestExecutor)
                return;
            if (executor.Parent is not CodeClass parentClass)
                return;
            var bodyParam = executor.Parameters.OfKind(CodeParameterKind.RequestBody);
            if (bodyParam is null) return;
            if (!executor.Parameters.Any(p => p.Type is CodeType ct && ct.IsKiotaFileBodyType()))
                return;
            if (!bodyParam.CustomData.TryGetValue("multipart-file-fields", out var fieldsCsv) ||
                string.IsNullOrEmpty(fieldsCsv))
                return;

            foreach (var fieldName in fieldsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                methodsToAdd.Add((parentClass, CreateMultipartOverload(executor, fieldName, 1, "InStream")));
                methodsToAdd.Add((parentClass, CreateMultipartOverload(executor, fieldName, 2, "Text")));
            }
        });

        foreach (var (parent, method) in methodsToAdd)
            parent.AddMethod(method);
    }

    /// <summary>
    /// Clones <paramref name="executor"/>, strips its body/local parameters, then appends a
    /// <c>Filename</c> parameter and a <c>FileBody</c> parameter of type
    /// <paramref name="fileBodyTypeName"/> for the given multipart <paramref name="fieldName"/>.
    /// </summary>
    private static CodeMethod CreateMultipartOverload(
        CodeMethod executor, string fieldName, int overloadIndex, string fileBodyTypeName)
    {
        var overload = (executor.Clone() as CodeMethod)!;
        overload.SimpleName = overload.Name;
        overload.Name += $"-overload-{overloadIndex}";
        overload.CustomData["source"] = "multipart-overload";
        overload.CustomData["multipart-field-name"] = fieldName;

        // Rebuild the parameter list: drop the original body and all local variables,
        // then append the field-specific Filename + FileBody parameters.
        var originalParams = overload.Parameters;
        overload.ClearParameters();
        foreach (var param in originalParams)
        {
            if (param.Kind == CodeParameterKind.RequestBody || param.IsLocalVariable())
                continue;
            overload.AddParameter((CodeParameter)param.Clone());
        }

        overload.AddParameter(new CodeParameter
        {
            Name = "Filename",
            Kind = CodeParameterKind.Custom,
            Type = new CodeType { Name = "Text", IsExternal = true },
            Documentation = new CodeDocumentation { DescriptionTemplate = $"The filename for form field '{fieldName}'." },
        });
        overload.AddParameter(new CodeParameter
        {
            Name = "FileBody",
            Kind = CodeParameterKind.RequestBody,
            Optional = false,
            Type = new CodeType { Name = fileBodyTypeName, IsExternal = true },
            Documentation = new CodeDocumentation { DescriptionTemplate = $"The file content for form field '{fieldName}'." },
        });
        AddLocalVariable(overload, "body", "Codeunit \"Kiota File Body\"");

        return overload;
    }
    #endregion

    #region Step 6: Request Executor Enhancement
    private static void UpdateRequestExecutorMethods(CodeElement generatedCode, ALConfiguration alConfig, ALConventionService conventionService, ALObjectIdProvider objectIdProvider)
    {
        DeepCrawlTree(generatedCode, element =>
        {
            if (element is CodeMethod method && method.Kind == CodeMethodKind.RequestExecutor &&
                method.Parent is CodeClass parentClass &&
                !method.IsConvenienceOverload()) // skip multipart overloads since they will be implemented by calling the main executor method
            {
                // Add RequestHandler local variable
                AddLocalVariable(method, "RequestHandler", CodeTypeBaseExtensions.CreateExternal($"Codeunit {alConfig.ClientNamespace}.\"Kiota RequestHandler\""));

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
                        AddLocalVariable(method, "BodyElement", bodyElementType);
                        AddLocalVariable(method, "BodyObjects", $"List of [Interface \"Kiota IModelClass\"]");

                        AddUsing(parentClass, alConfig.DefinitionsNamespace);
                    }
                }
                if (parentClass.Documentation?.DescriptionTemplate.Contains(@"\conversion\ava\excel", StringComparison.OrdinalIgnoreCase) == true)
                {
                    // just for debugging
                    int i = 0;
                    string s = i.ToString(CultureInfo.InvariantCulture);
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
                        AddLocalVariable(method, "TargetType", elementType);
                        AddLocalVariable(method, "SubToken", "JsonToken");
                        AddLocalVariable(method, "ResponseAsArray", "JsonArray");
                        AddLocalVariable(method, "i", "Integer");
                    }
                    else if (ct.TypeDefinition is CodeClass)
                    {
                        // Single codeunit return
                        method.CustomData["return-variable-name"] = "Target";
                    }
                    else if (conventionService.GetTypeString(ct, method).Equals("InStream", StringComparison.OrdinalIgnoreCase))
                    {
                        // Stream return
                        method.CustomData["return-variable-name"] = "InStr";
                    }
                }

                // Handle query parameters → parameter codeunit
                var queryParamsClass = parentClass.InnerClasses.FirstOrDefault(c => c.IsOfKind(CodeClassKind.QueryParameters));

                if (queryParamsClass is not null)
                {
                    var queryParamProperties = queryParamsClass.Properties
                        .Where(p => p.Kind == CodePropertyKind.QueryParameter)
                        .ToList();

                    if (queryParamProperties.Count > 0)
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
                            paramClass.CustomData["object-id"] = objectIdProvider.GetNextCodeunitId().ToString(CultureInfo.InvariantCulture);
                            parentNs.AddClass(paramClass);

                            // Using for the client namespace (where Kiota Query Param Formatter lives)
                            AddUsing(paramClass, alConfig.ClientNamespace);

                            // Access = Internal object property (SetDefaultObjectProperties ran before this class existed)
                            if (alConfig.MarkInternal)
                                AddObjectProperty(paramClass, "Access", "Internal");

                            // Global variables
                            AddGlobalVariable(paramClass, "QueryParamFormatter", $"Codeunit {alConfig.ClientNamespace}.\"Kiota Query Param Formatter\"", "1");
                            AddGlobalVariable(paramClass, "QueryParameters", "Dictionary of [Text, Text]", "2");

                            // SetQueryParameter(QueryKey: Text; QueryValue: Text) method
                            var setQueryParamMethod = CreateVoidMethod("SetQueryParameter", CodeMethodKind.Custom, paramClass);
                            setQueryParamMethod.CustomData["source"] = "query-param-generic-setter";
                            setQueryParamMethod.CustomData["sorting-value"] = "1";
                            AddParameter(setQueryParamMethod, "QueryKey", "Text");
                            AddParameter(setQueryParamMethod, "QueryValue", "Text");
                            paramClass.AddMethod(setQueryParamMethod);

                            // Add usings for any referenced types (e.g. deduplicated enums) whose
                            // TypeDefinition lives in a namespace different from the one this
                            // parameter codeunit is being placed in.
                            // Also add one typed Set{Name}(Value: <type>) method per query parameter.
                            foreach (var qp in queryParamProperties)
                            {
                                if (qp.Type is CodeType qpType && qpType.TypeDefinition is not null)
                                {
                                    try
                                    {
                                        var typeNs = qpType.TypeDefinition.GetImmediateParentOfType<CodeNamespace>();
                                        if (!typeNs.Name.Equals(parentNs.Name, StringComparison.OrdinalIgnoreCase))
                                            AddUsing(paramClass, typeNs.Name);
                                    }
                                    catch (InvalidOperationException) { }
                                }

                                // Determine type category for writer formatting
                                string typeCategory;
                                var alTypeName = conventionService.GetTypeString(qp.Type, qp);
                                if (alTypeName.Equals("Text", StringComparison.OrdinalIgnoreCase))
                                    typeCategory = "text";
                                else if (qp.Type is CodeType { TypeDefinition: CodeEnum })
                                    typeCategory = "enum";
                                else
                                    typeCategory = "primitive";

                                var typedSetterMethod = CreateVoidMethod($"Set{qp.Name.ToFirstCharacterUpperCase()}", CodeMethodKind.Custom, paramClass);
                                typedSetterMethod.CustomData["source"] = "query-param-typed-setter";
                                typedSetterMethod.CustomData["sorting-value"] = "2";
                                typedSetterMethod.CustomData["query-param-name"] = qp.Name;
                                typedSetterMethod.CustomData["query-param-type-category"] = typeCategory;
                                AddParameter(typedSetterMethod, "Value", (CodeTypeBase)qp.Type.Clone());
                                paramClass.AddMethod(typedSetterMethod);
                            }

                            // GetQueryParameters(): Dictionary of [Text, Text] method
                            var getQueryParamsMethod = CreateMethod("GetQueryParameters", CodeMethodKind.Custom, paramClass, "Dictionary of [Text, Text]");
                            getQueryParamsMethod.CustomData["source"] = "query-param-getter";
                            getQueryParamsMethod.CustomData["sorting-value"] = "3";
                            paramClass.AddMethod(getQueryParamsMethod);

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
        if (!alConfig.GenerateInterfaces)
            return;
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

    private static void AddReadmeAsCodeFunction(CodeElement generatedCode, ALConfiguration alConfig, GenerationConfiguration configuration)
    {
        if (generatedCode is CodeNamespace rootNs)
        {
            var holderClass = new CodeClass { Name = "_ReadmeHolder" };
            rootNs.AddClass(holderClass);

            var method = new CodeMethod
            {
                Name = "Readme",
                Kind = CodeMethodKind.Custom,
                IsStatic = true,
                ReturnType = new CodeType { Name = "void", IsExternal = true },
            };
            holderClass.AddMethod(method);

            var readmeFunc = new CodeFunction(method);

            var usings = new List<CodeUsing>();
            AddConfigUsing(usings, "OpenAPIFilePath", configuration.OpenAPIFilePath);
            AddConfigUsing(usings, "Language", configuration.Language.ToString());
            AddConfigUsing(usings, "ClientClassName", configuration.ClientClassName);
            AddConfigUsing(usings, "ClientNamespaceName", configuration.ClientNamespaceName);
            AddConfigUsing(usings, "OutputPath", configuration.OutputPath);
            AddConfigUsing(usings, "ObjectPrefix", alConfig.ObjectPrefix);
            AddConfigUsing(usings, "ObjectSuffix", alConfig.ObjectSuffix);
            AddConfigUsing(usings, "IDRangeStart", alConfig.ObjectIdRangeStart.ToString(CultureInfo.InvariantCulture));
            AddConfigUsing(usings, "IDRangeEnd", alConfig.ObjectIdRangeEnd.ToString(CultureInfo.InvariantCulture));
            AddConfigUsing(usings, "CompanionNamespace", alConfig.CompanionNamespace);
            AddConfigUsing(usings, "GenerateInterfaces", alConfig.GenerateInterfaces.ToString(CultureInfo.InvariantCulture));

            if (configuration.IncludePatterns.Count > 0)
                AddConfigUsing(usings, "IncludePatterns", string.Join(",", configuration.IncludePatterns));
            if (configuration.ExcludePatterns.Count > 0)
                AddConfigUsing(usings, "ExcludePatterns", string.Join(",", configuration.ExcludePatterns));

            foreach (var u in usings)
                readmeFunc.AddUsing(u);

            rootNs.AddFunction(readmeFunc);
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
                if (method.SimpleName != null)
                    method.Name = method.SimpleName; // reset to original name so that the writer can apply AL naming conventions (e.g. PascalCase)
            }
        });
    }

    private static void UpdateMethodParameters(CodeElement generatedCode)
    {
        var reservedNames = new ALReservedNamesProvider();
        DeepCrawlTree(generatedCode, element =>
        {
            if (element is CodeMethod method)
            {
                foreach (var param in method.Parameters)
                {
                    if (reservedNames.ReservedNames.Contains(param.Name))
                    {
                        param.CustomData["property-name"] = param.Name;
                        param.Name += "_";
                    }
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
    private static void AddLocalVariable(CodeMethod method, string name, CodeProperty fromProperty, bool clearCollectionInformation)
    {
        var genericType = (CodeTypeBase)fromProperty.Type.Clone();
        if (clearCollectionInformation)
        {
            if (genericType is CodeType gt) gt.CustomData.Remove("al-dictionary");
            genericType.CollectionKind = CodeTypeBase.CodeTypeCollectionKind.None;
        }
        AddLocalVariable(method, name, genericType);

    }
    private static void AddLocalVariable(CodeMethod method, string name, CodeTypeBase type)
    {
        var param = new CodeParameter
        {
            Name = name,
            Kind = CodeParameterKind.Custom,
            Type = type,
        };
        param.CustomData["local-variable"] = "true";
        method.AddParameter(param);
    }

    private static void AddLocalVariable(CodeMethod method, string name, string externalTypeName)
        => AddLocalVariable(method, name, CodeTypeBaseExtensions.CreateExternal(externalTypeName));

    private static void AddObjectProperty(CodeClass codeClass, string name, string value)
    {
        var prop = new CodeProperty
        {
            Name = name,
            Kind = CodePropertyKind.Custom,
            Type = CodeTypeBaseExtensions.CreateExternal("ObjectProperty"),
        };
        prop.CustomData["object-property"] = "true";
        prop.CustomData["value"] = value;
        prop.CustomData["locked"] = "true";
        codeClass.AddProperty(prop);
    }

    private static void AddObjectOption(CodeEnum codeEnum, string name, string value)
    {
        var option = new CodeEnumOption { Name = name };
        option.CustomData["object-property"] = "true";
        option.CustomData["value"] = value;
        option.CustomData["locked"] = "true";
        codeEnum.AddOption(option);
    }

    private static CodeMethod CreateMethod(
        string name, CodeMethodKind kind, CodeClass parent,
        string returnTypeName, AccessModifier access = AccessModifier.Public)
    {
        return new CodeMethod
        {
            Name = name,
            Kind = kind,
            Access = access,
            ReturnType = CodeTypeBaseExtensions.CreateExternal(returnTypeName),
            Parent = parent,
        };
    }

    private static CodeMethod CreateMethod(
        string name, CodeMethodKind kind, CodeClass parent,
        CodeTypeBase returnType, AccessModifier access = AccessModifier.Public)
    {
        return new CodeMethod
        {
            Name = name,
            Kind = kind,
            Access = access,
            ReturnType = returnType,
            Parent = parent,
        };
    }

    private static CodeMethod CreateVoidMethod(
        string name, CodeMethodKind kind, CodeClass parent,
        AccessModifier access = AccessModifier.Public)
        => CreateMethod(name, kind, parent, "void", access);

    private static CodeMethod CreateSkippableMethod(
        string name, CodeMethodKind kind, CodeClass parent,
        string returnTypeName, string sortingValue,
        AccessModifier access = AccessModifier.Public)
    {
        var method = CreateMethod(name, kind, parent, returnTypeName, access);
        method.CustomData["skip"] = "false";
        method.CustomData["sorting-value"] = sortingValue;
        return method;
    }
    private static CodeMethod CreateDefaultInitMethod(CodeClass parent)
    {
        var initMethod = CreateSkippableMethod("Initialize", CodeMethodKind.Constructor, parent, "void", "1");
        AddParameter(initMethod, "NewAPIAuthorization", $"Codeunit \"Kiota API Authorization\"");
        return initMethod;
    }

    private static void AddConfigurationMethods(CodeClass parent)
    {
        var configGetter = CreateSkippableMethod("Configuration", CodeMethodKind.ClientConstructor, parent, $"Codeunit \"Kiota ClientConfig\"", "27");
        parent.AddMethod(configGetter);

        var configSetter = CreateSkippableMethod("Configuration-overload", CodeMethodKind.ClientConstructor, parent, "void", "28");
        configSetter.SimpleName = "Configuration";
        AddParameter(configSetter, "config", $"Codeunit \"Kiota ClientConfig\"");
        parent.AddMethod(configSetter);

        var defaultConfig = CreateSkippableMethod("DefaultConfiguration", CodeMethodKind.Factory, parent, $"Codeunit \"Kiota ClientConfig\"", "29", AccessModifier.Private);
        parent.AddMethod(defaultConfig);
    }

    private static CodeMethod CreateResponseGetterMethod(CodeClass parent)
    {
        var method = CreateMethod("Response", CodeMethodKind.Custom, parent, "Codeunit System.RestClient.\"Http Response Message\"");
        method.CustomData["sorting-value"] = "50";
        method.CustomData["source"] = "response-getter";
        return method;
    }

    private static CodeMethod CreateResponseSetterMethod(CodeClass parent)
    {
        var method = CreateVoidMethod("Response-overload", CodeMethodKind.Custom, parent);
        method.SimpleName = "Response";
        method.CustomData["sorting-value"] = "51";
        method.CustomData["source"] = "response-setter";
        method.AddParameter(new CodeParameter
        {
            Name = "var ApiResponse", // 'var' to pass it by reference in AL
            Kind = CodeParameterKind.Custom,
            Type = new CodeType { Name = "Codeunit System.RestClient.\"Http Response Message\"", IsExternal = true },
        });
        return method;
    }

    private static void AddSetBodyMethods(CodeClass parent)
    {
        var setBody1 = CreateVoidMethod("SetBody", CodeMethodKind.Deserializer, parent);
        setBody1.CustomData["sorting-value"] = "100";
        AddParameter(setBody1, "NewJsonBody", "JsonObject");
        parent.AddMethod(setBody1);

        var setBody2 = CreateVoidMethod("SetBody-overload", CodeMethodKind.Deserializer, parent);
        setBody2.SimpleName = "SetBody";
        setBody2.CustomData["sorting-value"] = "101";
        setBody2.AddParameter(new CodeParameter { Name = "NewJsonBody", Kind = CodeParameterKind.Custom, Type = new CodeType { Name = "JsonObject", IsExternal = true }, DefaultValue = "1" });
        setBody2.AddParameter(new CodeParameter { Name = "Debug", Kind = CodeParameterKind.Custom, Type = new CodeType { Name = "Boolean", IsExternal = true }, DefaultValue = "2" });
        parent.AddMethod(setBody2);
    }
    private static void AddParameter(CodeMethod method, string name, string externalTypeName)
    {
        method.AddParameter(new CodeParameter
        {
            Name = name,
            Kind = CodeParameterKind.Custom,
            Type = CodeTypeBaseExtensions.CreateExternal(externalTypeName),
        });
    }

    private static void AddParameter(CodeMethod method, string name, CodeTypeBase type)
    {
        method.AddParameter(new CodeParameter
        {
            Name = name,
            Kind = CodeParameterKind.Custom,
            Type = type,
        });
    }
    #endregion
}
