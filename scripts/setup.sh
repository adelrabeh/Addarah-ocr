#!/usr/bin/env bash
# ============================================================
# دارة الملك عبدالعزيز — OCR Platform
# سكريبت التركيب الشامل
# ============================================================
# الاستخدام:
#   chmod +x scripts/setup.sh
#   ./scripts/setup.sh
# ============================================================

set -euo pipefail

RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'
BLUE='\033[0;34m'; BOLD='\033[1m'; NC='\033[0m'

info()    { echo -e "${BLUE}[INFO]${NC}  $*"; }
success() { echo -e "${GREEN}[OK]${NC}    $*"; }
warn()    { echo -e "${YELLOW}[WARN]${NC}  $*"; }
error()   { echo -e "${RED}[ERROR]${NC} $*"; exit 1; }
step()    { echo -e "\n${BOLD}══ $* ══${NC}"; }

echo -e "${BOLD}"
echo "╔══════════════════════════════════════════════════════════╗"
echo "║    دارة الملك عبدالعزيز — منظومة رقمنة الوثائق          ║"
echo "║    سكريبت التركيب الشامل                                  ║"
echo "╚══════════════════════════════════════════════════════════╝"
echo -e "${NC}"

# ── التحقق من المتطلبات ──────────────────────────────────────
step "التحقق من المتطلبات"

command -v docker  >/dev/null 2>&1 || error "Docker غير مثبت. راجع: https://docs.docker.com/get-docker/"
command -v docker compose >/dev/null 2>&1 || \
  docker-compose version >/dev/null 2>&1   || \
  error "Docker Compose غير مثبت."

DOCKER_COMPOSE="docker compose"
docker compose version >/dev/null 2>&1 || DOCKER_COMPOSE="docker-compose"

success "Docker و Docker Compose متوفران"

# ── إعداد ملف البيئة ─────────────────────────────────────────
step "إعداد ملف البيئة"

if [[ ! -f .env ]]; then
  if [[ ! -f .env.example ]]; then
    error "ملف .env.example غير موجود"
  fi
  cp .env.example .env
  warn "تم إنشاء .env من .env.example"
  warn "يجب تعديل القيم التالية في .env قبل المتابعة:"
  echo ""
  echo "  POSTGRES_PASSWORD  — كلمة مرور قاعدة البيانات"
  echo "  SESSION_SECRET     — أنشئه بـ: openssl rand -hex 64"
  echo "  GEMINI_API_KEY     — مفتاح Google AI Studio"
  echo ""
  read -rp "هل قمت بتعديل .env؟ اضغط Enter للمتابعة أو Ctrl+C للإلغاء..."
fi

# التحقق من القيم الأساسية
source .env 2>/dev/null || true

[[ "${POSTGRES_PASSWORD:-CHANGE_ME}" == "CHANGE_ME"* ]] && \
  error "يجب تغيير POSTGRES_PASSWORD في ملف .env"

[[ "${SESSION_SECRET:-CHANGE_ME}" == "CHANGE_ME"* ]] && \
  error "يجب تغيير SESSION_SECRET في ملف .env"

success "ملف .env جاهز"

# ── إنشاء مجلدات البيانات ────────────────────────────────────
step "إنشاء مجلدات البيانات"
mkdir -p data/uploads data/backups
success "المجلدات جاهزة"

# ── بناء الصور ───────────────────────────────────────────────
step "بناء صور Docker (قد يستغرق 3-5 دقائق)"
$DOCKER_COMPOSE build --no-cache
success "تم بناء الصور"

# ── تشغيل قاعدة البيانات وانتظار جاهزيتها ───────────────────
step "تشغيل قاعدة البيانات"
$DOCKER_COMPOSE up -d db
info "انتظار جاهزية قاعدة البيانات..."

MAX_WAIT=60
WAITED=0
until $DOCKER_COMPOSE exec -T db pg_isready -U "${POSTGRES_USER:-darah}" -d "${POSTGRES_DB:-darah_ocr}" >/dev/null 2>&1; do
  sleep 2
  WAITED=$((WAITED + 2))
  [[ $WAITED -ge $MAX_WAIT ]] && error "قاعدة البيانات لم تبدأ خلال ${MAX_WAIT} ثانية"
  echo -n "."
done
echo ""
success "قاعدة البيانات جاهزة"

# ── تهجير قاعدة البيانات ─────────────────────────────────────
step "إنشاء جداول قاعدة البيانات"
$DOCKER_COMPOSE run --rm db-migrate
success "تم إنشاء الجداول"

# ── تشغيل جميع الخدمات ───────────────────────────────────────
step "تشغيل جميع الخدمات"
$DOCKER_COMPOSE up -d
success "جميع الخدمات تعمل"

# ── انتظار صحة الـ API ────────────────────────────────────────
step "التحقق من صحة النظام"
info "انتظار جاهزية الـ API..."

MAX_WAIT=120
WAITED=0
HTTP_PORT="${HTTP_PORT:-80}"

until curl -sf "http://localhost:${HTTP_PORT}/api/healthz" >/dev/null 2>&1; do
  sleep 3
  WAITED=$((WAITED + 3))
  [[ $WAITED -ge $MAX_WAIT ]] && {
    warn "API لم يستجب خلال ${MAX_WAIT} ثانية — تحقق من اللوجات:"
    $DOCKER_COMPOSE logs --tail=30 api
    error "فشل التحقق"
  }
  echo -n "."
done
echo ""
success "النظام يعمل بشكل صحيح"

# ── ملخص ─────────────────────────────────────────────────────
echo ""
echo -e "${GREEN}${BOLD}"
echo "╔══════════════════════════════════════════════════════════╗"
echo "║                 ✅  التركيب مكتمل                        ║"
echo "╠══════════════════════════════════════════════════════════╣"
echo -e "║  ${NC}الرابط:       http://localhost:${HTTP_PORT}                        ${GREEN}${BOLD}║"
echo -e "║  ${NC}المستخدم:     admin                                     ${GREEN}${BOLD}║"
echo -e "║  ${NC}كلمة المرور:  Admin@1234  (غيّرها فور الدخول!)          ${GREEN}${BOLD}║"
echo "╠══════════════════════════════════════════════════════════╣"
echo -e "║  ${NC}أوامر مفيدة:                                           ${GREEN}${BOLD}║"
echo -e "║  ${NC}  docker compose logs -f       — متابعة اللوجات        ${GREEN}${BOLD}║"
echo -e "║  ${NC}  docker compose restart api   — إعادة تشغيل الـ API   ${GREEN}${BOLD}║"
echo -e "║  ${NC}  ./scripts/backup.sh          — نسخة احتياطية         ${GREEN}${BOLD}║"
echo "╚══════════════════════════════════════════════════════════╝"
echo -e "${NC}"
echo -e "${YELLOW}⚠️  تذكّر: غيّر كلمة مرور admin و operator فور أول تسجيل دخول${NC}"
echo ""
