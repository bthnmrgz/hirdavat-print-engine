function(instance, properties, context) {
  var engine = window.HirdavatPrintEngine;
  var engineUrl = "https://cdn.jsdelivr.net/gh/bthnmrgz/hirdavat-print-engine@5a40212e089edc69abcc8289878bda0a9fe317fd/src/hirdavat-print-engine.js";
  var instanceId = properties.instance_id || "";
  var debugMode = !!properties.debug_mode;
  var autoRenderPreview = !!properties.auto_render_preview;
  var root = instance.data.hpe_root;

  function loadEngine(callback) {
    if (window.HirdavatPrintEngine) {
      callback(window.HirdavatPrintEngine);
      return;
    }

    if (window.__hpe_engine_loading) {
      window.__hpe_engine_loading.push(callback);
      return;
    }

    window.__hpe_engine_loading = [callback];

    var script = document.createElement("script");
    script.src = engineUrl;
    script.async = false;
    script.onload = function() {
      var callbacks = window.__hpe_engine_loading || [];
      window.__hpe_engine_loading = null;
      callbacks.forEach(function(done) {
        done(window.HirdavatPrintEngine);
      });
    };
    script.onerror = function() {
      var callbacks = window.__hpe_engine_loading || [];
      window.__hpe_engine_loading = null;
      callbacks.forEach(function(done) {
        done(null);
      });
    };

    document.head.appendChild(script);
  }

  function publish(result) {
    instance.publishState("is_valid", !!result.isValid);
    instance.publishState("error_message", result.errorMessage || "");
    instance.publishState("document_type", result.documentType || "");
    instance.publishState("item_count", result.itemCount || 0);
    instance.publishState("last_rendered_html", result.html || "");
  }

  function renderPreview(result) {
    if (!root) return;

    root.innerHTML = "";

    if (!autoRenderPreview) {
      root.style.minHeight = "1px";
      return;
    }

    if (!result.isValid) {
      var error = document.createElement("div");
      error.style.cssText =
        "box-sizing:border-box;width:100%;padding:10px;border:1px solid #fecaca;background:#fef2f2;color:#991b1b;font:12px/1.35 Arial,sans-serif;border-radius:6px;";
      error.textContent = result.errorMessage || "Print belgesi hazirlanamadi.";
      root.appendChild(error);
      return;
    }

    var iframe = document.createElement("iframe");
    iframe.title = "Print preview";
    iframe.setAttribute("sandbox", "allow-same-origin allow-scripts");
    iframe.style.cssText =
      "display:block;width:100%;height:360px;border:1px solid #d1d5db;border-radius:6px;background:#fff;box-sizing:border-box;";
    iframe.srcdoc = engine.parseAndRender(properties.document_json, {
      includeToolbar: false,
      compact: true
    }).html;

    root.appendChild(iframe);
  }

  function run(engine) {
    if (!engine) {
      var missingEngine = {
        isValid: false,
        errorMessage: "HirdavatPrintEngine shared library yuklenmedi.",
        documentType: "",
        itemCount: 0,
        html: ""
      };

      instance.data.hpe_last_result = missingEngine;
      publish(missingEngine);
      renderPreview(missingEngine);
      return;
    }

    var result = engine.parseAndRender(properties.document_json, {
      includeToolbar: true,
      compact: false
    });

    instance.data.hpe_instance_id = instanceId;
    instance.data.hpe_last_result = result;
    engine.setInstanceDocument(instanceId, result);

    publish(result);
    renderPreview(result);

    if (debugMode) {
      if (result.isValid) {
        console.info("[HirdavatPrintEngine] document ready", {
          instance_id: instanceId,
          document_type: result.documentType,
          item_count: result.itemCount
        });
      } else {
        console.warn("[HirdavatPrintEngine] document invalid", {
          instance_id: instanceId,
          error: result.errorMessage
        });
      }
    }
  }

  if (!engine) {
    var missingEngine = {
      isValid: false,
      errorMessage: "HirdavatPrintEngine yukleniyor...",
      documentType: "",
      itemCount: 0,
      html: ""
    };

    instance.data.hpe_last_result = missingEngine;
    publish(missingEngine);
    renderPreview(missingEngine);
    loadEngine(run);
    return;
  }

  run(engine);
}
