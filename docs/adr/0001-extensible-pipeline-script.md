# ADR 0001: Extensible Pipeline Script

## Status

Proposed

## Context

Paperoni currently uses a hardcoded pipeline: a static `Prompt.md` defines the LLM instructions, `MarkdownHelper` post-processes the output, and `AiResult` only persists the title. This makes it impossible for users to customize the output format, control the PDF reference style, or adapt the schema to different use cases without modifying source code.

The LLM response is tightly coupled to a single record type (`AiResult` with just a `Title` field), and the Markdown output format is fixed in code. Users who want to publish to Obsidian with wikilinks, or to a different destination with URLs, cannot configure this.

## Decision

Replace the hardcoded pipeline with a **Pipeline Script** — a user-authored C# script (`.csx`) that defines the entire pipeline behavior. The script must define four conventions:

```cs
// --- SCHEMA ---
public record AlbumNote(
    [property: Description("Title: YYYY-MM-DD Category Counterparty Tags")]
    string Title,
    [property: Description("Lowercase tags, 3-6 items")]
    string[] Tags,
    [property: Description("Importance level")]
    string Importance,
    string Summary,
    string MarkdownBody
);

var Schema = typeof(AlbumNote);

// --- PROMPT ---
var Prompt = """
Analysiere das folgende Dokument...
Rules:
- Importance: high if amount > 400€, medium 100-300€, low otherwise
- Categories: Auto, Motorrad, Gesundheit, Wohnung, Finanzen, Other
""";

// --- FILENAME ---
string GetFilename(AlbumNote note) =>
    note.Title.Replace(":", " -").Replace("/", "_");

// --- FORMAT ---
string Format(AlbumNote note) {
    var filename = GetFilename(note);
    return $"""
---
title: {filename}
tags: [{string.Join(", ", note.Tags.Select(t => $"#{t.ToLower()}"))}]
pdf: "[[{filename}.pdf]]"
---
# {filename}
{note.MarkdownBody}
""";
}
```

### Convention details

| Convention | Type | Required | Description |
|---|---|---|---|
| `Schema` | `Type` | Yes | Record type used to generate the JSON schema for the LLM call |
| `Prompt` | `string` | Yes | System prompt sent to the LLM |
| `GetFilename(T)` | `string` | Yes | Maps the LLM record to a safe filename (for `.md` and `.pdf`) |
| `Format(T)` | `string` | Yes | Maps the LLM record to the final Markdown output |

### Key design decisions

1. **All four conventions are mandatory** — no defaults, no surprises. The user has full control.

2. **Schema and prompt are separate concerns** — `[Description]` attributes on the record define the JSON schema shape; the `Prompt` string defines the rules and instructions. Both are sent to the LLM, but through different channels (schema as structured JSON schema, prompt as natural language).

3. **`GetFilename` is the single source of truth** — the pipeline calls it once and passes the result to all downstream steps (Markdown publisher, PDF publisher). This guarantees the filename in the Markdown frontmatter always matches the actual files on disk.

4. **Pipeline-injected globals** — the pipeline injects `Captions` (list of user captions from Telegram) and `CurrentDate` (`DateTime.Now`) as global variables into the CSX script before execution. The prompt can reference them: `var Prompt = $"...\nDate: {CurrentDate:yyyy-MM-dd}\n{(Captions.Count > 0 ? $"User instructions: {string.Join(" | ", Captions)}" : "")}\n..."`.

5. **Runtime type deserialization** — the LLM response is deserialized into the CSX-defined record type via reflection. `GetFilename` and `Format` are invoked via reflection against the runtime type. No `Dictionary<string, object>`.

6. **Script is loaded fresh on every album** — no caching. Compile errors are sent to the user via Telegram. Runtime errors cause the album to fail completely.

7. **`PipelineResult.json` replaces `AiResult.json`** — persisted in the working directory, contains the full deserialized LLM record and the computed filename. Used for retry/delete without re-executing the script.

8. **Markdown publisher writes a string directly** — `Format` returns a string, the publisher writes it to the destination. No intermediate file in the working directory.

9. **PDF reference in Markdown is user-controlled** — the `Format` method decides whether to use `[[filename.pdf]]` (Obsidian wikilink), `./filename.pdf` (relative path), or a URL. The user who writes the script knows where the PDF is published.

10. **Script path is configured via `AiSettings.ScriptFilePath`** — replaces the current `AiSettings.PromptFilePath`. Mounted in Docker like other config files.

### Working directory contents (new)

| File | Purpose |
|---|---|
| Raw photos | Input from Telegram |
| Processed images | OpenCV output |
| `firstAiResponse.md` | Debug: raw LLM output |
| `PipelineResult.json` | Full deserialized record + computed filename |
| `{filename}.pdf` | Generated PDF |

### Pipeline flow (new)

```
1. Load CSX script from ScriptFilePath
   → Validate: Schema, Prompt, GetFilename, Format must exist
   → Compile error? Send to user via Telegram
2. Inject globals: Captions, CurrentDate
   → Execute script
3. Send Prompt + JSON schema (from Schema) to LLM
   → LLM returns JSON
4. Deserialize JSON into runtime record type (from Schema)
5. Call GetFilename(record) → filename
6. Call Format(record) → markdown string
7. Persist PipelineResult.json (record + filename)
8. Persist firstAiResponse.md (debug)
9. Create PDF with filename
10. Publish markdown string (FilePublisher, Markdown target)
11. Publish PDF file (FilePublisher, PDF target)
```

### Components removed

| Component | Reason |
|---|---|
| `Prompt.md` (static file) | Replaced by `Prompt` in CSX |
| `MarkdownHelper.GetTitleFromMarkdown()` | Replaced by `GetFilename` in CSX |
| `MarkdownHelper.FixMarkdownFromAi()` | Replaced by `Format` in CSX |
| `AiResult` (only Title) | Replaced by `PipelineResult` (full record + filename) |
| `FilePromptProvider` (partial) | Replaced by CSX `Prompt` + injected globals |

### Components modified

| Component | Change |
|---|---|
| `FilePublisher` (Markdown) | Accepts a string instead of copying a file |
| `AiSettings` | `PromptFilePath` → `ScriptFilePath` |
| `PdfCreator` | Receives filename from `GetFilename` instead of `AiResult.Title` |
| `AlbumProcessor` | Orchestrates CSX loading, execution, and the new pipeline flow |

## Consequences

- Users can fully customize schema, prompt, output format, and filename logic without touching the codebase
- The script must be valid C# — compile errors surface immediately via Telegram
- Adding a new field to the record requires only editing the CSX file
- The Markdown output format is entirely user-controlled, including PDF references
- Runtime errors in `Format` or `GetFilename` cause the album to fail — no fallback