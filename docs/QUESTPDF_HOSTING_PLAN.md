# QuestPDF Production Hosting Plan

Bu dokuman, QuestPDF tabanli PDF API'nin production ortaminda nasil calistirilacagini tanimlar.

## Karar Ozeti

QuestPDF bir Cloud/SaaS PDF servisi degildir. `questpdf-service` su anda `.NET 8` uzerinde calisan ASP.NET API olarak tasarlandi. Bu nedenle Cloudflare Workers veya Cloudflare Pages Functions icinde dogrudan calistirilmasi uygun degildir.

Onerilen production mimarisi:

```text
Bubble -> Cloudflare DNS/WAF -> Cloudflare Tunnel -> VPS Docker QuestPDF API -> R2 -> pdf_url
```

Bu modelde compute kucuk bir VPS uzerinde calisir; Cloudflare ise DNS, SSL, WAF, Tunnel, rate limit ve PDF dosya saklama tarafini toparlar.

## Neden Cloudflare Pages / Workers Degil?

- Cloudflare Pages Functions, Workers runtime uzerinde calisir.
- Workers runtime JavaScript/TypeScript, Python, Rust ve Wasm isleri icin uygundur; ASP.NET + QuestPDF servisini dogrudan host etmez.
- QuestPDF PDF uretimi icin .NET runtime, native dependency davranisi ve server benzeri process modeli ister.
- Cloudflare Containers teknik olarak Docker image calistirabilir, ancak beta durumunda oldugu icin v1 production icin ana yol olarak secilmedi.

## Onerilen Altyapi

### Compute

- Kucuk Linux VPS.
- Baslangic icin 2 vCPU / 4 GB RAM yeterli hedef kabul edilir.
- Servis Docker container olarak calisir.
- Host uzerinde sadece local port acilir:

```text
127.0.0.1:5159 -> QuestPDF API
```

### Cloudflare Tunnel

`cloudflared`, VPS uzerinden Cloudflare'a outbound baglanti kurar. Inbound port acmaya gerek kalmaz.

Ornek hostname:

```text
pdf-api.<domain>.com -> http://localhost:5159
```

### R2 PDF Storage

`/render/order-slip-url` endpoint'i PDF'i urettikten sonra R2'ye yukler ve Bubble'a JSON response dondurur.

Response formati korunur:

```json
{
  "ok": true,
  "pdf_url": "https://...",
  "file_name": "OZG2026-17.pdf",
  "content_type": "application/pdf",
  "size_bytes": 43037
}
```

V1 icin PDF'lerin hassas veri icerebilecegi kabul edilir. Bu nedenle public bucket yerine kisa sureli presigned GET URL tercih edilir.

## API Guvenligi

Bubble API Connector, her request'te gizli bir header gonderir:

```text
X-Api-Key: <secret>
```

QuestPDF API bu header'i dogrular. Yanlis veya eksik key icin `401 Unauthorized` doner.

CORS production'da daraltilir:

```text
https://kodsuzai.bubbleapps.io
https://hirdavat.ai
```

Cloudflare WAF uzerinde ek koruma:

- `pdf-api.<domain>.com` icin rate limit.
- Ornek baslangic limiti: IP basina dakikada 30 request.
- Gerekirse sadece Bubble backend/origin kaynakli request'lere izin veren ek rule.

## Docker Deployment Taslagi

Servis production icin su parcalarla paketlenir:

- `Dockerfile`
- `docker-compose.yml`
- `.env`

Bu parcalar repo icinde eklendi:

- `questpdf-service/Dockerfile`
- `docker-compose.yml`
- `.env.example`
- `docs/HOSTINGER_KVM4_DEPLOYMENT.md`

Ornek environment:

```text
QUESTPDF_API_KEY=...
R2_ACCOUNT_ID=...
R2_ACCESS_KEY_ID=...
R2_SECRET_ACCESS_KEY=...
R2_BUCKET=...
PDF_URL_TTL_MINUTES=15
ALLOWED_ORIGINS=https://kodsuzai.bubbleapps.io,https://hirdavat.ai
```

Deploy komutu:

```bash
docker compose up -d --build
```

## Bubble API Connector

Ana endpoint:

```text
POST https://pdf-api.<domain>.com/render/order-slip-url
```

Headers:

```text
Content-Type: application/json
X-Api-Key: <secret>
```

Body:

```json
{
  "document_type": "order_slip",
  "print_style": {
    "paper_size": "a4"
  }
}
```

`paper_size` davranisi:

- `a4` -> A4 dikey.
- `a5` -> A5 yatay.
- bos/gecersiz -> A4 dikey.

Bubble workflow'da PDF acma:

```text
Open external website -> Result of step Create QuestPDF Order Slip's pdf_url
```

## Test Checklist

Production deploy sonrasi:

```bash
curl https://pdf-api.<domain>.com/health
```

A4 test:

- `print_style.paper_size = "a4"`
- PDF olcusu A4 dikey olmali.
- `pdf_url` tarayicida acilmali.

A5 test:

- `print_style.paper_size = "a5"`
- PDF olcusu A5 yatay olmali.
- `pdf_url` tarayicida acilmali.

Regression:

- `/render/order-slip-url` JSON response formatini korumali.
- `/render/order-slip` binary PDF endpoint'i calismaya devam etmeli.
- Missing required fields icin anlamli `400` response donmeli.

## Sonraki Iyilestirmeler

- R2 lifecycle rule ile eski PDF'leri otomatik silme.
- Structured logging.
- Request id / document id correlation.
- Bubble tarafinda hata mesajlarini kullaniciya temiz gosteren workflow.
- Cloudflare Analytics/WAF loglari ile endpoint kullanimi takibi.
