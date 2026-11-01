using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace RealTracks
{
    /// <summary>The MonoBehaviour that drives everything per frame (created by AutoTest.Init, lives across scene loads).</summary>
    public sealed class AtBehaviour : MonoBehaviour
    {
        public static AtBehaviour Instance;

        private void Awake() { Instance = this; }

        /// <summary>Application.Quit after a delay (used when the resolution was just put back: Unity remembers the size it has when it quits).</summary>
        public static IEnumerator QuitAfter(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
            Main.Log("Real Tracks auto-test: quitting the game (Application.Quit, nothing is saved)");
            Application.Quit();
        }

        private void Update()
        {
            try { AtDriver.Tick(this); }
            catch (Exception e) { AtDriver.Crash(e); }
            try { AtMarkers.Tick(this); }
            catch (Exception e) { Main.Logger.Error("Real Tracks marker: " + e); AtMarkers.Reset(); }
        }

        private void OnGUI()
        {
            try { AtUi.DrawOverlay(); AtMarkers.DrawNoteBox(this); }
            catch (Exception e) { Main.Logger.Error("Real Tracks auto-test OnGUI: " + e.Message); }
        }

        private void OnDestroy()
        {
            try { AtDriver.Restore(); } catch { }
        }

        /// <summary>Screenshot at the end of this frame (PNG). done(true) when the file exists and is not empty.</summary>
        public void Capture(string path, Action<bool> done)
        {
            StartCoroutine(CaptureCo(path, done));
        }

        private IEnumerator CaptureCo(string path, Action<bool> done)
        {
            yield return new WaitForEndOfFrame();
            bool ok = false;
            try
            {
                int w = Screen.width, h = Screen.height;
                Texture2D t = new Texture2D(w, h, TextureFormat.RGB24, false);
                t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                t.Apply(false);
                byte[] png = t.EncodeToPNG();
                UnityEngine.Object.Destroy(t);
                AtIo.WriteBytes(path, png);
                ok = File.Exists(path) && new FileInfo(path).Length > 0;
            }
            catch (Exception e)
            {
                Main.Logger.Error("Real Tracks auto-test: screenshot " + path + " failed: " + e.Message);
            }
            if (done != null)
            {
                try { done(ok); } catch (Exception e) { Main.Logger.Error("Real Tracks auto-test: screenshot callback: " + e.Message); }
            }
        }
    }

    /// <summary>Reading screens (detector screensShowData), the status line and run screenshots.</summary>
    public static class AtUi
    {
        private static Type tmpType;
        private static PropertyInfo tmpTextProp;
        private static bool tmpTried;
        private static GUIStyle overlayStyle;

        private static void FindTmp()
        {
            if (tmpTried) return;
            tmpTried = true;
            try
            {
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = a.GetType("TMPro.TMP_Text", false);
                    if (t != null) { tmpType = t; tmpTextProp = t.GetProperty("text", BindingFlags.Public | BindingFlags.Instance); break; }
                }
            }
            catch { }
        }

        /// <summary>The text of every UI label that is on screen right now (UnityEngine.UI.Text and TextMeshPro, by reflection because TMP is in Assembly-CSharp-firstpass).</summary>
        public static List<string> CollectTexts()
        {
            List<string> list = new List<string>();
            try
            {
                UnityEngine.UI.Text[] ts = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Text>();
                for (int i = 0; i < ts.Length; i++)
                    if (ts[i] != null && ts[i].isActiveAndEnabled && !string.IsNullOrEmpty(ts[i].text)) list.Add(ts[i].text);
            }
            catch { }
            try
            {
                FindTmp();
                if (tmpType != null && tmpTextProp != null)
                {
                    UnityEngine.Object[] os = UnityEngine.Object.FindObjectsOfType(tmpType);
                    for (int i = 0; i < os.Length; i++)
                    {
                        Behaviour b = os[i] as Behaviour;
                        if (b == null || !b.isActiveAndEnabled) continue;
                        string s = tmpTextProp.GetValue(os[i], null) as string;
                        if (!string.IsNullOrEmpty(s)) list.Add(s);
                    }
                }
            }
            catch { }
            return list;
        }

        public static AtScreenScan Scan(string screen, string expected, string donor)
        {
            AtScreenScan s = new AtScreenScan();
            s.Screen = screen; s.Expected = expected;
            List<string> texts = CollectTexts();
            s.TextCount = texts.Count;
            string sample;
            s.MatchCount = AtRules.ScanTexts(texts, expected, out sample);
            s.Found = s.MatchCount > 0;
            s.Sample = sample;
            if (!string.IsNullOrEmpty(donor) && !string.Equals(donor, expected, StringComparison.OrdinalIgnoreCase))
            {
                string ds;
                if (AtRules.ScanTexts(texts, donor, out ds) > 0) s.DonorLeak = donor;
            }
            return s;
        }

        /// <summary>Run screenshot: screenshots/NN-itemK-what.png in the run folder. Returns the relative file name or "".</summary>
        public static string Shot(AtBehaviour host, string what)
        {
            AtRunCtx r = AtRun.Current;
            Settings s = Main.Settings;
            if (r == null || !r.ItemActive || !r.Req.Screenshots || !s.TestScreenshots) return "";
            r.ShotCount++;
            string rel = "screenshots/" + r.ShotCount.ToString("00") + "-item" + r.Index + "-" + AtRequest.Sanitize(what) + ".png";
            string path = Path.Combine(r.RunDir, rel.Replace('/', Path.DirectorySeparatorChar));
            AtItemData d = r.Cur;
            AtShot shot = new AtShot();
            shot.File = rel; shot.What = what;
            d.Shots.Add(shot);
            host.Capture(path, ok =>
            {
                AtRun.Event("screenshot", "file", rel, "what", what, "ok", ok);
                if (!ok) shot.What = what + " (FAILED)";
            });
            return rel;
        }

        public static void DrawOverlay()
        {
            Settings s = Main.Settings;
            AtRunCtx r = AtRun.Current;
            if (s == null || !s.TestOverlay || r == null) return;
            if (overlayStyle == null)
            {
                overlayStyle = new GUIStyle(GUI.skin.box);
                overlayStyle.alignment = TextAnchor.MiddleLeft;
                overlayStyle.fontSize = 12;
                overlayStyle.normal.textColor = Color.white;
            }
            string line = "Real Tracks auto-test: " + r.StatusLine() + "   sim " + AtSim.SimT.ToString("0") + " s, lap " + AtSim.LeaderLap;
            GUI.Box(new Rect(8, 8, Mathf.Min(Screen.width - 16, 760), 22), line, overlayStyle);
        }
    }

    /// <summary>Detector minimapOnOutline: is each car's minimap dot on the drawn track outline?</summary>
    public static class AtMinimap
    {
        private static AtAlphaGrid grid;
        private static MinimapWidget widget;
        private static int radius;
        private static bool tried;

        public static void Reset() { grid = null; widget = null; tried = false; }

        public static bool Ready { get { return grid != null; } }

        /// <summary>Copies the alpha of the minimap sprite that is on the HUD. Called once, a few seconds into the race.</summary>
        public static void Prepare(AtItemData d)
        {
            if (tried) return;
            tried = true;
            try
            {
                SessionHUD hud = UIManager.instance.GetScreen<SessionHUD>();
                widget = hud != null ? hud.minimapWidget : null;
                if (widget == null) { d.MapNote = "the HUD has no minimap widget"; return; }
                if (!widget.loaded) { d.MapNote = "the minimap widget did not load (no sprite for this circuit?)"; return; }
                Sprite sp = widget.trackLayout != null ? widget.trackLayout.sprite : null;
                if (sp == null || sp.texture == null) { d.MapNote = "the minimap has no sprite"; return; }
                Rect rc = sp.rect;
                int w = Mathf.Max(1, (int)rc.width), h = Mathf.Max(1, (int)rc.height);
                Texture src = sp.texture;
                RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                RenderTexture prev = RenderTexture.active;
                Texture2D t2 = null;
                try
                {
                    Graphics.Blit(src, rt);
                    RenderTexture.active = rt;
                    t2 = new Texture2D(w, h, TextureFormat.ARGB32, false);
                    t2.ReadPixels(new Rect(rc.x, rc.y, w, h), 0, 0);
                    t2.Apply(false);
                    Color32[] px = t2.GetPixels32();
                    byte[] a = new byte[px.Length];
                    int opaque = 0;
                    for (int i = 0; i < px.Length; i++) { a[i] = px[i].a; if (px[i].a >= 40) opaque++; }
                    if (opaque == 0) { d.MapNote = "the minimap sprite is empty"; return; }
                    grid = new AtAlphaGrid(w, h, a);
                    radius = Mathf.Max(3, Mathf.RoundToInt(Mathf.Max(w, h) * 0.012f));
                    d.MapAvailable = true;
                    d.MapNote = "sprite " + sp.name + " " + w + "x" + h + ", tolerance " + radius + " px";
                }
                finally
                {
                    RenderTexture.active = prev;
                    RenderTexture.ReleaseTemporary(rt);
                    if (t2 != null) UnityEngine.Object.Destroy(t2);
                }
            }
            catch (Exception e)
            {
                d.MapNote = "could not read the minimap sprite: " + e.GetType().Name + " " + e.Message;
            }
        }

        public static void Sample(AtItemData d)
        {
            if (grid == null || widget == null) return;
            Vector2 size = widget.minimap.sizeDelta;
            if (size.x < 2f || size.y < 2f) return;
            for (int i = 0; i < AtSim.Cars.Count; i++)
            {
                RacingVehicle v = AtSim.Cars[i].V;
                try
                {
                    if (v.pathState.pathStateGroup != PathStateManager.PathStateGroup.OnTrack) continue;
                    if (v.pathController.currentPathType != PathController.PathType.Track) continue;
                    if (v.behaviourManager.isOutOfRace || v.timer.hasSeenChequeredFlag) continue;
                    Vector2 q = widget.CalculateItemPosition(v.transform.position);
                    float u = q.x / size.x, vv = (q.y + size.y) / size.y;
                    d.MapSamples++;
                    if (grid.HitNear(u, vv, radius, 40)) d.MapHits++;
                }
                catch { }
            }
        }
    }
}
