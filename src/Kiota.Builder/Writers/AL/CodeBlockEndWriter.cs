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
            func.Name.Equals("AppJson", StringComparison.OrdinalIgnoreCase))
        {
            // AppJson writer handles its own output
            return;
        }

        if (codeElement?.Parent is CodeClass parentClass &&
            parentClass.CustomData.TryGetValue("parameter-codeunit", out var val) &&
            val.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            // Parameter codeunit writer handles closing
            return;
        }

        writer.CloseBlock();
    }
}
