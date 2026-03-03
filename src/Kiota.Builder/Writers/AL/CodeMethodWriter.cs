using System;
using System.Collections.Generic;
using System.Linq;
using Kiota.Builder.CodeDOM;
using Kiota.Builder.Extensions;

namespace Kiota.Builder.Writers.AL;

public class CodeMethodWriter : BaseElementWriter<CodeMethod, ALConventionService>
{
    public CodeMethodWriter(ALConventionService conventionService) : base(conventionService) { }

    public override void WriteCodeElement(CodeMethod codeElement, LanguageWriter writer)
    {
        ArgumentNullException.ThrowIfNull(codeElement);
        ArgumentNullException.ThrowIfNull(writer);
        if (codeElement.ParentIsSkipped())
            return;

        // Skip logic
        if (codeElement.CustomData.TryGetValue("skip", out var skip) &&
            skip.Equals("true", StringComparison.OrdinalIgnoreCase))
            return;

        if (codeElement.Kind == CodeMethodKind.RawUrlConstructor ||
            codeElement.Kind == CodeMethodKind.RequestGenerator)
            return;

        if (codeElement.Parent is CodeClass parentClass &&
            parentClass.CustomData.TryGetValue("parameter-codeunit", out var paramCu) &&
            paramCu.Equals("true", StringComparison.OrdinalIgnoreCase))
            return; // Parameter codeunit methods are written by class declaration writer

        WriteMethodPrototype(codeElement, writer);
        WriteMethodBody(codeElement, writer);
    }

    private void WriteMethodPrototype(CodeMethod method, LanguageWriter writer)
    {
        WriteMethodDocumentation(method, writer);

        // Access modifier
        var access = method.Access == AccessModifier.Private ? "local " :
                     method.Access == AccessModifier.Protected ? "internal " : string.Empty;

        var methodName = method.SimpleName is not null && !string.IsNullOrEmpty(method.SimpleName)
            ? method.SimpleName : method.Name;

        // Build parameter list
        var parameters = method.OrderedParameters().ToList();
        var paramStr = parameters.Count > 0
            ? string.Join("; ", parameters.Select(FormatParameter))
            : string.Empty;

        // Return type
        var returnType = GetReturnTypeString(method);

        var returnVarName = method.CustomData.TryGetValue("return-variable-name", out var returnVar) ? returnVar : null;
        var returnClause = !string.IsNullOrEmpty(returnType) && !returnType.Equals("void", StringComparison.OrdinalIgnoreCase)
            ? $" {returnVarName}: {returnType}" : string.Empty;

        // Pragma for method
        method.CustomData.TryGetValue("pragmas", out var pragmas);
        if (!string.IsNullOrEmpty(pragmas))
            writer.WriteLine($"#pragma warning disable {pragmas}");

        writer.WriteLine($"{access}procedure {methodName}({paramStr}){returnClause}");

        if (!string.IsNullOrEmpty(pragmas))
            writer.WriteLine($"#pragma warning restore {pragmas}");

        // Local variables
        if (method.HasVariables())
        {
            writer.WriteLine("var");
            writer.IncreaseIndent();

            var varPragmas = string.Empty;
            method.CustomData.TryGetValue("pragmas-variables", out varPragmas);

            if (!string.IsNullOrEmpty(varPragmas))
                writer.WriteLine($"#pragma warning disable {varPragmas}");

            foreach (var v in method.Variables())
            {
                var typeName = conventions.GetTypeString(v.Type, method);
                writer.WriteLine($"{v.Name}: {typeName};");
            }

            if (!string.IsNullOrEmpty(varPragmas))
                writer.WriteLine($"#pragma warning restore {varPragmas}");

            writer.DecreaseIndent();
        }

        writer.WriteLine("begin");
        writer.IncreaseIndent();
    }

    private string FormatParameter(CodeParameter param)
    {
        var typeName = conventions.GetTypeString(param.Type, param);

        // Check if parameter needs 'var' (by reference)
        var byRef = param.CustomData.TryGetValue("by-ref", out var refVal) &&
                    refVal.Equals("true", StringComparison.OrdinalIgnoreCase);

        return byRef ? $"var {param.Name}: {typeName}" : $"{param.Name}: {typeName}";
    }

