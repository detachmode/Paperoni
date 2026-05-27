using System.ComponentModel;
using System.Text.Json.Serialization;
using Paperoni.Ai;

public record AlbumNote(
    [property: Description("Title: YYYY-MM-DD Category Counterparty Tags")]
    string Title,

    [property: Description("Lowercase tags, 3-6 items")]
    string[] Tags,

    [property: Description("Document type: Invoice, Receipt, Contract, Letter, Warranty, Reminder")]
    string DocumentType,

    [property: Description("Counterparty - company or person")]
    string Counterparty,

    [property: Description("Date as shown on the document, if present")]
    string? DocumentDate,

    [property: Description("Importance: high, medium, or low")]
    string Importance,

    [property: Description("Area - the broader category or life area this document belongs to")]
    string Area,

    [property: Description("Total amount (gross/final) if mentioned, otherwise leave empty")]
    decimal? Amount,

    [property: Description("Markdown body following the structure specified in the rules")]
    string MarkdownBody
);

var Schema = typeof(AlbumNote);

var Prompt = $"""
Analyse the following document (invoice, receipt, contract, letter, etc.) and extract the key data
for personal record-keeping.

## Extraction requirements

### Rules
- `title`: Create a title following this pattern:
  **Format:** `YYYY-MM-DD Category Counterparty Tag1 Tag2`
  **Example:** `2025-06-05 Auto Smith Auto Repair Invoice`
  **Components:**
    - Document date in YYYY-MM-DD format
    - Chosen category (from the list below)
    - Name of counterparty (company or person)
    - 2-4 relevant tags (no hyphens, space-separated)
      → Maximum 8-10 words total

- `DocumentDate`: The date as shown on the document
   IMPORTANT! ONLY ADD IF A DATE IS VISIBLE ON THE DOCUMENT, OTHERWISE LEAVE EMPTY!
- `Counterparty`: Contracting party / issuer / other party (company or person)
- `Area`: One of the following: Car, Motorcycle, Health, Cooking, Housing, Work, Finance, Other
- `Tags`: List of relevant tags (3-6 items, e.g. invoice, repair, paid, warranty)

### Importance rules
- **high**: Amount > 400, or important contracts (lease, employment), or warranty/guarantee documents, or terminations/reminders
- **medium**: Amount 100-300, or recurring documents (utility bills), or general letters with relevance
- **low**: Small amounts, or advertising, or purely informational documents without financial/contractual relevance

### Other rules
- Only capture the **gross/final amount** (no net/VAT breakdown)
- Only use data that is actually present in the document
- Leave missing fields empty (e.g. `amount: `)
- Always add an empty line before Markdown tables!

### Context
Current date: {CurrentDate:yyyy-MM-dd HH:mm:ss}
{(Captions.Count > 0 ? $"Additional instructions from user: {string.Join(" | ", Captions)}" : "")}

### MarkdownBody
The MarkdownBody should have this layout:

# Summary
Short and concise summary of the document. Approx. 50 words

# Key Facts
Extract key facts from the document.
Use Markdown tables when the document contains tabular data, or in the case of an invoice,
list the individual items as a table

""";

string GetFilename(AlbumNote note)
{
	Log("The AI returned following response:");
	Log(note);
    if (string.IsNullOrWhiteSpace(note.Title))
    {
        throw new InvalidOperationException("Title is empty — the LLM must provide a title.");
    }

    var safeTitle = MarkdownHelper.AutoFixDate(note.Title);
    return MarkdownHelper.SanitizeFilename(safeTitle);
}

string Format(AlbumNote note)
{
    var filename = GetFilename(note);
    var tagList = string.Join("\n",
        (note.Tags ?? [])
        .Select(t => t.ToLower().Replace(" ", "-"))
        .Select(t => $"""  - "{t}" """)
        );
    var amountStr = note.Amount.HasValue ? $"amount: {note.Amount.Value:F2}" : "";
    return $"""
---
pdf: "[[{filename}.pdf]]"
counterparty: {note.Counterparty}
document_type: {note.DocumentType}
importance: {note.Importance}
{amountStr}
parent:
  - "[[{note.Area}]]"
tags:
{tagList}
---

{note.MarkdownBody}
""";
}
