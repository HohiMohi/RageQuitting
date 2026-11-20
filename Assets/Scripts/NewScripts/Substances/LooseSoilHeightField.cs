using System;

/// <summary>
/// Bounded, centered X/Z height grid. Heights are local vertical meters above the hard support.
/// Material volume is derived from each cell's true area, including partial edge cells.
/// </summary>
public sealed class LooseSoilHeightField
{
    private const int MaximumCellCount = 16384;

    public sealed class Snapshot
    {
        public readonly float Width;
        public readonly float Length;
        public readonly float CellSize;
        public readonly float MaximumHeight;
        public readonly double VolumePerUnit;
        public readonly int Columns;
        public readonly int Rows;
        private readonly float[] heights;
        public int CellCount => heights.Length;

        internal Snapshot(LooseSoilHeightField field)
        {
            Width = field.width; Length = field.length; CellSize = field.cellSize;
            MaximumHeight = field.maximumHeight; VolumePerUnit = field.volumePerUnit;
            Columns = field.columns; Rows = field.rows;
            heights = (float[])field.heights.Clone();
        }

        internal Snapshot(float width, float length, float cellSize, float maximumHeight,
            double volumePerUnit, int columns, int rows, float[] sourceHeights)
        {
            Width = width; Length = length; CellSize = cellSize; MaximumHeight = maximumHeight;
            VolumePerUnit = volumePerUnit; Columns = columns; Rows = rows;
            heights = (float[])sourceHeights.Clone();
        }

        internal float[] CopyHeights() => (float[])heights.Clone();
        public float GetHeightAtIndex(int index) => heights[index];
    }

    private readonly float width, length, cellSize;
    private float maximumHeight;
    private readonly double volumePerUnit;
    private readonly int columns, rows;
    private readonly float[] heights;
    private readonly float[] eligibleAreas;
    private ulong mutationRevision;

    public int Columns => columns;
    public int Rows => rows;
    public float Width => width;
    public float Length => length;
    public float CellSize => cellSize;
    public float MaximumHeight => maximumHeight;
    public float VolumePerUnit => (float)volumePerUnit;
    public float TotalVolume => (float)CalculateTotalVolume();
    public float TotalUnits => (float)(CalculateTotalVolume() / volumePerUnit);
    public double TotalUnitsPrecise => CalculateTotalVolume() / volumePerUnit;
    public ulong MutationRevision => mutationRevision;

    public LooseSoilHeightField(float width, float length, float cellSize, float maximumHeight,
        float initialHeight, float unitsForMaximumHeight)
    {
        if (!FinitePositive(width) || !FinitePositive(length) || !FinitePositive(cellSize) ||
            !FinitePositive(maximumHeight) || !FinitePositive(unitsForMaximumHeight) ||
            !Finite(initialHeight) || initialHeight < 0f || initialHeight > maximumHeight)
            throw new ArgumentOutOfRangeException("Height field dimensions, heights and units must be finite and valid.");

        this.width = width; this.length = length; this.cellSize = cellSize;
        this.maximumHeight = maximumHeight;
        double columnCount = Math.Ceiling((double)width / cellSize);
        double rowCount = Math.Ceiling((double)length / cellSize);
        if (double.IsNaN(columnCount) || double.IsInfinity(columnCount) ||
            double.IsNaN(rowCount) || double.IsInfinity(rowCount) ||
            columnCount < 1d || rowCount < 1d || columnCount * rowCount > MaximumCellCount)
            throw new ArgumentOutOfRangeException(nameof(cellSize), "The requested height field grid is too large.");
        columns = (int)columnCount;
        rows = (int)rowCount;
        // Calibrate against the same per-cell volume sum used by CalculateTotalVolume.
        // A nominal width*length product or area-first accumulation can differ by ulps
        // from the clipped float edge cells and leave a tail after a full layer drain.
        double fullLayerVolume = 0d;
        for (int row = 0; row < rows; row++)
        for (int column = 0; column < columns; column++)
        {
            float area = CellWidth(column) * CellLength(row);
            fullLayerVolume += (double)maximumHeight * area;
        }
        volumePerUnit = fullLayerVolume / unitsForMaximumHeight;
        if (double.IsNaN(volumePerUnit) || double.IsInfinity(volumePerUnit) || volumePerUnit <= 0d)
            throw new ArgumentOutOfRangeException(nameof(unitsForMaximumHeight));
        heights = new float[columns * rows];
        eligibleAreas = new float[heights.Length];
        if (initialHeight > 0f)
            for (int i = 0; i < heights.Length; i++) heights[i] = initialHeight;
    }

