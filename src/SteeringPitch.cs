using System;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace RealTracks
{
    public partial class Settings
    {
        /// <summary>Pack circuits only: before each steering step, give the car's forward vector the pitch of the racing line (see SteeringPitch).</summary>
        public bool SteerPitchAlign = true;
    }

    /// <summary>
    /// Steering wobble on hills (Kyle's note 7: "wobble into turns, e.g. T11").
    /// Root cause, from SteeringManager.SimulationUpdate (SteeringManager.cs:201-215): the heading correction toward the racing line is
    ///   num8 = Vector3.Angle(car.forward, racingLine.forward)   (a 3D angle, always >= 0)  signed by  Cross(car.forward, racingLine.forward).y .
    /// The car is only ever turned about the Y axis (SetForward callers, Quaternion.AngleAxis(.., up)), so its pitch is frozen at whatever it was when it was
    /// last placed (grid / pit exit: GridPathState.cs:105, Vehicle.MovePosition:477). The racing line's forward is the 3D spline tangent, so on a slope
    /// the two vectors differ by the pitch difference even when the yaw is perfect: |num8| = |pitch gap| (7 deg on Austin's 13 % T1 hill) and only the SIGN follows the
    /// tiny yaw error. The controller therefore pushes +-gap every step: a bang-bang chatter, one heading reversal per simulation step. Phoenix is nearly flat, so
    /// the stock game never shows it.
    /// Fix: a prefix on SteeringManager.SimulationUpdate rewrites the car's forward with its own yaw and the racing line's pitch, so the angle is the yaw error only.
    /// Pack circuits only (ActiveVenue), toggle Settings.SteerPitchAlign.
    /// </summary>
    public static class SteeringPitch
    {
        private static readonly AccessTools.FieldRef<SteeringManager, Vehicle> sVehicle = AccessTools.FieldRefAccess<SteeringManager, Vehicle>("mVehicle");
        private static Circuit sCircuit;
        private static bool sIsPack;

        static SteeringPitch() { AtWobbleCheck.ExtraDetail = Summary; }

        /// <summary>Counters per circuit id: the auto-test writes its summary after the last item, so a single set would be wiped by a stock item that ran after the pack.</summary>
        private class Counters { public long Steps, Fixed; public double SumGapDeg, MaxGapDeg; public bool Logged; }
        private static readonly System.Collections.Generic.Dictionary<int, Counters> sCounters = new System.Collections.Generic.Dictionary<int, Counters>();
        private static Counters sCur;

        public static bool Active()
        {
            if (!Main.Enabled || Main.Settings == null || !Main.Settings.SteerPitchAlign) return false;
            Circuit c = ActiveVenue.CurrentCircuit;
            if (!ReferenceEquals(c, sCircuit))
            {
                sCircuit = c; sIsPack = ActiveVenue.For(c) != null; sCur = null;
                if (sIsPack && !sCounters.TryGetValue(c.circuitID, out sCur)) { sCur = new Counters(); sCounters[c.circuitID] = sCur; }
            }
            if (sIsPack) return true;
            // Auto-test only: a request item with "steerPitchAlign": 1 forces the fix on at a stock venue, to measure its pace effect A/B.
            AtRunCtx r = AtRun.Current;
            if (r == null || r.Cur == null || !r.Cur.SteerPitchAlign) return false;
            int key = 100000 + r.Cur.Index;
            if (!sCounters.TryGetValue(key, out sCur)) { sCur = new Counters(); sCounters[key] = sCur; }
            return true;
        }

        public static void Align(SteeringManager sm)
        {
            if (!Active() || sCur == null) return;
            Vehicle v = sVehicle(sm);
            if (v == null || v.pathController == null) return;
            PathController.Path path = v.pathController.GetCurrentPath();
            if (path == null) return;
            Vector3 line = path.racingLinePosition.forward;
            Vector3 fwd = v.transform.forward;
            float lh = Mathf.Sqrt(line.x * line.x + line.z * line.z);
            float fh = Mathf.Sqrt(fwd.x * fwd.x + fwd.z * fwd.z);
            if (lh < 1e-4f || fh < 1e-4f) return;
            float wantY = line.y / lh;               // tan(pitch) of the line
            float haveY = fwd.y / fh;
            float gap = Mathf.Abs(Mathf.Atan(wantY) - Mathf.Atan(haveY)) * Mathf.Rad2Deg;
            sCur.Steps++;
            sCur.SumGapDeg += gap;
            if (gap > sCur.MaxGapDeg) sCur.MaxGapDeg = gap;
            if (gap < 0.05f) return;
            sCur.Fixed++;
            Vector3 n = new Vector3(fwd.x / fh, wantY, fwd.z / fh);
            v.transform.SetForward(n.normalized);
            if (!sCur.Logged)
            {
                sCur.Logged = true;
                Main.Log("RT STEER: pitch alignment on for this pack circuit (first correction: car/line pitch gap " + gap.ToString("0.0", CultureInfo.InvariantCulture)
                    + " deg; SteerPitchAlign makes the steering controller see only the yaw error, not the hill)");
            }
        }

        /// <summary>One line for the auto-test summary: how often and how much the pitch had to be corrected for that item's circuit.</summary>
        public static string Summary(AtItemData d)
        {
            if (d == null) return "";
            if (!d.IsPack && !d.SteerPitchAlign) return "";
            if (Main.Settings != null && !Main.Settings.SteerPitchAlign) return "pitch alignment OFF";
            Counters c;
            int key = d.IsPack ? d.CircuitId : 100000 + d.Index;
            if (!sCounters.TryGetValue(key, out c) || c.Steps == 0) return "pitch alignment: no steps counted (key " + key + ")";
            return "pitch alignment: " + c.Fixed + "/" + c.Steps + " steering steps corrected, mean car-vs-line pitch gap " + (c.SumGapDeg / c.Steps).ToString("0.00", CultureInfo.InvariantCulture)
                + " deg, max " + c.MaxGapDeg.ToString("0.0", CultureInfo.InvariantCulture) + " deg";
        }

        public static void DrawGui(Settings s)
        {
            s.SteerPitchAlign = GUILayout.Toggle(s.SteerPitchAlign, " Smooth steering on hills: stop cars wobbling where the track climbs or drops (pack circuits only)");
        }
    }

    // Target checked against the decompiled source: SteeringManager.SimulationUpdate()  public void  SteeringManager.cs:132. The body is wrapped in try/catch.
    [HarmonyPatch(typeof(SteeringManager), "SimulationUpdate")]
    public static class Patch_Steering_SimulationUpdate
    {
        public static void Prefix(SteeringManager __instance)
        {
            try { SteeringPitch.Align(__instance); }
            catch (Exception e) { Main.Logger.Error("Real Tracks steering pitch alignment failed, game continues: " + e); }
        }
    }
}
