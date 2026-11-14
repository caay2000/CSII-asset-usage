using AssetUsage.Systems;
using Colossal.Logging;
using Game;
using Game.Modding;

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
            }
            catch (System.Exception e)
            {
                Log.Error(e);
            }
        }

        public void OnDispose()
        {
            Log.Info(nameof(OnDispose));
            UsageSystem.Instance?.FlushNow("exit");
            Storage.WaitForWrites();
        }
    }
}
