# Unity CI Build

로컬 네트워크의 Mac Mini 빌드 서버에 SSH로 빌드를 요청하는 Unity 에디터 툴입니다. Android / iOS를 지원합니다.

## 설치

`Packages/manifest.json`에 아래 항목을 추가하세요.

```json
"com.dfunity.ci": "https://github.com/MyungJewon/Unity_CI-CD_Library.git"
```

특정 버전을 고정하려면 태그를 명시합니다.

```json
"com.dfunity.ci": "https://github.com/MyungJewon/Unity_CI-CD_Library.git#v0.0.2"
```

## 버전 특이사항

v0.0.1: 첫 커밋
v0.0.2: readme 추가
v0.0.3: SSH 기반 원격 빌드 서버 구조로 전환

## 요구사항

**개발 PC**
- Unity 6000.0 이상
- Mac Mini와 SSH 키 인증 설정 완료

**Mac Mini (빌드 서버)**
- Unity Hub + 빌드 대상 Unity 버전 설치 (`/Applications/Unity/Hub/Editor/<version>/`)
- 빌드할 Unity 프로젝트가 git으로 clone되어 있을 것
- Android SDK / Xcode (빌드 플랫폼에 따라)

## SSH 설정

개발 PC에서 한 번만 실행하면 됩니다.

```bash
# SSH 키 생성 (없으면)
ssh-keygen -t ed25519

# Mac Mini에 공개키 등록
ssh-copy-id <user>@<macmini-ip>
```

## 사용법

1. Unity 메뉴에서 **Window > CI Build** 를 엽니다.
2. **SSH 설정** 폴드아웃을 열고 입력합니다.
   - **Host (IP)** — Mac Mini의 IP 주소
   - **User** — Mac Mini 사용자명
   - **SSH Key 경로** — 기본 위치(`~/.ssh/id_ed25519`) 사용 시 비워도 됩니다.
3. 나머지 항목을 입력합니다.
   - **Build 프로젝트** — Mac Mini에 clone된 Unity 프로젝트 경로
   - **Build 출력 폴더** — 빌드 결과물이 저장될 경로
   - **브랜치** — Fetch 버튼으로 목록을 불러온 뒤 선택합니다.
   - **플랫폼** — Android 또는 iOS
4. **Build** 버튼을 누르면 빌드가 시작되고 로그가 실시간으로 출력됩니다.

빌드 결과물은 `<출력 폴더>/<user>/<platform>/` 아래에 저장됩니다.
빌드 로그는 `<출력 폴더>/<user>/Logs/<platform>_build.log`에 저장됩니다.

## 동작 방식

1. 개발 PC의 `build.sh`를 SSH stdin으로 Mac Mini에 전송해 실행합니다. (Mac Mini에 별도 설치 불필요)
2. Mac Mini에서 `git fetch` 후 로컬과 리모트를 비교해 변경사항이 있으면 자동으로 `git pull`합니다.
3. 대상 프로젝트의 `ProjectSettings/ProjectVersion.txt`에서 Unity 버전을 감지합니다.
4. `BuildScript.cs`를 `Assets/Editor/`에 임시 주입 후 Unity를 `-batchmode`로 실행합니다.
5. 빌드 완료 후 주입한 파일을 자동으로 삭제합니다.
