using System;
using System.IO;
using System.Linq;
using Kiota.Builder.CodeDOM;
using Kiota.Builder.Configuration;

namespace Kiota.Builder.Refiners;

public static class ALConfigurationHelper
{
    internal static int GetPrefixAndSuffixLength(GenerationConfiguration configuration)
    {
        return (GetObjectPrefix(configuration)?.Length ?? 0) + (GetObjectSuffix(configuration)?.Length ?? 0);
    }
    internal static string GetObjectPrefix(GenerationConfiguration configuration)
    {
        return GetPropertyFromConfigurationFileString(configuration, "objectPrefix");
    }
    internal static string GetObjectSuffix(GenerationConfiguration configuration)
    {
        return GetPropertyFromConfigurationFileString(configuration, "objectSuffix");
    }
    internal static string GetAppPublisherName(GenerationConfiguration configuration)
    {
        return GetPropertyFromConfigurationFileString(configuration, "appPublisherName", "Default Publisher");
    }
    internal static string GetAppDescription(GenerationConfiguration configuration)
    {
        return GetPropertyFromConfigurationFileString(configuration, "appDescription", "Auto-generated API Extension. Generated with Kiota");
    }
    internal static string GetAppBrief(GenerationConfiguration configuration)
    {
        return GetPropertyFromConfigurationFileString(configuration, "appBrief", "Auto-generated API Extension");
    }
    internal static string GetAppVersion(GenerationConfiguration configuration)
    {
        return GetPropertyFromConfigurationFileString(configuration, "appVersion", "0.0.0.1");
    }
    internal static int GetObjectIdRangeStart(GenerationConfiguration configuration)
    {
        return GetPropertyFromConfigurationFileInt(configuration, "objectIdRangeStart");
    }
    internal static int GetObjectIdRangeEnd(GenerationConfiguration configuration)
    {
        return GetPropertyFromConfigurationFileInt(configuration, "objectIdRangeEnd");
    }
    private static int GetPropertyFromConfigurationFileInt(GenerationConfiguration configuration, string property)
    {
        var propertyValue = GetPropertyFromConfigurationFile(configuration, property);
        return propertyValue?.GetInt32() ?? 0;
    }
    private static string GetPropertyFromConfigurationFileString(GenerationConfiguration configuration, string property, string defaultValue = "")
    {
        var propertyValue = GetPropertyFromConfigurationFile(configuration, property);
        return propertyValue?.GetString() ?? defaultValue;
    }
    private static System.Text.Json.JsonElement? GetPropertyFromConfigurationFile(GenerationConfiguration configuration, string property)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var di = new DirectoryInfo(configuration.OutputPath);
        if (di.Parent == null || !di.Parent.Exists)
            return null;
        string configFilePath = Path.Combine(di.Parent.FullName, "al-config.json");
        // We check if there is a JSON config file at configuration.OutputPath\al-config.json
        if (!File.Exists(configFilePath))
            return null;

        // If the file exists, we read it and look for the specified property
        var alConfig = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(configFilePath)));
        if (alConfig.RootElement.TryGetProperty(property, out var desiredProperty))
            return desiredProperty;
        return null;
    }
    internal static CodeNamespace GetClientNamespace(CodeElement currentElement, GenerationConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var root = GetRootNamespace(currentElement);
        var clientNamespace = root.FindNamespaceByName($"{configuration.ClientNamespaceName}");
        ArgumentNullException.ThrowIfNull(clientNamespace);
        return clientNamespace;
    }
    internal static CodeNamespace GetModelNamespace(CodeElement currentElement, GenerationConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var root = GetRootNamespace(currentElement);
        var modelNamespace = root.FindNamespaceByName($"{configuration.ClientNamespaceName}.{GenerationConfiguration.ModelsNamespaceSegmentName}");
        ArgumentNullException.ThrowIfNull(modelNamespace);
        return modelNamespace;
    }
    internal static CodeNamespace GetRootNamespace(CodeElement currentElement)
    {
        if (currentElement is CodeNamespace currentNamespace)
        {
            var root = currentNamespace.GetRootNamespace();
            return root;
        }
        else
        {
            var ns = currentElement.GetImmediateParentOfType<CodeNamespace>();
            return GetRootNamespace(ns);
        }
    }
    internal static string? GetBaseUrl(CodeElement element, GenerationConfiguration configuration)
    {
        return element.GetImmediateParentOfType<CodeNamespace>()
                      .GetRootNamespace()?
                      .FindChildByName<CodeClass>(configuration.ClientClassName)?
                      .Methods?
                      .FirstOrDefault(static x => x.IsOfKind(CodeMethodKind.ClientConstructor))?
                      .BaseUrl;
    }
}