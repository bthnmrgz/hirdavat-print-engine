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
- `examples/labeler-valid.json` - TopStick 8706 QR label payload.
- `examples/catalog-price-list-valid.json` - catalog price list payload.
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

Render a catalog price list and receive a public URL:

```bash
curl -X POST http://localhost:5159/render/catalog-price-list-url \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: local-dev-key" \
  --data-binary @examples/catalog-price-list-valid.json
```

## Data Contract

Endpoint names stay backwards-compatible, but the request body selects the layout with `document_type`:

- `quote` - teklif layout.
- `receipt` - tahsilat/tediyat makbuzu layout.
- `order_slip` - sipariş fişi layout.
- `cari_ledger` - cari ekstre layout.
- `labeler` - TopStick 8706 / 70x37mm / A4 3x8 QR etiket layout.

Catalog price lists use a separate endpoint and document type:

- Endpoint: `POST /render/catalog-price-list-url`.
- `document_type`: `catalog_price_list`.
- Response includes both `pdf_url` and `url` for server-side relays.
- `company.colors` may carry brand colors; the first color becomes primary and the second color becomes accent.
- `style.page_size` accepts `a4_portrait`, `a4_landscape`, `square`, and `story`.
- `style.price_emphasis` accepts `balanced`, `prominent`, and `subtle`.
- `style.missing_image_behavior=text_only` keeps missing-image products as text cards.
- `pages[]` may contain `cover`, `section`, or `products` pages.
- `pages[].products[].image_url` may carry an HTTP(S) raster product image URL. SVG and oversized images are skipped.
- If a page includes `generated_image.data_uri`, the image is rendered as a full page.
- Product pages are capped by `summary.items_per_page` (default `16`) so a 17th product must be sent as a continuation page.

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

- `document_type` must be `quote`, `receipt`, `order_slip`, `cari_ledger`, or `labeler`.
- `company.name` is required for `quote`, `receipt`, and `order_slip`.
- `quote` and `order_slip` require `items` or `table`.
- `receipt` requires `payments` or `table`.
- `cari_ledger` requires `cari.name` and root-level `columns`; `rows` may be empty.
- `labeler` requires root-level `labels`; it does not require `company`, `items`, or `table`.
- `catalog_price_list` is accepted only by `/render/catalog-price-list-url`; it requires root-level `pages` with at least one product page and one product.
- `customer` is optional. If omitted, null, or empty, the customer header block is hidden.
- For `quote`, `receipt`, `order_slip`, and `cari_ledger`, the document header repeats on every page. Multi-page PDFs show `Sayfa X/Y` centered at the bottom.
- `labeler` keeps its fixed label grid and does not render document headers or page numbers.

Bubble or any caller should send formatted visible strings for money and totals. The service validates and renders; it does not calculate KDV, discount, withholding, or grand totals.

Rows in `total_rows` and `payment_totals` may include `"hide_if_zero": true`. When set, values such as `"0"`, `"0,00"`, `"0,00 TL"`, or `"₺0.00"` are omitted from the rendered PDF.

Labeler fields:

```json
{
  "document_type": "labeler",
  "document_no": "ETIKET-001",
  "labels": [
    {
      "svg_kod": "<svg ...>...</svg>",
      "stok_adi": "Reyon Z - Sira 2 - Kat 5",
      "etiket_adedi": 1
    }
  ]
}
```

- `svg_kod` is rendered as SVG with QuestPDF SVG support, not as a raster logo image.
- `svg_kod` should be the real scanner-tested QR SVG generated by the caller; do not use the sample SVG as production QR data.
- `stok_adi` is printed to the right of the QR code and may be blank.
- `etiket_adedi` repeats the same label; pages flow automatically after 24 labels.

Bubble-safe labeler raw body:

When Bubble's dynamic JSON body escapes list object quotes, avoid nested JSON. The simplest Bubble API Connector setup is `Raw` body with a line format selected through query parameters:

```text
POST /render/order-slip-url?body_mode=labeler_lines&document_no=ETIKET-001&row_delimiter=__ROW__&field_delimiter=__FIELD__
Content-Type: text/plain

<svg ...></svg>__FIELD__Reyon Z - Sira 2 - Kat 5__FIELD__1__ROW__<svg ...></svg>__FIELD__Reyon A__FIELD__2
```

- In Bubble, set Body type to `Raw`.
- Build the raw body from the label list with `:formatted as text`.
- Content per list item should be `svg_kod + "__FIELD__" + stok_adi + "__FIELD__" + etiket_adedi`.
- Delimiter between list items should match `row_delimiter`, for example `__ROW__`.
- `etiket_adedi` may be empty; empty values default to `1`.

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

curl -X POST http://localhost:5159/render/order-slip-url \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: local-dev-key" \
  --data-binary @examples/labeler-valid.json

curl -X POST http://localhost:5159/render/catalog-price-list-url \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: local-dev-key" \
  --data-binary @examples/catalog-price-list-valid.json
```

## Build

```bash
/Users/batuhanmerguz/.dotnet/dotnet build questpdf-service/HirdavatQuestPdf.Api.csproj
```
