namespace Kiota.Builder.Writers.AL;

/// <summary>
/// Strongly-typed category of an AL-specific generated method. Replaces the former free-text
/// <c>ALCustomDataKeys.Source</c> string so the refiner (producer) and the writers/comparer
/// (consumers) share a single, compile-checked contract instead of magic strings.
/// AL's bespoke synthetic methods are modelled as <see cref="CodeDOM.CodeMethodKind.Custom"/>
/// carrying one of these categories rather than hijacking shared <c>CodeMethodKind</c> values.
/// Stored under <see cref="ALCustomDataKeys.MethodCategory"/>; adds no footprint to the shared CodeDOM.
/// </summary>
internal enum ALMethodCategory
{
    /// <summary>No AL-specific category (the method is dispatched purely by its <c>CodeMethodKind</c>).</summary>
    None = 0,

    // --- Property-derived methods ---
    /// <summary>Getter/setter generated from a model property.</summary>
    FromProperty,
    /// <summary>Request-builder getter generated from a navigation property.</summary>
    FromRequestBuilder,
    /// <summary>Item accessor generated from an indexer.</summary>
    FromIndexer,

    // --- Value-wrapper convenience overloads ---
    /// <summary>Convenience getter that unwraps a value-wrapper object.</summary>
    ValueWrapperGetter,
    /// <summary>Convenience setter that wraps a primitive into a value-wrapper object.</summary>
    ValueWrapperSetter,

    // --- Request executor overloads ---
    /// <summary>Multipart request-executor overload that builds the body from a stream.</summary>
    MultipartOverload,

    // --- Query parameters ---
    /// <summary>Generic query-parameter setter (key/value).</summary>
    QueryParamGenericSetter,
    /// <summary>Typed query-parameter setter for a specific parameter.</summary>
    QueryParamTypedSetter,
    /// <summary>Query-parameter getter returning the accumulated parameters.</summary>
    QueryParamGetter,

    // --- Stored response ---
    /// <summary>Getter returning the stored HTTP response.</summary>
    ResponseGetter,
    /// <summary>Setter storing the HTTP response.</summary>
    ResponseSetter,

    // --- Serialization helper ---
    /// <summary><c>ValidateBody</c> helper that round-trips every property to trigger validation.</summary>
    ValidateBody,

    // --- AL synthetic client / request-builder methods ---
    /// <summary>Client codeunit <c>Initialize</c> procedure.</summary>
    ClientInitialize,
    /// <summary>Client codeunit <c>Configuration</c> getter/setter procedures.</summary>
    ClientConfiguration,
    /// <summary>Client codeunit <c>DefaultConfiguration</c> factory procedure.</summary>
    ClientDefaultConfiguration,
    /// <summary>Request builder <c>SetConfiguration</c> procedure.</summary>
    RequestBuilderConfiguration,
    /// <summary>Request builder <c>SetIdentifier</c> procedure.</summary>
    RequestBuilderIdentifier,
    /// <summary>Request builder <c>SetConfigurationRaw</c> procedure.</summary>
    RequestBuilderRawConfiguration,
}
