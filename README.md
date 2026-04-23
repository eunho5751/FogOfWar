# FogOfWar for Unity
[![en](https://img.shields.io/badge/lang-en-red.svg)](https://github.com/eunho5751/FogOfWar/blob/master/README.md)
[![ko](https://img.shields.io/badge/lang-ko-green.svg)](https://github.com/eunho5751/FogOfWar/blob/master/README.ko.md)

A grid-based Fog of War package for Unity, implemented with the Shadowcasting algorithm.
- **Unity**: 2022.3 or later
- **Namespace**: `EunoLab.FogOfWar`

<img width="594" height="298" alt="image" src="https://github.com/user-attachments/assets/659fc0e1-426d-4308-9a0e-92766b70a070" />
<br>
<img width="427" height="240" alt="녹화_2026_04_23_22_48_43_532" src="https://github.com/user-attachments/assets/7d213bd0-486a-4071-962a-89defe63be39" />

---

### 1. Installation

Unity Package Manager → **＋** → Install package from git URL → enter https://github.com/eunho5751/FogOfWar.git → Install

<img width="267" height="221" alt="image" src="https://github.com/user-attachments/assets/b2e75504-224d-4731-9605-3432779dc2da" />  
<br>
<img width="278" height="113" alt="image" src="https://github.com/user-attachments/assets/80eecbdb-8e07-4396-a130-54db9dd84dc4" />

---

### 2. Required Scene Setup

#### (1) Add the `MainFogOfWar` tag
Add a **`MainFogOfWar`** tag in Unity's **Tags & Layers** settings.
A `FogOfWar` instance with this tag can be accessed at runtime via `FogOfWar.Main`.
_※ The `AutoRegisterToMain` option on the `FogOfWarUnit` component accesses the global `FogOfWar` instance through `FogOfWar.Main`, so the tag is required whenever that option is enabled._

#### (2) Place the FogOfWar GameObject
1. Create an empty GameObject and set its **Tag to `MainFogOfWar`**.
2. Add the `FogOfWar` component to it.
3. The GameObject's **position** becomes the center of the grid. Place it to match your map. (Enable the gizmos to visualize the grid area.)
4. Assign the layer of your vision-blocking colliders (walls, etc.) to **Obstacle Scan Mask**.

#### (3) Prepare unit prefabs
For objects that should provide vision or be hidden by the fog (players, enemies, structures, etc.):
- Add a `FogOfWarUnit` component (attach it to the root or topmost GameObject).
- If you need show/hide behavior, add a `FogOfWarRendererSwitcher` or `FogOfWarGraphicSwitcher` on a child object.

#### (4) (Optional) Pre-scan the grid data
If the **`Grid Data Asset`** field on the `FogOfWar` component is not assigned, obstacles are scanned automatically during initialization.
To skip the runtime obstacle scan:
1. Click **Save Grid** in the `FogOfWar` component's context menu → a `.bytes` file is generated.
2. Assign the generated `.bytes` file to the `FogOfWar`'s **Grid Data Asset** field.

---

### 3. Main Scripts

#### `FogOfWar` (required)
The main controller of the fog of war system. Handles grid generation, visibility updates, and fog texture rendering.

**Main API**
- `Activate(int? teamMask = null)` / `Deactivate()` — turn the fog of war system ON / OFF
- `ScanGrid()` — rescan obstacles in the current scene and refresh the grid
- `AddUnit(FogOfWarUnit)` / `RemoveUnit(FogOfWarUnit)` / `ContainsUnit(FogOfWarUnit)`
- `IsVisible(Vector3 worldPos, int? teamMask = null)` — whether a given world position is currently visible
- `static FogOfWar.Main` — returns the instance tagged with `MainFogOfWar`
- `IsActivated` — current activation state
- `VisibilityUpdateRate` — visibility refresh frequency
- `TeamMask` — the team layer mask whose vision is rendered

---

#### `FogOfWarUnit`
Attach to objects that should provide vision or be hidden by the fog.

**Main API / Events**
- `event Action<bool> VisibilityChanged` — raised when the unit's visibility changes
- `bool IsVisible` — current visibility
- `bool IsTeammate(int teamMask)` — whether the unit belongs to the given mask

---

#### `FogOfWarVisibilityHandlerBase` (abstract class)
An abstract class that subscribes to a `FogOfWarUnit`'s visibility-change event.

**Overridable methods**
- `OnAwake()`, `OnEnabled()`, `OnDisabled()`
- `OnVisibilityChanged(bool isVisible)` — called when visibility changes

Example: inherit from this class to add custom behavior such as muting audio or switching AI states when a unit is hidden by fog.
_(The `FogOfWarRendererSwitcher` and `FogOfWarGraphicSwitcher` components are implemented by inheriting from this class.)_

---

#### `FogOfWarRendererSwitcher`
Turns the `Renderer` component on the same GameObject ON/OFF based on visibility.

#### `FogOfWarGraphicSwitcher`
Turns a UI `Graphic` component (Image, Text, etc.) on the same GameObject ON/OFF based on visibility.

---

#### `TeamMaskAttribute`
When applied to an `int` field, lets you edit teams 0–31 in the inspector as a checkbox mask.

```csharp
[SerializeField, TeamMask] private int _allowedTeams;
```

---

### 4. Team Layers

- Teams are identified by **layer numbers from 0 to 31** (separate from Unity Layers).
- `FogOfWar.TeamMask` is a bitmask — multiple teams' vision can be displayed at the same time.
- `FogOfWarUnit.TeamLayer` is a single layer number — that team's vision clears the surrounding fog.

---

### 5. Sample

- Import via Unity Package Manager → **Samples → Import**.
- The demo is playable out of the box without additional setup. However, if you assign an obstacle layer to the `Obstacles` objects in the demo scene and include that layer in the `FogOfWar` component's `Obstacle Scan Mask`, you can see vision being blocked by the obstacles.
