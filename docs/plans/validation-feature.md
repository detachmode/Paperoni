# Pipeline Script Validation Feature

## Design Decisions

1. **Optional convention** — existing scripts work without changes
2. **`ValidationContext` parameter** — `Assert` scoped to `Validate` only, not a global
3. **`ValidationContext` in `Paperoni.Ai`** — script already imports this namespace
4. **Validate runs before GetFilename** — validates the record before filename computation
5. **Empty-title check moves** from `GetFilename` to `Validate`

## Pipeline Flow (updated)

```
Deserialize LLM JSON
    ↓
InvokeValidate(record)     ← NEW STEP
    ↓ (failures → retry with validation errors)
InvokeGetFilename(record)
    ↓
InvokeFormat(record)
    ↓
Return PipelineRunResult
```

## Changes

### 1. NEW: `src/Paperoni.Ai/ValidationContext.cs`

```csharp
public sealed class ValidationContext
{
    private readonly List<string> _failures = [];
    public IReadOnlyList<string> Failures => _failures;
    public bool HasFailures => _failures.Count > 0;

    public void Assert(bool condition, string message)
    {
        if (!condition) _failures.Add(message);
    }
}
```

### 2. NEW: `src/Paperoni.Ai/ValidationException.cs`

```csharp
public class ValidationException : Exception
{
    public IReadOnlyList<string> Failures { get; }
    public ValidationException(IReadOnlyList<string> failures)
        : base(string.Join(Environment.NewLine, failures))
    {
        Failures = failures;
    }
}
```

### 3. MODIFY: `src/Paperoni.Contract/PipelineScript.cs`

- Add `Delegate? ValidateDelegate` property (nullable)
- Add `InvokeValidate(object record)`:
  - If `ValidateDelegate` is null → no-op
  - Creates `ValidationContext`, calls `DynamicInvoke(record, context)`
  - If `context.HasFailures` → throws `ValidationException`

### 4. MODIFY: `src/Paperoni.Ai/ScriptLoader.cs`

- After extracting `GetFilename`/`Format`, try to extract `Validate` variable
- If found and is a `Delegate` → store as `ValidateDelegate`; otherwise null
- No error if missing

### 5. MODIFY: `src/Paperoni.Ai/PipelineService.cs`

- After deserialization (line 110), call `script.InvokeValidate(record)` before `InvokeGetFilename`
- Add `ValidationException` to retry catch clause (line 135)
- Update `BuildRetryPrompt` to format validation failures as bullet list

Retry prompt for validation:
```
Your previous response was parsed but failed validation. Fix these issues:

- Importance must be high, medium, or low
- Tags must have between 3 and 6 items

Your previous response:
{badResponse}

Respond with corrected JSON only. Do not include any explanation.
```

### 6. MODIFY: `src/Paperoni/defaultPipeline.csx`

- Add `Validate` method with domain assertions:
  - Title non-empty (moved from `GetFilename`)
  - Importance ∈ {high, medium, low}
  - Tags length 3–6
  - DocumentType ∈ {Rechnung, Quittung, Vertrag, Brief, Garantie, Mahnung}
  - Area ∈ {Auto, Motorrad, Gesundheit, Kochen, Wohnung, Bosch, Software development, Finanzen, Other}
- Remove empty-title check from `GetFilename`

### 7. MODIFY: `test/Paperoni.Tests/ScriptLoaderTests.cs`

- Script with `Validate` → loads, `ValidateDelegate` non-null
- Script without `Validate` → loads, `ValidateDelegate` null
- `InvokeValidate` passing → no exception
- `InvokeValidate` failing → `ValidationException` with all failures

### 8. MODIFY: `README.md`

Update "Pipeline Script" section to document `Validate` as an optional fifth convention.

## File Summary

| File | Action |
|------|--------|
| `src/Paperoni.Ai/ValidationContext.cs` | NEW |
| `src/Paperoni.Ai/ValidationException.cs` | NEW |
| `src/Paperoni.Contract/PipelineScript.cs` | MODIFY |
| `src/Paperoni.Ai/ScriptLoader.cs` | MODIFY |
| `src/Paperoni.Ai/PipelineService.cs` | MODIFY |
| `src/Paperoni/defaultPipeline.csx` | MODIFY |
| `test/Paperoni.Tests/ScriptLoaderTests.cs` | MODIFY |
| `README.md` | MODIFY |
