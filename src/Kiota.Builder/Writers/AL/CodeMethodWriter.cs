using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Kiota.Builder.CodeDOM;
using Kiota.Builder.Extensions;
using Kiota.Builder.OrderComparers;

namespace Kiota.Builder.Writers.AL;

public class CodeMethodWriter : BaseElementWriter<CodeMethod, ALConventionService>
{
    public CodeMethodWriter(ALConventionService conventionService) : base(conventionService) { }

    public override void WriteCodeElement(CodeMethod codeElement, LanguageWriter writer)
    {
        var alWriter = writer as ALWriter;
        ArgumentNullException.ThrowIfNull(codeElement);
        ArgumentNullException.ThrowIfNull(alWriter);
        if (codeElement.IsOfKind(CodeMethodKind.RawUrlConstructor, CodeMethodKind.RequestGenerator)) return;
        if (codeElement.GetCustomProperty("skip") == "true") return; // skip methods that are marked to be skipped
        if (codeElement.ParentIsSkipped()) return; // we can't handle nested classes, but also can't remove them from the model
        if (codeElement.ReturnType == null) throw new InvalidOperationException($"{nameof(codeElement.ReturnType)} should not be null");
        ArgumentNullException.ThrowIfNull(alWriter);
        if (codeElement.Parent is not CodeClass parentClass) throw new InvalidOperationException("the parent of a method should be a class");
        if (codeElement.Name.StartsWith("AAA", StringComparison.CurrentCulture) && !String.IsNullOrEmpty(codeElement.SimpleName)) // this is because of the workaround in ALRefiner to sort the methods
            codeElement.Name = codeElement.SimpleName;
        var returnType = conventions.GetTypeString(codeElement.ReturnType, codeElement);
        //WriteMethodDocumentation(codeElement, writer); // TODO-SF: Implement documentation 
        WriteMethodPrototype(codeElement, alWriter, returnType);
        alWriter.WriteVariablesDeclaration(codeElement.Variables(), codeElement);
        alWriter.WriteLine("begin");
        alWriter.IncreaseIndent();
        HandleMethodKind(codeElement, alWriter, parentClass);
        alWriter.CloseBlock("end;");
    }

    private void WriteMethodPrototype(CodeMethod code, ALWriter writer, string returnType)
    {
        if (code.HasPragmas())
            writer.WritePragmaConditionalDisable(code.GetPragmas());
        var completeReturnType = returnType;
        
        // Check if using parameter codeunit approach
        var useParameterCodeunit = code.GetCustomProperty("use-parameter-codeunit") == "true";
        string parameters;
        
        if (useParameterCodeunit)
        {
            // When using parameter codeunit, filter out individual query parameters
            // but keep the parameter codeunit parameter (which is already added by ALRefiner)
            var filteredParameters = code.OrderedParameters()
                .Where(p => !p.IsOfKind(CodeParameterKind.QueryParameter) || 
                           p.IsOfKind(CodeParameterKind.RequestConfiguration) ||
                           p.GetCustomProperty("parameter-codeunit") == "true")
                .Select(p => conventions.GetParameterSignature(p, code))
                .ToList();
            
            parameters = string.Join("; ", filteredParameters);
        }
        else
        {
            // Use legacy approach with all parameters
            parameters = string.Join("; ", code.OrderedParameters().Select(p => conventions.GetParameterSignature(p, code)).ToList());
        }
        
        // Use the natural method name from the operation ID
        var methodName = code.Name.ToFirstCharacterUpperCase();
        
        var returnValueName = String.Empty;
        if (code.GetCustomProperty("return-variable-name") is string returnVariableName)
            returnValueName = $"{returnVariableName}";

        writer.WriteLine($"{conventions.GetAccessModifier(code.Access)}procedure {methodName}({parameters}) {(String.IsNullOrEmpty(completeReturnType.Trim()) ? "" : $"{returnValueName}: {completeReturnType}")}");
        if (code.HasPragmas())
            writer.WritePragmaConditionalRestore(code.GetPragmas());
    }

