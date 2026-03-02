using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Kiota.Builder.CodeDOM;

namespace Kiota.Builder.Writers.AL;

public class ALAppManifestWriter : BaseElementWriter<CodeFunction, ALConventionService>
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = new AppJsonNamingPolicy(),
    };

    /// <summary>
    /// CamelCase for all properties except "EULA" which the AL compiler requires uppercase.
    /// </summary>
    private sealed class AppJsonNamingPolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name) =>
            string.Equals(name, "EULA", StringComparison.Ordinal) ? name : CamelCase.ConvertName(name);
    }

    public ALAppManifestWriter(ALConventionService conventionService) : base(conventionService) { }

    public override void WriteCodeElement(CodeFunction codeElement, LanguageWriter writer)
    {
        ArgumentNullException.ThrowIfNull(codeElement);
        ArgumentNullException.ThrowIfNull(writer);

        if (!codeElement.Name.Equals("AppJson", StringComparison.OrdinalIgnoreCase))
            return;

        // Extract configuration from CodeUsing data carriers
        var config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var u in codeElement.Usings)
        {
            var eqIdx = u.Name.IndexOf('=', StringComparison.Ordinal);
            if (eqIdx > 0)
            {
                var key = u.Name[..eqIdx];
                var value = u.Name[(eqIdx + 1)..];
                config[key] = value;
            }
        }

        var name = GetConfigValue(config, "Name", "GeneratedClient");
        var publisher = GetConfigValue(config, "Publisher", "Default Publisher");
        var version = GetConfigValue(config, "Version", "0.0.0.1");
        var brief = GetConfigValue(config, "Brief", "Auto-generated API Extension");
        var description = GetConfigValue(config, "Description", "Auto-generated API Extension. Generated with Kiota");
        var idRangeStart = GetConfigValue(config, "IDRangeStart", "50000");
        var idRangeEnd = GetConfigValue(config, "IDRangeEnd", "99999");
        var companionAppId = GetConfigValue(config, "CompanionAppId", "c24a2609-e5c2-4702-b734-db13e5a6594c");
        var companionAppName = GetConfigValue(config, "CompanionAppName", "Kiota.Abstractions");
        var companionPublisher = GetConfigValue(config, "CompanionPublisher", "SimonOfHH");
        var companionAppVersion = GetConfigValue(config, "CompanionAppVersion", "1.0.0.0");
        var privacyUrl = GetConfigValue(config, "PrivacyStatementUrl", "https://www.providersample.com/privacy");
        var eulaUrl = GetConfigValue(config, "EulaUrl", "https://www.providersample.com/eula");
        var helpUrl = GetConfigValue(config, "HelpUrl", "https://www.providersample.com/help");
        var appUrl = GetConfigValue(config, "AppUrl", "https://www.providersample.com/app");

        var appJson = new
        {
            id = Guid.NewGuid().ToString("D"),
            name,
            publisher,
            version,
            brief,
            description,
            privacyStatement = privacyUrl,
            EULA = eulaUrl,
            help = helpUrl,
            url = appUrl,
            contextSensitiveHelpUrl = "https://www.providersample.com/contexthelp",
            logo = "",
            dependencies = new[]
            {
                new
                {
                    id = companionAppId,
                    name = companionAppName,
                    publisher = companionPublisher,
                    version = companionAppVersion,
                }
            },
            screenshots = Array.Empty<object>(),
            platform = "27.0.0.0",
            application = "27.0.0.0",
            idRanges = new[]
            {
                new
                {
                    from = int.TryParse(idRangeStart, out var rangeStart) ? rangeStart : 50000,
                    to = int.TryParse(idRangeEnd, out var rangeEnd) ? rangeEnd : 99999,
                }
            },
            runtime = "16.0",
            features = new[] { "NoImplicitWith" },
            target = "Cloud",
            supportedLocales = new[] { "en-US" },
            resourceExposurePolicy = new
            {
                allowDebugging = true,
                allowDownloadingSource = true,
                includeSourceInSymbolFile = false,
            },
            propagateDependencies = false,
        };

        var options = s_jsonOptions;

#pragma warning disable IL2026 // Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access
        var json = JsonSerializer.Serialize(appJson, options);
#pragma warning restore IL2026
        writer.Write(json, includeIndent: false);
    }

    private static string GetConfigValue(Dictionary<string, string> config, string key, string defaultValue)
    {
        return config.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : defaultValue;
    }
}
