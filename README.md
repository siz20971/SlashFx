# Slash Effect Studio (Unity UPM Package)

Unity 6 (`6000.3.14f1`+)에서 **XY 평면 2D/횡스크롤 액션 게임용 베기·참격 이펙트**를 절차적으로 제작하고, **SpriteSheet 즉시 베이크** 및 **ParticleSystem 오브젝트/프리팹 자동 생성**을 지원하는 에디터 툴 패키지입니다.

## 주요 기능
- **다중 앵커 Poly-Bezier 궤적 + 균일 아크 길이 LUT 샘플링**
- **2중 커브(Dual-Curve) 폭 프로파일**: 진행도별 헤드 폭(`progressWidthCurve`) × 꼬리~헤드 형태(`widthCurve`)
- **GPU 오프스크린 리본 렌더러 (RP 무관: Built-in / URP / HDRP)**:
  - UV 공간 다중 컬러 그라데이션 & 꼬리 알파 페이드 (360° 회전 베기에서도 왜곡 없음)
  - 이너 코어, 외곽 소프트 페더, 비대칭 칼날, 가우시안 글로우
  - 궤적 패턴(Wind / Fire / Lightning), 툰 외곽선, 픽셀아트 양자화
  - 환영 잔상(Phantom Echo) & 멀티 레이어 콤보 시퀀서
- **SpriteSheet 즉시 출력 & `TextureImporter` 자동 슬라이싱**:
  - Uniform Grid / Tight Trim / POT / 빈 프레임 자동 스킵 / 알파 출력 모드(Standard, PMA, Additive, Grayscale HDR Mask)
  - `ISpriteEditorDataProvider` 기반 Multiple Sprite Rect 및 Custom Pivot 자동 등록
- **XY 평면 ParticleSystem 오브젝트 / 프리팹 원클릭 생성**:
  - 양면(`Cull Off`) 머티리얼 자동 생성으로 좌우 반전(`Scale.x = -1`) 횡스크롤 캐릭터 완벽 대응
  - `Mesh (Quad)` / `Billboard` 및 `Grid` / `Sprites` 애니메이션 모드 지원
  - GUID 보존 **Hot-Rebake**로 씬/프리팹 참조 유지 갱신
- **Scene View 실시간 오버레이**: 실제 게임 캐릭터/무기 오브젝트 위에 참격 프리뷰
- **웹 `SlashEffectStudio` / `SlashEffectCli` `.slash.json` 양방향 호환**

## 설치 (UPM Git URL)
Unity `Window > Package Manager > + > Add package from git URL...`에서 본 레포지토리 URL을 입력합니다.

## 사용법
1. 메뉴 **`Tools > Slash Effect Studio`** 실행
2. 새 `SlashEffectAsset` 생성 또는 11종 내장 프리셋 / `.slash.json` 로드
3. 뷰포트에서 베지어 핸들·피벗 드래그 및 좌/우 패널로 폭·색상·타이밍 조정
4. **`Bake SpriteSheet`** 또는 **`Create Particle Object` / `Save Particle Prefab`** 클릭
