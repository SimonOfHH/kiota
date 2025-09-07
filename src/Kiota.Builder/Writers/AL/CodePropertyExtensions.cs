using System;
using System.Collections.Generic;
using System.Linq;
using Kiota.Builder.CodeDOM;
using Kiota.Builder.Refiners;
using static Kiota.Builder.CodeDOM.CodeTypeBase;

namespace Kiota.Builder.Writers.AL;

internal static class CodePropertyExtensions
{
    private static ALConventionService ConventionService { get; } = new();
    private static ALReservedNamesProvider ReservedNamesProvider { get; } = new();
    public static CodeEnumOption ToCodeEnumOption(this CodeProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        var option = new CodeEnumOption
        {
            Name = ReservedNamesProvider.GetSafeName(property.Name),
            SerializationName = property.DefaultValue
        };
        option.AddCustomProperty("object-property", "true");
        return option;
    }
    public static CodeParameter ToCodeParameter(this CodeProperty property, string name = "")
    {
        ArgumentNullException.ThrowIfNull(property);
        var parameter = new CodeParameter
        {
            Name = ReservedNamesProvider.GetSafeName(property.Name),
            Type = property.Type,
            DefaultValue = property.DefaultValue
        };
        if (!string.IsNullOrEmpty(name))
            parameter.Name = name;
        return parameter;
    }
    public static CodeMethod ToGetterCodeMethod(this CodeProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        var method = new CodeMethod
        {
            Name = $"Get-{ReservedNamesProvider.GetSafeName(property.Name)}", // workaround, because we can't add getter and setter with same name
            SimpleName = ReservedNamesProvider.GetSafeName(property.Name),
            Access = property.Access,
            ReturnType = property.Type,
            Kind = CodeMethodKind.Getter
        };
        if (property.Kind == CodePropertyKind.RequestBuilder)
        {
            method.Name = method.SimpleName;
            method.Kind = CodeMethodKind.RequestBuilderBackwardCompatibility;
            method.SetSource("from request-builder");
            method.AddCustomProperty("sorting-value", "99");
        }
        else
            method.SetSourceFromProperty(property);
        method.AddCustomProperty("method-type", "Getter");
        if (ConventionService.IsEnumType(property.Type) && (property.Type.CollectionKind == CodeTypeCollectionKind.None))
        {
            method.AddParameter(ALVariableProvider.GetLocalVariableP("Ordinal", "Integer", "2"));
            method.AddParameter(ALVariableProvider.GetLocalVariableP("Ordinals", "List of [Integer]", "3"));
            method.AddParameter(ALVariableProvider.GetLocalVariableP("enumValue", property.Type, "1"));
        }
        switch (property.Type.CollectionKind)
        {
            case CodeTypeCollectionKind.None:
                if (ConventionService.IsCodeunitType(property.Type))
                    if (property.Kind == CodePropertyKind.Custom)
                        method.AddParameter(ALVariableProvider.GetLocalVariableP("TargetCodeunit", property.Type, "1"));
                    else
                        method.AddCustomProperty("return-variable-name", "Rqst");
                break;
            case CodeTypeCollectionKind.Array:
                throw new InvalidOperationException("Array properties are not (yet?) supported");
            case CodeTypeCollectionKind.Complex:
                if (property.Kind == CodePropertyKind.Custom)
                    method.AddParameter(DefaultGetterComplexCollectionParameters(property.Type));
                break;
            default:
                throw new InvalidOperationException("Unknown collection kind");
        }
        return method;
    }
    public static CodeMethod ToSetterCodeMethod(this CodeProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        var method = new CodeMethod
        {
            Name = $"Set-{ReservedNamesProvider.GetSafeName(property.Name)}", // workaround, because we can't add getter and setter with same name
            SimpleName = ReservedNamesProvider.GetSafeName(property.Name),
            Access = property.Access,
            ReturnType = new CodeType { Name = "void" },
            Kind = CodeMethodKind.Setter
        };
        if (property.Kind == CodePropertyKind.RequestBuilder)
        {
            method.Kind = CodeMethodKind.RequestBuilderBackwardCompatibility;
            method.SetSource("from request-builder");
            method.AddCustomProperty("sorting-value", "99");
        }
        else
            method.SetSourceFromProperty(property);
        method.AddCustomProperty("method-type", "Setter");
        method.AddParameter(property.ToCodeParameter("p"));
        switch (property.Type.CollectionKind)
        {
            case CodeTypeCollectionKind.None:
                break;
            case CodeTypeCollectionKind.Array:
                throw new InvalidOperationException("Array properties are not (yet?) supported");
            case CodeTypeCollectionKind.Complex:
                if (property.Kind == CodePropertyKind.Custom)
                    method.AddParameter(DefaultSetterComplexCollectionParameters(property.Type));
                break;
            default:
                throw new InvalidOperationException("Unknown collection kind");
        }
        return method;
    }
    public static ALVariable ToVariable(this CodeProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        return new ALVariable(property.Name, property.Type, property.DefaultValue, property.GetCustomProperty("value"), property.GetPragmas(), property.GetCustomProperty("locked-label") == "true");
    }
    public static IEnumerable<ALObjectProperty> ToObjectProperties(this IEnumerable<CodeProperty> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        return properties.Select(p1 => new ALObjectProperty(p1.Name, p1.DefaultValue));
    }
    public static IEnumerable<ALVariable> ToVariables(this IEnumerable<CodeProperty> properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        return properties.Select(p1 => p1.ToVariable());
    }
    private static CodeParameter[] DefaultGetterComplexCollectionParameters(CodeTypeBase propertyType)
    {
        var parameters = new List<CodeParameter>
        {
            ALVariableProvider.GetLocalVariableP("TargetCodeunit", propertyType.CloneWithoutCollection(), "1"),
            ALVariableProvider.GetLocalVariableP("JArray", "JsonArray", "2"),
            ALVariableProvider.GetLocalVariableP("JToken", "JsonToken", "3")
        };
        if (!ConventionService.IsCodeunitType(propertyType))
            parameters.RemoveAt(parameters.IndexOf(parameters.First(x => x.Name == "TargetCodeunit")));
        if (!ConventionService.IsCodeunitType(propertyType) && !ConventionService.IsTextType(propertyType))
            parameters.Add(ALVariableProvider.GetLocalVariableP("evluationVariable", propertyType.CloneWithoutCollection(), "1"));

        return [.. parameters];
    }
    private static CodeParameter[] DefaultSetterComplexCollectionParameters(CodeTypeBase propertyType)
    {
        var parameters = new List<CodeParameter>
        {
            ALVariableProvider.GetLocalVariableP("v", propertyType.CloneWithoutCollection(), "1"),
            ALVariableProvider.GetLocalVariableP("JArray", "JsonArray", "2")
        };
        return [.. parameters];
    }
}
