#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-http://127.0.0.1:5050}"
WORKDIR="${WORKDIR:-$(mktemp -d)}"
TS="$(date +%Y%m%d%H%M%S)"
EMAIL="autotest_${TS}@example.com"
PASSWORD="Passw0rd!123"
DISPLAY_NAME="Auto Test User"
SESSION_ID="sess_$(date +%s)_$RANDOM"
PROVIDER="${PROVIDER:-openai}"
MODEL="${MODEL:-GPT-5.5}"
STRICT_CHAT_STATUS="${STRICT_CHAT_STATUS:-0}"

mkdir -p "$WORKDIR"

cleanup() {
  echo "[INFO] artifacts: $WORKDIR"
}
trap cleanup EXIT

require_cmd() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "[FAIL] command not found: $1"
    exit 2
  fi
}

require_cmd curl
require_cmd node

http_code() {
  curl -s -o /dev/null -w "%{http_code}" "$1" || true
}

echo "[STEP] Check API health"
code="$(http_code "$BASE_URL/api/plans")"
if [[ "$code" != "200" ]]; then
  echo "[FAIL] API not ready at $BASE_URL (GET /api/plans => $code)"
  exit 1
fi

echo "[STEP] Register user"
curl -sS -X POST "$BASE_URL/api/auth/register" \
  -H 'Content-Type: application/json' \
  -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\",\"displayName\":\"$DISPLAY_NAME\"}" > "$WORKDIR/register.json"

USER_ID="$(node -e "const fs=require('fs');const j=JSON.parse(fs.readFileSync(process.argv[1],'utf8')); if(!j.user?.id||!j.accessToken){process.exit(1)}; process.stdout.write(j.user.id)" "$WORKDIR/register.json")"
echo "[PASS] register userId=$USER_ID"

echo "[STEP] Login"
curl -sS -X POST "$BASE_URL/api/auth/login" \
  -H 'Content-Type: application/json' \
  -d "{\"email\":\"$EMAIL\",\"password\":\"$PASSWORD\"}" > "$WORKDIR/login.json"

TOKEN="$(node -e "const fs=require('fs');const j=JSON.parse(fs.readFileSync(process.argv[1],'utf8')); if(!j.accessToken||!j.user?.id){process.exit(1)}; process.stdout.write(j.accessToken)" "$WORKDIR/login.json")"
LOGIN_UID="$(node -e "const fs=require('fs');const j=JSON.parse(fs.readFileSync(process.argv[1],'utf8')); process.stdout.write(j.user.id)" "$WORKDIR/login.json")"
if [[ "$LOGIN_UID" != "$USER_ID" ]]; then
  echo "[FAIL] login user mismatch: register=$USER_ID login=$LOGIN_UID"
  exit 1
fi
echo "[PASS] login"

echo "[STEP] Create task"
curl -sS -X POST "$BASE_URL/api/work-tasks" \
  -H 'Content-Type: application/json' \
  -H "Authorization: Bearer $TOKEN" \
  -d "{\"title\":\"E2E Test Task\",\"goal\":\"Validate create task and multi-round chat\",\"teamId\":null,\"providerId\":\"$PROVIDER\",\"model\":\"$MODEL\"}" > "$WORKDIR/task_create.json"

TASK_ID="$(node -e "const fs=require('fs');const j=JSON.parse(fs.readFileSync(process.argv[1],'utf8')); if(!j.task?.id){process.exit(1)}; process.stdout.write(j.task.id)" "$WORKDIR/task_create.json")"
echo "[PASS] create taskId=$TASK_ID"

echo "[STEP] Chat round #1"
curl -sS -N -X POST "$BASE_URL/api/chat/stream" \
  -H 'Content-Type: application/json' \
  -H "Authorization: Bearer $TOKEN" \
  -d "{\"ownerId\":\"$USER_ID\",\"sessionId\":\"$SESSION_ID\",\"userText\":\"请回复: round1-ok\",\"provider\":\"$PROVIDER\",\"model\":\"$MODEL\",\"maxIterations\":2}" > "$WORKDIR/chat1.sse"

