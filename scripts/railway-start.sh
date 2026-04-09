#!/usr/bin/env bash
# ============================================================
# سكريبت بدء تشغيل Railway
# يشغّل: Node.js API + Nginx في نفس الحاوية
# ============================================================

set -e

# المنفذ الخارجي (Railway يضبطه تلقائياً)
EXTERNAL_PORT="${PORT:-8080}"
# المنفذ الداخلي لـ Node.js
API_PORT="${API_INTERNAL_PORT:-3000}"

echo "[start] EXTERNAL_PORT=${EXTERNAL_PORT}, API_PORT=${API_PORT}"

# ── إعداد Nginx: استبدال المنافذ في ملف الإعداد ──────────────
sed -i "s/__PORT__/${EXTERNAL_PORT}/g"  /etc/nginx/conf.d/default.conf
sed -i "s/__API_PORT__/${API_PORT}/g"   /etc/nginx/conf.d/default.conf

# ── ترحيل قاعدة البيانات (إنشاء الجداول عند أول تشغيل) ──────
echo "[start] Running database migration..."
cd /app
node node_modules/.bin/drizzle-kit push \
  --config lib/db/drizzle.config.ts \
  --force 2>&1 | tail -5 || {
  echo "[start] Migration warning — continuing anyway"
}
echo "[start] Migration done"

# ── تشغيل Node.js API في الخلفية ─────────────────────────────
PORT="${API_PORT}" \
  node --enable-source-maps /app/artifacts/api-server/dist/index.mjs &
NODE_PID=$!
echo "[start] API server started (pid=${NODE_PID})"

# انتظار حتى يصبح API جاهزاً
MAX_WAIT=30
WAITED=0
until curl -sf "http://127.0.0.1:${API_PORT}/api/healthz" >/dev/null 2>&1; do
  sleep 1
  WAITED=$((WAITED + 1))
  [[ $WAITED -ge $MAX_WAIT ]] && {
    echo "[start] ERROR: API did not start in ${MAX_WAIT}s"
    kill $NODE_PID 2>/dev/null
    exit 1
  }
done
echo "[start] API is healthy"

# ── تشغيل Nginx في المقدمة (يبقي الحاوية حية) ───────────────
echo "[start] Starting Nginx on port ${EXTERNAL_PORT}"
nginx -g "daemon off;" &
NGINX_PID=$!

# انتظار أي من العمليتين ينتهي — إذا انتهى أحدهما أعد التشغيل
wait -n $NODE_PID $NGINX_PID 2>/dev/null || wait $NODE_PID $NGINX_PID

echo "[start] A process exited — shutting down"
kill $NODE_PID $NGINX_PID 2>/dev/null
exit 1
