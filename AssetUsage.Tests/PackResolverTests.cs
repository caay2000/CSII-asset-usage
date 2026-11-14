using AssetUsage.Core;
using Xunit;

namespace AssetUsage.Tests
{
    public class PackResolverTests
    {
        const string Cache = "C:/Users/me/AppData/LocalLow/Colossal Order/Cities Skylines II/.cache/Mods/pdx_mods/";

        [Fact]
        public void Prefabs_without_an_asset_that_the_game_ships_belong_to_the_game()
        {
            Assert.Equal(PackSource.Game, PackResolver.Resolve(new AssetOrigin { HasAsset = false, Builtin = true }).Id);
        }

        [Fact]
        public void Prefabs_without_an_asset_created_by_mods_are_runtime()
        {
            Assert.Equal(PackSource.Runtime, PackResolver.Resolve(new AssetOrigin { HasAsset = false }).Id);
        }

        [Fact]
        public void Assets_from_the_game_database_belong_to_the_game()
        {
            Assert.Equal(PackSource.Game, PackResolver.Resolve(new AssetOrigin { HasAsset = true, Database = "Game", Path = "x/Content/Game/a.Prefab" }).Id);
        }

        [Fact]
        public void Paradox_mods_are_identified_by_their_platform_id()
        {
            var pack = PackResolver.Resolve(new AssetOrigin { HasAsset = true, Database = "ParadoxMods", PlatformId = "130410", DisplayName = "Oberstdorf", Path = Cache + "130410_3/DB.cok" });

            Assert.Equal("pdx:130410", pack.Id);
            Assert.Equal("Oberstdorf", pack.Name);
            Assert.Equal(Cache + "130410_3", pack.Path);
        }

        [Fact]
        public void Paradox_mods_without_a_platform_id_are_identified_by_their_cache_folder()
        {
            var pack = PackResolver.Resolve(new AssetOrigin { HasAsset = true, Database = "ParadoxMods", Path = Cache.Replace('/', '\\') + "126562_3\\Sully.cok" });

            Assert.Equal("pdx:126562", pack.Id);
        }

        [Fact]
        public void Paradox_mods_are_identified_by_the_asset_uri_when_the_path_is_missing()
        {
            var pack = PackResolver.Resolve(new AssetOrigin { HasAsset = true, Database = "ParadoxMods", Uri = "assetdb://paradoxmods/pdx_mods/130410_3/DB.cok@A.Prefab" });

            Assert.Equal("pdx:130410", pack.Id);
        }

        [Fact]
        public void Local_assets_are_grouped_by_their_folder_under_mods()
        {
            var pack = PackResolver.Resolve(new AssetOrigin { HasAsset = true, Database = "User", Path = "C:/Users/me/AppData/LocalLow/Colossal Order/Cities Skylines II/Mods/MyPack/Sub/a.Prefab" });

            Assert.Equal("local:MyPack", pack.Id);
            Assert.Equal(PackSource.Local, pack.Source);
        }

        [Fact]
        public void Assets_that_match_nothing_are_unknown_by_database()
        {
            var pack = PackResolver.Resolve(new AssetOrigin { HasAsset = true, Database = "User", Path = "D:/elsewhere/a.Prefab" });

            Assert.Equal("unknown:User", pack.Id);
            Assert.Equal(PackSource.Unknown, pack.Source);
        }
    }
}
