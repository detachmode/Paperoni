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

    [property: Description("Das auf dem Dokument stehende Datum, wenn vorhanden")]
    string? DocumentDate,

    [property: Description("Importance: high, medium, or low")]
    string Importance,

    [property: Description("Die 'Area' (größere Kategory / Lebensbereich) die dem Dokument zuzuordnen ist")]
    string Area,

    [property: Description("Falls ein Geldbetrag genannt wird (der **Endbetrag (Brutto)**), wenn nicht leer lassen)")]
    decimal? Amount,

    [property: Description("Markdown Body mit der Struktur wie angegeben in den Regeln")]
    string MarkdownBody
);

var Schema = typeof(AlbumNote);

var Prompt = $"""
Analysiere das folgende Dokument (Rechnung, Quittung, Vertrag, Brief, etc.) und extrahiere die wichtigsten Daten
für die private Verwaltung.

## Anforderungen an die Extraktion

###  Rules
- `title`: Erstelle einen Titel nach folgendem Muster:
  **Format:** `YYYY-MM-DD Kategorie Gegenpart Tag1 Tag2`
  **Beispiel:** `2025-06-05 Auto Serer GmbH Werkstatt Rechnung`
  **Bestandteile:**
    - Dokumentdatum im Format YYYY-MM-DD
    - Die gewählte Kategorie (aus der Liste unten)
    - Name des Gegenparts (Firma oder Person)
    - 2-4 relevante Tags (ohne Bindestriche, Leerzeichen getrennt)
      → Maximal 8-10 Wörter insgesamt

- `DocumentDate`: Das auf dem Dokument stehende Datum
   Wichtig! NUR HINZUFÜGEN, WENN IM DOKUMENT EIN DATUM ZU SEHEN IST, SONST BITTE LEER LASSEN !
- `Counterparty`: Vertragspartner / Aussteller / andere Partei (Firma oder Person)
- `Area`: Eine der folgenden Areas: Auto, Motorrad, 🌱 Gesundheit, 🧑‍🍳 Kochen, 🏠 Wohnung, Bosch (meine Arbeitgeber), Software development (Hobby Projekte), 💶 Finanzen, 🧐 Other
- `Tags`: Liste von relevanten Tags (3-6 Stück, z.B. rechnung, werkstatt, bezahlt, feder)

### Regeln für die Wichtigkeitsbewertung (importance)
- **high**: Betrag > 400 €, oder wichtige Verträge (Mietvertrag, Arbeitsvertrag), oder Garantie-/Gewährleistungsdokumente, oder Kündigungen/Mahnungen
- **medium**: Betrag 100 - 300 €, oder wiederkehrende Dokumente (Nebenkostenabrechnung), oder allgemeine Briefe mit Relevanz
- **low**: Kleinstbeträge, oder Werbung, oder rein informative Dokumente ohne finanzielle/vertragliche Relevanz

### Sonstige Regeln
- Bei Beträgen nur den **Brutto-Endbetrag** erfassen (keine Netto/MwSt.-Aufteilung)
- Nur Daten verwenden, die tatsächlich im Dokument stehen
- Fehlende Felder einfach leer lassen (z.B. `amount: `)
- Vor jeder Markdowntabellen IMMER eine leere Zeile einfügen!

### Andere Fakten
Aktuelles Datum: {CurrentDate:yyyy-MM-dd HH:mm:ss}
{(Captions.Count > 0 ? $"Zusätzliche Anweisungen vom Benutzer: {string.Join(" | ", Captions)}" : "")}

### MarkdownBody
Der MarkdownBody soll folgendes Layout haben:

# Zusammenfassung
Kurz und prägnante zusammenfassung des dokuments. Ca. 50 Wörter

# Wichtige Fakten
Extrahiere wichtige Fakten aus dem Dokumente.
Benutze Markdown Tabellen, wenn im Dokument Daten in Tabellen vorliegen, oder im Fall einer Rechnung,
dann die einzelnen Artikel als Tabelle

""";

Func<AlbumNote, string> GetFilename = note =>
{
    var safeTitle = MarkdownHelper.AutoFixDate(note.Title ?? "Unknown");
    return MarkdownHelper.SanitizeFilename(safeTitle);
};

Func<AlbumNote, string> Format = note =>
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
};
