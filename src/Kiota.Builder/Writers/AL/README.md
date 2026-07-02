# AL (Business Central) Language Support

This folder contains the writer implementation that turns Kiota's generated
CodeDOM into Microsoft Dynamics 365 Business Central AL source code (a `.app`
extension). The companion refiner lives in
[`../../Refiners/ALRefiner.cs`](../../Refiners/ALRefiner.cs) and the AL-specific
configuration model in
[`../../Refiners/ALConfiguration.cs`](../../Refiners/ALConfiguration.cs).

This document explains **how the AL target differs from the "normal" Kiota
languages** (CSharp, TypeScript, Go, ...), **why** those differences exist, and
**what is still open for discussion**.

---

## 1. Why AL is not "just another language"

Every other Kiota target (CSharp, Java, TypeScript, Go, PHP, Python, Ruby, ...)
generates a client library that is compiled/interpreted by a general-purpose
runtime and consumed by arbitrary application code. AL is different in a way
that affects the generator itself:

- **AL only exists inside Business Central (BC).** There is no standalone AL
  runtime, no NuGet/npm-style package manager, and no way to `dotnet run` the
  output. Generated code must be packaged as a BC **extension (`.app`)** and
  installed into a BC sandbox/tenant to be exercised at all.
- **BC is an ERP system, not a general application host.** The AL language and
  the BC platform impose restrictions that don't exist for the other targets
  (see §2).
- **AL has no client-side package ecosystem.** Where the CSharp/TypeScript/...
  writers can `import`/`using` a shared abstractions package from a registry,
  AL extensions depend on other AL extensions being installed side-by-side in
  the same tenant. Kiota's generated AL code therefore depends on a small
  hand-written **companion app** instead of a NuGet/npm package (see §3).
- **Object identity is numeric and tenant-scoped.** Every AL object
  (codeunit, enum, page, table, ...) needs a unique integer ID that must not
  collide with IDs used by any other extension installed in the same tenant.
  This has no equivalent in the other languages and requires its own
  configuration (see §4).

These constraints are why the AL refiner/writer pair carries more
special-casing than e.g. the CSharp implementation, and why some north-star
clean-ups (see the linked alignment plan) are still in progress.

---

## 2. Business Central as a target platform — technical restrictions

Business Central is an ERP system first; AL is the extensibility language
bolted onto it. That has concrete consequences for generated code:

| Restriction | Consequence for the generator |
|---|---|
| **30-character object name limit.** All AL object names (and most identifiers) are capped at 30 characters. | Generated type/method names are aggressively abbreviated and truncated; an abbreviation dictionary and configurable prefix/suffix (see §4) are used to keep names both unique and within the limit. |
| **No implementation inheritance.** AL has interfaces but no class inheritance. | Inherited model properties are flattened onto each generated table/complex-type instead of being expressed via a base type. |
| **No public fields/auto-properties.** Data must go through table fields or codeunit variables, exposed via explicit getter/setter procedures. | Every Kiota model "property" becomes a pair of generated procedures (`Get<X>`/`Set<X>`) instead of a language-level property. |
| **Everything lives in an object, not a free function/module.** AL code must be a member of a `codeunit`, `page`, `table`, etc. | Kiota's request builders/model classes map to `codeunit`s; there is no equivalent of a free-standing module or static class. |
| **Object IDs are global per tenant, not per extension.** Two independently installed extensions must never reuse the same object ID. | Generated apps need a configurable, non-overlapping ID range per customer/environment (see §4) — this is unique to AL among all Kiota targets. |
| **No first-class HTTP/JSON standard library at the call site.** BC exposes HTTP and JSON via specific system codeunits (`Http Client`, `Json Object`, ...) with their own idioms (e.g. call-by-reference `[TryFunction]`s instead of exceptions/results). | Generated request execution, (de)serialization, and error handling are written against those system codeunits rather than idiomatic exceptions/futures/promises used elsewhere. |
| **Compilation requires the BC symbol packages for the target version.** There's no "just run `tsc`/`javac`". | End-to-end validation of generated AL requires an AL compiler (`alc`) and downloaded symbols for a specific BC version; this is heavier than the other languages' `it/<lang>` harnesses (tracked as future work). |