    private string GetReturnTypeString(CodeMethod method)
    {
        if (method.ReturnType is null) return string.Empty;

        var typeName = conventions.GetTypeString(method.ReturnType, method, false);

        // Handle collections
        if (method.ReturnType.CollectionKind != CodeTypeBase.CodeTypeCollectionKind.None)
            return $"List of [{typeName}]";

        return typeName;
    }

    private void WriteMethodBody(CodeMethod method, LanguageWriter writer)
    {
        switch (method.Kind)
        {
            case CodeMethodKind.RequestExecutor:
                WriteRequestExecutorBody(method, writer);
                break;
            case CodeMethodKind.Getter:
                WriteGetterBody(method, writer);
                break;
            case CodeMethodKind.Setter:
                WriteSetterBody(method, writer);
                break;
            case CodeMethodKind.Deserializer:
                WriteSetBodyBody(method, writer);
                break;
            case CodeMethodKind.Serializer:
                WriteToJsonBody(method, writer);
                break;
            case CodeMethodKind.Constructor:
                WriteInitializeBody(method, writer);
                break;
            case CodeMethodKind.ClientConstructor:
                WriteConfigurationBody(method, writer);
                break;
            case CodeMethodKind.Factory:
                WriteDefaultConfigurationBody(method, writer);
                break;
            case CodeMethodKind.RawUrlBuilder:
                WriteRawUrlBuilderBody(method, writer);
                break;
            case CodeMethodKind.RequestBuilderBackwardCompatibility:
                WriteRequestBuilderGetterBody(method, writer);
                break;
            case CodeMethodKind.IndexerBackwardCompatibility:
                WriteRequestBuilderGetterBody(method, writer);
                break;
            case CodeMethodKind.Custom:
                WriteCustomMethodBody(method, writer);
                break;
            default:
                writer.WriteLine("// TODO: Implement method body");
                break;
        }

        writer.DecreaseIndent();
        writer.WriteLine("end;");
        writer.WriteLine();
    }

    private void WriteRequestExecutorBody(CodeMethod method, LanguageWriter writer)
    {
        var parentClass = method.Parent as CodeClass;

        writer.WriteLine("RequestHandler.SetClientConfig(ReqConfig);");

        // Query parameters
        if (method.CustomData.TryGetValue("use-parameter-codeunit", out var usePcu) &&
            usePcu.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            var paramsParam = method.Parameters.FirstOrDefault(p => p.Name.Equals("Parameters", StringComparison.OrdinalIgnoreCase));
            if (paramsParam is not null)
                writer.WriteLine("RequestHandler.AddQueryParameter(Parameters.GetQueryParameters());");
        }

        // Body parameter
        var bodyParam = method.Parameters.FirstOrDefault(p =>
            p.Kind == CodeParameterKind.RequestBody);
        if (bodyParam is not null)
        {
            if (bodyParam.Type.IsCollection && bodyParam.Type.IsModelCodeunitType())
            {
                writer.WriteLine($"foreach BodyElement in {bodyParam.Name} do");
                writer.IncreaseIndent();
                writer.WriteLine($"BodyObjects.Add(BodyElement);");
                writer.DecreaseIndent();
                writer.WriteLine($"RequestHandler.SetBody(BodyObjects);");
            }
            else
                writer.WriteLine($"RequestHandler.SetBody({bodyParam.Name});");
        }

        // HTTP method
        var httpMethod = method.HttpMethod?.ToString()?.ToUpperInvariant() ?? "GET";
        writer.WriteLine($"RequestHandler.SetMethod(enum::System.RestClient.\"Http Method\"::{httpMethod});");
        writer.WriteLine("RequestHandler.HandleRequest();");

        // Response handling
        var isCollection = method.ReturnType.CollectionKind != CodeTypeBase.CodeTypeCollectionKind.None;
        var isCodeunit = method.ReturnType is CodeType { TypeDefinition: CodeClass };
        var isVoid = method.ReturnType.Name.Equals("void", StringComparison.OrdinalIgnoreCase);

        if (!isVoid)
        {
            if (isCollection)
                writer.WriteLine("if ReqConfig.Client().Response().GetIsSuccessStatusCode() then begin");
            else
                writer.WriteLine("if ReqConfig.Client().Response().GetIsSuccessStatusCode() then");
            writer.IncreaseIndent();

            if (isCollection)
            {
                writer.WriteLine("ResponseAsArray := ReqConfig.Client().Response().GetContent().AsJson().AsArray();");
                writer.WriteLine("for i := 0 to ResponseAsArray.Count() - 1 do begin");
                writer.IncreaseIndent();
                writer.WriteLine("ResponseAsArray.Get(i, SubToken);");
                writer.WriteLine("Clear(TargetType);");
                writer.WriteLine("TargetType.SetBody(SubToken.AsObject());");
                writer.WriteLine("Target.Add(TargetType);");
                writer.DecreaseIndent();
                writer.WriteLine("end;");
            }
            else if (isCodeunit)
            {
                writer.WriteLine("Target.SetBody(ReqConfig.Client().Response().GetContent().AsJson().AsObject());");
            }
            else
            {
                // Primitive return
                var alType = conventions.GetTypeString(method.ReturnType, method);
                var asMethod = GetAsMethodForType(alType);
                if (alType == "HttpContent")
                    writer.WriteLine($"exit(ReqConfig.Client().Response().GetContent().GetHttpContent());");
                else
                    writer.WriteLine($"exit(ReqConfig.Client().Response().GetContent().AsJson().AsValue().{asMethod}());");
            }

            writer.DecreaseIndent();
            if (isCollection)
                writer.WriteLine("end;");
        }
    }

