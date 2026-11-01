using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// Unity-free (see AutoTestJson.cs): the auto-test's resolution matrix model (request option "resolutions"), its per-resolution results and the two
// informational checks "resolutions" and "uiFallbacks".
namespace RealTracks
{
    /// <summary>One entry of the request's "resolutions" list, e.g. "3840x1080".</summary>
    public class AtResolution
    {
        public int W, H;
        public override string ToString() { return W + "x" + H; }

        public const int MinW = 640, MaxW = 7680, MinH = 360, MaxH = 4320, MaxEntries = 6;

        /// <summary>"3840x1080" (also "3840 x 1080" or "3840X1080").</summary>
        public static bool TryParse(string s, out AtResolution r, out string why)
        {
            r = null; why = null;
            if (s == null) { why = "not text"; return false; }
            string t = s.Trim().ToLowerInvariant().Replace(" ", "");
            int x = t.IndexOf('x');
            int w, h;
            if (x <= 0 || x >= t.Length - 1 || !int.TryParse(t.Substring(0, x), NumberStyles.None, CultureInfo.InvariantCulture, out w) || !int.TryParse(t.Substring(x + 1), NumberStyles.None, CultureInfo.InvariantCulture, out h))
            { why = "'" + s + "' is not WIDTHxHEIGHT"; return false; }
            if (w < MinW || w > MaxW || h < MinH || h > MaxH) { why = "'" + s + "' is outside " + MinW + "x" + MinH + " .. " + MaxW + "x" + MaxH; return false; }
            r = new AtResolution(); r.W = w; r.H = h;
            return true;
        }

        /// <summary>Parses the request value (an array of strings, or one string): valid distinct entries in order (at most MaxEntries); problems go to `errors`.</summary>
        public static List<AtResolution> ParseList(object raw, List<string> errors)
        {
            List<AtResolution> res = new List<AtResolution>();
            if (raw == null) return res;
            List<object> arr = raw as List<object>;
            if (arr == null && raw is string) { arr = new List<object>(); arr.Add(raw); }
            if (arr == null) { errors.Add("resolutions must be an array of \"WIDTHxHEIGHT\" strings"); return res; }
            foreach (object o in arr)
            {
                AtResolution r; string why;
                if (!TryParse(o as string, out r, out why)) { errors.Add(why); continue; }
                if (res.Exists(delegate (AtResolution q) { return q.W == r.W && q.H == r.H; })) { errors.Add(r + " is listed twice"); continue; }
                if (res.Count >= MaxEntries) { errors.Add(r + " ignored: at most " + MaxEntries + " resolutions"); continue; }
                res.Add(r);
            }
            return res;
        }
    }

    /// <summary>What happened at one resolution of the matrix (the Quick Race setup screen of the first item).</summary>
    public class AtResResult
    {
        public string Resolution = "";            // as requested, "3840x1080"
        public int ReqW, ReqH, ActualW, ActualH;
        public bool Applied;                      // Screen.width x height reached the requested size
        public string Skipped = "";               // non-empty: not tried (the display does not offer it ...) or failed, with the reason
        public bool Rebuilt;                      // the custom column was laid out again after the change
        public string ColumnMode = "";            // left | scaled | none | grid | "" (no column)
        public double Scale = 1;
        public bool Fits, InsideScreen;
        public int Entries;
        public double CanvasW, CanvasH;
        public string Shot = "";                  // run-relative file name
        public string Note = "";
        public bool StaleInside = true;           // before the re-layout: was the column inside the screen? (only meaningful when Rebuilt)

        /// <summary>The column is usable at this resolution: laid out, inside the screen, and either a column or an announced grid fallback.</summary>
        public bool Good { get { return Applied && Skipped.Length == 0 && (ColumnMode == "left" || ColumnMode == "scaled") && Fits && InsideScreen; } }

        public override string ToString()
        {
            if (Skipped.Length > 0) return Resolution + ": " + Skipped;
            string s = Resolution + ": column " + (ColumnMode.Length > 0 ? ColumnMode : "none") + (ColumnMode == "scaled" ? " x" + Scale.ToString("0.00", CultureInfo.InvariantCulture) : "")
                + ", " + (Fits ? "fits" : "does not fit") + ", " + (InsideScreen ? "inside screen" : "OUTSIDE screen") + ", canvas " + CanvasW.ToString("0", CultureInfo.InvariantCulture) + "x" + CanvasH.ToString("0", CultureInfo.InvariantCulture)
                + (Rebuilt ? ", re-laid out" + (StaleInside ? "" : " (the old layout was outside the screen)") : ", not re-laid out")
                + (Shot.Length > 0 ? ", " + Shot : ", no screenshot");
            if (Note.Length > 0) s += " [" + Note + "]";
            return s;
        }
    }

    public static class AtResChecks
    {
        /// <summary>Informational: per resolution the column result and screenshot. Pass = every listed resolution was applied and the column fits inside the screen.</summary>
        public static AtCheck Resolutions(AtItemData d)
        {
            AtCheck c = new AtCheck("resolutions", true, "", d.ResResults.Count);
            c.Informational = true;
            if (d.ResNote.Length > 0 && d.ResResults.Count == 0) { c.Pass = false; c.Detail = d.ResNote; return c; }
            List<string> parts = new List<string>(); int good = 0, tried = 0;
            foreach (AtResResult r in d.ResResults)
            {
                parts.Add(r.ToString());
                if (r.Skipped.Length == 0) tried++;
                if (r.Good) good++;
            }
            c.Value = good;
            c.Pass = d.ResResults.Count > 0 && good == tried && tried > 0 && d.ResRestored;
            c.Detail = good + " of " + d.ResResults.Count + " resolutions with the column inside the screen; original resolution " + (d.ResRestored ? "restored" : "NOT RESTORED") + " (" + d.ResOriginal + "): " + string.Join(" | ", parts.ToArray())
                + (d.ResNote.Length > 0 ? " | " + d.ResNote : "");
            return c;
        }

        /// <summary>Informational, listed prominently: every fallback that fired during the item (RtFallbacks). Pass = none fired.</summary>
        public static AtCheck UiFallbacks(AtItemData d)
        {
            AtCheck c = new AtCheck("uiFallbacks", d.Fallbacks.Count == 0, "", d.Fallbacks.Count);
            c.Informational = true;
            c.Detail = d.Fallbacks.Count == 0 ? "no fallback fired" : d.Fallbacks.Count + " fallback(s) fired: " + RtFallbacks.Join(d.Fallbacks);
            return c;
        }
    }
}
