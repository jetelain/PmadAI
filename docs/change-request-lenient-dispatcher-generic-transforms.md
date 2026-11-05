# Change Request: Generic Tool Dispatcher Transforms & Declarative Normalization (`Pmad.AI` `0.0.4`)

> **Target Component:** `Pmad.AI.Tools` (`LenientToolDispatcher`, `ToolDispatcherConfiguration`, `ToolMapping`, `ToolTranslation`)  
> **Origin:** Lessons learned from migrating `NightCodeForge.Agents` (`Step 11.43B`) to `Pmad.AI` `0.0.3`

---

## 1. Context & Motivation

During the migration of `NightCodeForge.Agents` to `Pmad.AI` `0.0.3` (`LenientToolDispatcher` and `LenientToolDispatchChatClient`), we were able to delete ~1,450 lines of legacy dispatcher code. However, `NightCodeForgeToolDispatcher` (`src/NightCodeForge.Agents/Dispatcher/LenientToolDispatcher.cs`) and `CanonicalAIFunctionFactory` still contain ~250 lines of custom imperative glue (`FindFirstNonNullValue`, `UnwrapToClr`, `TryParseInt`, `TryParseBool`, `CreateVirtualTranslationFunctions`, `ComputedParameters` lambdas for `read` and `edit`, and `EditTaskArgumentValidatingAIFunction`) to handle generic LLM tool-calling patterns.

Moving these generic transforms directly into `Pmad.AI.Tools` will make consumer configurations purely declarative and eliminate all manual `JsonElement` / dictionary manipulation in host applications.

---

## 2. Requested Enhancements

### 2.1 Built-In Null-Coalescing & Deterministic Alias Precedence (Top-Level, Nested & Computed)

`Pmad.AI` `0.0.3` already introduced null-coalescing precedence in Step 3 (`ParameterAliases`) and Step 5 (`propDict` casing normalization), which allowed removing `RegisterNullCoalescingFallbacks` for standard aliases. However, three edge cases remain where consumers still had to write manual fallback logic:

1. **Deterministic Tie-Breaking When Multiple Non-Null Aliases Are Present:**
   - In `LenientToolDispatcher.NormalizeCall` (Step 3), `aliased.Sort(...)` compares `(aNull, isExactCanonicalMatch)`. When a payload contains two non-null aliases for the same canonical parameter (neither equal to the canonical parameter name), the comparator returns `0`. Because `List<T>.Sort` uses an unstable `IntroSort`, the winning alias is not guaranteed to follow deterministic priority order.
   - **Requirement:** Preserve deterministic priority order (either insertion order in `ToolMapping.ParameterAliases` or appearance order in `rawArgs`) by including the original index as a tie-breaker in `aliased.Sort`.
2. **Post-Alias Input for `ComputedParameters` (Eliminating `FindFirstNonNullValue` & `UnwrapToClr`):**
   - Currently, `mapping.ComputedParameters` (Step 4) receives `rawArgs` *before* parameter aliases are mapped and *before* `JsonElement` values are unwrapped or coerced.
   - Consequently, every `ComputedParameters` delegate in consumer code must re-list all parameter aliases (`["offset", .. ReadOffsetAliases]`), manually unwrap `JsonElement` (`JsonValueKind.Null`, `Number`, `String`, `True`/`False`), and parse `#`-prefixed integers.
   - **Requirement:** Pass an alias-resolved, `JsonElement`-unwrapped dictionary (merged with unmapped raw keys such as `end_line` that are not schema parameters) to `ComputedParameters`—or provide an overload / `ToolParameterComputationContext` with typed accessors (`GetInt`, `GetString`, `GetBool`, `HasExplicitKey`) that automatically honour `ParameterAliases` and null-coalescing.
3. **Blank Collection Placeholder Handling Before `ComputedParameters`:**
   - When an LLM emits `"edits": [{}]` alongside flat shortcut parameters `"old_text": "foo", "new_text": "bar"`, `PromoteFlattenedParametersToArrays` (Step 4.5) recognizes `[{}]` as a blank collection (`IsBlankCollectionElement`), but Step 4 (`ComputedParameters`) only checks `!canonicalArgs.ContainsKey(computedParam) || IsNullOrUndefined(canonicalArgs[computedParam])`.
   - **Requirement:** In Step 4, treat collection values where all elements satisfy `IsBlankCollectionElement` as unset so `ComputedParameters` (and Step 4.5 promotion) can replace `[{}]`.

---

### 2.2 Declarative Range-to-Count / End-Line Pagination Transform (`start_line` + `end_line` $\rightarrow$ `offset` + `limit`)

Almost every file-reading or log-inspection tool exposes `(offset, limit)` (1-based start line + line count), whereas LLMs frequently call `read` with `(start_line, end_line)` or `(from_line, to_line)`.

