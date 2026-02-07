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
    public void Placement_InRug_ReportsRugCellsInGlobalSpace()
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

        // Anchor at rug origin => global cells are anchor + base cells.
        var ok = brain.TryCommitPlacementAt(new Int2(5, 2), out _);

        Assert.True(ok);
        Assert.True(brain.IsPlacedOnBoard);
        Assert.That(brain.LastLockedRugCellCount, Is.EqualTo(4));

        Assert.That(brain.LastLockedRugCells, Is.EqualTo(new[]
        {
            new Int2(5, 2),
            new Int2(5, 3),
            new Int2(5, 4),
            new Int2(6, 2)
        }));
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

    [Test]
    public void InvalidCommit_BeforeAnyLock_DoesNotCreateLockedPlacement()
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

        // Anchor is outside global bounds => invalid.
        Assert.False(brain.TryCommitPlacementAt(new Int2(-1, 0), out _));
        Assert.False(brain.HasLockedPlacement);
        Assert.False(brain.IsLocked);
    }

    [Test]
    public void LastValidPlacement_AfterFirstLock_InvalidCommit_KeepsLastLockedAnchor()
    {
        var brain = new Phase0PlacementBrain();
        var rug = new IntRect(new Int2(0, 0), 4, 4);

        brain.Initialize(
            globalWidth: 10,
            globalHeight: 10,
            rugRect: rug,
            blockedCellsGlobal: new[] { new Int2(2, 2) },
            occupiedCellsGlobal: new List<Int2>(),
            baseCells: L4_BaseCells,
            pivot: Int2.zero);

        var firstAnchor = new Int2(6, 6);
        Assert.True(brain.TryCommitPlacementAt(firstAnchor, out _));

        brain.OnPickup();

        // This commit is invalid due to blocked pivot cell at (2,2).
        Assert.False(brain.TryCommitPlacementAt(new Int2(2, 2), out _));

        Assert.True(brain.HasLockedPlacement);
        Assert.That(brain.LastLockedAnchorCell, Is.EqualTo(firstAnchor));
    }

    [Test]
    public void LastValidPlacement_AfterFirstLock_RestorePlacement_ReoccupiesPreviousCells()
    {
        var brain = new Phase0PlacementBrain();
        var rug = new IntRect(new Int2(0, 0), 4, 4);

        brain.Initialize(
            globalWidth: 10,
            globalHeight: 10,
            rugRect: rug,
            blockedCellsGlobal: new List<Int2>(),
            occupiedCellsGlobal: new List<Int2>(),
            baseCells: L4_BaseCells,
            pivot: Int2.zero);

        var firstAnchor = new Int2(6, 6);
        Assert.True(brain.TryCommitPlacementAt(firstAnchor, out _));

        brain.OnPickup();
        Assert.False(brain.TryCommitPlacementAt(new Int2(-1, 0), out _));

        // Restore previous lock occupancy, then verify overlap is rejected.
        brain.RestorePlacementIfAny();
        brain.SetShape(new[] { Int2.zero }, Int2.zero);

        Assert.False(brain.TryCommitPlacementAt(firstAnchor, out _));
    }

    [Test]
    public void LastPlacedWorldCells_ReturnsRugLocalSubset_ForUi()
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

        // Only one cell intersects rug: global (5,2) => rug-local (0,0).
        Assert.True(brain.TryCommitPlacementAt(new Int2(4, 2), out _));

        Assert.True(brain.IsPlacedOnBoard);
        Assert.That(brain.LastLockedRugCells, Is.EqualTo(new[] { new Int2(5, 2) }));
        Assert.That(brain.LastPlacedWorldCells, Is.EqualTo(new[] { new Int2(0, 0) }));
    }

    [Test]
    public void Rotation_IntoBlockedCell_IsInvalid()
    {
        var brain = new Phase0PlacementBrain();
        var rug = new IntRect(new Int2(0, 0), 4, 4);

        brain.Initialize(
            globalWidth: 10,
            globalHeight: 10,
            rugRect: rug,
            blockedCellsGlobal: new[] { new Int2(2, 1) },
            occupiedCellsGlobal: new List<Int2>(),
            baseCells: L4_BaseCells,
            pivot: Int2.zero);

        var anchor = new Int2(2, 2);
        Assert.True(brain.TryCommitPlacementAt(anchor, out _));

        brain.OnPickup();
        brain.RotateCW();

        // After rotation, one cell lands on blocked (2,1).
        Assert.False(brain.TryCommitPlacementAt(anchor, out _));
    }

    [Test]
    public void MultiPiece_SecondPlacement_OverlapsFirst_IsInvalid()
    {
        var brain = new Phase0PlacementBrain();
        var rug = new IntRect(new Int2(0, 0), 4, 4);

        brain.Initialize(
            globalWidth: 20,
            globalHeight: 20,
            rugRect: rug,
            blockedCellsGlobal: new List<Int2>(),
            occupiedCellsGlobal: new List<Int2>(),
            baseCells: L4_BaseCells,
            pivot: Int2.zero);

        Assert.True(brain.TryCommitPlacementAt(new Int2(8, 8), out _));

        // Simulate placing a second piece shape without picking up the first.
        brain.SetShape(new[] { Int2.zero }, Int2.zero);

        // Overlaps first piece pivot cell at (8,8).
        Assert.False(brain.TryCommitPlacementAt(new Int2(8, 8), out _));
    }
}
