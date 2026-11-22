using AssetUsage.Systems;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace AssetUsage
{
    public class Mod : IMod
    {
        public static ILog Log = LogManager.GetLogger("asset-usage").SetShowsErrorsInUI(false);

        public static string Version { get; } = typeof(Mod).Assembly.GetName().Version is System.Version v ? $"{v.Major}.{v.Minor}.{v.Build}" : "?";

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info($"{nameof(OnLoad)} v{Version}, data in {Storage.Folder}");
            try
            {
                updateSystem.UpdateAt<UsageSystem>(SystemUpdatePhase.ModificationEnd);
                updateSystem.UpdateAt<Systems.LoadIssues.LoadIssuePanelSystem>(SystemUpdatePhase.UIUpdate);
                RegisterUIAssets();
            }
            catch (System.Exception e)
            {
                Log.Error(e);
            }
        }

        void RegisterUIAssets()
        {
            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
            {
                Colossal.UI.UIManager.defaultUISystem?.AddHostLocation("assetusage", System.IO.Path.GetDirectoryName(asset.path), false);
            }
            else
            {
                Log.Warn("Mod folder not found; the panel icon will be missing.");
            }
        }

        public void OnDispose()
        {
            Log.Info(nameof(OnDispose));
            UsageSystem.Instance?.FlushNow("exit", true);
            Storage.WaitForWrites();
        }
    }
}
