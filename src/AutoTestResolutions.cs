using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RealTracks
{
    public partial class Settings
    {
        /// <summary>Run option "resolutions": switch the resolution at the first item's Quick Race setup screen, photograph it, restore the original.</summary>
        public bool TestResolutions = true;
        /// <summary>Art tour: plan from the pack manifest's "views" (off = always the generic default set).</summary>
        public bool TestArtTourManifestViews = true;
        /// <summary>The informational "uiFallbacks" check (every fallback that fired during the item).</summary>
        public bool TestDetUiFallbacks = true;
    }

    /// <summary>
    /// The auto-test resolution matrix (request run option "resolutions": ["3840x1080","1920x1080","1920x1200"]). At the Quick Race setup screen of the first item,
    /// for each resolution: apply it the way the game's own video preference does, wait for the UI to settle, lay the custom-tracks column out again, take a screenshot
    /// (screenshots/NN-item0-setup-WxH.png), record the column's mode / scale / fit / inside-screen, and finally put the original resolution back.
    ///
    /// How the game changes resolution (decompiled): the Preferences dropdown calls VideoPreferences.SetVideoResolution(w, h, currentFullscreenMode) (UIPreferencesVideoSettings.cs:296-299),
    /// which sets Screen.fullScreen and starts the coroutine WaitSetResolution (VideoPreferences.cs:427-439): two WaitForEndOfFrame, Display.main.SetRenderingResolution(w, h),
    /// Screen.SetResolution(w, h, fullscreen). This class runs the same two calls in the same order (after the same two end-of-frame waits) with the fullscreen flag left as it is.
    /// The game offers only modes of Screen.resolutions (VideoPreferences.cs:96, filter IsValidResolution :111), so a fullscreen request for a size the display does not list is
    /// skipped here (a mode switch to an unsupported size can leave a black screen); windowed requests larger than the desktop are skipped too. Nothing is saved to the game's
    /// preferences: the game only stores Video_Resolution when SetCurrentResolution(.., inSave: true) runs (VideoPreferences.cs:192-210), i.e. from the menu; Unity's own remembered
    /// size (PlayerPrefs "Screenmanager Resolution ...") follows the real size at quit, which is why the original is restored before the run ends.
    ///
    /// The UI does not lay itself out again: the root canvases use a CanvasScaler (UIManager.cs:272 sets matchWidthOrHeight 0, UIScreen.cs:281 0.5) so they rescale with the screen, and
    /// the stock panels follow their anchors, but the custom column's rectangle and the content block's scale were fixed numbers measured at the old size, and
    /// QuickRaceSetupScreen.CreateEntries returns at once when the list already has items (QuickRaceSetupScreen.cs:101-104), so nothing builds the column again by itself.
    /// UiQuickRace.Rebuild therefore runs after the change.
    /// Safety: every step is in a try/catch; on any failure, timeout, abort, crash or the end of the run the original resolution is put back (EnsureRestored).
    /// </summary>
    public static class AtResMatrix
    {
        private enum S { Idle, Next, ApplyWait, Settle, Rebuilt, Shot, Restore, RestoreWait, RestoreSettle, Done }
        private static S st = S.Idle;
        private static bool done;
        private static List<AtResolution> list = new List<AtResolution>();
        private static int idx, frames;
        private static AtResResult cur;
        private static int origW, origH;
        private static bool origFs, changed, everChanged;
        private static float t0, stepT0;

        public static bool Active { get { return st != S.Idle && st != S.Done; } }
        public static bool Changed { get { return changed; } }

        /// <summary>Forget everything (new run).</summary>
        public static void Reset() { st = S.Idle; done = false; changed = false; everChanged = false; list = new List<AtResolution>(); cur = null; }

        public static bool Wanted(AtRunCtx r, AtItemData d)
        {
            if (done || st != S.Idle || r.Index != 0 || !d.ResRequested) return false;
            if (!Main.Settings.TestResolutions)
            {
                done = true;
                d.ResNote = "requested, but the TestResolutions setting is off";
                Main.Log("RT RES: the request lists resolutions but TestResolutions is off in the settings: matrix skipped");
                return false;
            }
            return true;
        }

        public static void Begin(AtBehaviour host, AtRunCtx r, AtItemData d)
        {
            list = r.Req.Resolutions; idx = -1; cur = null;
            origW = Screen.width; origH = Screen.height; origFs = Screen.fullScreen;
            d.ResOriginal = origW + "x" + origH + (origFs ? " fullscreen" : " windowed");
            t0 = Time.realtimeSinceStartup;
            Main.Log("RT RES: matrix start at the Quick Race setup screen: original " + d.ResOriginal + ", " + list.Count + " resolution(s) to try");
            AtRun.Event("resolutionMatrixStart", "original", d.ResOriginal, "count", list.Count);
            Go(S.Next);
        }

        private static void Go(S s) { st = s; frames = 0; stepT0 = Time.realtimeSinceStartup; }

        /// <summary>One frame of the matrix. Never throws: a failure restores the resolution and ends the matrix.</summary>
        public static void Step(AtBehaviour host, AtRunCtx r, AtItemData d)
        {
            try { Run(host, r, d); }
            catch (Exception e)
            {
                Main.Logger.Error("Real Tracks: resolution matrix failed: " + e);
                RtFallbacks.Note("resolution matrix", "failed and was stopped (" + e.GetType().Name + " " + e.Message + "); the original resolution is restored");
                d.ResNote = ("matrix failed: " + e.GetType().Name + " " + e.Message).Trim();
                if (cur != null && !d.ResResults.Contains(cur)) d.ResResults.Add(cur);
                EnsureRestored("matrix exception");
                d.ResRestored = !changed;
                st = S.Done; done = true;
            }
        }

        private static void Run(AtBehaviour host, AtRunCtx r, AtItemData d)
        {
            float now = Time.realtimeSinceStartup;
            frames++;
            if (st != S.Restore && st != S.RestoreWait && st != S.RestoreSettle && now - t0 > 40f + 30f * list.Count)
            {
                Main.Log("RT RES: matrix timeout after " + (now - t0).ToString("0") + " s: stopping and restoring");
                d.ResNote = "matrix timed out";
                if (cur != null && cur.Skipped.Length == 0 && cur.Shot.Length == 0) cur.Skipped = "timed out";
                Go(S.Restore);
                return;
            }
            QuickRaceSetupScreen scr = UIManager.instance.currentScreen as QuickRaceSetupScreen;
            switch (st)
            {
                case S.Next:
                {
                    idx++;
                    if (idx >= list.Count) { Go(S.Restore); return; }
                    AtResolution want = list[idx];
                    cur = new AtResResult(); cur.Resolution = want.ToString(); cur.ReqW = want.W; cur.ReqH = want.H;
                    d.ResResults.Add(cur);
                    string why = Unsupported(want.W, want.H, origFs);
                    if (why != null) { cur.Skipped = why; Main.Log("RT RES: " + cur.Resolution + " skipped: " + why); Report(cur); Go(S.Next); return; }
                    if (Screen.width == want.W && Screen.height == want.H) { cur.Applied = true; Main.Log("RT RES: " + cur.Resolution + " is the current size: no change needed"); Go(S.Settle); return; }
                    Main.Log("RT RES: " + cur.Resolution + ": SetResolution(" + want.W + ", " + want.H + ", fullscreen " + origFs + ") from " + Screen.width + "x" + Screen.height);
                    changed = true; everChanged = true;
                    host.StartCoroutine(ApplyCo(want.W, want.H, origFs));
                    Go(S.ApplyWait);
                    return;
                }
                case S.ApplyWait:
                    if (frames > 3 && Screen.width == cur.ReqW && Screen.height == cur.ReqH) { cur.Applied = true; Main.Log("RT RES: now " + Screen.width + "x" + Screen.height + " (fullscreen " + Screen.fullScreen + ") after " + (now - stepT0).ToString("0.0") + " s"); Go(S.Settle); }
                    else if (now - stepT0 > 10f)
                    {
                        cur.ActualW = Screen.width; cur.ActualH = Screen.height;
                        cur.Skipped = "not applied: the screen stayed " + Screen.width + "x" + Screen.height + " 10 s after SetResolution";
                        Main.Log("RT RES: " + cur.Resolution + " " + cur.Skipped);
                        RtFallbacks.Note("resolution matrix", cur.Resolution + " " + cur.Skipped);
                        Report(cur); Go(S.Next);
                    }
                    return;
                case S.Settle:
                    // CanvasScaler.Update reacts to the new screen size on the next frames; give the layout a moment
                    if (frames < 20 || now - stepT0 < 0.6f) return;
                    cur.ActualW = Screen.width; cur.ActualH = Screen.height;
                    if (scr == null) { cur.Note = "the Quick Race setup screen is not current"; Go(S.Shot); return; }
                    {
                        UiColumnInfo before = UiQuickRace.Measure(scr);
                        bool col = before.Mode == "left" || before.Mode == "scaled";
                        cur.StaleInside = !col || before.InsideScreen;
                        if (UiQuickRace.RebuildOnResize && col)
                        {
                            UiQuickRace.Rebuild(scr, "resolution matrix " + cur.Resolution);
                            cur.Rebuilt = true;
                        }
                        else if (!UiQuickRace.RebuildOnResize) cur.Note = "UiColumnRebuildOnResize is off: the column keeps the layout of the previous size";
                    }
                    Go(S.Rebuilt);
                    return;
                case S.Rebuilt:
                    if (frames < 8) return;
                    Fill(cur, scr);
                    Go(S.Shot);
                    return;
                case S.Shot:
                    if (frames == 1) { cur.Shot = AtUi.Shot(host, "setup-" + cur.ReqW + "x" + cur.ReqH); return; }
                    if (frames < 5) return;
                    Report(cur);
                    Go(S.Next);
                    return;
                case S.Restore:
                    if (!changed) { FinishRestore(d, scr); return; }
                    Main.Log("RT RES: restoring " + origW + "x" + origH + " (fullscreen " + origFs + ") from " + Screen.width + "x" + Screen.height);
                    if (Screen.width == origW && Screen.height == origH) { changed = false; Go(S.RestoreSettle); return; }
                    host.StartCoroutine(ApplyCo(origW, origH, origFs));
                    Go(S.RestoreWait);
                    return;
                case S.RestoreWait:
                    if (frames > 3 && Screen.width == origW && Screen.height == origH) { changed = false; Go(S.RestoreSettle); }
                    else if (now - stepT0 > 12f)
                    {
                        Main.Log("RT RES: the original resolution did not come back by itself (screen is " + Screen.width + "x" + Screen.height + "): forcing SetResolution once more");
                        EnsureRestored("restore timeout");
                        Go(S.RestoreSettle);
                    }
                    return;
                case S.RestoreSettle:
                    if (frames < 20 || now - stepT0 < 0.6f) return;
                    FinishRestore(d, scr);
                    return;
            }
        }

        private static void FinishRestore(AtItemData d, QuickRaceSetupScreen scr)
        {
            bool back = Screen.width == origW && Screen.height == origH;
            d.ResRestored = back;
            if (!back) RtFallbacks.Note("resolution matrix", "the original resolution " + origW + "x" + origH + " was NOT restored (screen is " + Screen.width + "x" + Screen.height + ")");
            if (scr != null && everChanged && UiQuickRace.RebuildOnResize)
            {
                UiColumnInfo m = UiQuickRace.Measure(scr);
                if (m.Mode == "left" || m.Mode == "scaled") UiQuickRace.Rebuild(scr, "original resolution back");
            }
            Main.Log("RT RES: matrix done: original " + d.ResOriginal + (back ? " restored" : " NOT RESTORED") + "; " + d.ResResults.Count + " resolution(s): " + string.Join(" | ", d.ResResults.ConvertAll(delegate (AtResResult x) { return x.ToString(); }).ToArray()));
            AtRun.Event("resolutionRestored", "original", d.ResOriginal, "restored", back, "screen", Screen.width + "x" + Screen.height);
            st = S.Done; done = true;
        }

        /// <summary>Puts the original resolution back at once if the matrix changed it (abort, crash, end of run). Returns true when it had to.</summary>
        public static bool EnsureRestored(string why)
        {
            if (!changed) return false;
            try
            {
                Main.Log("RT RES: " + why + ": putting " + origW + "x" + origH + " (fullscreen " + origFs + ") back");
                try { Display.main.SetRenderingResolution(origW, origH); } catch (Exception) { }
                Screen.SetResolution(origW, origH, origFs);
                changed = false;
                return true;
            }
            catch (Exception e) { Main.Logger.Error("Real Tracks: could not restore the resolution: " + e); return false; }
        }

        private static void Fill(AtResResult r, QuickRaceSetupScreen scr)
        {
            UiColumnInfo m = UiQuickRace.Measure(scr);
            r.ColumnMode = m.Mode; r.Scale = m.Scale; r.Fits = m.Fits; r.InsideScreen = m.InsideScreen; r.Entries = m.Entries; r.CanvasW = m.CanvasW; r.CanvasH = m.CanvasH;
            if (m.Note.Length > 0 && r.Note.IndexOf(m.Note) < 0) r.Note = (r.Note + " " + m.Note).Trim();
        }

        private static void Report(AtResResult r)
        {
            Main.Log("RT RES: " + r);
            AtRun.Event("resolutionShot", "resolution", r.Resolution, "actual", r.ActualW + "x" + r.ActualH, "applied", r.Applied, "skipped", r.Skipped, "rebuilt", r.Rebuilt,
                "columnMode", r.ColumnMode, "scale", Math.Round(r.Scale, 3), "fits", r.Fits, "insideScreen", r.InsideScreen, "staleInside", r.StaleInside, "entries", r.Entries,
                "canvas", Math.Round(r.CanvasW) + "x" + Math.Round(r.CanvasH), "file", r.Shot, "note", r.Note);
        }

        /// <summary>The same calls as VideoPreferences.WaitSetResolution (VideoPreferences.cs:433-439).</summary>
        private static IEnumerator ApplyCo(int w, int h, bool fs)
        {
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();
            try { Display.main.SetRenderingResolution(w, h); } catch (Exception e) { Main.Log("RT RES: Display.main.SetRenderingResolution failed: " + e.Message); }
            try { Screen.SetResolution(w, h, fs); } catch (Exception e) { Main.Logger.Error("Real Tracks: Screen.SetResolution failed: " + e); }
        }

        /// <summary>null if the size may be tried; otherwise why it is skipped.</summary>
        public static string Unsupported(int w, int h, bool fullscreen)
        {
            try
            {
                Resolution[] rs = Screen.resolutions;
                if (fullscreen)
                {
                    if (rs == null || rs.Length == 0) return null;
                    int bw = 0, bh = 0;
                    foreach (Resolution x in rs) { if (x.width == w && x.height == h) return null; if (x.width * x.height > bw * bh) { bw = x.width; bh = x.height; } }
                    return "the display does not offer " + w + "x" + h + " in fullscreen (Screen.resolutions lists " + rs.Length + " modes, largest " + bw + "x" + bh + "); not tried so the screen is never left on an unsupported mode";
                }
                Resolution cur = Screen.currentResolution;
                if (cur.width > 0 && (w > cur.width || h > cur.height)) return "a " + w + "x" + h + " window does not fit the " + cur.width + "x" + cur.height + " desktop";
                return null;
            }
            catch (Exception) { return null; }
        }
    }
}
