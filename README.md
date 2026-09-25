# Unity C# Game

Unity 기반 C# 게임 프로젝트입니다.

## 요구 사항

- **Unity 6000.6.3f1** — [Unity Hub](https://unity.com/download)에서 정확히 이 버전을 설치 (프로젝트는 2D URP 템플릿 기반)
- Git

## 시작하기

1. 이 저장소를 클론합니다.
2. Unity Hub에서 **Add** → 클론한 폴더를 선택해 프로젝트를 엽니다.
3. `Assets/Scenes/SampleScene.unity` 씬을 열고 Play를 누릅니다.

## 폴더 구조

```
Assets/
  Scenes/      씬 파일
  Scripts/     C# 스크립트
  Prefabs/     프리팹
  Materials/   머티리얼
  Settings/    URP 2D 렌더러, 볼륨 프로필, Input System 액션
Packages/      패키지 매니페스트 (URP, 2D 패키지, Input System 포함)
ProjectSettings/
```

## 컨벤션

- 스크립트는 `Assets/Scripts` 아래에 기능 단위로 폴더를 나눕니다.
- 클래스와 파일 이름은 PascalCase, 비공개 필드는 `_camelCase`를 사용합니다.
- 씬과 프리팹은 Git LFS 없이도 병합할 수 있도록 **Force Text** 직렬화를 사용합니다.
- 클론 후 아래 한 줄로 UnityYAMLMerge를 등록하면 씬/프리팹 충돌이 자동으로 병합됩니다 (경로는 본인 Unity 설치 위치로):

  ```bash
  git config merge.unityyamlmerge.driver "'/Applications/Unity/Hub/Editor/6000.6.3f1/Unity.app/Contents/Helpers/UnityYAMLMerge' merge -p --force %O %B %A %A"
  ```
