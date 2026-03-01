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
        ["Transaction"] = "Txn",
        ["Avatar"] = "Ava",
        ["Action"] = "Act",
        ["Alignment"] = "Algnmt",
        ["Button"] = "Btn",
        ["Builder"] = "Bldr",
        ["Blocking"] = "Block",
        ["Category"] = "Cat",
        ["Categories"] = "Cats",
        ["Capture"] = "Cpt",
        ["Certificates"] = "Certs",
        ["Certificate"] = "Cert",
        ["Connections"] = "Conns",
        ["Connection"] = "Conn",
        ["Children"] = "Chld",
        ["Channel"] = "Chnl",
        ["Contact"] = "Cont",
        ["Configuration"] = "Cfg",
        ["Config"] = "Cfg",
        ["Collection"] = "Coll",
        ["Classification"] = "Class",
        ["Customer"] = "Cust",
        ["Custom"] = "Cust",
        ["Currency"] = "Curr",
        ["Dictionary"] = "Dict",
        ["Discount"] = "Disc",
        ["Data"] = "Dt",
        ["Describe"] = "Desc",
        ["Delivery"] = "Dlv",
        ["Definition"] = "Def",
        ["Description"] = "Desc",
        ["Details"] = "Dtl",
        ["Dependent"] = "Dep",
        ["Dependency"] = "Dep",
        ["Document"] = "Doc",
        ["Download"] = "Dwld",
        ["Entity"] = "Ent",
        ["Error"] = "Err",
        ["Exception"] = "Ex",
        ["Event"] = "Evt",
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
        ["Number"] = "Num",
        ["Notification"] = "Notf",
        ["Override"] = "Ovrd",
        ["Object"] = "Obj",
        ["Order"] = "Odr",
        ["Original"] = "Orig",
        ["Parameters"] = "Params",
        ["Payment"] = "Pmt",
        ["Product"] = "Prod",
        ["Property"] = "Prop",
        ["Promotion"] = "Prmt",
        ["Position"] = "Pos",
        ["Query"] = "Qry",
        ["Referenced"] = "Ref",
        ["Reference"] = "Ref",
        ["Refund"] = "Rfd",
        ["Relationship"] = "Rel",
        ["Relation"] = "Rel",
        ["Regulation"] = "Reg",
        ["Recovery"] = "Rcvry",
        ["Request"] = "Req",
        ["Response"] = "Rsp",
        ["Result"] = "Rslt",
        ["Sales"] = "Sls",
        ["Section"] = "Sect",
        ["Service"] = "Svc",
        ["Sequence"] = "Seq",
        ["Stream"] = "Strm",
        ["Shipping"] = "Shp",
        ["User"] = "Usr",
        ["Wishlist"] = "WList",
    };
    #endregion

    #region Global Name Tracking
    private readonly HashSet<string> _allNames = new(StringComparer.OrdinalIgnoreCase);
    #endregion

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
            "binary" or "base64" or "base64url" => "HttpContent",
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
            string typeName;
            if (codeType.TypeDefinition is CodeClass or CodeEnum)
                typeName = codeType.TypeDefinition.GetFullALName();
            else
                typeName = TranslateType(codeType);

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
        if (element is not null && !element.CustomData.ContainsKey("original-name"))
            element.CustomData["original-name"] = name;

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
            return name;

        // Store original name
        if (element is not null && !element.CustomData.ContainsKey("original-name"))
            element.CustomData["original-name"] = name;

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
                AddPragma(element, "AA0215");
                return withNs;
            }
        }

        // Try abbreviation
        var abbreviated = ApplyAbbreviations(name, maxLength);
        if (abbreviated.Length > maxLength)
            abbreviated = abbreviated[..maxLength];
        if (_allNames.Add(abbreviated))
        {
            AddPragma(element, "AA0215");
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
                AddPragma(element, "AA0215");
                return candidate;
            }
        }

        AddPragma(element, "AA0215");
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
        if (element.CustomData.TryGetValue("pragmas", out var existing) && !string.IsNullOrEmpty(existing))
        {
            if (!existing.Contains(pragma, StringComparison.OrdinalIgnoreCase))
                element.CustomData["pragmas"] = $"{existing},{pragma}";
        }
        else
        {
            element.CustomData["pragmas"] = pragma;
        }
    }

    public void WritePragmaDisable(LanguageWriter writer, string? pragmas)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (!string.IsNullOrEmpty(pragmas))
            writer.WriteLine($"#pragma warning disable {pragmas}");
    }

    public void WritePragmaRestore(LanguageWriter writer, string? pragmas)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (!string.IsNullOrEmpty(pragmas))
            writer.WriteLine($"#pragma warning restore {pragmas}");
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
            writer.WriteLine($"#pragma warning disable {pragmas}");

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
            writer.WriteLine($"#pragma warning restore {pragmas}");

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
