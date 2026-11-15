using System;
using System.Collections.Generic;

namespace AssetUsage.Core
{
    public sealed class PackRecord
    {
        public string Source { get; set; }

        public string PdxId { get; set; }

        public string Name { get; set; }

        public string Path { get; set; }

        public int PrefabCount { get; set; }

        public SortedDictionary<string, int> Types { get; set; } = new SortedDictionary<string, int>(StringComparer.Ordinal);
    }

    public sealed class PrefabRecord
    {
        public string Type { get; set; }

        public List<string> Packs { get; set; } = new List<string>();

        public List<string> Refs { get; set; } = new List<string>();
    }

    public sealed class Inventory
    {
        public const int CurrentSchema = 1;

        public int Schema { get; set; } = CurrentSchema;

        public string GeneratedAt { get; set; }

        public string Fingerprint { get; set; }

        public SortedDictionary<string, PackRecord> Packs { get; set; } = new SortedDictionary<string, PackRecord>(StringComparer.Ordinal);

        public Dictionary<string, PrefabRecord> Prefabs { get; set; } = new Dictionary<string, PrefabRecord>(StringComparer.Ordinal);

        public static string Key(string type, string name) => type + ":" + name;

        public void Add(string type, string name, PackRef pack, IEnumerable<string> refs)
        {
            string key = Key(type, name);
            if (!Prefabs.TryGetValue(key, out var prefab))
            {
                prefab = new PrefabRecord { Type = type };
                Prefabs[key] = prefab;
            }

            if (!prefab.Packs.Contains(pack.Id))
            {
                prefab.Packs.Add(pack.Id);
                Count(pack, type);
            }

            foreach (var target in refs)
            {
                if (target != key && !prefab.Refs.Contains(target))
                {
                    prefab.Refs.Add(target);
                }
            }
        }

        public IReadOnlyList<string> RefsOf(string key) =>
            Prefabs.TryGetValue(key, out var prefab) ? prefab.Refs : (IReadOnlyList<string>)Array.Empty<string>();

        void Count(PackRef pack, string type)
        {
            if (!Packs.TryGetValue(pack.Id, out var record))
            {
                record = new PackRecord { Source = pack.Source, PdxId = pack.PdxId, Name = pack.Name, Path = pack.Path };
                Packs[pack.Id] = record;
            }

            record.Name ??= pack.Name;
            record.Path ??= pack.Path;
            record.PrefabCount++;
            record.Types.TryGetValue(type, out int n);
            record.Types[type] = n + 1;
        }
    }
}
