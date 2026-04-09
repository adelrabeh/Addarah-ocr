# دليل النشر الشامل — منظومة رقمنة الوثائق
# دارة الملك عبدالعزيز

---

## نظرة عامة على البنية التقنية

```
┌─────────────────────────────────────────────────────────────┐
│                        خادم المؤسسة                         │
│                                                             │
│   ┌──────────────┐    ┌──────────────┐    ┌─────────────┐  │
│   │   Nginx       │    │  API Server  │    │ PostgreSQL  │  │
│   │  (واجهة +     │───▶│  (Node.js)   │───▶│   (قاعدة   │  │
│   │  reverse      │    │  port 8080   │    │  البيانات)  │  │
│   │  proxy)       │    │              │    │             │  │
│   │  port 80/443  │    │              │    │             │  │
│   └──────────────┘    └──────────────┘    └─────────────┘  │
│                               │                            │
│                        ┌──────┴──────┐                     │
│                        │  /data/     │                     │
│                        │  uploads/   │ ← الملفات المرفوعة  │
│                        │  (volume)   │                     │
│                        └─────────────┘                     │
└─────────────────────────────────────────────────────────────┘
              ▲
              │ HTTPS (port 443) أو HTTP (port 80)
              │
         المستخدمون (متصفح)
```

---

## متطلبات الخادم

| المورد | الحد الأدنى | الموصى به |
|--------|------------|-----------|
| المعالج | 4 cores | 8 cores |
| الذاكرة | 8 GB RAM | 16 GB RAM |
| التخزين | 100 GB SSD | 500 GB SSD |
| نظام التشغيل | Ubuntu 22.04 LTS | Ubuntu 22.04 LTS |
| الاتصال | إنترنت لـ Gemini AI | إنترنت لـ Gemini AI |

> كل صفحة PDF مرفوعة تستهلك ~2-5 MB في التخزين.

---

## الخطوة 1 — تثبيت Docker على الخادم

```bash
# تحديث النظام
sudo apt update && sudo apt upgrade -y

# تثبيت Docker (بأمر واحد)
curl -fsSL https://get.docker.com | sudo bash

# إضافة المستخدم الحالي لمجموعة docker (لتجنب كتابة sudo دائماً)
sudo usermod -aG docker $USER
newgrp docker

# التحقق من التثبيت
docker --version        # يجب أن يظهر: Docker version 24+
docker compose version  # يجب أن يظهر: Docker Compose version 2+
```

---

## الخطوة 2 — رفع كود المشروع للخادم

### الخيار أ — عبر GitHub (الأسهل)

```bash
# على الخادم
sudo mkdir -p /opt/darah-ocr
sudo chown $USER:$USER /opt/darah-ocr
cd /opt/darah-ocr

# اسحب الكود من GitHub
git clone https://github.com/اسمك/اسم-المستودع.git .
```

### الخيار ب — نقل ملفات مضغوطة (للشبكات المغلقة)

```bash
# على جهازك المحلي: ضغط المشروع
zip -r darah-ocr.zip . --exclude "node_modules/*" ".git/*" "*/dist/*"

# نقله للخادم عبر SCP
scp darah-ocr.zip ubuntu@عنوان-IP-الخادم:/opt/

# على الخادم: فك الضغط
sudo mkdir -p /opt/darah-ocr
sudo chown $USER:$USER /opt/darah-ocr
cd /opt/darah-ocr
unzip /opt/darah-ocr.zip
```

---

## الخطوة 3 — إعداد ملف البيئة

```bash
cd /opt/darah-ocr

# نسخ الملف النموذجي
cp .env.example .env

# تعديل القيم الإلزامية
nano .env
```

**القيم الإلزامية التي يجب تغييرها:**

```bash
# 1. كلمة مرور قاعدة البيانات (اختر كلمة مرور قوية)
POSTGRES_PASSWORD=كلمة_مرور_قوية_مثل_Xk9mP2nQ7r

# 2. مفتاح أمان الجلسات (أنشئه بهذا الأمر ثم انسخ النتيجة)
openssl rand -hex 64
SESSION_SECRET=الناتج_هنا

# 3. مفتاح Gemini AI (من https://aistudio.google.com/app/apikey)
GEMINI_API_KEY=AIzaSy...
```

> ⚠️ **تحذير:** لا ترفع ملف `.env` لـ GitHub أبداً. هو مدرج في `.gitignore`.

---

## الخطوة 4 — تشغيل النظام

```bash
cd /opt/darah-ocr

# تفعيل السكريبت وتشغيله (يعمل كل شيء تلقائياً)
chmod +x scripts/setup.sh
./scripts/setup.sh
```

