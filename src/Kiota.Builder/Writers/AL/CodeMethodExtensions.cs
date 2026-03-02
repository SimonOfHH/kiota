using System;
using System.Collections.Generic;
using System.Linq;
using Kiota.Builder.CodeDOM;
using Kiota.Builder.Refiners;

namespace Kiota.Builder.Writers.AL;

public static class CodeMethodExtensions
{
    public static string GetSingularName(string name, IEnumerable<CodeParameter> existingParams)
    {
        ArgumentNullException.ThrowIfNull(name);
        var singular = name;
        if (singular.EndsWith('s') && singular.Length > 1)
            singular = singular[..^1];
        else if (singular.Length > 1)
            singular = singular[..^1];

        singular = ALReservedNamesProvider.GetSafeName(singular);

        if (string.Equals(singular, name, StringComparison.OrdinalIgnoreCase))
            singular += "_var";

        // Deduplicate against existing params
        var paramNames = existingParams.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var baseSingular = singular;
        var counter = 2;
        while (paramNames.Contains(singular))
        {
            singular = $"{baseSingular}{counter}";
            counter++;
        }

        return singular;
    }

    public static bool HasVariables(this CodeMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return method.Parameters.Any(p =>
            p.CustomData.TryGetValue("local-variable", out var val) &&
            val.Equals("true", StringComparison.OrdinalIgnoreCase));
    }

    public static IEnumerable<CodeParameter> Variables(this CodeMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        var variables = method.Parameters
            .Where(p => p.CustomData.TryGetValue("local-variable", out var val) &&
                        val.Equals("true", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Codeunit types first
        var codeunitVars = variables.Where(v => v.Type.IsCodeunitType());
        var otherVars = variables.Where(v => !v.Type.IsCodeunitType());

        return codeunitVars.Concat(otherVars);
    }

    public static IEnumerable<CodeParameter> OrderedParameters(this CodeMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return method.Parameters
            .Where(p => !p.CustomData.ContainsKey("local-variable") ||
                        !p.CustomData["local-variable"].Equals("true", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.DefaultValue ?? string.Empty, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsPropertyMethod(this CodeMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return method.CustomData.TryGetValue("source", out var val) &&
               val.Contains("from property", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsGetterMethod(this CodeMethod method)
    {
        return method.IsPropertyMethod() &&
               method.IsOfKind(CodeMethodKind.Getter) &&
               method.CustomData.TryGetValue("method-type", out var val) &&
               val.Equals("Getter", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSetterMethod(this CodeMethod method)
    {
        return method.IsPropertyMethod() &&
               method.IsOfKind(CodeMethodKind.Setter) &&
               method.CustomData.TryGetValue("method-type", out var val) &&
               val.Equals("Setter", StringComparison.OrdinalIgnoreCase);
    }

    public static int GetSortingValue(this CodeMethod method, int defaultValue = 0)
    {
        ArgumentNullException.ThrowIfNull(method);
        return method.CustomData.TryGetValue("sorting-value", out var val) &&
               int.TryParse(val, out var sortVal) ? sortVal : defaultValue;
    }

    public static ALVariable ToVariable(this CodeMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        method.CustomData.TryGetValue("pragmas", out var pragmas);
        return new ALVariable(method.Name, method.ReturnType, string.Empty, string.Empty, pragmas ?? string.Empty);
    }
}
