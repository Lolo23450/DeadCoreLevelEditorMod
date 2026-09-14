# DeadCore Level Editor Suite

A real-time, in-game 3D level editor and custom map manager for **DeadCore Redux**. Build custom parkour courses, place native mechanics (jump pads, turbines, defense turrets, rotating lasers, checkpoints), link moving kinematic paths, customize map lighting, and playtest seamlessly inside the engine.

---

## Requirements

* **[DeadCore Redux](https://store.steampowered.com/app/3484260/DeadCore_Redux)** (Steam)
* **[MelonLoader](https://github.com/LavaGang/MelonLoader/releases)** (`v0.6.0` or newer)

---

## Installation

1. **Install MelonLoader**:
   * Download and run the **MelonLoader.Installer.exe** from the official GitHub releases.
   * Select your **DeadCore Redux** executable (`DeadCoreRedux.exe`) in your Steam game directory:
     ```text
     Steam/steamapps/common/DeadCoreRedux/DeadCoreRedux.exe
     ```
   * Click **Install**.
2. **Install the Mod**:
   * Download `DeadCoreLevelEditorMod.dll` from this repository’s **releases** folder.
   * Place the `.dll` file into the `Mods/` folder in your game directory:
     ```text
     Steam/steamapps/common/DeadCoreRedux/Mods/DeadCoreLevelEditorMod.dll
     ```
3. **Launch the Game**:
   * Start DeadCore Redux through Steam. MelonLoader will initialize the editor suite automatically.

---

## Quick Start Guide

1. **Open the Menu**: On the Main Menu, click the rebranded **Level Editor** button (or press `F2`).
2. **Create a Course**: Click **`+ New`** on the bottom bar to generate a fresh map with a starting platform and metadata template.
3. **Edit Metadata**: Fill in your level title, author, difficulty rating, and description directly in the UI panel, then click **`SAVE DETAILS`**.
4. **Launch**: Click **`Play`** (or press `Enter`) to load into the map.
5. **Build**: Press `F1` to enter **Edit Mode** (Freecam).
6. **Playtest**: Press `F1` again to drop back into **First-Person Playtest Mode** at any time.
7. **Save**: Press `F5` while editing to quick-save your progress.

---

## Controls & Keybindings

### Camera & Viewport Navigation (Edit Mode)

| Key / Input | Action |
| :--- | :--- |
| **`F1`** | Toggle between **Playtest Mode** & **3D Hammer Edit Mode** |
| **`F2`** | Open native **Level Editor Menu** (Main Menu scene) |
| **`F3`** | Toggle the **Light & Atmosphere Inspector Window** |
| **`F4`** | Dump full scene lighting hierarchy to MelonLoader console (Debug) |
| **`F5`** / **`F6`** | **Quick Save** / **Quick Load** current level |
| **`WASD`** | Fly Freecam forward / left / backward / right |
| **`Space` / `E`** | Fly upward |
| **`Q`** | Fly downward |
| **`Right Click (Hold)`** | Freelook / Rotate viewport camera |
| **`Right Click (Click)`** | Deselect active asset / Cancel placement |
| **`Left Shift (Hold)`** | Sprint / High-speed camera flight |
| **`Left Ctrl (Hold)`** | Precision / Slow camera flight |

---

### Asset Palette & Placement

| Key / Input | Action |
| :--- | :--- |
| **`Tab`** | Toggle active category (**1: Building** $\leftrightarrow$ **2: Gameplay**) |
| **`1` / `2`** | Directly select category (**1** = Building, **2** = Gameplay) |
| **`Mouse Scroll`** | Cycle through items on the 3D Carousel Arc |
| **`Left Click (World)`** | Place active hologram block into the world |
| **`Left Click (Arc)`** | Click any icon on the 3D wheel to equip it directly |
| **`Esc` / `X`** | Cancel placement / Deselect active item or light |
| **`Delete` / `Middle Click`** | Delete aimed placed object |

---

### Object Alignment & Snapping

| Key / Input | Action |
| :--- | :--- |
| **`C`** | Toggle **Surface Auto-Align** (`ON` = Snaps flush to walls/ceilings, `OFF` = Manual) |
| **`G`** | Cycle Grid Snapping (`1m` $\rightarrow$ `2m` $\rightarrow$ `4m` $\rightarrow$ `0.5m` $\rightarrow$ `OFF`) |
| **`Left / Right Arrow`** | Rotate **Yaw** (Y-axis) |
| **`Up / Down Arrow`** | Rotate **Pitch** (X-axis) |
| **`[` / `]`** *(or `PgUp` / `PgDn`)* | Rotate **Roll** (Z-axis) |
| **`Shift + Arrows`** | Fast **45°** angle step |
| **`Ctrl + Arrows`** | Micro-precision **5°** angle step |
| **`T`** | Snap all rotation axes to nearest **90°** angle |
| **`R`** | Reset rotation to zero `(0°, 0°, 0°)` |

---

### Advanced Mechanics: Moving Platforms & Parenting

#### 1. Kinematic Moving Paths (`M`)
Any placed object (or child hierarchy) can smoothly oscillate between two points with player momentum transfer:
1. Aim at an object and press **`M`** to select it and lock **Point A**.
2. Aim your crosshair at the destination position and press **`M`** again to lock **Point B**.
3. Hold **`Shift + Scroll`** while aiming at the path to adjust travel speed (`0.5 m/s` – `30.0 m/s`).
4. Press **`Shift + M`** while aiming at the object to remove its path.

#### 2. Assembly Parenting (`P`)
Link multiple objects together into compound structures:
1. Aim at the object you want to attach and press **`P`** (marked as Child).
2. Aim at the base object you want to attach it to and press **`P`** again (Parent).
3. Moving or animating the parent will carry all children along with it.
4. Press **`Shift + P`** while aiming at an object to unparent it.

---

### In-Game Object Tweaking (`Shift + Scroll`)

Hold **`Left Shift`** while scrolling the **`Mouse Wheel`** to adjust the parameters of the aimed or selected item:

* **Generic Blocks / Platforms**: Adjust placement scale (`0.01x` – `50.0x`).
* **Moving Paths**: Adjust platform velocity (`0.5 m/s` – `30.0 m/s`).
* **Jump Pads**: Adjust launch impulse force (`1.0` – `60.0`).
* **Turbines (Helices)**: Adjust wind repulsion speed (`1.0` – `100.0`).
* **Defense Turrets**: Adjust projectile firing delay (`0.05s` – `5.0s`).
* **Rotating Lasers**: Adjust angular rotation speed (`°/s`).

---

### Lighting & Atmosphere Inspector (`F3`)

The editor includes full control over HDRP scene lighting and skybox illumination:
* **Global Sunlight**: Places an infinite directional sun source that adjusts sun angle, shadows, and scene-wide trilight ambient tones.
* **Spotlights**: Place focused volumetric cone lights.
* **Inspector Panel (`F3`)**:
  * **Intensity Slider**: Calibrated for HDRP physical Lux units (`0.1x` – `30.0x`).
  * **Cone Angle**: Adjust spot angle (`10°` – `150°`).
  * **Volumetric Fog Intensity**: Scale atmospheric volumetric scattering.
  * **RGB Color Sliders & Presets**: Custom palette with instant Cyan, Sun, Green, Red, and White presets.
  * **Aiming**: Select any placed light and use the **Arrow Keys** to re-aim its beam direction in real time.

---

### Undo / Redo System

| Key | Action |
| :--- | :--- |
| **`Ctrl + Z`** | **Undo** last action (Placement, Deletion, Parenting, Motion Path) |
| **`Ctrl + Y`** *(or `Ctrl + Shift + Z`)* | **Redo** undone action |

---

## File Storage & Sharing

All courses are saved as plain-text `.txt` files containing full transform data, custom parameters, motion vectors, light configs, and metadata headers.

* **Your Levels (`My Levels` tab)**:
  ```text
  Steam/steamapps/common/DeadCoreRedux/UserData/MyLevels/
  ```
* **Downloaded Levels (`Community` tab)**:
  ```text
  Steam/steamapps/common/DeadCoreRedux/UserData/DownloadedLevels/
  ```

> **Sharing Custom Maps**: Send your level's `.txt` file to other players. When placed into their `UserData/DownloadedLevels/` directory, it will automatically appear in their in-game **Community** browser tab.