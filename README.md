# Hirdavat QuestPDF API

ASP.NET 8 + QuestPDF service for rendering Hirdavat print JSON as PDF.

The old Bubble plugin/browser-print layer has been removed. This repository now keeps the server-side PDF renderer, deployment files, and sample JSON payloads.

## Files

- `questpdf-service/` - ASP.NET + QuestPDF PDF API.
- `examples/order-slip-valid.json` - order slip payload.
- `examples/order-slip-a5.json` - A5 order slip payload.
- `examples/order-slip-custom-table.json` - custom table payload.
- `examples/order-slip-customerless.json` - payload without a customer block.
- `examples/quote-valid.json` - quote payload.
- `examples/receipt-valid.json` - receipt payload.
- `examples/cari-ledger-valid.json` - cari ledger payload.
- `docker-compose.yml` - local/production compose entrypoint.
- `deploy/Caddyfile` - Caddy reverse proxy config.
- `launch/` - local macOS launchd helpers.
- `docs/` - hosting, runbook, and integration notes.

## Run Locally

```bash
QUESTPDF_API_KEY=local-dev-key /Users/batuhanmerguz/.dotnet/dotnet run --project questpdf-service/HirdavatQuestPdf.Api.csproj --urls http://localhost:5159
```

Health check:

```bash
curl -i http://localhost:5159/health
```

Render a PDF binary:

```bash
curl -X POST http://localhost:5159/render/order-slip \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: local-dev-key" \
  --data-binary @examples/order-slip-valid.json \
  --output /tmp/order-slip-questpdf.pdf
```

Render and receive a public URL:

```bash
curl -X POST http://localhost:5159/render/order-slip-url \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: local-dev-key" \
  --data-binary @examples/quote-valid.json
```

## Data Contract

Endpoint names stay backwards-compatible, but the request body selects the layout with `document_type`:

- `quote` - teklif layout.
- `receipt` - tahsilat/tediyat makbuzu layout.
- `order_slip` - sipariş fişi layout.
- `cari_ledger` - cari ekstre layout.

Common fields:

```json
{
  "document_type": "quote",
  "document_no": "OZG2026-34",
  "date": "15/05/2026",
  "title": "Teklif",
  "company": {},
  "customer": null,
  "print_style": {}
}
```

Validation rules:

- `document_type` must be `quote`, `receipt`, `order_slip`, or `cari_ledger`.
- `company.name` is required for `quote`, `receipt`, and `order_slip`.
- `quote` and `order_slip` require `items` or `table`.
- `receipt` requires `payments` or `table`.
- `cari_ledger` requires `cari.name` and root-level `columns`; `rows` may be empty.
- `customer` is optional. If omitted, null, or empty, the customer header block is hidden.

Bubble or any caller should send formatted visible strings for money and totals. The service validates and renders; it does not calculate KDV, discount, withholding, or grand totals.

Rows in `total_rows` and `payment_totals` may include `"hide_if_zero": true`. When set, values such as `"0"`, `"0,00"`, `"0,00 TL"`, or `"₺0.00"` are omitted from the rendered PDF.

## Examples

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

curl -X POST http://localhost:5159/render/order-slip-url \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: local-dev-key" \
  --data-binary @examples/cari-ledger-valid.json
```

## Build

```bash
/Users/batuhanmerguz/.dotnet/dotnet build questpdf-service/HirdavatQuestPdf.Api.csproj
```