    private void WriteGetterBody(CodeMethod method, LanguageWriter writer)
    {
        var serializationName = GetSerializationName(method);
        var returnType = method.ReturnType;
        var isCollection = returnType.CollectionKind != CodeTypeBase.CodeTypeCollectionKind.None;
        var isEnum = returnType is CodeType { TypeDefinition: CodeEnum };
        var isCodeunit = returnType is CodeType { TypeDefinition: CodeClass };
        var isWrapperGetter = method.CustomData.TryGetValue("source", out var wrapperVal) &&
                             wrapperVal.Equals("value-wrapper-getter", StringComparison.OrdinalIgnoreCase);
        if (isWrapperGetter)
        {
            WriteValueWrapperGetterBody(method, writer);
        }
        else if (isCollection)
        {
            WriteCollectionGetterBody(method, writer, serializationName, isEnum, isCodeunit);
        }
        else if (isEnum)
        {
            WriteSingleEnumGetterBody(method, writer, serializationName, returnType);
        }
        else if (isCodeunit)
        {
            WriteSingleCodeunitGetterBody(method, writer, serializationName);
        }
        else
        {
            WriteSinglePrimitiveGetterBody(method, writer, serializationName, returnType);
        }
    }

    private void WriteSinglePrimitiveGetterBody(CodeMethod method, LanguageWriter writer, string serializationName, CodeTypeBase returnType)
    {
        var alType = conventions.GetTypeString(returnType, method);
        var asMethod = GetAsMethodForType(alType);
        writer.WriteLine($"if JsonBody.SelectToken('{serializationName}', SubToken) then");
        writer.IncreaseIndent();
        writer.WriteLine($"exit(SubToken.AsValue().{asMethod}());");
        writer.DecreaseIndent();
    }

    private void WriteSingleCodeunitGetterBody(CodeMethod method, LanguageWriter writer, string serializationName)
    {
        writer.WriteLine($"if JsonBody.SelectToken('{serializationName}', SubToken) then begin");
        writer.IncreaseIndent();
        writer.WriteLine("TargetCodeunit.SetBody(SubToken.AsObject(), DebugCall);");
        writer.WriteLine("exit(TargetCodeunit);");
        writer.DecreaseIndent();
        writer.WriteLine("end;");
    }

