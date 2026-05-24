using System.ComponentModel;
using System.Text.Json.Serialization;
using Paperoni.Ai;

public record AlbumNote(
    [property: Description("Title: YYYY-MM-DD Category Counterparty Tags")]
    string Title,

    [property: Description("Lowercase tags, 3-6 items")]
    string[] Tags,

    [property: Description("Document type: Rechnung, Quittung, Vertrag, Brief, Garantie, Mahnung")]
    string DocumentType,

    [property: Description("Counterparty - company or person")]
    string Counterparty,

    [property: JsonPropertyName("document_date")]
    string? DocumentDate,

    [property: Description("Importance: high, medium, or low")]
    string Importance,

    [property: Description("Amount if present (gross/total), null if not applicable")]
    decimal? Amount,

    [property: Description("Summary sentence")]
    string Summary,

    [property: Description("Full note content formatted in markdown")]
    string MarkdownBody
);

var Schema = typeof(AlbumNote);

var Prompt = $"""
Analysiere das folgende Dokument (Rechnung, Quittung, Vertrag, Brief, etc.) und extrahiere die wichtigsten Daten
für die private Verwaltung.

## Anforderungen an die Extraktion

### Frontmatter-Felder (YAML)
- `title`: Erstelle einen Titel nach folgendem Muster:
  **Format:** `YYYY-MM-DD Kategorie Gegenpart Tag1 Tag2`
  **Beispiel:** `2025-06-05 Auto Serer GmbH Werkstatt Rechnung`
  **Bestandteile:**
    - Dokumentdatum im Format YYYY-MM-DD
    - Die gewählte Kategorie (aus der Liste unten)
    - Name des Gegenparts (Firma oder Person)
    - 2-4 relevante Tags (ohne Bindestriche, Leerzeichen getrennt)
      → Maximal 8-10 Wörter insgesamt

- `document_date`: Das auf dem Dokument stehende Datum (NUR HINZUFÜGEN, WENN IM DOKUMENT EIN DATUM ZU SEHEN IST, SONST BITTE DATUM-UNBEKANNT EINTRAGEN!)
- `counterparty`: Vertragspartner / Aussteller / andere Partei (Firma oder Person)
- `document_type`: Art des Dokuments (z.B. "Rechnung", "Quittung", "Vertrag", "Brief", "Garantie", "Mahnung")
- `amount`: Falls ein Geldbetrag genannt wird – der **Endbetrag (Brutto)** als Zahl (sonst leer lassen)
- `importance`: "high", "medium" oder "low"
- `category`: Eine der folgenden Kategorien – "[[Auto]]", "[[Motorrad]]", "[[🌱 Gesundheit]]", "[[🧑‍🍳 Kochen]]", "[[🏠 Wohnung]]", "[[Bosch]]", "[[Software development]]", "[[💶 Finanzen]]", "[[Other]]"
- `tags`: Liste von relevanten Tags (3-6 Stück, klein geschrieben und keine Leerzeichen, z.B. rechnung, werkstatt, bezahlt, feder)

### Regeln für die Wichtigkeitsbewertung (importance)
- **high**: Betrag > 400 €, oder wichtige Verträge (Mietvertrag, Arbeitsvertrag), oder Garantie-/Gewährleistungsdokumente, oder Kündigungen/Mahnungen
- **medium**: Betrag 100 - 300 €, oder wiederkehrende Dokumente (Nebenkostenabrechnung), oder allgemeine Briefe mit Relevanz
- **low**: Kleinstbeträge, oder Werbung, oder rein informative Dokumente ohne finanzielle/vertragliche Relevanz

### Sonstige Regeln
- Bei Beträgen nur den **Brutto-Endbetrag** erfassen (keine Netto/MwSt.-Aufteilung)
- Nur Daten verwenden, die tatsächlich im Dokument stehen
- Fehlende Felder einfach leer lassen (z.B. `amount: `)
- Vor jeder Markdowntabellen IMMER eine leere Zeile einfügen!

Current date and time: {CurrentDate:yyyy-MM-dd HH:mm:ss}
{(Captions.Count > 0 ? $"User's instructions: {string.Join(" | ", Captions)}" : "")}
""";

Func<AlbumNote, string> GetFilename = note =>
{
    var safeTitle = MarkdownHelper.AutoFixDate(note.Title ?? "Unknown");
    return MarkdownHelper.SanitizeFilename(safeTitle);
};

Func<AlbumNote, string> Format = note =>
{
    var filename = GetFilename(note);
    var tagsStr = string.Join(", ", (note.Tags ?? []).Select(t => $"#{t.ToLower()}"));
    var amountStr = note.Amount.HasValue ? note.Amount.Value.ToString("F2") : "";
    return $"""
---
title: {filename}
tags: [{tagsStr}]
counterparty: {note.Counterparty}
document_type: {note.DocumentType}
importance: {note.Importance}
amount: {amountStr}
pdf: "[[{filename}.pdf]]"
---

# {note.Summary}

{note.MarkdownBody}
""";
};