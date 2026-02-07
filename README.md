# Phase0 Prototype (Technical README)

This README documents only the implementation details required for review.

## Architecture Map

```
Input/Orchestration (Unity)
  └─ Phase0GameController
       ├─ Core/Brain (pure gameplay rules, no rendering)
       │    ├─ Phase0CoreModel
       │    └─ Phase0PlacementBrain
       └─ View (Unity/Spine/PrimeTween/Haptics)
            ├─ Phase0GhostTilesView
            ├─ Phase0PieceTilesView
            ├─ Phase0BoardPlacedHighlightView
            ├─ Phase0GameFeelFX
            └─ Phase0Haptics

Config assets:
  - Assets/Phase0/Configs/Phase0GameFeelSettings.asset
  - Assets/Resources/SceneConfig.asset
  - Assets/Phase0/Configs/Shape_*.asset (ShapeDefinitionSO)
```

## Blocked Cells

Current blocked rug-local coordinates in scene setup:

- `(1,1)`
- `(2,2)`

## Tuning Locations

### 1) Game feel / easing / thresholds
`Assets/Phase0/Configs/Phase0GameFeelSettings.asset`

Tune values such as:
- snap/bounce durations and easing
- overshoot and return behavior
- hover debounce / feel thresholds
- hatch visual controls used for invalid readability

### 2) Grid and board mapping
`Assets/Resources/SceneConfig.asset`

Tune values such as:
- global grid dimensions
- highlighted board (4x4) origin/size
- mapping between global space and board/rug-local space

## Anchor Rule (Explicit)

This project uses **piece-internal anchor** (not pointer-based anchor).

- Rotation pivot is the piece-local pivot stored in `ShapeDefinitionSO.pivot`.
- Default pivot is `(0,0)` (see `Assets/Scripts/ShapeDefinitionSO.cs`).
- Rotation math is anchor-based around that pivot.
- Negative local cell coordinates are allowed.
- Cells are **not normalized after rotation**.

## Quick Manual Verification

1. Open `Assets/Scenes/SampleScene.unity`.
2. Verify blocked cells match `(1,1)` and `(2,2)` in rug-local coordinates.
3. Inspect:
   - `Assets/Phase0/Configs/Phase0GameFeelSettings.asset`
   - `Assets/Resources/SceneConfig.asset`
4. Inspect any shape asset + `ShapeDefinitionSO` and confirm pivot-driven rotation with default `(0,0)`.