    private void WriteSingleEnumGetterBody(CodeMethod method, LanguageWriter writer, string serializationName, CodeTypeBase returnType)
    {
        var enumName = GetEnumName(returnType);
        writer.WriteLine($"if JsonBody.SelectToken('{serializationName}', SubToken) then begin");
        writer.IncreaseIndent();
        writer.WriteLine($"Ordinals := Enum::{enumName}.Ordinals();");
        writer.WriteLine("foreach Ordinal in Ordinals do begin");
        writer.IncreaseIndent();
        writer.WriteLine($"enumValue := Enum::{enumName}.FromInteger(Ordinal);");
        writer.WriteLine("if (Format(enumValue) = SubToken.AsValue().AsText()) then");
        writer.IncreaseIndent();
        writer.WriteLine("exit(enumValue);");
        writer.DecreaseIndent();
        writer.DecreaseIndent();
        writer.WriteLine("end;");
        var propertyName = method.SimpleName ?? method.Name;
        writer.WriteLine($"Error('Invalid value for {propertyName}: %1', SubToken.AsValue().AsText());");
        writer.DecreaseIndent();
        writer.WriteLine("end;");
    }

    private void WriteCollectionGetterBody(CodeMethod method, LanguageWriter writer, string serializationName, bool isEnum, bool isCodeunit)
    {
        writer.WriteLine($"if not JsonBody.SelectToken('{serializationName}', SubToken) then");
        writer.IncreaseIndent();
        writer.WriteLine("exit;");
        writer.DecreaseIndent();
        writer.WriteLine("JArray := SubToken.AsArray();");

        if (isCodeunit)
        {
            writer.WriteLine("foreach JToken in JArray do begin");
            writer.IncreaseIndent();
            writer.WriteLine("Clear(TargetCodeunit);");
            writer.WriteLine("TargetCodeunit.SetBody(JToken.AsObject(), DebugCall);");
            writer.WriteLine("ReturnList.Add(TargetCodeunit);");
            writer.DecreaseIndent();
            writer.WriteLine("end;");
        }
        else if (isEnum)
        {
            writer.WriteLine("foreach JToken in JArray do begin");
            writer.IncreaseIndent();
            writer.WriteLine("Evaluate(value, JToken.AsValue().AsText());");
            writer.WriteLine("ReturnList.Add(value);");
            writer.DecreaseIndent();
            writer.WriteLine("end;");
        }
        else
        {
            // Primitive collection
            var alType = conventions.GetTypeString(method.ReturnType, method);
            var asMethod = GetAsMethodForType(alType);
            writer.WriteLine("foreach JToken in JArray do");
            writer.IncreaseIndent();
            writer.WriteLine($"ReturnList.Add(JToken.AsValue().{asMethod}());");
            writer.DecreaseIndent();
        }
        writer.WriteLine("exit(ReturnList);");
    }

    private void WriteSetterBody(CodeMethod method, LanguageWriter writer)
    {
        var serializationName = GetSerializationName(method);
        var returnType = method.Parameters.FirstOrDefault(p => !p.CustomData.ContainsKey("local-variable"))?.Type;
        if (returnType is null) return;

        var isCollection = returnType.CollectionKind != CodeTypeBase.CodeTypeCollectionKind.None;
#pragma warning disable CA1508 // Avoid dead conditional code
        var isEnum = returnType is CodeType { TypeDefinition: CodeEnum };
        var isCodeunit = returnType is CodeType { TypeDefinition: CodeClass };
#pragma warning restore CA1508
        var isWrapperSetter = method.CustomData.TryGetValue("source", out var wrapperVal) &&
                             wrapperVal.Equals("value-wrapper-setter", StringComparison.OrdinalIgnoreCase);
        if (isWrapperSetter)
        {
            WriteValueWrapperSetterBody(method, writer);
        }
        else if (isCollection)
        {
            WriteCollectionSetterBody(method, writer, serializationName, isEnum, isCodeunit);
        }
        else if (isCodeunit)
        {
            writer.WriteLine($"if JsonBody.SelectToken('{serializationName}', SubToken) then");
            writer.IncreaseIndent();
            writer.WriteLine($"JsonBody.Replace('{serializationName}', p.ToJson().AsToken())");
            writer.DecreaseIndent();
            writer.WriteLine("else");
            writer.IncreaseIndent();
            writer.WriteLine($"JsonBody.Add('{serializationName}', p.ToJson().AsToken());");
            writer.DecreaseIndent();
        }
        else if (isEnum)
        {
            writer.WriteLine($"if JsonBody.SelectToken('{serializationName}', SubToken) then");
            writer.IncreaseIndent();
            writer.WriteLine($"JsonBody.Replace('{serializationName}', Format(p))");
            writer.DecreaseIndent();
            writer.WriteLine("else");
            writer.IncreaseIndent();
            writer.WriteLine($"JsonBody.Add('{serializationName}', Format(p));");
            writer.DecreaseIndent();
        }
        else
        {
            // Primitive
            writer.WriteLine($"if JsonBody.SelectToken('{serializationName}', SubToken) then");
            writer.IncreaseIndent();
            writer.WriteLine($"JsonBody.Replace('{serializationName}', p)");
            writer.DecreaseIndent();
            writer.WriteLine("else");
            writer.IncreaseIndent();
            writer.WriteLine($"JsonBody.Add('{serializationName}', p);");
            writer.DecreaseIndent();
        }
    }

