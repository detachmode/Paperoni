# Chat Session Feature

## Summary

Add conversational chat capability to the Paperoni Telegram bot. Users send text messages (or `/chat`-prefixed photo
messages) and the LLM responds, with 8 tools to inspect, clean, retry, and re-process albums, plus write/search Obsidian
notes.

---

## Routing

| Message                          | Route                      |
|----------------------------------|----------------------------|
| Photo(s), no `/chat` prefix      | Album pipeline (unchanged) |
| Text-only, no `/chat` prefix     | Chat Session               |
| `/chat` prefix + optional photos | Chat Session               |

## Domain Terms (→ `CONTEXT.md`)

**Chat Session**: An in-memory conversation between a Telegram user and the LLM, scoped to a `ChatId`. Contains message
history, a per-chat `SemaphoreSlim` for sequential processing, and an LRU eviction policy (20 sessions).

**Chat Tool**: A function registered with `[AIFunction]` that the Chat Session LLM can invoke to perform actions on
behalf of the user (list albums, clean dirs, retry processing, search summaries, read logs, write/search Obsidian).

## Architecture

```
TelegramPhotoAlbumCollector.HandleMessage
├── Photo + no /chat prefix  → Album pipeline (unchanged)
├── Text-only                → ChatSessionChannel (NEW)
└── /chat prefix + optional photos → ChatSessionChannel (NEW)

ChatSessionService : BackgroundService
├── Consumes ChatWorkItem from ChatSessionChannel
├── Per-chat: SemaphoreSlim + List<ChatMessage> history
├── LRU eviction (20 sessions, oldest evicted on insert)
├── Uses same IChatClient as album summarizer
├── Streams tool progress via ITelegramReplier
└── Max 10 tool iterations per user message
```

## Files

| File                                                         | Status   | Purpose                                      |
|--------------------------------------------------------------|----------|----------------------------------------------|
| `src/Paperoni.Telegram/Chat/ChatWorkItem.cs`                 | New      | `ChatWorkItem` and `PhotoAttachment` records |
| `src/Paperoni.Telegram/Chat/ChatSessionChannel.cs`           | New      | `Channel<ChatWorkItem>` wrapper              |
| `src/Paperoni.Telegram/TelegramReplier.cs`                   | Modified | Add `SendReply(chatId, text)`                |
| `src/Paperoni.Telegram/Album/TelegramPhotoAlbumCollector.cs` | Modified | Route text/`/chat` messages to session       |
| `src/Paperoni.Telegram/DependencyInjection.cs`               | Modified | Register `ChatSessionChannel`                |
| `src/Paperoni.AlbumProcessing/Chat/ChatTools.cs`             | New      | 8 AIFunction-annotated tools                 |
| `src/Paperoni.AlbumProcessing/Chat/ChatSessionService.cs`    | New      | BackgroundService with tool loop             |
| `src/Paperoni.AlbumProcessing/DependencyInjection.cs`        | Modified | Add `AddChatSession()`                       |
| `src/Paperoni/Program.cs`                                    | Modified | Call `AddChatSession()`                      |
| `src/Paperoni/appsettings.json`                              | Modified | Add Obsidian config keys                     |
| `src/Paperoni.Telegram/Paperoni.Telegram.csproj`             | Modified | Add `Microsoft.Extensions.AI` package        |
| `CONTEXT.md`                                                 | Modified | New glossary terms                           |

## Tools

| # | Tool                                 | Description                              |
|---|--------------------------------------|------------------------------------------|
| 1 | `list_albums(filter?)`               | Scan working dirs, return album metadata |
| 2 | `clean_album(albumId)`               | Delete a working directory               |
| 3 | `retry_album(albumId)`               | Enqueue to AlbumQueue                    |
| 4 | `re_pdf(albumId)`                    | Re-run PDF generation                    |
| 5 | `search_summaries(query)`            | Full-text search published .md folder    |
| 6 | `read_logs(albumId)`                 | Call ILogRetriever                       |
| 7 | `write_obsidian(title, body)`        | Create .md in configured Obsidian inbox  |
| 8 | `search_obsidian(query, maxResults)` | Full-text search vault                   |

## UX Flow

1. User sends text → bot sends "thinking..." reply
2. As tools execute, reply edits with progress (e.g., "🗑️ Deleting album 12345...")
3. Final answer edits the thinking reply
4. Error: edit reply to "❌ {errorMessage}"
5. Max 10 tool iterations per message

## Configuration

| Key                 | Default             | Purpose                 |
|---------------------|---------------------|-------------------------|
| `ObsidianVaultPath` | `~/Obsidian/BCI/AI` | Root of Obsidian vault  |
| `ObsidianInboxPath` | `inbox`             | Subfolder for new notes |

## Dependencies (no new projects, no new NuGet packages beyond Microsoft.Extensions.AI)

| New Component        | Depends On                                                                                                                                                                     |
|----------------------|--------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `ChatSessionService` | `IChatClient`, `ITelegramReplier`, `AlbumQueue`, `AlbumWorkingDirectory`, `ILogRetriever`, `IPdfCreator`, `IFilePublisher` (Markdown/Pdf), `AlbumIdAccessor`, `IConfiguration` |
| `ChatTools`          | `AlbumWorkingDirectory`, `AlbumQueue`, `ILogRetriever`, `IPdfCreator`, `IFilePublisher` (Markdown), `AlbumIdAccessor`, `IConfiguration`                                        |
| `ChatSessionChannel` | None                                                                                                                                                                           |