السكريبت يقوم تلقائياً بـ:
1. التحقق من ملف .env
2. بناء صور Docker
3. تشغيل وإعداد قاعدة البيانات
4. تشغيل جميع الخدمات
5. التحقق من سلامة النظام

**النتيجة بعد الانتهاء:**

| | |
|--|--|
| الرابط | `http://عنوان-IP-الخادم` |
| المدير | `admin` / `Admin@1234` |
| المشغّل | `operator` / `Operator@1234` |

> ⚠️ **غيّر كلمات المرور فور أول دخول!**

---

## نشر على Oracle Cloud (OCI)

### 1. إنشاء جهاز افتراضي (Compute Instance)

من **Oracle Cloud Console**:
1. اذهب لـ **Compute → Instances → Create Instance**
2. الإعدادات:
   - **Image:** Ubuntu 22.04
   - **Shape:** VM.Standard.E4.Flex (4 OCPUs, 16 GB RAM) — للإنتاج
   - **Shape للاختبار:** VM.Standard.A1.Flex (4 ARM cores مجاناً في الـ Free Tier)
3. **Storage:** أضف Block Volume بحجم 200 GB على الأقل
4. **Networking:** أنشئ VCN جديد أو استخدم موجوداً
5. **SSH Keys:** أضف مفتاح SSH للدخول

### 2. فتح المنافذ في Oracle Cloud

بعد إنشاء الجهاز، يجب فتح المنافذ:

```
Oracle Console → Networking → Virtual Cloud Networks → اسم-VCN
→ Security Lists → Default Security List → Add Ingress Rules
```

| المصدر | البروتوكول | المنفذ | الوصف |
|--------|-----------|-------|------|
| 0.0.0.0/0 | TCP | 80 | HTTP |
| 0.0.0.0/0 | TCP | 443 | HTTPS |
| 0.0.0.0/0 | TCP | 22 | SSH (اختياري) |

**وأيضاً على الخادم نفسه:**

```bash
# Oracle Linux / Ubuntu
sudo iptables -I INPUT -p tcp --dport 80 -j ACCEPT
sudo iptables -I INPUT -p tcp --dport 443 -j ACCEPT
sudo netfilter-persistent save 2>/dev/null || sudo iptables-save > /etc/iptables/rules.v4
```

### 3. الاتصال بالخادم وتثبيت النظام

```bash
# الاتصال عبر SSH
ssh ubuntu@<عنوان-IP-العام>

# اتباع الخطوات 1-4 أعلاه
```

### 4. تثبيت Volume التخزين (اختياري للملفات الكبيرة)

```bash
# بعد إرفاق Block Volume من Oracle Console
sudo fdisk -l           # ابحث عن القرص الجديد (مثل /dev/sdb)
sudo mkfs.ext4 /dev/sdb
sudo mkdir -p /data
sudo mount /dev/sdb /data
echo '/dev/sdb /data ext4 defaults 0 0' | sudo tee -a /etc/fstab
```

ثم في `.env`:
```bash
# تعديل مسار التخزين
UPLOADS_DIR=/data/uploads
```

---

## نشر على Google Cloud (GCP)

### 1. إنشاء جهاز افتراضي (Compute Engine)

```bash
# أو من Google Cloud Console → Compute Engine → VM instances → Create
gcloud compute instances create darah-ocr-server \
  --machine-type=n2-standard-4 \
  --image-family=ubuntu-2204-lts \
  --image-project=ubuntu-os-cloud \
  --boot-disk-size=200GB \
  --boot-disk-type=pd-ssd \
  --zone=me-central1-a \
  --tags=http-server,https-server
```

### 2. فتح المنافذ في GCP

```bash
# فتح HTTP و HTTPS
gcloud compute firewall-rules create allow-http \
  --allow tcp:80 --target-tags http-server

gcloud compute firewall-rules create allow-https \
  --allow tcp:443 --target-tags https-server
```

### 3. استخدام Google Cloud Storage لتخزين الملفات

لتخزين الوثائق في GCS بدلاً من القرص المحلي:

**أنشئ Service Account:**
```bash
gcloud iam service-accounts create darah-ocr-storage \
  --display-name="Darah OCR Storage"

gcloud projects add-iam-policy-binding PROJECT_ID \
  --member="serviceAccount:darah-ocr-storage@PROJECT_ID.iam.gserviceaccount.com" \
  --role="roles/storage.objectAdmin"

# تحميل مفتاح JSON
gcloud iam service-accounts keys create ./gcs-key.json \
  --iam-account=darah-ocr-storage@PROJECT_ID.iam.gserviceaccount.com
```

**أنشئ Bucket:**
```bash
gsutil mb -l ME gs://darah-ocr-documents-$(date +%s)
```