    private void WriteCollectionSetterBody(CodeMethod method, LanguageWriter writer, string serializationName, bool isEnum, bool isCodeunit)
    {
        if (isCodeunit)
        {
            writer.WriteLine("foreach v in p do");
            writer.IncreaseIndent();
            writer.WriteLine("JSONHelper.AddToArrayIfNotEmpty(JArray, v);");
            writer.DecreaseIndent();
        }
        else if (isEnum)
        {
            writer.WriteLine("foreach v in p do");
            writer.IncreaseIndent();
            writer.WriteLine("JArray.Add(Format(v));");
            writer.DecreaseIndent();
        }
        else
        {
            writer.WriteLine("foreach v in p do");
            writer.IncreaseIndent();
            writer.WriteLine("JArray.Add(v);");
            writer.DecreaseIndent();
        }

        writer.WriteLine($"if JsonBody.SelectToken('{serializationName}', SubToken) then");
        writer.IncreaseIndent();
        writer.WriteLine($"JsonBody.Replace('{serializationName}', JArray)");
        writer.DecreaseIndent();
        writer.WriteLine("else");
        writer.IncreaseIndent();
        writer.WriteLine($"JsonBody.Add('{serializationName}', JArray);");
        writer.DecreaseIndent();
    }

    private void WriteSetBodyBody(CodeMethod method, LanguageWriter writer)
    {
        var hasDebugParam = method.Parameters.Any(p =>
            p.Name.Equals("Debug", StringComparison.OrdinalIgnoreCase));

        if (!hasDebugParam)
        {
            // One-parameter overload - delegate to two-param overload
            writer.WriteLine("SetBody(NewJsonBody, false);");
        }
        else
        {
            // Two-parameter overload
            writer.WriteLine("JsonBody := NewJsonBody;");
            writer.WriteLine("if (Debug) then begin");
            writer.IncreaseIndent();
            writer.WriteLine("#pragma warning disable AA0206");
            writer.WriteLine("DebugCall := true;");
            writer.WriteLine("#pragma warning restore AA0206");
            writer.WriteLine("ValidateBody();");
            writer.DecreaseIndent();
            writer.WriteLine("end;");
        }
    }

