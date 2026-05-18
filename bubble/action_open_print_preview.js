function(instance, properties, context) {
  var engine = window.HirdavatPrintEngine;

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
