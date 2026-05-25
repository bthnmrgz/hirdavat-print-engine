# Cari Ledger QuestPDF Support Spec

## Scope

Add support for `document_type: "cari_ledger"` to the Printer v2 / QuestPDF API.

Do not change the behavior, validation rules, layout, sample payloads, or output of the existing document types:

- `quote`
- `receipt`
- `order_slip`

This work must be additive. Existing document types must continue to render exactly as before.

## Integration Target

Analytics Worker sends cari ledger print requests to the existing URL-render endpoint:

```text
POST /render/order-slip-url
Content-Type: application/json
X-Api-Key: <QUESTPDF_API_KEY>
```

The endpoint name stays unchanged for backward compatibility. The request body selects the layout through `document_type`.

The direct PDF endpoint should also accept the same payload:

```text
POST /render/order-slip
```

## Incoming Payload Shape

The Worker sends a payload like this:

```json
{
  "document_type": "cari_ledger",
  "document_no": "cari-ledger-<cari_id>",
  "generated_at": "2026-05-25T12:00:00.000Z",
  "report_date": "2026-05-25T12:00:00.000Z",
  "scope": "current_view",
  "title": "Cari Ekstre",
  "cari": {
    "id": "<cari_id>",
    "code": "ABC-001",
    "name": "ABC Hirdavat",
    "short_title": "ABC",
    "type": "Musteri"
  },
  "view": {
    "movement_mode": "paged",
    "movement_limit": 50,
    "movement_cursor": 0,
    "density": "compact",
    "movement_count": 50,
    "page_count": 50,
    "page_remaining": 120
  },
  "filters": {
    "date_from": "2026-05-01",
    "date_to": "2026-05-31",
    "entry_kind": "debt",
    "summary": "Son hareket sayfasi / Baslangic: 2026-05-01 / Borclar"
  },
  "metrics": [
    { "label": "Cari Bakiye TL", "value": "12.345 TL" },
    { "label": "Sayfa borc", "value": "5.000 TL" }
  ],
  "findings": [
    "Hizli gorunum; metrikler sadece yuklenen cari hareket sayfasi icindir."
  ],
  "columns": [
    { "key": "date", "label": "Tarih" },
    { "key": "documentNo", "label": "Evrak No" },
    { "key": "documentType", "label": "Evrak Tipi" },
    { "key": "debt", "label": "Borc", "align": "right" },
    { "key": "credit", "label": "Alacak", "align": "right" },
    { "key": "balance", "label": "Bakiye", "align": "right" },
    { "key": "dueDate", "label": "Vade" },
    { "key": "dueStatus", "label": "Durum" },
    { "key": "detail", "label": "Detay" }
  ],
  "rows": [
    {
      "date": "25.05.2026",
      "documentNo": "F-123",
      "documentType": "Satis Faturasi",
      "debt": "1.000 TL",
      "credit": "0 TL",
      "balance": "1.000 TL",
      "dueDate": "30.05.2026",
      "dueStatus": "5 gun var",
      "detail": "-"
    }
  ]
}
```

## Implementation Requirements

### 1. Add `cari_ledger` as an allowed document type

Update the document-type allowlist so `cari_ledger` is accepted.

Do not alter existing behavior for:

- `quote`
- `receipt`
- `order_slip`

Update only the error message if needed so it includes `cari_ledger`.

### 2. Keep existing validation untouched

The existing validation rules for `quote`, `receipt`, and `order_slip` must stay exactly the same.

Add a separate validation branch for `cari_ledger`:

- `cari.name` is required.
- `columns` must contain at least one column.
- Each column must have a non-empty `key`.
- `rows` may be empty or missing; in that case the PDF should render an empty-state row.
- `company.name`, `items`, `payments`, and `table` must not be required for `cari_ledger`.

### 3. Add payload model fields

Add fields to `PrintDocumentPayload` for the cari ledger payload:

```csharp
[JsonPropertyName("generated_at")]
public string? GeneratedAt { get; init; }

[JsonPropertyName("report_date")]
public string? ReportDate { get; init; }

[JsonPropertyName("scope")]
public string? Scope { get; init; }

[JsonPropertyName("cari")]
public CariLedgerParty? Cari { get; init; }

[JsonPropertyName("view")]
public CariLedgerView? View { get; init; }

[JsonPropertyName("filters")]
public CariLedgerFilters? Filters { get; init; }

[JsonPropertyName("metrics")]
public List<LabeledValueRow>? Metrics { get; init; }

[JsonPropertyName("findings")]
public List<string>? Findings { get; init; }

[JsonPropertyName("columns")]
public List<CariLedgerColumn>? Columns { get; init; }

[JsonPropertyName("rows")]
public List<DocumentTableRow>? Rows { get; init; }
```