    protected virtual void HandleMethodKind(CodeMethod codeElement, LanguageWriter writer, CodeClass parentClass)
    {
        ArgumentNullException.ThrowIfNull(codeElement);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(parentClass);
        var returnType = conventions.GetTypeString(codeElement.ReturnType, codeElement);
        var returnTypeWithoutCollectionInformation = conventions.GetTypeString(codeElement.ReturnType, codeElement, false);
        var requestBodyParam = codeElement.Parameters.OfKind(CodeParameterKind.RequestBody);
        var requestConfig = codeElement.Parameters.OfKind(CodeParameterKind.RequestConfiguration);
        var requestContentType = codeElement.Parameters.OfKind(CodeParameterKind.RequestBodyContentType);
        var requestParams = new RequestParams(requestBodyParam, requestConfig, requestContentType);
        switch (codeElement.Kind)
        {
            case CodeMethodKind.Serializer:
                WriteToJsonMethodBody(codeElement, parentClass, writer);
                break;
            case CodeMethodKind.RequestGenerator:
                throw new InvalidOperationException("RequestGenerator is not supported right now.");
            case CodeMethodKind.RequestExecutor:
                WriteRequestExecutorBody(codeElement, writer);
                break;
            case CodeMethodKind.Deserializer:
                WriteSetBodyMethodBody(codeElement, writer);
                break;
            case CodeMethodKind.ClientConstructor:
                WriteConfigurationMethodBody(codeElement, writer);
                break;
            case CodeMethodKind.RawUrlBuilder:
                WriteSetConfigurationMethodBody(codeElement, parentClass, writer);
                WriteSetIdentifierMethodBody(codeElement, parentClass, writer);
                break;
            case CodeMethodKind.Constructor:
                WriteApiClientInitializeMethodBody(codeElement, parentClass, writer);
                break;
            case CodeMethodKind.RawUrlConstructor:
                throw new InvalidOperationException("Constructor/RawUrlConstructor is not supported right now.");
            case CodeMethodKind.RequestBuilderWithParameters:
                throw new InvalidOperationException("RequestBuilderWithParameters is not supported right now.");
            case CodeMethodKind.Getter:
                WriteFromPropertyGetterMethodBody(codeElement, writer);
                break;
            case CodeMethodKind.Setter:
                WriteFromPropertySetterMethodBody(codeElement, writer);
                break;
            case CodeMethodKind.RequestBuilderBackwardCompatibility:
                WriteFromRequestBuilderSourceMethodBody(codeElement, parentClass, writer);
                break;
            case CodeMethodKind.ErrorMessageOverride:
                throw new InvalidOperationException("ErrorMessageOverride is not supported as the error message is implemented by a property.");
            case CodeMethodKind.CommandBuilder:
                throw new InvalidOperationException("CommandBuilder is not supported right now.");
            case CodeMethodKind.Factory:
                WriteDefaultConfigurationMethodBody(codeElement, writer);
                break;
            case CodeMethodKind.ComposedTypeMarker:
                throw new InvalidOperationException("ComposedTypeMarker is not required as interface is explicitly implemented.");
            case CodeMethodKind.Custom:
                WriteValidateBodyMethodBody(codeElement, parentClass, writer);
                WriteItemIdxMethodBody(codeElement, parentClass, writer);
                WriteResponseGetterSetter(codeElement, parentClass, writer);
                break;
            default:
                writer.WriteLine("return null;");
                break;
        }
    }

