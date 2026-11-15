using System;
using System.IO;
using System.Threading.Tasks;
using AssetUsage.Core;
using Colossal.PSI.Environment;
using Newtonsoft.Json;

namespace AssetUsage
{
    public static class Storage
    {
        static readonly object gate = new object();
        static Task last = Task.CompletedTask;
        static volatile bool lastWriteSucceeded = true;

        static readonly JsonSerializerSettings settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
            {
                NamingStrategy = new Newtonsoft.Json.Serialization.CamelCaseNamingStrategy(),
            },
        };

        public static bool LastWriteSucceeded => lastWriteSucceeded;

        public static string Folder => Path.Combine(EnvPath.kUserDataPath, "ModsData", "asset-usage");

        public static string InventoryPath => Path.Combine(Folder, "inventory.json");

        public static string UsagePath => Path.Combine(Folder, "usage.json");

        public static UsageLedger LoadLedger()
        {
            try
            {
                if (!File.Exists(UsagePath))
                {
                    return new UsageLedger();
                }

                var ledger = JsonConvert.DeserializeObject<UsageLedger>(File.ReadAllText(UsagePath), settings) ?? new UsageLedger();
                Mod.Log.Info($"Loaded usage history: {ledger.Prefabs.Count} prefabs in {ledger.Cities.Count} cities.");
                return ledger;
            }
            catch (Exception e)
            {
                string backup = UsagePath + ".broken-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                Mod.Log.Error($"Could not read {UsagePath}; keeping it as {backup} and starting a new history. {e}");
                TryCopy(UsagePath, backup);
                return new UsageLedger();
            }
        }

        public static Task<Inventory> LoadInventoryAsync() => Task.Run(() =>
        {
            try
            {
                if (!File.Exists(InventoryPath))
                {
                    return null;
                }

                var started = DateTime.UtcNow;
                Inventory inventory;
                using (var reader = new JsonTextReader(new StreamReader(InventoryPath)))
                {
                    inventory = JsonSerializer.Create(settings).Deserialize<Inventory>(reader);
                }

                Mod.Log.Info($"Read the saved inventory ({inventory?.Prefabs.Count ?? 0} prefabs) in {(DateTime.UtcNow - started).TotalSeconds:F1} s.");
                return inventory;
            }
            catch (Exception e)
            {
                Mod.Log.Warn($"Could not read the saved inventory, it will be rebuilt: {e.Message}");
                return null;
            }
        });

        public static void Enqueue(string what, Func<object> snapshot, string path)
        {
            lock (gate)
            {
                last = last.ContinueWith(_ => Write(what, snapshot, path), TaskScheduler.Default);
            }
        }

        public static void WaitForWrites()
        {
            Task pending;
            lock (gate)
            {
                pending = last;
            }

            try
            {
                pending.Wait(TimeSpan.FromSeconds(30));
            }
            catch (Exception e)
            {
                Mod.Log.Error(e);
            }
        }

        static void Write(string what, Func<object> snapshot, string path)
        {
            try
            {
                var started = DateTime.UtcNow;
                Directory.CreateDirectory(Folder);
                string tmp = path + ".tmp";
                using (var writer = new StreamWriter(tmp))
                {
                    JsonSerializer.Create(settings).Serialize(writer, snapshot());
                }

                if (File.Exists(path))
                {
                    File.Replace(tmp, path, null);
                }
                else
                {
                    File.Move(tmp, path);
                }

                lastWriteSucceeded = true;
                Mod.Log.Info($"Wrote {what} to {path} in {(DateTime.UtcNow - started).TotalMilliseconds:F0} ms.");
            }
            catch (Exception e)
            {
                lastWriteSucceeded = false;
                Mod.Log.Error($"Could not write {what}: {e}");
            }
        }

        static void TryCopy(string from, string to)
        {
            try
            {
                File.Copy(from, to, true);
            }
            catch (Exception e)
            {
                Mod.Log.Warn(e.Message);
            }
        }
    }
}