    private void WriteToJsonBody(CodeMethod method, LanguageWriter writer)
    {
        var hasParams = method.Parameters.Any(p => !p.CustomData.ContainsKey("local-variable"));

        if (!hasParams)
        {
            // Simple version
            writer.WriteLine("exit(JsonBody);");
        }
        else
        {
            // Full version with parameters
            var parameters = method.Parameters
                .Where(p => !p.CustomData.ContainsKey("local-variable"))
                .ToList();

            foreach (var param in parameters)
            {
                var paramName = param.Name;
                var isCodeunit = param.Type is CodeType { TypeDefinition: CodeClass };
                var isEnum = param.Type is CodeType { TypeDefinition: CodeEnum };
                var isCollection = param.Type.CollectionKind != CodeTypeBase.CodeTypeCollectionKind.None;

                if (isCollection && isCodeunit)
                {
                    // Codeunit collection
                    param.CustomData.TryGetValue("foreach-variable", out var foreachVar);
                    param.CustomData.TryGetValue("corresponding-array", out var arrayName);
                    foreachVar ??= $"{paramName}_item";
                    arrayName ??= $"{paramName}Array";

                    writer.WriteLine($"foreach {foreachVar} in {paramName} do");
                    writer.IncreaseIndent();
                    writer.WriteLine($"JSONHelper.AddToArrayIfNotEmpty({arrayName}, {foreachVar});");
                    writer.DecreaseIndent();
                    writer.WriteLine($"JSONHelper.AddToObjectIfNotEmpty(TargetJson, '{paramName}', {arrayName});");
                }
                else if (isCodeunit)
                {
                    writer.WriteLine($"JSONHelper.AddToObjectIfNotEmpty(TargetJson, '{paramName}', {paramName}.ToJson().AsToken());");
                }
                else if (isEnum)
                {
                    writer.WriteLine($"JSONHelper.AddToObjectIfNotEmpty(TargetJson, '{paramName}', Format({paramName}));");
                }
                else
                {
                    writer.WriteLine($"JSONHelper.AddToObjectIfNotEmpty(TargetJson, '{paramName}', {paramName});");
                }
            }

            writer.WriteLine("exit(TargetJson);");
        }
    }

    private void WriteCustomMethodBody(CodeMethod method, LanguageWriter writer)
    {
        if (method.CustomData.TryGetValue("source", out var source))
        {
            switch (source)
            {
                case "validate-body":
                    WriteValidateBodyBody(method, writer);
                    return;
                case "from indexer":
                    WriteItemIdxBody(method, writer);
                    return;
                case "response-getter":
                    writer.WriteLine("exit(StoredResponse);");
                    return;
                case "response-setter":
                    writer.WriteLine("StoredResponse := ApiResponse;");
                    return;
            }
        }

        // Default custom method - might be unused
        writer.WriteLine("// TODO: Implement custom method body");
    }

    private void WriteValueWrapperGetterBody(CodeMethod method, LanguageWriter writer)
    {
        // exit(FirstName().Value());
        var wrapperGetterName = method.CustomData.TryGetValue("wrapper-getter-name", out var wgn) ? wgn : method.Name;
        writer.WriteLine($"exit({wrapperGetterName}().Value());");
    }

    private void WriteValueWrapperSetterBody(CodeMethod method, LanguageWriter writer)
    {
        // Wrapper.Value(p);
        // FirstName(Wrapper);
        var wrapperGetterName = method.CustomData.TryGetValue("wrapper-getter-name", out var wgn) ? wgn : method.Name;
        writer.WriteLine("Wrapper.Value(p);");
        writer.WriteLine($"{wrapperGetterName}(Wrapper);");
    }

    private void WriteValidateBodyBody(CodeMethod method, LanguageWriter writer)
    {
        if (method.Parent is not CodeClass parentClass) return;

        var getters = parentClass.Methods
            .Where(m => m.IsGetterMethod())
            .ToList();

        var setters = parentClass.Methods
            .Where(m => m.IsSetterMethod())
            .ToList();
        // first all getters
        foreach (var getter in getters)
        {
            var propertyName = getter.SimpleName ?? getter.Name;
            var lowerName = propertyName.ToFirstCharacterLowerCase();

            var hasSetter = setters.Any(s =>
                (s.SimpleName ?? s.Name).Equals(propertyName, StringComparison.OrdinalIgnoreCase));

            if (hasSetter)
            {
                writer.WriteLine($"{lowerName} := {propertyName}();");
            }
        }
        // then all setters to trigger validation
        foreach (var getter in getters)
        {
            var propertyName = getter.SimpleName ?? getter.Name;
            var lowerName = propertyName.ToFirstCharacterLowerCase();

            var hasSetter = setters.Any(s =>
                (s.SimpleName ?? s.Name).Equals(propertyName, StringComparison.OrdinalIgnoreCase));

            if (hasSetter)
            {
                writer.WriteLine($"{propertyName}({lowerName});");
            }
        }
    }

