Feature: Album Processing Smoke (Real AI)
	As a user sending photos to Telegram
	I want albums to be processed end-to-end with the real AI
	So that I can verify the full integration works

	Scenario: Single photo album is processed end-to-end with real AI
		Given the system is configured for real AI integration testing
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
		And the real AI processing pipeline is built
		When I enqueue a photo with caption "Test document"
		Then the album is processed with real AI
		And a PDF is created
		And the summary is published to Obsidian
		And the PDF is published to the output directory
		And the trace log contains expected traces
