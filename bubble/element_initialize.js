function(instance, context) {
  instance.data.hpe_instance_id = "";
  instance.data.hpe_last_result = null;

  var root = document.createElement("div");
  root.className = "hpe-element-root";
  root.style.width = "100%";
  root.style.minHeight = "1px";
  root.style.boxSizing = "border-box";

  instance.data.hpe_root = root;
  instance.canvas.empty();
  instance.canvas.append(root);

  instance.publishState("is_valid", false);
  instance.publishState("error_message", "");
  instance.publishState("document_type", "");
  instance.publishState("item_count", 0);
  instance.publishState("last_rendered_html", "");
}
