using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace RealTracks
{
    /// <summary>
    /// Race phases for smoothness + video (Kyle's M1 feedback, 2026-10-28):
    ///   Racing (top test speed) -> from the start of lap 2: Smooth (1x, real time, no capture; frame times only)
    ///   -> Video (adaptive slow motion; every frame captured at reduced size with its race time, for an MP4 at true speed)
    ///   -> Done (top test speed again).
    /// Capture cost never shows up in the smoothness numbers: the two windows don't overlap.
    /// </summary>
    public static partial class AtPhases
    {
        public enum Phase { Racing, Smooth, Video, Done, Clip }   // Clip: a request-driven turn clip is being captured (AutoTestClipRun.cs)
        public static Phase Current = Phase.Racing;
        private static double smoothUnpausedReal, smoothStartSim;
        private static double videoStartSim, lastFrameSim;
        private static float videoSpeed = 0.1f;
        private static bool frameBusy;
        private static int frameNo;
        private static readonly StringBuilder index = new StringBuilder();
        private static string videoDirAbs = "";

        public static void ResetForItem()
        {
            Current = Phase.Racing;
            smoothUnpausedReal = 0;
            videoStartSim = lastFrameSim = 0;
            videoSpeed = 0.1f;
            frameBusy = false;
            frameNo = 0;
            index.Length = 0;
            videoDirAbs = "";
            ResetClips();
        }

        /// <summary>The sim speed the driver should apply, or -1 for "use the normal test speed".</summary>
        public static float SpeedOverride()
        {
            if (Current == Phase.Smooth) return 1f;
            if (Current == Phase.Video || Current == Phase.Clip) return videoSpeed;
            return -1f;
        }

        public static void Tick(AtBehaviour host, AtItemData d, Settings s, bool paused)
        {
            switch (Current)
            {
                case Phase.Racing:
                    if (s.TestSmoothSeconds <= 0 && s.TestVideoSeconds <= 0) { Current = Phase.Done; break; }
                    if (AtSim.LeaderLap >= 2 || d.LapsTarget <= 1)   // the start of lap 2 (LeaderLap is the leader's current lap, 1-based): the field has spread out, as in a real race
                    {
                        Current = s.TestSmoothSeconds > 0 ? Phase.Smooth : Phase.Video;
                        if (Current == Phase.Smooth) BeginSmooth(d);
                        else BeginVideo(d);
                    }
                    break;
                case Phase.Smooth:
                    if (paused) break;
                    double dt = Time.unscaledDeltaTime;
                    d.Smooth.Add(dt);
                    d.Fps.Add(dt);
                    smoothUnpausedReal += dt;
                    if (smoothUnpausedReal >= s.TestSmoothSeconds)
                    {
                        double ratio = smoothUnpausedReal > 0 ? (AtSim.SimT - smoothStartSim) / smoothUnpausedReal : 0;
                        d.Smooth.Diag += (d.Smooth.Diag.Length > 0 ? ", " : "") + "game speed setting 1x; race clock ran at " + ratio.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "x real time (the game slows its clock when frames are long)";
                        AtSmoothOut o = d.Smooth.Result();
                        AtRun.Event("smoothEnd", "frames", o.Frames, "seconds", Math.Round(o.Seconds, 2), "p95Ms", Math.Round(o.P95Ms, 1), "hitches", o.Hitches, "stutters", o.Stutters);
                        if (s.TestVideoSeconds > 0) BeginVideo(d); else Current = Phase.Done;
                    }
                    break;
                case Phase.Video:
                    if (paused || frameBusy) break;
                    double simT = AtSim.SimT;
                    if (simT - videoStartSim >= s.TestVideoSeconds || frameNo >= 2000)
                    {
                        EndVideo(d);
                        break;
                    }
                    frameBusy = true;
                    host.StartCoroutine(FrameCo(d, s, simT));
                    break;
                case Phase.Done:
                case Phase.Clip:
                    ClipTick(host, d, s, paused);   // no-op unless the request asked for clips
                    break;
            }
        }

        private static void BeginSmooth(AtItemData d)
        {
            string diag = "";
            try
            {
                diag = "screen " + Screen.width + "x" + Screen.height + ", vSync " + QualitySettings.vSyncCount + ", targetFrameRate " + Application.targetFrameRate
                    + ", quality '" + QualitySettings.names[QualitySettings.GetQualityLevel()] + "', runInBackground " + Application.runInBackground;
            }
            catch (Exception) { }
            d.Smooth.Diag = diag;
            smoothStartSim = AtSim.SimT;
            AtRun.Event("smoothStart", "lap", AtSim.LeaderLap, "simT", Math.Round(AtSim.SimT, 1), "diag", diag);
        }

        private static void BeginVideo(AtItemData d)
        {
            Current = Phase.Video;
            AtRunCtx r = AtRun.Current;
            if (r == null) { Current = Phase.Done; return; }
            string rel = "video/item" + d.Index;
            videoDirAbs = Path.Combine(r.RunDir, rel.Replace('/', Path.DirectorySeparatorChar));
            try { AtIo.Check(videoDirAbs); Directory.CreateDirectory(videoDirAbs); }
            catch (Exception e) { Main.Logger.Error("Real Tracks auto-test video: " + e.Message); Current = Phase.Done; return; }
            d.VideoDir = rel;
            videoStartSim = lastFrameSim = AtSim.SimT;
            videoSpeed = 0.1f;
            index.Length = 0;
            index.Append("frame,simT\n");
            AtRun.Event("videoStart", "lap", AtSim.LeaderLap, "simT", Math.Round(videoStartSim, 2), "dir", rel);
        }

        private static void EndVideo(AtItemData d)
        {
            Current = Phase.Done;
            d.VideoFrames = frameNo;
            try { AtIo.WriteBytes(Path.Combine(videoDirAbs, "frames.csv"), Encoding.UTF8.GetBytes(index.ToString())); }
            catch (Exception e) { Main.Logger.Error("Real Tracks auto-test video index: " + e.Message); }
            AtRun.Event("videoEnd", "frames", frameNo, "simSeconds", Math.Round(AtSim.SimT - videoStartSim, 2), "dir", d.VideoDir);
        }

        /// <summary>
        /// End-of-frame capture, scaled down by an integer factor (3840x1080 -> 1280x360), JPEG. Then adapt the slow motion so the race
        /// advances about 1/30 s per captured frame (the MP4 is timed by race time, so it plays at true speed whatever the capture cost).
        /// </summary>
        private static IEnumerator FrameCo(AtItemData d, Settings s, double simAtRequest)
        {
            yield return new WaitForEndOfFrame();
            try
            {
                int w = Screen.width, h = Screen.height;
                int f = Math.Max(1, (int)Math.Ceiling(w / (double)Math.Max(320, s.TestVideoWidth)));
                int ow = w / f, oh = h / f;
                Texture2D full = new Texture2D(w, h, TextureFormat.RGB24, false);
                full.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                full.Apply(false);
                Color32[] src = full.GetPixels32();
                UnityEngine.Object.Destroy(full);
                Color32[] dst = new Color32[ow * oh];
                for (int y = 0; y < oh; y++)
                {
                    int sy = y * f * w;
                    int dy = y * ow;
                    for (int x = 0; x < ow; x++) dst[dy + x] = src[sy + x * f];
                }
                Texture2D small = new Texture2D(ow, oh, TextureFormat.RGB24, false);
                small.SetPixels32(dst);
                small.Apply(false);
                byte[] jpg = small.EncodeToJPG(Mathf.Clamp(s.TestVideoQuality, 30, 95));
                UnityEngine.Object.Destroy(small);
                frameNo++;
                string name = "f" + frameNo.ToString("00000") + ".jpg";
                AtIo.WriteBytes(Path.Combine(videoDirAbs, name), jpg);
                double simNow = AtSim.SimT;
                index.Append(name).Append(',').Append(simNow.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
                // adapt: aim for ~1/30 s of race time between captured frames
                double adv = simNow - lastFrameSim;
                lastFrameSim = simNow;
                const double target = 1.0 / 30.0;
                if (adv > 1e-4) videoSpeed = Mathf.Clamp(videoSpeed * (float)(target / adv), 0.005f, 1f);
                else videoSpeed = Mathf.Clamp(videoSpeed * 1.5f, 0.005f, 1f);
            }
            catch (Exception e)
            {
                Main.Logger.Error("Real Tracks auto-test video frame: " + e.Message);
                if (Current == Phase.Clip) clipFrameFailed = true; else Current = Phase.Done;
            }
            frameBusy = false;
        }
    }
}
