# Paperoni

Send photos of documents to a Telegram bot — Paperoni generates structured Markdown notes and A4 PDFs, ready for your vault or archive.

<!-- TODO: add screenshot showing Telegram app with sent photos, and the generated Markdown + PDF output side by side -->

## What it does

1. You send photos of documents (receipts, invoices, contracts, etc.) to a Telegram bot — individually or as an album
2. Paperoni processes the images (perspective correction, grayscale, configurable contrast/shadow correction)
3. An LLM extracts structured data: title, date, counterparty, tags, category, importance, and a Markdown summary
4. A formatted Markdown file and an A4 PDF are saved to your output directory (like an Obsidian a vault)

The output format is fully customizable via a C# script — see [Pipeline Script](#pipeline-script).

## Quick start (Docker Compose)

Copy the Docker Compose file below into a `docker-compose.yml` and configure your LLM provider and Telegram bot token.

### Local LLM (e.g. llama.cpp, Ollama)

```yaml
services:
  paperoni:
    image: ghcr.io/detachmode/paperoni:latest
    restart: on-failure:3
    environment:
      Telegram__BotToken: "${Telegram__BotToken:?Telegram__BotToken is required}"
      Ai__Endpoint: "http://host.docker.internal:2276"
      Ai__Model: "qwen-3.6-35b-a3b-q4"
      ImageProcessing__CorrectionMode: "AutoLevels"
      AlbumProcessing__MarkdownOutputPath: /output/markdown
      AlbumProcessing__PdfOutputPath: /output/pdf
      PaperoniWorkingDirectory: /data
    extra_hosts:
      - host.docker.internal:host-gateway
    volumes:
      - ./workingDir:/data
      - ./output/markdown:/output/markdown
      - ./output/pdf:/output/pdf
```

```bash
export Telegram__BotToken="your-bot-token"  # get one from @BotFather
docker compose up -d
```

### Cloud LLM (example here with: OpenCode Go)

```yaml
services:
  paperoni:
    image: ghcr.io/detachmode/paperoni:latest
    restart: on-failure:3
    environment:
      Telegram__BotToken: "${Telegram__BotToken:?Telegram__BotToken is required}"
      Ai__Endpoint: "https://opencode.ai/zen/go/v1"
      Ai__Model: "qwen3.6-plus"
      Ai__ApiKey: "${Ai__ApiKey:?Ai__ApiKey is required}"
      ImageProcessing__CorrectionMode: "AutoLevels"
      AlbumProcessing__MarkdownOutputPath: /output/markdown
      AlbumProcessing__PdfOutputPath: /output/pdf
      PaperoniWorkingDirectory: /data
    volumes:
      - ./workingDir:/data
      - ./output/markdown:/output/markdown
      - ./output/pdf:/output/pdf
```

```bash
export Telegram__BotToken="your-bot-token"  # get one from @BotFather
export Ai__ApiKey="sk-..."
docker compose up -d
```

Send photos to the bot and check your `output/` folder.

## QuickStart (without docker)

Download binaries at [GitHub Releases](https://github.com/detachmode/paperoni/releases) for Windows (x64), Linux (x64, arm64), and macOS (x64, arm64).

Modify the appsettings.json:
- set Telegram BotToken
- set AI model and endpoint

## Configuration

All settings are configured via environment variables or `appsettings.json`. Use double underscores (`__`) for env vars (e.g., `Ai__Endpoint`).

### Required

| Setting | Description |
|---|---|
| `Telegram__BotToken` | Telegram bot token |
| `Ai__Endpoint` | OpenAI-compatible endpoint URL (local or cloud) |
| `Ai__Model` | Model name for your LLM |
| `AlbumProcessing__MarkdownOutputPath` | Where to save generated Markdown files |
| `AlbumProcessing__PdfOutputPath` | Where to save generated PDFs |

### Optional

| Setting | Default | Description |
|---|---|---|
| `Ai__ApiKey` | — | API key (required for remote/cloud endpoints) |
| `Ai__TimeoutSeconds` | `600` | Request timeout |
| `Ai__MaxRetries` | `2` | Retries on failure |
| `Ai__ScriptFilePath` | `defaultPipeline.csx` | Path to the [pipeline script](#pipeline-script) |
| `Cropping__Mode` | `Auto` | Cropping mode: `Off`, `OpenCvOnly`, `Auto`, or `ForceLlm` |
| `Cropping__HighConfidenceThreshold` | `0.75` | OpenCV score cutoff for high-confidence crops |
| `Cropping__MediumConfidenceThreshold` | `0.45` | OpenCV score cutoff for medium-confidence crops |
| `Cropping__LlmTimeoutSeconds` | `120` | LLM crop timeout per Photo |
| `Cropping__LlmMaxConcurrency` | `1` | Maximum concurrent LLM crop calls |
| `Cropping__LlmMaxDimension` | `1024` | Maximum long side sent to the LLM crop detector |
| `ImageProcessing__CorrectionMode` | `AutoLevels` | PDF image correction mode: `AutoLevels` or `ShadowNormalized` |
| `AlbumProcessing__TestMode` | `false` | Route all output to `TestModeOutputPath` |
| `AlbumProcessing__TestModeOutputPath` | — | Output directory in test mode |
| `AlbumProcessing__WorkingDirectoryRetentionDays` | `7` | Days to keep per-album working directories (≤0 = never clean) |
| `PaperoniWorkingDirectory` | system temp | Root for per-album working directories |
| `Diagnostics__LogPath` | working directory | Log file directory |

## Pipeline Script

Processing behaviour is defined by a C# script (`.csx`). The script controls:

- **Schema** — the JSON structure the LLM need to respond with (define your own fields)
- **Prompt** — the system prompt and extraction rules
- **Filename** — how the LLM result maps to a safe filename
- **Format** — the Markdown output (including YAML frontmatter)

The default script is [`src/Paperoni/defaultPipeline.csx`](./src/Paperoni/defaultPipeline.csx). To customize, copy it and point `Ai:ScriptFilePath` to your copy.
The script is reloaded on every album, so changes take effect immediately. Also check the logs on startup to see if the script has compilation errors.

Example of the default Markdown output:

```markdown
---
pdf: "[[2025-06-05 Auto Smith Auto Repair Invoice.pdf]]"
counterparty: Smith Auto Repair
document_type: Invoice
importance: medium
amount: 114.0
parent:
  - "[[Car]]"
tags:
  - "invoice"
  - "repair"
  - "paid"
---

# Summary
Vehicle repair invoice for oil change and filter replacement...

# Key Facts
| Item | Amount |
|------|--------|
| Oil change | 89.00 |
| Filter | 25.00 |
```

## Building from source

```bash
dotnet test test/Paperoni.Tests/Paperoni.Tests.csproj
dotnet run --project src/Paperoni
```

Requires .NET 10 SDK.

## Tech

- **.NET 10** (Worker Service)
- **Telegram.Bot** — Telegram Bot API
- **OpenCvSharp** — Image processing (document detection, warp, auto-levels, shadow normalization)
- **Microsoft.Extensions.AI** + **OpenAI client** — LLM integration
- **QuestPDF** — PDF generation