- **Current Workaround in Host Code:** ~40 lines of `ComputedParameters["offset"]` and `ComputedParameters["limit"]` lambdas in `NightCodeForgeToolDispatcher.ConfigureRead`.
- **Proposed API in `Pmad.AI`:**
  ```csharp
  public sealed class RangeToLimitRule
  {
      public required string StartParameter { get; init; } // e.g. "offset"
      public required string CountParameter { get; init; } // e.g. "limit"
      public HashSet<string> EndParameterAliases { get; init; } = new(StringComparer.OrdinalIgnoreCase); // e.g. ["end_line", "endLine", "line_end", "to_line", "to", "end"]
      public int DefaultStart { get; init; } = 1;
      public bool Inclusive { get; init; } = true;
  }
  ```
  Or a fluent helper on `ToolMapping`:
  ```csharp
  mapping.MapEndLineToLimit(
      startParameter: "offset",
      limitParameter: "limit",
      endAliases: ["end_line", "endLine", "line_end", "to_line", "to", "end"],
      defaultStart: 1);
  ```
- **Behavior:**
  - When `limitParameter` is not directly provided (or is `null`) and any key in `EndParameterAliases` has a non-null integer value `endLine`:
    - Resolve `startLine` from `startParameter` (after alias resolution and integer coercion), defaulting to `DefaultStart` (`1`).
    - Compute `limit = Math.Max(1, endLine - startLine + (Inclusive ? 1 : 0))`.
    - Remove the consumed `end_line` alias key from `canonicalArgs` so no unmapped argument remains.

---

### 2.3 Enhanced Flattened-to-Collection Promotion & Explicit-Null-to-Empty-String Coercion (`edit` Tool Pattern)

Search-and-replace tools (`edit`) commonly accept either a batch array `edits: [{ oldText, newText, replaceAll }]` or flat single-edit shortcut arguments (`old_text`, `new_text`, `replace_all`).

Two generic issues currently force host applications to write custom `ComputedParameters["edits"]` and `ComputedParameters["new_text"]` lambdas:

1. **`PromoteFlattenedParametersToArrays` Skips Shortcut Parameters Declared on the Top-Level Schema:**
   - In `LenientToolDispatcher.PromoteFlattenedParametersToArrays` (lines 1415–1435), any argument key that exists in `propDict` (top-level tool schema properties) is skipped (`if (topLevelParams.Contains(argKey)) continue;`).
   - When the `AIFunction` signature is `Edit(string path, IReadOnlyList<EditChunk>? edits = null, string? old_text = null, string? new_text = null, bool replace_all = false)`, `old_text`, `new_text`, and `replace_all` (as well as their `camelCase` counterparts `oldText`, `newText`, `replaceAll`) are in `propDict`, so `PromoteFlattenedParametersToArrays` refuses to promote them into `edits`!
   - **Requirement:** Allow `ToolMapping` (or `PromoteFlattenedParametersToArrays`) to promote top-level parameters whose names/aliases match properties of a collection item schema whenever they are not required top-level parameters (like `path`)—or provide an explicit `mapping.PromotedCollectionParameters["edits"] = ["old_text", "new_text", "replace_all"]` setting.
2. **Explicit `null` Coercion to `string.Empty` for Deletion Replacements (`"new_text": null` $\rightarrow$ `""`):**
   - When an LLM wants to delete a matched block of text, it often emits `{"path": "a.cs", "old_text": "obsolete", "new_text": null}` (or `{"edits": [{"old_text": "obsolete", "new_text": null}]}`).
   - Because `NormalizeCall` unwraps `JsonValueKind.Null` to `null`, `"new_text": null` becomes indistinguishable from `"new_text"` being omitted altogether.
   - **Requirement:** Support a declarative `NullAsEmptyStringParameters` set on `ToolMapping` (supporting both top-level names like `"new_text"` and nested paths like `"edits[].newText"`). When an argument key (or any of its configured aliases) is *explicitly present* in the raw JSON/dictionary with a `null` value and targets a parameter in `NullAsEmptyStringParameters`, coerce its value to `string.Empty` (`""`) instead of `null`.

---

### 2.4 First-Class Virtual Tool Translations (`ToolTranslation.Aliases`, `ToolTranslation.ParameterAliases` & `Validate()` Support)

When configuring dropped/virtual tools that translate into shell commands (`list_dir` $\rightarrow$ `ls -la {path:.}`, `grep_search` $\rightarrow$ `rg -n -- {query} {path:.}`, `get_file_outline`, `find_files`), three limitations in `Pmad.AI` `0.0.3` force boilerplate in host code:

