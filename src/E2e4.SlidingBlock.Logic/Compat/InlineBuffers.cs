using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace E2e4.SlidingBlock.Logic;

// Fixed-size buffers: [InlineArray] where the runtime has it (.NET 8), plain fields with a switch indexer elsewhere.

internal partial struct MoveDeltaBuffer
{
    [UnscopedRef]
    internal ReadOnlySpan<PieceDelta> Slice(int count)
    {
#if NETSTANDARD2_0
        return new[] { _e0, _e1, _e2, _e3 }.AsSpan(0, count);
#else
        return MemoryMarshal.CreateReadOnlySpan(ref _e0, count);
#endif
    }
}

#if NET8_0_OR_GREATER
[InlineArray(KlotskiState.MaxPieces)]
internal struct KlotskiAnchorBuffer
{
    private byte _element0;
}
#else
internal struct KlotskiAnchorBuffer
{
    private byte _e0, _e1, _e2, _e3, _e4, _e5, _e6, _e7, _e8, _e9;

    /// <summary>The entry at <paramref name="index"/>.</summary>
    public byte this[int index]
    {
        readonly get
        {
            switch (index)
            {
                case 0: return _e0;
                case 1: return _e1;
                case 2: return _e2;
                case 3: return _e3;
                case 4: return _e4;
                case 5: return _e5;
                case 6: return _e6;
                case 7: return _e7;
                case 8: return _e8;
                case 9: return _e9;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
        set
        {
            switch (index)
            {
                case 0: _e0 = value; break;
                case 1: _e1 = value; break;
                case 2: _e2 = value; break;
                case 3: _e3 = value; break;
                case 4: _e4 = value; break;
                case 5: _e5 = value; break;
                case 6: _e6 = value; break;
                case 7: _e7 = value; break;
                case 8: _e8 = value; break;
                case 9: _e9 = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }
}
#endif

#if NET8_0_OR_GREATER
[InlineArray(Length)]
internal struct GroupAnchorBuffer<TBitboard> where TBitboard : unmanaged
{
    /// <summary>The most equivalence groups a <see cref="PolyominoState{TBitboard}"/> can hold.</summary>
    public const int Length = 16;

    private TBitboard _element0;
}
#else
internal struct GroupAnchorBuffer<TBitboard> where TBitboard : unmanaged
{
    private TBitboard _e0, _e1, _e2, _e3, _e4, _e5, _e6, _e7, _e8, _e9, _e10, _e11, _e12, _e13, _e14, _e15;

    /// <summary>The entry at <paramref name="index"/>.</summary>
    public TBitboard this[int index]
    {
        readonly get
        {
            switch (index)
            {
                case 0: return _e0;
                case 1: return _e1;
                case 2: return _e2;
                case 3: return _e3;
                case 4: return _e4;
                case 5: return _e5;
                case 6: return _e6;
                case 7: return _e7;
                case 8: return _e8;
                case 9: return _e9;
                case 10: return _e10;
                case 11: return _e11;
                case 12: return _e12;
                case 13: return _e13;
                case 14: return _e14;
                case 15: return _e15;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
        set
        {
            switch (index)
            {
                case 0: _e0 = value; break;
                case 1: _e1 = value; break;
                case 2: _e2 = value; break;
                case 3: _e3 = value; break;
                case 4: _e4 = value; break;
                case 5: _e5 = value; break;
                case 6: _e6 = value; break;
                case 7: _e7 = value; break;
                case 8: _e8 = value; break;
                case 9: _e9 = value; break;
                case 10: _e10 = value; break;
                case 11: _e11 = value; break;
                case 12: _e12 = value; break;
                case 13: _e13 = value; break;
                case 14: _e14 = value; break;
                case 15: _e15 = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }

    /// <summary>The most equivalence groups a <see cref="PolyominoState{TBitboard}"/> can hold.</summary>
    public const int Length = 16;
}
#endif

#if NET8_0_OR_GREATER
[InlineArray(Length)]
internal struct PieceAnchorBuffer
{
    /// <summary>The most pieces a <see cref="PolyominoState{TBitboard}"/> can hold.</summary>
    public const int Length = 16;

    private byte _element0;
}
#else
internal struct PieceAnchorBuffer
{
    private byte _e0, _e1, _e2, _e3, _e4, _e5, _e6, _e7, _e8, _e9, _e10, _e11, _e12, _e13, _e14, _e15;

    /// <summary>The entry at <paramref name="index"/>.</summary>
    public byte this[int index]
    {
        readonly get
        {
            switch (index)
            {
                case 0: return _e0;
                case 1: return _e1;
                case 2: return _e2;
                case 3: return _e3;
                case 4: return _e4;
                case 5: return _e5;
                case 6: return _e6;
                case 7: return _e7;
                case 8: return _e8;
                case 9: return _e9;
                case 10: return _e10;
                case 11: return _e11;
                case 12: return _e12;
                case 13: return _e13;
                case 14: return _e14;
                case 15: return _e15;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
        set
        {
            switch (index)
            {
                case 0: _e0 = value; break;
                case 1: _e1 = value; break;
                case 2: _e2 = value; break;
                case 3: _e3 = value; break;
                case 4: _e4 = value; break;
                case 5: _e5 = value; break;
                case 6: _e6 = value; break;
                case 7: _e7 = value; break;
                case 8: _e8 = value; break;
                case 9: _e9 = value; break;
                case 10: _e10 = value; break;
                case 11: _e11 = value; break;
                case 12: _e12 = value; break;
                case 13: _e13 = value; break;
                case 14: _e14 = value; break;
                case 15: _e15 = value; break;
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }
    }

    /// <summary>The most pieces a <see cref="PolyominoState{TBitboard}"/> can hold.</summary>
    public const int Length = 16;
}
#endif
