using System;
using System.Collections.Generic;

namespace PraetorisClient.ServerGuideFeature
{
    internal sealed class GuideHistory
    {
        internal sealed class Entry
        {
            internal readonly string Title;
            internal float Scroll = 1f;
            internal Entry(string title) { Title = title; }
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private int _index = -1;
        internal Entry? Current => _index >= 0 ? _entries[_index] : null;
        internal bool CanBack => _index > 0;
        internal bool CanForward => _index >= 0 && _index < _entries.Count - 1;

        internal void Visit(string title, float previousScroll)
        {
            if (Current != null) Current.Scroll = previousScroll;
            if (string.Equals(Current?.Title, title, StringComparison.OrdinalIgnoreCase)) return;
            _entries.RemoveRange(_index + 1, _entries.Count - _index - 1);
            _entries.Add(new Entry(title));
            if (_entries.Count > 100) _entries.RemoveAt(0);
            _index = _entries.Count - 1;
        }

        internal Entry? Move(int direction, float scroll)
        {
            if (direction == -1 ? !CanBack : direction != 1 || !CanForward) return null;
            Current!.Scroll = scroll;
            _index += direction;
            return Current;
        }

        internal void Clear() { _entries.Clear(); _index = -1; }

        internal void Prune(IEnumerable<string> titles)
        {
            HashSet<string> valid = new HashSet<string>(titles, StringComparer.OrdinalIgnoreCase);
            if (Current != null && !valid.Contains(Current.Title)) { Clear(); return; }
            for (int index = _entries.Count - 1; index >= 0; index--)
            {
                if (valid.Contains(_entries[index].Title)) continue;
                _entries.RemoveAt(index);
                if (index <= _index) _index--;
            }
        }
    }
}
