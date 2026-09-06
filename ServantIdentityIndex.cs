using System;
using System.Collections.Generic;

namespace HuntsAndRifts;

internal sealed class ServantIdentityIndex<T>
{
    private readonly Dictionary<string, T> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
    internal void Clear() { _byId.Clear(); _names.Clear(); }
    internal void Add(string name, string id, T value)
    {
        if (string.IsNullOrEmpty(id)) return;
        _byId[id] = value;
        if (_names.TryGetValue(name, out var previous) && previous != id)
            _names[name] = null; // A name-only UI row cannot identify either servant.
        else if (!_names.ContainsKey(name))
            _names[name] = id;
    }
    internal bool TryGet(string name, out T value)
    {
        value = default;
        return name != null && _names.TryGetValue(name, out var id) && id != null
            && _byId.TryGetValue(id, out value);
    }
}
