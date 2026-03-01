using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kiota.Builder.CodeDOM;

namespace Kiota.Builder.Refiners;

#pragma warning disable CA1056 // URI-like properties should not be strings
public class ALConfiguration
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
    [JsonPropertyName("objectPrefix")]
    public string ObjectPrefix { get; set; } = string.Empty;

    [JsonPropertyName("objectSuffix")]
    public string ObjectSuffix { get; set; } = string.Empty;

    [JsonPropertyName("appPublisherName")]
    public string AppPublisherName { get; set; } = "Default Publisher";

    [JsonPropertyName("appDescription")]
    public string AppDescription { get; set; } = "Auto-generated API Extension. Generated with Kiota";

    [JsonPropertyName("appBrief")]
    public string AppBrief { get; set; } = "Auto-generated API Extension";

    [JsonPropertyName("appVersion")]
    public string AppVersion { get; set; } = "0.0.0.1";

    [JsonPropertyName("objectIdRangeStart")]
    public int ObjectIdRangeStart { get; set; } = 50000;

    [JsonPropertyName("objectIdRangeEnd")]
    public int ObjectIdRangeEnd { get; set; } = 99999;

    [JsonPropertyName("companionNamespace")]
    public string CompanionNamespace { get; set; } = "Fps.Kiota";

    [JsonPropertyName("companionAppId")]
    public string CompanionAppId { get; set; } = "c24a2609-e5c2-4702-b734-db13e5a6594c";

    [JsonPropertyName("companionAppName")]
    public string CompanionAppName { get; set; } = "Kiota.Abstractions";

    [JsonPropertyName("companionPublisher")]
    public string CompanionPublisher { get; set; } = "SimonOfHH";

    [JsonPropertyName("companionAppVersion")]
    public string CompanionAppVersion { get; set; } = "1.0.0.0";

    [JsonPropertyName("privacyStatementUrl")]
    public string PrivacyStatementUrl { get; set; } = string.Empty;

    [JsonPropertyName("eulaUrl")]
    public string EulaUrl { get; set; } = string.Empty;

    [JsonPropertyName("helpUrl")]
    public string HelpUrl { get; set; } = string.Empty;

    [JsonPropertyName("appUrl")]
    public string AppUrl { get; set; } = string.Empty;

    // Computed companion references
    [JsonIgnore]
#pragma warning disable CA1721 // Property names should not match get methods
    public string ClientNamespace => $"{CompanionNamespace}.Client";
#pragma warning restore CA1721
    [JsonIgnore]
    public string DefinitionsNamespace => $"{CompanionNamespace}.Definitions";
    [JsonIgnore]
    public string UtilitiesNamespace => $"{CompanionNamespace}.Utilities";

    public CodeNamespace? GetClientNamespace(CodeNamespace root, string clientNamespaceName)
    {
        ArgumentNullException.ThrowIfNull(root);
        return root.FindNamespaceByName(clientNamespaceName);
    }

    public CodeNamespace? GetModelNamespace(CodeNamespace root, string clientNamespaceName)
    {
        ArgumentNullException.ThrowIfNull(root);
        return root.FindNamespaceByName($"{clientNamespaceName}.models");
    }

    public string GetBaseUrl(CodeNamespace root, string clientClassName)
    {
        ArgumentNullException.ThrowIfNull(root);
        var clientNs = root.FindNamespaceByName(root.Name);
        if (clientNs is null) return string.Empty;
        foreach (var element in root.GetChildElements(false))
        {
            if (element is CodeNamespace ns)
            {
                foreach (var child in ns.GetChildElements(true))
                {
                    if (child is CodeClass codeClass && codeClass.Name.Equals(clientClassName, StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var method in codeClass.Methods)
                        {
                            if (method.Kind == CodeMethodKind.ClientConstructor && !string.IsNullOrEmpty(method.BaseUrl))
                                return method.BaseUrl;
                        }
                    }
                }
            }
        }
        return string.Empty;
    }

    public static ALConfiguration LoadFromDisk(string outputPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(outputPath);
        var configPath = Path.Combine(Path.GetDirectoryName(outputPath) ?? outputPath, "al-config.json");
        if (!File.Exists(configPath))
        {
            // Also try in parent directory
            configPath = Path.Combine(Directory.GetParent(outputPath)?.FullName ?? outputPath, "al-config.json");
        }
        if (File.Exists(configPath))
        {
            try
            {
                var json = File.ReadAllText(configPath);
#pragma warning disable IL2026 // Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access
                return JsonSerializer.Deserialize<ALConfiguration>(json, s_jsonOptions) ?? new ALConfiguration();
#pragma warning restore IL2026
            }
            catch (JsonException)
            {
                return new ALConfiguration();
            }
        }
        return new ALConfiguration();
    }
}
