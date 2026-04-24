# FogOfWar for Unity
[![en](https://img.shields.io/badge/lang-en-red.svg)](https://github.com/eunho5751/FogOfWar/blob/master/README.md)
[![ko](https://img.shields.io/badge/lang-ko-green.svg)](https://github.com/eunho5751/FogOfWar/blob/master/README.ko.md)

Unity용 그리드 기반 전장의 안개(Fog of War) 패키지입니다. Shadowcasting 알고리즘으로 구현되었습니다.
- **Unity**: 2022.3 이상
- **Namespace**: `EunoLab.FogOfWar`

<img width="594" height="298" alt="image" src="https://github.com/user-attachments/assets/659fc0e1-426d-4308-9a0e-92766b70a070" />
<br>
<img width="427" height="240" alt="녹화_2026_04_23_22_48_43_532" src="https://github.com/user-attachments/assets/7d213bd0-486a-4071-962a-89defe63be39" />

---

### 1. 설치

Unity Package Manager → **＋** → Install package from git URL → https://github.com/eunho5751/FogOfWar.git#1.0.0 입력 → Install  

<img width="267" height="221" alt="image" src="https://github.com/user-attachments/assets/b2e75504-224d-4731-9605-3432779dc2da" />  
<br>
<img width="278" height="113" alt="image" src="https://github.com/user-attachments/assets/80eecbdb-8e07-4396-a130-54db9dd84dc4" />

---

### 2. 씬 필수 설정

#### (1) `MainFogOfWar` 태그 추가
Unity의 **Tags & Layers** 설정에 **`MainFogOfWar`** 태그를 추가해야 합니다.  
태그가 설정된 `FogOfWar` 인스턴스는 런타임에 `FogOfWar.Main`으로 접근할 수 있습니다.  
_※ `ForOfWarUnit` 컴포넌트의 `AutoRegisterToMain` 옵션은 `FogOfWarUnit.Main`을 통해 전역 `FogOfWar` 인스턴스에 접근하므로 해당 옵션이 켜져있다면 반드시 태그가 필요합니다._

#### (2) FogOfWar GameObject 배치
1. 빈 GameObject를 만들고 **Tag를 `MainFogOfWar`** 로 설정합니다.
2. `FogOfWar` 컴포넌트를 추가합니다.
3. 이 GameObject의 **position**이 그리드의 중심(center)이 됩니다. 맵 위에 맞춰 배치하세요. (기즈모를 키면 그리드 영역을 확인할 수 있음)
4. **Obstacle Scan Mask**에 시야를 차단할 장애물(벽 등) 콜라이더가 속한 레이어를 지정합니다.

#### (3) 유닛 프리팹 준비
시야를 밝히거나 안개에 의해 숨겨져야 하는 오브젝트(플레이어, 적, 구조물 등)에는:
- `FogOfWarUnit` 컴포넌트를 추가 (루트 또는 최상위 GameObject에 부착)
- 숨김/표시 처리가 필요하다면 `FogOfWarRendererSwitcher` 또는 `FogOfWarGraphicSwitcher` 컴포넌트를 추가

#### (4) (선택) 그리드 데이터 프리스캔
`FogOfWar` 컴포넌트의 **`Grid Data Asset`**이 할당되어 있지 않다면, 컴포넌트 초기화 시 자동으로 장애물을 스캔합니다.  
런타임 중에 장애물 스캔을 생략하려면:
1. `FogOfWar` 컴포넌트의 컨텍스트 메뉴에서 **Save Grid** 클릭 → `.bytes` 파일 생성
2. 생성된 `.bytes` 파일을 `FogOfWar`의 **Grid Data Asset** 필드에 할당

---

### 3. FogOfWar 품질 조정

FogOfWar 컴포넌트 설정에 따라 전장의 안개 품질이 크게 달라지므로, 여기에 필수 설정 몇 가지를 설명하겠습니다.

#### (1) 그리드 설정
<img width="577" height="64" alt="image" src="https://github.com/user-attachments/assets/77932960-e6ba-49e5-8eec-70e66b8fb33c" />
<br>

**`Grid Dimensions`** : 타일의 수. 값이 클수록 안개 텍스처의 해상도가 증가하며, 월드에서 **같은 타일의 크기**로 더 넓은 영역을 커버하게 됩니다.  
**`Grid Unit Scale`** : 월드에서 각 타일의 크기. 값이 클수록 타일의 크기가 증가하므로 **같은 안개 텍스처의 해상도**로 더 넓은 영역을 커버하게 됩니다.  

위 설명에서 볼 수 있듯이, `Grid Dimensions`, `Grid Unit Scale` 둘 다 월드에서 그리드가 커버하는 영역을 조절할 수 있지만 어느 한 쪽만 키우거나 줄이게 되면 문제가 생길 수 있습니다.  
(&#10060;) `Grid Dimensions` 설정만을 통해 넓은 영역을 커버하게 되면 안개 텍스처의 해상도가 증가하고 CPU/GPU 처리 비용이 증가하게 됩니다.  
(&#10060;) `Grid Unit Scale` 설정만을 통해 넓은 영역을 커버하게 되면 월드에서 타일 하나당 안개 텍스처의 많은 픽셀들이 그려져야하므로 전장의 안개 품질이 낮아지게 됩니다.  
(&#9989;) 두 설정을 같이 조절하면서 내 프로젝트에 맞는 그리드 크기와 타일 크기의 적정 값을 찾습니다. (Gizmos 활용)

#### (2) 안개 설정
<img width="328" height="90" alt="image" src="https://github.com/user-attachments/assets/0d643ea6-7bd7-4eb0-a85d-12ac05e94259" />
<br>

**`Fog Lerp Speed`** : 안개 보간 속도, 값이 클수록 안개 텍스처가 변화에 빠르게 반응합니다.  
**`Fog Blur Iterations/Radius/Sigma`** : 가우시안 블러에 사용되는 설정들로, 시야 경계를 부드럽게 하기 위해 사용됩니다.

<img width="697" height="290" alt="image" src="https://github.com/user-attachments/assets/b04a8e03-df60-49e1-8ead-1104009be21f" />

---

### 4. 주요 스크립트

#### `FogOfWar` (필수)
전장의 안개 시스템의 메인 컨트롤러입니다. 그리드 생성, 시야 업데이트, 안개 텍스처 렌더링을 모두 관리합니다.

**주요 API**
- `Activate(int? teamMask = null)` / `Deactivate()` — 전장의 안개 시스템 ON / OFF
- `ScanGrid()` — 현재 씬의 장애물을 다시 스캔하여 그리드 갱신
- `AddUnit(FogOfWarUnit)` / `RemoveUnit(FogOfWarUnit)` / `ContainsUnit(FogOfWarUnit)`
- `IsVisible(Vector3 worldPos, int? teamMask = null)` — 특정 월드 위치의 시야 여부
- `static FogOfWar.Main` — `MainFogOfWar` 태그를 가진 인스턴스 반환
- `IsActivated` — 현재 활성 상태
- `VisibilityUpdateRate` — 가시성 갱신 빈도
- `TeamMask` — 시야를 보여줄 팀 레이어 마스크

---

#### `FogOfWarUnit`
시야를 제공하거나 안개에 의해 가려지는 오브젝트에 부착합니다.

**주요 API / 이벤트**
- `event Action<bool> VisibilityChanged` — 유닛의 가시성이 바뀔 때 호출
- `bool IsVisible` — 현재 가시성
- `bool IsTeammate(int teamMask)` — 주어진 마스크와 같은 팀인지

---

#### `FogOfWarVisibilityHandlerBase` (추상 클래스)
`FogOfWarUnit`의 가시성 변화 이벤트를 구독한 추상 클래스입니다.

**오버라이드 가능한 메서드**
- `OnAwake()`, `OnEnabled()`, `OnDisabled()`
- `OnVisibilityChanged(bool isVisible)` — 가시성 변경 시 호출

예: 안개에 가려졌을 때 사운드를 음소거하거나 AI 상태를 바꾸는 등 커스텀 동작을 붙일 때 이 클래스를 상속해 구현하세요.
_(`FogOfWarRendererSwitchter`, `FogOfWarGraphicSwitcher` 컴포넌트가 이 클래스를 상속하여 구현되었음)_

---

#### `FogOfWarRendererSwitcher`
같은 GameObject의 `Renderer` 컴포넌트를 가시성에 따라 ON/OFF 합니다.

#### `FogOfWarGraphicSwitcher`
같은 GameObject의 UI `Graphic` 컴포넌트(Image, Text 등)를 가시성에 따라 ON/OFF 합니다.

---

#### `TeamMaskAttribute`
`int` 필드에 붙이면 인스펙터에서 0~31번 팀을 체크박스 마스크로 편집할 수 있습니다.

```csharp
[SerializeField, TeamMask] private int _allowedTeams;
```

---

### 5. 팀 레이어

- 팀은 0~31번의 **Layer 번호**로 식별됩니다 (Unity Layer와는 별개).
- `FogOfWar.TeamMask`는 비트마스크 — 여러 팀의 시야를 동시에 볼 수 있습니다.
- `FogOfWarUnit.TeamLayer`는 단일 레이어 번호 — 그 팀의 시야로 주변 안개를 걷어냅니다.

---

### 6. 샘플

- Unity Package Manager의 **Samples → Import**로 불러올 수 있습니다.
- 기본적으로 다른 설정 없이도 플레이가 가능하나, Demo 씬 내 `Obstacles` 오브젝트들에 장애물 레이어를 할당하고 `FogOfWar` 컴포넌트의 `Obstacle Scan Mask`에 장애물 레이어를 포함시키면 장애물에 의해 시야가 막히는 것을 확인할 수 있습니다.
