using System;
using System.Collections.Generic;
using System.Text;

// Unity-free (compiled by tools/autotest-unit-tests with a stub Main).
namespace RealTracks
{
    /// <summary>One distinct fallback (area + reason) with how often it fired since a mark.</summary>
    public class FallbackGroup
    {
        public string Area = "", Why = "";
        public int Count;
        public override string ToString() { return Area + ": " + Why + (Count > 1 ? " (x" + Count + ")" : ""); }
    }

    /// <summary>
    /// Registry of every fallback that fired where the mod would rather do something else: the custom Quick Race column falling back to the stock grid, the
    /// intro overlay not hosted, art pieces skipped, colour statistics unavailable, a catch that leaves stock behaviour in place. Call sites say
    /// RtFallbacks.Note("area", "why"); the line "RT FALLBACK: area: why" goes to the game log (first 3 times per distinct pair) and the auto-test's
    /// "uiFallbacks" check lists what fired during each item. Never throws.
    /// </summary>
    public static class RtFallbacks
    {
        private const int MaxEntries = 2000, LogTimes = 3;
        private static readonly List<string[]> sEvents = new List<string[]>();        // [area, why] per occurrence, in order
        private static readonly Dictionary<string, int> sSeen = new Dictionary<string, int>();
        private static int sTotal;

        /// <summary>Occurrences so far (use as the mark for Since).</summary>
        public static int Mark() { lock (sEvents) return sTotal; }

        public static void Note(string area, string why)
        {
            try
            {
                area = (area ?? "").Trim(); why = (why ?? "").Trim();
                if (area.Length == 0) area = "?";
                int n;
                lock (sEvents)
                {
                    string key = area + "\n" + why;
                    sSeen.TryGetValue(key, out n); n++; sSeen[key] = n;
                    sTotal++;
                    if (sEvents.Count < MaxEntries) sEvents.Add(new[] { area, why });
                }
                if (n <= LogTimes) Main.Log("RT FALLBACK: " + area + ": " + why + (n == LogTimes ? " (further repeats are counted, not logged)" : ""));
            }
            catch (Exception) { }
        }

        /// <summary>Distinct fallbacks that fired since `mark`, in first-seen order, with counts.</summary>
        public static List<FallbackGroup> Since(int mark) { return Between(mark, int.MaxValue); }

        /// <summary>Distinct fallbacks noted from occurrence number `from` up to (not including) `to`.</summary>
        public static List<FallbackGroup> Between(int from, int to)
        {
            List<FallbackGroup> res = new List<FallbackGroup>();
            lock (sEvents)
            {
                for (int i = Math.Max(0, from); i < sEvents.Count && i < to; i++)
                {
                    FallbackGroup g = res.Find(delegate (FallbackGroup x) { return x.Area == sEvents[i][0] && x.Why == sEvents[i][1]; });
                    if (g == null) { g = new FallbackGroup(); g.Area = sEvents[i][0]; g.Why = sEvents[i][1]; res.Add(g); }
                    g.Count++;
                }
            }
            return res;
        }

        public static void Clear() { lock (sEvents) { sEvents.Clear(); sSeen.Clear(); sTotal = 0; } }

        public static string Join(List<FallbackGroup> l)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < l.Count; i++) { if (i > 0) sb.Append("; "); sb.Append(l[i].ToString()); }
            return sb.ToString();
        }
    }
}
