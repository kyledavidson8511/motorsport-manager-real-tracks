using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace RealTracks
{
    /// <summary>File writes of the auto-test and the markers go through here. Nothing is ever written to the game's save folders.</summary>
    public static class AtIo
    {
        /// <summary>GUARD 4: true for any path in or under the game's save folders ("Saved Games", Application.persistentDataPath).</summary>
        public static bool IsForbidden(string path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            string full;
            try { full = Path.GetFullPath(path).Replace('\\', '/').ToLowerInvariant(); }
            catch { return true; }
            if (full.IndexOf("/saved games", StringComparison.Ordinal) >= 0) return true;
            try
            {
                string pd = Application.persistentDataPath;
                if (!string.IsNullOrEmpty(pd))
                {
                    string p = Path.GetFullPath(pd).Replace('\\', '/').ToLowerInvariant().TrimEnd('/');
                    if (full == p || full.StartsWith(p + "/", StringComparison.Ordinal)) return true;
                }
            }
            catch { }
            return false;
        }

        public static void Check(string path)
        {
            if (IsForbidden(path)) throw new IOException("Real Tracks auto-test refuses to write to " + path + " (game save area)");
        }

        public static void WriteText(string path, string text)
        {
            Check(path);
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        public static void WriteBytes(string path, byte[] bytes)
        {
            Check(path);
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, bytes);
        }

        public static StreamWriter OpenAppend(string path)
        {
            Check(path);
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            FileStream fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            StreamWriter w = new StreamWriter(fs, new UTF8Encoding(false));
            w.AutoFlush = false;   // flushed once a second by the driver (writes go to a cloud-synced folder; per-line flushes can stall frames)
            return w;
        }
    }

    /// <summary>One request being worked off.</summary>
    public sealed class AtRunCtx
    {
        public AtRequest Req;
        public string RunId = "", RunDir = "", RunningPath = "", DonePath = "", ReqDir = "";
        public DateTime StartedUtc;
        public float T0;
        public readonly List<AtItemData> Items = new List<AtItemData>();
        public int Index = -1;
        public bool ItemActive;
        public StreamWriter Events, Gates;
        public float SimScale;
        public string Phase = "";
        public bool Finished;
        public bool InLog;
        public int ShotCount;

        public AtItemData Cur { get { return Index >= 0 && Index < Items.Count ? Items[Index] : null; } }

        public string StatusLine()
        {
            AtItemData d = Cur;
            if (d == null) return "starting " + RunId;
            return "run " + RunId + " item " + (Index + 1) + "/" + Items.Count + " " + d.Location + " " + d.Layout + " [" + Phase + "]";
        }
    }

    public static class AtRun
    {
        public static AtRunCtx Current;
        public static string LastRunDir = "", LastResult = "";

        public static string RequestFolder
        {
            get
            {
                Settings s = Main.Settings;
                if (s != null && !string.IsNullOrEmpty(s.TestRequestFolder)) return s.TestRequestFolder;
                return Path.Combine(Main.ModPath ?? ".", "autotest");
            }
        }

        public static string LastRunText()
        {
            if (LastRunDir.Length == 0) return "no run yet in this session";
            return LastResult + "  " + LastRunDir;
        }

        public static void WriteSampleRequest()
        {
            string p = Path.Combine(RequestFolder, "request.json");
            if (File.Exists(p)) { Main.Log("Real Tracks auto-test: " + p + " already exists, sample not written"); return; }
            string json = "{\n  \"runId\": \"sample\",\n  \"items\": [\n    {\"location\": \"Austin\", \"layout\": \"A\", \"laps\": 5},\n    {\"location\": \"Phoenix\", \"layout\": \"B\", \"laps\": 3}\n  ],\n  \"simSpeed\": \"max\",\n  \"screenshots\": true,\n  \"markerSelfTest\": true,\n  \"quit\": true\n}\n";
            AtIo.WriteText(p, json);
            Main.Log("Real Tracks auto-test: wrote " + p);
        }

        // ------------------------------------------------------------------ events

        public static void Event(string type, params object[] kv)
        {
            AtRunCtx r = Current;
            if (r == null || r.Events == null) return;
            try
            {
                r.Events.WriteLine(AtLines.Event(Time.realtimeSinceStartup - r.T0, r.Index, AtSim.SimT, AtSim.LeaderLap, type, kv));
            }
            catch (Exception e) { Main.Logger.Error("Real Tracks auto-test: events.jsonl write failed: " + e.Message); }
        }

        public static void GateRow(string line)
        {
            AtRunCtx r = Current;
            if (r == null || r.Gates == null) return;
            try { r.Gates.WriteLine(line); }
            catch (Exception e) { Main.Logger.Error("Real Tracks auto-test: gate_speeds.csv write failed: " + e.Message); }
        }

        // ------------------------------------------------------------------ log capture (detector: exceptions)

        public static void OnLog(string condition, string stackTrace, LogType type)
        {
            AtRunCtx r = Current;
            if (r == null || !r.ItemActive || r.InLog) return;
            if (type == LogType.Log || type == LogType.Warning) return;
            r.InLog = true;
            try
            {
                AtItemData d = r.Cur;
                if (d == null) return;
                string tn = type == LogType.Exception ? "Exception" : (type == LogType.Assert ? "Assert" : "Error");
                string sig = AtRules.ErrSig(condition);
                d.ErrorCount++;
                if (type == LogType.Exception) d.ExceptionCount++;
                int n;
                d.ErrorSigs.TryGetValue(sig, out n);
                d.ErrorSigs[sig] = n + 1;
                if (d.Errors.Count < 20)
                {
                    AtErr e = new AtErr();
                    e.Type = tn; e.Sig = sig; e.Message = condition ?? ""; e.Stack = stackTrace ?? ""; e.T = Time.realtimeSinceStartup - r.T0;
                    d.Errors.Add(e);
                }
                if (n == 0 || n == 9 || n == 99)
                    Event("error", "kind", tn, "sig", sig, "count", n + 1, "message", Cut(condition, 400), "stack", Cut(stackTrace, 600));
            }
            catch { }
            finally { r.InLog = false; }
        }

        private static string Cut(string s, int n)
        {
            if (s == null) return "";
            return s.Length > n ? s.Substring(0, n) : s;
        }

        // ------------------------------------------------------------------ run lifecycle

        /// <summary>Picks up request.json if there is one. Returns the new run, or null.</summary>
        public static AtRunCtx TryPickUp()
        {
            string dir = RequestFolder;
            string reqPath = Path.Combine(dir, "request.json");
            if (!File.Exists(reqPath)) return null;
            string err;
            AtRequest req;
            string text;
            try { text = File.ReadAllText(reqPath); }
            catch (Exception e) { Main.Logger.Error("Real Tracks auto-test: cannot read " + reqPath + ": " + e.Message); return null; }
            req = AtRequest.Parse(text, out err);
            string running = Path.Combine(dir, "request.running.json");
            string done = Path.Combine(dir, "request.done.json");
            try
            {
                if (File.Exists(running)) File.Delete(running);
                File.Move(reqPath, running);
            }
            catch (Exception e) { Main.Logger.Error("Real Tracks auto-test: cannot rename " + reqPath + ": " + e.Message); return null; }
            if (req == null)
            {
                Main.Logger.Error("Real Tracks auto-test: request.json is invalid: " + err);
                try { if (File.Exists(done)) File.Delete(done); File.Move(running, done); } catch { }
                LastRunDir = dir; LastResult = "invalid request: " + err;
                return null;
            }

            AtRunCtx r = new AtRunCtx();
            r.Req = req;
            r.ReqDir = dir; r.RunningPath = running; r.DonePath = done;
            r.StartedUtc = DateTime.UtcNow;
            r.T0 = Time.realtimeSinceStartup;
            string stamp = r.StartedUtc.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            r.RunId = req.RunId.Length > 0 ? req.RunId : stamp;
            string loc = AtRequest.Sanitize(req.Items[0].Location);
            string baseDir = Path.Combine(Main.LogDir, loc.Length > 0 ? loc : "run");
            string runDir = Path.Combine(baseDir, r.RunId);
            if (Directory.Exists(runDir) && (File.Exists(Path.Combine(runDir, "events.jsonl")) || File.Exists(Path.Combine(runDir, "summary.json"))))
            {
                r.RunId = r.RunId + "-" + stamp;
                runDir = Path.Combine(baseDir, r.RunId);
            }
            r.RunDir = runDir;
            try
            {
                AtIo.Check(runDir);
                Directory.CreateDirectory(runDir);
                AtIo.WriteText(Path.Combine(runDir, "request.json"), text);
                r.Events = AtIo.OpenAppend(Path.Combine(runDir, "events.jsonl"));
                if (Main.Settings.TestDetGateSpeeds)
                {
                    r.Gates = AtIo.OpenAppend(Path.Combine(runDir, "gate_speeds.csv"));
                    r.Gates.WriteLine(AtLines.GateCsvHeader);
                }
            }
            catch (Exception e)
            {
                Main.Logger.Error("Real Tracks auto-test: cannot create the run folder " + runDir + ": " + e.Message);
                try { if (File.Exists(done)) File.Delete(done); File.Move(running, done); } catch { }
                LastRunDir = runDir; LastResult = "cannot create run folder: " + e.Message;
                return null;
            }

            float max = Mathf.Max(1, Main.Settings.TestMaxSimScale);
            r.SimScale = req.SimScale(max);
            if (req.Resolutions.Count > 0 || req.ResolutionErrors.Count > 0)
                Main.Log("RT RES: request lists " + req.Resolutions.Count + " resolution(s) [" + string.Join(", ", req.Resolutions.ConvertAll(delegate (AtResolution x) { return x.ToString(); }).ToArray()) + "]" +
                         (req.ResolutionErrors.Count > 0 ? "; ignored: " + string.Join("; ", req.ResolutionErrors.ToArray()) : "") + "; matrix " + (Main.Settings.TestResolutions ? "on" : "OFF in the settings"));
            for (int i = 0; i < req.Items.Count; i++)
            {
                AtItemData d = new AtItemData();
                d.Index = i; d.Location = req.Items[i].Location; d.Layout = req.Items[i].Layout; d.LapsTarget = req.Items[i].Laps; d.ForceCrashes = req.Items[i].ForceCrashes; d.SteerPitchAlign = req.Items[i].SteerPitchAlign; d.ClipRequests = req.Items[i].Clips; d.ArtTour = req.Items[i].ArtTour;
                d.Abort = "not run";
                r.Items.Add(d);
            }
            Current = r;
            LastRunDir = r.RunDir; LastResult = "running";
            Event("runStart", "runId", r.RunId, "modVersion", Main.Version, "items", req.Items.Count, "simScale", r.SimScale,
                "screenshots", req.Screenshots && Main.Settings.TestScreenshots, "markerSelfTest", req.MarkerSelfTest, "resolutions", string.Join(",", req.Resolutions.ConvertAll(delegate (AtResolution x) { return x.ToString(); }).ToArray()), "quit", req.Quit,
                "runDir", r.RunDir);
            Main.Log("Real Tracks auto-test: run " + r.RunId + " started, " + req.Items.Count + " item(s), logs in " + r.RunDir);
            WriteSummary(false);
            return r;
        }

        public static void BeginItem(AtRunCtx r, int index)
        {
            r.Index = index;
            AtItemData d = r.Items[index];
            d.Abort = "";
            d.MarkerRequested = r.Req.MarkerSelfTest && index == 0 && Main.Settings.TestDetMarkerSelf;
            d.FbMark = index == 0 ? 0 : RtFallbacks.Mark();       // item 0 also gets what fired while the game started (pack registration, venue art ...)
            if (index > 0) r.Items[index - 1].FbEnd = d.FbMark;
            d.ResRequested = index == 0 && r.Req.Resolutions.Count > 0;
            r.ItemActive = true;
            AtSim.ResetItem();
            Event("itemStart", "location", d.Location, "layout", d.Layout, "laps", d.LapsTarget);
            WriteSummary(false);
        }

        public static void EndItem(AtRunCtx r)
        {
            AtItemData d = r.Cur;
            if (d == null) return;
            r.ItemActive = false;
            AtSim.FinishItem(d);
            RefreshFallbacks(d);
            if (d.Fallbacks.Count > 0) Event("uiFallbacks", "count", d.Fallbacks.Count, "list", RtFallbacks.Join(d.Fallbacks));
            Event("itemEnd", "location", d.Location, "layout", d.Layout, "abort", d.Abort, "raceFinished", d.RaceFinished, "errors", d.ErrorCount, "cars", d.Cars.Count);
            WriteSummary(false);
        }

        public static void RefreshFallbacks(AtItemData d)
        {
            if (d.FbMark >= 0) d.Fallbacks = RtFallbacks.Between(d.FbMark, d.FbEnd >= 0 ? d.FbEnd : int.MaxValue);
        }

        public static AtRunSummary Build(AtRunCtx r, bool finished, bool quitClean)
        {
            foreach (AtItemData di in r.Items) RefreshFallbacks(di);
            return AtChecks.BuildSummary(r.RunId, Main.Version, r.StartedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                finished ? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) : "", quitClean, r.Items, AutoTest.Cfg());
        }

        public static void WriteSummary(bool finished)
        {
            AtRunCtx r = Current;
            if (r == null) return;
            WriteSummaryFor(r, finished, false);
        }

        private static void WriteSummaryFor(AtRunCtx r, bool finished, bool quitClean)
        {
            try
            {
                AtRunSummary s = Build(r, finished, quitClean);
                AtIo.WriteText(Path.Combine(r.RunDir, "summary.json"), s.ToJson());
            }
            catch (Exception e) { Main.Logger.Error("Real Tracks auto-test: summary.json failed: " + e); }
        }

        /// <summary>Ends the run: marks what did not run, writes the final summary, renames the request and optionally quits.</summary>
        public static void Finish(AtRunCtx r, string abortReason)
        {
            if (r == null || r.Finished) return;
            r.Finished = true;
            try
            {
                if (r.ItemActive) EndItem(r);
                for (int i = 0; i < r.Items.Count; i++)
                {
                    AtItemData d = r.Items[i];
                    if (d.Abort == "not run" && abortReason.Length > 0) d.Abort = "not run: " + abortReason;
                }
                bool clean = abortReason.Length == 0;
                AtRunSummary s = Build(r, true, clean);
                bool allPass = true;
                foreach (AtItemSummary it in s.Items) if (it.Result != "pass") allPass = false;
                Event("runEnd", "result", allPass ? "pass" : "fail", "abort", abortReason, "quit", r.Req.Quit);
                WriteSummaryFor(r, true, clean);
                LastRunDir = r.RunDir; LastResult = (allPass ? "PASS" : "FAIL") + (abortReason.Length > 0 ? " (" + abortReason + ")" : "");
                Main.Log("Real Tracks auto-test: run " + r.RunId + " finished: " + LastResult);
            }
            catch (Exception e) { Main.Logger.Error("Real Tracks auto-test: finishing the run failed: " + e); }
            try { if (r.Events != null) { r.Events.Flush(); r.Events.Close(); r.Events = null; } } catch { }
            try { if (r.Gates != null) { r.Gates.Flush(); r.Gates.Close(); r.Gates = null; } } catch { }
            try
            {
                if (File.Exists(r.DonePath)) File.Delete(r.DonePath);
                if (File.Exists(r.RunningPath)) File.Move(r.RunningPath, r.DonePath);
            }
            catch (Exception e) { Main.Logger.Error("Real Tracks auto-test: renaming the request failed: " + e.Message); }
            AtSim.EndRun();
            Current = null;
        }
    }
}