    public float GetHeight(int column, int row) => heights[Index(column, row)];

    public bool TryGetHeight(float localX, float localZ, out float height)
    {
        height = 0f;
        if (!Finite(localX) || !Finite(localZ)) return false;
        float minX = -width * 0.5f, minZ = -length * 0.5f;
        float maxX = width * 0.5f, maxZ = length * 0.5f;
        if (localX < minX || localX > maxX || localZ < minZ || localZ > maxZ) return false;
        int column = Math.Min(columns - 1, (int)((localX - minX) / cellSize));
        int row = Math.Min(rows - 1, (int)((localZ - minZ) / cellSize));
        height = heights[row * columns + column];
        return true;
    }

    public float GetMaximumHeightWithinRadius(float localX, float localZ, float radius)
    {
        if (!Finite(localX) || !Finite(localZ) || !Finite(radius) || radius < 0f) return 0f;
        float maximum = 0f;
        FillBrushMask(localX, localZ, radius, false, maximumHeight, false);
        for (int row = 0; row < rows; row++)
        for (int column = 0; column < columns; column++)
        {
            if (eligibleAreas[row * columns + column] > 0f)
                maximum = Math.Max(maximum, heights[row * columns + column]);
        }
        return maximum;
    }

    public double GetAdditionCapacity(float localX, float localZ, float radius, float heightCeiling)
    {
        if (!Finite(localX) || !Finite(localZ) || !Finite(radius) || radius < 0f ||
            !FinitePositive(heightCeiling) || heightCeiling > maximumHeight) return 0d;
        double availableVolume = FillBrushMask(localX, localZ, radius, true, heightCeiling, true);
        return availableVolume / volumePerUnit;
    }

    private double FillBrushMask(float localX, float localZ, float radius, bool add,
        float heightCeiling, bool requireAvailable)
    {
        Array.Clear(eligibleAreas, 0, eligibleAreas.Length);
        double eligibleVolume = 0d;
        float radiusSquared = radius * radius;
        for (int row = 0; row < rows; row++)
        for (int column = 0; column < columns; column++)
        {
            float x = -width * 0.5f + column * cellSize + CellWidth(column) * 0.5f;
            float z = -length * 0.5f + row * cellSize + CellLength(row) * 0.5f;
            float dx = x - localX, dz = z - localZ;
            if (dx * dx + dz * dz > radiusSquared) continue;
            int index = row * columns + column;
            float availableHeight = add ? heightCeiling - heights[index] : heights[index];
            if (requireAvailable && availableHeight <= 0f) continue;
            float area = CellWidth(column) * CellLength(row);
            eligibleAreas[index] = area;
            if (requireAvailable) eligibleVolume += (double)availableHeight * area;
        }
        return eligibleVolume;
    }