None of the above are bugs in the generator — they are the reason the AL
writer/refiner necessarily deviates from the CSharp/TypeScript/Go pattern in
places. Some of these deviations are unavoidable consequences of the platform
(listed above); others are implementation choices in the current refiner/writer
that are still being iterated on (see §5).

---

## 3. The companion app (`Kiota.Abstractions-al`)

Unlike the .NET/TypeScript/Java targets — which pull a published
runtime/abstractions **package** (e.g. `Microsoft.Kiota.Abstractions`) — AL has
no package manager. Instead, a small, hand-written, separately versioned BC
**extension** provides the shared runtime pieces every generated AL client
needs:

- Repository (today): **<https://github.com/SimonOfHH/Kiota.Abstractions-al>**
- Role: functionally equivalent to the `Microsoft.Kiota.Abstractions` /
  `@microsoft/kiota-abstractions` packages — it supplies request
  adapters/executors, authentication provider interfaces, serialization
  helpers, and other cross-cutting building blocks that would otherwise have
  to be regenerated (and duplicated) into every single generated app.
- Every generated AL app declares a **dependency** on this companion app in
  its `app.json` (publisher/app id/version — configurable, see §4) instead of
  vendoring the shared code.
- Because it lives in a separate repository/app, it can be versioned and
  updated independently of any specific generated client, similar to how the
  .NET abstractions package is versioned independently of any specific
  generated SDK.

This split (generated app + shared companion app) mirrors the "generated
client depends on a shared abstractions package" pattern from the other
languages as closely as AL's lack of a package manager allows.

---

## 4. Per-project configuration (`al-config.json`)

The other language writers take all their configuration from Kiota's regular
CLI options / `GenerationConfiguration`. AL needs **additional**, AL-specific
settings that have no equivalent elsewhere (ID ranges, naming affixes,
companion app coordinates, manifest metadata). These are read from an optional
`al-config.json` file placed next to (or in a parent of) the generation output
path — see `ALConfiguration.LoadFromDisk` in
[`../../Refiners/ALConfiguration.cs`](../../Refiners/ALConfiguration.cs).

If the file is missing, generation proceeds with built-in defaults so the
feature is fully opt-in.

