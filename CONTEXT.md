# Paperoni

Paperoni is a background service that ingests photo albums from Telegram, produces AI-written Markdown summaries and document-corrected PDFs, and publishes them to a configurable output directory.

## Language

**Album**:
A set of photos sent together as a Telegram media group, collected with a 2-second debounce, and processed as a single unit.
_Avoid_: Message group, batch

**AlbumId**:
The Telegram message ID of the first photo in an album. Used as the primary key throughout the pipeline — keys the working directory, reply message, span tags, and trace log routing.
_Avoid_: msgId, MessageId

**Photo**:
A single image file received from a Telegram user.
_Avoid_: Image (prefer Photo when it comes from Telegram specifically; Image is acceptable in OpenCV/processing contexts)

**Working Directory**:
A per-message folder on disk (`{DownloadBasePath}/{messageId}/`) where downloaded photos, metadata JSON, AI response, and the generated PDF live during processing.

**Pipeline Script**:
A C# script (`.csx`) that defines the schema, prompt, filename logic, and output format for the pipeline. Loaded fresh on every album processing cycle. Must define four conventions: `Schema` (Type), `Prompt` (string), `GetFilename` (method), and `Format` (method). Compile errors are sent to the user via Telegram.
_Avoid_: Config file, template, extension

**Pipeline Result**:
A JSON file (`PipelineResult.json`) persisted in the **Working Directory** containing the deserialized LLM record and the computed filename. Used for retry/delete operations without re-executing the pipeline script.

**PDF**:
The final A4 document combining all processed photos, one per page with 1cm margins. Generated via QuestPDF after the OpenCV correction pipeline.

**Processed Image**:
A photo that has passed through the OpenCV pipeline: optional document-detection + perspective warp, grayscale conversion, and histogram-based auto-levels, encoded as JPEG.

**Telegram Replier**:
A component that edits the bot's reply message with emoji-prefixed progress status.
_Avoid_: Notifier, status updater

**File Publisher**:
A component that writes pipeline output to a configured destination. The Markdown publisher writes a string directly; the PDF publisher copies a file from the **Working Directory**. Registered as two keyed singletons — one for Markdown (`PublisherTarget.Markdown`) and one for PDF (`PublisherTarget.Pdf`).

**ActivityScope**:
A null-safe wrapper around `System.Diagnostics.Activity` that auto-tags `AlbumId` from the ambient **AlbumIdAccessor**,
provides fluent `SetTag`, and is managed by `Tracer.TraceAsync<T>()` / `Tracer.TraceAsync<T, TResult>()` which
automatically set `Ok` on success and `Error` on exception.
_Avoid_: Manual `Tracer.StartActivity<T>()` + `?.SetTag` + `?.SetStatus` pattern (use `TraceAsync` instead).

**AlbumIdAccessor**:
A singleton service that flows the current **AlbumId** through async context via `AsyncLocal<int?>`. Used by *
*ActivityScope** to auto-tag all spans without manual wiring.
_Avoid_: Passing `msgId` as a parameter to every trace call.

## Relationships

- An **Album** is composed of one or more **Photos**
- An **Album** has exactly one **Working Directory**
- A **Working Directory** contains the raw **Photos**, the LLM raw response, the **Pipeline Result**, and the **PDF**
- Processing an **Album** produces one formatted output (Markdown) and one **PDF**
- The **Pipeline Script** defines the LLM schema, prompt, filename, and output format
- The **Pipeline Result** stores the LLM record and computed filename for retry/delete without re-running the script
- The Markdown output is published via the **File Publisher** (Markdown target)
- The **PDF** is published via the **File Publisher** (PDF target)
- The **Telegram Replier** sends progress updates throughout the pipeline

## Example dialogue

> **Dev:** "If an **Album** has no caption, does the formatted output still get written?"
> **User:** "Yes — the **Pipeline Script** determines how captions are included. The pipeline injects them as script globals regardless."

> **Dev:** "What if OpenCV doesn't find a document in a **Photo**?"
> **User:** "It skips the perspective warp and still applies grayscale + auto-levels. The **Processed Image** just isn't cropped."

## Flagged ambiguities

- "Image" was used to mean both a source **Photo** from Telegram and the **Processed Image** after OpenCV — these are distinct pipeline stages.
