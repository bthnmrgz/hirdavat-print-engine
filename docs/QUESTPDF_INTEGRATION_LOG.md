# QuestPDF Integration Log

Bu dokuman, Printer v2 icinde QuestPDF entegrasyonu sirasinda alinan kararlarin ve yapilan denemelerin teknik kaydidir.

## Baslangic Durumu

Mevcut sistem baslangicta Bubble plugin icinde calisan HTML/browser-print tabanli bir yapiydi.

Ana akisi:

```text
document_json -> HirdavatPrintEngine -> kontrollu HTML/CSS -> browser print preview
```

Ilk hedef, eski sistemi cope atmadan QuestPDF ile paralel bir PDF uretim yolu denemekti. Daha sonra Bubble plugin katmani kaldirildi ve repo server-side QuestPDF API'ye odaklandi.

## QuestPDF Degerlendirmesi

QuestPDF'in uygun oldugu noktalar:

- Server-side PDF uretimi.
- A4/A5 gibi kesin sayfa boyutlari.
- Cok sayfali tablo, header/footer, sayfa numarasi gibi rapor ihtiyaclari.
- PDF onizleme, kaydetme, mail eki, arsivleme gibi workflow'lar.

Sinir:

- QuestPDF browser veya Bubble plugin icinde calismaz.
- QuestPDF bir Cloud servisi degildir.
- .NET backend gerekir.

## Ilk Prototip

Repo icine minimal ASP.NET + QuestPDF servisi eklendi:

```text
questpdf-service/
```

Baslangic endpoint'leri:

```text
GET  /health
POST /render/order-slip
```

`/render/order-slip`, mevcut `order_slip` JSON kontratini alip `application/pdf` binary response dondurur.

## Bubble Plugin Element Denemesi

Ilk yaklasim, eski HTML elementini koruyup ikinci bir Bubble plugin elementi eklemekti. Elementin hedefi `document_json + questpdf_api_url -> fetch PDF -> iframe/blob preview` akisiydi.

Ancak Bubble plugin editor parser'i update/action kodunda parse hatalari verdi. Wrapper'li ve wrapper'siz ES5 uyumlu surumler de denendi. Bu yol gereksiz kirilgan bulundu ve ilgili plugin dosyalari repodan kaldirildi.

Karar:

```text
QuestPDF Bubble plugin elementi olarak degil, Bubble API Connector uzerinden kullanilacak.
```

## API Connector'a Gecis

Bubble API Connector binary PDF response ile JSON seciliyken hata verdi:

```text
The API call returns a non-object and you picked JSON.
```

Bu nedenle JSON response donen ikinci endpoint eklendi:

```text
POST /render/order-slip-url
```

Davranis:

```text
JSON input -> PDF uret -> PDF dosyasi kaydet -> JSON response ile pdf_url don
```

Response:

```json
{
  "ok": true,
  "pdf_url": "https://...",
  "file_name": "...pdf",
  "content_type": "application/pdf",
  "size_bytes": 43037
}
```

Bu model Bubble icin daha stabil bulundu.

## Local ve ngrok Testleri

Local servis portu:

```text
http://localhost:5159
```

Test icin ngrok ile public URL acildi:

```text
https://<ngrok-domain>
```

Dogulananlar:

- `/health` response verdi.
- `/render/order-slip` PDF binary dondu.
- `/render/order-slip-url` JSON response dondu.
- Bubble API Connector JSON endpoint'i initialize edebildi.

Not:

Mevcut baska ngrok tunnel'ina dokunmamak icin QuestPDF icin ayrica ikinci ngrok endpoint kullanildi.

## A4 / A5 Paper Size Karari

Ilk QuestPDF servisinde sayfa boyutu A4 sabitti. Sonra JSON kontratina `print_style.paper_size` eklendi.

Desteklenen degerler:

```text
a4
a5
```

Karar:

- `a4` -> A4 dikey.
- `a5` -> A5 yatay.
- bos/gecersiz -> A4 dikey.

Gerekce:

Canli referanslarda A4 dosya dikey, A5 dosya yataydi.

## Canli Referanslarla Tasarim Uyarlamasi

Karsilastirilan dosyalar:

