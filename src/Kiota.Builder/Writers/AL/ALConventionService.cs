using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Kiota.Builder.CodeDOM;
using Kiota.Builder.Extensions;
using Kiota.Builder.Refiners;

namespace Kiota.Builder.Writers.AL;

public class ALConventionService : CommonLanguageConventionService
{
    private readonly ALConfiguration _alConfig;

    public ALConventionService(ALConfiguration alConfig)
    {
        _alConfig = alConfig ?? new ALConfiguration();
    }

    public override string StreamTypeName => "HttpContent";
    public override string VoidTypeName => string.Empty;
    public override string DocCommentPrefix => "/// ";
    public override string ParseNodeInterfaceName => "JsonObject";
    public override string TempDictionaryVarName => "QueryParameters";

    public ALConfiguration AlConfig => _alConfig;

    #region Abbreviation Dictionary
    private static readonly Dictionary<string, string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Action"] = "Act",
        ["Alignment"] = "Algnmt",
        ["Avatar"] = "Ava",
        ["Blocking"] = "Block",
        ["Builder"] = "Bldr",
        ["Button"] = "Btn",
        ["Capture"] = "Cpt",
        ["Categories"] = "Cats",
        ["Category"] = "Cat",
        ["Certificate"] = "Cert",
        ["Certificates"] = "Certs",
        ["Channel"] = "Chnl",
        ["Children"] = "Chld",
        ["Classification"] = "Class",
        ["Collection"] = "Coll",
        ["Config"] = "Cfg",
        ["Configuration"] = "Cfg",
        ["Connection"] = "Conn",
        ["Connections"] = "Conns",
        ["Contact"] = "Cont",
        ["Currency"] = "Curr",
        ["Custom"] = "Cust",
        ["Customer"] = "Cust",
        ["Data"] = "Dt",
        ["Default"] = "Def",
        ["Definition"] = "Def",
        ["Delivery"] = "Dlv",
        ["Department"] = "Dept",
        ["Dependency"] = "Dep",
        ["Dependent"] = "Dep",
        ["Describe"] = "Desc",
        ["Description"] = "Desc",
        ["Destination"] = "Dest",
        ["Details"] = "Dtl",
        ["Developer"] = "Dev",
        ["Development"] = "Dev",
        ["Device"] = "Dev",
        ["Dictionary"] = "Dict",
        ["Directory"] = "Dir",
        ["Discount"] = "Disc",
        ["Distribution"] = "Dist",
        ["Division"] = "Div",
        ["Document"] = "Doc",
        ["Documents"] = "Docs",
        ["Download"] = "Dwld",
        ["Entity"] = "Ent",
        ["Error"] = "Err",
        ["Event"] = "Evt",
        ["Exception"] = "Ex",
        ["Exchange"] = "Exch",
        ["Extended"] = "Ext",
        ["Extension"] = "Ext",
        ["Field"] = "Fld",
        ["Folder"] = "Fld",
        ["Global"] = "Glb",
        ["History"] = "Hist",
        ["Integration"] = "Intg",
        ["Keyword"] = "Key",
        ["Language"] = "Lang",
        ["Machine"] = "Mch",
        ["Media"] = "Med",
        ["Message"] = "Msg",
        ["Method"] = "Meth",
        ["Microsoft"] = "Ms",
        ["Navigation"] = "Nav",
        ["Notification"] = "Notf",
        ["Number"] = "Num",
        ["Object"] = "Obj",
        ["Order"] = "Odr",
        ["Original"] = "Orig",
        ["Override"] = "Ovrd",
        ["Parameter"] = "Param",
        ["Parameters"] = "Params",
        ["Payment"] = "Pmt",
        ["Position"] = "Pos",
        ["Product"] = "Prod",
        ["Promotion"] = "Prmt",
        ["Property"] = "Prop",
        ["Query"] = "Qry",
        ["Recovery"] = "Rcvry",
        ["Reference"] = "Ref",
        ["Referenced"] = "Ref",
        ["Refund"] = "Rfd",
        ["Regulation"] = "Reg",
        ["Relation"] = "Rel",
        ["Relationship"] = "Rel",
        ["Request"] = "Req",
        ["Response"] = "Rsp",
        ["Result"] = "Rslt",
        ["Sales"] = "Sls",
        ["Section"] = "Sect",
        ["Sequence"] = "Seq",
        ["Service"] = "Svc",
        ["Shipping"] = "Shp",
        ["Stream"] = "Strm",
        ["Transaction"] = "Txn",
        ["Transform"] = "Trans",
        ["User"] = "Usr",
        ["Version"] = "Ver",
        ["Wishlist"] = "WList",
    };
    #endregion

    #region Global Name Tracking
    private readonly HashSet<string> _allNames = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Stable object-map key -> previously-assigned final AL name, for active
    /// (non-tombstoned) entries only. Populated via <see cref="SeedFromMap"/>.</summary>
    private readonly Dictionary<string, string> _existingNamesByKey = new(StringComparer.Ordinal);
    #endregion

    /// <summary>
    /// Seeds this service with previously-assigned names from a persisted object map, before any new
    /// names are deduplicated this run. Every name in the map (active or tombstoned) is reserved into
    /// <see cref="_allNames"/> so it can never be handed to a different, unrelated object; only
    /// active entries become resolvable via <see cref="TryGetExistingName"/>.
    /// </summary>
    public void SeedFromMap(ALObjectMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        foreach (var (key, entry) in map.Objects)
        {
            _allNames.Add(entry.AssignedName);
            if (!entry.Tombstoned)
                _existingNamesByKey[key] = entry.AssignedName;
        }
    }

    /// <summary>Returns the previously-assigned final AL name for <paramref name="key"/>, if this
    /// object was seen (and not removed) in a prior generation covered by the seeded map. Callers
    /// must skip <see cref="SanitizeName"/>/<see cref="DeduplicateName"/> entirely on a hit and reuse
    /// the name verbatim.</summary>
    public bool TryGetExistingName(string key, out string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return _existingNamesByKey.TryGetValue(key, out name!);
    }

    /// <summary>Names reserved by a "reuse the cached map name verbatim" call site during THIS run
    /// (see <see cref="TryClaimExistingName"/>). Distinct from <see cref="_allNames"/>, which is
    /// pre-seeded at startup with every name the map has ever handed out and therefore can't be used
    /// to detect a same-run collision between two reuse calls (an <c>_allNames.Add</c> for either
    /// would always fail, seeded or not).</summary>
    private readonly HashSet<string> _namesClaimedThisRun = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Call this before honoring a <see cref="TryGetExistingName"/> hit. Returns <see langword="true"/>
    /// the first time <paramref name="name"/> is claimed in the current run, reserving it so a second,
    /// unrelated object that also resolves to the identical cached name (a corrupt/stale map entry -
    /// e.g. two distinct objects were saved under different keys but with the same
    /// <c>assignedName</c>, which must never happen going forward but can exist in an old map file)
    /// is told to fall back to fresh <see cref="SanitizeName"/>/<see cref="DeduplicateName"/>
    /// treatment instead of silently reusing the same, already-taken AL object name.
    /// </summary>
    public bool TryClaimExistingName(string name) => _namesClaimedThisRun.Add(name);

    /// <summary>
    /// Maps special Kiota abstraction type names to their fixed AL external counterparts.
    /// Centralizes the type mapping instead of inlining name comparisons in <see cref="GetTypeString"/>.
    /// </summary>
    private static readonly Dictionary<string, string> SpecialExternalTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MultipartBody"] = "Codeunit \"Kiota File Body\"",
    };

    public override string TranslateType(CodeType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.Name.ToLowerInvariant() switch
        {
            "integer" => "Integer",
            "boolean" => "Boolean",
            "string" => "Text",
            "untypednode" => "Text",
            "int64" => "BigInteger",
            "sbyte" or "byte" => "Byte",
            "float" or "double" or "decimal" => "Decimal",
            "binary" or "base64" or "base64url" => "InStream", // will need separate handling for serialization/deserialization
            "date" or "dateonly" => "Date",
            "time" or "timeonly" => "Time",
            "datetime" or "datetimeoffset" => "DateTime",
            "void" => string.Empty,
            "guid" => "Guid",
            "timespan" => "Duration",
            _ => type.Name.ToFirstCharacterUpperCase(),
        };
    }

    public override string GetTypeString(CodeTypeBase code, CodeElement targetElement, bool includeCollectionInformation = true, LanguageWriter? writer = null)
    {
        if (code is CodeComposedTypeBase)
            return "JsonToken";

        if (code is CodeType codeType)
        {
            if (SpecialExternalTypeNames.TryGetValue(codeType.Name, out var mappedExternalName))
                codeType = new CodeType { Name = mappedExternalName, IsExternal = true };
            string typeName;
            if (codeType.TypeDefinition is CodeClass or CodeEnum)
                typeName = codeType.TypeDefinition.GetFullALName();
            else
                typeName = TranslateType(codeType);

            // AL dictionary type: Dictionary of [Text, <valueType>]
            if (codeType.IsDictionaryType())
                return $"Dictionary of [Text, {typeName}]";

            if (includeCollectionInformation && code.CollectionKind != CodeTypeBase.CodeTypeCollectionKind.None)
                return $"List of [{typeName}]";

            return typeName;
        }

        return TranslateType(code);
    }

    public override string GetAccessModifier(AccessModifier access)
    {
        return access switch
        {
            AccessModifier.Internal => "internal ",
            AccessModifier.Public => string.Empty,
            AccessModifier.Private => "local ",
            _ => string.Empty,
        };
    }

    public override string GetParameterSignature(CodeParameter parameter, CodeElement targetElement, LanguageWriter? writer = null)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        var typeString = GetTypeString(parameter.Type, targetElement);
        return $"{parameter.Name}: {typeString}";
    }

    public override bool WriteShortDescription(IDocumentedElement element, LanguageWriter writer, string prefix = "", string suffix = "")
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(writer);
        if (element is not CodeElement codeElement) return false;
        if (!element.Documentation.DescriptionAvailable) return false;
        var description = element.Documentation.GetDescription(type => GetTypeString(type, codeElement));
        writer.WriteLine($"{DocCommentPrefix}<summary>{description}</summary>");
        return true;
    }

    /// <summary>
    /// Sanitizes names to fit AL's 30-character object name limit.
    /// </summary>
    public string SanitizeName(string name, CodeElement? element, int maxLength = 30)
    {
        ArgumentNullException.ThrowIfNull(name);
        maxLength -= _alConfig.ObjectPrefix.Length + _alConfig.ObjectSuffix.Length;
        if (maxLength <= 0) maxLength = 30;

        if (name.Length <= maxLength)
            return name;

        // Store original name
        if (element is not null && !element.HasData(ALCustomDataKeys.OriginalName))
            element.SetData(ALCustomDataKeys.OriginalName, name);

        // Apply abbreviations
        var abbreviated = ApplyAbbreviations(name, maxLength);

        // Truncate if still too long
        if (abbreviated.Length > maxLength)
            abbreviated = abbreviated[..maxLength];

        return abbreviated;
    }

    /// <summary>
    /// Deduplicates a name against all registered names.
    /// </summary>
    public string DeduplicateName(string name, CodeElement? element, string? parentNamespaceSegment = null, int maxLength = 30)
    {
        maxLength -= _alConfig.ObjectPrefix.Length + _alConfig.ObjectSuffix.Length;
        if (maxLength <= 0) maxLength = 30;

        if (_allNames.Add(name))
        {
            _namesClaimedThisRun.Add(name);
            return name;
        }

        // Store original name
        if (element is not null && !element.HasData(ALCustomDataKeys.OriginalName))
            element.SetData(ALCustomDataKeys.OriginalName, name);

        // Try appending namespace segment
        if (!string.IsNullOrEmpty(parentNamespaceSegment))
        {
            var withNs = parentNamespaceSegment.ToFirstCharacterUpperCase() + name;
            if (withNs.Length > maxLength)
                withNs = ApplyAbbreviations(withNs, maxLength);
            if (withNs.Length > maxLength)
                withNs = withNs[..maxLength];
            if (_allNames.Add(withNs))
            {
                _namesClaimedThisRun.Add(withNs);
                AddPragma(element, ALCustomDataKeys.PragmaCodes.NamingConvention);
                return withNs;
            }
        }

        // Try abbreviation
        var abbreviated = ApplyAbbreviations(name, maxLength);
        if (abbreviated.Length > maxLength)
            abbreviated = abbreviated[..maxLength];
        if (_allNames.Add(abbreviated))
        {
            _namesClaimedThisRun.Add(abbreviated);
            AddPragma(element, ALCustomDataKeys.PragmaCodes.NamingConvention);
            return abbreviated;
        }

        // Append incrementing number
        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{abbreviated}{i}";
            if (candidate.Length > maxLength)
                candidate = abbreviated[..Math.Max(1, maxLength - i.ToString(CultureInfo.InvariantCulture).Length)] + i.ToString(CultureInfo.InvariantCulture);
            if (_allNames.Add(candidate))
            {
                _namesClaimedThisRun.Add(candidate);
                AddPragma(element, ALCustomDataKeys.PragmaCodes.NamingConvention);
                return candidate;
            }
        }

        AddPragma(element, ALCustomDataKeys.PragmaCodes.NamingConvention);
        return name; // fallback
    }

    private static string ApplyAbbreviations(string name, int maxLength)
    {
        var result = name;
        // Sort abbreviations by full word length descending so we replace longest words first
        var sortedAbbreviations = Abbreviations
            .OrderByDescending(kvp => kvp.Key.Length)
            .ToList();

        foreach (var kvp in sortedAbbreviations)
        {
            if (result.Length <= maxLength)
                break;

            var idx = result.IndexOf(kvp.Key, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                // Preserve the case of the first character
                var replacement = idx == 0 || char.IsUpper(result[idx])
                    ? kvp.Value.ToFirstCharacterUpperCase()
                    : kvp.Value;
                result = result[..idx] + replacement + result[(idx + kvp.Key.Length)..];
            }
        }
        return result;
    }

    private static void AddPragma(CodeElement? element, string pragma)
    {
        if (element is null) return;
        var existing = element.GetData(ALCustomDataKeys.Pragmas);
        if (!string.IsNullOrEmpty(existing))
        {
            if (!existing.Contains(pragma, StringComparison.OrdinalIgnoreCase))
                element.SetData(ALCustomDataKeys.Pragmas, $"{existing},{pragma}");
        }
        else
        {
            element.SetData(ALCustomDataKeys.Pragmas, pragma);
        }
    }

    public void WritePragmaDisable(LanguageWriter writer, string? pragmas)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (!string.IsNullOrEmpty(pragmas))
            writer.WriteLine($"#pragma warning disable {pragmas}", false);
    }

    public void WritePragmaRestore(LanguageWriter writer, string? pragmas)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (!string.IsNullOrEmpty(pragmas))
            writer.WriteLine($"#pragma warning restore {pragmas}", false);
    }

    public static void WriteVariablesDeclaration(IEnumerable<ALVariable> variables, LanguageWriter writer, ALConventionService conventions, string? pragmas = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(conventions);
        var variableList = variables.ToList();
        if (variableList.Count == 0) return;

        writer.WriteLine("var");
        writer.IncreaseIndent();

        if (!string.IsNullOrEmpty(pragmas))
            writer.WriteLine($"#pragma warning disable {pragmas}", false);

        // Group variables that can be combined
        var written = new HashSet<int>();
        for (var i = 0; i < variableList.Count; i++)
        {
            if (written.Contains(i)) continue;

            var combinable = new List<ALVariable> { variableList[i] };
            for (var j = i + 1; j < variableList.Count; j++)
            {
                if (written.Contains(j)) continue;
                if (variableList[i].CanBeCombined(variableList[j]))
                {
                    combinable.Add(variableList[j]);
                    written.Add(j);
                }
            }
            written.Add(i);

            if (combinable.Count > 1 && !(variableList[i].Type?.Name.Equals("Label", StringComparison.OrdinalIgnoreCase) ?? false))
            {
                var names = string.Join(", ", combinable.Select(v => v.Name));
                var typeStr = conventions.GetTypeString(combinable[0].Type!, null!);
                writer.WriteLine($"{names}: {typeStr};");
            }
            else
            {
                foreach (var v in combinable)
                    v.Write(writer, conventions);
            }
        }

        if (!string.IsNullOrEmpty(pragmas))
            writer.WriteLine($"#pragma warning restore {pragmas}", false);

        writer.DecreaseIndent();
    }
}

public static class ALTypeDefinitionExtensions
{
    public static string GetFullALName(this CodeElement typeDefinition)
    {
        return typeDefinition switch
        {
            CodeEnum e => $"Enum \"{e.Name.ToFirstCharacterUpperCase()}\"",
            CodeClass c => $"Codeunit {c.GetImmediateParentOfType<CodeNamespace>().Name}.\"{c.Name.ToFirstCharacterUpperCase()}\"",
            _ => typeDefinition.Name.ToFirstCharacterUpperCase(),
        };
    }
}