    private void WriteInitializeBody(CodeMethod method, LanguageWriter writer)
    {
        writer.WriteLine("if (not NewAPIAuthorization.IsInitialized()) then");
        writer.IncreaseIndent();
        writer.WriteLine("Error(AuthorizationNotInitializedErr);");
        writer.DecreaseIndent();
        writer.WriteLine("APIAuthorization := NewAPIAuthorization;");
    }

    private void WriteConfigurationBody(CodeMethod method, LanguageWriter writer)
    {
        var hasParam = method.Parameters.Any();

        if (!hasParam)
        {
            // Getter
            writer.WriteLine("if ConfigSet then");
            writer.IncreaseIndent();
            writer.WriteLine("exit(ReqConfig);");
            writer.DecreaseIndent();
            writer.WriteLine("ReqConfig := DefaultConfiguration();");
            writer.WriteLine("exit(ReqConfig);");
        }
        else
        {
            // Setter
            var paramName = method.Parameters.First().Name;
            writer.WriteLine($"ReqConfig := {paramName};");
            writer.WriteLine("ReqConfig.Client(this);");
            writer.WriteLine("ConfigSet := true;");
        }
    }

    private void WriteDefaultConfigurationBody(CodeMethod method, LanguageWriter writer)
    {
        writer.WriteLine("ReqConfig.BaseURL(BaseUrlLbl);");
        writer.WriteLine("ReqConfig.Client(this);");
        writer.WriteLine("ReqConfig.Authorization(APIAuthorization);");
        writer.WriteLine("exit(ReqConfig);");
    }

    private void WriteRawUrlBuilderBody(CodeMethod method, LanguageWriter writer)
    {
        if (method.Name.Equals("SetIdentifier", StringComparison.OrdinalIgnoreCase))
        {
            var param = method.Parameters.FirstOrDefault();
            if (param is not null)
            {
                writer.WriteLine($"Identifier := {param.Name};");
                writer.WriteLine($"ReqConfig.AppendBaseURL(Format(Identifier));");
            }
        }
        else // SetConfiguration
        {
            writer.WriteLine("ReqConfig := NewReqConfig;");

            // Extract URL template from parent class
            if (method.Parent is CodeClass parentClass)
            {
                var urlTemplate = GetUrlTemplatePart(parentClass);
                if (!string.IsNullOrEmpty(urlTemplate))
                    writer.WriteLine($"ReqConfig.AppendBaseURL('{urlTemplate}');");
            }
        }
    }

    private void WriteRequestBuilderGetterBody(CodeMethod method, LanguageWriter writer)
    {
        if (method.Parent is not CodeClass parentClass) return;

        var returnTypeName = conventions.GetTypeString(method.ReturnType, method);
        var varName = "Rqst";

        // Check if this is on the client class
        var isClientClass = parentClass.CustomData.ContainsKey("client-class");
        var configSource = isClientClass ? "Configuration()" : "ReqConfig";

        // Request builder methods return a codeunit that has SetConfiguration
        writer.WriteLine($"{varName}.SetConfiguration({configSource});");
    }

    private void WriteItemIdxBody(CodeMethod method, LanguageWriter writer)
    {
        var param = method.Parameters.FirstOrDefault();
        if (param is null) return;

        writer.WriteLine("Rqst.SetConfiguration(ReqConfig);");
        writer.WriteLine($"Rqst.SetIdentifier({param.Name});");
    }

    #region Helpers

    private string GetSerializationName(CodeMethod method)
    {
        // Try CustomData first (set by refiner from property)
        if (method.CustomData.TryGetValue("serialization-name", out var serName) && !string.IsNullOrEmpty(serName))
            return serName;

        // Fall back to method simple name
        return (method.SimpleName ?? method.Name).ToFirstCharacterLowerCase();
    }

    private static string GetAsMethodForType(string alType)
    {
        return alType.ToUpperInvariant() switch
        {
            "TEXT" => "AsText",
            "INTEGER" => "AsInteger",
            "DECIMAL" => "AsDecimal",
            "BOOLEAN" => "AsBoolean",
            "DATE" => "AsDate",
            "TIME" => "AsTime",
            "DATETIME" => "AsDateTime",
            "GUID" => "AsText",  // GUIDs come as text
            "BIGINTEGER" => "AsBigInteger",
            _ => "AsText",
        };
    }

