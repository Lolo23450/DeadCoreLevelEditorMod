---

# DeadCore Level Editor Suite

A real-time, in-game 3D level editor and custom map manager for **DeadCore Redux**. Build custom parkour courses, place native mechanics (jump pads, turbines, defense turrets, checkpoints), and playtest seamlessly inside the engine.

---

## Requirements

* **[DeadCore Redux](/)** (Steam)
* **[MelonLoader](https://github.com/LavaGang/MelonLoader/releases)** (`v0.6.0` or newer)

---

## Installation

1. **Install MelonLoader**:
   * Download and run the **MelonLoader.Installer.exe** from the official GitHub releases.
   * Select your **DeadCore Redux** executable (`DeadCoreRedux.exe`) in your Steam game directory:
     ```text
     Steam/steamapps/common/DeadCore/
     ```
   * Click **Install**.
2. **Install the Mod**:
   * Download the latest `DeadCoreLevelEditorMod.dll` from this repository’s **Releases** folder.
   * Place the `.dll` file into the newly generated `Mods/` folder in your game directory:
     ```text
     Steam/steamapps/common/DeadCore/Mods/DeadCoreLevelEditorMod.dll
     ```
3. **Launch the Game**:
   * Start DeadCore Redux normally through Steam. MelonLoader will initialize the mod automatically.

---

## Quick Start Guide

1. **Launch the Browser**: On the Main Menu, click the native **Level Editor** button (or press `F2`).
2. **Create a Course**: Click **`+ New`** at the bottom bar to generate a fresh map with a starter floor platform.
3. **Launch**: Click **`Play`** to load into the level.
4. **Build**: Press `F1` to toggle into the 3D Freecam Editor and start placing assets.
5. **Playtest**: Press `F1` again to immediately drop into First-Person Playtest Mode.

---

## Controls

### Navigation & Modes

| Key | Action |
| :--- | :--- |
| **`F1`** | Toggle between **Playtest Mode** & **3D Hammer Edit Mode** |
| **`F2`** | Open / Close the native **Level Editor Menu** (Main Menu only) |
| **`WASD`** | Move the Freecam forward / left / backward / right |
| **`Space` / `E`** | Elevate Freecam Up |
| **`Ctrl` / `Q`** | Lower Freecam Down |
| **`Right Click (Hold)`** | Look / Rotate Freecam viewport |
| **`Left Shift (Hold)`** | Sprint / Fast Freecam flight speed |

---

### Selection & Asset Palette

| Key / Input | Action |
| :--- | :--- |
| **`Mouse Scroll`** | Browse items on the 3D Carousel Wheel |
| **`Tab`** | Switch asset category (**Essentials** $\rightarrow$ **Extracted** $\rightarrow$ **Misc**) |
| **`Enter` / `Space`** | Pick up / Select active item into **Placement Mode** |
| **`Backspace` / `X`** | Deselect active item / Cancel placement |
| **`Left Click`** | Place item in the world |
| **`Delete` / `Middle Click`**| Delete aimed placed object |

---

### Rotation & Snapping

| Key / Input | Action |
| :--- | :--- |
| **`Left / Right Arrow`** | Rotate **Yaw** (Y-axis) |
| **`Up / Down Arrow`** | Rotate **Pitch** (X-axis) |
| **`[` / `]`** *(or `PgUp` / `PgDn`)* | Rotate **Roll** (Z-axis) |
| **`Shift + Arrow`** | Fast **45°** block angle snap |
| **`Ctrl + Arrow`** | Fine **5°** micro-precision adjustment |
| **`T`** | Snap all rotation axes to the nearest **90°** cardinal angle |
| **`R`** | Reset rotation back to zero `(0°, 0°, 0°)` |
| **`G`** | Cycle Grid Snap (`1m` $\rightarrow$ `2m` $\rightarrow$ `4m` $\rightarrow$ `Off`) |

---

### Fine-Tuning & Properties

Hold **`Left Shift`** while scrolling the **`Mouse Wheel`** to adjust parameters on the selected/aimed object:

* **Generic Blocks / Meshes**: Scales size up / down (`0.05x` – `10.0x`).
* **Jump Pads**: Increases or decreases launch power (`2.0` – `60.0`).
* **Turbines (Helices)**: Increases or decreases wind push velocity (`5.0` – `150.0`).
* **Turrets**: Adjusts firing delay interval (`0.2s` – `6.0s`).

---

### History & Saving

| Key | Action |
| :--- | :--- |
| **`Z`** | **Undo** last placement or deletion |
| **`Y`** | **Redo** undone action |
| **`F5`** | **Quick Save** current level to `UserData/MyLevels/` |
| **`F6`** | **Quick Reload** current level from file |

---

## File Storage & Sharing

All levels are saved as lightweight, human-readable text files inside your DeadCore folder:

* **Your Levels (`T-Logs` tab)**:
  ```text
  DeadCore/UserData/MyLevels/
  ```
* **Downloaded Community Levels (`M-Logs` tab)**:
  ```text
  DeadCore/UserData/DownloadedLevels/
  ```

> **Sharing Custom Maps**: To share a course with friends, simply send them your `.txt` file and have them drop it into their `UserData/DownloadedLevels/` folder. It will instantly show up under the **M-Logs** tab in-game.
