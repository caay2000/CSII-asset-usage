using System;
using System.Collections.Generic;
using System.Globalization;

namespace AssetUsage.Core
{
    public static class InventoryFingerprint
    {
        public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

        const ulong Offset = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;

        public static string Of(IEnumerable<string> keys)
        {
            ulong sum = 0;
            ulong xor = 0;
            int count = 0;
            foreach (var key in keys)
            {
                if (key == null)
                {
                    continue;
                }

                ulong hash = Hash(key);
                sum += hash;
                xor ^= hash * Prime;
                count++;
            }

            return $"{count}-{sum:x16}-{xor:x16}";
        }

        public static bool Matches(Inventory cached, string fingerprint, DateTime now)
        {
            if (cached == null || cached.Schema != Inventory.CurrentSchema || cached.Fingerprint != fingerprint)
            {
                return false;
            }

            return DateTime.TryParseExact(cached.GeneratedAt, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var generated) && now - generated < MaxAge && generated <= now;
        }

        static ulong Hash(string key)
        {
            ulong hash = Offset;
            foreach (char c in key)
            {
                hash ^= c;
                hash *= Prime;
            }

            return hash;
        }
    }
}
