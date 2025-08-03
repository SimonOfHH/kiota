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
        if ((parentNamespace is not null) && (typeDefinition is CodeClass))
        {
            fullNameBuilder.Append(parentNamespace.Name);
            fullNameBuilder.Append('.');
        }
        fullNameBuilder.Append('"');
        fullNameBuilder.Append(typeDefinition.Name.ToFirstCharacterUpperCase());
        fullNameBuilder.Append('"');
        return fullNameBuilder;
    }
}