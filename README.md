# Unity CI Build

Unity 프로젝트를 에디터 내에서 Android / iOS로 빌드할 수 있는 로컬 CI 툴입니다.

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

## 요구사항

- Unity 6000.0 이상
- macOS + Unity Hub 기본 경로에 Unity 설치
  (`/Applications/Unity/Hub/Editor/<version>/`)

## 사용법

1. Unity 메뉴에서 **Window > CI Build** 를 엽니다.
2. 세 가지 항목을 입력합니다.
   - **Build 프로젝트** — 빌드할 Unity 프로젝트의 루트 경로
   - **Build 출력 폴더** — 결과물(.apk / Xcode 프로젝트)이 저장될 경로
   - **플랫폼** — Android 또는 iOS
3. **Build** 버튼을 누르면 빌드가 시작되고 로그가 실시간으로 출력됩니다.

빌드 로그는 `<출력 폴더>/Logs/<platform>_build.log`에 저장됩니다.

## 동작 방식

빌드 버튼을 누르면 내부적으로 아래 순서로 실행됩니다.

1. 대상 프로젝트의 `ProjectSettings/ProjectVersion.txt`에서 Unity 버전을 감지합니다.
2. `BuildScript.cs`를 대상 프로젝트의 `Assets/Editor/`에 임시로 주입합니다.
3. Unity를 `-batchmode`로 실행해 `BuildScript.Build()`를 호출합니다.
4. 빌드 완료 후 주입한 파일을 자동으로 삭제합니다.
