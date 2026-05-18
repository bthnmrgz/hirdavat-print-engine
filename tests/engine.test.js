const assert = require("assert");
const fs = require("fs");
const path = require("path");

const engine = require("../src/hirdavat-print-engine");

function loadSample() {
  return JSON.parse(
    fs.readFileSync(path.join(__dirname, "../examples/order-slip-valid.json"), "utf8")
  );
}

function withItems(count) {
  const sample = loadSample();

  sample.items = Array.from({ length: count }, (_, index) => ({
    code: `CX${String(index + 1).padStart(4, "0")}`,
    name:
      "UZUN URUN ADI TESTI - KAYNAK KABLO JAKI ADAPTORU VE DAR HUCREDE WRAP KONTROLU",
    description: index % 3 === 0 ? "Opsiyonel aciklama satiri" : "",
    quantity: String((index % 5) + 1),
    unit: "Adet",
    kdv: index % 2 === 0 ? "%20" : "%10",
    status: index % 2 === 0 ? "Hazırlanacak" : "Beklemede",
    note: index % 4 === 0 ? "Raf kontrolu" : ""
  }));

  return sample;
}

for (const count of [1, 20, 60]) {
  const result = engine.parseAndRender(JSON.stringify(withItems(count)));

  assert.strictEqual(result.isValid, true, `${count} items should be valid`);
  assert.strictEqual(result.documentType, "order_slip");
  assert.strictEqual(result.itemCount, count);
  assert.ok(result.html.includes("<table class=\"hpe-items\">"));
  assert.ok(result.html.includes("<thead><tr>"));
  assert.ok(result.html.includes("Stok Kodu"));
  assert.ok(result.html.includes("Müşteri Bilgileri"));
  assert.ok(result.html.includes("@page{margin:10mm;}"));
  assert.ok(result.html.includes("@media screen and (max-width:760px)"));
  assert.ok(result.html.includes(".hpe-col-name{width:34%;}"));
}

{
  const sample = withItems(3);

  const result = engine.parseAndRender(JSON.stringify(sample));
  assert.strictEqual(result.isValid, true);
  assert.ok(result.html.includes(".hpe-document{width:auto;max-width:none;margin:0;box-shadow:none;}"));
  assert.ok(result.html.includes(".hpe-col-code{width:16%;}"));
}

{
  const result = engine.parseAndRender("{bad json");
  assert.strictEqual(result.isValid, false);
  assert.match(result.errorMessage, /JSON/);
}

{
  const sample = loadSample();
  delete sample.company.name;

  const result = engine.parseAndRender(JSON.stringify(sample));
  assert.strictEqual(result.isValid, false);
  assert.strictEqual(result.errorMessage, "company.name zorunludur.");
}

{
  const sample = loadSample();
  sample.document_type = "invoice";

  const result = engine.parseAndRender(JSON.stringify(sample));
  assert.strictEqual(result.isValid, false);
  assert.match(result.errorMessage, /order_slip/);
}

console.log("HirdavatPrintEngine tests passed");
