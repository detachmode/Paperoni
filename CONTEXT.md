# Paperoni

Paperoni is a background service that ingests photo albums from Telegram, produces AI-written Markdown summaries and document-corrected PDFs, and publishes them to a configurable output directory.

## Language

**Album**:
A set of photos sent together as a Telegram media group, collected with a 2-second debounce, and processed as a single unit.
_Avoid_: Message group, batch

**Photo**:
A single image file received from a Telegram user.
_Avoid_: Image (prefer Photo when it comes from Telegram specifically; Image is acceptable in OpenCV/processing contexts)

**Working Directory**:
A per-message folder on disk (`{DownloadBasePath}/{messageId}/`) where downloaded photos, metadata JSON, AI response, and the generated PDF live during processing.

**AI Summary**:
The Markdown document produced by the LLM describing the contents of an album's photos. Written to `firstAiResponse.md` and the title is persisted as `AiResult.json`.

**PDF**:
The final A4 document combining all processed photos, one per page with 1cm margins. Generated via QuestPDF after the OpenCV correction pipeline.

**Processed Image**:
A photo that has passed through the OpenCV pipeline: optional document-detection + perspective warp, grayscale conversion, and histogram-based auto-levels, encoded as JPEG.

**Telegram Replier**:
A component that edits the bot's reply message with emoji-prefixed progress status.
_Avoid_: Notifier, status updater

**File Publisher**:
A generic component that copies a file from the **Working Directory** to a configured output directory. Registered as two keyed singletons — one for Markdown (`PublisherTarget.Markdown`) and one for PDF (`PublisherTarget.Pdf`).

## Relationships

- An **Album** is composed of one or more **Photos**
- An **Album** has exactly one **Working Directory**
- A **Working Directory** contains the raw **Photos**, the **AI Summary**, and the **PDF**
- Processing an **Album** produces one **AI Summary** and one **PDF**
- The **AI Summary** is published via the **File Publisher** (Markdown target)
- The **PDF** is published via the **File Publisher** (PDF target)
- The **Telegram Replier** sends progress updates throughout the pipeline

## Example dialogue

> **Dev:** "If an **Album** has no caption, does the **AI Summary** still get written?"
> **User:** "Yes — the **AI Summary** just won't include 'User's instructions'. The base prompt still applies."

> **Dev:** "What if OpenCV doesn't find a document in a **Photo**?"
> **User:** "It skips the perspective warp and still applies grayscale + auto-levels. The **Processed Image** just isn't cropped."

## Flagged ambiguities

- "Image" was used to mean both a source **Photo** from Telegram and the **Processed Image** after OpenCV — these are distinct pipeline stages.
