# Hostinger KVM4 Deployment

## Current production note

Bu dokuman ilk Hostinger/Cloudflare Tunnel planini tarihsel olarak saklar. Guncel production yolu Cloudflare Tunnel degil:

```text
Namecheap A record -> Hostinger VPS -> Caddy
```

Authoritative runbook:

```text
docs/QUESTPDF_SERVER_RUNBOOK.md
```

Mobil React canli host'u da ayni VPS/Caddy hattina eklenmistir:

```text
https://m.hirdavat.ai -> host:/opt/hirdavat-mobile/current
                      -> caddy:/srv/hirdavat-mobile/current
```

Bu dokuman, QuestPDF API'yi ngrok yerine Hostinger KVM4 uzerinde Docker ve Cloudflare Tunnel ile yayinlamak icin uygulanacak production yoludur.

## 1. Hostinger VPS

1. hPanel uzerinden KVM4 kurulumunu tamamlayin.
2. OS olarak Ubuntu 24.04 64-bit secin.
3. SSH key ekleyin ve root password'u guvenli sekilde saklayin.
4. Hostinger firewall'da SSH disinda public inbound port acmayin. Cloudflare Tunnel outbound baglanti kuracagi icin 80/443 portlari gerekli degil.

## 2. Sunucu Hazirligi

SSH ile VPS'e girin:

```bash
ssh root@<server-ip>
```

Docker ve compose plugin kurun:

```bash
apt-get update
apt-get install -y ca-certificates curl gnupg
install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
chmod a+r /etc/apt/keyrings/docker.asc
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" > /etc/apt/sources.list.d/docker.list
apt-get update
apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
```

## 3. Cloudflare Tunnel

Cloudflare Zero Trust panelinde bir tunnel olusturun:

1. Zero Trust > Networks > Tunnels.
2. Create tunnel.
3. Connector type olarak Docker secin.
4. Token'i kopyalayin.
5. Public hostname ekleyin:
   - Hostname: `pdf-api.<domain>`
   - Service: `http://questpdf-api:5159`

Bu repo token tabanli remote-managed tunnel kullanir. Cloudflare'in guncel Docker ornegi de `cloudflare/cloudflared:latest tunnel --no-autoupdate run --token <TUNNEL_TOKEN>` formatindadir.

## 4. Uygulamayi Yayinlama

Sunucuda repo klasorune girin ve `.env` dosyasini olusturun:

```bash
cp .env.example .env
nano .env
```

Degerleri doldurun:

```text
QUESTPDF_API_KEY=<long-random-secret>
ALLOWED_ORIGINS=https://kodsuzai.bubbleapps.io,https://hirdavat.ai
PDF_RETENTION_HOURS=24
CLOUDFLARE_TUNNEL_TOKEN=<cloudflare-token>
```

Servisi baslatin:

```bash
docker compose up -d --build
```

Loglari kontrol edin:

```bash
docker compose ps
docker compose logs --tail=100 questpdf-api
docker compose logs --tail=100 cloudflared
```

## 5. Bubble Ayari

Bubble API Connector endpoint:

```text
POST https://pdf-api.<domain>/render/order-slip-url
```

Headers:

```text
Content-Type: application/json
X-Api-Key: <QUESTPDF_API_KEY>
```

Workflow:

```text
Open external website -> Result of step Create QuestPDF Order Slip's pdf_url
```

## 6. Dogrulama

Health endpoint public kalir:

```bash
curl https://pdf-api.<domain>/health
```

API key olmadan render endpoint'i `401` donmelidir:

```bash
curl -i -X POST https://pdf-api.<domain>/render/order-slip-url \
  -H "Content-Type: application/json" \
  --data-binary @examples/order-slip-valid.json
```

Dogru key ile PDF URL uretmelidir:

```bash
curl -i -X POST https://pdf-api.<domain>/render/order-slip-url \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: <QUESTPDF_API_KEY>" \
  --data-binary @examples/order-slip-valid.json
```

`PDF_RETENTION_HOURS` dolan local PDF dosyalari servis acilisinda ve yeni PDF uretimlerinde temizlenir.
