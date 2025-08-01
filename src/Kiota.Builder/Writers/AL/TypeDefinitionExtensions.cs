using System;
using System.Collections.Generic;
using System.Text;
using Kiota.Builder.CodeDOM;
using Kiota.Builder.Extensions;

namespace Kiota.Builder.Writers.AL;

internal static class TypeDefinitionExtensions
{
    public static string GetFullName(this ITypeDefinition typeDefinition)
    {
        ArgumentNullException.ThrowIfNull(typeDefinition);

        var fullNameBuilder = new StringBuilder();
        return AppendTypeName(typeDefinition, fullNameBuilder).ToString();
    }
    private static StringBuilder AppendTypeName(ITypeDefinition typeDefinition, StringBuilder fullNameBuilder)
    {
        if (string.IsNullOrEmpty(typeDefinition.Name))
            throw new ArgumentException("Cannot append a full name for a type without a name.", nameof(typeDefinition));

        switch (typeDefinition)
        {
            case CodeEnum:
                fullNameBuilder.Append("Enum");
                break;
            case CodeClass:
                fullNameBuilder.Append("Codeunit");
                break;
            default:
                throw new InvalidOperationException($"Type {typeDefinition.Name} is neither a CodeEnum nor a CodeClass.");
        }
        fullNameBuilder.Append(' ');
        var parentNamespace = typeDefinition.GetImmediateParentOfType<CodeNamespace>();
        if (parentNamespace is not null)
        {
            fullNameBuilder.Append(parentNamespace.Name);
            fullNameBuilder.Append('.');
        }
        fullNameBuilder.Append('"');
        fullNameBuilder.Append(typeDefinition.GetShortName().ToFirstCharacterUpperCase());
        fullNameBuilder.Append('"');
        return fullNameBuilder;
    }
    public static string GetShortName(this ICodeElement codeElement)
    {
        // Hier gehts vom CodeClassDeclarationWriter rein
        ArgumentNullException.ThrowIfNull(codeElement);

        return GetShortName(codeElement.Name);
    }
    public static string GetShortName(this string? input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return input.Length <= 30 ? input : ShortenName(input);
    }
    private static string ShortenName(string newName)
    {
        ArgumentNullException.ThrowIfNull(newName);
        // TODO-SF: find more generic approach to this
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
            { "Integration", "Intg" },
            { "Keyword", "Key"},
            { "Language", "Lang" },
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
            { "Wishlist", "WList"}
        };
        foreach (var kvp in dict)
        {
            if (newName.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                newName = newName.Replace(kvp.Key, kvp.Value, StringComparison.OrdinalIgnoreCase);
            }
        }
        if (newName.Length > 30)
            newName = newName.Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase);
        if (newName.Length > 30)
            newName = newName.Substring(0, 30);
        return newName;
    }
}