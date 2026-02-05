using System;
using Phase0;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Running Phase0 Rotation Fix Verification...");

        // Test basic rotation
        Test_BasicRotation();

        // Test anchor-based placement
        Test_AnchorBasedPlacement();

        Console.WriteLine("Verification complete.");
    }

    static void Test_BasicRotation()
    {
        Console.WriteLine("\n=== Basic Rotation Test ===");

        var baseCells = new[] { new Int2(0, 0), new Int2(1, 0), new Int2(0, 1) };
        var pivot = new Int2(0, 0);

        // Test old method (should still work)
        var rotatedOld = ShapeRotation.GetRotated(baseCells, pivot, 1);
        Console.WriteLine("Old rotation method:");
        foreach (var cell in rotatedOld) Console.Write(cell + " ");
        Console.WriteLine();

        // Test new method
        var baseOffsets = new Int2[baseCells.Length];
        for (int i = 0; i < baseCells.Length; i++)
        {
            baseOffsets[i] = baseCells[i] - pivot;
        }

        var rotatedNew = ShapeRotation.GetRotatedOffsets(baseOffsets, 1);
        Console.WriteLine("New rotation method (offsets):");
        foreach (var cell in rotatedNew) Console.Write(cell + " ");
        Console.WriteLine();

        // They should be the same since pivot is (0,0)
        bool same = rotatedOld.Length == rotatedNew.Length;
        if (same)
        {
            for (int i = 0; i < rotatedOld.Length; i++)
            {
                if (rotatedOld[i] != rotatedNew[i])
                {
                    same = false;
                    break;
                }
            }
        }

        Console.WriteLine($"Results match: {same}");
    }

    static void Test_AnchorBasedPlacement()
    {
        Console.WriteLine("\n=== Anchor-Based Placement Test ===");

        var brain = new Phase0PlacementBrain();
        var baseCells = new[] { new Int2(0, 0), new Int2(0, 1), new Int2(0, 2), new Int2(1, 0) };

        brain.Initialize(
            globalWidth: 10,
            globalHeight: 10,
            blockedCellsGlobal: new List<Int2>(),
            occupiedCellsGlobal: new List<Int2>(),
            baseCells: baseCells,
            pivot: Int2.zero,
            rugOriginGlobal: Int2.zero,
            rugWidth: 10,
            rugHeight: 10
        );

        var anchor = new Int2(5, 5);

        // Place initially
        var ok1 = brain.TryCommitPlacementAt(anchor, out _);
        Console.WriteLine($"Initial placement at {anchor}: {ok1}");

        // Rotate
        brain.OnPickup();
        brain.RotateCW();

        // Try to place at same anchor
        var ok2 = brain.TryCommitPlacementAt(anchor, out _);
        Console.WriteLine($"Placement after rotation at same anchor {anchor}: {ok2}");

        Console.WriteLine($"Anchor remains stable: {brain.LastLockedAnchorCell == anchor}");

        if (brain.LastLockedWorldCells != null)
        {
            Console.Write("World cells after rotation: ");
            foreach (var cell in brain.LastLockedWorldCells)
            {
                Console.Write(cell + " ");
            }
            Console.WriteLine();
        }
    }
}