    private static void WriteRequestExecutorBody(CodeMethod codeElement, LanguageWriter writer)
    {
        if (codeElement.HttpMethod is null)
            throw new InvalidOperationException($"{nameof(codeElement.HttpMethod)} cannot be null");

        writer.WriteLine("RequestHandler.SetClientConfig(ReqConfig);");

        // Check if this method should use parameter codeunit approach
        var useParameterCodeunit = codeElement.GetCustomProperty("use-parameter-codeunit") == "true";
        var parameterCodeunitParam = codeElement.Parameters.FirstOrDefault(p => p.GetCustomProperty("parameter-codeunit") == "true");
        
        if (useParameterCodeunit && parameterCodeunitParam != null)
        {
            WriteParameterCodeunitQueryHandling(codeElement, writer, parameterCodeunitParam);
        }
        else
        {
            // Legacy approach - handle individual query parameters
            var queryStringParameters = codeElement.Parameters.Where(static x => x.IsOfKind(CodeParameterKind.QueryParameter) && !x.IsOfKind(CodeParameterKind.RequestConfiguration)).OrderBy(static x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            if (queryStringParameters.Length != 0)
            {
                WriteLegacyQueryParameterHandling(codeElement, writer, queryStringParameters);
            }
        }
        
        var parameter = codeElement.Parameters.FirstOrDefault(x => x.Name.Contains("body", StringComparison.OrdinalIgnoreCase));
        if (parameter is not null)
            if (parameter.Type.IsCollection)
            {
                writer.WriteLine("// TODO: Fix collection Handling: ");
                writer.WriteLine($"// RequestHandler.SetBody(body);");
            }
            else
                writer.WriteLine("RequestHandler.SetBody(body);");
        if (codeElement.HttpMethod is not null)
            writer.WriteLine($"RequestHandler.SetMethod(enum::System.RestClient.\"Http Method\"::{codeElement.HttpMethod?.ToString().ToUpper(CultureInfo.CurrentCulture)});");
        writer.WriteLine("RequestHandler.HandleRequest();");

        WriteResponseCreation(codeElement, writer);
    }

    private static void WriteParameterCodeunitQueryHandling(CodeMethod codeElement, LanguageWriter writer, CodeParameter parameterCodeunitParam)
    {
        // Get the parameter codeunit CodeClass to extract parameter information
        var parameterCodeunitType = parameterCodeunitParam.Type as CodeType;
        var parameterCodeunitClass = parameterCodeunitType?.TypeDefinition as CodeClass;
        if (parameterCodeunitClass == null) return;

        var queryParametersJson = parameterCodeunitClass.GetCustomProperty("query-parameters");
        if (string.IsNullOrEmpty(queryParametersJson)) return;

        writer.WriteLine("// Set query parameters using parameter codeunit");
        
        // Parse the parameter string containing parameter information
        // Format: "param1:Type1,param2:Type2,..."
        var parameters = queryParametersJson.Split(',');
        
        foreach (var paramInfo in parameters)
        {
            var parts = paramInfo.Split(':');
            if (parts.Length != 2) continue;
            
            var paramName = parts[0].Trim();
            var paramType = parts[1].Trim();
            
            // For now, assume the query parameter name matches the parameter name (lowercased)
            // In the future, this could be enhanced to use SerializationName from the original parameters
            var queryParameterName = paramName.ToFirstCharacterLowerCase();
            
            writer.WriteLine($"if {parameterCodeunitParam.Name}.Is{paramName}Set() then begin");
            writer.IncreaseIndent();
            
            if (paramType.Contains("List of", StringComparison.OrdinalIgnoreCase))
            {
                // For collections - create local variable to join with commas
                writer.WriteLine("JoinedValues := '';");
                writer.WriteLine($"foreach Item in {parameterCodeunitParam.Name}.Get{paramName}() do begin");
                writer.IncreaseIndent();
                writer.WriteLine("if JoinedValues <> '' then");
                writer.IncreaseIndent();
                writer.WriteLine("JoinedValues := JoinedValues + ',';");
                writer.DecreaseIndent();
                writer.WriteLine("JoinedValues := JoinedValues + Format(Item);");
                writer.DecreaseIndent();
                writer.WriteLine("end;");
                writer.WriteLine($"RequestHandler.AddQueryParameter('{queryParameterName}', JoinedValues);");
            }
            else
            {
                // For scalar parameters
                writer.WriteLine($"RequestHandler.AddQueryParameter('{queryParameterName}', {parameterCodeunitParam.Name}.Get{paramName}());");
            }
            
            writer.DecreaseIndent();
            writer.WriteLine("end;");
        }
    }

    private static void WriteLegacyQueryParameterHandling(CodeMethod codeElement, LanguageWriter writer, CodeParameter[] queryStringParameters)
    {
        writer.WriteLine("// Set query parameters");
        
        foreach (var queryParam in queryStringParameters)
        {
            var paramName = queryParam.Name.ToFirstCharacterLowerCase();
            
            // Use the parameter name as the query parameter name - prioritize SerializationName if available
            var queryParameterName = !string.IsNullOrEmpty(queryParam.SerializationName) ? queryParam.SerializationName : queryParam.Name;
            
            // Handle different parameter types
            if (queryParam.Type.IsCollection)
            {
                // For collections - create local variable to join with commas
                writer.WriteLine($"JoinedValues := '';");
                writer.WriteLine($"foreach Item in {paramName} do begin");
                writer.IncreaseIndent();
                writer.WriteLine($"if JoinedValues <> '' then");
                writer.IncreaseIndent();
                writer.WriteLine($"JoinedValues := JoinedValues + ',';");
                writer.DecreaseIndent();
                writer.WriteLine($"JoinedValues := JoinedValues + Format(Item);");
                writer.DecreaseIndent();
                writer.WriteLine($"end;");
                writer.WriteLine($"RequestHandler.AddQueryParameter('{queryParameterName}', JoinedValues);");
            }
            else
            {
                // For all other parameters - pass directly without conditional checks
                writer.WriteLine($"RequestHandler.AddQueryParameter('{queryParameterName}', {paramName});");
            }
        }
    }

    private static void WriteResponseCreation(CodeMethod codeElement, LanguageWriter writer)
    {
        var returnType = codeElement.ReturnType;
        if (returnType != null && !returnType.Name.Equals("void", StringComparison.OrdinalIgnoreCase))
        {
            var returnVariableName = codeElement.GetCustomProperty("return-variable-name");
            if (!string.IsNullOrEmpty(returnVariableName))
            {
                // Method has a Target parameter - set the response on it
                writer.WriteLine("if ReqConfig.Client().Response().GetIsSuccessStatusCode() then");
                writer.IncreaseIndent();
                writer.WriteLine($"{returnVariableName}.SetBody(ReqConfig.Client().Response().GetContent().AsJson().AsObject());");
                writer.DecreaseIndent();
            }
            // For methods that return HttpContent directly (like DELETE, POST), don't add any response handling
            // The caller will handle the response as needed
        }
    }

    private void WriteResponseGetterSetter(CodeMethod codeElement, CodeClass parentClass, LanguageWriter writer)
    {
        if (codeElement.Name != "Response")
            return;
        var globalVariable = parentClass.GlobalVariables().FirstOrDefault(p => p.Type.Name.Contains("System.RestClient.\"Http Response Message\"", StringComparison.OrdinalIgnoreCase));
        ArgumentNullException.ThrowIfNull(globalVariable);
        var parameter = codeElement.Parameters.FirstOrDefault();
        if (parameter is null)
            writer.WriteLine($"exit({globalVariable.Name});");
        else
            writer.WriteLine($"{globalVariable.Name} := {parameter.Name.Replace("var ", String.Empty, StringComparison.OrdinalIgnoreCase)};"); // TODO-SF: Think about introducing a property for byReference parameters
    }
    private void WriteConfigurationMethodBody(CodeMethod codeElement, LanguageWriter writer)
    {
        if (codeElement.Parameters.Any())
        {
            writer.WriteLine($"ReqConfig := config;");
            writer.WriteLine($"ConfigSet := true;");
        }
        else
        {
            writer.WriteLine($"if ConfigSet then");
            writer.IncreaseIndent();
            writer.WriteLine($"exit(ReqConfig);");
            writer.DecreaseIndent();
            writer.WriteLine($"ReqConfig := DefaultConfiguration();");
            writer.WriteLine($"exit(ReqConfig);");
        }
    }
    private void WriteDefaultConfigurationMethodBody(CodeMethod codeElement, LanguageWriter writer)
    {
        if (codeElement.Parameters.Any())
            throw new InvalidOperationException("DefaultConfiguration method should have no parameters");
        var returnType = codeElement.ReturnType as CodeType;
        if (returnType == null)
            throw new InvalidOperationException("DefaultConfiguration method should have a return type");
        writer.WriteLine($"ReqConfig.BaseURL(BaseUrlLbl);");
        writer.WriteLine($"ReqConfig.Client(this);");
        writer.WriteLine($"ReqConfig.Authorization(APIAuthorization);");
        writer.WriteLine($"exit(ReqConfig);");
    }
    private void WriteFromRequestBuilderSourceMethodBody(CodeMethod codeElement, CodeClass parentClass, LanguageWriter writer)
    {
        if (codeElement.Parameters.Any())
            throw new InvalidOperationException("FromRequestBuilderSource method should have no parameters");
        var returnType = codeElement.ReturnType as CodeType;
        if (returnType == null)
            throw new InvalidOperationException("FromRequestBuilderSource method should have a return type");
        if (parentClass.GetCustomProperty("client-class") == "true")
            writer.WriteLine($"{codeElement.GetCustomProperty("return-variable-name")}.SetConfiguration(Configuration());");
        else
            writer.WriteLine($"{codeElement.GetCustomProperty("return-variable-name")}.SetConfiguration(ReqConfig);");
    }
    private void WriteSetConfigurationMethodBody(CodeMethod codeElement, CodeClass parentClass, LanguageWriter writer)
    {
        if (codeElement.Name != "SetConfiguration")
            return;
        if (codeElement.Parameters.Count() != 1)
            throw new InvalidOperationException("SetConfiguration method should have one parameter");
        var parameter = codeElement.Parameters.First();
        if (parameter.Type is not CodeType parameterType)
            throw new InvalidOperationException("SetConfiguration method parameter should be a type");
        var globalVariable = parentClass.GlobalVariables().FirstOrDefault(p => p.Type.Name.Contains("Kiota ClientConfig", StringComparison.OrdinalIgnoreCase));
        if (globalVariable == null)
            throw new InvalidOperationException("SetConfiguration method should have a global variable");

        writer.WriteLine($"{globalVariable.Name} := {parameter.Name};");
        writer.WriteLine($"ReqConfig.AppendBaseURL('{parentClass.BaseUrlPartFromTemplate()}');");
    }
    private void WriteItemIdxMethodBody(CodeMethod codeElement, CodeClass parentClass, LanguageWriter writer)
    {
        if (codeElement.Name != "Item_Idx")
            return;
        if (codeElement.Parameters.Count() != 1)
            throw new InvalidOperationException("Item_Idx method should have one parameter");
        var parameter = codeElement.Parameters.First();
        if (parameter.Type is not CodeType)
            throw new InvalidOperationException("Item_Idx method parameter should be a type");
        var returnType = codeElement.ReturnType as CodeType;
        if (returnType == null)
            throw new InvalidOperationException("Item_Idx method should have a return type");
        writer.WriteLine($"Rqst.SetConfiguration(ReqConfig);");
        writer.WriteLine($"{codeElement.GetCustomProperty("return-variable-name")}.SetIdentifier({parameter.Name});");
    }
    private void WriteSetIdentifierMethodBody(CodeMethod codeElement, CodeClass parentClass, LanguageWriter writer)
    {
        if (codeElement.Name != "SetIdentifier")
            return;
        if (codeElement.Parameters.Count() != 1)
            throw new InvalidOperationException("SetIdentifier method should have one parameter");
        var parameter = codeElement.Parameters.First();
        if (parameter.Type is not CodeType parameterType)
            throw new InvalidOperationException("SetIdentifier method parameter should be a type");
        var globalVariable = parentClass.GlobalVariables().FirstOrDefault();
        if (globalVariable == null)
            throw new InvalidOperationException("SetIdentifier method should have a global variable");
        writer.WriteLine($"{globalVariable.Name} := {parameter.Name};");
        writer.WriteLine($"ReqConfig.AppendBaseURL(Format(Identifier));");
    }
    private void WriteApiClientInitializeMethodBody(CodeMethod codeElement, CodeClass parentClass, LanguageWriter writer)
    {
        if (parentClass.Name != "ApiClient")
            return;
        writer.WriteLine($"if (not NewAPIAuthorization.IsInitialized()) then");
        writer.IncreaseIndent();
        writer.WriteLine("Error(AuthorizationNotInitializedErr);");
        writer.DecreaseIndent();
        writer.WriteLine("APIAuthorization := NewAPIAuthorization;");
    }

    private void WriteFromPropertyGetterMethodBody(CodeMethod codeElement, LanguageWriter writer)
    {
        // Note: AL language allows omitting begin/end blocks for single statements, 
        // which creates intentionally inconsistent block structure throughout this method.
        // This is correct AL syntax and follows AL language conventions.
        
        if (codeElement.GetSourceType() == "List")
        {
            writer.WriteLine($"if not {conventions.ModelCodeunitJsonBodyVariableName}.SelectToken('{codeElement.Name}', SubToken) then");
            writer.IncreaseIndent();
            writer.WriteLine($"exit;");
            writer.DecreaseIndent();
            writer.WriteLine($"JArray := SubToken.AsArray();");
            if (conventions.IsCodeunitType(codeElement.ReturnType))
                writer.WriteLine($"foreach JToken in JArray do begin");
            else
                writer.WriteLine($"foreach JToken in JArray do begin");
            writer.IncreaseIndent();
            if (conventions.IsCodeunitType(codeElement.ReturnType))
            {
                writer.WriteLine($"Clear(TargetCodeunit);");
                writer.WriteLine($"TargetCodeunit.SetBody(JToken.AsObject(), DebugCall);");
                writer.WriteLine($"{codeElement.GetCustomProperty("return-variable-name")}.Add(TargetCodeunit);");
            }
            else
            {
                // Get the element type for proper JSON parsing
                var elementType = codeElement.ReturnType.AllTypes.FirstOrDefault();
                if (elementType != null && conventions.IsPrimitiveType(elementType))
                {
                    // Use type-aware JSON parsing for primitive types
                    var typeString = conventions.TranslateType(elementType);
                    var parseMethod = typeString switch
                    {
                        "BigInteger" => "AsBigInteger",
                        "Integer" => "AsInteger", 
                        "Boolean" => "AsBoolean",
                        "Decimal" => "AsDecimal",
                        "Date" => "AsDate",
                        "DateTime" => "AsDateTime", 
                        "Time" => "AsTime",
                        _ => "AsText" // Fallback to text for other types
                    };
                    writer.WriteLine($"{codeElement.GetCustomProperty("return-variable-name")}.Add(JToken.AsValue().{parseMethod}());");
                }
                else if (elementType != null && conventions.IsEnumType(elementType))
                {
                    // Handle enum conversion using Evaluate - much simpler approach
                    writer.WriteLine($"Evaluate(value, JToken.AsValue().AsText());");
                    writer.WriteLine($"{codeElement.GetCustomProperty("return-variable-name")}.Add(value);");
                }
                else
                {
                    // Fallback to text parsing for unknown types
                    writer.WriteLine($"{codeElement.GetCustomProperty("return-variable-name")}.Add(JToken.AsValue().AsText());");
                }
            }
            writer.DecreaseIndent();
            writer.WriteLine($"end;");
        }
        else if (codeElement.Documentation.DocumentationLabel.Contains("(Array)", StringComparison.OrdinalIgnoreCase))
        {

        }
        else if (codeElement.Documentation.DocumentationLabel.Contains("(Dictionary)", StringComparison.OrdinalIgnoreCase))
        {

        }
        else
        {
            if (conventions.IsPrimitiveType(codeElement.ReturnType))
            {
                writer.WriteLine($"if {conventions.ModelCodeunitJsonBodyVariableName}.SelectToken('{codeElement.Name}', SubToken) then"); // TODO-SF: Think about making this variable-based instead of hardcoded
                writer.IncreaseIndent();
                writer.WriteLine($"exit(SubToken.AsValue().As{conventions.GetTypeString(codeElement.ReturnType, codeElement)}());");
                writer.DecreaseIndent();
            }
            else
            {
                if (conventions.IsCodeunitType(codeElement.ReturnType))
                {
                    writer.WriteLine($"if {conventions.ModelCodeunitJsonBodyVariableName}.SelectToken('{codeElement.Name}', SubToken) then begin"); // TODO-SF: Think about making this variable-based instead of hardcoded
                    writer.IncreaseIndent();
                    writer.WriteLine($"TargetCodeunit.SetBody(SubToken.AsObject(), DebugCall);");
                    writer.WriteLine($"exit(TargetCodeunit);");
                    writer.DecreaseIndent();
                    writer.WriteLine("end;");
                }
                if (conventions.IsEnumType(codeElement.ReturnType))
                {
                    var enumName = codeElement.ReturnType.Name.ToFirstCharacterUpperCase();
                    writer.WriteLine($"if {conventions.ModelCodeunitJsonBodyVariableName}.SelectToken('{codeElement.Name}', SubToken) then begin"); // TODO-SF: Think about making this variable-based instead of hardcoded
                    writer.IncreaseIndent();
                    writer.WriteLine($"// Return value based on captions; needed because it's possible that the \"Names\" differ from the captions");
                    writer.WriteLine($"Ordinals := Enum::{enumName}.Ordinals();");
                    writer.WriteLine($"foreach Ordinal in Ordinals do begin");
                    writer.IncreaseIndent();
                    writer.WriteLine($"enumValue := Enum::{enumName}.FromInteger(Ordinal);");
                    writer.WriteLine($"if (Format(enumValue) = SubToken.AsValue().AsText()) then");
                    writer.IncreaseIndent();
                    writer.WriteLine($"exit(enumValue);");
                    writer.DecreaseIndent();
                    writer.DecreaseIndent();
                    writer.WriteLine($"end;");
                    writer.DecreaseIndent();
                    writer.WriteLine($"Error('Invalid value for {codeElement.Name}: %1', SubToken.AsValue().AsText());");
                    writer.WriteLine($"end;");
                }
            }
        }
    }
    private void WriteFromPropertySetterMethodBody(CodeMethod codeElement, LanguageWriter writer)
    {
        var param = codeElement.OrderedParameters().FirstOrDefault();
        if (param == null)
            throw new InvalidOperationException("Setter method should have a parameter");
        var targetName = param.Name;
        if (param.Type.CollectionKind == CodeTypeBase.CodeTypeCollectionKind.Complex)
        {
            writer.WriteLine($"foreach v in {param.Name} do");
            writer.IncreaseIndent();
            if (conventions.IsCodeunitType(param.Type))
                writer.WriteLine($"JSONHelper.AddToArrayIfNotEmpty(JArray, v);");
            else if (conventions.IsEnumType(param.Type))
                writer.WriteLine($"JArray.Add(Format(v));");
            else
                writer.WriteLine($"JArray.Add(v);");
            writer.DecreaseIndent();
            targetName = "JArray";
        }
        bool simpleAssignment = conventions.IsPrimitiveType(param.Type) || (targetName != param.Name);
        writer.WriteLine($"if {conventions.ModelCodeunitJsonBodyVariableName}.SelectToken('{codeElement.Name}', SubToken) then");
        writer.IncreaseIndent();
        writer.WriteLine($"{conventions.ModelCodeunitJsonBodyVariableName}.Replace('{codeElement.Name}', {(simpleAssignment ? targetName : conventions.IsEnumType(param.Type) ? $"Format({targetName})" : $"{targetName}.ToJson()")})");
        writer.DecreaseIndent();
        writer.WriteLine("else");
        writer.IncreaseIndent();
        writer.WriteLine($"{conventions.ModelCodeunitJsonBodyVariableName}.Add('{codeElement.Name}', {(simpleAssignment ? targetName : conventions.IsEnumType(param.Type) ? $"Format({targetName})" : $"{targetName}.ToJson()")});");
        writer.DecreaseIndent();
    }
    private void WriteValidateBodyMethodBody(CodeMethod codeElement, CodeClass parentClass, LanguageWriter writer)
    {
        if (codeElement.Name != "ValidateBody")
            return;
        var codeClass = codeElement.Parent as CodeClass;
        if (codeClass == null) throw new InvalidOperationException("ValidateBody method should be in a class");

        foreach (var local in GetPropertyGetterLocals(parentClass))
        {
            writer.WriteLine($"{local.Key} := {local.Key.ToFirstCharacterUpperCase()}();");
        }
        foreach (var local in GetPropertySetterLocals(parentClass))
        {
            writer.WriteLine($"{local.Key.ToFirstCharacterUpperCase()}({local.Key});");
        }
    }
    private Dictionary<string, CodeTypeBase> GetPropertyGetterLocals(CodeClass parentClass)
    {
        var locals = new Dictionary<string, CodeTypeBase>();
        foreach (var local in parentClass.GetPropertyGetterMethods())
        {
            locals.Add(local.Name, local.ReturnType);
        }
        return locals;
    }
    private Dictionary<string, CodeTypeBase> GetPropertySetterLocals(CodeClass parentClass)
    {
        var locals = new Dictionary<string, CodeTypeBase>();
        foreach (var local in parentClass.GetPropertySetterMethods())
        {
            locals.Add(local.Name, local.ReturnType);
        }
        return locals;
    }
    private void WriteToJsonMethodBody(CodeMethod codeElement, CodeClass parentClass, LanguageWriter writer)
    {
        switch (codeElement.Parameters.Count())
        {
            case 0: WriteToJsonMethodBodyForNoParameters(codeElement, writer); break;
            case > 0: WriteToJsonMethodBodyWithParameters(codeElement, writer); break;
            default: throw new InvalidOperationException("ToJson method should have one or two parameters");
        }
    }
    private void WriteToJsonMethodBodyForNoParameters(CodeMethod codeElement, LanguageWriter writer)
    {
        writer.WriteLine($"exit({conventions.ModelCodeunitJsonBodyVariableName});");
    }
    private void WriteToJsonMethodBodyWithParameters(CodeMethod codeElement, LanguageWriter writer)
    {
        foreach (var variable in codeElement.Parameters.Where(x => !x.IsLocalVariable()))
        {
            if (conventions.IsCodeunitType(variable.Type))
                if (variable.Type.IsCollection)
                {
                    var foreachVariable = variable.GetCustomProperty("foreach-variable");
                    var arrayName = variable.GetCustomProperty("corresponding-array");
                    writer.WriteLine($"foreach {foreachVariable} in {variable.Name} do");
                    writer.IncreaseIndent();
                    writer.WriteLine($"JSONHelper.AddToArrayIfNotEmpty({arrayName}, {foreachVariable});");
                    writer.DecreaseIndent();
                    writer.WriteLine($"JSONHelper.AddToObjectIfNotEmpty(TargetJson, '{variable.Name}', {arrayName});");
                }
                else
                    writer.WriteLine($"JSONHelper.AddToObjectIfNotEmpty(TargetJson, '{variable.Name}', {variable.Name}.ToJson().AsToken());");
            else
            {
                if (conventions.IsEnumType(variable.Type))
                    writer.WriteLine($"JSONHelper.AddToObjectIfNotEmpty(TargetJson, '{variable.Name}', Format({variable.Name}));");
                else
                    writer.WriteLine($"JSONHelper.AddToObjectIfNotEmpty(TargetJson, '{variable.Name}', {variable.Name});");
            }
        }
        writer.WriteLine($"exit(TargetJson);");
    }
    private void WriteSetBodyMethodBody(CodeMethod codeElement, LanguageWriter writer)
    {
        if (codeElement.Name == "GetFieldDeserializers")
            return; // skip for debugging, comes from union types
        switch (codeElement.Parameters.Count())
        {
            case 1: WriteSetBodyMethodBodyForSingleParameter(codeElement, writer); break;
            case 2: WriteSetBodyMethodBodyForTwoParameters(codeElement, writer); break;
            default: throw new InvalidOperationException("SetBody method should have one or two parameters");
        }
    }
    private void WriteSetBodyMethodBodyForSingleParameter(CodeMethod codeElement, LanguageWriter writer)
    {
        writer.WriteLine($"SetBody({GetJsonBodyParameter(codeElement).Name}, false);");
    }
    private void WriteSetBodyMethodBodyForTwoParameters(CodeMethod codeElement, LanguageWriter writer)
    {
        writer.WriteLine($"{conventions.ModelCodeunitJsonBodyVariableName} := {GetJsonBodyParameter(codeElement).Name};");
        writer.WriteLine($"if ({GetDebugParameter(codeElement).Name}) then begin");
        writer.IncreaseIndent();
        writer.WriteLine("#pragma warning disable AA0206");
        writer.WriteLine($"DebugCall := true;"); // TODO-SF: Change to const in convention service or get name from globals
        writer.WriteLine("#pragma warning restore AA0206");
        writer.WriteLine("ValidateBody();");
        writer.DecreaseIndent();
        writer.WriteLine("end;");
    }
    private CodeParameter GetDebugParameter(CodeMethod codeElement)
    {
        return codeElement.Parameters.FirstOrDefault(x => x.Name.Contains("debug", StringComparison.OrdinalIgnoreCase))!;
    }
    private CodeParameter GetJsonBodyParameter(CodeMethod codeElement)
    {
        return codeElement.Parameters.FirstOrDefault(x => x.Name.Contains(conventions.ModelCodeunitJsonBodyVariableName, StringComparison.OrdinalIgnoreCase))!;
    }
}
