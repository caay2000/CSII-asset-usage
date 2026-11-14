using System;
using System.Collections.Generic;

namespace AssetUsage.Core
{
    public static class Propagation
    {
        public const int MaxVia = 20;

        public static Dictionary<string, List<string>> Indirect(IEnumerable<string> direct, Func<string, IReadOnlyList<string>> refs)
        {
            var directSet = new HashSet<string>(direct, StringComparer.Ordinal);
            var reached = new HashSet<string>(directSet, StringComparer.Ordinal);
            var queue = new Queue<string>(directSet);
            var indirect = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            while (queue.Count > 0)
            {
                string from = queue.Dequeue();
                foreach (var to in refs(from))
                {
                    if (directSet.Contains(to))
                    {
                        continue;
                    }

                    if (!indirect.TryGetValue(to, out var via))
                    {
                        via = new List<string>();
                        indirect[to] = via;
                    }

                    if (via.Count < MaxVia && !via.Contains(from))
                    {
                        via.Add(from);
                    }

                    if (reached.Add(to))
                    {
                        queue.Enqueue(to);
                    }
                }
            }

            return indirect;
        }
    }
}
