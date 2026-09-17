# DeadCore Level Editor

A real-time, in-engine 3D level editor and custom map studio for **DeadCore Redux**. Build intricate parkour courses, place native mechanics (jump pads, turbines, defense turrets, rotating lasers, switches, checkpoints), create reusable multi-object prefab instances, configure kinematic motion paths with continuous rotation, tune physical HDRP scene lighting and celestial skyboxes, and playtest instantly with one keystroke.

---

## Key Features

* **Complete Studio Workspace**: Docked UGUI panels including an interactive Scene Hierarchy, expandable Contextual Inspector, Top Toolbar, and an Asset Browser with live 3D isometric previews.
* **3D Transform Gizmos**: Translation, Rotation, and Scaling gizmos (including uniform center-box scaling and independent 3D non-uniform axis scaling) with adjustable gizmo handle sizes.
* **Prefab & Instance System**:
  * Save any selected multi-object group as a reusable prefab template directly into `UserData/MyPrefabs/`.
  * Spawn, position, scale, and rotate saved instances as single cohesive assets from the Asset Browser, with single-click deletion.
* **Interactive Switch Targets**:
  * Place target switches (`Interuptor`) that control all parented child objects when shot by the player's weapon.
  * Configurable auto-timer duration, invert state (turns hazards OFF or platforms ON), and initial power state.
* **Kinematic Motion Paths & Visual Waypoints**:
  * In-scene interactive waypoint handles (Point A = Green sphere, Point B = Orange sphere) with real-time vector connection lines.
  * Standing player tangential momentum carry and friction transfer.
  * **Spinning Axis Override**: Rotate platforms around canonical axes (`X - Tumble`, `Y - Turntable`, `Z - Roll`), align continuous rotation along the travel trajectory (`Point A -> Point B`), or enter arbitrary $(X, Y, Z)$ vector overrides with automatic normalization.
* **Scene Hierarchy with Drag & Drop**:
  * Parent and unparent objects via keybinds (`Ctrl+P`, `Alt+P`) or by dragging rows in the hierarchy tree.
  * Cycle-detection prevents recursive parenting loops.
  * Quick-focus (`[F]`) and instant unparent (`[X]`) buttons per row.
* **Catalog Asset Browser**:
  * Categorized filtering: `Architecture`, `Gameplay`, `Hazards`, `Instances`, `All`.
  * Size-tier filters (`ALL SIZES`, `SMALL 0-3m`, `MEDIUM 3-11m`, `LARGE 11m+`) with ascending and descending dimension sorting.
  * Color-coded physical size badges (`16x2x16`, `PREFAB`, `4.2m`, etc.) rendered on every thumbnail.
* **Physical HDRP Lighting & Celestial Skybox**:
  * Direct control over focused Tech Spotlights and scene-wide Global Sunlight (physical Lux/Lumens, cone angles, volumetric dimmers, and 0–255 RGB color sliders).
  * Dedicated **Skybox Controller** prop allowing live adjustment of exposure, yaw alignment, continuous orbital spin speed, and custom sky tint grading dynamically injected into HDRP volumes.
* **Gameplay Flow & Checkpoint Tracking**:
  * Dedicated Start (Entry Gate), Finish (Goal Gate), and intermediate Checkpoint objects.
  * Dynamic void-fall death barrier calculation, checkpoint respawning, run timer HUD, and level completion victory overlay.
* **Automated 3D Isometric Thumbnails**: Automatically captures 18° low-FOV isometric `.png` snapshots of your courses upon saving.
* **Non-Destructive Playtesting**: Instantly toggle between First-Person gameplay and freecam editing (`F1`) without reloading the scene.

---

## Requirements

