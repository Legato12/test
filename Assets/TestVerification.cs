using System;
using System.Collections.Generic;
using Phase0;

public class TestVerification
{
    private static readonly Int2[] L4_BaseCells =
    {
        new Int2(0, 0),
        new Int2(0, 1),
        new Int2(0, 2),
        new Int2(1, 0)
    };

    public static void Test_Placement_InRug_ReportsRugLocalCells()
    {
        Console.WriteLine("Testing: Placement_InRug_ReportsRugLocalCells");

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

        Console.WriteLine($"TryCommitPlacementAt result: {ok}");
        Console.WriteLine($"IsPlacedOnBoard: {brain.IsPlacedOnBoard}");
        Console.WriteLine($"LastLockedRugCellCount: {brain.LastLockedRugCellCount}");

        if (brain.LastLockedRugCells != null)
        {
            Console.Write("LastLockedRugCells: ");
            for (int i = 0; i < brain.LastLockedRugCells.Length; i++)
            {
                Console.Write(brain.LastLockedRugCells[i] + " ");
            }
            Console.WriteLine();
        }

        // Check if it matches expected
        bool cellsMatch = brain.LastLockedRugCells != null &&
                          brain.LastLockedRugCells.Length == L4_BaseCells.Length;
        if (cellsMatch)
        {
            for (int i = 0; i < L4_BaseCells.Length; i++)
            {
                if (brain.LastLockedRugCells[i] != L4_BaseCells[i])
                {
                    cellsMatch = false;
                    break;
                }
            }
        }

        Console.WriteLine($"Cells match expected: {cellsMatch}");
        Console.WriteLine($"Test PASSED: {ok && brain.IsPlacedOnBoard && brain.LastLockedRugCellCount == 4 && cellsMatch}");
        Console.WriteLine();
    }

    public static void Test_Placement_OutsideRug_IsAllowed()
    {
        Console.WriteLine("Testing: Placement_OutsideRug_IsAllowed_WhenWithinGlobalBounds");

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

        Console.WriteLine($"TryCommitPlacementAt result: {ok}");
        Console.WriteLine($"HasLockedPlacement: {brain.HasLockedPlacement}");
        Console.WriteLine($"IsPlacedOnBoard: {brain.IsPlacedOnBoard}");
        Console.WriteLine($"LastLockedRugCellCount: {brain.LastLockedRugCellCount}");

        Console.WriteLine($"Test PASSED: {ok && brain.HasLockedPlacement && !brain.IsPlacedOnBoard && brain.LastLockedRugCellCount == 0}");
        Console.WriteLine();
    }

    public static void Test_Rotation_AnchorBased()
    {
        Console.WriteLine("Testing: Rotation_IsAnchorBased_DoesNotChangeAnchorCell");

        var brain = new Phase0PlacementBrain();
        var rug = new IntRect(new Int2(0, 0), 30, 30);

        brain.Initialize(
            globalWidth: 30,
            globalHeight: 30,
            rugRect: rug,
            blockedCellsGlobal: new List<Int2>(),
            occupiedCellsGlobal: new List<Int2>(),
            baseCells: L4_BaseCells,
            pivot: Int2.zero);

        var anchor = new Int2(10, 10);
        var ok1 = brain.TryCommitPlacementAt(anchor, out _);
        Console.WriteLine($"Initial placement at {anchor}: {ok1}");

        brain.OnPickup();
        brain.RotateCW();

        var ok2 = brain.TryCommitPlacementAt(anchor, out _);
        Console.WriteLine($"Placement after rotation at same anchor {anchor}: {ok2}");
        Console.WriteLine($"LastLockedAnchorCell: {brain.LastLockedAnchorCell}");

        // Anchor cell should be stable during rotation
        bool anchorStable = brain.LastLockedAnchorCell == anchor;

        // Pivot cell (0,0 relative) should still be occupied
        bool pivotOccupied = false;
        if (brain.LastLockedWorldCells != null)
        {
            for (int i = 0; i < brain.LastLockedWorldCells.Length; i++)
            {
                if (brain.LastLockedWorldCells[i] == anchor)
                {
                    pivotOccupied = true;
                    break;
                }
            }
        }

        Console.WriteLine($"Anchor stable: {anchorStable}");
        Console.WriteLine($"Pivot cell occupied: {pivotOccupied}");
        Console.WriteLine($"Test PASSED: {ok1 && ok2 && anchorStable && pivotOccupied}");
        Console.WriteLine();
    }
    }
}