using Kiota.Builder.PathSegmenters;
using Kiota.Builder.Refiners;

namespace Kiota.Builder.Writers.AL;

public class ALWriter : LanguageWriter
{
    public ALWriter(string rootPath, string clientNamespaceName)
    {
        PathSegmenter = new ALPathSegmenter(rootPath, clientNamespaceName);
        var conventionService = new ALConventionService(
            ALConfiguration.LoadFromDisk(rootPath));
        AddOrReplaceCodeElementWriter(new CodeClassDeclarationWriter(conventionService));
        AddOrReplaceCodeElementWriter(new CodeBlockEndWriter());
        AddOrReplaceCodeElementWriter(new CodeEnumWriter(conventionService));
        AddOrReplaceCodeElementWriter(new ALAppManifestWriter(conventionService));
        AddOrReplaceCodeElementWriter(new CodeMethodWriter(conventionService));
        AddOrReplaceCodeElementWriter(new CodePropertyWriter(conventionService));
        AddOrReplaceCodeElementWriter(new CodeInterfaceDeclarationWriter(conventionService));
    }
}
