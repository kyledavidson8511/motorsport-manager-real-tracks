using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using RealTracks.ArtGeo;

namespace RealTracks
{
    public partial class Settings
    {
        /// <summary>Request flag "artTour": close-up screenshots of the generated venue art at the pre-race hub (pack venues only).</summary>
        public bool TestArtTour = true;
    }

    /// <summary>
    /// The auto-test art tour (request flag "artTour", per run or per item). At a pack venue, during the pre-race hub (the 3D venue is loaded, the
    /// hub's orbit camera is turning and nothing is timed: AtDriver.DoHub waits here before pressing Start), a temporary Unity camera is placed at each
    /// viewpoint planned by TourPlan (ArtTour.cs, from what the art generator recorded in ArtRegistry) and rendered into a RenderTexture, which is saved
    /// as screenshots/NN-itemK-art-NAME.png.
    ///
    /// Why a dedicated camera and not the game's: the hub camera is a donor-scene camera whose transform is copied onto the real game camera every frame by
    /// AnimatedCamera.Update (AnimatedCamera.cs, CameraManager.SetCameraMode, GameCamera.mainCamera) and moved by VenueCameraRig.Update, so moving it would
    /// be overwritten next frame and would then have to be put back. Our own Camera (CopyFrom the game camera: clear flags, background, culling mask, clip
    /// planes, skybox) is never part of the game's camera system, nothing of the game's camera state is touched, and the UI and tilt-shift/blur effects (image
    /// effects are components, not copied) stay out of the art shots. RenderSettings (fog, ambient, skybox) are global, so the horizon looks as a player sees it.
    /// One view is rendered per frame so the game keeps ticking; the run continues exactly as before afterwards.
    /// </summary>
    public static class AtArtTour
    {
        private enum St { Idle, Running, Done }
        private static St st = St.Idle;
        private static List<TourView> views;
        private static int idx;
        private static Camera cam;
        private static RenderTexture rt;
        private static Texture2D tex;
        private static float t0, farBase;
        private static int width, height, shots;
        private static readonly List<string> files = new List<string>(), skipped = new List<string>();

