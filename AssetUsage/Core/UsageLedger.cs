using System;
using System.Collections.Generic;

namespace AssetUsage.Core
{
    public sealed class CityRecord
    {
        public string Name { get; set; }

        public string FirstSeen { get; set; }

        public string LastSeen { get; set; }
    }

    public sealed class CityUsage
    {
        public string FirstDirect { get; set; }

        public string LastDirect { get; set; }

        public string FirstIndirect { get; set; }

        public string LastIndirect { get; set; }

        public List<string> Via { get; set; }
    }

    public sealed class PrefabUsage
    {
        public string Type { get; set; }

        public List<string> Packs { get; set; } = new List<string>();

        public SortedDictionary<string, CityUsage> Cities { get; set; } = new SortedDictionary<string, CityUsage>(StringComparer.Ordinal);
    }

    public sealed class UsageLedger
    {
        public const int CurrentSchema = 1;

        public int Schema { get; set; } = CurrentSchema;

        public SortedDictionary<string, CityRecord> Cities { get; set; } = new SortedDictionary<string, CityRecord>(StringComparer.Ordinal);

        public SortedDictionary<string, PrefabUsage> Prefabs { get; set; } = new SortedDictionary<string, PrefabUsage>(StringComparer.Ordinal);

        public void Record(string cityId, string cityName, string day, IEnumerable<string> direct, IReadOnlyDictionary<string, List<string>> indirect, Func<string, PrefabRecord> lookup)
        {
            SeeCity(cityId, cityName, day);

            foreach (var key in direct)
            {
                var usage = City(key, cityId, lookup);
                usage.FirstDirect = Earliest(usage.FirstDirect, day);
                usage.LastDirect = Latest(usage.LastDirect, day);
            }

            foreach (var pair in indirect)
            {
                var usage = City(pair.Key, cityId, lookup);
                usage.FirstIndirect = Earliest(usage.FirstIndirect, day);
                usage.LastIndirect = Latest(usage.LastIndirect, day);
                usage.Via ??= new List<string>();
                foreach (var from in pair.Value)
                {
                    if (usage.Via.Count >= Propagation.MaxVia)
                    {
                        break;
                    }

                    if (!usage.Via.Contains(from))
                    {
                        usage.Via.Add(from);
                    }
                }
            }
        }

        void SeeCity(string cityId, string cityName, string day)
        {
            if (!Cities.TryGetValue(cityId, out var city))
            {
                city = new CityRecord { FirstSeen = day };
                Cities[cityId] = city;
            }

            if (!string.IsNullOrEmpty(cityName))
            {
                city.Name = cityName;
            }

            city.LastSeen = Latest(city.LastSeen, day);
        }

        CityUsage City(string key, string cityId, Func<string, PrefabRecord> lookup)
        {
            if (!Prefabs.TryGetValue(key, out var prefab))
            {
                prefab = new PrefabUsage();
                Prefabs[key] = prefab;
            }

            var known = lookup(key);
            if (known != null)
            {
                prefab.Type = known.Type;
                foreach (var pack in known.Packs)
                {
                    if (!prefab.Packs.Contains(pack))
                    {
                        prefab.Packs.Add(pack);
                    }
                }
            }

            if (!prefab.Cities.TryGetValue(cityId, out var usage))
            {
                usage = new CityUsage();
                prefab.Cities[cityId] = usage;
            }

            return usage;
        }

        static string Earliest(string current, string day) => current == null || string.CompareOrdinal(day, current) < 0 ? day : current;

        static string Latest(string current, string day) => current == null || string.CompareOrdinal(day, current) > 0 ? day : current;
    }
}
