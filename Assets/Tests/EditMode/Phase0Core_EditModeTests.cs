using NUnit.Framework;
using Phase0;

public class Phase0Core_EditModeTests
{
    [Test]
    public void Rotation_GetRotated_ReturnsExpectedCells()
    {
        var baseCells = new[] { new Int2(0, 0), new Int2(1, 0), new Int2(0, 1) };
        var pivot = Int2.zero;

        var rotated = ShapeRotation.GetRotated(baseCells, pivot, rotationIndexCW: 1);

        Assert.That(rotated, Is.EqualTo(new[]
        {
            new Int2(0, 0),
            new Int2(0, -1),
            new Int2(1, 0)
        }));
    }

    [Test]
    public void BlockedCell_MakesCannotPlace()
    {
        var grid = new GridModel(4);
        grid.SetBlocked(new[] { new Int2(1, 1) });

        var canPlace = grid.CanPlace(new Int2(0, 0), new[] { new Int2(1, 1) }, out _);

        Assert.False(canPlace);
    }

    [Test]
    public void OccupiedCell_MakesCannotPlace()
    {
        var grid = new GridModel(4);
        grid.AddOccupied(new[] { new Int2(2, 0) });

        var canPlace = grid.CanPlace(new Int2(2, 0), new[] { new Int2(0, 0) }, out _);

        Assert.False(canPlace);
    }
}