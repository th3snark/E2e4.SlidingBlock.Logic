using System.Diagnostics.CodeAnalysis;

namespace E2e4.SlidingBlock.Logic;

/// <summary>Inline storage for the changes of a <see cref="CompositeMove"/>; it holds <see cref="CompositeMove.MaxPieces"/> of them without allocating.</summary>
internal partial struct MoveDeltaBuffer
{
    private PieceDelta _e0, _e1, _e2, _e3;

    /// <summary>The entry at <paramref name="index"/>.</summary>
    [UnscopedRef]
    public ref PieceDelta this[int index]
    {
        get
        {
            switch (index)
            {
                case 0: return ref _e0;
                case 1: return ref _e1;
                case 2: return ref _e2;
                case 3: return ref _e3;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }
}

/// <summary>Occupancy of the two elevation layers (0 = ground, 1 = upper).</summary>
internal struct LayerOccupancyBuffer<T> where T : unmanaged
{
    private T _e0, _e1;

    /// <summary>The entry at <paramref name="index"/>.</summary>
    [UnscopedRef]
    public ref T this[int index]
    {
        get
        {
            switch (index)
            {
                case 0: return ref _e0;
                case 1: return ref _e1;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }
}
