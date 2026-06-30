using System;
using Kiota.Builder.CodeDOM;
using Kiota.Builder.Extensions;

namespace Kiota.Builder.Writers.AL;

public static class TypeDefinitionExtensions
{
    public static string GetFullName(this CodeElement typeDefinition)
    {
        return typeDefinition switch
        {
            CodeEnum e => $"Enum \"{e.Name.ToFirstCharacterUpperCase()}\"",
            CodeClass c => $"Codeunit {c.GetImmediateParentOfType<CodeNamespace>().Name}.\"{c.Name.ToFirstCharacterUpperCase()}\"",
            _ => typeDefinition.Name.ToFirstCharacterUpperCase(),
        };
    }
    public static bool IsLocalVariable(this CodeParameter parameter)
    {
        if (parameter is null) return false;
        return parameter.GetFlag(ALCustomDataKeys.LocalVariable);
    }
}
