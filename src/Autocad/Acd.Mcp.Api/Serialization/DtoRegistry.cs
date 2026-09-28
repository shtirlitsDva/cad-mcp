using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Acd.Mcp.Serialization
{
    // The folder a DTO came from. A User DTO overrides the System DTO for the
    // same type.
    public enum DtoLayer
    {
        System,
        User,
    }

    // Thread-safe map from runtime System.Type to its registered projection.
    //
    // Each type has one slot per layer, and TryGet returns the User slot
    // before the System slot. The override rule therefore does not depend on
    // the order in which the loader compiles files: an incremental refresh
    // that recompiles only a changed system file cannot hide a user DTO.
    //
    // Lives in Acd.Mcp.Api (default ALC) — Roslyn-emitted IL from DTO .csx
    // submissions JIT-loads through the default ALC, so DtoRegistrationApi's
    // field of this type must be resolvable from that ALC. The composite data
    // providers, converter factory, and loader continue to live in Acd.Mcp
    // (isolated ALC) and reach in through InternalsVisibleTo for TryGet.
    public sealed class DtoRegistry
    {
        private readonly ConcurrentDictionary<Type, Layers> _entries = new();

        public void Register<T>(Func<T, object?> projection, DtoLayer layer, string source)
        {
            if (projection is null) throw new ArgumentNullException(nameof(projection));
            IDtoProjection entry = new TypedProjection<T>(projection, source);
            _entries.AddOrUpdate(typeof(T),
                _ => Layers.Empty.With(layer, entry),
                (_, existing) => existing.With(layer, entry));
        }

        // Used by the loader's reload path: clears every registration so the
        // next scan can repopulate from scratch. Cheaper and more predictable
        // than diffing.
        public void Clear() => _entries.Clear();

        internal bool TryGet(Type runtimeType, out IDtoProjection projection)
        {
            if (_entries.TryGetValue(runtimeType, out var layers))
            {
                projection = layers.User ?? layers.System!;
                return true;
            }
            projection = null!;
            return false;
        }

        public IReadOnlyCollection<Type> RegisteredTypes =>
            _entries.Keys.ToList().AsReadOnly();

        // Immutable, so AddOrUpdate can replace it without a lock. At least
        // one slot is set for every stored entry.
        private sealed record Layers(IDtoProjection? System, IDtoProjection? User)
        {
            public static readonly Layers Empty = new(null, null);

            public Layers With(DtoLayer layer, IDtoProjection entry) => layer switch
            {
                DtoLayer.System => this with { System = entry },
                DtoLayer.User => this with { User = entry },
                _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, null),
            };
        }
    }
}
