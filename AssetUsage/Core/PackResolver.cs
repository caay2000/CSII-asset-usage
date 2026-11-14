using System;
using System.Text.RegularExpressions;

namespace AssetUsage.Core
{
    public static class PackSource
    {
        public const string Game = "game";
        public const string ParadoxMods = "pdx";
        public const string Local = "local";
        public const string Runtime = "runtime";
        public const string Unknown = "unknown";
    }

    public sealed class AssetOrigin
    {
        public bool HasAsset { get; set; }

        public bool Builtin { get; set; }

        public string Database { get; set; }

        public string PlatformId { get; set; }

        public string DisplayName { get; set; }

        public string Path { get; set; }

        public string Uri { get; set; }
    }

    public sealed class PackRef
    {
        public string Id { get; set; }

        public string Source { get; set; }

        public string PdxId { get; set; }

        public string Name { get; set; }

        public string Path { get; set; }
    }

    public static class PackResolver
    {
        static readonly Regex PdxFolder = new Regex(@"pdx_mods/(\d+)_[^/]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex ModsFolder = new Regex(@"/Mods/([^/]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static PackRef Resolve(AssetOrigin origin)
        {
            if (origin == null || !origin.HasAsset)
            {
                return origin != null && origin.Builtin ? Game() : Runtime();
            }

            if (origin.Builtin || string.Equals(origin.Database, "Game", StringComparison.OrdinalIgnoreCase))
            {
                return Game();
            }

            string path = Normalize(origin.Path);
            string uri = Normalize(origin.Uri);
            string pdxId = !string.IsNullOrEmpty(origin.PlatformId) ? origin.PlatformId : PdxIdFrom(path) ?? PdxIdFrom(uri);
            if (pdxId != null)
            {
                return new PackRef
                {
                    Id = PackSource.ParadoxMods + ":" + pdxId,
                    Source = PackSource.ParadoxMods,
                    PdxId = pdxId,
                    Name = string.IsNullOrEmpty(origin.DisplayName) ? null : origin.DisplayName,
                    Path = PackFolder(path, PdxFolder),
                };
            }

            if (string.Equals(origin.Database, "ParadoxMods", StringComparison.OrdinalIgnoreCase))
            {
                return new PackRef { Id = PackSource.ParadoxMods + ":?", Source = PackSource.ParadoxMods, Name = origin.DisplayName };
            }

            string folder = ModsFolderName(path) ?? ModsFolderName(uri);
            if (folder != null)
            {
                return new PackRef { Id = PackSource.Local + ":" + folder, Source = PackSource.Local, Name = folder, Path = PackFolder(path, ModsFolder) };
            }

            return new PackRef { Id = PackSource.Unknown + ":" + (origin.Database ?? "?"), Source = PackSource.Unknown, Name = origin.Database, Path = path };
        }

        static PackRef Game() => new PackRef { Id = PackSource.Game, Source = PackSource.Game, Name = "Game" };

        static PackRef Runtime() => new PackRef { Id = PackSource.Runtime, Source = PackSource.Runtime, Name = "Created at runtime (EAI, code mods)" };

        static string Normalize(string path) => string.IsNullOrEmpty(path) ? null : path.Replace('\\', '/');

        static string PdxIdFrom(string path)
        {
            if (path == null)
            {
                return null;
            }

            var match = PdxFolder.Match(path);
            return match.Success ? match.Groups[1].Value : null;
        }

        static string ModsFolderName(string path)
        {
            if (path == null)
            {
                return null;
            }

            var match = ModsFolder.Match(path);
            return match.Success && !match.Groups[1].Value.Equals("pdx_mods", StringComparison.OrdinalIgnoreCase) ? match.Groups[1].Value : null;
        }

        static string PackFolder(string path, Regex folder)
        {
            if (path == null)
            {
                return null;
            }

            var match = folder.Match(path);
            return match.Success ? path.Substring(0, match.Index + match.Length) : null;
        }
    }
}
