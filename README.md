# Phase0 Prototype (Global Grid + Rug Subset)

## Architecture Map

**Core** (pure gameplay rules, no rendering concerns):
- `Assets/Scripts/Phase0CoreModel.cs` - Pure C# math for shape rotations, collision detection
- `Assets/Scripts/Phase0PlacementBrain.cs` - Validation and placement logic
- `Assets/Scripts/IntRect.cs` - Grid rectangle utilities

**View/Unity** (MonoBehaviours + Spine + PrimeTween + Haptics):
- `Assets/Scripts/Phase0GameController.cs` - Main game controller, input handling
- `Assets/Scripts/Phase0BoardMapping.cs` - Rug grid mapping
- `Assets/Scripts/Phase0GlobalGridMapping.cs` - Global screen grid mapping
- `Assets/Scripts/Phase0GhostTilesView.cs` - Ghost placement visualization
- `Assets/Scripts/Phase0BoardPlacedHighlightView.cs` - Placed piece highlighting
- `Assets/Scripts/Phase0GameFeelFX.cs` - Animation and visual effects
- `Assets/Scripts/Phase0Haptics.cs` - Haptic feedback

**Shape**: Figure L = 4 cells located at `Assets/Phase0/Configs/Shape_L.asset` (cells: (0,0), (0,1), (0,2), (1,0))

## Clarification (Global grid + Rug subset)

**Global grid** = entire screen/camera in integer cells: mapping in `Phase0GlobalGridMapping`. Global grid is invisible and covers the full movable space.

**Rug grid** = subset of global grid: RugOrigin(rx,ry) + RugWidth×RugHeight, used for UI/highlight/goal but does NOT restrict placement. Configured in `SceneConfigSO` (globalGridWidth/globalGridHeight, useRugOverride, rugOrigin, rugWidth, rugHeight) or auto-compute from GridRoot.

## Anchor Rule

Strategy: **B) piece-internal anchor**. Pivot = `ShapeDefinitionSO.pivot` (currently (0,0)), root transform treated as center of pivot-cell, around which grid-rotation occurs in Core (no sprite orbit).

## Blocked cells + Dummy occupied

Based on `Assets/Scenes/SampleScene.unity`:

**Blocked rug-local** = (1,1) and (2,2) (from objects `Cell_1_1_BLOCKED` and `Cell_2_2_BLOCKED`)

**Dummy occupied rug-local** = (0,3) (object `DummyPiece_Occupied` positioned there)

**Global coords** = rugOriginGlobal + local

## Tuning Knobs

- **Debounce/hysteresis**: `SceneConfigSO.hoverDebounceSeconds`, `Phase0GameController.cellSwitchHysteresisWorld`
- **Snap/reject durations**: `SceneConfigSO.snapDuration`, `SceneConfigSO.bounceBackDuration`
- **Invalid stripes**: `Phase0GameFeelSettingsSO` (hatchAngleDeg=135, hatchWidth=0.18, hatchOpacity=0.8, etc.)

## How to run tests

Unity Test Runner → EditMode → Run All

## Performance note

In drag loop avoid LINQ/alloc; monitor Profiler CPU Usage + GC Alloc; expect: GC alloc ~0 per frame during piece dragging.

## Open
- Unity: **2022.3 LTS**
- Open scene: `Assets/Scenes/SampleScene.unity`

## Packages (required)
This project references:
- PrimeTween (local tgz)
- Unity Test Framework
- Unity UI (uGUI)
- Animation, Physics, Physics2D engine modules

If packages don't resolve automatically:
- Window → Package Manager → ensure the above are enabled/installed.

## Scene upgrade (optional)
- Tools → Phase0 → Upgrade Scene To Global Grid
