namespace Kiota.Builder.Writers.AL;

/// <summary>
/// Canonical names of the AL-specific metadata entries stored in <see cref="CodeDOM.CodeElement.CustomData"/>.
/// All AL refiner and writer code must reference these constants instead of magic strings so that the
/// producer (refiner) and consumers (writers, comparers, path segmenter) share a single, compile-checked contract.
/// This keeps the AL metadata self-contained and adds zero footprint to the shared Kiota CodeDOM.
/// </summary>
internal static class ALCustomDataKeys
{
    // --- Object / element identity ---
    /// <summary>Numeric AL object id (codeunit/enum) as an invariant string.</summary>
    public const string ObjectId = "object-id";
    /// <summary>Original (pre-sanitization) name, used for file naming and pragma decisions.</summary>
    public const string OriginalName = "original-name";
    /// <summary>Marks the generated API client codeunit.</summary>
    public const string ClientClass = "client-class";
    /// <summary>Marks an element that must not be emitted (e.g. nested/skipped classes or methods).</summary>
    public const string Skip = "skip";
    /// <summary>Marks an enum/class produced by de-duplication of structurally identical definitions.</summary>
    public const string Deduplicated = "deduplicated";
    /// <summary>Records the ancestor type a flattened (inherited) property originated from.</summary>
    public const string InheritedFrom = "inherited-from";

    // --- Pragmas ---
    /// <summary>Comma-separated AL warning codes to suppress around the object/member.</summary>
    public const string Pragmas = "pragmas";
    /// <summary>Comma-separated AL warning codes to suppress around variable declarations.</summary>
    public const string PragmasVariables = "pragmas-variables";
    /// <summary>Comma-separated AL warning codes to suppress around documentation comments.</summary>
    public const string DocumentationPragmas = "documentation-pragmas";

    // --- Variables / properties ---
    /// <summary>Marks a method parameter that represents an AL local variable rather than a real parameter.</summary>
    public const string LocalVariable = "local-variable";
    /// <summary>Marks a property that becomes an AL global variable.</summary>
    public const string GlobalVariable = "global-variable";
    /// <summary>Marks a property that becomes an AL object property (e.g. Extensible).</summary>
    public const string ObjectProperty = "object-property";
    /// <summary>Literal value carried by labels / object properties / enum option properties.</summary>
    public const string Value = "value";
    /// <summary>Marks a label/value as Locked for translation.</summary>
    public const string Locked = "locked";
    /// <summary>Marks a label whose Locked flag must be emitted.</summary>
    public const string LockedLabel = "locked-label";
    /// <summary>Marks a parameter that must be passed by reference (var).</summary>
    public const string ByRef = "by-ref";
    /// <summary>Marks a type that must be emitted as an AL Dictionary.</summary>
    public const string AlDictionary = "al-dictionary";

    // --- Methods ---
    /// <summary>Strongly-typed AL method category (see <see cref="ALMethodCategory"/>), stored as the enum name.</summary>
    public const string MethodCategory = "method-category";
    /// <summary>Backing collection kind of a generated method (see <see cref="SourceTypes"/>).</summary>
    public const string SourceType = "source-type";
    /// <summary>Stable ordering hint used by the AL order comparer.</summary>
    public const string SortingValue = "sorting-value";
    /// <summary>Explicit parameter ordering index (lower sorts first; absent means insertion order).</summary>
    public const string OrderIndex = "order-index";
    /// <summary>Explicit AL return variable name for a method.</summary>
    public const string ReturnVariableName = "return-variable-name";
    /// <summary>Original property name for a getter/setter or a renamed parameter.</summary>
    public const string PropertyName = "property-name";
    /// <summary>Wire/serialization name carried onto a getter/setter method.</summary>
    public const string SerializationName = "serialization-name";

    // --- Dictionary / collection helper variable names ---
    public const string KeyVariable = "key-variable";
    public const string ValueVariable = "value-variable";
    public const string ObjectVariable = "object-variable";
    public const string ForeachVariable = "foreach-variable";
    public const string CorrespondingArray = "corresponding-array";

    // --- Query parameters ---
    public const string QueryParamName = "query-param-name";
    public const string QueryParamTypeCategory = "query-param-type-category";

    // --- Value wrappers ---
    public const string WrapperGetterName = "wrapper-getter-name";
    public const string WrapperClassName = "wrapper-class-name";

    // --- Parameter codeunits ---
    public const string ParameterCodeunit = "parameter-codeunit";
    public const string UseParameterCodeunit = "use-parameter-codeunit";

    // --- Multipart ---
    public const string MultipartFieldName = "multipart-field-name";
    public const string MultipartFileFields = "multipart-file-fields";

    /// <summary>Boolean flag values stored as strings.</summary>
    public static class Flags
    {
        public const string True = "true";
        public const string False = "false";
    }

    /// <summary>Well-known values for <see cref="SourceType"/>.</summary>
    public static class SourceTypes
    {
        public const string Dictionary = "Dictionary";
        public const string List = "List";
    }

    /// <summary>
    /// AL CodeCop analyzer warning codes that the generator suppresses via <c>#pragma warning disable</c>
    /// (directly in the writers) or by accumulating them on <see cref="Pragmas"/>/<see cref="PragmasVariables"/>/
    /// <see cref="DocumentationPragmas"/>. Centralized here so the codes are not scattered as magic strings
    /// across the refiner and writers.
    /// </summary>
    public static class PragmaCodes
    {
        /// <summary>AA0137 — Do not declare variables that are unused.</summary>
        public const string UnusedVariable = "AA0137";
        /// <summary>AA0202 — Do not give local variables the same name as fields, methods, or actions in the same scope.</summary>
        public const string LocalVariableNameClash = "AA0202";
        /// <summary>AA0206 — The value assigned to a variable must be used.</summary>
        public const string UnusedAssignedValue = "AA0206";
        /// <summary>AA0215 — Follow the style guide naming best practices (object/file naming).</summary>
        public const string NamingConvention = "AA0215";
        /// <summary>AA0217 — Use a text constant or label for the format string in StrSubstNo.</summary>
        public const string StrSubstNoFormatLiteral = "AA0217";
        /// <summary>AA0245 — Do not give parameters the same name as fields, methods, or actions in the same scope.</summary>
        public const string ParameterNameClash = "AA0245";
    }
}
