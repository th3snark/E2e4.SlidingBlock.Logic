#if !NET
namespace System.Collections.Generic;

/// <summary>
/// A 4-ary min-heap laid out as in <c>System.Collections.Generic.PriorityQueue</c> (.NET 6), for the targets that predate it: elements of equal priority
/// leave in the same order, so a search finds the same solution on every target. Only what the solvers use.
/// </summary>
internal sealed class PriorityQueue<TElement, TPriority>
{
    private const int Arity = 4;
    private readonly Comparer<TPriority> _comparer = Comparer<TPriority>.Default;
    private (TElement Element, TPriority Priority)[] _nodes = new (TElement, TPriority)[16];
    private int _size;

    public void Enqueue(TElement element, TPriority priority)
    {
        int index = _size++;
        if (index == _nodes.Length) Array.Resize(ref _nodes, _nodes.Length * 2);
        MoveUp((element, priority), index);
    }

    public bool TryDequeue(out TElement element, out TPriority priority)
    {
        if (_size == 0)
        {
            element = default!;
            priority = default!;
            return false;
        }
        (element, priority) = _nodes[0];
        int last = --_size;
        if (last > 0) MoveDown(_nodes[last], 0);
        _nodes[last] = default;
        return true;
    }

    private void MoveUp((TElement Element, TPriority Priority) node, int index)
    {
        while (index > 0)
        {
            int parent = (index - 1) / Arity;
            if (_comparer.Compare(node.Priority, _nodes[parent].Priority) >= 0) break;
            _nodes[index] = _nodes[parent];
            index = parent;
        }
        _nodes[index] = node;
    }

    private void MoveDown((TElement Element, TPriority Priority) node, int index)
    {
        int i;
        while ((i = index * Arity + 1) < _size)
        {
            var minChild = _nodes[i];
            int minIndex = i;
            int bound = Math.Min(i + Arity, _size);
            while (++i < bound)
            {
                var next = _nodes[i];
                if (_comparer.Compare(next.Priority, minChild.Priority) < 0)
                {
                    minChild = next;
                    minIndex = i;
                }
            }
            if (_comparer.Compare(node.Priority, minChild.Priority) <= 0) break;
            _nodes[index] = minChild;
            index = minIndex;
        }
        _nodes[index] = node;
    }
}
#endif
