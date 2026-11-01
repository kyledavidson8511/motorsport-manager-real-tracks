using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace RealTracks
{
    /// <summary>
    /// Holds or slowly orbits one of the donor scene's eight cameras around a focus point.
    /// The game never renders these cameras: CameraManager registers them (CircuitScene.Start) and AnimatedCamera copies the
    /// chosen one's transform, field of view and clip planes onto the real game camera every frame (AnimatedCamera.Update),
    /// so placing the transform is all that is needed. The donor's Animator (which flew the Phoenix fly-through) is disabled.
    /// </summary>
    public class VenueCameraRig : MonoBehaviour
    {
        public Vector3 Focus;
        public float Radius = 100f;
        public float Height = 40f;
        public float Azimuth;           // degrees, 0 = camera south of the focus looking north
        public float SpeedDegPerSec;    // 0 = static

        private void OnEnable() { Place(); }

        private void Update()
        {
            if (SpeedDegPerSec != 0f) Azimuth += SpeedDegPerSec * Time.unscaledDeltaTime;
            Place();
        }

        public void Place()
        {
            float a = Azimuth * Mathf.Deg2Rad;
            Vector3 p = Focus + new Vector3(Mathf.Sin(a) * Radius, Height, -Mathf.Cos(a) * Radius);
            Vector3 look = Focus - p;
            transform.position = p;
            if (look.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(look, Vector3.up);
        }
    }

    public static class VenueCameras
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly AccessTools.FieldRef<CircuitScene, Camera> sPracticeIntro = AccessTools.FieldRefAccess<CircuitScene, Camera>("practiceIntroCamera");
        private static readonly AccessTools.FieldRef<CircuitScene, Camera> sQualiIntro = AccessTools.FieldRefAccess<CircuitScene, Camera>("qualifyingIntroCamera");
        private static readonly AccessTools.FieldRef<CircuitScene, Camera> sRaceIntro = AccessTools.FieldRefAccess<CircuitScene, Camera>("raceIntroCamera");
        private static readonly AccessTools.FieldRef<CircuitScene, Camera> sPracticeHub = AccessTools.FieldRefAccess<CircuitScene, Camera>("practiceHUBCamera");
        private static readonly AccessTools.FieldRef<CircuitScene, Camera> sQualiHub = AccessTools.FieldRefAccess<CircuitScene, Camera>("qualifyingHUBCamera");
        private static readonly AccessTools.FieldRef<CircuitScene, Camera> sRaceHub = AccessTools.FieldRefAccess<CircuitScene, Camera>("raceHUBCamera");
        private static readonly AccessTools.FieldRef<CircuitScene, Camera> sPostSession = AccessTools.FieldRefAccess<CircuitScene, Camera>("postSessionCamera");
        private static readonly AccessTools.FieldRef<CircuitScene, Camera> sSimulating = AccessTools.FieldRefAccess<CircuitScene, Camera>("simulatingSessionCamera");

        private static PackLayout sCheckLayout;      // the layout being built: the tunnel check needs its tunnels (ArtTunnels.CheckCamera)

        public static void Apply(CircuitScene scene, LayoutPaths lp, PathData track, BuiltVenue built)
        {
            Settings st = Main.Settings;
            sCheckLayout = built != null ? built.Layout : null;

            // Venue bounds from the track and the pit lane (pack frame = world).
            float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue, ySum = 0f;
            List<Vector3>[] sets = { lp.Track, lp.PitLane };
            int n = 0;
            foreach (List<Vector3> l in sets)
                foreach (Vector3 p in l)
                {
                    x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x);
                    z0 = Mathf.Min(z0, p.z); z1 = Mathf.Max(z1, p.z);
                    ySum += p.y; n++;
                }
            Vector3 centre = new Vector3((x0 + x1) * 0.5f, ySum / Mathf.Max(1, n), (z0 + z1) * 0.5f);
            float radius = 0.5f * Mathf.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0)) * 0.9f;

            // Start/finish area: gate 0 and the track direction there.
            PathData.Gate g0 = track.gates[0];
            Vector3 fwd = g0.normal; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
            float orbit = st.VenueCameraOrbit ? st.VenueCameraOrbitSpeed : 0f;

            int placed = 0;
            // Overview cameras: frame the whole venue from the south-ish at 30 degrees, slow orbit.
            placed += Overview(sPracticeHub(scene), centre, radius, orbit * 0.5f, 0f);
            placed += Overview(sQualiHub(scene), centre, radius, orbit * 0.5f, 120f);
            placed += Overview(sRaceHub(scene), centre, radius, orbit * 0.5f, 240f);
            placed += Overview(sSimulating(scene), centre, radius, orbit * 0.5f, 60f);
            placed += Overview(sPostSession(scene), centre, radius * 0.8f, orbit * 0.4f, 180f);

            // Intro cameras: over the start/finish and the run to turn one.
            Vector3 startFocus = g0.position + fwd * 40f;
            Vector3 gridFocus = g0.position + fwd * 100f;
            placed += Close(sPracticeIntro(scene), startFocus, 120f, 38f, 20f, orbit);
            placed += Close(sQualiIntro(scene), g0.position + fwd * 10f, 70f, 14f, 200f, -orbit * 0.8f);
            placed += Close(sRaceIntro(scene), gridFocus, 170f, 62f, 330f, orbit * 0.7f);

            built.Counts["camerasPlaced"] = placed;
            Main.Log(string.Format(Inv, "RT VENUE CAMERAS: {0} of 8 placed; venue centre ({1:0},{2:0},{3:0}), framing radius {4:0} m, start/finish ({5:0},{6:0}), orbit {7:0.0} deg/s.",
                placed, centre.x, centre.y, centre.z, radius, g0.position.x, g0.position.z, orbit));
        }

        private static int Overview(Camera cam, Vector3 centre, float radius, float speed, float azimuth)
        {
            if (cam == null) return 0;
            float fov = Mathf.Max(20f, cam.fieldOfView);
            // fit the venue's bounding circle in the narrower (vertical) field of view, with a margin
            float dist = Mathf.Max(200f, radius * 1.15f / Mathf.Sin(fov * 0.5f * Mathf.Deg2Rad));
            float elev = 32f * Mathf.Deg2Rad;
            return Configure(cam, centre, dist * Mathf.Cos(elev), dist * Mathf.Sin(elev), azimuth, speed);
        }

        private static int Close(Camera cam, Vector3 focus, float radius, float height, float azimuth, float speed)
        {
            if (cam == null) return 0;
            return Configure(cam, focus, radius, height, azimuth, speed);
        }

        private static int Configure(Camera cam, Vector3 focus, float radius, float height, float azimuth, float speed)
        {
            // Disable the donor fly-through (RegisterCameraForMode set it to unscaled time; a disabled Animator leaves the transform alone).
            foreach (Animator a in cam.GetComponentsInChildren<Animator>(true)) a.enabled = false;
            VenueCameraRig rig = cam.GetComponent<VenueCameraRig>();
            if (rig == null) rig = cam.gameObject.AddComponent<VenueCameraRig>();
            rig.Focus = focus;
            rig.Radius = radius;
            rig.Height = height;
            rig.Azimuth = azimuth;
            rig.SpeedDegPerSec = speed;
            rig.Place();
            ArtTunnels.CheckCamera(sCheckLayout, cam.name, focus, radius, height, azimuth, speed != 0f);    // warns when the camera would sit inside a tunnel shell
            return 1;
        }
    }
}
