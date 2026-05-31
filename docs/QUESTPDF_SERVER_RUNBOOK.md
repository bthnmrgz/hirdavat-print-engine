# QuestPDF Server Runbook

Bu dokuman, Hirdavat QuestPDF API sunucusunun nasil yayinlandigini, Bubble tarafindan nasil kullanildigini ve sorun aninda hangi kontrollerin yapilacagini anlatir.

## Canli Mimari

Su an kullanilan production akisi:

```text
Bubble API Connector
  -> https://pdf-api.hirdavat.ai
  -> Namecheap DNS A record
  -> Hostinger VPS <vps-public-ip>
  -> Caddy HTTPS reverse proxy
  -> Docker network
  -> questpdf-api:5159
```

VPS bilgileri:

```text
Provider: Hostinger VPS
OS: Ubuntu 24.04.4 LTS
Public IP: <vps-public-ip>
App path: /opt/hirdavat-print-engine
SSH user: root
SSH key on Mac: ~/.ssh/<deploy-key>
```

## DNS

`hirdavat.ai` DNS kayitlari Namecheap tarafinda yonetiliyor. Canli Bubble site kayitlari degistirilmedi.

QuestPDF API icin eklenen tek kayit:

```text
Type: A Record
Host: pdf-api
Value: <vps-public-ip>
TTL: Automatic
```

Bu kayit sadece `pdf-api.hirdavat.ai` adresini etkiler. `hirdavat.ai`, `www.hirdavat.ai`, Bubble kayitlari, email/MX ve TXT kayitlari ayni kalir.

DNS kontrolu:

```bash
dig +short pdf-api.hirdavat.ai A
dig @dns1.registrar-servers.com +short pdf-api.hirdavat.ai A
```

Beklenen sonuc:

```text
<vps-public-ip>
```

## Neden Cloudflare Tunnel Kullanilmiyor?

Ilk kurulumda Cloudflare Tunnel denendi. Tunnel connector VPS uzerinde calisti, ancak `hirdavat.ai` DNS'i Cloudflare nameserver'larinda degil Namecheap'te oldugu icin public CNAME akisi bekledigimiz gibi calismadi.

Deneme kaydi:

```text
pdf-api.hirdavat.ai -> <tunnel-id>.cfargotunnel.com
```

Public DNS tarafinda bu target private IPv6 benzeri bir deger dondurdu ve dis istekler timeout oldu. Canli Bubble domain'ini riske atmamak icin nameserver tasimasi yapilmadi.

Bu nedenle kullanilan yol:

```text
Namecheap A record -> VPS IP -> Caddy -> questpdf-api
```

Cloudflare panelinde `hirdavat-questpdf-api` tunnel'i `DOWN` gorunebilir. Bu normaldir; mevcut production akisi Cloudflare Tunnel kullanmaz.

## Docker Servisleri

Ana servisler:

```text
questpdf-api  ASP.NET 8 + QuestPDF API
caddy         HTTPS reverse proxy ve otomatik Let's Encrypt SSL
```

`cloudflared` servisi compose dosyasinda kalabilir, ancak production akista kullanilmiyor ve durdurulmus olmasi beklenir.

Durum kontrolu:

```bash
ssh -i ~/.ssh/<deploy-key> root@<vps-public-ip>
cd /opt/hirdavat-print-engine
docker compose ps
```

Beklenen:

```text
caddy         Up
questpdf-api  Up
cloudflared   Stopped veya absent
```

Loglar:

```bash
docker compose logs --tail=100 caddy
docker compose logs --tail=100 questpdf-api
```

Restart:

```bash
docker compose restart caddy questpdf-api
```

Rebuild/deploy:

```bash
docker compose up -d --build questpdf-api caddy
```

## Secrets ve Environment

Sunucudaki env dosyasi:

```text
/opt/hirdavat-print-engine/.env
```

Beklenen alanlar:

```text
QUESTPDF_API_KEY=<secret>
ALLOWED_ORIGINS=https://kodsuzai.bubbleapps.io,https://hirdavat.ai
PDF_RETENTION_HOURS=24
CLOUDFLARE_TUNNEL_TOKEN=<unused unless tunnel is re-enabled>
```

