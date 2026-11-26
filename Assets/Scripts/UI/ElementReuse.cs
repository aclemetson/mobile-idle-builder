using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Lets a panel that redraws on a timer reuse its elements instead of clearing and rebuilding them (#282).
    /// A refresh runs the same build code between <see cref="Begin"/> and <see cref="End"/>, but asks for each
    /// element with <see cref="Take{T}"/> instead of <c>new</c> + <c>Add</c>: the child already at that position
    /// under that parent comes back when its type and key match, reset to its built-in classes and enabled;
    /// otherwise a new one is inserted there. <see cref="End"/> removes the children nothing asked for.
    /// So a refresh with the same layout creates no elements, keeps every Button (a tap held across it is not
    /// lost) and does not jump the scroll. Callers set text, classes and inline styles on every refresh.
    /// </summary>
    public sealed class ElementReuse
    {
        sealed class Tag
        {
            public string   Key;
            public string[] BaseClasses;   // classes the element's constructor added (e.g. unity-button)
        }

        static readonly ConditionalWeakTable<VisualElement, Tag> Tags = new();

        readonly Dictionary<VisualElement, int> _next    = new();
        readonly List<VisualElement>            _parents = new();

        /// <summary>Elements created since the last <see cref="Begin"/> (0 on a refresh with an unchanged layout).</summary>
        public int Created { get; private set; }

        /// <summary>Starts a refresh. Every root is trimmed by <see cref="End"/> even if nothing is taken under it.</summary>
        public void Begin(params VisualElement[] roots)
        {
            _next.Clear();
            _parents.Clear();
            Created = 0;
            if (roots == null) return;
            foreach (var r in roots) Track(r);
        }

        /// <summary>
        /// The element of type <typeparamref name="T"/> at the next position under <paramref name="parent"/>.
        /// With no parent, a new detached element.
        /// </summary>
        public T Take<T>(VisualElement parent, string key) where T : VisualElement, new()
        {
            if (parent == null) { Created++; return New<T>(key); }
            Track(parent);
            int i = _next[parent];
            _next[parent] = i + 1;

            if (i < parent.childCount && parent[i] is T existing && existing.GetType() == typeof(T)
                && Tags.TryGetValue(existing, out var tag) && tag.Key == key)
            {
                existing.ClearClassList();
                foreach (var c in tag.BaseClasses) existing.AddToClassList(c);
                existing.SetEnabled(true);
                return existing;
            }

            Created++;
            var made = New<T>(key);
            parent.Insert(i, made);
            return made;
        }

        /// <summary>
        /// Marks <paramref name="parent"/> as rebuilt this pass even if nothing is taken under it, so
        /// <see cref="End"/> drops children it no longer has (e.g. an icon a reused button no longer shows).
        /// </summary>
        public void Own(VisualElement parent) => Track(parent);

        /// <summary>A <see cref="Label"/> with this text and class.</summary>
        public Label Label(VisualElement parent, string key, string text, string ussClass = null)
        {
            var l = Take<Label>(parent, key);
            if (l.text != text) l.text = text;
            if (ussClass != null) l.AddToClassList(ussClass);
            return l;
        }

        /// <summary>Finishes a refresh: removes every child no <see cref="Take{T}"/> asked for.</summary>
        public void End()
        {
            foreach (var p in _parents)
            {
                int keep = _next[p];
                for (int i = p.childCount - 1; i >= keep; i--) p.RemoveAt(i);
            }
            _next.Clear();
            _parents.Clear();
        }

        void Track(VisualElement parent)
        {
            if (parent == null || _next.ContainsKey(parent)) return;
            _next[parent] = 0;
            _parents.Add(parent);
        }

        static T New<T>(string key) where T : VisualElement, new()
        {
            var e = new T();
            var classes = new List<string>(e.GetClasses());
            Tags.Add(e, new Tag { Key = key, BaseClasses = classes.ToArray() });
            return e;
        }
    }
}
