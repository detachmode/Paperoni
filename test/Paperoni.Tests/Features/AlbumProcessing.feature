Feature: Album Processing
As a user sending photos to Telegram
I want albums to be processed automatically
So that I receive AI summaries and processed PDFs

Background:
    Given the system is configured for integration testing
    And the pipeline script is:
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
    And the dashboard showed "🤖 AI reading"
    And the dashboard showed "🤖 AI thinking"
    And the dashboard showed "🤖 AI is formulating the final output"
    And the dashboard showed "📄 Creating PDF"
    And the dashboard showed "📤 Publishing"
    And the last bot reply starts with "Done in"
    And the last bot reply contains "Paperoni v"
    And the bot reacted with "👏"
    And the trace log contains expected traces
    And the trace log is written to the correct album directory
    And the dashboard was deleted

Scenario: Multi-photo album produces a PDF with correct page count
    Given the album has 3 photos
    When I enqueue the message
    Then the album is processed
    And the AI summary mentions "title: Lorem ipsum"
    And the PDF has 3 pages
    And the trace log shows 3 images were processed
    And the last bot reply starts with "Done in"
    And the last bot reply contains "Paperoni v"
    And the bot reacted with "👏"
    And the trace log contains expected traces

Scenario: Album retry re-processes and cleans old published files
    When I enqueue the message
    Then the album finishes processing
    And the summary is published to Obsidian
    And the PDF is published to the output directory
    When I request a retry
    Then the album finishes processing
    And the old published files were cleaned before re-publishing
    And the old trace log was cleaned before re-processing
    And the summary is published to Obsidian
    And the PDF is published to the output directory

Scenario: AI timeout is reported to the user
    Given the AI service is unresponsive
    When I enqueue the message
    Then the album processing fails
    And the bot replied with "Failed to process"
    And the dashboard showed "❌ Failed"
    And no PDF was created in the working directory
    And no files were published to the output directory

Scenario: Log command returns logs and traces after album processing
    When I enqueue the message
    Then the album is processed
    And the log content for the message contains logs and traces

Scenario: Log content is interleaved and chronologically sorted with short timestamps
    When I enqueue the message
    Then the album is processed
    And the log content is chronologically sorted
    And the log content uses short timestamp format

Scenario: Diagnostic shows correct album and counts multiple requests
    When I enqueue the message
    Then the album is processed
    When I request diagnostics for the message
    Then the diagnostic was shown for album 42
    When I request diagnostics for the message
    Then the diagnostic was shown 2 times
