using System;
using AssetUsage.Core.LoadIssues;
using Colossal.Serialization.Entities;
using Game;
using Game.Prefabs;

namespace AssetUsage.Systems.LoadIssues
{
    public partial class LoadIssueDiagnosticSystem : GameSystemBase
    {
        PrefabSystem prefabSystem;

        protected override void OnCreate()
        {
            base.OnCreate();
            prefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            Enabled = false;
        }

        protected override void OnUpdate()
        {
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
                var started = DateTime.UtcNow;
                var report = new LoadIssueReport();
                new MissingPrefabScanner(EntityManager, prefabSystem, report).Scan();
                new BrokenNodeScanner(EntityManager, report).Scan();
                double elapsed = (DateTime.UtcNow - started).TotalMilliseconds;
                foreach (string line in report.Lines(elapsed))
                {
                    Mod.Log.Info(line);
                }
            }
            catch (Exception e)
            {
                Mod.Log.Error(e);
            }
        }
    }
}
