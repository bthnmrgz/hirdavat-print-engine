function(instance, properties, context) {
  var engine = window.HirdavatPrintEngine;
  var engineUrl = "https://cdn.jsdelivr.net/gh/bthnmrgz/hirdavat-print-engine@v0.1.2/src/hirdavat-print-engine.js";

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

  function run(engine) {
    if (!engine) {
      console.error("[HirdavatPrintEngine] Shared library yuklenmedi.");
      return;
    }

    var overrideJson = properties.document_json_override || "";
    var result = overrideJson
      ? engine.parseAndRender(overrideJson, { includeToolbar: true })
      : instance.data.hpe_last_result;

    if (!result && instance.data.hpe_instance_id) {
      result = engine.getInstanceDocument(instance.data.hpe_instance_id);
    }

    if (!result) {
      result = {
        isValid: false,
        errorMessage: "Element henuz gecerli bir print belgesi olusturmadi.",
        documentType: "",
        itemCount: 0,
        html: ""
      };
    }

    if (!result.isValid) {
      instance.publishState("is_valid", false);
      instance.publishState("error_message", result.errorMessage || "Print belgesi gecersiz.");
      console.warn("[HirdavatPrintEngine] Preview acilmadi:", result.errorMessage);
      return;
    }

    var previewResult = engine.openPrintPreview(result, {
      windowName: "hirdavat_print_preview"
    });

    if (!previewResult.isValid) {
      instance.publishState("is_valid", false);
      instance.publishState("error_message", previewResult.errorMessage || "Preview acilamadi.");
      console.warn("[HirdavatPrintEngine] Preview acilamadi:", previewResult.errorMessage);
    }
  }

  if (!engine) {
    loadEngine(run);
    return;
  }

  run(engine);
}
