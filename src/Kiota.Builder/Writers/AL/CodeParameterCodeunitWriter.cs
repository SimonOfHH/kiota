using System;
using System.Linq;
using Kiota.Builder.CodeDOM;
using Kiota.Builder.Extensions;

namespace Kiota.Builder.Writers.AL;

public class CodeParameterCodeunitWriter : BaseElementWriter<ClassDeclaration, ALConventionService>
{
    public CodeParameterCodeunitWriter(ALConventionService conventionService) : base(conventionService) { }

    public override void WriteCodeElement(ClassDeclaration codeElement, LanguageWriter writer)
    {
        var alWriter = writer as ALWriter;
        ArgumentNullException.ThrowIfNull(codeElement);
        ArgumentNullException.ThrowIfNull(alWriter);
        
        if (codeElement.Parent is not CodeClass parentClass) 
            return;
            
        // Only handle parameter codeunits
        if (parentClass.GetCustomProperty("parameter-codeunit") != "true") return;
        
        var parentNamespace = parentClass.GetImmediateParentOfType<CodeNamespace>();
        if (parentNamespace == null) return;

        // Write codeunit declaration
        alWriter.WriteLine(CodeClassDeclarationWriter.AutoGenerationHeader);
        alWriter.WriteLine($"namespace {parentNamespace.Name};");
        alWriter.WriteLine();

        // Write usings if any
        codeElement.Usings
            .Where(x => (x.Declaration?.IsExternal ?? true) || !x.Declaration.Name.Equals(codeElement.Name, StringComparison.OrdinalIgnoreCase))
            .Select(static x => x.Declaration?.IsExternal ?? false ?
                            $"using {x.Declaration.Name.NormalizeNameSpaceName(".")};" :
                            $"using {x.Name.NormalizeNameSpaceName(".")};")
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static x => x, StringComparer.Ordinal)
            .ToList()
            .ForEach(x => alWriter.WriteLine(x));

        if (codeElement.Usings.Any())
            alWriter.WriteLine();

        // Write codeunit header
        alWriter.WriteLine($"codeunit {alWriter.ObjectIdProvider.GetNextCodeunitId()} \"{codeElement.Name}\"");
        alWriter.StartBlock();

        WriteParameterVariables(parentClass, alWriter);
        WriteParameterMethods(parentClass, alWriter);

        alWriter.CloseBlock();
    }

    private void WriteParameterVariables(CodeClass codeElement, ALWriter writer)
    {
        // Get query parameters from the stored method info
        var queryParametersJson = codeElement.GetCustomProperty("query-parameters");
        if (string.IsNullOrEmpty(queryParametersJson)) 
        {
            // Write a comment to help debug if no parameters found
            writer.WriteLine("// No query parameters found");
            return;
        }

        // Parse the parameter string containing parameter information
        // Format: "param1:Type1,param2:Type2,..."
        var parameters = queryParametersJson.Split(',');
        
        writer.WriteLine("var");
        writer.IncreaseIndent();

        foreach (var paramInfo in parameters)
        {
            var parts = paramInfo.Split(':');
            if (parts.Length != 2) continue;
            
            var paramName = parts[0].Trim();
            var paramType = parts[1].Trim();
            
            writer.WriteLine($"{paramName}: {paramType};");
            writer.WriteLine($"Has{paramName}: Boolean;");
        }

        writer.DecreaseIndent();
        writer.WriteLine();
    }

    private void WriteParameterMethods(CodeClass codeElement, ALWriter writer)
    {
        // Get query parameters from the stored method info
        var queryParametersJson = codeElement.GetCustomProperty("query-parameters");
        if (string.IsNullOrEmpty(queryParametersJson)) return;

        var parameters = queryParametersJson.Split(',');

        foreach (var paramInfo in parameters)
        {
            var parts = paramInfo.Split(':');
            if (parts.Length != 2) continue;
            
            var paramName = parts[0].Trim();
            var paramType = parts[1].Trim();
            
            // Write setter method
            writer.WriteLine($"procedure Set{paramName}(Value: {paramType})");
            writer.WriteLine("begin");
            writer.IncreaseIndent();
            writer.WriteLine($"{paramName} := Value;");
            writer.WriteLine($"Has{paramName} := true;");
            writer.DecreaseIndent();
            writer.WriteLine("end;");
            writer.WriteLine();

            // Write getter method (internal)
            writer.WriteLine($"internal procedure Get{paramName}() ReturnValue: {paramType}");
            writer.WriteLine("begin");
            writer.IncreaseIndent();
            writer.WriteLine($"exit({paramName});");
            writer.DecreaseIndent();
            writer.WriteLine("end;");
            writer.WriteLine();

            // Write "IsSet" method (internal)
            writer.WriteLine($"internal procedure Is{paramName}Set(): Boolean");
            writer.WriteLine("begin");
            writer.IncreaseIndent();
            writer.WriteLine($"exit(Has{paramName});");
            writer.DecreaseIndent();
            writer.WriteLine("end;");
            writer.WriteLine();
        }
    }

    public static CodeClass CreateParameterCodeunit(string className, string methodName, CodeParameter[] queryParameters, CodeNamespace parentNamespace)
    {
        ArgumentNullException.ThrowIfNull(parentNamespace);
        ArgumentNullException.ThrowIfNull(queryParameters);
        
        var parameterCodeunitName = $"{className}{methodName}Parameters";
        var parameterCodeunit = new CodeClass
        {
            Name = parameterCodeunitName,
            Kind = CodeClassKind.Custom,
            Parent = parentNamespace
        };

        // Mark as parameter codeunit
        parameterCodeunit.AddCustomProperty("parameter-codeunit", "true");

        // Store parameter information for the writer
        var conventionService = new ALConventionService();
        var paramInfos = queryParameters.Select(p => 
        {
            var typeString = conventionService.GetTypeString(p.Type, null);
            return $"{p.Name.ToFirstCharacterUpperCase()}:{typeString}";
        });
        
        parameterCodeunit.AddCustomProperty("query-parameters", string.Join(",", paramInfos));

        // Add usings from parent class
        var parentClass = parentNamespace.Classes.FirstOrDefault(c => c.Name == className);
        if (parentClass != null)
        {
            foreach (var using_ in parentClass.Usings)
            {
                parameterCodeunit.AddUsing(using_);
            }
        }

        return parameterCodeunit;
    }
}