**أضف لـ `.env`:**
```bash
GCS_BUCKET_NAME=darah-ocr-documents-XXXXX
GOOGLE_APPLICATION_CREDENTIALS=/opt/darah-ocr/gcs-key.json
```

> ملاحظة: بدون إعداد GCS، تُحفظ الملفات على القرص المحلي (كافٍ لبيئة الخادم الواحد).

---

## نشر على AWS

### 1. إنشاء EC2 Instance

من **AWS Console → EC2 → Launch Instance:**
- **AMI:** Ubuntu Server 22.04 LTS
- **Instance Type:** `t3.xlarge` (4 vCPU, 16 GB) أو `m6i.xlarge` للإنتاج
- **Storage:** 200 GB gp3 SSD
- **Security Group:** أضف قواعد HTTP (80) و HTTPS (443) و SSH (22)

### 2. الاتصال والتثبيت

```bash
# الاتصال عبر SSH
ssh -i your-key.pem ubuntu@<EC2-PUBLIC-IP>

# اتباع خطوات التثبيت العامة أعلاه
```

### 3. استخدام S3 لتخزين الملفات (اختياري)

```bash
# إنشاء Bucket
aws s3 mb s3://darah-ocr-documents --region me-south-1

# إنشاء IAM User مخصص
aws iam create-user --user-name darah-ocr-storage
aws iam attach-user-policy \
  --user-name darah-ocr-storage \
  --policy-arn arn:aws:iam::aws:policy/AmazonS3FullAccess
aws iam create-access-key --user-name darah-ocr-storage
```

أضف لـ `.env`:
```bash
AWS_S3_BUCKET=darah-ocr-documents
AWS_ACCESS_KEY_ID=AKIA...
AWS_SECRET_ACCESS_KEY=...
AWS_REGION=me-south-1
```

---

## نشر على Azure

### 1. إنشاء Virtual Machine

```bash
az group create --name darah-ocr-rg --location uaenorth

az vm create \
  --resource-group darah-ocr-rg \
  --name darah-ocr-vm \
  --image Ubuntu2204 \
  --size Standard_D4s_v3 \
  --admin-username ubuntu \
  --generate-ssh-keys

# فتح المنافذ
az vm open-port --port 80  --resource-group darah-ocr-rg --name darah-ocr-vm
az vm open-port --port 443 --resource-group darah-ocr-rg --name darah-ocr-vm
```

---

## تفعيل HTTPS (مطلوب للإنتاج)

### الخيار أ — Let's Encrypt (مجاني، يتطلب اسم نطاق)

```bash
# تثبيت Certbot
sudo apt install certbot -y

# الحصول على شهادة SSL
# (يجب أن يكون اسم النطاق يشير لعنوان IP الخادم أولاً)
sudo certbot certonly --standalone -d your-domain.sa

# نسخ الشهادات
sudo cp /etc/letsencrypt/live/your-domain.sa/fullchain.pem ./nginx/ssl/cert.pem
sudo cp /etc/letsencrypt/live/your-domain.sa/privkey.pem   ./nginx/ssl/key.pem
sudo chown $USER:$USER ./nginx/ssl/*.pem
```

ثم تعديل `nginx/darah-ocr.conf`:
```nginx
server {
    listen 80;
    server_name your-domain.sa;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl;
    server_name your-domain.sa;

    ssl_certificate     /etc/nginx/ssl/cert.pem;
    ssl_certificate_key /etc/nginx/ssl/key.pem;
    ssl_protocols       TLSv1.2 TLSv1.3;
    ssl_ciphers         HIGH:!aNULL:!MD5;

    # ... بقية الإعدادات كما هي
}
```

### الخيار ب — شهادة ذاتية (للشبكات الداخلية المغلقة)

```bash
mkdir -p nginx/ssl

# إنشاء شهادة ذاتية التوقيع (صالحة 10 سنوات)
openssl req -x509 -nodes -days 3650 -newkey rsa:4096 \
  -keyout nginx/ssl/key.pem \
  -out nginx/ssl/cert.pem \
  -subj "/C=SA/ST=Riyadh/L=Riyadh/O=Al-Darah/CN=darah-ocr.internal"
```

---

## إدارة النظام

### الأوامر اليومية

```bash
cd /opt/darah-ocr

# عرض حالة الخدمات
docker compose ps

# متابعة السجلات مباشرة
docker compose logs -f api

# إعادة تشغيل خدمة
docker compose restart api

# إيقاف النظام
docker compose down

# تشغيل النظام بعد إيقافه
docker compose up -d
```

### مراقبة الموارد

```bash
# استهلاك CPU والذاكرة
docker stats --no-stream

# حجم قاعدة البيانات
docker compose exec db psql -U darah -d darah_ocr -c "\l+"

# مساحة التخزين
df -h
docker volume ls
```

---

## النسخ الاحتياطي

