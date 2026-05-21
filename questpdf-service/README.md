# Hirdavat QuestPDF Service

API for rendering Hirdavat print JSON as real PDFs with QuestPDF.

The route names stay backwards-compatible:

- `POST /render/order-slip` returns a PDF binary.
- `POST /render/order-slip-url` writes the PDF and returns `{ pdf_url }`.

The request body selects the layout with `document_type`: `quote`, `receipt`, or `order_slip`.

## Run locally

Requires the .NET 8 SDK or newer.

```bash
QUESTPDF_API_KEY=local-dev-key dotnet run --project questpdf-service/HirdavatQuestPdf.Api.csproj --urls http://localhost:5159
```

## Test with curl

```bash
curl -X POST http://localhost:5159/render/order-slip \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: local-dev-key" \
  --data-binary @examples/order-slip-valid.json \
  --output /tmp/order-slip-questpdf.pdf
```

Other sample payloads:

```bash
curl -X POST http://localhost:5159/render/order-slip-url \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: local-dev-key" \
  --data-binary @examples/quote-valid.json

curl -X POST http://localhost:5159/render/order-slip-url \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: local-dev-key" \
  --data-binary @examples/receipt-valid.json

curl -X POST http://localhost:5159/render/order-slip-url \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: local-dev-key" \
  --data-binary @examples/order-slip-customerless.json
```

## Payload rules

- `company.name` is required.
- `customer` is optional; when empty, the customer header block is hidden.
- `quote` and `order_slip` require `items` or `table`.
- `receipt` requires `payments` or `table`.
- Callers send formatted totals and visible amount strings. The service validates and renders them, but does not calculate tax, discount, withholding, or grand totals.

Quote-specific fields:

- `detail_fields`: left-side label/value rows below the item table.
- `total_rows`: right-side totals; set `emphasis: true` for the final line.
- `signature`: `left_text` and `right_text` blocks near the footer.

Receipt-specific fields:

- `payments`: payment rows.
- `payment_totals`: right-side payment type totals and final amount.

## Custom tables

The default item table is still used when the payload only has `items`.
To control the QuestPDF table from JSON, send a top-level `table` object:

```json
{
  "table": {
    "columns": [
      { "key": "date", "title": "Tarih", "width": 16, "align": "center" },
      { "key": "description", "title": "Açıklama", "width": 46 },
      { "key": "amount", "title": "Tutar", "width": 20, "align": "right" }
    ],
    "rows": [
      {
        "date": "20/05/2026",
        "description": "Sipariş tahsilatı",
        "amount": "1.250,00 TL"
      }
    ]
  }
}
```

- `columns[].key` is required and reads the matching field from each row.
- `columns[].title` is the visible header; if omitted, the key is used.
- `columns[].width` is a relative width. For example `20` and `40` means the second column is twice as wide.
- `columns[].align` supports `left`, `center`, and `right`.
- If `table.columns` is sent without `table.rows`, the service uses the existing `items` list as the row source. Useful item keys include `code`, `name`, `description`, `name_description`, `quantity`, `unit`, `unit_price`, `price`, `amount`, `line_total`, `total`, `vat_rate`, `kdv`, `tax_rate`, `note`, `explanation`, and `aciklama`.

Full example:

```bash
curl -X POST http://localhost:5159/render/order-slip \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: local-dev-key" \
  --data-binary @examples/order-slip-custom-table.json \
  --output /tmp/custom-table-questpdf.pdf
```

## Production configuration

The render endpoints require `X-Api-Key` when the service is configured for local or production use. `/health` stays public.

Environment variables:

- `QUESTPDF_API_KEY` - required secret for `/render/order-slip` and `/render/order-slip-url`.
- `ALLOWED_ORIGINS` - comma-separated CORS allowlist. If blank, local development allows any origin.
- `PDF_RETENTION_HOURS` - generated local PDF cleanup window, default `24`; set `0` to disable cleanup.

Docker deployment files live at the repo root:

- `docker-compose.yml`
- `.env.example`
- `questpdf-service/Dockerfile`

See `docs/HOSTINGER_KVM4_DEPLOYMENT.md` for Hostinger KVM4 deployment.

## License note

The service sets `QuestPDF.Settings.License = LicenseType.Community` for evaluation. Check QuestPDF licensing before production use, especially if the organization is above the community-license revenue threshold.
