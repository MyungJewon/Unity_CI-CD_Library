#!/bin/bash
# Unity 프로젝트를 batchmode로 빌드하는 스크립트
# 실행 흐름: 인자 파싱 → Unity 버전 감지 → BuildScript.cs 주입 → 빌드 → 정리

# 에러 발생 시 즉시 중단 (set -e)
# 미정의 변수 사용 시 에러 (set -u)
# 파이프 중간 실패도 감지 (set -o pipefail)
set -euo pipefail

# ── 사용법 출력 ────────────────────────────────────────────────────────────────
usage() {
    echo "Usage: $0 -project <path> -output <path> -platform <android|ios>"
    exit 1
}

# ── 인자 파싱 ──────────────────────────────────────────────────────────────────
# CIBuildWindow.cs에서 -project / -output / -platform 세 인자를 전달받음
PROJECT_PATH=""   # 빌드 대상 Unity 프로젝트 경로
OUTPUT_PATH=""    # 빌드 결과물 출력 경로
PLATFORM=""       # 빌드 플랫폼 (android | ios)

while [[ $# -gt 0 ]]; do
    case "$1" in
        -project)  PROJECT_PATH="$2"; shift 2 ;;
        -output)   OUTPUT_PATH="$2";  shift 2 ;;
        -platform) PLATFORM="$2";     shift 2 ;;
        *) usage ;;
    esac
done

# 세 인자 중 하나라도 비어있으면 사용법 출력 후 종료
[[ -z "$PROJECT_PATH" || -z "$OUTPUT_PATH" || -z "$PLATFORM" ]] && usage

# ── Unity 버전 감지 ────────────────────────────────────────────────────────────
# Unity 프로젝트는 ProjectSettings/ProjectVersion.txt에 사용 버전을 기록함
# 예시 내용:
#   m_EditorVersion: 6000.4.0f1
#   m_EditorVersionWithRevision: 6000.4.0f1 (...)
VERSION_FILE="$PROJECT_PATH/ProjectSettings/ProjectVersion.txt"
if [[ ! -f "$VERSION_FILE" ]]; then
    echo "[CI] ERROR: ProjectVersion.txt not found at $VERSION_FILE"
    exit 1
fi

# grep으로 버전 줄 추출 → awk로 두 번째 필드(버전 문자열)만 가져옴
# tr -d '\r' : Windows에서 생성된 파일의 캐리지 리턴(\r) 제거
UNITY_VERSION=$(grep "m_EditorVersion:" "$VERSION_FILE" | awk '{print $2}' | tr -d '\r')

# ── Unity 실행 파일 경로 구성 ──────────────────────────────────────────────────
# ※ 주의: 아래 경로는 Mac + Unity Hub 기본 설치 환경에서만 유효함
#   - Mac 기본값 : /Applications/Unity/Hub/Editor/{버전}/Unity.app/Contents/MacOS/Unity
#   - Windows    : C:\Program Files\Unity\Hub\Editor\{버전}\Editor\Unity.exe
#   - 커스텀 경로에 Unity를 설치했다면 아래 경로를 직접 수정해야 함
UNITY_BIN="/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app/Contents/MacOS/Unity"

if [[ ! -f "$UNITY_BIN" ]]; then
    echo "[CI] ERROR: Unity $UNITY_VERSION not found at $UNITY_BIN"
    echo "[CI] Unity Hub의 설치 경로를 확인하거나 build.sh의 UNITY_BIN 경로를 수정하세요."
    exit 1
fi

echo "[CI] Unity version : $UNITY_VERSION"
echo "[CI] Project       : $PROJECT_PATH"
echo "[CI] Output        : $OUTPUT_PATH"
echo "[CI] Platform      : $PLATFORM"

# ── BuildScript.cs 주입 ────────────────────────────────────────────────────────
# BuildScript.cs.template은 CIEditor/Assets/ 에 영구 보관되는 원본
# 빌드 시 CIBuildTarget의 Assets/Editor/ 에 복사(주입)하고, 빌드 후 삭제함
# Unity는 Assets/Editor/ 안의 스크립트를 에디터 전용으로 인식함
# build.sh는 Shell/ 에 위치하므로 한 단계 위가 패키지 루트
TEMPLATE_DIR="$(cd "$(dirname "$0")/.." && pwd)/Templates"
TEMPLATE="$TEMPLATE_DIR/BuildScript.cs.template"

if [[ ! -f "$TEMPLATE" ]]; then
    echo "[CI] ERROR: BuildScript.cs.template not found at $TEMPLATE"
    exit 1
fi

EDITOR_DIR="$PROJECT_PATH/Assets/Editor"
INJECT_CS="$EDITOR_DIR/BuildScript.cs"
INJECT_META="$INJECT_CS.meta"  # Unity가 스크립트 임포트 시 자동 생성하는 메타파일

# Assets/Editor 폴더가 없으면 생성
mkdir -p "$EDITOR_DIR"
# 원본 템플릿을 빌드 대상 프로젝트에 복사
cp "$TEMPLATE" "$INJECT_CS"
echo "[CI] Injected BuildScript.cs -> $INJECT_CS"

# ── 정리 트랩 ──────────────────────────────────────────────────────────────────
# trap ... EXIT : 스크립트가 종료될 때 (성공·실패·크래시 모두) 반드시 실행됨
# 빌드 중 강제 종료되더라도 주입한 파일이 프로젝트에 남지 않도록 보장
cleanup() {
    echo "[CI] Cleaning up injected files..."
    rm -f "$INJECT_CS" "$INJECT_META"
    echo "[CI] Cleanup done."
}
trap cleanup EXIT

# ── 로그 디렉터리 생성 ─────────────────────────────────────────────────────────
# 빌드 로그를 플랫폼별로 분리해 저장
# 예: output/Logs/android_build.log, output/Logs/ios_build.log
LOG_DIR="$OUTPUT_PATH/Logs"
mkdir -p "$LOG_DIR"
LOG_FILE="$LOG_DIR/${PLATFORM}_build.log"

# ── Unity batchmode 빌드 실행 ──────────────────────────────────────────────────
echo "[CI] Starting build..."

"$UNITY_BIN" \
    -batchmode \               # GUI 없이 백그라운드 실행
    -nographics \              # 그래픽 초기화 생략 (렌더링 불필요)
    -projectPath "$PROJECT_PATH" \   # 빌드할 Unity 프로젝트 경로
    -executeMethod BuildScript.Build \  # 주입한 BuildScript.cs의 정적 메서드 호출
    -platform "$PLATFORM" \    # BuildScript.Build()가 읽는 커스텀 인자
    -output "$OUTPUT_PATH" \   # BuildScript.Build()가 읽는 커스텀 인자
    -logFile "$LOG_FILE" \     # Unity 상세 로그를 파일로 저장
    -quit                      # 빌드 완료 후 Unity 자동 종료

# Unity 종료 코드 저장 (-e 옵션이 있어도 이 변수 참조 전에 종료되지 않도록)
EXIT_CODE=$?

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
