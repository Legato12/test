# Phase0 Prototype

Technical notes for the current Unity prototype.

## Scope

- Drag/rotate/place pieces on a global integer grid.
- Central highlighted 4x4 area is board preview/obstacle context.
- Spine + PrimeTween drive visual feedback in the View layer.

## Architecture (Core vs View)

- **Core/Brain (pure gameplay rules)**
  - `Assets/Scripts/Phase0CoreModel.cs`
  - `Assets/Scripts/Phase0PlacementBrain.cs`
  - `Assets/Scripts/IntRect.cs`
- **View (Unity/Spine/tweens/haptics)**
  - `Assets/Scripts/Phase0GameController.cs`
  - `Assets/Scripts/Phase0GhostTilesView.cs`
  - `Assets/Scripts/Phase0GameFeelFX.cs`

Rule: Core code must not depend on rendering concerns.

## Grid and Rotation Rules

- Whole screen is movable grid space.
- Highlighted board area is a central 4x4 subset used for preview/obstacles.
- Rotation is anchor-based.

### Anchor contract (explicit)

- **Pivot cell = (0,0)**
- **Strategy B (piece-internal anchor)**
- Piece local cells may be negative.
- Do **not** normalize local cell coordinates after rotation.

## Visual Rules

- Ghost on board uses **outline only**.
  - Valid: soft blue outline.
  - Invalid: red outline.
- Invalid hover shows diagonal hatch pattern inside cat silhouette.
  - Pattern is world-space and fixed to world/grid.
  - Angle: 45 degrees.

## Setup

1. Open project in Unity 2022.3 LTS.
2. Open `Assets/Scenes/SampleScene.unity`.
3. Ensure PrimeTween and Unity Test Framework are available.
4. Run EditMode tests in `Assets/Tests/EditMode`.

## Key Config Assets

- `Assets/Resources/SceneConfig.asset`
- `Assets/Phase0/Configs/Phase0GameFeelSettings.asset`
- `Assets/Phase0/Configs/Shape_L.asset`

## License

MIT (see LICENSE file if present in repository).