using System;
using System.Collections.Generic;
using UnityEngine;
using RealTracks.ArtGeo;

namespace RealTracks
{
    /// <summary>
    /// Live pack-venue diagnostic for a visual car leaving the generated road.  It deliberately changes no
    /// racing line, collision, or AI behaviour: it measures the rendered car body's lateral envelope against
    /// layout_GP widths and reports the worst samples in the normal game log.
    /// </summary>
    public static class RoadEdgeMonitor
    {
        private static RoadIndex sRoad;
        private static int sCircuitId = -1;
        private static double sNext;
        private static readonly Dictionary<int, float> sWorstByCar = new Dictionary<int, float>();

        public static void OnRaceEnter()
        {
            sRoad = null; sCircuitId = -1; sNext = 0; sWorstByCar.Clear();
            if (Main.Settings == null || !Main.Settings.TestRoadEdgeMeasure) return;
            PackLayout l = ActiveVenue.CurrentLayout;
            Circuit c = ActiveVenue.CurrentCircuit;
            if (l == null || c == null || l.Paths == null || l.Paths.Track == null || l.Paths.Track.Count < 4) return;
            List<V3> p = new List<V3>();
            foreach (Vector3 q in l.Paths.Track) p.Add(new V3(q.x, q.y, q.z));
            float shoulder = Mathf.Clamp(Main.Settings.VenueRoadAiMarginM, 0f, 3f);
            float[] wl = new float[l.Paths.WidthLeft.Length], wr = new float[l.Paths.WidthRight.Length];
            for (int i = 0; i < wl.Length; i++) { wl[i] = l.Paths.WidthLeft[i] + shoulder; wr[i] = l.Paths.WidthRight[i] + shoulder; }
            sRoad = new RoadIndex(64f, 80f);
            sRoad.AddPolyline(p, wl, wr, 6f, 6f, true);
            sCircuitId = c.circuitID;
            Main.Log("RT ROAD EDGE: monitor armed for " + l.Pack.Id + " " + l.Key + "; " + sRoad.SegmentCount +
                " segments; " + shoulder.ToString("0.0") + " m shoulder per side; checks rendered body envelope every 0.5 s (positive metres = outside rendered road edge)");
        }

        public static void OnStep()
        {
            if (sRoad == null || Main.Settings == null || !Main.Settings.TestRoadEdgeMeasure) return;
            Game g = Game.instance;
            if (g == null || g.sessionManager == null || g.vehicleManager == null || ActiveVenue.CurrentCircuit == null || ActiveVenue.CurrentCircuit.circuitID != sCircuitId) return;
            if (!(App.instance.gameStateManager.currentState is RaceSessionState)) return;
            if (!g.sessionManager.hasSessionStarted || g.sessionManager.isRollingOut || GameTimer.simulationDeltaTime <= 0f) return;
            sNext -= GameTimer.simulationDeltaTime;
            if (sNext > 0) return;
            sNext = 0.5;

            for (int i = 0; i < g.vehicleManager.vehicleCount; i++)
            {
                RacingVehicle v = g.vehicleManager.GetVehicle(i);
                if (v == null || v.behaviourManager.isOutOfRace || v.pathController.currentPathType != PathController.PathType.Track || v.speed < 8f) continue;
                UnityVehicle uv = v.unityVehicle;
                Vector3 p = uv != null ? uv.transform.position : v.transform.position;
                Vector3 right = uv != null ? uv.transform.right : v.transform.right;
                float half = BodyHalfSpan(uv, right);
                float left = sRoad.Clearance(p.x - right.x * half, p.z - right.z * half);
                float rgt = sRoad.Clearance(p.x + right.x * half, p.z + right.z * half);
                float edge = Mathf.Max(left, rgt);
                float was;
                if (sWorstByCar.TryGetValue(v.id, out was) && edge <= was + 0.10f) continue;
                sWorstByCar[v.id] = edge;
                if (edge < 0.25f) continue;
                Main.Log(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "RT ROAD EDGE: car '{0}' P{1} visual-body edge {2:0.00} m outside road (left {3:0.00}, right {4:0.00}; half-span {5:0.00}; x {6:0.0}, z {7:0.0}; speed {8:0.0} kph)",
                    v.driver != null ? v.driver.name : v.id.ToString(), v.standingsPosition, edge, left, rgt, half, p.x, p.z, v.speed * 3.6f));
            }
        }

        /// <summary>Projected renderer bounds give an observable body-width envelope without assuming hidden wheel transforms.</summary>
        private static float BodyHalfSpan(UnityVehicle uv, Vector3 right)
        {
            if (uv == null) return 1.0f;
            float max = 0f;
            try
            {
                foreach (Renderer r in uv.GetComponentsInChildren<Renderer>())
                {
                    if (r == null || !r.enabled) continue;
                    Vector3 e = r.bounds.extents;
                    float projected = Mathf.Abs(right.x) * e.x + Mathf.Abs(right.y) * e.y + Mathf.Abs(right.z) * e.z;
                    if (projected > max) max = projected;
                }
            }
            catch { }
            return Mathf.Clamp(max > 0 ? max : 1f, 0.7f, 2.5f);
        }
    }
}
