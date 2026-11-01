using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace RealTracks
{
    /// <summary>
    /// Turn-targeted clips (request field "clips", AutoTestClips.cs). After the lap-2 smooth window and the default video, for each clip in order:
    /// wait at the normal test speed until a running car is leadMeters before the named turn on lap 2 or later, point the game's race camera at it,
    /// capture frames exactly as the default video does (same FrameCo, same slow motion) into video/itemN-Tn/, then restore the camera.
    /// Everything here is inert unless the item's request has clips (ClipTick returns on its first line).
    /// </summary>
    public static partial class AtPhases
    {
        private enum ClipSt { Idle, Waiting, Recording, Finished }

        private class ClipPlan
        {
            public bool Ok;
            public string Skip = "";
            public double TurnDist, AimX, AimZ;
            public int Gate;
        }

        private static readonly List<ClipPlan> plans = new List<ClipPlan>();
        private static ClipSt clipSt = ClipSt.Idle;
        private static bool clipsInit, clipArmed, clipFrameFailed;
        private static int clipNo, clipArmLap;
        private static double lapLen, clipStartSim, clipSeconds, clipArmSim;
        private static AtCarState clipCar;
        private static AtClipOut clipOut;
        private static string clipDirRel = "";
        // camera restore
        private static Vehicle prevVehicle;
        private static Quaternion prevRotation;
        private static float prevZoom;
        private static bool camSaved;
        private static double camYaw;
        private static readonly FieldInfo zoomField = typeof(FreeRoamCamera).GetField("mZoom", BindingFlags.Instance | BindingFlags.NonPublic);

        private static void ResetClips()
        {
            if (clipSt == ClipSt.Recording) RestoreCamera();
            plans.Clear();
            clipSt = ClipSt.Idle;
            clipsInit = clipArmed = clipFrameFailed = camSaved = false;
            clipNo = 0; clipArmLap = 0;
            lapLen = clipStartSim = clipSeconds = clipArmSim = 0;
            clipCar = null; clipOut = null; clipDirRel = "";
            prevVehicle = null;
        }

        // ------------------------------------------------------------------ per frame

        private static void ClipTick(AtBehaviour host, AtItemData d, Settings s, bool paused)
        {
            if (d.ClipRequests.Count == 0) return;
            try
            {
                if (!clipsInit) { clipsInit = true; InitClips(d, s); }
                if (clipSt == ClipSt.Idle || clipSt == ClipSt.Finished) return;
                SessionManager sm = Game.instance.sessionManager;
                bool over = sm.hasSessionEnded || sm.flag == SessionManager.Flag.Chequered;
                if (clipSt == ClipSt.Recording)
                {
                    if (over) { EndClip(d, "race over"); AbortClips(d, "race ended before the clip started"); return; }
                    if (paused || frameBusy) return;
                    if (clipFrameFailed) { EndClip(d, "frame capture failed"); return; }
                    double simT = AtSim.SimT;
                    if (simT - clipStartSim >= clipSeconds || frameNo >= 2000) { EndClip(d, "complete"); return; }
                    AimCamera(clipCar, plans[clipNo]);
                    frameBusy = true;
                    host.StartCoroutine(FrameCo(d, s, simT));
                    return;
                }
                // waiting for a car to reach the lead distance
                if (over) { AbortClips(d, "race ended before the clip started"); return; }
                if (paused) return;
                ClipPlan p = plans[clipNo];
                if (!p.Ok) { SkipClip(d, clipNo, p.Skip); NextClip(); return; }
                if (!clipArmed)
                {
                    clipArmed = true; clipArmLap = AtSim.LeaderLap; clipArmSim = AtSim.SimT;
                    AtRun.Event("clipArmed", "turn", d.ClipRequests[clipNo].Label, "leadMeters", d.ClipRequests[clipNo].LeadMeters, "leaderLap", clipArmLap, "simT", Math.Round(AtSim.SimT, 1));
                }
                if (AtClips.TimedOut(clipArmLap, AtSim.LeaderLap)) { SkipClip(d, clipNo, "timeout"); NextClip(); return; }
                AtCarState car; double dist;
                if (FindCar(d.ClipRequests[clipNo], p, out car, out dist)) StartClip(d, s, car, dist);
            }
            catch (Exception e)
            {
                Main.Logger.Error("Real Tracks auto-test clip: " + e);
                try { if (clipSt == ClipSt.Recording) EndClip(d, "exception"); } catch { }
                try { AbortClips(d, "clip error: " + e.Message); } catch { }
            }
        }

        // ------------------------------------------------------------------ plan

        private static void InitClips(AtItemData d, Settings s)
        {
            plans.Clear();
            string fail = "";
            List<LineTurn> turns = null;
            LayoutPaths lp = null;
            PathData data = null;
            string file = "";
            if (!s.TestTurnClips) fail = "the TestTurnClips setting is off";
            else if (!d.IsPack) fail = "not a pack venue (no corner table)";
            else
            {
                try
                {
                    SessionManager sm = Game.instance.sessionManager;
                    PackLayout pl = ActiveVenue.For(sm.eventDetails.circuit);
                    if (pl == null) fail = "not a pack venue (no corner table)";
                    else if (pl.CornersFile == null) fail = "the pack layout has no corner table";
                    else
                    {
                        file = pl.Pack.PathOf(pl.CornersFile);
                        if (!File.Exists(file)) fail = "corner table " + pl.CornersFile + " is missing";
                        else
                        {
                            turns = LineCornerMap.ParseCsv(File.ReadAllLines(file));
                            lp = pl.Paths;
                            data = sm.circuit.GetTrackPath().data;
                            lapLen = data.racingLineSpline.length;
                            if (turns.Count == 0) fail = "the corner table is empty";
                            else if (lapLen <= 0 || lp.Track.Count < 8 || lp.S == null || lp.S.Length != lp.Track.Count) fail = "no usable track path";
                        }
                    }
                }
                catch (Exception e) { fail = "plan failed: " + e.Message; }
            }
            for (int i = 0; i < d.ClipRequests.Count; i++)
            {
                AtClipRequest rq = d.ClipRequests[i];
                ClipPlan p = new ClipPlan();
                plans.Add(p);
                if (fail.Length > 0) { p.Skip = fail; continue; }
                LineTurn t = AtClips.FindTurn(turns, rq.Turn);
                if (t == null)
                {
                    List<string> have = new List<string>();
                    foreach (LineTurn x in turns) have.Add("T" + x.Turn);
                    p.Skip = "unknown turn " + rq.Label + " (the corner table has " + string.Join(",", have.ToArray()) + ")";
                    continue;
                }
                // the start of the turn's core (s0) -> the nearest track point -> the nearest game gate: its racing-line distance is where the turn is for the cars
                int ti = 0; double bd = double.MaxValue;
                for (int k = 0; k < lp.S.Length; k++) { double dd = Math.Abs(lp.S[k] - t.S0); if (dd < bd) { bd = dd; ti = k; } }
                Vector3 tp = lp.Track[ti];
                int gi = 0; double gb = double.MaxValue;
                for (int k = 0; k < data.gates.Count; k++)
                {
                    double dx = data.gates[k].position.x - tp.x, dz = data.gates[k].position.z - tp.z;
                    double dd = dx * dx + dz * dz;
                    if (dd < gb) { gb = dd; gi = k; }
                }
                p.Ok = true; p.Gate = gi; p.TurnDist = data.gates[gi].startDistance; p.AimX = t.ApexX; p.AimZ = t.ApexZ;
                Main.Log("RT CLIP: plan " + rq.Label + " core starts at s=" + t.S0.ToString("0.0") + " m, nearest gate " + gi + " (" + Math.Sqrt(gb).ToString("0.0") + " m off) at racing-line distance "
                    + p.TurnDist.ToString("0.0") + " of " + lapLen.ToString("0.0") + " m; lead " + rq.LeadMeters + " m, " + rq.Seconds + " s");
            }
            clipNo = 0;
            clipSt = ClipSt.Waiting;
            if (fail.Length > 0) Main.Log("RT CLIP: no clips for this item: " + fail);
        }

        // ------------------------------------------------------------------ choosing the car

        /// <summary>The first running car (lowest race position) that is on lap 2+, on the main track, and inside the start window. Logged by clipStart.</summary>
        private static bool FindCar(AtClipRequest rq, ClipPlan p, out AtCarState best, out double bestDist)
        {
            best = null; bestDist = 0;
            int bestPos = int.MaxValue;
            for (int i = 0; i < AtSim.Cars.Count; i++)
            {
                AtCarState cs = AtSim.Cars[i];
                try
                {
                    RacingVehicle v = cs.V;
                    if (cs.Data.Retired || cs.Data.Finished || v.behaviourManager.isOutOfRace || v.timer.hasSeenChequeredFlag) continue;
                    if (v.pathState.pathStateGroup != PathStateManager.PathStateGroup.OnTrack || v.pathController.currentPathType != PathController.PathType.Track) continue;
                    PathSpline.SplinePosition rl = v.pathController.GetCurrentPath().racingLinePosition;
                    if (rl.id < 0) continue;
                    double dist = AtClips.DistanceToTurn(rl.pathDistance, p.TurnDist, lapLen);
                    if (!AtClips.ShouldStart(v.timer.lap + 1, dist, rq.LeadMeters)) continue;
                    if (v.standingsPosition < bestPos) { bestPos = v.standingsPosition; best = cs; bestDist = dist; }
                }
                catch { }
            }
            return best != null;
        }

        // ------------------------------------------------------------------ start / end

        private static void StartClip(AtItemData d, Settings s, AtCarState car, double dist)
        {
            AtClipRequest rq = d.ClipRequests[clipNo];
            AtRunCtx r = AtRun.Current;
            if (r == null) { SkipClip(d, clipNo, "no run folder"); NextClip(); return; }
            string rel = "video/item" + d.Index + "-" + rq.Label;
            string abs = Path.Combine(r.RunDir, rel.Replace('/', Path.DirectorySeparatorChar));
            try { AtIo.Check(abs); Directory.CreateDirectory(abs); }
            catch (Exception e) { Main.Logger.Error("Real Tracks auto-test clip folder: " + e.Message); SkipClip(d, clipNo, "cannot create " + rel); NextClip(); return; }

            string camNote = "";
            try { camNote = FocusCamera(car, plans[clipNo]); }
            catch (Exception e) { Main.Logger.Error("Real Tracks auto-test clip camera: " + e); SkipClip(d, clipNo, "camera: " + e.Message); NextClip(); return; }

            videoDirAbs = abs; clipDirRel = rel;
            clipCar = car; clipSeconds = rq.Seconds;
            clipFrameFailed = false;
            frameNo = 0; frameBusy = false;
            videoSpeed = 0.1f;
            clipStartSim = lastFrameSim = AtSim.SimT;
            index.Length = 0;
            index.Append("frame,simT\n");
            clipOut = new AtClipOut();
            clipOut.Turn = rq.Label; clipOut.Dir = rel; clipOut.Car = car.Data.Name; clipOut.Lap = car.V.timer.lap + 1;
            Current = Phase.Clip;
            clipSt = ClipSt.Recording;
            AtRun.Event("clipStart", "turn", rq.Label, "car", car.Data.Name, "pos", car.V.standingsPosition, "leader", car.V.standingsPosition == 1, "lap", clipOut.Lap,
                "simT", Math.Round(AtSim.SimT, 2), "distToTurnM", Math.Round(dist, 1), "dir", rel, "seconds", rq.Seconds, "camera", camNote);
            Main.Log("RT CLIP: " + rq.Label + " started with " + car.Data.Name + " (P" + car.V.standingsPosition + ", lap " + clipOut.Lap + ") " + dist.ToString("0") + " m before the turn; " + camNote);
        }

        private static void EndClip(AtItemData d, string why)
        {
            if (clipSt != ClipSt.Recording) return;
            Current = Phase.Done;
            try { AtIo.WriteBytes(Path.Combine(videoDirAbs, "frames.csv"), Encoding.UTF8.GetBytes(index.ToString())); }
            catch (Exception e) { Main.Logger.Error("Real Tracks auto-test clip index: " + e.Message); }
            clipOut.Frames = frameNo;
            clipOut.Reason = why;
            d.ClipResults.Add(clipOut);
            AtRun.Event("clipEnd", "turn", clipOut.Turn, "frames", frameNo, "simSeconds", Math.Round(AtSim.SimT - clipStartSim, 2), "dir", clipDirRel, "why", why);
            Main.Log("RT CLIP: " + clipOut.Turn + " ended (" + why + "): " + frameNo + " frames, " + Math.Round(AtSim.SimT - clipStartSim, 1) + " s of race time");
            RestoreCamera();
            frameNo = 0; frameBusy = false;
            clipCar = null; clipOut = null;
            NextClip();
        }

        private static void NextClip()
        {
            clipNo++;
            clipArmed = false;
            clipSt = clipNo >= plans.Count ? ClipSt.Finished : ClipSt.Waiting;
        }

        private static void SkipClip(AtItemData d, int i, string reason)
        {
            AtClipOut o = new AtClipOut();
            o.Turn = d.ClipRequests[i].Label; o.Skipped = true; o.Reason = reason;
            d.ClipResults.Add(o);
            AtRun.Event("clipSkipped", "turn", o.Turn, "reason", reason);
            Main.Log("RT CLIP: " + o.Turn + " skipped: " + reason);
        }

        /// <summary>Race over or item aborted: finish a running clip (frames.csv written, camera restored) and skip every clip not yet done.</summary>
        public static void AbortClips(AtItemData d, string reason)
        {
            if (d == null || d.ClipRequests.Count == 0) return;
            try
            {
                if (clipSt == ClipSt.Recording) EndClip(d, reason);
                if (clipSt == ClipSt.Finished) return;
                int from = clipsInit ? Math.Min(clipNo, d.ClipRequests.Count) : 0;
                if (clipsInit && clipSt == ClipSt.Idle) from = 0;
                for (int i = from; i < d.ClipRequests.Count; i++) SkipClip(d, i, reason);
                clipNo = d.ClipRequests.Count;
                clipSt = ClipSt.Finished;
                clipsInit = true;
            }
            catch (Exception e) { Main.Logger.Error("Real Tracks auto-test clip abort: " + e.Message); }
        }

        // ------------------------------------------------------------------ camera
        // The race screen's camera is CameraManager mode FreeRoam (SessionHUD.OnEnter, SessionHUD.cs:95), an orbit camera that follows a target vehicle:
        // clicking a driver in the standings does CameraManager.SetTarget(vehicle, Smooth) (BaseDriverEntry.cs:25). The orbit is the rig's rotation
        // (FreeRoamCamera.ApplyRotation keeps whatever rotation the rig has, FreeRoamCamera.cs:409-436; pitch limit 25..80) and the distance is mZoom (:325-366).

        private static FreeRoamCamera Cam()
        {
            return App.instance.cameraManager.gameCamera.freeRoamCamera;
        }

        private static string FocusCamera(AtCarState car, ClipPlan p)
        {
            CameraManager cm = App.instance.cameraManager;
            FreeRoamCamera fr = Cam();
            Transform rig = fr.transform.parent;
            string note = "mode " + cm.activeMode;
            prevVehicle = fr.targetVehicle;
            prevRotation = rig.rotation;
            prevZoom = zoomField != null ? (float)zoomField.GetValue(fr) : 0f;
            camSaved = true;
            if (cm.activeMode != CameraManager.CameraMode.FreeRoam) { cm.ActivateMode(CameraManager.CameraMode.FreeRoam); note += " -> FreeRoam"; }
            cm.SetTarget(car.V, CameraManager.Transition.Instant);
            Vector3 cp = car.V.transform.position;
            camYaw = AtClips.YawDegrees(cp.x, cp.z, p.AimX, p.AimZ);
            rig.rotation = Quaternion.Euler(fr.cameraMinRotationX, (float)camYaw, 0f);     // lowest pitch (25 degrees): the turn ahead stays in view; camera sits behind the car
            float zoom = Mathf.Lerp(fr.cameraMinZoom, fr.cameraMaxZoom, Mathf.Clamp01(Main.Settings.TestClipZoom));   // 0 = closest the game allows, 1 = farthest
            if (zoomField != null) zoomField.SetValue(fr, zoom);
            return note + ", pitch " + fr.cameraMinRotationX.ToString("0") + ", zoom " + zoom.ToString("0") + " (range " + fr.cameraMinZoom.ToString("0") + "-" + fr.cameraMaxZoom.ToString("0") + "), yaw " + camYaw.ToString("0");
        }

        /// <summary>Per captured frame: turn the rig a few degrees towards the apex (before it) or along the car's heading (after it).</summary>
        private static void AimCamera(AtCarState car, ClipPlan p)
        {
            if (car == null || !camSaved) return;
            try
            {
                Vector3 cp = car.V.transform.position;
                double dist = double.PositiveInfinity;
                try { dist = AtClips.DistanceToTurn(car.V.pathController.GetCurrentPath().racingLinePosition.pathDistance, p.TurnDist, lapLen); } catch { }
                double target;
                if (dist < lapLen * 0.5 && dist > 15) target = AtClips.YawDegrees(cp.x, cp.z, p.AimX, p.AimZ);
                else { Vector3 f = car.V.transform.forward; target = Math.Atan2(f.x, f.z) * 180.0 / Math.PI; }
                camYaw = AtClips.StepAngle(camYaw, target, 2.0);
                FreeRoamCamera fr = Cam();
                fr.transform.parent.rotation = Quaternion.Euler(fr.cameraMinRotationX, (float)camYaw, 0f);
            }
            catch { }
        }

        private static void RestoreCamera()
        {
            if (!camSaved) return;
            camSaved = false;
            try
            {
                FreeRoamCamera fr = Cam();
                if (prevVehicle != null && fr.targetVehicle != prevVehicle) App.instance.cameraManager.SetTarget(prevVehicle, CameraManager.Transition.Instant);
                fr.transform.parent.rotation = prevRotation;
                if (zoomField != null && prevZoom > 0f) zoomField.SetValue(fr, prevZoom);
            }
            catch (Exception e) { Main.Logger.Error("Real Tracks auto-test clip camera restore: " + e.Message); }
        }
    }
}
