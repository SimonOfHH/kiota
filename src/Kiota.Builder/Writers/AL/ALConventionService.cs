using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using Kiota.Builder.CodeDOM;
using Kiota.Builder.Extensions;

using static Kiota.Builder.CodeDOM.CodeTypeBase;

namespace Kiota.Builder.Writers.AL;

public class ALConventionService : CommonLanguageConventionService // This is currently based on the CSharp-file, needs to be modified for AL
{
    public string ModelCodeunitJsonBodyVariableName { get; } = "JsonBody";
    public override string StreamTypeName => "stream";
    public override string VoidTypeName => "void";
    public override string DocCommentPrefix => "/// ";
    public override string ParseNodeInterfaceName => "IParseNode";
    public override string TempDictionaryVarName => "urlTplParams";
    private const string ReferenceTypePrefix = "<see cref=\"";
    private const string ReferenceTypeSuffix = "\"/>";
    public override string GetAccessModifier(AccessModifier access)
    {
        return access switch
        {
            AccessModifier.Internal => "internal ",
            AccessModifier.Public => "", // public is the default
            AccessModifier.Protected => throw new InvalidOperationException("AL does not support protected access modifier"),
            _ => "local ",
        };
    }
#pragma warning disable S1006 // Method overrides should not change parameter defaults
    public override bool WriteShortDescription(IDocumentedElement element, LanguageWriter writer, string prefix = "<summary>", string suffix = "</summary>")
#pragma warning restore S1006 // Method overrides should not change parameter defaults
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(element);
        if (element is not CodeElement codeElement) return false;
        if (!element.Documentation.DescriptionAvailable) return false;
        var description = element.Documentation.GetDescription(type => GetTypeStringForDocumentation(type), normalizationFunc: static x => x.CleanupXMLString());
        writer.WriteLine($"{DocCommentPrefix}{prefix}{description}{suffix}");
        return true;
    }
    public bool WriteLongDescription(IDocumentedElement element, LanguageWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(element);
        if (element.Documentation is not { } documentation) return false;
        if (element is not CodeElement codeElement) return false;
        if (documentation.DescriptionAvailable || documentation.ExternalDocumentationAvailable)
        {
            writer.WriteLine($"{DocCommentPrefix}<summary>");
            if (documentation.DescriptionAvailable)
            {
                var description = element.Documentation.GetDescription(type => GetTypeStringForDocumentation(type), normalizationFunc: static x => x.CleanupXMLString());
                writer.WriteLine($"{DocCommentPrefix}{description}");
            }
            if (documentation.ExternalDocumentationAvailable)
                writer.WriteLine($"{DocCommentPrefix}{documentation.DocumentationLabel} <see href=\"{documentation.DocumentationLink}\" />");
            writer.WriteLine($"{DocCommentPrefix}</summary>");
            return true;
        }
        return false;
    }
    public string GetTypeStringForDocumentation(CodeTypeBase code)
    {
        var typeString = GetTypeString(code, true); // don't include nullable markers
        if (typeString.EndsWith('>'))
            return typeString.CleanupXMLString(); // don't generate cref links for generic types as concrete types generate invalid links

        return $"{ReferenceTypePrefix}{typeString.CleanupXMLString()}{ReferenceTypeSuffix}";
    }
    public string GetTypeString(CodeTypeBase code, bool includeCollectionInformation = true, LanguageWriter? writer = null)
    {
        return GetTypeString(code, null, includeCollectionInformation, writer);
    }
    public override string GetTypeString(CodeTypeBase code, CodeElement? targetElement, bool includeCollectionInformation = true, LanguageWriter? writer = null)
    {
        return GetTypeString(code, includeCollectionInformation);
    }
    public bool IsPrimitiveType(CodeTypeBase code)
    {
        if (IsEnumType(code))
            return false;
        if (IsCodeunitType(code))
            return false;
        return true;
    }
    public bool IsEnumType(CodeTypeBase code)
    {
        if (code is CodeType currentType)
        {
            if (currentType.TypeDefinition is CodeEnum)
                return true;
        }
        return false;
    }
    public bool IsCodeunitType(CodeTypeBase code)
    {
        if (code is CodeType currentType)
        {
            if (currentType.TypeDefinition is CodeClass codeClass)
                return true;
        }
        return false;
    }
    public bool IsTextType(CodeTypeBase code)
    {
        if (code is CodeType currentType)
        {
            var typeName = TranslateType(currentType);
            return typeName.Equals("Text", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
    public string GetTypeString(CodeTypeBase code, bool includeCollectionInformation)
    {
        if (code is CodeComposedTypeBase)
            throw new InvalidOperationException($"AL does not support union types, the union type {code.Name} should have been filtered out by the refiner");
        if (code is CodeType currentType)
        {
            var typeName = TranslateType(currentType);
            var collectionPrefix = currentType.CollectionKind != CodeTypeCollectionKind.None && includeCollectionInformation ? "List of [" : string.Empty;
            var collectionSuffix = currentType.CollectionKind switch
            {
                CodeTypeCollectionKind.Array when includeCollectionInformation => "]", // Arrays will also be handled as complex types
                CodeTypeCollectionKind.Complex when includeCollectionInformation => "]",
                _ => string.Empty,
            };
            return $"{collectionPrefix}{typeName}{collectionSuffix}";
        }

        throw new InvalidOperationException($"type of type {code?.GetType()} is unknown");
    }

    public override string TranslateType(CodeType type)
    {
        ArgumentNullException.ThrowIfNull(type);

        if (type.TypeDefinition is ITypeDefinition typeDefinition)
            return typeDefinition.GetFullName();

        return type.Name.ToLower(CultureInfo.CurrentCulture) switch
        {
            "integer" => "Integer",
            "boolean" => "Boolean",
            "string" => "Text",
            "untypednode" => "Text", // TODO-SF: not sure what to do with UntypedNode, let's assume it's a string for now
            "int64" => "BigInteger",
            "sbyte" or "byte" => "Byte",
            "float" or "double" or "decimal" => "Decimal",
            "binary" or "base64" or "base64url" => "HttpContent", // TODO-SF: this was byte[] (copied from CSharpConventionService) but AL does not support byte[] in the same way; 
                                                                  // the only situation I found for this yet is the body of a request and HttpContent is the easiest way right now to handle
                                                                  // might be adjusted later
            "date" or "dateonly" => "Date",
            "time" or "timeonly" => "Time",
            "datetime" or "datetimeoffset" => "DateTime",
            "void" => String.Empty,
            _ => type.Name.ToFirstCharacterUpperCase()
        };
    }
    public override string GetParameterSignature(CodeParameter parameter, CodeElement targetElement, LanguageWriter? writer = null)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        var parameterType = GetTypeString(parameter.Type, targetElement);
        return $"{parameter.Name} : {parameterType}";
    }
    private string GetDeprecationInformation(IDeprecableElement element)
    {
        if (element.Deprecation is null || !element.Deprecation.IsDeprecated) return string.Empty;

        var versionComment = string.IsNullOrEmpty(element.Deprecation.Version) ? string.Empty : $" as of {element.Deprecation.Version}";
        var dateComment = element.Deprecation.Date is null ? string.Empty : $" on {element.Deprecation.Date.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        var removalComment = element.Deprecation.RemovalDate is null ? string.Empty : $" and will be removed {element.Deprecation.RemovalDate.Value.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        return $"[Obsolete(\"{element.Deprecation.GetDescription(type => GetTypeString(type, (element as CodeElement)!).Split('.', StringSplitOptions.TrimEntries)[^1])}{versionComment}{dateComment}{removalComment}\")]";
    }
    internal void WriteDeprecationAttribute(IDeprecableElement element, LanguageWriter writer)
    {
        var deprecationMessage = GetDeprecationInformation(element);
        if (!string.IsNullOrEmpty(deprecationMessage))
            writer.WriteLine(deprecationMessage);
    }
    protected static CodeNamespace GetRootNamespaceFromClass(CodeElement codeClass)
    {
        ArgumentNullException.ThrowIfNull(codeClass);
        var currentNamespace = codeClass.GetImmediateParentOfType<CodeNamespace>();
        if (currentNamespace is null)
            throw new InvalidOperationException($"The provided code class {codeClass.Name} does not have a parent namespace.");
        var root = currentNamespace.GetRootNamespace();
        if (root is null)
            throw new InvalidOperationException($"The provided code class {codeClass.Name} does not have a root namespace.");
        if (String.IsNullOrEmpty(root.Name))
        {
            var firstActualNamespace = root.GetChildElements(true).FirstOrDefault();
            if (firstActualNamespace is not null && firstActualNamespace is CodeNamespace firstNamespace)
            {
                return firstNamespace;
            }
        }
        return root;
    }
    internal static int CountClassNameOccurences(CodeClass currentElement, string className)
    {
        var root = GetRootNamespaceFromClass(currentElement);
        if (root is null)
            throw new InvalidOperationException($"The provided code class {currentElement.Name} does not have a root namespace.");
        var children = root.GetChildElements(true);
        var count = CountClassNameInNamespace(root, className);
        return count;
    }
    internal static int CountClassNameInNamespace(CodeNamespace currentNamespace, string className)
    {
        var count = 0;
        ArgumentNullException.ThrowIfNull(currentNamespace);
        foreach (var nspaces in currentNamespace.Namespaces)
        {
            count += CountClassNameInNamespace(nspaces, className);
        }
        if (currentNamespace.Classes is null)
            return count;
        count += currentNamespace.Classes.Count(x => x.Name.Equals(className, StringComparison.OrdinalIgnoreCase));
        return count;
    }
    internal static int CountEnumNameOccurences(CodeEnum currentElement, string enumName)
    {
        var root = GetRootNamespaceFromClass(currentElement);
        if (root is null)
            throw new InvalidOperationException($"The provided code enum {currentElement.Name} does not have a root namespace.");
        var children = root.GetChildElements(true);
        var count = CountEnumNameInNamespace(root, enumName);
        return count;
    }
    internal static int CountEnumNameInNamespace(CodeNamespace currentNamespace, string enumName)
    {
        var count = 0;
        ArgumentNullException.ThrowIfNull(currentNamespace);
        foreach (var nspaces in currentNamespace.Namespaces)
        {
            count += CountEnumNameInNamespace(nspaces, enumName);
        }
        if (currentNamespace.Enums is null)
            return count;
        count += currentNamespace.Enums.Count(x => x.Name.Equals(enumName, StringComparison.OrdinalIgnoreCase));
        return count;
    }
    public static bool CanAbbreviate(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var abbreviationDictionary = AbbreviationDictionary();
        return abbreviationDictionary.Keys.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase));
    }
    public static string AbbreviateName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var abbreviationDictionary = AbbreviationDictionary();
        foreach (var kvp in abbreviationDictionary)
        {
            if (name.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                name = name.Replace(kvp.Key, kvp.Value, StringComparison.OrdinalIgnoreCase);
                if (name.Length <= 30) break; // stop if the name is already short enough, to only make one change at a time
            }
        }
        
        // If still too long after abbreviation, truncate intelligently
        if (name.Length > 30)
        {
            // Try to preserve the end part (often contains "Params", "Bldr", etc.)
            // and truncate from the beginning, keeping important parts
            if (name.EndsWith("Params", StringComparison.OrdinalIgnoreCase))
            {
                var prefixLength = 30 - 6; // 6 for "Params"
                name = string.Concat(name.AsSpan(0, Math.Min(prefixLength, name.Length - 6)), "Params");
            }
            else if (name.EndsWith("Bldr", StringComparison.OrdinalIgnoreCase))
            {
                var prefixLength = 30 - 4; // 4 for "Bldr" 
                name = string.Concat(name.AsSpan(0, Math.Min(prefixLength, name.Length - 4)), "Bldr");
            }
            else
            {
                // Generic truncation
                name = name[..30];
            }
        }
        
        return name;
    }
    public static ReadOnlyDictionary<string, string> AbbreviationDictionary()
    {
        var dict = new Dictionary<string, string>
        {
            { "Transaction", "Txn"},
            { "Avatar", "Ava"},
            { "Action", "Act"},
            { "Alignment", "Algnmt" },
            { "Button", "Btn"},
            { "Builder", "Bldr" },
            { "Blocking", "Block" },
            { "Category", "Cat"},
            { "Categories", "Cats"},
            { "Capture", "Cpt"},
            { "Certificates", "Certs"},
            { "Certificate", "Cert"},
            { "Connections", "Conns"},
            { "Connection", "Conn"},
            { "Children", "Chld" },
            { "Channel", "Chnl" },
            { "Contact", "Cont" },
            { "Configuration", "Cfg" },
            { "Config", "Cfg" },
            { "Collection", "Coll" },
            { "Classification", "Class" },
            { "Customer", "Cust" },
            { "Custom", "Cust" },
            { "Currency", "Curr"},
            { "Dictionary", "Dict" },
            { "Discount", "Disc" },
            { "Data", "Dt" },
            { "Describe", "Desc" },
            { "Delivery", "Dlv" },
            { "Definition", "Def" },
            { "Description", "Desc" },
            { "Details", "Dtl" },
            { "Dependent", "Dep" },
            { "Dependency", "Dep" },
            { "Document", "Doc" },
            { "Download", "Dwld" },
            { "Entity", "Ent" },
            { "Error", "Err" },
            { "Exception", "Ex" },
            { "Event", "Evt" },
            { "Extended", "Ext" },
            { "Extension", "Ext" },
            { "Field", "Fld" },
            { "Folder", "Fld" },
            { "Global", "Glb" },
            { "History", "Hist" },
            { "Integration", "Intg" },
            { "Keyword", "Key"},
            { "Language", "Lang" },
            { "Machine", "Mch"},
            { "Media", "Med"},
            { "Message", "Msg" },
            { "Method", "Meth"},
            { "Microsoft", "Ms" },
            { "Navigation", "Nav"},
            { "Number", "Num" },
            { "Notification", "Notf" },
            { "Override", "Ovrd" },
            { "Object", "Obj" },
            { "Order", "Odr" },
            { "Original", "Orig" },
            { "Parameters", "Params" },
            { "Payment", "Pmt" },
            { "Product", "Prod" },
            { "Property", "Prop" },
            { "Promotion", "Prmt" },
            { "Position", "Pos" },
            { "Query", "Qry" },
            { "Referenced", "Ref" },
            { "Reference", "Ref" },
            { "Refund", "Rfd" },
            { "Relationship", "Rel" },
            { "Relation", "Rel" },
            { "Regulation", "Reg" },
            { "Recovery", "Rcvry"},
            { "Request", "Req" },
            { "Response", "Rsp" },
            { "Result", "Rslt" },
            { "Sales", "Sls"},
            { "Section", "Sect" },
            { "Service", "Svc" },
            { "Sequence", "Seq" },
            { "Stream", "Strm" },
            { "Shipping", "Shp"},
            { "User", "Usr" },
            { "Wishlist", "WList"},
            { "_", ""}
        };
        return new ReadOnlyDictionary<string, string>(dict);
    }
}
