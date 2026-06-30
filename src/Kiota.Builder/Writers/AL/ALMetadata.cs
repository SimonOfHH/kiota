using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Kiota.Builder.CodeDOM;

namespace Kiota.Builder.Writers.AL;

/// <summary>
/// Type-safe facade over <see cref="CodeElement.CustomData"/> for AL metadata.
/// Centralizes the stringly-typed get/set/flag/parse logic so that AL refiner and writer code
/// works against one validated contract keyed by <see cref="ALCustomDataKeys"/> instead of raw strings.
/// Lives entirely in the AL namespace and adds no footprint to the shared CodeDOM.
/// </summary>
internal static class ALMetadata
{
    /// <summary>Returns true when the metadata entry exists and equals the boolean-true marker (case-insensitive).</summary>
    public static bool GetFlag(this CodeElement element, string key)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.CustomData.TryGetValue(key, out var value) &&
               value.Equals(ALCustomDataKeys.Flags.True, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Sets a boolean metadata entry. Stores the literal "true"/"false" marker to preserve existing behavior.</summary>
    public static void SetFlag(this CodeElement element, string key, bool value = true)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.CustomData[key] = value ? ALCustomDataKeys.Flags.True : ALCustomDataKeys.Flags.False;
    }

    /// <summary>Returns true when the metadata entry is present (regardless of value).</summary>
    public static bool HasData(this CodeElement element, string key)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.CustomData.ContainsKey(key);
    }

    /// <summary>Gets a metadata value, or <c>null</c> when absent.</summary>
    public static string? GetData(this CodeElement element, string key)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.CustomData.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>Gets a metadata value, or <paramref name="defaultValue"/> when absent.</summary>
    public static string GetData(this CodeElement element, string key, string defaultValue)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.CustomData.TryGetValue(key, out var value) ? value : defaultValue;
    }

    /// <summary>Attempts to get a metadata value.</summary>
    public static bool TryGetData(this CodeElement element, string key, [NotNullWhen(true)] out string? value)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.CustomData.TryGetValue(key, out value);
    }

    /// <summary>Sets a metadata value.</summary>
    public static void SetData(this CodeElement element, string key, string value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.CustomData[key] = value;
    }

    /// <summary>Removes a metadata entry if present.</summary>
    public static void RemoveData(this CodeElement element, string key)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.CustomData.Remove(key);
    }

    /// <summary>Gets a metadata value parsed as an invariant integer, or <paramref name="defaultValue"/> when absent or invalid.</summary>
    public static int GetInt(this CodeElement element, string key, int defaultValue = 0)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.CustomData.TryGetValue(key, out var value) &&
               int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;
    }

    /// <summary>Returns true when the <see cref="ALCustomDataKeys.Source"/> entry equals the supplied value (case-insensitive).</summary>
    public static bool SourceIs(this CodeElement element, string source)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.CustomData.TryGetValue(ALCustomDataKeys.Source, out var value) &&
               value.Equals(source, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Appends a comma-separated token to a metadata entry (used for pragma accumulation), creating it when absent.</summary>
    public static void AppendCsv(this CodeElement element, string key, string token)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.CustomData[key] = element.CustomData.TryGetValue(key, out var existing) && !string.IsNullOrEmpty(existing)
            ? $"{existing},{token}"
            : token;
    }
}
