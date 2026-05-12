# Unity CI Build

로컬 네트워크의 빌드 서버/PC에 SSH로 빌드를 요청하는 Unity 에디터 툴입니다. Android / iOS / Windows(StandaloneWindows64)를 지원하며, 빌드 서버로 Mac과 Windows 모두 사용할 수 있습니다.

## 설치

`Packages/manifest.json`에 아래 항목을 추가하세요.

```json
"com.dfunity.ci": "https://github.com/MyungJewon/Unity_CI-CD_Library.git"
```

특정 버전을 고정하려면 태그를 명시합니다.

```json
"com.dfunity.ci": "https://github.com/MyungJewon/Unity_CI-CD_Library.git#v0.1.1"
```

## 버전 특이사항

- v0.0.1: 첫 커밋
- v0.0.2: README 추가
- v0.0.3: SSH 기반 원격 빌드 서버 구조로 전환
- v0.1.0: 사용자 ID + 빌드 시간 기반 출력 폴더, 로그 영문화
- v0.1.1: 빌드PC의 빌드 수행전 git reset
- v0.1.2: Windows 빌드 서버 지원, Windows(StandaloneWindows64) 플랫폼 추가

## 요구사항

**개발 PC**
- Unity 6000.0 이상
- 빌드 서버와 SSH 키 인증 설정 완료

**빌드 서버 — Mac**
- Unity Hub + 빌드 대상 Unity 버전 설치 (`/Applications/Unity/Hub/Editor/<version>/`)
- 빌드할 Unity 프로젝트가 git으로 clone되어 있을 것
- 빌드할 Unity 프로젝트를 한번은 열어서 Library 폴더가 생성되어 있을 것
- Android SDK / Xcode (빌드 플랫폼에 따라)
- GitHub SSH 키 인증 설정 완료 (아래 참고)

**빌드 서버 — Windows**
- OpenSSH 서버 활성화 (Windows 설정 → 선택적 기능 → OpenSSH 서버)
- Git for Windows 설치 — 기본 경로 `C:\Program Files\Git\` 에 설치
- Unity Hub + 빌드 대상 Unity 버전 설치 (`C:\Program Files\Unity\Hub\Editor\<version>\`)
- 빌드할 Unity 프로젝트가 git으로 clone되어 있을 것
- 빌드할 Unity 프로젝트를 한번은 열어서 Library 폴더가 생성되어 있을 것
- Android SDK (Android 빌드 시)
- GitHub SSH 키 인증 설정 완료 (아래 참고)

> **경로 주의**: 빌드 서버가 Windows인 경우 프로젝트 경로와 출력 경로를 입력할 때 백슬래시(`\`) 대신 슬래시(`/`)를 사용하세요. (예: `C:/UnityProjects/MyGame`)

## SSH 설정

### 개발 PC → 빌드 서버

**Mac 서버**
```bash
# SSH 키 생성 (없으면)
ssh-keygen -t ed25519

# 빌드 서버에 공개키 등록 (최초 1회)
ssh-copy-id <user>@<server-ip>
```

**Windows 서버**

PowerShell (관리자)에서 실행합니다.
```powershell
# OpenSSH 서버 설치 확인
Get-WindowsCapability -Online | Where-Object Name -like 'OpenSSH.Server*'

# OpenSSH 서버 시작 및 자동 시작 등록
Start-Service sshd
Set-Service -Name sshd -StartupType Automatic
```

개발 PC에서 공개키를 복사합니다.
```bash
# 개발 PC에서 공개키 내용 확인
cat ~/.ssh/id_ed25519.pub
```

Windows 서버에서 아래 파일에 위 공개키를 붙여넣습니다.
```
C:\Users\<user>\.ssh\authorized_keys
```

### 빌드 서버 → GitHub

빌드 서버에서 직접 실행합니다.

**Mac 서버**
```bash
# SSH 키 생성
ssh-keygen -t ed25519 -C "buildserver"

# 공개키 출력 → GitHub Settings > SSH and GPG keys > New SSH key 에 등록
cat ~/.ssh/id_ed25519.pub

# 모든 레포에 SSH 자동 적용 (HTTPS URL도 SSH로 투명하게 변환)
git config --global url."git@github.com:".insteadOf "https://github.com/"
git config --global url."git@github.com:".insteadOf "https://<github-username>@github.com/"
```

**Windows 서버** (Git Bash에서 실행)
```bash
# SSH 키 생성
ssh-keygen -t ed25519 -C "buildserver"

# 공개키 출력 → GitHub Settings > SSH and GPG keys > New SSH key 에 등록
cat ~/.ssh/id_ed25519.pub

# 모든 레포에 SSH 자동 적용
git config --global url."git@github.com:".insteadOf "https://github.com/"
git config --global url."git@github.com:".insteadOf "https://<github-username>@github.com/"
```

## 사용법

1. Unity 메뉴에서 **Window > CI Build** 를 엽니다.
2. **SSH 설정** 폴드아웃을 열고 입력합니다.
   - **서버 OS** — 빌드 서버의 운영체제 (Mac / Windows)
   - **Host (IP)** — 빌드 서버의 IP 주소
   - **User** — 빌드 서버 사용자명
   - **SSH Key 경로** — 기본 위치(`~/.ssh/id_ed25519`) 사용 시 비워도 됩니다.
3. 나머지 항목을 입력합니다.
   - **사용자 ID** — 빌드 출력 폴더 이름에 사용될 식별자
   - **Build 프로젝트** — 빌드 서버에 clone된 Unity 프로젝트 경로
   - **Build 출력 폴더** — 빌드 결과물이 저장될 루트 경로
   - **브랜치** — Fetch 버튼으로 목록을 불러온 뒤 선택합니다.
   - **플랫폼** — Android / iOS / Windows
4. **Build** 버튼을 누르면 빌드가 시작되고 로그가 실시간으로 출력됩니다.

빌드 결과물은 `<출력 폴더>/<사용자ID>_<YYYYMMDD_HHMMSS>/<platform>/` 아래에 저장됩니다.  
빌드 로그는 `<출력 폴더>/<사용자ID>_<YYYYMMDD_HHMMSS>/Logs/<platform>_build.log`에 저장됩니다.

## 동작 방식

1. 개발 PC의 `build.sh`와 `BuildScript.cs.template`을 SSH stdin으로 빌드 서버에 전송합니다. (빌드 서버에 별도 설치 불필요)
2. 빌드 서버에서 `git fetch` 후 로컬 변경사항을 초기화(`git reset --hard` + `git clean -fd`)하고, 원격과 차이가 있으면 `git pull`합니다.
3. `ProjectSettings/ProjectVersion.txt`에서 Unity 버전을 감지합니다.
4. `BuildScript.cs`를 `Assets/Editor/`에 임시 주입 후 Unity를 `-batchmode`로 실행합니다.
5. 빌드 완료 후 주입한 파일을 자동으로 삭제합니다.

## 주의 사항

- 이 CI 툴은 Git과 연동하여 사용하는 것을 전제로 제작되었습니다. 빌드 수행 전 pull을 수행하므로, 로컬 작업 내용을 commit하지 않으면 빌드 서버에 반영되지 않습니다.
- 빌드 서버에 있는 로컬 변경사항은 빌드 시마다 자동으로 초기화됩니다.
- Unity가 기본 경로 이외에 설치된 경우 `Shell/build.sh`의 `UNITY_BIN` 경로를 직접 수정하세요.
