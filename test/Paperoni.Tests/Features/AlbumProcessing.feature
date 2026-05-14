Feature: Album Processing
	As a user sending photos to Telegram
	I want albums to be processed automatically
	So that I receive AI summaries and processed PDFs

	Scenario: Single photo album is processed end-to-end
		Given the system is configured for integration testing
		And the prompt template is:
		"""
Analyse the following document and extract the text.
Extrakt the full visible content and return it as a markdown.

## Output format (Markdown)

---
title: put here the heading you find in the document
---

# Summary
Short summary of the document

# Complete Text
Extracted text from the document.
		"""
		And the processing pipeline is built
		When I enqueue a photo with caption "Test document"
		Then the album is processed
		And the AI summary mentions "title: Lorem ipsum"
		And a PDF is created
		And the summary is published to Obsidian
		And the PDF is published to Google Drive
		And the bot replied with "🤖 AI is reading"
		And the bot replied with "📄 Creating PDF"
		And the last bot reply starts with "Done:"
