using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using AssetUsage.Core;
using Colossal.IO.AssetDatabase;
using Game.Prefabs;

namespace AssetUsage
{
    public sealed class LoadedPrefabs
    {
        static readonly FieldInfo PrefabsField = typeof(PrefabSystem).GetField("m_Prefabs", BindingFlags.Instance | BindingFlags.NonPublic);

        readonly PrefabBase[] prefabs;
        readonly string[] keys;
        readonly List<ComponentBase> components = new List<ComponentBase>();
        readonly List<PrefabBase> dependencies = new List<PrefabBase>();
        readonly Stopwatch working = new Stopwatch();
        Inventory building;
        int next;
        int failed;

        LoadedPrefabs(PrefabBase[] prefabs, string[] keys)
        {
            this.prefabs = prefabs;
            this.keys = keys;
            Fingerprint = InventoryFingerprint.Of(keys);
        }

        public Inventory Inventory { get; private set; }

        public bool Ready => Inventory != null;

        public string Fingerprint { get; }

        public int Count => keys.Length;

        public static bool IsStale(LoadedPrefabs current, PrefabSystem system) => current == null || All(system).Count != current.Count;

        public string Key(int index) => index >= 0 && index < keys.Length ? keys[index] : null;

        public static LoadedPrefabs Snapshot(PrefabSystem system)
        {
            var started = DateTime.UtcNow;
            var prefabs = All(system).ToArray();
            var keys = new string[prefabs.Length];
            for (int i = 0; i < prefabs.Length; i++)
            {
                keys[i] = prefabs[i] != null ? KeyOf(prefabs[i]) : null;
            }

            var loaded = new LoadedPrefabs(prefabs, keys);
            Mod.Log.Info($"Listed {prefabs.Length} prefabs in {(DateTime.UtcNow - started).TotalMilliseconds:F0} ms, fingerprint {loaded.Fingerprint}.");
            return loaded;
        }

        public bool TryReuse(Inventory cached)
        {
            if (!InventoryFingerprint.Matches(cached, Fingerprint, DateTime.Now))
            {
                return false;
            }

            Inventory = cached;
            Mod.Log.Info($"The loaded prefabs match the saved inventory from {cached.GeneratedAt}; reusing it.");
            return true;
        }

        public bool Step(TimeSpan budget)
        {
            if (Ready)
            {
                return true;
            }

            building ??= new Inventory { Fingerprint = Fingerprint };
            var slice = Stopwatch.StartNew();
            working.Start();
            while (next < prefabs.Length && slice.Elapsed < budget)
            {
                Add(next++);
            }

            working.Stop();
            if (next < prefabs.Length)
            {
                return false;
            }

            building.GeneratedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            Inventory = building;
            Mod.Log.Info($"Inventory: {prefabs.Length} prefabs in {Inventory.Packs.Count} packs read in {working.Elapsed.TotalSeconds:F1} s of work ({failed} failed).");
            LogSummary(Inventory);
            return true;
        }

        public void Finish() => Step(TimeSpan.MaxValue);

        void Add(int index)
        {
            var prefab = prefabs[index];
            if (prefab == null)
            {
                return;
            }

            try
            {
                building.Add(prefab.GetType().Name, prefab.name, PackResolver.Resolve(OriginOf(prefab)), Dependencies(prefab, components, dependencies));
            }
            catch (Exception e)
            {
                if (failed++ < 10)
                {
                    Mod.Log.Warn($"Could not read {keys[index]}: {e.Message}");
                }

                building.Add(prefab.GetType().Name, prefab.name, new PackRef { Id = PackSource.Unknown + ":error", Source = PackSource.Unknown, Name = "Could not read" }, Array.Empty<string>());
            }
        }

        static string KeyOf(PrefabBase prefab) => Inventory.Key(prefab.GetType().Name, prefab.name);

        static List<PrefabBase> All(PrefabSystem system)
        {
            if (PrefabsField?.GetValue(system) is List<PrefabBase> list)
            {
                return list;
            }

            throw new InvalidOperationException("PrefabSystem.m_Prefabs not found; the game has changed.");
        }

        static AssetOrigin OriginOf(PrefabBase prefab)
        {
            var asset = prefab.asset;
            var origin = new AssetOrigin { HasAsset = asset != null, Builtin = prefab.isBuiltin };
            if (asset == null || asset.database == null)
            {
                origin.HasAsset = asset != null;
                origin.Database = asset != null ? "none" : null;
                return origin;
            }

            var meta = asset.GetMeta();
            origin.Database = asset.database?.name;
            origin.PlatformId = meta.platformID;
            origin.DisplayName = meta.displayName;
            origin.Path = meta.path;
            origin.Uri = SafeUri(asset);
            return origin;
        }

        static string SafeUri(AssetData asset)
        {
            try
            {
                return asset.uri;
            }
            catch
            {
                return null;
            }
        }

        static IEnumerable<string> Dependencies(PrefabBase prefab, List<ComponentBase> components, List<PrefabBase> dependencies)
        {
            components.Clear();
            dependencies.Clear();
            prefab.TryGet(components);
            foreach (var component in components)
            {
                try
                {
                    component.GetDependencies(dependencies);
                }
                catch (Exception e)
                {
                    Mod.Log.Warn($"Could not read the references of {KeyOf(prefab)} ({component.GetType().Name}): {e.Message}");
                }
            }

            return dependencies.Where(d => d != null).Select(KeyOf).Distinct().ToList();
        }

        static void LogSummary(Inventory inventory)
        {
            var bySource = inventory.Packs.Values
                .GroupBy(p => p.Source)
                .Select(g => $"{g.Key}: {g.Count()} packs, {g.Sum(p => p.PrefabCount)} prefabs")
                .ToList();
            Mod.Log.Info("Inventory by source: " + string.Join("; ", bySource));

            foreach (var id in new[] { PackSource.Runtime })
            {
                if (inventory.Packs.TryGetValue(id, out var pack))
                {
                    Mod.Log.Info($"{id} prefab types: " + string.Join(", ", pack.Types.OrderByDescending(t => t.Value).Select(t => $"{t.Key} {t.Value}")));
                }
            }

            foreach (var pack in inventory.Packs.Where(p => p.Value.Source == PackSource.Unknown))
            {
                Mod.Log.Info($"Unattributed pack {pack.Key} ({pack.Value.PrefabCount} prefabs, path {pack.Value.Path}).");
            }
        }
    }
}