`QUESTPDF_API_KEY` repo veya dokumanlara commit edilmemelidir. Bubble API Connector'da header olarak kullanilir.

API key'i sunucuda gormek gerekirse:

```bash
ssh -i ~/.ssh/<deploy-key> root@<vps-public-ip>
cd /opt/hirdavat-print-engine
grep '^QUESTPDF_API_KEY=' .env
```

## Public Endpoints

Health:

```text
GET https://pdf-api.hirdavat.ai/health
```

Beklenen response:

```json
{
  "ok": true,
  "service": "hirdavat-questpdf"
}
```

PDF URL uretimi:

```text
POST https://pdf-api.hirdavat.ai/render/order-slip-url
```

Headers:

```text
Content-Type: application/json
X-Api-Key: <QUESTPDF_API_KEY>
```

Basarili response:

```json
{
  "ok": true,
  "pdf_url": "https://pdf-api.hirdavat.ai/files/...",
  "file_name": "...pdf",
  "content_type": "application/pdf",
  "size_bytes": 38357
}
```

API key eksik veya yanlissa `401` ve JSON hata govdesi doner:

```json
{
  "ok": false,
  "error": {
    "code": "unauthorized",
    "message": "X-Api-Key hatali veya eksik."
  }
}
```

## Bubble Kullanimi

Bubble API Connector'da call:

```text
Name: Create QuestPDF Order Slip
Method: POST
URL: https://pdf-api.hirdavat.ai/render/order-slip-url
Use as: Action
Data type: JSON
```

Headers:

```text
Content-Type: application/json
X-Api-Key: <QUESTPDF_API_KEY>
```

Body ayni `order_slip` JSON contract'ini kullanir. Response icindeki `pdf_url`, Bubble workflow'da dis site olarak acilir:

```text
Open external website -> Result of step Create QuestPDF Order Slip's pdf_url
```

## Test Komutlari

Health:

```bash
curl -i https://pdf-api.hirdavat.ai/health
```

API key olmadan 401 beklenir:

```bash
curl -i -X POST https://pdf-api.hirdavat.ai/render/order-slip-url \
  -H "Content-Type: application/json" \
  --data-binary @examples/order-slip-valid.json
```

API key ile PDF URL beklenir:

```bash
cd /opt/hirdavat-print-engine
set -a
. ./.env
set +a
curl -i -X POST https://pdf-api.hirdavat.ai/render/order-slip-url \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: $QUESTPDF_API_KEY" \
  --data-binary @examples/order-slip-valid.json
```

DNS cache gecikmesi varsa, test icin dogrudan IP cozumlemesi kullanilabilir:

```bash
curl --resolve pdf-api.hirdavat.ai:443:<vps-public-ip> \
  -i https://pdf-api.hirdavat.ai/health
```

## Dosya Saklama

PDF'ler container icinde `generated-pdfs` klasorune yazilir ve Docker volume ile tutulur:

```text
questpdf-generated-pdfs
```

Public dosya path'i:

```text
https://pdf-api.hirdavat.ai/files/<file-name>.pdf
```

Temizlik davranisi:

```text
PDF_RETENTION_HOURS=24
```

Servis yeni PDF uretimlerinde ve acilista eski PDF'leri temizler.

## Sorun Giderme

DNS eski CNAME'i donduruyorsa:

```bash
dig +short pdf-api.hirdavat.ai CNAME
dig @dns1.registrar-servers.com +short pdf-api.hirdavat.ai A
```

Authoritative Namecheap sonucu dogru ama public resolver eskiyse DNS cache beklenir.

SSL sorunu varsa:

```bash
docker compose logs --tail=100 caddy
```

Basarili sertifika logu:

```text
certificate obtained successfully
```

API ayakta degilse:

```bash
docker compose ps
docker compose logs --tail=100 questpdf-api
docker compose restart questpdf-api
```

Public endpoint 401 donuyorsa:

```text
X-Api-Key header eksik veya yanlis.
```

Public endpoint 502/connection error donuyorsa:

```text
Caddy questpdf-api container'ina ulasamiyor olabilir.
docker compose ps
docker compose restart caddy questpdf-api
```
