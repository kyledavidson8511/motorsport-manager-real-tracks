using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace RealTracks
{
    /// <summary>
    /// Pack art as Unity objects, loaded on first use and cached for the session (Texture2D.LoadImage, Unity 5.3).
    /// Public for the other packages: PackArt.Minimap(layout), PackArt.Loading(pack).
    /// Everything returns null when the file is missing or bad (logged once), and callers then fall back to the donor's art.
    /// </summary>
    public static class PackArt
    {
        private static readonly Dictionary<int, Sprite> sMinimap = new Dictionary<int, Sprite>();
        private static readonly Dictionary<string, Texture2D> sLoading = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, Sprite> sImage = new Dictionary<string, Sprite>();
        private static readonly HashSet<string> sFailed = new HashSet<string>();

        /// <summary>The layout's minimap outline as a Sprite (white on transparent, north-up), or null.</summary>
        public static Sprite Minimap(PackLayout l)
        {
            if (l == null) return null;
            Sprite s;
            if (sMinimap.TryGetValue(l.CircuitId, out s) && s != null) return s;
            Texture2D t = Load(l.Pack.PathOf(l.MinimapFile), "minimap " + l.Pack.Id + "/" + l.Key);
            if (t == null) return null;
            s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
            s.name = "Minimap_" + RegPlan.SpriteName(l.Pack.Id) + l.Letter;
            s.hideFlags = HideFlags.HideAndDontSave;
            sMinimap[l.CircuitId] = s;
            Main.Log("Real Tracks: minimap sprite for " + l.Pack.Id + "/" + l.Key + " loaded (" + t.width + "x" + t.height + ")");
            return s;
        }

        /// <summary>The pack's loading picture (1920x1080 from the orthophoto), or null.</summary>
        public static Texture2D Loading(TrackPack p)
        {
            if (p == null) return null;
            Texture2D t;
            if (sLoading.TryGetValue(p.Id, out t) && t != null) return t;
            t = Load(p.PathOf(p.LoadingFile), "loading picture " + p.Id);
            if (t == null) return null;
            sLoading[p.Id] = t;
            Main.Log("Real Tracks: loading picture for " + p.Id + " loaded (" + t.width + "x" + t.height + ")");
            return t;
        }

        /// <summary>
        /// The picture for the TrackImages atlas slot (Quick Race circuit image): the loading picture, centre-cropped to the
        /// aspect ratio of the donor's own TrackImages sprite so the Image component does not stretch it.
        /// </summary>
        public static Sprite CircuitImage(TrackPack p)
        {
            if (p == null) return null;
            Sprite s;
            if (sImage.TryGetValue(p.Id, out s) && s != null) return s;
            Texture2D t = Loading(p);
            if (t == null) return null;
            Rect r = new Rect(0, 0, t.width, t.height);
            string donor = Registration.DonorSprite(p);
            if (donor != null)
            {
                Sprite d = App.instance.atlasManager.GetSprite(AtlasManager.Atlas.TrackImages, "TrackImages-" + donor);
                if (d != null && d.rect.width > 1f && d.rect.height > 1f)
                {
                    float target = d.rect.width / d.rect.height;
                    float have = (float)t.width / t.height;
                    if (have > target) { float w = t.height * target; r = new Rect((t.width - w) / 2f, 0, w, t.height); }
                    else { float h = t.width / target; r = new Rect(0, (t.height - h) / 2f, t.width, h); }
                }
            }
            s = Sprite.Create(t, r, new Vector2(0.5f, 0.5f), 100f);
            s.name = "TrackImages-" + RegPlan.SpriteName(p.Id);
            s.hideFlags = HideFlags.HideAndDontSave;
            sImage[p.Id] = s;
            return s;
        }

        private static Texture2D Load(string path, string what)
        {
            if (string.IsNullOrEmpty(path) || sFailed.Contains(path)) return null;
            try
            {
                if (!File.Exists(path))
                {
                    sFailed.Add(path);
                    Main.Log("Real Tracks: " + what + " not found at " + path + ", the donor's art is used instead");
                    return null;
                }
                Texture2D t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!t.LoadImage(File.ReadAllBytes(path)))
                {
                    UnityEngine.Object.Destroy(t);
                    sFailed.Add(path);
                    Main.Logger.Error("Real Tracks: " + what + " could not be decoded (" + path + ")");
                    RtFallbacks.Note("pack art", what + " could not be decoded (" + path + ")");
                    return null;
                }
                t.wrapMode = TextureWrapMode.Clamp;
                t.filterMode = FilterMode.Bilinear;
                // Resources.UnloadUnusedAssets runs before every race (EventLoadingState.cs:55): keep these alive.
                t.hideFlags = HideFlags.HideAndDontSave;
                return t;
            }
            catch (Exception e)
            {
                sFailed.Add(path);
                Main.Logger.Error("Real Tracks: loading " + what + " failed: " + e);
                RtFallbacks.Note("pack art", what + " could not be loaded: " + e.Message);
                return null;
            }
        }
    }
}