    public double GetAdditionRepresentationTolerance(float localX, float localZ, float radius,
        float heightCeiling, double requestedUnits)
    {
        if (!Finite(localX) || !Finite(localZ) || !Finite(radius) || radius < 0f ||
            !FinitePositive(heightCeiling) || heightCeiling > maximumHeight ||
            double.IsNaN(requestedUnits) || double.IsInfinity(requestedUnits) || requestedUnits < 0d)
            return 0d;
        FillBrushMask(localX, localZ, radius, true, heightCeiling, true);
        double volumeTolerance = 0d;
        for (int row = 0; row < rows; row++)
        for (int column = 0; column < columns; column++)
        {
            float area = eligibleAreas[row * columns + column];
            if (area > 0f) volumeTolerance += 4d * SingleSpacing(heightCeiling) * area;
        }
        float requestedFloat = (float)requestedUnits;
        return volumeTolerance / volumePerUnit + 2d * SingleSpacing(requestedFloat) + 1e-12d;
    }

    private static double SingleSpacing(float value)
    {
        if (!Finite(value) || value < 0f) return double.PositiveInfinity;
        if (value == 0f) return float.Epsilon;
        int bits = BitConverter.SingleToInt32Bits(value);
        return (double)BitConverter.Int32BitsToSingle(bits + 1) - value;
    }

    public float Remove(float localX, float localZ, float radius, float requestedUnits) =>
        Transfer(localX, localZ, radius, requestedUnits, false);

    /// <summary>Removes material from the whole bounded field, for an eligible whole-layer tail transfer.</summary>
    public float RemoveEverywhere(float requestedUnits) =>
        Transfer(0f, 0f, (float)Math.Sqrt((double)width * width + (double)length * length), requestedUnits, false);

    public float Add(float localX, float localZ, float radius, float requestedUnits) =>
        Transfer(localX, localZ, radius, requestedUnits, true, maximumHeight);

    /// <summary>
    /// Adds material locally without allowing any touched cell to rise above the
    /// supplied absolute height. Returns the volume actually represented by the grid.
    /// </summary>
    public float AddWithHeightCeiling(float localX, float localZ, float radius, float requestedUnits,
        float heightCeiling) =>
        Transfer(localX, localZ, radius, requestedUnits, true, heightCeiling);

    /// <summary>
    /// Raises the grid's maximum supported height while preserving its existing
    /// unit-to-volume calibration and every cell's current height.
    /// </summary>
    public bool ExpandMaximumHeight(float newMaximumHeight)
    {
        if (!FinitePositive(newMaximumHeight) || newMaximumHeight <= maximumHeight) return false;
        maximumHeight = newMaximumHeight;
        mutationRevision++;
        return true;
    }

    public void Clear()
    {
        bool changed = false;
        for (int i = 0; i < heights.Length; i++)
            if (heights[i] != 0f) { changed = true; break; }
        Array.Clear(heights, 0, heights.Length);
        if (changed) mutationRevision++;
    }

    public Snapshot CaptureSnapshot() => new Snapshot(this);

    public static bool TryCreateSnapshot(float width, float length, float cellSize, float maximumHeight,
        double volumePerUnit, int columns, int rows, float[] sourceHeights, out Snapshot snapshot)
    {
        snapshot = null;
        if (!FinitePositive(width) || !FinitePositive(length) || !FinitePositive(cellSize) ||
            !FinitePositive(maximumHeight) || double.IsNaN(volumePerUnit) ||
            double.IsInfinity(volumePerUnit) || volumePerUnit <= 0d || sourceHeights == null)
            return false;
        double expectedColumns = Math.Ceiling((double)width / cellSize);
        double expectedRows = Math.Ceiling((double)length / cellSize);
        if (expectedColumns < 1d || expectedRows < 1d ||
            expectedColumns * expectedRows > MaximumCellCount ||
            columns != (int)expectedColumns || rows != (int)expectedRows ||
            sourceHeights.Length != columns * rows)
            return false;
        for (int i = 0; i < sourceHeights.Length; i++)
            if (!Finite(sourceHeights[i]) || sourceHeights[i] < 0f || sourceHeights[i] > maximumHeight)
                return false;
        snapshot = new Snapshot(width, length, cellSize, maximumHeight, volumePerUnit,
            columns, rows, sourceHeights);
        return true;
    }

