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
        ALParameterCodeunitHelper.WriteParameterVariables(codeElement, writer);
    }

    private void WriteParameterMethods(CodeClass codeElement, ALWriter writer)
    {
        ALParameterCodeunitHelper.WriteParameterMethods(codeElement, writer);
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