| Key | Purpose | Default |
|---|---|---|
| `objectPrefix` / `objectSuffix` | Prepended/appended to generated object names to avoid collisions between multiple generated apps and to fit BC naming conventions (partner/customer prefixing is common practice in AL). Counted against the 30-character limit. | `""` / `""` |
| `objectIdRangeStart` / `objectIdRangeEnd` | The inclusive integer range the generator allocates codeunit/enum/page/table/report/xmlport/query IDs from (`ALObjectIdProvider`). Must be a range the target tenant has reserved for this extension. Generation fails fast (`InvalidOperationException`) if the range is exhausted or invalid. | `50000` – `99999` |
| `appPublisherName`, `appDescription`, `appBrief`, `appVersion` | Populate the generated `app.json` manifest for the generated client app itself. | placeholders |
| `companionNamespace`, `companionAppId`, `companionAppName`, `companionPublisher`, `companionAppVersion` | Identify the companion app (§3) that the generated `app.json` declares as a dependency, and the AL `namespace` prefix used to reference its shared types. | points at `Kiota.Abstractions-al` |
| `privacyStatementUrl`, `eulaUrl`, `helpUrl`, `appUrl` | Passed straight through to the generated `app.json`. | `""` |
| `generateInterfaces` | Whether AL interfaces are generated for inheritance-like relationships (AL has no class inheritance, see §2). | `false` |
| `markInternal` | Whether generated objects/procedures are marked `Access = Internal` / `Internal` rather than public. | `false` |
| `objectMapPath` | Path (relative to `al-config.json`'s own directory, or absolute) to a persisted object-id/name map used to keep object IDs and names stable across regenerations of an **evolving** spec, not just an unchanged one — see §4a. Feature is fully opt-in; omit/leave empty to disable. | `null` (disabled) |

`ALConfiguration.Validate()` rejects an invalid ID range, a malformed
`companionAppId` (must be a GUID), or malformed version strings before
generation proceeds, so a broken config fails fast instead of producing
uncompilable AL.

### 4a. Keeping object IDs and names stable across spec changes (`objectMapPath`)

Even with deterministic CodeDOM traversal (see §5's ordering note), IDs and
names minted **within a single generation run** are only stable for an
**unchanged** spec — adding, removing, or reordering schemas/operations still
shifts which class/enum gets which ID or name on the next run, because both
are assigned by walking the tree at generation time. `objectMapPath` closes
that gap by persisting previously-assigned IDs/names to a small JSON file
(`ALObjectMap`, see
[`../../Refiners/ALObjectMap.cs`](../../Refiners/ALObjectMap.cs)) and reusing
them on the next run whenever the *same* object is seen again.

- **Stable identity key:** each object is keyed as
  `"{Namespace.Name}::{OriginalName}"` — the AL namespace it lives in plus its
  name *before* prefix/suffix/dedup/abbreviation were applied
  (`ALCustomDataKeys.OriginalName`). This key is derived from Kiota-core's own
  (already deterministic) schema/path naming, not from CodeDOM traversal order
  or the final, possibly-renamed AL identifier.
- **On a hit** (key found in the map and not tombstoned): the previously
  assigned `objectId`/`assignedName` are reused verbatim — no re-minting, no
  re-sanitization.
- **On a miss:** a new ID/name is minted as usual (existing prefix/suffix,
  sanitize/deduplicate, and ID-range logic all still apply), and a new entry
  is added to the map.
- **Renames are treated as a new object.** Because the key is derived from the
  *original*, pre-rename schema name, renaming a schema in the source spec
  produces a cache miss (new key) rather than reusing the old entry — this is
  intentional: from BC's perspective a rename and an add+remove are
  indistinguishable, and silently reassigning an existing object's identity to
  semantically different content would be worse than minting a new one.
- **Removed objects are tombstoned, never reused.** If a previously-mapped
  key is not seen during a generation run, its entry is kept in the map with
  `tombstoned: true`. Its `objectId`/`assignedName` stay reserved *forever* —
  they are never handed to a different, unrelated object on a later run. This
  mirrors AppSource's breaking-change/obsoletion rules, where reusing a
  retired object ID or name for something else is not allowed.
- **Treat the map like a lock file:** it is machine-maintained, rewritten in
  full on every generation run that has `objectMapPath` configured, and not
  meant to be hand-edited. Check it into source control alongside the
  generated output so IDs/names stay stable across machines and CI runs.
- **Known limitation:** concurrent regenerations against the same map file
  (e.g. two branches regenerating in parallel) can produce merge conflicts in
  the map JSON, the same way `kiota-lock.json` or a package-manager lock file
  can. Resolving that is left to the consuming pipeline (e.g. serialize
  regeneration, or regenerate on a single branch and merge the result) rather
  than solved by the generator itself.

### Open for discussion

The `al-config.json` side-file approach is a **pragmatic first cut**, not a
final design. Known trade-offs / alternatives worth revisiting:

- **Discoverability:** the file is optional and looked up relative to the
  output path with a parent-directory fallback; this is easy to misconfigure
  compared to an explicit `--al-config <path>` CLI flag or first-class fields
  on Kiota's own configuration/lock file.
- **Overlap with `kiota-lock.json`:** Kiota already persists generation
  settings per client in `kiota-lock.json`. Folding AL-specific settings into
  (or alongside) that file — instead of a bespoke `al-config.json` — would
  keep configuration in one place and get incremental-update tracking "for
  free".
- **ID range ownership:** the range itself is still a flat `start`/`end` pair
  with no notion of per-tenant reservation bookkeeping beyond what's in the
  object map. A persisted per-object-name ID map to keep IDs stable across
  regenerations of an *evolving* spec is now available via `objectMapPath`
  (see §4a); what's still open is whether/how that map should also track
  range exhaustion or per-environment overrides.
Feedback and proposals for a better shape are welcome before this surface is
considered stable.

---

## 5. `CustomData` usage and method dispatch design

The other language writers (CSharp, TypeScript, Go, ...) drive almost all
behavior from the strongly-typed CodeDOM: `CodeMethodKind`, `CodePropertyKind`,
`Access`, `IsStatic`, `IsOverload`, `OriginalMethod`, `AccessedProperty`, and
real `CodeType`s. Their writers dispatch with a single `switch` on
`method.Kind`, and `CodeElement.CustomData` is essentially unused.

The AL writer/refiner cannot fully follow that pattern yet, because AL needs
concepts the shared CodeDOM has no first-class representation for — an
object's numeric ID, accumulated compiler-pragma suppressions, and a handful of
AL-only synthetic procedures (client `Initialize`/`Configuration`, request
builder identifiers/raw-configuration, query-parameter get/set, response
get/set, multipart-body overloads, ...). Two mechanisms carry that extra
information today:

- **`ALCustomDataKeys` / `CodeElement.CustomData`** — a small, per-element
  string-keyed dictionary (accessed through typed helper extensions in
  `ALMetadata.cs`: `GetData`/`SetData`/`GetFlag`/`SetFlag`/`GetInt`/`HasData`/
  `TryGetData`/`RemoveData`). It is intentionally used only for **data that has
  no home in the shared CodeDOM**: the allocated AL object ID, accumulated
  pragma codes, original/abbreviated name bookkeeping, parameter/global
  ordering indices, and similar bookkeeping. This is a deliberate trade-off —
  keeping AL-only concerns out of the shared CodeDOM rather than adding
  AL-specific fields that every other language's CodeDOM consumer would have
  to ignore.
- **`ALMethodCategory`** (an internal enum in `ALMethodCategory.cs`) —
  identifies which *kind* of AL-synthetic procedure a `CodeMethodKind.Custom`
  method represents (e.g. `ClientInitialize`, `RequestBuilderIdentifier`,
  `ResponseGetter`, `MultipartOverload`, ...). `CodeMethodWriter.WriteMethodBody`
  switches on `method.Kind` first, exactly like the other languages; only
  when `Kind == Custom` does it fall through to a second switch on the
  element's `ALMethodCategory` to pick the right synthetic body. Because the
  category is a typed, closed enum, the full set of AL-synthetic method
  variants is enumerable and each case is checked by the compiler.

Other behavioral flags follow the same principle of using the typed CodeDOM
wherever it already has a home: getter/setter detection relies on
`CodeMethodKind.Getter`/`Setter`, overloads use the built-in
`CodeMethod.OriginalMethod`/`IsOverload`, and parameter/member ordering uses a
dedicated ordering key rather than repurposing `DefaultValue`. The result is
that `CustomData` on AL elements is data-only bookkeeping; behavioral routing
goes through `method.Kind` + `ALMethodCategory`, matching the spirit (if not
yet the letter) of how the other language writers dispatch.

---

## 6. Bug fix note (temporary — remove once merged): dictionary value types reachable only via `additionalProperties` were silently dropped

**Status:** fixed 2026-07-01, tracked here temporarily so it isn't lost before being folded into the PR description.

**Symptom:** for a schema like:

```jsonc
"ProjectInformationDto": {
  "properties": {
    "priceComponentTypes": {
      "type": "object",
      "additionalProperties": { "$ref": "#/definitions/PriceComponentTypeDto" }
    }
  }
}
```

AL used to generate `priceComponentTypes: Dictionary of [Text, Enum "PriceComponentTypeDto"]`
plus a standalone `PriceComponentTypeDto.Enum.al`. At some point this regressed to an empty
wrapper codeunit (`ProjectInformationDto_priceComponentTypes.Codeunit.al`) with no dictionary
and no enum file at all.

**Important: this was not an AL-only bug.** Regenerating the same spec for CSharp showed the
exact same problem (a `ProjectInformationDto_priceComponentTypes.cs` wrapper class with no
standalone `PriceComponentTypeDto.cs`). The regression lived entirely in the shared,
language-agnostic core (`KiotaBuilder.cs`), not in the AL writer/refiner. AL was simply the
only target that ever tried to *use* the missing type information (via its pure-dictionary
conversion in `ALRefiner.CollectPureDictionaryClasses`/`ConvertPureDictionaryProperties`), which
is why it was the one place the regression became visible as broken output.

**Root cause (two parts, both in `KiotaBuilder.cs`):**

1. When a property's schema is `type: object` with a *referenced* `additionalProperties` schema
   (e.g. an enum or class), nothing in the pipeline ever visited that referenced schema to create
   its own CodeDOM declaration. The generic `AdditionalData` "extensibility bag" property added by
   `AddSerializationMembers` is — by design — always untyped (`IDictionary<string, object>`), so it
   can never be used to recover the value type. The referenced type (`PriceComponentTypeDto`) was
   therefore never generated at all, for any language.
2. Even after fixing (1) to explicitly create the declaration, `TrimInheritedModels` (the pass that
   removes CodeDOM models not reachable from any real request/response type) immediately deleted it
   again as "unused" — because nothing in the CodeDOM held an actual `CodeType` reference to it, only
   a side-channel `CustomData` tag.

**Fix:**

- `KiotaBuilder.AddModelClass`: when `schema.AdditionalProperties` is a referenced schema, tag the
  generated wrapper class with `CustomData["AdditionalPropertiesValueTypeName"]` and explicitly call
  `AddModelDeclarationIfDoesntExist(...)` on that referenced schema so its declaration actually gets
  created.
- `KiotaBuilder.TrimInheritedModels`: after computing the set of models kept alive
  (`relatedModels`/`classesInUse`), do a second pass that looks for classes carrying the
  `AdditionalPropertiesValueTypeName` tag and keeps their tagged value type alive too, even though no
  `CodeType` points to it directly. (Gotcha hit while fixing this: the source LINQ query must be
  materialized with `.ToArray()` before the loop body mutates the `relatedModels` hash set, otherwise
  you get an `InvalidOperationException` from the lazily-enumerated `Union`.)
- `ALRefiner.CollectPureDictionaryClasses`: now reads `CustomData["AdditionalPropertiesValueTypeName"]`
  first, falling back to the old (always-broken) `AdditionalData` property type lookup for safety.

**Regression test:** `KeepsEnumReferencedOnlyThroughAdditionalPropertiesSchema` in
`tests/Kiota.Builder.Tests/KiotaBuilderTests.cs` — deliberately placed in the core builder test file
(not an AL-specific test file) since the bug and fix are both in shared, language-agnostic code.

**Verification:** regenerated the real-world spec end-to-end after the fix;
`priceComponentTypes` now renders as `Dictionary of [Text, Enum "PriceComponentTypeDto"]` again, and
`PriceComponentTypeDto.Enum.al` is generated as a standalone file. Full `Kiota.Builder.Tests` suite
(2128 tests) passes.

---

## 7. Where to look next

- Refiner (CodeDOM transformations specific to AL):
  [`../../Refiners/ALRefiner.cs`](../../Refiners/ALRefiner.cs)
- AL-specific configuration model:
  [`../../Refiners/ALConfiguration.cs`](../../Refiners/ALConfiguration.cs)
- Object ID allocation: [`ALObjectIdProvider.cs`](ALObjectIdProvider.cs)
- Manifest (`app.json`) generation: [`ALAppManifestWriter.cs`](ALAppManifestWriter.cs)
- Method body dispatch: [`CodeMethodWriter.cs`](CodeMethodWriter.cs)
- Method category enum: [`ALMethodCategory.cs`](ALMethodCategory.cs)
- CustomData keys and typed accessors: [`ALCustomDataKeys.cs`](ALCustomDataKeys.cs), [`ALMetadata.cs`](ALMetadata.cs)
- Naming/type conventions: [`ALConventionService.cs`](ALConventionService.cs)
