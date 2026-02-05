using NUnit.Framework;
using Phase0;

public class Phase0Core_EditModeTests
{
    [Test]
    public void Rotation_GetRotated_ReturnsExpectedCells()
    {
        var baseCells = new[] { new Int2(0, 0), new Int2(0, 1), new Int2(0, 2), new Int2(1, 0) };
        var pivot = Int2.zero;

        var rotated = ShapeRotation.GetRotated(baseCells, pivot, rotationIndexCW: 1);

        Assert.That(rotated, Is.EqualTo(new[]
        {
            new Int2(0, 0),
            new Int2(-1, 0),
            new Int2(-2, 0),
            new Int2(0, 1)
        }));
    }

    [Test]
    public void Bounds_LShapeNearEdge_MakesCannotPlace()
    {
        var grid = new GridModel(4);
        var lShape = new[] { new Int2(0, 0), new Int2(1, 0), new Int2(0, 1), new Int2(0, 2) };

        var canPlace = grid.CanPlace(new Int2(3, 2), lShape, out _);

        Assert.False(canPlace);
    }

    [Test]
    public void Rotation_NearEdge_RotatedOutOfBounds_MakesCannotPlace()
    {
        var grid = new GridModel(4);
        var baseCells = new[] { new Int2(0, 0), new Int2(1, 0), new Int2(1, 1), new Int2(2, 1) };
        var rotated = ShapeRotation.GetRotated(baseCells, Int2.zero, rotationIndexCW: 1);

        var canPlace = grid.CanPlace(new Int2(1, 0), rotated, out _);

        Assert.False(canPlace);
    }

    [Test]
    public void BlockedCell_MakesCannotPlace()
    {
        var grid = new GridModel(4);
        grid.SetBlocked(new[] { new Int2(1, 1) });

        var shape = new[] { new Int2(0, 0), new Int2(0, 1), new Int2(0, 2), new Int2(1, 0) };

        var canPlace = grid.CanPlace(new Int2(1, 1), shape, out _);

        Assert.False(canPlace);
    }

    [Test]
    public void OccupiedCell_MakesCannotPlace()
    {
        var grid = new GridModel(4);
        grid.AddOccupied(new[] { new Int2(2, 0) });

        var shape = new[] { new Int2(0, 0), new Int2(0, 1), new Int2(0, 2), new Int2(1, 0) };

        var canPlace = grid.CanPlace(new Int2(2, 0), shape, out _);

        Assert.False(canPlace);
    }
}