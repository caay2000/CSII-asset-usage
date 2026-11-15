using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AssetUsage.Core;
using Colossal.Serialization.Entities;
using Game;
using Game.City;
using Game.Common;
using Game.Prefabs;
using Game.PSI;
using Game.SceneFlow;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace AssetUsage.Systems
{
    public partial class UsageSystem : GameSystemBase
    {
        static readonly TimeSpan FlushEvery = TimeSpan.FromMinutes(5);
        static readonly TimeSpan BuildBudget = TimeSpan.FromMilliseconds(4);

        sealed class Session
        {
            public string CityId;
            public string CityName;
            public readonly HashSet<int> Direct = new HashSet<int>();
            public int Flushed = -1;
        }

        public static UsageSystem Instance { get; private set; }

        readonly Dictionary<Entity, int> indexOf = new Dictionary<Entity, int>();
        PrefabSystem prefabSystem;
        CityConfigurationSystem cityConfiguration;
        EntityQuery placedQuery;
        EntityQuery createdQuery;
        UsageLedger ledger;
        LoadedPrefabs loaded;
        Task<Inventory> saved;
        Session session;
        DateTime nextFlush;
        int createdFrames;

        protected override void OnCreate()
        {
            base.OnCreate();
            Instance = this;
            prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            cityConfiguration = World.GetOrCreateSystemManaged<CityConfigurationSystem>();
            placedQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<PrefabData>() },
            });
            createdQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[] { ComponentType.ReadOnly<Created>(), ComponentType.ReadOnly<PrefabRef>() },
                None = new[] { ComponentType.ReadOnly<Temp>(), ComponentType.ReadOnly<Deleted>(), ComponentType.ReadOnly<PrefabData>() },
            });
            ledger = Storage.LoadLedger();
            saved = Storage.LoadInventoryAsync();
            GameManager.instance.onGameSaveLoad += OnSaved;
        }

        protected override void OnDestroy()
        {
            FlushNow("destroy", true);
            if (GameManager.instance != null)
            {
                GameManager.instance.onGameSaveLoad -= OnSaved;
            }

            Instance = null;
            base.OnDestroy();
        }

        protected override void OnGamePreload(Purpose purpose, GameMode mode)
        {
            base.OnGamePreload(purpose, mode);
            FlushNow("leaving city", true);
            session = null;
            indexOf.Clear();
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            if (mode != GameMode.Game)
            {
                return;
            }

            try
            {
                RefreshInventory();
                session = new Session { CityId = CityId(), CityName = cityConfiguration.cityName };
                Mod.Log.Info($"City '{session.CityName}' loaded ({purpose}), id {session.CityId}.");
                ScanPlaced();
                FlushNow("city loaded");
            }
            catch (Exception e)
            {
                Mod.Log.Error(e);
            }
        }

        protected override void OnUpdate()
        {
            try
            {
                BuildInventory();
            }
            catch (Exception e)
            {
                Mod.Log.Error(e);
            }

            if (session == null)
            {
                return;
            }

            try
            {
                if (!createdQuery.IsEmptyIgnoreFilter)
                {
                    Collect(createdQuery);
                    createdFrames++;
                }

                if (DateTime.UtcNow >= nextFlush)
                {
                    Collect(placedQuery);
                    FlushNow("periodic");
                }
            }
            catch (Exception e)
            {
                Mod.Log.Error(e);
                session = null;
            }
        }

        public void FlushNow(string reason, bool final = false)
        {
            nextFlush = DateTime.UtcNow + FlushEvery;
            var current = session;
            if (current == null || loaded == null || (current.Direct.Count == current.Flushed && Storage.LastWriteSucceeded))
            {
                return;
            }

            var inventory = loaded.Inventory;
            if (inventory == null && !final)
            {
                return;
            }

            var direct = current.Direct.Select(loaded.Key).Where(k => k != null).ToList();
            current.Flushed = current.Direct.Count;
            string day = DateTime.Now.ToString("yyyy-MM-dd");
            string cityId = current.CityId;
            string cityName = current.CityName;
            int frames = createdFrames;
            string scope = inventory == null ? ", inventory not ready so direct use only" : "";
            Storage.Enqueue($"usage ({reason}: {direct.Count} prefabs used directly, {frames} frames with new entities{scope})", () =>
            {
                var indirect = inventory != null ? Propagation.Indirect(direct, inventory.RefsOf) : new Dictionary<string, List<string>>();
                ledger.Record(cityId, cityName, day, direct, indirect, key => inventory != null && inventory.Prefabs.TryGetValue(key, out var p) ? p : null);
                Mod.Log.Info($"Usage history: {ledger.Prefabs.Count} prefabs in {ledger.Cities.Count} cities ({indirect.Count} used indirectly in this city).");
                return ledger;
            }, Storage.UsagePath);
        }

        void RefreshInventory()
        {
            if (!LoadedPrefabs.IsStale(loaded, prefabSystem))
            {
                return;
            }

            loaded = LoadedPrefabs.Snapshot(prefabSystem);
            if (TryReuseSaved())
            {
                return;
            }

            Mod.Log.Info(saved == null ? "The loaded prefabs changed; building the inventory over the next frames." : "The saved inventory is still being read; building the inventory over the next frames.");
        }

        void BuildInventory()
        {
            if (loaded == null || loaded.Ready)
            {
                return;
            }

            if (TryReuseSaved())
            {
                FlushNow("inventory reused");
                return;
            }

            if (loaded.Step(BuildBudget))
            {
                SaveInventory();
                FlushNow("inventory ready");
            }
        }

        bool TryReuseSaved()
        {
            if (saved == null || !saved.IsCompleted)
            {
                return false;
            }

            var candidate = saved.Result;
            saved = null;
            return loaded.TryReuse(candidate);
        }

        void SaveInventory()
        {
            var inventory = loaded.Inventory;
            Storage.Enqueue("inventory", () => inventory, Storage.InventoryPath);
        }

        void ScanPlaced()
        {
            var started = DateTime.UtcNow;
            int entities = placedQuery.CalculateEntityCount();
            Collect(placedQuery);
            Mod.Log.Info($"Scanned {entities} entities in {(DateTime.UtcNow - started).TotalMilliseconds:F0} ms: {session.Direct.Count} prefabs in use.");
        }

        void Collect(EntityQuery query)
        {
            using (var refs = query.ToComponentDataArray<PrefabRef>(Allocator.TempJob))
            {
                var last = Entity.Null;
                for (int i = 0; i < refs.Length; i++)
                {
                    var prefab = refs[i].m_Prefab;
                    if (prefab == last)
                    {
                        continue;
                    }

                    last = prefab;
                    if (!indexOf.TryGetValue(prefab, out int index))
                    {
                        index = EntityManager.HasComponent<PrefabData>(prefab) ? EntityManager.GetComponentData<PrefabData>(prefab).m_Index : -1;
                        indexOf[prefab] = index;
                    }

                    if (index >= 0)
                    {
                        session.Direct.Add(index);
                    }
                }
            }
        }

        string CityId()
        {
            var guid = Telemetry.GetCurrentSession();
            Mod.Log.Info($"Telemetry session {guid}.");
            return guid != Guid.Empty ? guid.ToString() : "name:" + cityConfiguration.cityName;
        }

        void OnSaved(string saveName, string previewUri, bool start, bool success)
        {
            if (!start && success && session != null)
            {
                try
                {
                    Collect(placedQuery);
                }
                catch (Exception e)
                {
                    Mod.Log.Error(e);
                }

                FlushNow("saved " + saveName);
            }
        }
    }
}