        public static void Reset()
        {
            try
            {
                if (cam != null) UnityEngine.Object.Destroy(cam.gameObject);
                if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
            catch (Exception) { }
            cam = null; rt = null; tex = null; views = null;
            st = St.Idle; idx = 0; shots = 0;
            files.Clear(); skipped.Clear();
        }

        /// <summary>True while the item still owes a tour (DoHub holds the Start button until it is done).</summary>
        public static bool Wanted(AtItemData d) { return d != null && d.ArtTour && st != St.Done; }

        /// <summary>One step per frame at the hub.</summary>
        public static void Step(AtBehaviour host, AtItemData d)
        {
            try
            {
                if (st == St.Idle) Begin(d);
                if (st == St.Running) RenderNext(d);
            }
            catch (Exception e)
            {
                Main.Logger.Error("RT ART TOUR: failed: " + e);
                skipped.Add("tour: error " + e.GetType().Name + ": " + e.Message);
                End(d);
            }
        }

        private static void Begin(AtItemData d)
        {
            t0 = Time.realtimeSinceStartup;
            Settings s = Main.Settings;
            AtRunCtx r = AtRun.Current;
            string why = null;
            if (!d.IsPack) why = "stock venue: the art tour only runs at pack venues";
            else if (!s.TestArtTour) why = "the TestArtTour setting is off";
            else if (r == null || !r.Req.Screenshots || !s.TestScreenshots) why = "screenshots are off";
            PackLayout pl = ActiveVenue.CurrentLayout;
            if (why == null && (pl == null || !ArtRegistry.Matches(pl.Pack.Id, pl.Key))) why = "the art generator recorded nothing for this venue (art disabled or its build failed)";
            Camera main = null;
            try { main = App.instance.cameraManager.gameCamera.GetCamera(); } catch (Exception) { }
            if (why == null && main == null) why = "no game camera to copy settings from";
            if (why != null) { skipped.Add("all: " + why); Main.Log("RT ART TOUR: skipped: " + why); End(d); return; }

            width = Mathf.Clamp(Screen.width, 640, 1920); height = Mathf.RoundToInt(width * 9f / 16f);   // 16:9, the framing the plan assumes
            int aa = QualitySettings.antiAliasing; aa = aa >= 8 ? 8 : (aa >= 4 ? 4 : (aa >= 2 ? 2 : 1));
            rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            rt.antiAliasing = aa;
            tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            GameObject go = new GameObject("RT ArtTour Camera");
            cam = go.AddComponent<Camera>();
            cam.CopyFrom(main);
            cam.enabled = false;                      // rendered by hand only
            cam.rect = new Rect(0, 0, 1, 1);
            cam.depth = -50f;
            cam.targetTexture = rt;
            cam.useOcclusionCulling = false;
            Skybox sb = main.GetComponent<Skybox>();
            if (sb != null && sb.material != null) go.AddComponent<Skybox>().material = sb.material;
            farBase = main.farClipPlane;
            TourScene sc = ArtRegistry.Scene;
            List<RealTracks.ArtGeo.ViewSpec> keepViews = sc.Views;
            bool useManifest = s.TestArtTourManifestViews && keepViews != null && keepViews.Count > 0;
            if (!useManifest) sc.Views = null;                      // the planner then uses the generic default set
            try { views = TourPlan.Compute(sc, (float)width / height); }
            finally { sc.Views = keepViews; }
            string viewSet = useManifest ? "view set: the pack manifest's " + keepViews.Count + " entries"
                : "view set: generic default (" + (!s.TestArtTourManifestViews ? "TestArtTourManifestViews is off" : "the pack manifest has no views") + ")";
            idx = 0;
            Main.Log("RT ART TOUR: " + views.Count + " viewpoints planned from " + sc.Marks.Count + " recorded objects; " + width + "x" + height + ", aa " + aa + ", game camera clear " + main.clearFlags + ", fov " + main.fieldOfView.ToString("0") + ", far " + farBase.ToString("0") +
                     ", culling mask " + main.cullingMask + ", fog " + (RenderSettings.fog ? "on" : "off") + ", skybox " + (sb != null && sb.material != null ? sb.material.name : (RenderSettings.skybox != null ? "RenderSettings:" + RenderSettings.skybox.name : "none")) + "; " + viewSet);
            st = St.Running;
        }

        private static void RenderNext(AtItemData d)
        {
            AtRunCtx r = AtRun.Current;
            if (Time.realtimeSinceStartup - t0 > 120f)
            {
                while (idx < views.Count) skipped.Add(views[idx++].Name + ": the tour took more than 120 s");
                End(d);
                return;
            }
            if (idx >= views.Count) { End(d); return; }
            TourView v = views[idx++];
            if (!v.Ok)
            {
                skipped.Add(v.Name + ": " + v.Skipped);
                Main.Log("RT ART TOUR: " + v.Name + " skipped: " + v.Skipped);
                return;
            }
            cam.transform.position = new Vector3(v.Pos.X, v.Pos.Y, v.Pos.Z);
            Vector3 look = new Vector3(v.Target.X - v.Pos.X, v.Target.Y - v.Pos.Y, v.Target.Z - v.Pos.Z);
            cam.transform.rotation = Quaternion.LookRotation(look, Vector3.up);
            cam.fieldOfView = v.Fov;
            cam.farClipPlane = Mathf.Max(farBase, (Main.Settings.ArtHorizonFarClip && Main.Settings.ArtFixHorizonFade) ? v.FarClip : 0f);
            cam.Render();
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply(false);
            RenderTexture.active = prev;
            byte[] png = tex.EncodeToPNG();

            r.ShotCount++;
            string rel = "screenshots/" + r.ShotCount.ToString("00") + "-item" + r.Index + "-" + AtRequest.Sanitize("art-" + v.Name) + ".png";
            string path = Path.Combine(r.RunDir, rel.Replace('/', Path.DirectorySeparatorChar));
            AtIo.WriteBytes(path, png);
            bool ok = File.Exists(path) && new FileInfo(path).Length > 0;
            AtShot shot = new AtShot(); shot.File = rel; shot.What = "art-" + v.Name + (ok ? "" : " (FAILED)");
            d.Shots.Add(shot);
            if (ok) { shots++; files.Add(rel); } else skipped.Add(v.Name + ": the PNG could not be written");
            Main.Log("RT ART TOUR: " + TourPlan.Describe(v) + " far " + cam.farClipPlane.ToString("0") + "; framed " + v.Subject + "; " + v.InView + " art objects in view; " + rel + (ok ? "" : " FAILED"));
            AtRun.Event("artShot", "name", v.Name, "file", rel, "ok", ok,
                "camX", Math.Round(v.Pos.X, 1), "camY", Math.Round(v.Pos.Y, 1), "camZ", Math.Round(v.Pos.Z, 1),
                "targetX", Math.Round(v.Target.X, 1), "targetY", Math.Round(v.Target.Y, 1), "targetZ", Math.Round(v.Target.Z, 1),
                "fov", v.Fov, "yaw", Math.Round(TourPlan.Yaw(v.Pos, v.Target), 1), "pitchDown", Math.Round(TourPlan.PitchDown(v.Pos, v.Target), 1),
                "framed", v.Subject, "ids", v.Ids, "objectsInView", v.InView);
        }

        private static void End(AtItemData d)
        {
            double secs = Math.Round(Time.realtimeSinceStartup - t0, 1);
            d.ArtShots.Clear(); d.ArtShots.AddRange(files);
            d.ArtSkipped.Clear(); d.ArtSkipped.AddRange(skipped);
            d.ArtTourRan = true;
            AtRun.Event("artTourEnd", "shots", shots, "skipped", skipped.Count, "files", string.Join(", ", files.ToArray()), "skippedList", string.Join("; ", skipped.ToArray()), "seconds", secs);
            Main.Log("RT ART TOUR: done, " + shots + " shots, " + skipped.Count + " skipped" + (skipped.Count > 0 ? " (" + string.Join("; ", skipped.ToArray()) + ")" : "") + ", " + secs.ToString("0.0") + " s; temporary camera removed, the game's cameras were not touched");
            try
            {
                if (cam != null) UnityEngine.Object.Destroy(cam.gameObject);
                if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
                if (tex != null) UnityEngine.Object.Destroy(tex);
            }
            catch (Exception) { }
            cam = null; rt = null; tex = null;
            st = St.Done;
        }
    }
}
