using System;
using System.Linq;
using Kiota.Builder.CodeDOM;

namespace Kiota.Builder.Writers.AL;

public static class CodeTypeBaseExtensions
{
    public static CodeType? GetTypeFromBase(this CodeTypeBase codeTypeBase)
    {
        return codeTypeBase as CodeType;
    }

    public static string GetNamespaceName(this CodeTypeBase codeTypeBase)
    {
        if (codeTypeBase is CodeType codeType && codeType.TypeDefinition is CodeElement element)
        {
            try
            {
                var ns = element.GetImmediateParentOfType<CodeNamespace>();
                return ns.Name;
            }
            catch (InvalidOperationException)
            {
                return string.Empty;
            }
        }
        return string.Empty;
    }

    public static CodeTypeBase CloneWithoutCollection(this CodeTypeBase codeTypeBase)
    {
        ArgumentNullException.ThrowIfNull(codeTypeBase);
        var clone = (CodeTypeBase)codeTypeBase.Clone();
        clone.CollectionKind = CodeTypeBase.CodeTypeCollectionKind.None;
        return clone;
    }
    public static bool IsCodeunitType(this CodeTypeBase codeTypeBase)
    {
        if (codeTypeBase is CodeType codeType && codeType.TypeDefinition is CodeClass codeClass)
        {
            return true; // CodeClass basically translates to a Codeunit in AL
        }
        return false;
    }
    public static bool IsModelCodeunitType(this CodeTypeBase codeTypeBase)
    {
        if (codeTypeBase is CodeType codeType && codeType.TypeDefinition is CodeClass codeClass)
        {
            if (codeClass.Kind == CodeClassKind.Model)
                return true;
        }
        return false;
    }
}
