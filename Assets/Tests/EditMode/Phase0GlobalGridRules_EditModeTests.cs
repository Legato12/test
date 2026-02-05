using System.Collections.Generic;
using NUnit.Framework;
using Phase0;

public class Phase0GlobalGridRules_EditModeTests
{
    private static readonly Int2[] L4_BaseCells =
    {
        new Int2(0, 0),
        new Int2(0, 1),
        new Int2(0, 2),
        new Int2(1, 0)
    };

    [Test]
    public void Placement_OutsideRug_IsAllowed_WhenWithinGlobalBounds()
    {
        var brain = new Phase0PlacementBrain();
        var rug = new IntRect(new Int2(3, 3), 4, 4);

        brain.Initialize(
            globalWidth: 10,
            globalHeight: 10,
            rugRect: rug,
            blockedCellsGlobal: new List<Int2>(),
            occupiedCellsGlobal: new List<Int2>(),
            baseCells: L4_BaseCells,
            pivot: Int2.zero);

        // Completely outside the rug (rug starts at 3,3).
        var ok = brain.TryCommitPlacementAt(new Int2(1, 1), out _);

        Assert.True(ok);
        Assert.True(brain.HasLockedPlacement);
        Assert.False(brain.IsPlacedOnBoard);
        Assert.That(brain.LastLockedRugCellCount, Is.EqualTo(0));
    }

    [Test]
    public void Placement_InRug_ReportsRugLocalCells()
    {
        var brain = new Phase0PlacementBrain();
        var rug = new IntRect(new Int2(5, 2), 4, 4);

        brain.Initialize(
            globalWidth: 20,
            globalHeight: 20,
            rugRect: rug,
            blockedCellsGlobal: new List<Int2>(),
            occupiedCellsGlobal: new List<Int2>(),
            baseCells: L4_BaseCells,
            pivot: Int2.zero);

        // Anchor at rug origin => rug-local cells match base cells.
        var ok = brain.TryCommitPlacementAt(new Int2(5, 2), out _);

        Assert.True(ok);
        Assert.True(brain.IsPlacedOnBoard);
        Assert.That(brain.LastLockedRugCellCount, Is.EqualTo(4));

        Assert.That(brain.LastLockedRugCells, Is.EqualTo(L4_BaseCells));
    }

    [Test]
    public void Rotation_IsAnchorBased_DoesNotChangeAnchorCell()
    {
        var brain = new Phase0PlacementBrain();
        var rug = new IntRect(new Int2(0, 0), 4, 4);

        brain.Initialize(
            globalWidth: 30,
            globalHeight: 30,
            rugRect: rug,
            blockedCellsGlobal: new List<Int2>(),
            occupiedCellsGlobal: new List<Int2>(),
            baseCells: L4_BaseCells,
            pivot: Int2.zero);

        var anchor = new Int2(10, 10);
        Assert.True(brain.TryCommitPlacementAt(anchor, out _));

        brain.OnPickup();
        brain.RotateCW();

        Assert.True(brain.TryCommitPlacementAt(anchor, out _));
        Assert.That(brain.LastLockedAnchorCell, Is.EqualTo(anchor));

        // Pivot cell is always occupied.
        Assert.That(brain.LastLockedWorldCells, Does.Contain(anchor));
    }

    [Test]
    public void Rotation_ThatWouldExitGlobalBounds_IsInvalid()
    {
        var brain = new Phase0PlacementBrain();
        var rug = new IntRect(new Int2(0, 0), 4, 4);

        brain.Initialize(
            globalWidth: 4,
            globalHeight: 4,
            rugRect: rug,
            blockedCellsGlobal: new List<Int2>(),
            occupiedCellsGlobal: new List<Int2>(),
            baseCells: L4_BaseCells,
            pivot: Int2.zero);

        var anchor = new Int2(0, 0);
        Assert.True(brain.TryCommitPlacementAt(anchor, out _));

        brain.OnPickup();
        brain.RotateCW();

        // Rotating the L at (0,0) produces a cell at y=-1 => invalid.
        Assert.False(brain.TryCommitPlacementAt(anchor, out _));
    }
}
