# Side-Scroller XY Slash Demo Sample

이 샘플은 **XY 평면 횡스크롤 게임**에서 `Slash Effect Studio`로 베이크한 `ParticleSystem` 프리팹을 캐릭터 좌우 반전(`+X` / `-X`)에 맞춰 재생하는 방법을 보여줍니다.

## 구성
- `SideScrollerSlashController.cs`: `A`/`D` 또는 `←`/`→` 키로 XY 평면 가로 이동 및 `Space`/좌클릭으로 참격 발동, `1`~`4` 키로 베기 프리팹 전환.

## 사용법
1. `Tools > Slash Effect Studio`에서 원하는 베기 이펙트를 제작하고 **`Bake & Save Particle Prefab Bundle`**을 실행합니다.
2. 생성된 `VFX_*.prefab`의 `SlashParticlePlayer` 컴포넌트를 `SideScrollerSlashController.slashPrefabs` 배열에 연결합니다.
3. 플레이 모드에서 좌우로 이동하며 공격을 테스트합니다.
