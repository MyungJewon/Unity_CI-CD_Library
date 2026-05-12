#!/bin/bash
set -euo pipefail

# ── Variable check ─────────────────────────────────────────────────────────────
BRANCH="${BRANCH:-main}"
USER_ID="${USER_ID:-unknown}"

if [[ -z "${PROJECT_PATH:-}" || -z "${OUTPUT_PATH:-}" || -z "${PLATFORM:-}" ]]; then
    echo "[CI] ERROR: PROJECT_PATH, OUTPUT_PATH, PLATFORM are not set."
    exit 1
fi

# ── Git sync ───────────────────────────────────────────────────────────────────
if [[ ! -d "$PROJECT_PATH/.git" ]]; then
    echo "[CI] ERROR: $PROJECT_PATH is not a git repository."
    exit 1
fi

echo "[CI] git fetch origin..."
git -C "$PROJECT_PATH" fetch origin

# Local changes check — reset before pull to ensure clean state
if [[ -n "$(git -C "$PROJECT_PATH" status --porcelain)" ]]; then
    echo "[CI] Local changes detected — resetting to clean state..."
    git -C "$PROJECT_PATH" reset --hard HEAD
    git -C "$PROJECT_PATH" clean -fd
    echo "[CI] Reset complete."
fi

LOCAL=$(git -C "$PROJECT_PATH" rev-parse "$BRANCH")
REMOTE=$(git -C "$PROJECT_PATH" rev-parse "origin/$BRANCH")

if [[ "$LOCAL" != "$REMOTE" ]]; then
    echo "[CI] Changes detected — git pull origin $BRANCH"
    git -C "$PROJECT_PATH" pull origin "$BRANCH"
else
    echo "[CI] Already up to date ($BRANCH)"
fi

# ── Detect Unity version ───────────────────────────────────────────────────────
VERSION_FILE="$PROJECT_PATH/ProjectSettings/ProjectVersion.txt"
if [[ ! -f "$VERSION_FILE" ]]; then
    echo "[CI] ERROR: ProjectVersion.txt not found at $VERSION_FILE"
    exit 1
fi

UNITY_VERSION=$(grep "m_EditorVersion:" "$VERSION_FILE" | awk '{print $2}' | tr -d '\r')

SERVER_OS="${SERVER_OS:-mac}"
if [[ "$SERVER_OS" == "windows" ]]; then
    UNITY_BIN="C:/Program Files/Unity/Hub/Editor/$UNITY_VERSION/Editor/Unity.exe"
else
    UNITY_BIN="/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app/Contents/MacOS/Unity"
fi

if [[ ! -f "$UNITY_BIN" ]]; then
    echo "[CI] ERROR: Unity $UNITY_VERSION not found at $UNITY_BIN"
    exit 1
fi

echo "[CI] Unity version : $UNITY_VERSION"
echo "[CI] Project       : $PROJECT_PATH"
echo "[CI] Output        : $OUTPUT_PATH"
echo "[CI] Platform      : $PLATFORM"

# ── Inject BuildScript.cs ──────────────────────────────────────────────────────
TEMPLATE="$TEMPLATE_PATH"

if [[ ! -f "$TEMPLATE" ]]; then
    echo "[CI] ERROR: BuildScript.cs.template not found at $TEMPLATE"
    exit 1
fi

EDITOR_DIR="$PROJECT_PATH/Assets/Editor"
INJECT_CS="$EDITOR_DIR/BuildScript.cs"
INJECT_META="$INJECT_CS.meta"

mkdir -p "$EDITOR_DIR"
cp "$TEMPLATE" "$INJECT_CS"
echo "[CI] Injected BuildScript.cs -> $INJECT_CS"

# ── Cleanup trap ───────────────────────────────────────────────────────────────
cleanup() {
    echo "[CI] Cleaning up injected files..."
    rm -f "$INJECT_CS" "$INJECT_META"
    echo "[CI] Cleanup done."
}
trap cleanup EXIT

# ── Output subfolder: <USER_ID>_<timestamp> ────────────────────────────────────
BUILD_TIMESTAMP=$(date +%Y%m%d_%H%M%S)
OUTPUT_PATH="$OUTPUT_PATH/${USER_ID}_${BUILD_TIMESTAMP}"
mkdir -p "$OUTPUT_PATH"
echo "[CI] Build output : $OUTPUT_PATH"

# ── Log directory ──────────────────────────────────────────────────────────────
LOG_DIR="$OUTPUT_PATH/Logs"
mkdir -p "$LOG_DIR"
LOG_FILE="$LOG_DIR/${PLATFORM}_build.log"

# ── Unity batchmode 빌드 실행 ──────────────────────────────────────────────────
echo "[CI] Starting build..."

# set -e 를 잠시 해제 — Unity 실패 시 에러 요약을 출력하기 위해
set +e
"$UNITY_BIN" \
    -batchmode \
    -nographics \
    -projectPath "$PROJECT_PATH" \
    -executeMethod BuildScript.Build \
    -platform "$PLATFORM" \
    -output "$OUTPUT_PATH" \
    -logFile "$LOG_FILE" \
    -quit
EXIT_CODE=$?
set -e

# ── 결과 출력 ─────────────────────────────────────────────────────────────────
if [[ $EXIT_CODE -eq 0 ]]; then
    echo "[CI] Build succeeded. Log: $LOG_FILE"
else
    echo "[CI] Build FAILED (exit $EXIT_CODE). Log: $LOG_FILE"
    # 로그 파일에서 에러 관련 줄만 추출해 CI 창에 요약 출력
    # grep 패턴: ERROR: / FAILED / Exception / C# 컴파일 에러(error CS)
    echo "[CI] -- Error Summary --"
    grep -E "ERROR:|FAILED|Exception|error CS" "$LOG_FILE" 2>/dev/null | sed 's/^/[CI] /' || true
    echo "[CI] -----------------------"
    exit $EXIT_CODE
fi
