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
        if (codeElement.GetFlag(ALCustomDataKeys.Skip))
            return;

        if (codeElement.Kind == CodeMethodKind.RawUrlConstructor ||
            codeElement.Kind == CodeMethodKind.RequestGenerator)
            return;

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

        var returnVarName = method.GetData(ALCustomDataKeys.ReturnVariableName);
        if (!String.IsNullOrEmpty(returnVarName))
            returnVarName = $" {returnVarName}"; // Precede with space for formatting, that's what the AL formatter would do anyway
        var returnClause = !string.IsNullOrEmpty(returnType) && !returnType.Equals("void", StringComparison.OrdinalIgnoreCase)
            ? $"{returnVarName}: {returnType}" : string.Empty;

        // Pragma for method
        var pragmas = method.GetData(ALCustomDataKeys.Pragmas);
        if (!string.IsNullOrEmpty(pragmas))
            writer.WriteLine($"#pragma warning disable {pragmas}", false);

        writer.WriteLine($"{access}procedure {methodName}({paramStr}){returnClause}");

        if (!string.IsNullOrEmpty(pragmas))
            writer.WriteLine($"#pragma warning restore {pragmas}", false);

        // Local variables
        if (method.HasVariables())
        {
            writer.WriteLine("var");
            writer.IncreaseIndent();

            var varPragmas = method.GetData(ALCustomDataKeys.PragmasVariables);

            if (!string.IsNullOrEmpty(varPragmas))
                writer.WriteLine($"#pragma warning disable {varPragmas}", false);

            foreach (var v in method.Variables())
            {
                var typeName = conventions.GetTypeString(v.Type, method);
                writer.WriteLine($"{v.Name}: {typeName};");
            }

            if (!string.IsNullOrEmpty(varPragmas))
                writer.WriteLine($"#pragma warning restore {varPragmas}", false);

            writer.DecreaseIndent();
        }

        writer.WriteLine("begin");
        writer.IncreaseIndent();
    }

    private string FormatParameter(CodeParameter param)
    {
        var typeName = conventions.GetTypeString(param.Type, param);

        // Check if parameter needs 'var' (by reference)
        var byRef = param.GetFlag(ALCustomDataKeys.ByRef);

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

        // Find companion RequestGenerator method to retrieve Content-Type / Accept header values
        var generatorMethod = parentClass?.Methods
            .FirstOrDefault(m => m.IsOfKind(CodeMethodKind.RequestGenerator) && m.HttpMethod == method.HttpMethod);

        if (method.SourceIs(ALCustomDataKeys.Sources.MultipartOverload))
        {
            var fieldName = method.GetData(ALCustomDataKeys.MultipartFieldName, "file");
            writer.WriteLine($"body.Initialize(Filename);");
            writer.WriteLine($"body.WriteMultipartContent(FileBody, '{fieldName}');");
            if (method.Parameters.Any(p => p.Name.Equals("Parameters", StringComparison.OrdinalIgnoreCase)))
                writer.WriteLine($"exit(this.{method.Name}(body, Parameters));");
            else
                writer.WriteLine($"exit(this.{method.Name}(body));");
            return;
        }
        // Body parameter (hoisted so it can inform the Content-Type header below)
        var bodyParam = method.Parameters.FirstOrDefault(p => p.Kind == CodeParameterKind.RequestBody);

        // Content-Type header – emit only when there is a request body and the spec declares a media type
        if (bodyParam is not null && !bodyParam.Type.Name.Equals("MultipartBody", StringComparison.OrdinalIgnoreCase) && generatorMethod is not null && !string.IsNullOrEmpty(generatorMethod.RequestBodyContentType))
            writer.WriteLine($"ReqConfig.AddHeader('Content-Type', '{generatorMethod.RequestBodyContentType}');");

        // Accept header – emit when the spec declares accepted response media types
        if (generatorMethod is not null && generatorMethod.ShouldAddAcceptHeader)
            writer.WriteLine($"ReqConfig.AddHeader('Accept', '{generatorMethod.AcceptHeaderValue}');");


        writer.WriteLine("RequestHandler.SetClientConfig(ReqConfig);");
        // Query parameters
        if (method.GetFlag(ALCustomDataKeys.UseParameterCodeunit))
        {
            var paramsParam = method.Parameters.FirstOrDefault(p => p.Name.Equals("Parameters", StringComparison.OrdinalIgnoreCase));
            if (paramsParam is not null)
                writer.WriteLine("RequestHandler.AddQueryParameter(Parameters.GetQueryParameters());");
        }
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
                {
                    switch (alType)
                    {
                        case "Blob":
                            writer.WriteLine($"exit(ReqConfig.Client().Response().GetContent().AsBlob());");
                            break;
                        case "InStream":
                            writer.WriteLine($"exit(ReqConfig.Client().Response().GetContent().AsInStream());");
                            break;
                        case "Json":
                            writer.WriteLine($"exit(ReqConfig.Client().Response().GetContent().AsJson());");
                            break;
                        case "JsonArray":
                            writer.WriteLine($"exit(ReqConfig.Client().Response().GetContent().AsJsonArray().AsArray());");
                            break;
                        case "JsonObject":
                            writer.WriteLine($"exit(ReqConfig.Client().Response().GetContent().AsJsonObject().AsObject());");
                            break;
                        case "SecretText":
                            writer.WriteLine($"exit(ReqConfig.Client().Response().GetContent().AsSecretText());");
                            break;
                        case "Text":
                            writer.WriteLine($"exit(ReqConfig.Client().Response().GetContent().AsText());");
                            break;
                        case "XmlDocument":
                            writer.WriteLine($"exit(ReqConfig.Client().Response().GetContent().AsXmlDocument());");
                            break;
                        default:
                            writer.WriteLine($"exit(ReqConfig.Client().Response().GetContent().AsJson().AsValue().{asMethod}());");
                            break;
                    }
                }
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
        var isDictionary = returnType.IsDictionaryType();
        var isCollection = returnType.CollectionKind != CodeTypeBase.CodeTypeCollectionKind.None;
        var isEnum = returnType is CodeType { TypeDefinition: CodeEnum };
        var isCodeunit = returnType is CodeType { TypeDefinition: CodeClass };
        var isWrapperGetter = method.SourceIs(ALCustomDataKeys.Sources.ValueWrapperGetter);
        if (isWrapperGetter)
        {
            WriteValueWrapperGetterBody(method, writer);
        }
        else if (isDictionary)
        {
            WriteDictionaryGetterBody(method, writer, serializationName, isEnum, isCodeunit);
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

    private void WriteDictionaryGetterBody(CodeMethod method, LanguageWriter writer, string serializationName, bool isEnum, bool isCodeunit)
    {
        writer.WriteLine($"if not JsonBody.SelectToken('{serializationName}', JToken) then");
        writer.IncreaseIndent();
        writer.WriteLine("exit;");
        writer.DecreaseIndent();
        writer.WriteLine("JObject := JToken.AsObject();");
        writer.WriteLine("foreach KeyText in JObject.Keys do begin");
        writer.IncreaseIndent();
        writer.WriteLine("JObject.Get(KeyText, JToken);");
        if (isEnum)
        {
            writer.WriteLine("Evaluate(EnumValue, JToken.AsValue().AsText());");
            writer.WriteLine("ReturnDict.Add(KeyText, EnumValue);");
        }
        else if (isCodeunit)
        {
            writer.WriteLine("Clear(TargetCodeunit);");
            writer.WriteLine("TargetCodeunit.SetBody(JToken.AsObject(), DebugCall);");
            writer.WriteLine("ReturnDict.Add(KeyText, TargetCodeunit);");
        }
        else
        {
            // Primitive fallback – store the raw text value
            writer.WriteLine("ReturnDict.Add(KeyText, JToken.AsValue().AsText());");
        }
        writer.DecreaseIndent();
        writer.WriteLine("end;");
    }

    private void WriteSinglePrimitiveGetterBody(CodeMethod method, LanguageWriter writer, string serializationName, CodeTypeBase returnType)
    {
        var alType = conventions.GetTypeString(returnType, method);
        var asMethod = GetAsMethodForType(alType);
        writer.WriteLine($"if JsonBody.SelectToken('{serializationName}', SubToken) then");
        writer.IncreaseIndent();
        writer.WriteLine($"if not JSONHelper.SubTokenIsNull(SubToken) then");
        writer.IncreaseIndent();
        writer.WriteLine($"exit(SubToken.AsValue().{asMethod}());");
        writer.DecreaseIndent();
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
        writer.WriteLine($"#pragma warning disable AA0217");
        writer.WriteLine($"// No match found for {serializationName} value");
        writer.WriteLine($"Error(StrSubstNo('Invalid value for {propertyName}: %1', SubToken.AsValue().AsText()));");
        writer.WriteLine($"#pragma warning restore AA0217");
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
            var alType = conventions.GetTypeString(method.ReturnType, method, false);
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
        var returnType = method.Parameters.FirstOrDefault(p => !p.HasData(ALCustomDataKeys.LocalVariable))?.Type;
        if (returnType is null) return;
        var isDictionary = returnType.IsDictionaryType();
        var isCollection = returnType.CollectionKind != CodeTypeBase.CodeTypeCollectionKind.None;
#pragma warning disable CA1508 // Avoid dead conditional code
        var isEnum = returnType is CodeType { TypeDefinition: CodeEnum };
        var isCodeunit = returnType is CodeType { TypeDefinition: CodeClass };
#pragma warning restore CA1508
        var isWrapperSetter = method.SourceIs(ALCustomDataKeys.Sources.ValueWrapperSetter);
        if (isWrapperSetter)
        {
            WriteValueWrapperSetterBody(method, writer);
        }
        else if (isDictionary)
        {
            WriteDictionarySetterBody(method, writer, serializationName, isEnum, isCodeunit);
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
            if (returnType.Name.Equals("Guid", StringComparison.OrdinalIgnoreCase))
            {
                writer.WriteLine($"JsonBody.Replace('{serializationName}', JSONHelper.FormatGuid(p))");
            }
            else
            {
                writer.WriteLine($"JsonBody.Replace('{serializationName}', p)");
            }
            writer.DecreaseIndent();
            writer.WriteLine("else");
            writer.IncreaseIndent();
            if (returnType.Name.Equals("Guid", StringComparison.OrdinalIgnoreCase))
            {
                writer.WriteLine($"JsonBody.Add('{serializationName}', JSONHelper.FormatGuid(p));");
            }
            else
            {
                writer.WriteLine($"JsonBody.Add('{serializationName}', p);");
            }
            writer.DecreaseIndent();
        }
    }

    private void WriteDictionarySetterBody(CodeMethod method, LanguageWriter writer, string serializationName, bool isEnum, bool isCodeunit)
    {
        // Iterate the caller's dictionary and build a JSON object
        writer.WriteLine("foreach KeyText in p.Keys do begin");
        writer.IncreaseIndent();
        if (isEnum)
        {
            writer.WriteLine("p.Get(KeyText, EnumValue);");
            writer.WriteLine("JObject.Add(KeyText, Format(EnumValue));");
        }
        else if (isCodeunit)
        {
            writer.WriteLine("p.Get(KeyText, TargetCodeunit);");
            writer.WriteLine("JObject.Add(KeyText, TargetCodeunit.ToJson().AsToken());");
        }
        else
        {
            // Primitive fallback
            writer.WriteLine("p.Get(KeyText, SubToken);");
            writer.WriteLine("JObject.Add(KeyText, SubToken);");
        }
        writer.DecreaseIndent();
        writer.WriteLine("end;");

        writer.WriteLine($"if JsonBody.SelectToken('{serializationName}', SubToken) then");
        writer.IncreaseIndent();
        writer.WriteLine($"JsonBody.Replace('{serializationName}', JObject.AsToken())");
        writer.DecreaseIndent();
        writer.WriteLine("else");
        writer.IncreaseIndent();
        writer.WriteLine($"JsonBody.Add('{serializationName}', JObject.AsToken());");
        writer.DecreaseIndent();
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
            writer.WriteLine("#pragma warning disable AA0206", false);
            writer.WriteLine("DebugCall := true;");
            writer.WriteLine("#pragma warning restore AA0206", false);
            writer.WriteLine("ValidateBody();");
            writer.DecreaseIndent();
            writer.WriteLine("end;");
        }
    }

    private void WriteToJsonBody(CodeMethod method, LanguageWriter writer)
    {
        var hasParams = method.Parameters.Any(p => !p.HasData(ALCustomDataKeys.LocalVariable));

        if (!hasParams)
        {
            // Simple version
            writer.WriteLine("exit(JsonBody);");
        }
        else
        {
            // Full version with parameters
            var parameters = method.Parameters
                .Where(p => !p.HasData(ALCustomDataKeys.LocalVariable))
                .ToList();

            foreach (var param in parameters)
            {
                var paramName = param.Name;
                var paramNameClean = paramName;
                if (param.TryGetData(ALCustomDataKeys.PropertyName, out var propName)) // For parameters that had to be renamed due to reserved name conflicts, use the original property name for serialization
                    paramNameClean = propName;
                var isCodeunit = param.Type is CodeType { TypeDefinition: CodeClass };
                var isEnum = param.Type is CodeType { TypeDefinition: CodeEnum };
                var isCollection = param.Type.CollectionKind != CodeTypeBase.CodeTypeCollectionKind.None;
                var isDictionary = param.Type.IsDictionaryType();
                if (isDictionary)
                {
                    param.TryGetData(ALCustomDataKeys.KeyVariable, out var keyVar);
                    param.TryGetData(ALCustomDataKeys.ValueVariable, out var valueVar);
                    param.TryGetData(ALCustomDataKeys.ObjectVariable, out var objVar);
                    writer.WriteLine($"foreach {keyVar} in {paramName}.Keys do");
                    writer.IncreaseIndent();
                    writer.WriteLine($"if {paramName}.Get({keyVar}, {valueVar}) then");
                    writer.IncreaseIndent();
                    if (isEnum)
                    {
                        writer.WriteLine($"JSONHelper.AddToObjectIfNotEmpty({objVar}, {keyVar}, Format({valueVar}));");
                    }
                    else if (isCodeunit)
                    {
                        writer.WriteLine($"JSONHelper.AddToObjectIfNotEmpty({objVar}, {keyVar}, {valueVar}.AsToken());");
                    }
                    else
                    {
                        // Primitive fallback
                        writer.WriteLine($"JSONHelper.AddToObjectIfNotEmpty({objVar}, {keyVar}, {valueVar});");
                    }
                    writer.DecreaseIndent();
                    writer.DecreaseIndent();
                    writer.WriteLine($"JSONHelper.AddToObjectIfNotEmpty(TargetJson, '{paramNameClean}', {objVar}.AsToken());");
                }
                else if (isCollection && isCodeunit)
                {
                    // Codeunit collection
                    param.TryGetData(ALCustomDataKeys.ForeachVariable, out var foreachVar);
                    param.TryGetData(ALCustomDataKeys.CorrespondingArray, out var arrayName);
                    foreachVar ??= $"{paramName}_item";
                    arrayName ??= $"{paramName}Array";

                    writer.WriteLine($"foreach {foreachVar} in {paramNameClean} do");
                    writer.IncreaseIndent();
                    writer.WriteLine($"JSONHelper.AddToArrayIfNotEmpty({arrayName}, {foreachVar});");
                    writer.DecreaseIndent();
                    writer.WriteLine($"JSONHelper.AddToObjectIfNotEmpty(TargetJson, '{paramNameClean}', {arrayName});");
                }
                else if (isCodeunit)
                {
                    writer.WriteLine($"JSONHelper.AddToObjectIfNotEmpty(TargetJson, '{paramNameClean}', {paramName});");
                }
                else if (isEnum)
                {
                    writer.WriteLine($"JSONHelper.AddToObjectIfNotEmpty(TargetJson, '{paramNameClean}', Format({paramName}));");
                }
                else
                {
                    writer.WriteLine($"JSONHelper.AddToObjectIfNotEmpty(TargetJson, '{paramNameClean}', {paramName});");
                }
            }

            writer.WriteLine("exit(TargetJson);");
        }
    }

    private void WriteCustomMethodBody(CodeMethod method, LanguageWriter writer)
    {
        if (method.TryGetData(ALCustomDataKeys.Source, out var source))
        {
            switch (source)
            {
                case ALCustomDataKeys.Sources.ValidateBody:
                    WriteValidateBodyBody(method, writer);
                    return;
                case ALCustomDataKeys.Sources.FromIndexer:
                    WriteItemIdxBody(method, writer);
                    return;
                case ALCustomDataKeys.Sources.ResponseGetter:
                    writer.WriteLine("exit(StoredResponse);");
                    return;
                case ALCustomDataKeys.Sources.ResponseSetter:
                    writer.WriteLine("StoredResponse := ApiResponse;");
                    return;
                case ALCustomDataKeys.Sources.QueryParamGenericSetter:
                    WriteQueryParamGenericSetterBody(writer);
                    return;
                case ALCustomDataKeys.Sources.QueryParamTypedSetter:
                    WriteQueryParamTypedSetterBody(method, writer);
                    return;
                case ALCustomDataKeys.Sources.QueryParamGetter:
                    writer.WriteLine("exit(QueryParameters);");
                    return;
            }
        }

        // Default custom method - might be unused
        writer.WriteLine("// TODO: Implement custom method body");
    }

    private static void WriteQueryParamGenericSetterBody(LanguageWriter writer)
    {
        writer.WriteLine("if QueryParameters.ContainsKey(QueryKey) then");
        writer.IncreaseIndent();
        writer.WriteLine("QueryParameters.Remove(QueryKey);");
        writer.DecreaseIndent();
        writer.WriteLine("QueryParameters.Add(QueryKey, QueryValue);");
    }

    private static void WriteQueryParamTypedSetterBody(CodeMethod method, LanguageWriter writer)
    {
        var paramName = method.GetData(ALCustomDataKeys.QueryParamName, method.Name);
        var typeCategory = method.GetData(ALCustomDataKeys.QueryParamTypeCategory, "primitive");
        var valueExpr = typeCategory switch
        {
            "text" => "Value",
            "enum" => "Format(Value)",
            _ => "this.QueryParamFormatter.FormatAsText(Value)",
        };
        writer.WriteLine($"this.SetQueryParameter('{paramName}', {valueExpr});");
    }

    private void WriteValueWrapperGetterBody(CodeMethod method, LanguageWriter writer)
    {
        // exit(FirstName().Value());
        var wrapperGetterName = method.GetData(ALCustomDataKeys.WrapperGetterName, method.Name);
        writer.WriteLine($"exit({wrapperGetterName}().Value());");
    }

    private void WriteValueWrapperSetterBody(CodeMethod method, LanguageWriter writer)
    {
        // Wrapper.Value(p);
        // FirstName(Wrapper);
        var wrapperGetterName = method.GetData(ALCustomDataKeys.WrapperGetterName, method.Name);
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
            var param = method.Parameters.FirstOrDefault(p => !p.IsLocalVariable());
            if (param is not null)
            {
                writer.WriteLine($"Identifier := {param.Name};");
                var typeString = conventions.GetTypeString(param.Type, method);
                if (typeString == "Guid")
                    writer.WriteLine($"ReqConfig.AppendBaseURL('/' + JsonHelper.FormatGuid(Identifier));");
                else
                    writer.WriteLine($"ReqConfig.AppendBaseURL('/' + Format(Identifier));");
            }
        }
        else // SetConfiguration
        {
            writer.WriteLine("ReqConfig := NewReqConfig;");

            // Extract URL template from parent class
            if (method.Parent is CodeClass parentClass)
            {
                if (parentClass.Methods.Any(m => m.Name.Equals("SetIdentifier", StringComparison.OrdinalIgnoreCase)) && parentClass.Methods.Any(m => m.Name.Equals("SetConfiguration", StringComparison.OrdinalIgnoreCase)))
                    return; // if there is "SetIdentifier" and "SetConfiguration" it means we are in a With-Request-Builder scenario and the URL template is already applied from the parent request builder
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
        var isClientClass = parentClass.HasData(ALCustomDataKeys.ClientClass);
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
        if (method.TryGetData(ALCustomDataKeys.SerializationName, out var serName) && !string.IsNullOrEmpty(serName))
            return serName;
        if (method.TryGetData(ALCustomDataKeys.PropertyName, out var propName) && !string.IsNullOrEmpty(propName))
            return propName;

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
        var documentationPragmas = method.GetData(ALCustomDataKeys.DocumentationPragmas);
        if (!string.IsNullOrEmpty(documentationPragmas))
            writer.WriteLine($"#pragma warning disable {documentationPragmas}", false);
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
        if (!string.IsNullOrEmpty(documentationPragmas))
            writer.WriteLine($"#pragma warning restore {documentationPragmas}", false);
    }

    #endregion
}
