// Phase0PlacementBrain.cs
// Pure C# placement/rotation brain (no MonoBehaviour). Unity layer provides world mapping + visuals.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Phase0
{
    public sealed class Phase0PlacementBrain
    {
        private GridModel _grid;
        private Vector2Int[] _baseCells = new Vector2Int[0];
        private Vector2Int _pivot;

        private int _rotationCW;
        private Vector2Int[] _localCells;

        private bool _hasCandidate;
        private Vector2Int _candidateOriginCell;
        private bool _candidateValid;

        private bool _isPlacedOnBoard;
        private Vector2Int[] _lastPlacedWorldCells;

        public int RotationCW => _rotationCW;
        public Vector2Int[] LocalCells => _localCells;
        public bool HasCandidate => _hasCandidate;
        public Vector2Int CandidateOriginCell => _candidateOriginCell;
        public bool CandidateValid => _candidateValid;
        public bool IsPlacedOnBoard => _isPlacedOnBoard;
        public Vector2Int[] LastPlacedWorldCells => _lastPlacedWorldCells;

        public void Initialize(int gridSize,
            IEnumerable<Vector2Int> blockedCells,
            IEnumerable<Vector2Int> occupiedCells,
            Vector2Int[] baseCells,
            Vector2Int pivot)
        {
            _grid = new GridModel(gridSize);
            _grid.SetBlocked(blockedCells ?? new List<Vector2Int>());
            if (occupiedCells != null) _grid.AddOccupied(occupiedCells);

            _baseCells = baseCells ?? new Vector2Int[0];
            _pivot = pivot;
            _rotationCW = 0;
            RecomputeLocalCells();

            _hasCandidate = false;
            _candidateValid = false;
            _isPlacedOnBoard = false;
            _lastPlacedWorldCells = null;
        }

        public void SetShape(Vector2Int[] baseCells, Vector2Int pivot)
        {
            _baseCells = baseCells ?? new Vector2Int[0];
            _pivot = pivot;
            RecomputeLocalCells();
        }

        public void RotateCW()
        {
            _rotationCW = (_rotationCW + 1) & 3;
            RecomputeLocalCells();
        }

        public void ResetCandidate()
        {
            _hasCandidate = false;
            _candidateValid = false;
        }

        public void UpdateCandidate(Vector2Int approxCell, bool inside, bool shouldSwitchCandidate)
        {
            if (!inside)
            {
                ResetCandidate();
                return;
            }

            if (_hasCandidate)
            {
                if (shouldSwitchCandidate)
                    _candidateOriginCell = approxCell;
            }
            else
            {
                _candidateOriginCell = approxCell;
                _hasCandidate = true;
            }

            _candidateValid = _grid.CanPlace(_candidateOriginCell, _localCells, out _);
        }

        public void OnPickup()
        {
            if (_isPlacedOnBoard && _lastPlacedWorldCells != null)
                _grid.RemoveOccupied(_lastPlacedWorldCells);
        }

        public void RestorePlacementIfAny()
        {
            if (_isPlacedOnBoard && _lastPlacedWorldCells != null)
                _grid.AddOccupied(_lastPlacedWorldCells);
        }

        public Vector2Int[] GetCandidateWorldCells()
        {
            if (!_hasCandidate) return null;
            return _localCells.Select(c => _candidateOriginCell + c).ToArray();
        }

        public Vector2Int[] PlaceCandidate()
        {
            if (!_hasCandidate || !_candidateValid) return null;

            var worldCells = _localCells.Select(c => _candidateOriginCell + c).ToArray();
            _grid.AddOccupied(worldCells);

            _lastPlacedWorldCells = worldCells;
            _isPlacedOnBoard = true;
            return worldCells;
        }

        public void ClearPlacementOutsideGrid()
        {
            _isPlacedOnBoard = false;
            _lastPlacedWorldCells = null;
        }

        private void RecomputeLocalCells()
        {
            if (_baseCells == null || _baseCells.Length == 0)
            {
                _localCells = new[] { Vector2Int.zero };
                return;
            }

            _localCells = ShapeRotation.GetRotatedNormalized(_baseCells, _pivot, _rotationCW);
        }
    }
}