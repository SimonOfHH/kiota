using System;
using Kiota.Builder.CodeDOM;

namespace Kiota.Builder.Writers.AL;

public class CodeBlockEndWriter : ICodeElementWriter<BlockEnd>
{
    public void WriteCodeElement(BlockEnd codeElement, LanguageWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (codeElement.ParentIsSkipped())
            return;
        if (codeElement?.Parent is CodeFunction func &&
            (func.Name.Equals("AppJson", StringComparison.OrdinalIgnoreCase) ||
             func.Name.Equals("Readme", StringComparison.OrdinalIgnoreCase)))
        {
            // These writers handle their own output — no closing block needed
            return;
        }

        writer.CloseBlock();
    }
}
