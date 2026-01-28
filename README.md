# Phase 0 Prototype (Grey Box) + Spine Rigging Proof

Production-quality prototype validating **architecture**, **game feel**, and **Spine rigging** skills.
The mechanics and “squashy” feel are aligned with reference videos **Catsz1–Catsz4**.

---

## ✅ Deliverables

- Unity project (Grey Box prototype)
- Spine rigging proof (mesh deformation, weights, idle + squash)
- Source code (Git-ready)
- Spine project files included

---

## Part A — Core Code & Mechanics

### Grid System
- **4×4 grid** (as requested)
- Shapes (polyomino) defined via **ScriptableObject**
- Rotation is computed at runtime (pure math, no pre-baked rotations)

### Input (Direct Manipulation)
- **Touch Down + Move** = immediate pickup/drag with spring-follow
- **Touch Up** = drop
- **Tap (short touch, <10px movement)** = rotate 90°

### Placement Rules
- Pure C# logic separated from view (no physics/colliders)
- Blocked and occupied cells treated одинаково
- Invalid drop → **bounce back** to origin
- Dummy occupied cell present for collision test
- Movement allowed outside grid, but **never off-screen**

### Debounce / Hysteresis
- Finger jitter does **not** cause flickering
- Candidate cell changes only when leaving hysteresis radius

### Game Feel
- Smooth drag (no jitter)
- Overshoot/elastic snapping (PrimeTween)
- Haptics on valid/invalid drops

---

## Part B — Spine Rigging Proof

Placeholder sprite used (long cat / soft blob). Focus is on deformation quality.

### Spine Requirements Achieved
- Soft-body mesh deformation
- Smooth bending (no rigid folding)
- Idle breathing loop
- Squash reaction on impact

> **Note:** Ear/ tail physics are handled **inside Spine**, not by Unity code.
This avoids reinventing physics and improves performance.

---

## Architecture Notes

- **Pure Core Logic:** `Phase0CoreModel.cs`, `Phase0PlacementBrain.cs`
- **View/FX Layer:** `Phase0GameFeelFX.cs`
- **Orchestration/Input:** `Phase0GameController.cs`

Logic is isolated from presentation: _brain in Update, view FX only applied in controller._

---

## Build Output

- Android APK / iOS TestFlight build can be produced from this project.

---

If you need extra polish, refactor, or variation in feel curves, just say the word.