### نسخ يدوي

```bash
cd /opt/darah-ocr
./scripts/backup.sh
```

تُحفظ النسخة في: `data/backups/`

### نسخ تلقائي يومي

```bash
# فتح جدول المهام
crontab -e

# إضافة هذا السطر (نسخ يومي الساعة 2:00 فجراً)
0 2 * * * cd /opt/darah-ocr && ./scripts/backup.sh >> /var/log/darah-backup.log 2>&1
```

### استعادة نسخة احتياطية

```bash
cd /opt/darah-ocr

# استعادة قاعدة البيانات
zcat data/backups/db_YYYYMMDD_HHMMSS.sql.gz | \
  docker compose exec -T db psql -U darah -d darah_ocr

# استعادة الملفات
docker run --rm \
  -v darah-ocr-uploads:/data \
  -v $(pwd)/data/backups:/backup \
  alpine tar xzf /backup/uploads_YYYYMMDD_HHMMSS.tar.gz -C /data
```

---

## التحديث إلى إصدار جديد

```bash
cd /opt/darah-ocr

# 1. نسخة احتياطية أولاً
./scripts/backup.sh

# 2. سحب الكود الجديد من GitHub
git pull origin main

# 3. إعادة البناء
docker compose build --no-cache

# 4. ترحيل قاعدة البيانات (إذا وجدت تغييرات)
docker compose run --rm db-migrate

# 5. إعادة تشغيل الخدمات
docker compose up -d

# 6. التحقق من الصحة
curl http://localhost/api/healthz
```

---

## استكشاف الأخطاء

### لا يمكن الوصول للنظام

```bash
# 1. تحقق من الخدمات
docker compose ps

# 2. تحقق من جدار الحماية (Ubuntu)
sudo ufw status
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp

# 3. تحقق من السجلات
docker compose logs --tail=50 frontend
docker compose logs --tail=50 api
```

### فشل OCR / خطأ Gemini

```bash
# تحقق من المفتاح
docker compose exec api env | grep GEMINI

# اختبار الاتصال
docker compose exec api curl -s https://generativelanguage.googleapis.com

# تحقق من اللوجات
docker compose logs api | grep -i "gemini\|ocr\|error"
```

### قاعدة البيانات لا تبدأ

```bash
# تحقق من السجلات
docker compose logs db

# تحقق من كلمة المرور في .env
grep POSTGRES_PASSWORD .env

# إعادة تهيئة قاعدة البيانات (تحذير: يحذف البيانات)
docker compose down -v
docker compose up -d
```

### بطء في المعالجة

```bash
# موارد الخادم
docker stats --no-stream

# عدد المعالجة المتوازية (في .env)
PARALLEL_BATCH_SIZE=5   # لـ Gemini Flash (افتراضي)
PARALLEL_BATCH_SIZE=2   # لـ Azure GPT-4o
```

---

## النشر في شبكة مغلقة (بدون إنترنت)

إذا كانت الشبكة الداخلية لا تصل لـ Gemini، هناك خياران:

**الخيار 1 — Azure OpenAI (شبكة خاصة)**
```bash
# في .env
OCR_ENGINE=azure
AZURE_OPENAI_ENDPOINT=https://RESOURCE.openai.azure.com
AZURE_OPENAI_KEY=...
AZURE_OPENAI_DEPLOYMENT=gpt-4o
```

**الخيار 2 — خادم AI محلي (يتطلب GPU)**
```bash
# تثبيت Ollama
curl -fsSL https://ollama.ai/install.sh | sh
ollama pull llama3.2-vision:11b
```
> تواصل مع فريق التطوير لتفعيل دعم Ollama في كود النظام.

---

## قائمة التحقق الأمنية

- [ ] تغيير `POSTGRES_PASSWORD` في `.env`
- [ ] تشغيل `openssl rand -hex 64` وحفظ الناتج في `SESSION_SECRET`
- [ ] تغيير كلمة مرور `admin` من لوحة الإدارة
- [ ] تغيير كلمة مرور `operator` من لوحة الإدارة
- [ ] تفعيل HTTPS إذا كان النظام متاحاً من خارج الشبكة الداخلية
- [ ] إعداد النسخ الاحتياطي التلقائي (cron)
- [ ] تقييد الوصول للمنفذ 5432 (PostgreSQL) — داخلي فقط
- [ ] مراجعة سجلات التدقيق بانتظام من لوحة الإدارة

---

## الدعم الفني

| | |
|--|--|
| فحص الصحة | `curl http://localhost/api/healthz` |
| متابعة الأخطاء | `docker compose logs -f api \| grep error` |
| سجلات التدقيق | متاحة من داخل النظام → لوحة الإدارة |
| التحديث التلقائي | `git pull && docker compose up -d --build` |
