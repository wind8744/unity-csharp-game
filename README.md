# Unity C# Game

Unity 기반 C# 게임 프로젝트입니다.

## 요구 사항

- Unity 6 (6000.x) 이상 — [Unity Hub](https://unity.com/download)로 설치
- Git

## 시작하기

1. 이 저장소를 클론합니다.
2. Unity Hub에서 **Add** → 클론한 폴더를 선택해 프로젝트를 엽니다.
3. `Assets/Scenes/Main.unity` 씬을 열고 Play를 누릅니다.

## 폴더 구조

```
Assets/
  Scenes/      씬 파일
  Scripts/     C# 스크립트
  Prefabs/     프리팹
  Materials/   머티리얼
Packages/      패키지 매니페스트
ProjectSettings/
```

## 컨벤션

- 스크립트는 `Assets/Scripts` 아래에 기능 단위로 폴더를 나눕니다.
- 클래스와 파일 이름은 PascalCase, 비공개 필드는 `_camelCase`를 사용합니다.
- 씬과 프리팹은 Git LFS 없이도 병합할 수 있도록 **Force Text** 직렬화를 사용합니다.
