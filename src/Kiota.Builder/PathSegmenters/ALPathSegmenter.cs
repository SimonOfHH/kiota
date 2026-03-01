using System;
using System.IO;
using Kiota.Builder.CodeDOM;
using Kiota.Builder.Extensions;

namespace Kiota.Builder.PathSegmenters;

public class ALPathSegmenter : CommonPathSegmenter
{
    public ALPathSegmenter(string rootPath, string clientNamespaceName) : base(rootPath, clientNamespaceName)
    {
    }

    public override string FileSuffix => ".al";

    public override string NormalizeNamespaceSegment(string segmentName) =>
        segmentName.ToFirstCharacterUpperCase();

    public override string NormalizeFileName(CodeElement currentElement)
    {
        if (currentElement is CodeClass)
            return $"{GetOriginalName(currentElement)}.Codeunit";
        if (currentElement is CodeEnum)
            return $"{GetOriginalName(currentElement)}.Enum";
        if (currentElement is CodeInterface)
            return $"{GetOriginalName(currentElement)}.Interface";
        if (currentElement is CodeFunction cf && cf.Name.Equals("AppJson", StringComparison.OrdinalIgnoreCase))
            return "app.json";
        return $"{GetLastFileNameSegment(currentElement)}.Function"; // TODO: review what this is for
    }

    public override string NormalizePath(string fullPath)
    {
        ArgumentNullException.ThrowIfNull(fullPath);
        if (fullPath.EndsWith("app.json.al", StringComparison.OrdinalIgnoreCase))
            return fullPath[..^3]; // Strip trailing ".al"

        // Truncate filename if total path exceeds limits
        if (fullPath.Length > 32767)
        {
            var dir = Path.GetDirectoryName(fullPath) ?? string.Empty;
            var ext = Path.GetExtension(fullPath);
            var fileName = Path.GetFileNameWithoutExtension(fullPath);
            if (fileName.Length > 64)
                fileName = fileName[..64];
            fullPath = Path.Combine(dir, fileName + ext);
        }
        return fullPath;
    }

    private static string GetOriginalName(CodeElement element)
    {
        if (element.CustomData.TryGetValue("original-name", out var originalName) && !string.IsNullOrEmpty(originalName))
            return originalName;
        return element.Name;
    }
}