    public bool CanRestoreSnapshot(Snapshot snapshot) => IsCompatibleSnapshot(snapshot);

    public bool MatchesSnapshot(Snapshot snapshot)
    {
        if (!IsCompatibleSnapshot(snapshot)) return false;
        for (int i = 0; i < heights.Length; i++)
            if (heights[i] != snapshot.GetHeightAtIndex(i)) return false;
        return true;
    }

    public bool RestoreSnapshot(Snapshot snapshot)
    {
        if (!IsCompatibleSnapshot(snapshot)) return false;
        float[] copy = snapshot.CopyHeights();
        if (copy.Length != heights.Length) return false;
        bool changed = false;
        for (int i = 0; i < copy.Length; i++)
        {
            if (!Finite(copy[i]) || copy[i] < 0f || copy[i] > maximumHeight) return false;
            if (copy[i] != heights[i]) changed = true;
        }
        Array.Copy(copy, heights, heights.Length);
        if (changed) mutationRevision++;
        return true;
    }

    private bool IsCompatibleSnapshot(Snapshot snapshot) =>
        snapshot != null && snapshot.Width == width && snapshot.Length == length &&
        snapshot.CellSize == cellSize && snapshot.MaximumHeight == maximumHeight &&
        snapshot.VolumePerUnit == volumePerUnit && snapshot.Columns == columns && snapshot.Rows == rows;

    public float CellWidth(int column)
    {
        if (column < 0 || column >= columns) throw new ArgumentOutOfRangeException(nameof(column));
        return Math.Min(cellSize, width - column * cellSize);
    }

    public float CellLength(int row)
    {
        if (row < 0 || row >= rows) throw new ArgumentOutOfRangeException(nameof(row));
        return Math.Min(cellSize, length - row * cellSize);
    }

