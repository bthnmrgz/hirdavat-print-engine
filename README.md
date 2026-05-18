# Hirdavat Print Engine

Template-based Bubble print plugin v2. This version does not clone Bubble page DOM. It reads `document_json`, validates a known data contract, and produces controlled HTML/CSS for print.

## Plugin Shape

Plugin name suggestion:

`Hirdavat Print Engine`

Element fields:

- `instance_id` - text id for multiple print elements on the same page.
- `document_json` - text JSON payload.
- `auto_render_preview` - yes/no, renders a compact preview inside the element.
- `debug_mode` - yes/no, logs parse/layout state to the console.

Element states:

- `is_valid` - yes/no.
- `error_message` - text.
- `document_type` - text, currently `order_slip`.
- `item_count` - number.
- `last_rendered_html` - text, for debugging.

Element workflow action:

- `Open Print Preview`
- Field: `document_json_override` as optional text.

## Files

- `src/hirdavat-print-engine.js` - shared engine. Add this as the Bubble plugin shared/frontend script before element and action code.
- `bubble/element_initialize.js` - element initialize code.
- `bubble/element_update.js` - element update code.
- `bubble/action_open_print_preview.js` - element action code.
- `examples/order-slip-valid.json` - valid order slip payload.
- `tests/engine.test.js` - Node smoke tests for validation and rendering.

## Data Contract

The first production template supports only:

```json
{
  "document_type": "order_slip",
  "document_no": "SP-123",
  "date": "18/05/2026",
  "title": "Sipariş Fişi",
  "company": {
    "name": "Özgünbora Teknik Hırd. ve Büro Kırt. San. Tic. Ltd. Şti.",
    "address": "...",
    "phone": "...",
    "email": "...",
    "logo_url": "..."
  },
  "customer": {
    "name": "...",
    "address": "...",
    "phone": "...",
    "tax_office": "...",
    "tax_no": "..."
  },
  "order": {
    "status": "Hazırlanacak",
    "delivery_address": "...",
    "shipping_method": "...",
    "note": "..."
  },
  "items": [
    {
      "code": "CX0070",
      "name": "KAYNAK KABLO JAKI ADAPTORU",
      "description": "",
      "quantity": "2",
      "unit": "Adet",
      "status": "Hazırlanacak",
      "note": ""
    }
  ]
}
```

Validation errors are returned when any required field is missing:

- `document_type`
- `items`
- `company.name`
- `customer.name`

Optional missing fields render as empty text.

## Rendering Notes

- Uses a real table for product rows.
- Uses `@page { margin: 10mm; }`.
- Leaves paper size and orientation to the browser print dialog.
- Repeats `thead` on printed pages where the browser supports it.
- Does not use Bubble DOM, viewport size, Chrome zoom, or Bubble responsive layout.
- The preview window includes `Yazdır`, `Kapat`, and `Kompakt görünüm`.
- Browser header/footer cannot be controlled by the plugin; the user must disable it in the print dialog.
- Supports optional `print_style` values in `document_json` for page margin, logo size, font size, and header spacing.

## Local Test

```bash
node tests/engine.test.js
```