```text
/Users/batuhanmerguz/Downloads/canli-siparis-test2.pdf
/Users/batuhanmerguz/Downloads/canli-siparis-test.pdf
/Users/batuhanmerguz/Downloads/siparis-test2.pdf
```

Tespitler:

- Canli A4 referans: yaklasik `595 x 842 pt`.
- Canli A5 referans: yaklasik `595 x 420 pt`, yani A5 yatay.
- QuestPDF'in ilk A5 ciktisi A5 dikeydi.
- Canli tasarimda logo sol ustte daha belirgin.
- Musteri blogu orta-sag alanda ve solunda dikey ayirici cizgi var.
- Sag ustte tarih, fis no ve baslik kompakt duruyor.
- Tablo sayfa genisligini daha iyi kullaniyor.

Yapilan layout guncellemeleri:

- A5 icin `PageSizes.A5.Landscape()`.
- Header: sol firma/logo, orta-sag musteri, sag meta blok.
- Musteri bloguna sol border.
- Tablo kolon oranlari ve satir yukseklikleri canli tasarima yaklastirildi.
- A4/A5 icin farkli varsayilan margin/font/logo/tablo olculeri tanimlandi.

## Production Hosting Karari

Cloudflare urunleri degerlendirildi:

- Cloudflare Pages Functions, Workers runtime uzerinde calisir; .NET QuestPDF API icin uygun degil.
- Cloudflare Workers saf PDF servisimiz icin uygun degil.
- Cloudflare Containers teknik olarak uygun olabilir, fakat beta oldugu icin v1 production icin riskli.
- En stabil ve maliyet-dostu yol: VPS + Docker + Cloudflare Tunnel + R2.

Production hedef mimari:

```text
Bubble -> Cloudflare DNS/WAF -> Cloudflare Tunnel -> VPS Docker QuestPDF API -> R2 -> pdf_url
```

## Dogrulamalar

Calistirilan kontroller:

```bash
dotnet build questpdf-service/HirdavatQuestPdf.Api.csproj
git diff --check
```

PDF olcu kontrolleri:

```text
A4 -> 595 x 842
A5 -> 595 x 420
```

Endpoint kontrolleri:

- `/render/order-slip` -> `200 application/pdf`
- `/render/order-slip-url` -> `200 application/json`

## 2026-05-21 Print JSON Genisletme ve Production Deploy

Istek:

- `document_type` sadece `order_slip` ile sinirli kalmayacak.
- `quote`, `receipt`, `order_slip` ayni endpoint isimleriyle desteklenecek.
- `customer` opsiyonel olacak; bos/null geldiginde header'daki musteri blogu render edilmeyecek.
- Bubble tum para/tutar degerlerini formatli string olarak gonderecek; server hesaplama yapmayacak.
- Bubble plugin/browser-print dosyalari artik kullanilmayacak ve repo server-side QuestPDF API'ye indirgenecek.

Kod guncellemeleri:

- `OrderSlipPayload` yerine genisletilmis `PrintDocumentPayload` modeli kullanildi.
- `Validate` akisi `quote`, `receipt`, `order_slip` degerlerini kabul edecek sekilde guncellendi.
- `company.name` zorunlu kaldi.
- `customer.name` zorunlulugu kaldirildi.
- `quote` ve `order_slip` icin `items` veya `table` zorunlu.
- `receipt` icin `payments` veya `table` zorunlu.
- `table.columns` ve `columns[].key` validasyonu korundu.
- `quote` layout'u urun/hizmet tablosu, `detail_fields`, `total_rows` ve `signature` alanlarini render ediyor.
- `receipt` layout'u `payments` ve `payment_totals` alanlarini render ediyor.
- `order_slip` mevcut siparis fisi davranisini koruyor.

Repo temizligi:

- Bubble plugin dosyalari kaldirildi.
- Eski HTML/browser print engine ve ona bagli Node testi kaldirildi.
- README QuestPDF API odakli olacak sekilde yeniden yazildi.
- `.gitignore` build ciktilari, generated PDF'ler, `.env` ve `.DS_Store` icin genisletildi.
- Generated PDF'ler, build ciktilari ve hassas degerler commit'e alinmadi.