1. **No `Aliases` or `ParameterAliases` on `ToolTranslation`:**
   - To translate `list_dir` and its aliases (`listdir`, `list_directory`, `ls`) with parameter aliases (`dir`, `directory`, `folder` $\rightarrow$ `path`), the consumer must loop over all 4 tool names, add 4 identical `ToolTranslation` objects to `config.Translations`, and add 4 `ToolMapping` objects to `config.Tools[source]`.
2. **`Validate()` Rejects Virtual Translated Tools Unless Dummy `AIFunction`s Are Registered:**
   - Because `RenderTranslationCall` reads parameter aliases from `config.Tools[translation.SourceTool]`, adding `config.Tools["list_dir"]` causes `dispatcher.Validate()` to report:
     `ToolValidationSeverity.Error: Configured tool 'list_dir' is not registered as an AIFunction.`
   - To satisfy `Validate()`, `NightCodeForgeToolDispatcher` had to create and register 16 dummy `AIFunction` instances (`CreateVirtualTranslationFunctions()`) that are never executed!
3. **`RenderTranslationCall` Does Not Unwrap `JsonElement(Null)` or Coalesce Null Aliases:**
   - In `RenderTranslationCall` (line 538), `TryGetValueCaseInsensitive(rawArgs, paramName, out val)` returns `true` even when `val` is `null` or `JsonValueKind.Null`, preventing fallback to a non-null alias or to `{param:default}`.

- **Proposed Enhancements:**
  1. Add `Aliases` (`HashSet<string>`) and `ParameterAliases` (`Dictionary<string, string>`) directly on `ToolTranslation`, and index `ToolTranslation.Aliases` in `ToolDispatcherConfigurationSnapshot` for $O(1)$ lookup in `TryGetTranslation`.
  2. In `LenientToolDispatcher.Validate()`:
     - If a key in `_configuration.Tools` is a `SourceTool` (or alias) of a `ToolTranslation`, do **not** require it to be registered in `_registeredTools` as an `AIFunction`.
     - Instead, validate its `ParameterAliases` targets against the `{placeholder}` names extracted from `translation.CommandTemplate`.
  3. In `RenderTranslationCall`:
     - Treat `null`, `JsonValueKind.Null`, and `JsonValueKind.Undefined` as unset when probing `paramName` and its `ParameterAliases`, so a non-null alias or `{param:default}` is selected.

---

### 2.5 Automatic Casing & Separator-Insensitive Tool Name Resolution

- **Current Behavior:** Parameter names in `Pmad.AI` automatically match across `snake_case`, `camelCase`, and `PascalCase`. However, tool names and tool aliases in `ResolveCanonicalToolAndMapping` only match by exact `StringComparer.OrdinalIgnoreCase`.
  - Example: If `"read_file"` is configured as an alias for `"read"`, an LLM calling `"ReadFile"` or `"read-file"` fails to match unless `"readfile"` and `"read-file"` are also manually added to `Aliases`.
- **Proposed Enhancement:**
  - Normalize tool names and tool aliases in `ToolDispatcherConfigurationSnapshot` and `RegistredToolsMetadata` using a secondary lookup table keyed by a canonicalized identifier (e.g. `ToSnakeCase(name)` or alphanumeric-only lowercase ` name.Replace("_", "").Replace("-", "").ToLowerInvariant()`).
  - Exact case-insensitive matches still take priority, with separator/casing-normalized matches as automatic fallback (recording `LenientDispatchEventType.CasingNormalized` or `ToolAliasResolved`).

---

### 2.6 Descriptive `ArgumentException` on Uncoercible Scalar / Array Elements

- **Current Behavior:** When `EnableParameterCoercion = true` and an LLM passes a non-numeric string for an `integer` or `integer[]` parameter (e.g., `depends_on_ids: ["not-a-number"]`), `CoerceParameterValue` leaves `"not-a-number"` as a `string` inside `List<object?>`. When `AIFunction.InvokeAsync` subsequently deserializes the argument via `System.Text.Json`, it throws `JsonException` instead of `ArgumentException`.
- **Current Workaround in Host Code:** `EditTaskArgumentValidatingAIFunction` wrapper in `CanonicalAIFunctionFactory.cs`.
- **Proposed Enhancement:**
  - When a schema property (or array item schema) is strictly typed as `integer`, `number`, or `boolean` (not a union with `string`), and a value cannot be coerced to that scalar type, throw a descriptive `ArgumentException` (e.g. `"Element 'not-a-number' of parameter 'depends_on_ids' is not a valid integer."`) during invocation/coercion—or wrap `JsonException` from `AIFunction.InvokeAsync` into `ArgumentException` inside `LenientToolDispatcher.ExecuteDispatchedAsync`.