    private static string GetEnumName(CodeTypeBase typeBase)
    {
        if (typeBase is CodeType { TypeDefinition: CodeEnum enumDef })
        {
            var ns = string.Empty;
            try
            {
                ns = enumDef.GetImmediateParentOfType<CodeNamespace>().Name;
            }
            catch (InvalidOperationException) { }

            return string.IsNullOrEmpty(ns) ? $"\"{enumDef.Name}\"" : $"{ns}.\"{enumDef.Name}\"";
        }
        return $"\"{typeBase.Name}\"";
    }

    private static string GetUrlTemplatePart(CodeClass codeClass)
    {
        // Find URL template property or method
        var urlProp = codeClass.Properties
            .FirstOrDefault(p => p.Kind == CodePropertyKind.UrlTemplate);
        if (urlProp is not null)
        {
            var template = urlProp.DefaultValue?.Trim('"') ?? string.Empty;
            return ExtractUrlSegment(template);
        }

        return string.Empty;
    }

    private static string ExtractUrlSegment(string urlTemplate)
    {
        // Strip {+baseurl} prefix
        var template = urlTemplate.Replace("{+baseurl}", string.Empty, StringComparison.OrdinalIgnoreCase);

        // Remove query string templates
        var queryIdx = template.IndexOf('{', StringComparison.Ordinal);
        if (queryIdx > 0 && template[queryIdx..].Contains('?', StringComparison.Ordinal))
            template = template[..queryIdx];

        // Remove trailing path parameters like {id}
        while (template.EndsWith('}') || template.EndsWith('/'))
        {
            var lastOpen = template.LastIndexOf('{');
            if (lastOpen >= 0)
                template = template[..lastOpen];
            if (template.EndsWith('/'))
                template = template[..^1];
            else
                break;
        }

        // Take the last segment
        var segments = template.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length > 0)
            return "/" + segments[^1];

        return template;
    }

    /// <summary>
    /// Writes AL XML documentation comments above a procedure declaration.
    /// Emits summary, param, and returns tags following the AL triple-slash convention.
    /// </summary>
    private void WriteMethodDocumentation(CodeMethod method, LanguageWriter writer)
    {
        var documentation = method.Documentation;
        if (documentation is null) return;

        var hasDescription = documentation.DescriptionAvailable;
        var hasExternalDocs = documentation.ExternalDocumentationAvailable;
        var hasReturnType = method.ReturnType is not null
                           && !"void".Equals(method.ReturnType.Name, StringComparison.OrdinalIgnoreCase)
                           && method.Kind is not (CodeMethodKind.Constructor or CodeMethodKind.ClientConstructor);
        var paramsWithDocs = method.Parameters
            .Where(static p => p.Documentation.DescriptionAvailable)
            .OrderBy(static p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Nothing to write?
        if (!hasDescription && !hasExternalDocs && !hasReturnType && paramsWithDocs.Count == 0)
            return;

        var prefix = conventions.DocCommentPrefix;

        // <summary> block
        if (hasDescription || hasExternalDocs)
        {
            writer.WriteLine($"{prefix}<summary>");
            if (hasDescription)
            {
                var description = documentation.GetDescription(type => conventions.GetTypeString(type, method));
                writer.WriteLine($"{prefix}{description}");
            }
            if (hasExternalDocs)
                writer.WriteLine($"{prefix}{documentation.DocumentationLabel} - {documentation.DocumentationLink}");
            writer.WriteLine($"{prefix}</summary>");
        }

        // <param> tags
        foreach (var param in paramsWithDocs)
        {
            var paramDescription = param.Documentation.GetDescription(type => conventions.GetTypeString(type, param));
            writer.WriteLine($"{prefix}<param name=\"{param.Name}\">{paramDescription}</param>");
        }

        // <returns> tag
        if (hasReturnType && method.ReturnType is not null)
        {
            var returnTypeName = conventions.GetTypeString(method.ReturnType, method);
            writer.WriteLine($"{prefix}<returns>A {returnTypeName}</returns>");
        }
    }

    #endregion
}
