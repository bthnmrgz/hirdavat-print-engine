(function(root, factory) {
  if (typeof module === "object" && module.exports) {
    module.exports = factory();
  } else {
    root.HirdavatPrintEngine = factory();
  }
})(typeof globalThis !== "undefined" ? globalThis : this, function() {
  "use strict";

  var VERSION = "0.1.0";
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
      tax_office: trimText(value.tax_office),
      tax_no: trimText(value.tax_no),
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
      status: trimText(item.status),
      note: trimText(item.note)
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

  function renderInfoRow(label, value) {
    return (
      "<div class=\"hpe-info-row\">" +
      "<dt>" + escapeHtml(label) + "</dt>" +
      "<dd>" + escapeHtml(value) + "</dd>" +
      "</div>"
    );
  }

  function renderPartyBlock(title, party, extraRows) {
    var rows = [
      renderInfoRow("Unvan", party.name),
      renderInfoRow("Adres", party.address),
      renderInfoRow("Telefon", party.phone),
      party.email ? renderInfoRow("E-posta", party.email) : "",
      party.tax_office ? renderInfoRow("Vergi Dairesi", party.tax_office) : "",
      party.tax_no ? renderInfoRow("Vergi No", party.tax_no) : ""
    ];

    if (Array.isArray(extraRows)) rows = rows.concat(extraRows);

    return (
      "<section class=\"hpe-panel\">" +
      "<h2>" + escapeHtml(title) + "</h2>" +
      "<dl>" + rows.join("") + "</dl>" +
      "</section>"
    );
  }

  function renderOrderBlock(order) {
    return (
      "<section class=\"hpe-panel\">" +
      "<h2>Sipariş</h2>" +
      "<dl>" +
      renderInfoRow("Durum", order.status) +
      renderInfoRow("Teslimat", order.delivery_address) +
      renderInfoRow("Sevkiyat", order.shipping_method) +
      "</dl>" +
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
          "<td class=\"hpe-col-no\">" + escapeHtml(item.line_no) + "</td>" +
          "<td class=\"hpe-col-code\">" + escapeHtml(item.code) + "</td>" +
          "<td class=\"hpe-col-name\">" + escapeHtml(name) + "</td>" +
          "<td class=\"hpe-col-qty\">" + escapeHtml(item.quantity) + "</td>" +
          "<td class=\"hpe-col-unit\">" + escapeHtml(item.unit) + "</td>" +
          "<td class=\"hpe-col-status\">" + escapeHtml(item.status) + "</td>" +
          "<td class=\"hpe-col-note\">" + escapeHtml(item.note) + "</td>" +
          "</tr>"
        );
      })
      .join("");

    if (!body) {
      body =
        "<tr><td class=\"hpe-empty\" colspan=\"7\">Kalem bulunmuyor.</td></tr>";
    }

    return (
      "<table class=\"hpe-items\">" +
      "<thead><tr>" +
      "<th class=\"hpe-col-no\">#</th>" +
      "<th class=\"hpe-col-code\">Kod</th>" +
      "<th class=\"hpe-col-name\">Urun</th>" +
      "<th class=\"hpe-col-qty\">Miktar</th>" +
      "<th class=\"hpe-col-unit\">Birim</th>" +
      "<th class=\"hpe-col-status\">Durum</th>" +
      "<th class=\"hpe-col-note\">Not</th>" +
      "</tr></thead>" +
      "<tbody>" + body + "</tbody>" +
      "</table>"
    );
  }

  function baseStyles() {
    return (
      "@page{margin:10mm;}" +
      "html{background:#e8edf3;}" +
      "body{margin:0;color:#111827;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:1.35;-webkit-print-color-adjust:exact;print-color-adjust:exact;}" +
      ".hpe-toolbar{position:sticky;top:0;z-index:10;display:flex;align-items:center;gap:8px;padding:10px 14px;background:#111827;color:#fff;border-bottom:1px solid #030712;}" +
      ".hpe-toolbar strong{margin-right:auto;font-size:13px;font-weight:700;}" +
      ".hpe-toolbar button,.hpe-toolbar label{border:1px solid rgba(255,255,255,.22);background:#1f2937;color:#fff;border-radius:6px;padding:7px 10px;font-size:12px;line-height:1;cursor:pointer;}" +
      ".hpe-toolbar label{display:flex;align-items:center;gap:6px;}" +
      ".hpe-page-shell{padding:18px;}" +
      ".hpe-document{max-width:190mm;margin:0 auto;background:#fff;box-shadow:0 16px 48px rgba(15,23,42,.18);}" +
      ".hpe-page{padding:10mm;}" +
      ".hpe-header{display:grid;grid-template-columns:1fr auto;gap:16px;align-items:start;border-bottom:2px solid #111827;padding-bottom:12px;margin-bottom:12px;}" +
      ".hpe-brand{display:flex;gap:12px;align-items:flex-start;min-width:0;}" +
      ".hpe-logo{width:54px;height:54px;object-fit:contain;border:1px solid #d1d5db;padding:4px;}" +
      ".hpe-company h1{margin:0 0 4px;font-size:18px;line-height:1.15;color:#0f172a;}" +
      ".hpe-company p{margin:1px 0;color:#374151;}" +
      ".hpe-document-meta{text-align:right;white-space:nowrap;}" +
      ".hpe-document-meta h2{margin:0 0 6px;font-size:20px;line-height:1;color:#0f172a;}" +
      ".hpe-document-meta p{margin:2px 0;color:#374151;}" +
      ".hpe-grid{display:grid;grid-template-columns:1fr 1fr;gap:10px;margin-bottom:12px;}" +
      ".hpe-panel{border:1px solid #d1d5db;border-radius:6px;padding:9px;break-inside:avoid;page-break-inside:avoid;}" +
      ".hpe-panel h2{margin:0 0 7px;font-size:12px;text-transform:uppercase;letter-spacing:.04em;color:#334155;}" +
      ".hpe-panel dl{margin:0;display:grid;gap:4px;}" +
      ".hpe-info-row{display:grid;grid-template-columns:86px 1fr;gap:8px;min-width:0;}" +
      ".hpe-info-row dt{color:#64748b;font-weight:700;}" +
      ".hpe-info-row dd{margin:0;min-width:0;white-space:pre-wrap;overflow-wrap:anywhere;}" +
      ".hpe-items{width:100%;border-collapse:collapse;table-layout:fixed;font-size:11px;}" +
      ".hpe-items thead{display:table-header-group;}" +
      ".hpe-items th{background:#f1f5f9;color:#0f172a;border:1px solid #cbd5e1;padding:6px 5px;text-align:left;font-weight:700;}" +
      ".hpe-items td{border:1px solid #d1d5db;padding:6px 5px;vertical-align:top;white-space:pre-wrap;overflow-wrap:anywhere;word-break:normal;}" +
      ".hpe-items tr{break-inside:avoid;page-break-inside:avoid;}" +
      ".hpe-col-no{width:7mm;text-align:center;}" +
      ".hpe-col-code{width:23mm;}" +
      ".hpe-col-name{width:auto;}" +
      ".hpe-col-qty{width:16mm;text-align:right;}" +
      ".hpe-col-unit{width:16mm;}" +
      ".hpe-col-status{width:26mm;}" +
      ".hpe-col-note{width:28mm;}" +
      ".hpe-empty{text-align:center;color:#64748b;padding:16px!important;}" +
      ".hpe-footer{margin-top:12px;border-top:1px solid #d1d5db;padding-top:8px;color:#475569;white-space:pre-wrap;overflow-wrap:anywhere;}" +
      ".hpe-compact .hpe-page{padding:7mm;}" +
      ".hpe-compact .hpe-grid{gap:7px;margin-bottom:8px;}" +
      ".hpe-compact .hpe-panel{padding:7px;}" +
      ".hpe-compact .hpe-items th,.hpe-compact .hpe-items td{padding:4px;}" +
      "@media print{" +
      "html,body{background:#fff;}" +
      ".hpe-toolbar{display:none!important;}" +
      ".hpe-page-shell{padding:0;}" +
      ".hpe-document{max-width:none;margin:0;box-shadow:none;}" +
      ".hpe-page{padding:0;}" +
      ".hpe-panel{break-inside:avoid;page-break-inside:avoid;}" +
      "}" +
      "@media(max-width:760px){" +
      ".hpe-page-shell{padding:0;}" +
      ".hpe-document{box-shadow:none;}" +
      ".hpe-page{padding:12px;}" +
      ".hpe-header,.hpe-grid{grid-template-columns:1fr;}" +
      ".hpe-document-meta{text-align:left;white-space:normal;}" +
      ".hpe-info-row{grid-template-columns:78px 1fr;}" +
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
    var logo = data.company.logo_url
      ? "<img class=\"hpe-logo\" src=\"" + escapeAttr(data.company.logo_url) + "\" alt=\"\">"
      : "";

    var footer = data.order.note
      ? "<footer class=\"hpe-footer\"><strong>Not:</strong> " + escapeHtml(data.order.note) + "</footer>"
      : "<footer class=\"hpe-footer\">Bu belge Hirdavat Print Engine tarafindan uretilmistir.</footer>";

    return (
      "<main class=\"hpe-document\">" +
      "<div class=\"hpe-page\">" +
      "<header class=\"hpe-header\">" +
      "<div class=\"hpe-brand\">" +
      logo +
      "<div class=\"hpe-company\">" +
      "<h1>" + escapeHtml(data.company.name) + "</h1>" +
      "<p>" + escapeHtml(data.company.address) + "</p>" +
      "<p>" + escapeHtml(data.company.phone) + (data.company.email ? " | " + escapeHtml(data.company.email) : "") + "</p>" +
      "</div>" +
      "</div>" +
      "<div class=\"hpe-document-meta\">" +
      "<h2>" + escapeHtml(data.title) + "</h2>" +
      "<p><strong>Belge No:</strong> " + escapeHtml(data.document_no) + "</p>" +
      "<p><strong>Tarih:</strong> " + escapeHtml(data.date) + "</p>" +
      "<p><strong>Kalem:</strong> " + escapeHtml(data.items.length) + "</p>" +
      "</div>" +
      "</header>" +
      "<div class=\"hpe-grid\">" +
      renderPartyBlock("Müşteri", data.customer) +
      renderOrderBlock(data.order) +
      "</div>" +
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
      "<style>" + baseStyles() + "</style>" +
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
