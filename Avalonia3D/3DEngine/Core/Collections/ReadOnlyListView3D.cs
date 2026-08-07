using System;
using System.Collections;
using System.Collections.Generic;

namespace ThreeDEngine.Core.Collections;

/// <summary>
/// Reusable non-mutable facade over a replaceable list source. Intended for
/// retained frame objects whose backing scratch lists change between frames.
/// </summary>
internal sealed class ReadOnlyListView3D<T> : IReadOnlyList<T>
{
    private IReadOnlyList<T> _source = Array.Empty<T>();

    public int Count => _source.Count;
    public T this[int index] => _source[index];

    internal void Reset(IReadOnlyList<T>? source)
    {
        _source = source ?? Array.Empty<T>();
    }

    public IEnumerator<T> GetEnumerator() => _source.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