Ornek JSON'lar:

- `examples/quote-valid.json`
- `examples/receipt-valid.json`
- `examples/order-slip-customerless.json`
- `examples/order-slip-custom-table.json`

Git:

```text
a8acec1 feat: add questpdf print API
```

Production deploy:

- Hostinger uzerindeki `/opt/hirdavat-print-engine` klasoru git repo olmadigi icin kaynak dosyalar `rsync` ile senkronize edildi.
- `.env`, build ciktilari, generated PDF'ler ve secret iceren dosyalar senkronizasyon disinda birakildi.
- `QUESTPDF_API_KEY` rotate edildi.
- `docker compose up -d --build questpdf-api caddy` ile QuestPDF API container'i yeniden build/start edildi.

Production dogrulamalari:

- `GET https://pdf-api.hirdavat.ai/health` -> `200`
- API key olmadan `/render/order-slip-url` -> `401`
- Yeni API key ile `quote` payload -> `200` ve `pdf_url`
- Yeni API key ile `receipt` payload -> `200` ve `pdf_url`
- Gecersiz `document_type` artik guncel hata mesajini donuyor:

```text
document_type 'quote', 'receipt' veya 'order_slip' olmalidir.
```

Not:

Yeni API key Bubble API Connector'daki `X-Api-Key` header degeriyle eslestirilmelidir. Key dokumanlara yazilmadi.

## 2026-05-21 Makbuz Sifir Toplam Satirlari

Istek:

- Bubble makbuzlarda bazi genel toplam satirlarini zorunlu olarak `0` gonderiyor.
- Bu satirlar sadece sifir olduklarinda PDF'te render edilmemeli.
- Sifir olmayan degerler ayni satir kontratiyla gorunmeye devam etmeli.

Kod guncellemeleri:

- `LabeledValueRow` modeline `hide_if_zero` alani eklendi.
- `payment_totals` ve `total_rows` render akisi bu bayragi destekleyecek sekilde filtrelendi.
- `0`, `0,00`, `0,00 TL`, `TRY`, `TL`, `₺` ve yuzde sembolu iceren sifir-benzeri degerler desteklendi.
- Sadece ilgili satirda `"hide_if_zero": true` varsa gizleme uygulanir; varsayilan davranis degismedi.

Ornek:

```json
{"label":"Kredi Karti","value":"0,00 TL","hide_if_zero":true}
```

Production deploy:

- `README.md`, `examples/receipt-valid.json` ve `questpdf-service/Program.cs` dosyalari Hostinger VPS uzerindeki `/opt/hirdavat-print-engine` klasorune `rsync` ile senkronize edildi.
- `docs/QUESTPDF_INTEGRATION_LOG.md` ilk deploy paketine dahil edilmedi; bu not sonradan lokal log'a eklendi.
- `docker compose up -d --build questpdf-api caddy` ile QuestPDF API container'i yeniden build/start edildi.

Production dogrulamalari:

- `GET https://pdf-api.hirdavat.ai/health` -> `200`
- `docker compose ps` -> `caddy` ve `questpdf-api` Up
- Canli public endpoint uzerinden `examples/receipt-valid.json` ile `/render/order-slip-url` -> `200`, `ok: true`, `pdf_url` dondu.

## Acik Isler

- Bubble API Connector'da rotate edilen `X-Api-Key` yeni degerle guncellenecek.
- R2 upload ve presigned URL destegi ileride yeniden degerlendirilecek.

## 2026-05-25 Cari Ekstre Dokuman Tipi

Istek:

- Analytics Worker, mevcut `/render/order-slip-url` endpoint'ine `document_type: "cari_ledger"` payload'i gonderecek.
- `quote`, `receipt` ve `order_slip` davranislari degismeyecek.

Planlanan kontrat:

- `cari.name` ve root-level `columns[].key` zorunlu.
- `company.name`, `items`, `payments` ve `table` cari ekstre icin zorunlu degil.
- `rows` bos veya eksik oldugunda PDF tek satirlik `Cari hareketi yok.` bos durumunu render eder.
- Para, bakiye, vade ve durum degerleri hesaplanmaz; Worker'in gonderdigi string'ler aynen kullanilir.
