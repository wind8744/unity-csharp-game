# 쪼꼬미 공성전 (Chokkomi Siege)

귀여운 타워로 막고, 뽑은 유닛을 보내 상대 성을 무너뜨리는 **실시간 공격 디펜스** (스타크래프트 유즈맵 "공격 디펜스" 참고). 1v1 / 2v2 / 3v3 (혼자면 봇 팀원, 온라인이면 친구와 LAN/IP 직결 락스텝), 타워 합성·별 합치기, 증강·이벤트·비밀 미션·시너지. Unity 6, C#.

규칙: [Docs/design/core-rules-v0.4.md](Docs/design/core-rules-v0.4.md) (기본은 [v0.3](Docs/design/core-rules-v0.3.md)).

## 요구 사항

- **Unity 6000.6.3f1** — [Unity Hub](https://unity.com/download)에서 정확히 이 버전 (2D URP 템플릿 기반)
- Git
- (자산을 다시 만들 때만) Python 3 + `pip3 install Pillow numpy`

## 시작하기

1. 저장소를 클론하고 Unity Hub에서 **Add** → 폴더 선택.
2. `Assets/Scenes/Main.unity` 를 열고 Play. 타이틀에서 모드를 고른다.
3. 조작: 타워 상점 클릭(또는 Q~O) → 내 진영 경로 옆 빈 칸 클릭 = 짓기(굽이 안쪽 구석이 효율적) · 타워 클릭 = 강화/판매/★합치기/합성 · 우클릭 = 판매 · D = 뽑기 · 카드 클릭 = 보내기 · 스페이스 = 정지 · 1/2/3 = 배속 · ESC = 메뉴.

## 폴더 구조

```
Assets/
  Scenes/Main.unity            유일한 씬 (카메라 + GameFlow). 나머지는 실행 중 코드가 만든다
  Scripts/Core/                순수 C# 결정론 시뮬레이션 (Unity 의존 없음, 락스텝 준비)
    Wave/WaveDefs.cs           타워 17종(기본 9 + 합성 8)·유닛 11종·웨이브 시간표·레시피
    Wave/MapDef.cs             맵 (ASCII 경로 + 자리 칸, 뱀 맵 생성기, 자리별 경로 커버 수)
    Wave/LaneSim.cs            맵 하나: 타워 사격, 유닛 경로 이동·누수, 시너지, 별 합치기·합성
    Wave/MatchSim.cs           한 판: 두 라인, 골드·인컴·손패, 증강·이벤트·미션, 명령 처리
    Wave/FunDefs.cs            증강·이벤트·미션 정의
    Wave/SimpleBot.cs          봇 (팀원·상대)
    Meta/Profile.cs            전적·해금 (조건 비공개, 비밀 증강·맵·칭호)
    Net/Wire.cs                온라인 메시지 직렬화
    Net/Transport.cs           TCP 직결 전송 (+ 테스트용 루프백)
    Net/NetSession.cs          로비 + 호스트 중계 락스텝 (턴마다 명령 교환, 해시 검증)
  Scripts/Game/                Unity 표현층
    GameFlow.cs                타이틀 ↔ 경기
    TitleScreen.cs             타이틀 (모드 선택, 온라인, 해금 도감·맵·봇 선택, 게임 방법, 합성표, 소리)
    ProfileStore.cs            프로필 저장 (persistentDataPath/profile.txt)
    OnlineLobby.cs             온라인 로비 (방 만들기 / 참가, 팀 배치, 채팅)
    MatchView.cs               경기 HUD·입력·행동 패널·증강·결과
    LaneRenderer.cs            라인 그리기: 스프라이트 애니메이션, 투사체, 이펙트, 데미지 숫자, 효과음
    Art.cs / Sfx.cs / UiKit.cs 그림·소리 로더, 코드 UI 도우미
  Scripts/Editor/              씬 생성, 아트 임포트 설정, 맥 빌드 (메뉴 LaneBattle/…)
  Resources/Sprites, Audio     생성된 그림 151장, 소리 36개
  Tests/EditMode, PlayMode     NUnit 테스트 (규칙·결정론·합성 / 화면 스모크)
Tools/art/gen_sprites.py       그림 생성 스크립트 (Pillow)
Tools/audio/gen_audio.py       소리 생성 스크립트 (numpy)
Docs/design/                   규칙 문서
```

## 자주 쓰는 명령

Unity 에디터를 닫은 상태에서 (배치 모드는 에디터와 동시에 못 연다):

```bash
# 그림·소리 다시 만들기
python3 Tools/art/gen_sprites.py && python3 Tools/audio/gen_audio.py

# 테스트
/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath . -runTests -testPlatform EditMode -testResults /tmp/edit.xml -logFile /tmp/edit.log
/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/MacOS/Unity -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults /tmp/play.xml -logFile /tmp/play.log

# 맥 빌드 → Builds/mac/ChokkomiSiege.app
/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath . -executeMethod LaneBattle.Editor.PlayerBuilder.BuildMac -quit -logFile /tmp/build.log
```

빌드된 앱은 `-mode 1|2|3` (타이틀 건너뛰기), `-bots` (사람 자리도 봇), `-matchtime 초`, `-screenshot 경로`, `-online -host` (로비에서 바로 방 만들기) 옵션으로 헤드리스 확인이 된다.

## 온라인으로 같이 하기

1. 한 명이 타이틀 → **온라인 대전** → 이름 입력 → **방 만들기**. 화면에 뜨는 주소(예: 192.168.0.12)를 친구에게 알려준다.
2. 친구는 온라인 대전 → 주소 입력 → **참가**.
3. 호스트가 인원(1v1·2v2·3v3)과 팀 배치를 정하고 **시작**. 빈 자리는 봇이 맡는다.
4. 같은 와이파이가 아니면 호스트 공유기에서 TCP 27015 포트를 열어야 한다. macOS 방화벽이 물어보면 허용.

## 컨벤션

- 스크립트는 `Assets/Scripts` 아래에 기능 단위로 폴더를 나눕니다.
- 클래스와 파일 이름은 PascalCase, 비공개 필드는 `_camelCase`를 사용합니다.
- 규칙 로직은 `Core` 에만 (정수 연산, 결정론). 표현은 `Game` 에만.
- 씬과 프리팹은 **Force Text** 직렬화. 클론 후 아래 한 줄로 UnityYAMLMerge를 등록하면 씬/프리팹 충돌이 자동 병합됩니다:

  ```bash
  git config merge.unityyamlmerge.driver "'/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/Helpers/UnityYAMLMerge' merge -p --force %O %B %A %A"
  ```