Suggested new classes:

```csharp
sealed class CariLedgerParty
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("code")]
    public string? Code { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("short_title")]
    public string? ShortTitle { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }
}

sealed class CariLedgerView
{
    [JsonPropertyName("movement_mode")]
    public string? MovementMode { get; init; }

    [JsonPropertyName("movement_limit")]
    public int? MovementLimit { get; init; }

    [JsonPropertyName("movement_cursor")]
    public int? MovementCursor { get; init; }

    [JsonPropertyName("density")]
    public string? Density { get; init; }

    [JsonPropertyName("movement_count")]
    public int? MovementCount { get; init; }

    [JsonPropertyName("page_count")]
    public int? PageCount { get; init; }

    [JsonPropertyName("page_remaining")]
    public int? PageRemaining { get; init; }
}

sealed class CariLedgerFilters
{
    [JsonPropertyName("date_from")]
    public string? DateFrom { get; init; }

    [JsonPropertyName("date_to")]
    public string? DateTo { get; init; }

    [JsonPropertyName("entry_kind")]
    public string? EntryKind { get; init; }

    [JsonPropertyName("summary")]
    public string? Summary { get; init; }
}

sealed class CariLedgerColumn
{
    [JsonPropertyName("key")]
    public string? Key { get; init; }

    [JsonPropertyName("label")]
    public string? Label { get; init; }

    [JsonPropertyName("align")]
    public string? Align { get; init; }

    [JsonPropertyName("width")]
    public double? Width { get; init; }
}
```

### 4. Add a cari ledger render branch

Add a branch to the body composition logic:

```csharp
case "cari_ledger":
    ComposeCariLedger(container);
    break;
```

Do not modify the existing `quote`, `receipt`, or default `order_slip` branches.

### 5. Default title

Add only this new default title:

```csharp
"cari_ledger" => "Cari Ekstre"
```

Do not change existing default titles.

### 6. Cari ledger layout

The PDF should include:

- Title: `Cari Ekstre`
- Cari name, bold and prominent.
- Cari id, code, short title, type when present.
- Report date / generated date when present.
- Filter summary when present.
- Current view info:
  - `movement_mode`
  - movement count
  - remaining count for paged views
- Metrics section.
- Movement table using the incoming `columns` and `rows`.
- Findings section when present.

Rows should use the incoming formatted strings as-is. The service must not calculate balances, totals, due status, or money formatting.

### 7. Table behavior

Use `columns` as the source of truth:

- Header text: `label`, fallback to `key`.
- Cell lookup: row value by `key`.
- `align: "right"` means right-aligned cell.
- `align: "center"` means centered cell.
- Missing values render as empty string.

If there are no rows, render one full-width row:

```text
Cari hareketi yok.
```

### 8. File name fallback

For `cari_ledger`, generated URL files should use a safe cari-ledger fallback when `document_no` is missing:

```text
cari-ledger
```

Do not change file naming behavior for existing document types.

### 9. Example payload

Add only a new cari-ledger example file:

```text
examples/cari-ledger-valid.json
```

Do not modify existing example files for `quote`, `receipt`, or `order_slip`.

### 10. Documentation

If documentation is updated, only document the new `cari_ledger` support.

Do not rewrite or reinterpret existing document types.
Do not change existing payload examples for other document types.

## Local Validation

Run the service locally:

```bash
QUESTPDF_API_KEY=local-dev-key dotnet run --project questpdf-service/HirdavatQuestPdf.Api.csproj --urls http://localhost:5159
```

Health check:

```bash
curl -i http://localhost:5159/health
```

PDF URL test:

```bash
curl -i -X POST http://localhost:5159/render/order-slip-url \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: local-dev-key" \
  --data @examples/cari-ledger-valid.json
```

Expected success shape:

```json
{
  "ok": true,
  "pdf_url": "http://localhost:5159/files/....pdf",
  "file_name": "....pdf",
  "content_type": "application/pdf",
  "size_bytes": 12345
}
```

## Negative Tests

Add tests or manual checks for:

- `document_type: "cari_ledger"` with missing `cari.name` returns 400.
- `document_type: "cari_ledger"` with empty `columns` returns 400.
- Missing `X-Api-Key` returns 401.
- Existing `quote`, `receipt`, and `order_slip` example payloads still work without output contract changes.

## Acceptance Criteria

- `document_type: "cari_ledger"` produces a PDF through `/render/order-slip-url`.
- Response includes `pdf_url`.
- Existing document types are not changed.
- Existing examples for other document types are not modified.
- API key behavior is unchanged.
- Analytics Worker payload works without changing the Worker.
