// Unity-free: per-donor-scene choices (which donor objects the art generator expects and reuses), keyed by the Unity scene name the pack's
// "donorScene" names. Phoenix keeps exactly its M1.1 behaviour (no water root, no yachts). Compiled by the mod and by tools/autotest-unit-tests.
using System;
using System.Collections.Generic;

namespace RealTracks
{
    public class DonorProfile
    {
        public string Scene = "";
        /// <summary>False when the scene is not in the table: the generic (Phoenix-like) choices apply and a WARNING is logged.</summary>
        public bool Known;
        /// <summary>Name of the transform whose renderers carry the donor's sea/harbour water material (null = the generic "water"/"river" name match).</summary>
        public string WaterRoot;
        /// <summary>Lower-case name prefixes of the donor's yacht prefabs (LODGroup roots) cloned for harbour moorings; empty = no yachts.</summary>
        public string[] YachtPrefixes = new string[0];
        public int MaxYachtProtos;
        /// <summary>Donor objects the profile relies on; each missing one is a WARNING in the log (never fatal).</summary>
        public string[] Expected = new string[0];
        /// <summary>Stock venue (location name, layout letter) that uses this scene: the auto-test fps / smoothness baseline for packs built on it.</summary>
        public string StockLocation = "", StockLayout = "A";

        public override string ToString()
        {
            return Scene + (Known ? "" : " (not in the donor table: generic choices)") + ": water " + (WaterRoot ?? "generic name match") +
                   ", yacht prefixes [" + string.Join(",", YachtPrefixes) + "] max " + MaxYachtProtos + ", expects [" + string.Join(",", Expected) + "], stock baseline " + StockLocation + " " + StockLayout;
        }
    }

    public static class DonorProfiles
    {
        private static readonly string[] Machinery = { "Paths", "TeamGarages", "Cameras", "Environment", "Minimap Bounds", "Env_Art_Static" };

        public static DonorProfile For(string scene)
        {
            DonorProfile p = new DonorProfile();
            p.Scene = scene ?? "";
            switch (p.Scene)
            {
                case "Phoenix":
                    p.Known = true;
                    p.Expected = Machinery;
                    p.StockLocation = "Phoenix"; p.StockLayout = "B";
                    break;
                case "Cape Town":
                    p.Known = true;
                    p.WaterRoot = "Water_HQ";
                    p.YachtPrefixes = new string[] { "yacht_" };
                    p.MaxYachtProtos = 6;
                    List<string> e = new List<string>(Machinery);
                    e.Add("Water_HQ");
                    e.Add("Traffic");
                    p.Expected = e.ToArray();
                    p.StockLocation = "Cape Town"; p.StockLayout = "A";
                    break;
                default:
                    p.Known = false;
                    p.Expected = Machinery;
                    p.StockLocation = p.Scene; p.StockLayout = "A";
                    break;
            }
            return p;
        }
    }
}
