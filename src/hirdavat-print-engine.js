(function(root, factory) {
  if (typeof module === "object" && module.exports) {
    module.exports = factory();
  } else {
    root.HirdavatPrintEngine = factory();
  }
})(typeof globalThis !== "undefined" ? globalThis : this, function() {
  "use strict";

  var VERSION = "0.1.2";
  var registry = {};

  function isObject(value) {
    return value !== null && typeof value === "object" && !Array.isArray(value);
  }

  function asText(value) {
    if (value === null || value === undefined) return "";
    return String(value);
  }

  function trimText(value) {
    return asText(value).trim();
  }

  function escapeHtml(value) {
    return asText(value)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;")
      .replace(/'/g, "&#39;");
  }

  function escapeAttr(value) {
    return escapeHtml(value).replace(/`/g, "&#96;");
  }

  function firstText() {
    for (var i = 0; i < arguments.length; i += 1) {
      var value = trimText(arguments[i]);
      if (value) return value;
    }
    return "";
  }

  function numberInRange(value, fallback, min, max) {
    var parsed = Number(value);
    if (!isFinite(parsed)) return fallback;
    return Math.min(Math.max(parsed, min), max);
  }

  function normalizePaperSize(value) {
    value = trimText(value).toLowerCase();
    if (value === "a5" || value === "a5 portrait") return "a5";
    return "a4";
  }

  function parseDocumentJson(input) {
    if (isObject(input)) return input;

    var text = trimText(input);
    if (!text) {
      throw new Error("document_json bos olamaz.");
    }

    try {
      return JSON.parse(text);
    } catch (error) {
      throw new Error("document_json gecerli JSON degil: " + error.message);
    }
  }

  function normalizeParty(value) {
    value = isObject(value) ? value : {};

    return {
      name: trimText(value.name),
      address: trimText(value.address),
      phone: trimText(value.phone),
      email: trimText(value.email),
      city: trimText(value.city),
      district: trimText(value.district),
      tax_office: trimText(value.tax_office),
      tax_no: trimText(value.tax_no),
      tckn: trimText(value.tckn),
      document_type: trimText(value.document_type),
      delivery_address: trimText(value.delivery_address),
      logo_url: trimText(value.logo_url)
    };
  }

  function normalizeOrder(value) {
    value = isObject(value) ? value : {};

    return {
      status: trimText(value.status),
      delivery_address: trimText(value.delivery_address),
      shipping_method: trimText(value.shipping_method),
      note: trimText(value.note)
    };
  }

  function normalizeItem(item, index) {
    item = isObject(item) ? item : {};

    return {
      line_no: firstText(item.line_no, item.no, index + 1),
      code: trimText(item.code),
      name: trimText(item.name),
      description: trimText(item.description),
      quantity: trimText(item.quantity),
      unit: trimText(item.unit),
      vat_rate: firstText(item.vat_rate, item.kdv, item.tax_rate),
      status: trimText(item.status),
      note: trimText(item.note),
      explanation: firstText(item.explanation, item.aciklama, item.note)
    };
  }

  function normalizePrintStyle(value) {
    value = isObject(value) ? value : {};
    var paperSize = normalizePaperSize(firstText(value.paper_size, value.page_size, value.format));
    var isA5 = paperSize === "a5";

    return {
      paper_size: paperSize,
      page_margin_mm: numberInRange(value.page_margin_mm, isA5 ? 6 : 10, 3, 20),
      logo_width_mm: numberInRange(value.logo_width_mm, isA5 ? 25 : 32, 16, 60),
      logo_height_mm: numberInRange(value.logo_height_mm, isA5 ? 9 : 12, 6, 28),
      body_font_px: numberInRange(value.body_font_px, isA5 ? 5.2 : 7.2, 4.8, 11),
      table_font_px: numberInRange(value.table_font_px, isA5 ? 5.1 : 7.1, 4.8, 10),
      header_gap_mm: numberInRange(value.header_gap_mm, isA5 ? 9 : 15, 4, 28),
      document_width_mm: numberInRange(value.document_width_mm, isA5 ? 136 : 190, 90, 210),
      header_meta_width_mm: numberInRange(value.header_meta_width_mm, isA5 ? 18 : 24, 14, 36),
      header_gap_column_mm: numberInRange(value.header_gap_column_mm, isA5 ? 7 : 12, 3, 24),
      col_code_mm: numberInRange(value.col_code_mm, isA5 ? 20 : 25, 14, 36),
      col_qty_mm: numberInRange(value.col_qty_mm, isA5 ? 9 : 16, 7, 22),
      col_unit_mm: numberInRange(value.col_unit_mm, isA5 ? 11 : 17, 8, 24),
      col_vat_mm: numberInRange(value.col_vat_mm, isA5 ? 9 : 13, 7, 20),
      col_explanation_mm: numberInRange(value.col_explanation_mm, isA5 ? 24 : 46, 14, 64),
      table_cell_padding_y_mm: numberInRange(value.table_cell_padding_y_mm, isA5 ? 1.1 : 2, 0.6, 4),
      table_cell_padding_x_mm: numberInRange(value.table_cell_padding_x_mm, isA5 ? 0.9 : 1.4, 0.5, 3)
    };
  }

  function normalizeOrderSlip(raw) {
    raw = isObject(raw) ? raw : {};

    return {
      document_type: trimText(raw.document_type),
      document_no: trimText(raw.document_no),
      date: trimText(raw.date),
      title: firstText(raw.title, "Sipariş Fişi"),
      company: normalizeParty(raw.company),
      customer: normalizeParty(raw.customer),
      order: normalizeOrder(raw.order),
      print_style: normalizePrintStyle(raw.print_style || raw.style),
      items: Array.isArray(raw.items) ? raw.items.map(normalizeItem) : []
    };
  }

  function validateOrderSlip(raw, normalized) {
    if (trimText(raw.document_type) !== "order_slip") {
      return "document_type 'order_slip' olmalidir.";
    }

    if (!Array.isArray(raw.items)) {
      return "items alani liste olarak gonderilmelidir.";
    }

    if (!normalized.company.name) {
      return "company.name zorunludur.";
    }

    if (!normalized.customer.name) {
      return "customer.name zorunludur.";
    }

    return "";
  }

  function renderCompactLine(label, value) {
    if (!trimText(value)) return "";

    return (
      "<div class=\"hpe-compact-line\">" +
      "<span>" + escapeHtml(label) + "</span>" +
      "<strong>" + escapeHtml(value) + "</strong>" +
      "</div>"
    );
  }

  function renderCompanyBlock(company) {
    return (
      "<section class=\"hpe-company-block\">" +
      (company.logo_url ? "<img class=\"hpe-logo\" src=\"" + escapeAttr(company.logo_url) + "\" alt=\"\">" : "") +
      "<div class=\"hpe-company-name\">" + escapeHtml(company.name) + "</div>" +
      "<div>" + escapeHtml(company.address) + "</div>" +
      "<div>" + escapeHtml(company.phone) + (company.email ? " | " + escapeHtml(company.email) : "") + "</div>" +
      "</section>"
    );
  }

  function renderCustomerBlock(customer, order) {
    var deliveryAddress = firstText(customer.delivery_address, order.delivery_address);

    return (
      "<section class=\"hpe-customer-block\">" +
      "<h2>Müşteri Bilgileri</h2>" +
      "<div class=\"hpe-customer-name\">" + escapeHtml(customer.name) + "</div>" +
      (customer.address ? "<div>" + escapeHtml(customer.address) + "</div>" : "") +
      renderCompactLine("Tel:", customer.phone) +
      renderCompactLine("Mükellef Tipi:", customer.document_type) +
      renderCompactLine("Teslimat Adresi:", deliveryAddress) +
      renderCompactLine("Vergi Dairesi:", customer.tax_office) +
      renderCompactLine("Vergi No:", firstText(customer.tax_no, customer.tckn)) +
      "</section>"
    );
  }

  function renderItems(items) {
    var body = items
      .map(function(item) {
        var name = item.name;
        if (item.description) name += "\n" + item.description;

        return (
          "<tr>" +
          "<td class=\"hpe-col-code\">" + escapeHtml(item.code) + "</td>" +
          "<td class=\"hpe-col-name\">" + escapeHtml(name) + "</td>" +
          "<td class=\"hpe-col-qty\">" + escapeHtml(item.quantity) + "</td>" +
          "<td class=\"hpe-col-unit\">" + escapeHtml(item.unit) + "</td>" +
          "<td class=\"hpe-col-vat\">" + escapeHtml(item.vat_rate) + "</td>" +
          "<td class=\"hpe-col-explanation\">" + escapeHtml(item.explanation) + "</td>" +
          "</tr>"
        );
      })
      .join("");

    if (!body) {
      body =
        "<tr><td class=\"hpe-empty\" colspan=\"6\">Kalem bulunmuyor.</td></tr>";
    }

    return (
      "<table class=\"hpe-items\">" +
      "<thead><tr>" +
      "<th class=\"hpe-col-code\">Stok Kodu</th>" +
      "<th class=\"hpe-col-name\">Stok İsmi</th>" +
      "<th class=\"hpe-col-qty\">Miktar</th>" +
      "<th class=\"hpe-col-unit\">Birim</th>" +
      "<th class=\"hpe-col-vat\">KDV</th>" +
      "<th class=\"hpe-col-explanation\">Açıklama</th>" +
      "</tr></thead>" +
      "<tbody>" + body + "</tbody>" +
      "</table>"
    );
  }

  function dynamicStyleVars(style) {
    return (
      ":root{" +
      "--hpe-page-margin:" + style.page_margin_mm + "mm;" +
      "--hpe-logo-width:" + style.logo_width_mm + "mm;" +
      "--hpe-logo-height:" + style.logo_height_mm + "mm;" +
      "--hpe-body-font:" + style.body_font_px + "px;" +
      "--hpe-table-font:" + style.table_font_px + "px;" +
      "--hpe-header-gap:" + style.header_gap_mm + "mm;" +
      "--hpe-document-width:" + style.document_width_mm + "mm;" +
      "--hpe-header-meta-width:" + style.header_meta_width_mm + "mm;" +
      "--hpe-header-column-gap:" + style.header_gap_column_mm + "mm;" +
      "--hpe-col-code:" + style.col_code_mm + "mm;" +
      "--hpe-col-qty:" + style.col_qty_mm + "mm;" +
      "--hpe-col-unit:" + style.col_unit_mm + "mm;" +
      "--hpe-col-vat:" + style.col_vat_mm + "mm;" +
      "--hpe-col-explanation:" + style.col_explanation_mm + "mm;" +
      "--hpe-table-pad-y:" + style.table_cell_padding_y_mm + "mm;" +
      "--hpe-table-pad-x:" + style.table_cell_padding_x_mm + "mm;" +
      "}"
    );
  }

  function pageRule(style) {
    var size = style.paper_size === "a5" ? "size:A5 portrait;" : "size:A4 portrait;";
    return "@page{" + size + "margin:" + style.page_margin_mm + "mm;}";
  }

  function baseStyles(style) {
    return (
      dynamicStyleVars(style) +
      pageRule(style) +
      "html{background:#e8edf3;}" +
      "body{margin:0;color:#111827;font-family:Arial,Helvetica,sans-serif;font-size:var(--hpe-body-font);line-height:1.35;-webkit-print-color-adjust:exact;print-color-adjust:exact;}" +
      ".hpe-toolbar{position:sticky;top:0;z-index:10;display:flex;align-items:center;gap:8px;padding:10px 14px;background:#111827;color:#fff;border-bottom:1px solid #030712;}" +
      ".hpe-toolbar strong{margin-right:auto;font-size:13px;font-weight:700;}" +
      ".hpe-toolbar button,.hpe-toolbar label{border:1px solid rgba(255,255,255,.22);background:#1f2937;color:#fff;border-radius:6px;padding:7px 10px;font-size:12px;line-height:1;cursor:pointer;}" +
      ".hpe-toolbar label{display:flex;align-items:center;gap:6px;}" +
      ".hpe-page-shell{padding:18px;}" +
      ".hpe-document{width:var(--hpe-document-width);max-width:calc(100vw - 36px);margin:0 auto;background:#fff;box-shadow:0 16px 48px rgba(15,23,42,.18);}" +
      ".hpe-page{padding:var(--hpe-page-margin);}" +
      ".hpe-slip-header{display:grid;grid-template-columns:1.05fr .95fr var(--hpe-header-meta-width);gap:var(--hpe-header-column-gap);align-items:start;margin-bottom:var(--hpe-header-gap);}" +
      ".hpe-company-block,.hpe-customer-block,.hpe-document-meta{min-width:0;}" +
      ".hpe-logo{display:block;width:var(--hpe-logo-width);height:var(--hpe-logo-height);object-fit:contain;object-position:left center;margin:0 0 4mm;}" +
      ".hpe-company-name,.hpe-customer-name{font-weight:700;}" +
      ".hpe-company-block div,.hpe-customer-block div{margin:1px 0;white-space:pre-wrap;overflow-wrap:anywhere;}" +
      ".hpe-customer-block h2{margin:0 0 2.5mm;font-size:var(--hpe-body-font);font-weight:400;}" +
      ".hpe-compact-line{display:grid;grid-template-columns:22mm 1fr;gap:2mm;align-items:start;}" +
      ".hpe-compact-line span{color:#111827;}" +
      ".hpe-compact-line strong{font-weight:400;white-space:pre-wrap;overflow-wrap:anywhere;}" +
      ".hpe-document-meta{text-align:right;white-space:nowrap;}" +
      ".hpe-document-meta div{margin:1px 0;}" +
      ".hpe-document-meta .hpe-title{font-weight:400;}" +
      ".hpe-items{width:100%;border-collapse:collapse;table-layout:fixed;font-size:var(--hpe-table-font);}" +
      ".hpe-items thead{display:table-header-group;}" +
      ".hpe-items th{background:#fff;color:#111827;border:1px solid #d4d7dc;padding:var(--hpe-table-pad-y) var(--hpe-table-pad-x);text-align:center;font-weight:400;}" +
      ".hpe-items td{border:1px solid #d4d7dc;padding:var(--hpe-table-pad-y) var(--hpe-table-pad-x);vertical-align:top;white-space:pre-wrap;overflow-wrap:anywhere;word-break:normal;}" +
      ".hpe-items tr{break-inside:avoid;page-break-inside:avoid;}" +
      ".hpe-col-code{width:var(--hpe-col-code);}" +
      ".hpe-col-name{width:auto;}" +
      ".hpe-col-qty{width:var(--hpe-col-qty);text-align:center;}" +
      ".hpe-col-unit{width:var(--hpe-col-unit);text-align:center;}" +
      ".hpe-col-vat{width:var(--hpe-col-vat);text-align:center;}" +
      ".hpe-col-explanation{width:var(--hpe-col-explanation);}" +
      ".hpe-empty{text-align:center;color:#64748b;padding:16px!important;}" +
      ".hpe-footer{margin-top:8mm;color:#111827;white-space:pre-wrap;overflow-wrap:anywhere;}" +
      ".hpe-compact .hpe-page{padding:7mm;}" +
      ".hpe-compact .hpe-slip-header{gap:8mm;margin-bottom:9mm;}" +
      ".hpe-compact .hpe-items th,.hpe-compact .hpe-items td{padding:1.2mm;}" +
      "@media print{" +
      "html,body{background:#fff;}" +
      ".hpe-toolbar{display:none!important;}" +
      ".hpe-page-shell{padding:0;}" +
      ".hpe-document{width:auto;max-width:none;margin:0;box-shadow:none;}" +
      ".hpe-page{padding:0;}" +
      "}" +
      "@media screen and (max-width:760px){" +
      ".hpe-page-shell{padding:0;}" +
      ".hpe-document{box-shadow:none;}" +
      ".hpe-page{padding:12px;}" +
      ".hpe-slip-header{grid-template-columns:1fr;gap:12px;margin-bottom:18px;}" +
      ".hpe-document-meta{text-align:left;white-space:normal;}" +
      "}"
    );
  }

  function renderToolbar(documentTitle) {
    return (
      "<div class=\"hpe-toolbar\">" +
      "<strong>" + escapeHtml(documentTitle) + "</strong>" +
      "<label><input type=\"checkbox\" data-hpe-compact> Kompakt görünüm</label>" +
      "<button type=\"button\" data-hpe-print>Yazdır</button>" +
      "<button type=\"button\" data-hpe-close>Kapat</button>" +
      "</div>"
    );
  }

  function toolbarScript() {
    return (
      "<script>" +
      "(function(){" +
      "var compact=document.querySelector('[data-hpe-compact]');" +
      "var printButton=document.querySelector('[data-hpe-print]');" +
      "var closeButton=document.querySelector('[data-hpe-close]');" +
      "if(compact){compact.addEventListener('change',function(){document.body.classList.toggle('hpe-compact',compact.checked);});}" +
      "if(printButton){printButton.addEventListener('click',function(){window.focus();window.print();});}" +
      "if(closeButton){closeButton.addEventListener('click',function(){window.close();});}" +
      "})();" +
      "</script>"
    );
  }

  function renderOrderSlipDocument(data) {
    var footer = data.order.note
      ? "<footer class=\"hpe-footer\">" + escapeHtml(data.order.note) + "</footer>"
      : "";

    return (
      "<main class=\"hpe-document\">" +
      "<div class=\"hpe-page\">" +
      "<header class=\"hpe-slip-header\">" +
      renderCompanyBlock(data.company) +
      renderCustomerBlock(data.customer, data.order) +
      "<div class=\"hpe-document-meta\">" +
      "<div>" + escapeHtml(data.date) + "</div>" +
      "<div class=\"hpe-title\">" + escapeHtml(data.title) + "</div>" +
      "<div>#" + escapeHtml(data.document_no) + "</div>" +
      "</div>" +
      "</header>" +
      renderItems(data.items) +
      footer +
      "</div>" +
      "</main>"
    );
  }

  function renderOrderSlipHtml(data, options) {
    options = options || {};

    var bodyClass = options.compact ? " class=\"hpe-compact\"" : "";
    var toolbar = options.includeToolbar === false ? "" : renderToolbar(data.title);

    return (
      "<!doctype html>" +
      "<html><head>" +
      "<meta charset=\"utf-8\">" +
      "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
      "<title>" + escapeHtml(data.title) + "</title>" +
      "<style>" + baseStyles(data.print_style) + "</style>" +
      "</head><body" + bodyClass + ">" +
      toolbar +
      "<div class=\"hpe-page-shell\">" +
      renderOrderSlipDocument(data) +
      "</div>" +
      (options.includeToolbar === false ? "" : toolbarScript()) +
      "</body></html>"
    );
  }

  function parseAndRender(input, options) {
    var raw;
    var data;
    var errorMessage = "";

    try {
      raw = parseDocumentJson(input);
      data = normalizeOrderSlip(raw);
      errorMessage = validateOrderSlip(raw, data);
    } catch (error) {
      errorMessage = error.message;
    }

    if (errorMessage) {
      return {
        isValid: false,
        errorMessage: errorMessage,
        documentType: raw && raw.document_type ? trimText(raw.document_type) : "",
        itemCount: raw && Array.isArray(raw.items) ? raw.items.length : 0,
        data: null,
        html: ""
      };
    }

    return {
      isValid: true,
      errorMessage: "",
      documentType: data.document_type,
      itemCount: data.items.length,
      data: data,
      html: renderOrderSlipHtml(data, options || {})
    };
  }

  function setInstanceDocument(instanceId, result) {
    var id = trimText(instanceId);
    if (!id) return;
    registry[id] = result;
  }

  function getInstanceDocument(instanceId) {
    return registry[trimText(instanceId)] || null;
  }

  function openPrintPreview(input, options) {
    options = options || {};

    var result = input && input.isValid !== undefined
      ? input
      : parseAndRender(input, { includeToolbar: true, compact: !!options.compact });

    if (!result.isValid) return result;

    if (typeof window === "undefined" || !window.open) {
      return {
        isValid: false,
        errorMessage: "Print preview sadece tarayici ortaminda acilabilir.",
        documentType: result.documentType,
        itemCount: result.itemCount,
        data: result.data,
        html: result.html
      };
    }

    var previewWindow = window.open("", options.windowName || "_blank");
    if (!previewWindow) {
      return {
        isValid: false,
        errorMessage: "Preview penceresi acilamadi. Popup engelleyiciyi kontrol edin.",
        documentType: result.documentType,
        itemCount: result.itemCount,
        data: result.data,
        html: result.html
      };
    }

    previewWindow.document.open();
    previewWindow.document.write(result.html);
    previewWindow.document.close();
    previewWindow.focus();

    return result;
  }

  return {
    version: VERSION,
    parseAndRender: parseAndRender,
    openPrintPreview: openPrintPreview,
    setInstanceDocument: setInstanceDocument,
    getInstanceDocument: getInstanceDocument,
    _private: {
      escapeHtml: escapeHtml,
      normalizeOrderSlip: normalizeOrderSlip,
      renderOrderSlipHtml: renderOrderSlipHtml
    }
  };
});
