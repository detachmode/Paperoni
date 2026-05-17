Feature: Album Processing
As a user sending photos to Telegram
I want albums to be processed automatically
So that I receive AI summaries and processed PDFs

Background:
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
    And the pipeline is started

Scenario: Single photo album is processed end-to-end
    When I enqueue the message
    Then the album is processed
    And the AI summary mentions "title: Lorem ipsum"
    And a PDF is created
    And the summary is published to Obsidian
    And the PDF is published to the output directory
    And the bot replied with "🤖 AI is reading"
    And the bot replied with "📄 Creating PDF"
    And the last bot reply starts with "Done:"
    And the bot reacted with "👏"
    And the trace log contains expected traces

Scenario: Multi-photo album produces a PDF with correct page count
    Given the album has 3 photos
    When I enqueue the message
    Then the album is processed
    And the AI summary mentions "title: Lorem ipsum"
    And the PDF has 3 pages
    And the last bot reply starts with "Done:"
    And the bot reacted with "👏"
    And the trace log contains expected traces

Scenario: Album retry re-processes and cleans old published files
    When I enqueue the message
    Then the album finishes processing
    And the summary is published to Obsidian
    And the PDF is published to the output directory
    When I request a retry
    Then the album finishes processing
    And the summary is published to Obsidian
    And the PDF is published to the output directory
    And the bot reacted with "👏"

Scenario: AI timeout is reported to the user
    Given the AI service is unresponsive
    When I enqueue the message
    Then the album processing fails
    And the bot replied with "Failed to process"

Scenario: Log command returns logs and traces after album processing
    When I enqueue the message
    Then the album is processed
    And the log content for the message contains logs and traces

Scenario: Log content is interleaved and chronologically sorted with short timestamps
    When I enqueue the message
    Then the album is processed
    And the log content is chronologically sorted
    And the log content uses short timestamp format