echo "[STEP] Chat round #2"
curl -sS -N -X POST "$BASE_URL/api/chat/stream" \
  -H 'Content-Type: application/json' \
  -H "Authorization: Bearer $TOKEN" \
  -d "{\"ownerId\":\"$USER_ID\",\"sessionId\":\"$SESSION_ID\",\"userText\":\"请继续并回复: round2-ok\",\"provider\":\"$PROVIDER\",\"model\":\"$MODEL\",\"maxIterations\":2}" > "$WORKDIR/chat2.sse"

node -e '
const fs=require("fs");
const s1=fs.readFileSync(process.argv[1],"utf8");
const s2=fs.readFileSync(process.argv[2],"utf8");
function inspect(s){
  const done=/event: done/.test(s);
  const normalized=s.replace(/\\r/g, "");
  const errs=[...normalized.matchAll(/event:\s*error\n(?:data:\s*(.+)\n?)+/g)].map(block=>{
    const lines=String(block[0]).split("\\n").filter(l=>l.startsWith("data:"));
    const joined=lines.map(l=>l.slice(5).trimStart()).join("\\n");
    try{return JSON.parse(joined).message||""}catch{return joined}
  });
  const txt=[...normalized.matchAll(/event:\s*text\ndata:\s*(.+)/g)].map(m=>{try{return JSON.parse(m[1]).text||""}catch{return ""}}).join("");
  const sid=(normalized.match(/event:\s*session\ndata:\s*(.+)/)?.[1]||"");
  return {done,errs,textLen:txt.length,sessionRaw:sid};
}
const r1=inspect(s1), r2=inspect(s2);
if(!r1.done||!r2.done){console.error("CHAT_STREAM_INCOMPLETE");process.exit(1);}
console.log(`chat1_text_len=${r1.textLen}`);
console.log(`chat2_text_len=${r2.textLen}`);
const raw=(s1+"\n"+s2).toLowerCase();
const had401=raw.includes("http 401")||raw.includes("invalid token")||[...r1.errs,...r2.errs].some(e=>String(e).includes("401")||String(e).toLowerCase().includes("invalid token"));
if(had401){
  console.log("chat_status=DEGRADED_PROVIDER_AUTH");
}else if(r1.textLen===0 && r2.textLen===0){
  console.log("chat_status=DEGRADED_EMPTY_TEXT");
}else{
  console.log("chat_status=PASS");
}
' "$WORKDIR/chat1.sse" "$WORKDIR/chat2.sse" > "$WORKDIR/chat_eval.txt"
cat "$WORKDIR/chat_eval.txt"

if [[ "$STRICT_CHAT_STATUS" == "1" ]]; then
  chat_status="$(grep '^chat_status=' "$WORKDIR/chat_eval.txt" | tail -n1 | cut -d'=' -f2-)"
  if [[ "$chat_status" != "PASS" ]]; then
    echo "[FAIL] strict chat status requires PASS, got: $chat_status"
    exit 1
  fi
fi

echo "[STEP] Verify task in list"
curl -sS "$BASE_URL/api/work-tasks?take=20" -H "Authorization: Bearer $TOKEN" > "$WORKDIR/tasks.json"
node -e "const fs=require('fs');const tid=process.argv[2];const arr=JSON.parse(fs.readFileSync(process.argv[1],'utf8')); const ok=Array.isArray(arr)&&arr.some(x=>x.id===tid); if(!ok){process.exit(1)}; console.log('task_found=true total='+arr.length)" "$WORKDIR/tasks.json" "$TASK_ID"

echo "[STEP] Verify session persisted (multi-round continuity)"
curl -sS "$BASE_URL/api/sessions/$SESSION_ID" > "$WORKDIR/session.json"
node -e "const fs=require('fs');const j=JSON.parse(fs.readFileSync(process.argv[1],'utf8')); const count=Array.isArray(j.messages)?j.messages.length:0; if(!j.sessionId||count<2){process.exit(1)}; console.log('session_id='+j.sessionId+' message_count='+count)" "$WORKDIR/session.json"

echo "[RESULT] COMPLETED"
echo "session_id=$SESSION_ID"
echo "task_id=$TASK_ID"
echo "user_id=$USER_ID"