    private float Transfer(float localX, float localZ, float radius, float requestedUnits, bool add,
        float heightCeiling = 0f)
    {
        if (!Finite(localX) || !Finite(localZ) || !Finite(radius) || radius < 0f ||
            !Finite(requestedUnits) || requestedUnits <= 0f) return 0f;
        if (add && (!FinitePositive(heightCeiling) || heightCeiling > maximumHeight)) return 0f;

        double volumeBefore = CalculateTotalVolume();
        double totalEligibleVolume = FillBrushMask(localX, localZ, radius, add,
            add ? heightCeiling : maximumHeight, true);
        float maximumEligibleHeight = 0f;
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                int index = row * columns + column;
                if (eligibleAreas[index] <= 0f) continue;
                float availableHeight = add ? heightCeiling - heights[index] : heights[index];
                maximumEligibleHeight = Math.Max(maximumEligibleHeight, availableHeight);
            }
        }

        double targetVolume = Math.Min((double)requestedUnits * volumePerUnit, totalEligibleVolume);
        if (targetVolume <= 0d || double.IsNaN(targetVolume) || double.IsInfinity(targetVolume)) return 0f;
        double availableUnits = totalEligibleVolume / volumePerUnit;
        double drainTolerance = (double)requestedUnits * 0.0000001d;
        if (add && (double)requestedUnits * volumePerUnit >= totalEligibleVolume)
        {
            // A request that covers the exact eligible capacity should fill it exactly.
            // Keep this comparison strict so a near-capacity request cannot spend more
            // than requested through a tolerance.
            bool changed = false;
            for (int i = 0; i < heights.Length; i++)
            {
                if (eligibleAreas[i] <= 0f || heights[i] >= heightCeiling) continue;
                heights[i] = heightCeiling;
                changed = true;
            }
            if (changed) mutationRevision++;
            return Math.Min(requestedUnits, (float)((CalculateTotalVolume() - volumeBefore) / volumePerUnit));
        }
        if (!add && (double)requestedUnits + drainTolerance >= availableUnits)
        {
            // A complete drain must write exact zeroes instead of converging toward
            // them through repeated float bisection. This avoids a persistent ULP tail.
            bool changed = false;
            for (int row = 0; row < rows; row++)
            for (int column = 0; column < columns; column++)
            {
                int index = row * columns + column;
                if (eligibleAreas[index] <= 0f) continue;
                changed |= heights[index] != 0f;
                heights[index] = 0f;
            }
            if (changed) mutationRevision++;
            return Math.Min(requestedUnits, (float)((volumeBefore - CalculateTotalVolume()) / volumePerUnit));
        }
        float low = 0f, high = maximumEligibleHeight;
        for (int iteration = 0; iteration < 28; iteration++)
        {
            float delta = (low + high) * 0.5f;
            double moved = 0d;
            for (int i = 0; i < heights.Length; i++)
            {
                if (eligibleAreas[i] <= 0f) continue;
                float available = add ? heightCeiling - heights[i] : heights[i];
                moved += (double)Math.Min(delta, available) * eligibleAreas[i];
            }
            if (moved < targetVolume) low = delta; else high = delta;
        }

        double appliedVolume = 0d;
        bool transferred = false;
        for (int i = 0; i < heights.Length; i++)
        {
            if (eligibleAreas[i] <= 0f) continue;
            float available = add ? heightCeiling - heights[i] : heights[i];
            double remainingVolume = targetVolume - appliedVolume;
            if (remainingVolume <= 0d) break;
            double maxDelta = Math.Min(Math.Min((double)low, available), remainingVolume / eligibleAreas[i]);
            float delta = (float)maxDelta;
            float previousHeight = heights[i];
            float nextHeight = add ? previousHeight + delta : previousHeight - delta;
            nextHeight = Math.Max(0f, Math.Min(add ? heightCeiling : maximumHeight, nextHeight));
            float actualDelta = add ? nextHeight - previousHeight : previousHeight - nextHeight;
            double actualVolume = (double)actualDelta * eligibleAreas[i];
            if (actualDelta > 0f && actualVolume > remainingVolume)
            {
                nextHeight = FindConservativeHeight(nextHeight, previousHeight, eligibleAreas[i], remainingVolume, add);
                actualDelta = add ? nextHeight - previousHeight : previousHeight - nextHeight;
                actualVolume = (double)actualDelta * eligibleAreas[i];
            }
            if (actualDelta <= 0f) continue;
            heights[i] = nextHeight;
            transferred = true;
            appliedVolume += actualVolume;
        }

        // A uniform delta can fall below half an ULP at every cell even though a
        // representable localized change fits inside the remaining budget. Allocate
        // that remainder in one bounded sweep, choosing each cell's largest rounded
        // delta that does not exceed its share of the remaining volume.
        double residualVolume = targetVolume - appliedVolume;
        if (residualVolume > 0d)
        {
            for (int i = 0; i < heights.Length; i++)
            {
                if (eligibleAreas[i] <= 0f) continue;
                float previousHeight = heights[i];
                if (previousHeight == 0f) previousHeight = 0f;
                double remainingVolume = targetVolume - appliedVolume;
                if (remainingVolume <= 0d) break;
                if (add)
                {
                    if (previousHeight >= heightCeiling) continue;
                    double maxDelta = Math.Min((double)heightCeiling - previousHeight,
                        remainingVolume / eligibleAreas[i]);
                    float candidate = previousHeight + (float)maxDelta;
                    if (candidate > heightCeiling) candidate = heightCeiling;
                    double candidateVolume = (double)(candidate - previousHeight) * eligibleAreas[i];
                    if (candidateVolume > remainingVolume)
                    {
                        candidate = FindConservativeHeight(candidate, previousHeight, eligibleAreas[i],
                            remainingVolume, true);
                        candidateVolume = (double)(candidate - previousHeight) * eligibleAreas[i];
                    }
                    if (candidate <= previousHeight || candidateVolume <= 0d || candidateVolume > remainingVolume) continue;
                    heights[i] = candidate;
                    transferred = true;
                    appliedVolume += candidateVolume;
                }
                else
                {
                    if (previousHeight <= 0f) continue;
                    double maxDelta = Math.Min(previousHeight, remainingVolume / eligibleAreas[i]);
                    float candidate = previousHeight - (float)maxDelta;
                    if (candidate < 0f) candidate = 0f;
                    double candidateVolume = (double)(previousHeight - candidate) * eligibleAreas[i];
                    if (candidateVolume > remainingVolume)
                    {
                        candidate = FindConservativeHeight(candidate, previousHeight, eligibleAreas[i],
                            remainingVolume, false);
                        candidateVolume = (double)(previousHeight - candidate) * eligibleAreas[i];
                    }
                    if (candidate >= previousHeight || candidateVolume <= 0d || candidateVolume > remainingVolume) continue;
                    heights[i] = candidate;
                    transferred = true;
                    appliedVolume += candidateVolume;
                }
            }
        }
        if (transferred) mutationRevision++;
        double volumeAfter = CalculateTotalVolume();
        double transferredVolume = add ? volumeAfter - volumeBefore : volumeBefore - volumeAfter;
        return Math.Min(requestedUnits, (float)(transferredVolume / volumePerUnit));
    }

    private double CalculateTotalVolume()
    {
        double volume = 0d;
        for (int row = 0; row < rows; row++)
        for (int column = 0; column < columns; column++)
        {
            float area = CellWidth(column) * CellLength(row);
            volume += (double)heights[row * columns + column] * area;
        }
        return volume;
    }

    private int Index(int column, int row)
    {
        if (column < 0 || column >= columns) throw new ArgumentOutOfRangeException(nameof(column));
        if (row < 0 || row >= rows) throw new ArgumentOutOfRangeException(nameof(row));
        return row * columns + column;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool FinitePositive(float value) => Finite(value) && value > 0f;

    /// <summary>
    /// Finds the furthest representable height in the proposed transfer direction whose
    /// rounded float delta does not spend more volume than remains. All heights are
    /// finite and non-negative, so their IEEE-754 bit patterns are monotonically ordered.
    /// </summary>
    private static float FindConservativeHeight(float proposedHeight, float previousHeight,
        float area, double remainingVolume, bool add)
    {
        // Snapshot validation allows negative zero because it is not less than zero.
        // Normalize either zero before interpreting bits as ordered positive floats.
        if (proposedHeight == 0f) proposedHeight = 0f;
        if (previousHeight == 0f) previousHeight = 0f;
        int proposedBits = BitConverter.SingleToInt32Bits(proposedHeight);
        int previousBits = BitConverter.SingleToInt32Bits(previousHeight);

        if (add)
        {
            // Volume increases with height. Search for the greatest valid bit pattern
            // between the unchanged height and the proposed (overspending) height.
            long low = previousBits;
            long high = proposedBits;
            while (low < high)
            {
                int middle = (int)(low + (high - low + 1) / 2);
                float candidate = BitConverter.Int32BitsToSingle(middle);
                double candidateVolume = (double)(candidate - previousHeight) * area;
                if (candidateVolume <= remainingVolume) low = middle;
                else high = middle - 1;
            }
            return BitConverter.Int32BitsToSingle((int)low);
        }

        // For removal, volume decreases as the candidate approaches the previous
        // height. Find the first valid bit pattern in that monotonic interval.
        {
            long low = proposedBits;
            long high = previousBits;
            while (low < high)
            {
                int middle = (int)(low + (high - low) / 2);
                float candidate = BitConverter.Int32BitsToSingle(middle);
                double candidateVolume = (double)(previousHeight - candidate) * area;
                if (candidateVolume <= remainingVolume) high = middle;
                else low = middle + 1;
            }
            return BitConverter.Int32BitsToSingle((int)low);
        }
    }
}
