using System;
using Kiota.Builder.CodeDOM;

namespace Kiota.Builder.Writers.AL;

public class CodePropertyWriter : BaseElementWriter<CodeProperty, ALConventionService>
{
    public CodePropertyWriter(ALConventionService conventionService) : base(conventionService) { }

    public override void WriteCodeElement(CodeProperty codeElement, LanguageWriter writer)
    {
        ArgumentNullException.ThrowIfNull(codeElement);
        ArgumentNullException.ThrowIfNull(writer);
        if (codeElement.ParentIsSkipped())
            return;

        // Global variables are written by CodeClassDeclarationWriter
        if (codeElement.CustomData.ContainsKey("global-variable"))
            return;

        // Object properties are written by CodeClassDeclarationWriter
        if (codeElement.CustomData.ContainsKey("object-property"))
            return;

        // Locked properties were converted to methods by the refiner
        if (codeElement.CustomData.TryGetValue("locked", out var locked) &&
            locked.Equals("true", StringComparison.OrdinalIgnoreCase))
            return;

        // Skip any remaining properties — AL doesn't have member fields
        // They should all have been converted to getter/setter methods by the refiner
    }
}
