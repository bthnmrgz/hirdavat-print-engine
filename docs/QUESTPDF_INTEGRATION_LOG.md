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

## Acik Isler

- Production Dockerfile ve Docker Compose eklenecek.
- R2 upload ve presigned URL destegi eklenecek.
- API key dogrulamasi eklenecek.
- CORS production domainleriyle sinirlanacak.
- Cloudflare Tunnel production hostname'e baglanacak.
- Bubble API Connector production URL ve `X-Api-Key` ile guncellenecek.