* **[DeadCore Redux](https://store.steampowered.com/app/3484260/DeadCore_Redux)** (Steam)
* **[MelonLoader](https://github.com/LavaGang/MelonLoader/releases)** (`v0.6.0` or newer)

---

## Installation

1. **Install MelonLoader**:
   * Download and run the **MelonLoader.Installer.exe** from GitHub releases.
   * Select your **DeadCore Redux** executable (`DeadCoreRedux.exe`) in your Steam directory:
     ```text
     Steam/steamapps/common/DeadCoreRedux/DeadCoreRedux.exe
     ```
   * Complete the installation.
2. **Install the Mod**:
   * Download `DeadCoreLevelEditorMod.dll` from the release folder.
   * Place the file into the `Mods/` directory:
     ```text
     Steam/steamapps/common/DeadCoreRedux/Mods/DeadCoreLevelEditorMod.dll
     ```
3. **Launch**:
   * Start DeadCore Redux via Steam. MelonLoader will initialize the mod suite automatically.

---

## Quick Start Guide

1. **Open the Editor Browser**: On the game's Main Menu, click **Level Editor**.
2. **Create a Course**: Click **`+ New`** on the bottom bar to generate a starter level.
3. **Configure Details**: Fill in the title, author, difficulty rating, description, and base staging scene, then click **`SAVE DETAILS`**.
4. **Launch**: Select your level from the list and click **`Play`** (or press `Enter`).
5. **Switch to Studio Edit Mode**: Press **`F1`** to enter freecam editing.
6. **Equip & Place**: Click any asset in the bottom **Asset Browser** to spawn a placement hologram, then **Left-Click** in the scene to place it.
7. **Select & Transform**: Click **`MODE: [SELECT]`** (or press `Esc`) and click placed objects to position, rotate, or scale them using the 3D Gizmos or Inspector coordinates.
8. **Test & Save**: Press **`F1`** to drop back into first-person playtesting immediately. Press **`F5`** at any time to quick-save.

---

## Studio Interface Overview

```text
┌──────────────────────────────────────────────────────────────────────────────────────────────────┐
│ [MODE: SELECT/PLACEMENT] [Move] [Rotate] [Scale] [+ Instance] [Align] [Snap] [SNAP 3D] [PLAYTEST]│
├──────────────────────┬────────────────────────────────────────────┬──────────────────────────────┤
│ SCENE HIERARCHY      │                                            │ INSPECTOR        [+ Expand]  │
│                      │                                            │                              │
│ [ Filter...        ] │                                            │ Selection: Platform          │
│                      │                                            │                              │
│ ▼ Platform_Base (2)  │             3D VIEWPORT & GIZMOS           │ ► Transform (Pos/Rot/Scale)  │
│   ├── Jumper_Pad [F] │                                            │ ► Jumper / Turbine / Turret  │
│   └── Switch_01  [F] │                                            │ ► Switch Target Logic        │
│                      │                                            │ ► Lighting & Skybox Controls │
│ [ Delete Selected  ] │                                            │ ► Kinematic Motion Path      │
├──────────────────────┴────────────────────────────────────────────┴──────────────────────────────┤
│ ASSET BROWSER: [Architecture] [Gameplay] [Hazards] [Instances] [All]                 [Search...] │
│ [ALL SIZES] [SMALL 0-3m] [MEDIUM 3-11m] [LARGE 11m+]                              [SIZE: ▲ ASC]  │
│ [Card: Floor 16x16] [Card: Launch Jumper] [Card: Switch] [Card: Prefab Instance] ...             │
└──────────────────────────────────────────────────────────────────────────────────────────────────┘
```

---

## Controls & Keybindings

### Camera & Viewport Navigation (Edit Mode)

| Input | Action |
| :--- | :--- |
| **`F1`** | Toggle between **Playtest Mode** and **Studio Edit Mode** |
| **`F2`** | Open native **Level Editor Menu** (from Main Menu) |
| **`F4`** | Dump scene lighting hierarchy to console (Debug) |
| **`F5`** / **`F6`** | **Quick Save** / **Quick Load** current course |
| **`Right Click (Hold)`** | Freelook / Viewport camera orientation |
| **`WASD`** | Fly camera forward / left / backward / right |
| **`Space` / `E`** | Fly upward |
| **`Q`** | Fly downward |
| **`Left Shift (Hold)`** | Flight speed boost (3.5x) |
| **`Left Ctrl (Hold)`** | Precision flight deceleration (0.25x) |
| **`Mouse Scroll`** | Camera zoom / dolly (in Select Mode) |

---

### Selection, Gizmos & Placement

| Input | Action |
| :--- | :--- |
| **`Left Click` (World)** | In **Placement Mode**: Place active hologram.<br>In **Select Mode**: Select object (or drag active gizmo handle). |
| **`Ctrl + Left Click`** | Add or remove objects to/from **Multi-Selection** |
| **`Esc`** | Cancel active placement and return to **Select Mode** |
| **`Delete` / `Backspace`** | Delete selected object(s) |
| **`Left / Right Arrow`** | Decrease / Increase visual 3D gizmo handle scale |
| **`Numpad +` / `Numpad -`** | Increase / Decrease active placement scale (`0.05x` – `25.0x`) |
| **`Shift + Scroll`** | Fine-tune placement scale in Placement Mode |

---

### Clipboard, Duplication & Parenting

| Input | Action |
| :--- | :--- |
| **`Ctrl + C`** | Copy selected object(s) to clipboard (including custom parameters, paths, and lighting) |
| **`Ctrl + V`** | Paste clipboard objects at cursor position or near selection |
| **`Ctrl + D`** | Duplicate selection in-place with grid offset |
| **`Ctrl + P`** | **Parent Objects**: Multi-selection parents all items under the active target; single selection enters 2-step lock mode |
| **`Alt + P`** *(or `Ctrl+Shift+P`)* | **Unparent Objects**: Detaches selection to the root hierarchy |
| **`Ctrl + Z`** | **Undo** last action (Placement, Deletion, Parenting, Transform) |
| **`Ctrl + Y`** *(or `Ctrl+Shift+Z`)* | **Redo** undone action |

---

## Kinematic Motion Paths & Spinning Axis Override

Every placed platform or structure can oscillate kinematically between two points while maintaining independent continuous rotation and transferring realistic tangential momentum to the player.

### Setup & In-Scene Waypoints
1. Select an object and scroll down to the **Kinematic Motion Path** card in the Inspector.
2. Click **`[+ Create Motion Path]`**. Two visual waypoints will appear in the world:
   * **Point A (Green Sphere)**: Starting position.
   * **Point B (Orange Sphere)**: Target destination.
   * **Cyan Guide Line**: Visual connection between points.
3. Select Point B in the scene (or use the Inspector direction offset buttons) and move it with the 3D Gizmo to shape the travel trajectory.
4. Adjust **Speed (m/s)** via slider (`0.0` to `25.0 m/s`).

### Continuous Spin & Axis Override
Platforms can continuously spin while traveling along their path, acting as turntables, rolling cylinders, tumbling obstacles, or drill screws.

| Setting / Control | Description |
| :--- | :--- |
| **Spin Slider** | Sets rotational speed from `-150°/s` to `+150°/s`. Setting to `0` halts spin. |
| **Axis Cycle Button** | Cycles through 5 rotation modes:<br>• `X - Tumble`: Rotates around the local pitch axis.<br>• `Y - Turntable`: Rotates around the local yaw axis.<br>• `Z - Roll`: Rotates around the local barrel-roll axis.<br>• `Path (A -> B)`: Dynamically aligns the spin vector with the movement path heading.<br>• `Custom Override`: Rotates around the defined $(X, Y, Z)$ vector. |
| **Axis Vector Inputs** | Direct $(X, Y, Z)$ numeric inputs for defining arbitrary rotational axes (e.g. `X: 0.71, Y: 0.71, Z: 0.0` for a 45° diagonal axle). Typing directly into these fields switches the mode to **Custom Override**. |
| **`Align Path Dir`** | Automatically calculates the heading from Point A to Point B and sets it as the platform's local spin axis. |
| **`Normalize`** | Converts the current Axis Vector override into a unit vector with a single click. |
| **Player Momentum Transfer** | When standing on a rotating surface, the player receives real-time centrifugal push, and azimuthal (yaw) view orientation aligns smoothly with the platform. |

---

## Contextual Mechanics Inspector

Selecting an entity populates tailored control sliders in the right-hand panel:

* **Transform**: Direct coordinate inputs for $(X, Y, Z)$ position, Euler angles, and independent non-uniform scale. Includes **Snap 90** and **Reset Rot** buttons.
* **Jump Pads**: Adjust launch impulse acceleration force (`5.0` to `75.0`).
* **Turbines (Helices)**: Adjust wind repulsion speed (`5.0` to `100.0`). Wind forces dynamically apply directional thrust to the player's CharacterController.
* **Defense Turrets**: Tune fire delay intervals (`0.1s` to `5.0s`). Bullets cleanly ignore native turret colliders to prevent self-collision.
* **Rotating Lasers**: Configure continuous angular barrier speed (`0°/s` to `180°/s`).
* **Lighting & Atmosphere**:
  * **Type Toggle**: Configures focused **Tech Spotlights** or scene-wide **Global Sunlight**.
  * **Intensity**: Physical Lux calibrated for HDRP lighting (`0.1` to `30.0`).
  * **Spot Angle**: Cone spread angle (`10°` to `150°`).
  * **Volumetric Fog**: Controls volumetric scattering intensity and atmospheric dimming.
  * **RGB Color Pickers**: Individual R, G, B sliders (`0`–`255`), live preview swatch, and instant color presets (`Cyan`, `Gold`, `Acid`, `Red`, `White`, `Violet`).

---

## File Storage & Map Sharing

Courses are saved as plain-text `.txt` files containing full transform data, non-uniform scaling vectors, custom component parameters, kinematic paths, light configurations, and metadata headers.

* **Your Local Levels (`My Levels` tab)**:
  ```text
  Steam/steamapps/common/DeadCoreRedux/UserData/MyLevels/
  ```
* **Downloaded Community Levels (`Community` tab)**:
  ```text
  Steam/steamapps/common/DeadCoreRedux/UserData/DownloadedLevels/
  ```

### Sharing Custom Levels
To share a custom map with other players:
1. Locate your level file (e.g. `MyParkourCourse.txt`) and its companion screenshot (`MyParkourCourse.png`) in `UserData/MyLevels/`.
2. Send both files to your friend.
3. Have them place the files into their `UserData/DownloadedLevels/` directory.
4. The map will immediately appear with its metadata, stats HUD, and 3D preview under their in-game **Community** tab.
