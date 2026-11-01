using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace RealTracks
{
    /// <summary>Settings of the UI work (prefix Ui): Quick Race custom-track column and the race-intro overlay.</summary>
    public partial class Settings
    {
        /// <summary>Pack circuits get their own "Custom tracks" panel left of the stock list. Off = they are appended to the stock grid.</summary>
        public bool UiCustomTrackColumn = true;
        /// <summary>Cover the donor's race-intro movie picture with a map and outline made for the pack circuit.</summary>
        public bool UiCustomIntro = true;
        /// <summary>How long the world-map part of the intro lasts (the outline follows until the movie ends).</summary>
        public float UiIntroMapSeconds = 4.0f;
        /// <summary>How long the outline takes to draw. 0 = automatic (most of what is left of the movie).</summary>
        public float UiIntroRevealSeconds = 0f;
        /// <summary>Lay the Quick Race column out again when the screen size is not the one it was built for.</summary>
        public bool UiColumnRebuildOnResize = true;
    }

    /// <summary>
    /// The race-intro overlay (M1.1 A2). The race load plays the Bink movie "TrackMovies/&lt;spriteName&gt;" (EventLoadingState.cs:83,
    /// BaseMovieScreen.PlayNextMovieInQueue) and a pack circuit plays its donor's (DonorSwap): a world map zooming to Phoenix, then
    /// Phoenix's oval. Bink 2 cannot be edited, so the movie keeps playing for its sound and timing and this overlay hides its
    /// picture. BinkWrapper draws the movie itself (OnPostRender -> Bink.ScheduleOverlay on the back buffer, BinkWrapper.cs:122-148),
    /// not through a UI element, and the screen's UI renders over it: an opaque full-screen RawImage in the screen's canvas, below
    /// its texts, therefore covers the movie and leaves the texts, flag and logo showing.
    /// </summary>
    public class UiIntroOverlay : MonoBehaviour
    {
        public const string ObjectName = "RT_IntroOverlay";
        private const int OutlineSize = 1024;

        // inputs
        public Settings Cfg;
        public double Lat, Lon;
        public double[] OutX, OutY;         // outline pixels in the OutlineSize box, y up
        public Sprite FlagSprite;
        public string WorldMapPath;
        public string ScreenName = "";

        // state
        private Texture2D mMapTex, mMarkerTex, mSparkTex, mOutlineTex, mMaskTex;
        private RawImage mMap, mOutline;
        private Image mMarker, mSpark, mFlag, mFlagMask;
        private CanvasGroup mMapGroup;
        private RectTransform mRect, mBox, mFlagRect, mMarkerRect, mSparkRect;
        private UiOutlineRaster mRaster;
        private double mVenueU, mVenueV;
        private double mOwnT, mMapSeconds, mRevealSeconds, mMovieLength = -1;
        private bool mDecided, mMovieClockTrusted, mDirty;
        private float mLastUpload;

        public bool Build(Transform parent, int siblingIndex)
        {
            mMapSeconds = Math.Max(1.0, Cfg.UiIntroMapSeconds);
            UiIntroMath.LonLatToUv(Lon, Lat, out mVenueU, out mVenueV);

            byte[] png = File.ReadAllBytes(WorldMapPath);
            mMapTex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!mMapTex.LoadImage(png)) throw new Exception("world map PNG could not be decoded: " + WorldMapPath);
            mMapTex.wrapMode = TextureWrapMode.Clamp;
            mMapTex.filterMode = FilterMode.Bilinear;
            mMapTex.hideFlags = HideFlags.HideAndDontSave;

            mMarkerTex = RawTex(128, UiIntroSprites.Marker(128));
            mSparkTex = RawTex(64, UiIntroSprites.Spark(64));
            mRaster = new UiOutlineRaster(OutlineSize, OutX, OutY);
            mOutlineTex = RawTex(OutlineSize, mRaster.Rgba);
            mMaskTex = RawTex(OutlineSize, mRaster.FilledMask());

            // ---- objects: root > [ box(flag, outline, spark) , map group(map, marker) ]  (later siblings draw over earlier ones)
            mRect = (RectTransform)gameObject.transform;
            mRect.SetParent(parent, false);
            Stretch(mRect);
            mRect.SetSiblingIndex(siblingIndex);
            CanvasGroup rootGroup = gameObject.AddComponent<CanvasGroup>();
            rootGroup.blocksRaycasts = false; rootGroup.interactable = false;

            Image black = NewChild<Image>("Black", mRect);
            Stretch(black.rectTransform);
            black.color = Color.black; black.raycastTarget = false;

            mBox = NewRect("Box", mRect);
            mBox.anchorMin = mBox.anchorMax = new Vector2(0.58f, 0.5f);
            mBox.pivot = new Vector2(0.5f, 0.5f);

            // The flag is a full-sized child of an alpha mask made from the closed track polygon.  The old version
            // used the polygon's bounding rectangle, which visibly coloured the empty space around Monaco and Austin.
            mFlagMask = NewChild<Image>("FlagMask", mBox);
            Stretch(mFlagMask.rectTransform);
            mFlagMask.sprite = SpriteOf(mMaskTex); mFlagMask.color = Color.white; mFlagMask.raycastTarget = false;
            Mask flagMask = mFlagMask.gameObject.AddComponent<Mask>();
            flagMask.showMaskGraphic = false;
            mFlag = NewChild<Image>("Flag", mFlagMask.transform);
            mFlagRect = mFlag.rectTransform;
            Stretch(mFlagRect);
            mFlag.sprite = FlagSprite; mFlag.raycastTarget = false;
            mFlag.color = new Color(1, 1, 1, 0);
            mFlag.enabled = FlagSprite != null;
            Main.Log("RT UI: intro flag uses closed-track alpha mask (" + OutlineSize + " px; flag is clipped to the circuit silhouette)");

            mOutline = NewChild<RawImage>("Outline", mBox);
            Stretch(mOutline.rectTransform);
            mOutline.texture = mOutlineTex; mOutline.raycastTarget = false;

            mSpark = NewChild<Image>("Spark", mBox);
            mSparkRect = mSpark.rectTransform;
            mSparkRect.anchorMin = mSparkRect.anchorMax = new Vector2(0, 0);
            mSpark.sprite = SpriteOf(mSparkTex); mSpark.color = new Color(1f, 0.25f, 0.25f, 0f); mSpark.raycastTarget = false;

            RectTransform mapRoot = NewRect("MapGroup", mRect);
            Stretch(mapRoot);
            mMapGroup = mapRoot.gameObject.AddComponent<CanvasGroup>();
            mMapGroup.blocksRaycasts = false; mMapGroup.interactable = false;
            mMap = NewChild<RawImage>("Map", mapRoot);
            Stretch(mMap.rectTransform);
            mMap.texture = mMapTex; mMap.raycastTarget = false;
            mMarker = NewChild<Image>("Marker", mapRoot);
            mMarkerRect = mMarker.rectTransform;
            mMarker.sprite = SpriteOf(mMarkerTex); mMarker.raycastTarget = false;
            mMarker.color = new Color(1, 1, 1, 0);
            Apply(0);
            return true;
        }

        // ------------------------------------------------------------------------------------------------ frame

        private void Update()
        {
            try
            {
                if (mRect == null) return;
                // the screen closed without OnExit reaching us: go away (OnExit normally removes the overlay)
                if (!gameObject.activeInHierarchy) return;
                mOwnT += Time.unscaledDeltaTime;
                double t = mOwnT;
                UpdateMovieClock(ref t);
                if (!mDecided && (mMovieLength > 0 || mOwnT > 1.2)) Decide();
                Apply(t);
            }
            catch (Exception e)
            {
                Main.Logger.Error("Real Tracks: intro overlay update failed, removing it: " + e);
                RtFallbacks.Note("intro overlay", "update failed, overlay removed (the donor's movie shows): " + e.GetType().Name + " " + e.Message);
                UiIntro.Remove();
            }
        }

        private void Decide()
        {
            mDecided = true;
            CheckCoverage();
            if (mMovieLength > 0 && mMapSeconds > mMovieLength * 0.6) mMapSeconds = Math.Max(1.0, mMovieLength * 0.6);
            double left = mMovieLength > 0 ? mMovieLength - mMapSeconds : -1;
            mRevealSeconds = Cfg.UiIntroRevealSeconds > 0.1f ? Cfg.UiIntroRevealSeconds : (left > 0 ? Math.Max(1.5, (left - 0.8) * 0.85) : 5.0);
            UiMapView v = UiIntroMath.MapAt(0, mMapSeconds, mVenueU, mVenueV, ScreenAspect());
            Main.Log("RT UI: intro overlay on " + ScreenName + ": map " + mMapSeconds.ToString("0.0") + "s, outline " +
                     (left > 0 ? left.ToString("0.0") : "?") + "s (draws in " + mRevealSeconds.ToString("0.0") + "s), dot at " +
                     Lat.ToString("0.#####") + "," + Lon.ToString("0.#####") + " -> uv " + mVenueU.ToString("0.000") + "," + mVenueV.ToString("0.000") +
                     "; movie " + (mMovieLength > 0 ? mMovieLength.ToString("0.00") + "s (read from Bink)" : "length unknown (own timer)") +
                     "; start view " + v.W.ToString("0.000") + "x" + v.H.ToString("0.000") + " uv");
        }

        /// <summary>The overlay hides the movie only if it spans the whole canvas, is active and is not behind the movie's own object: otherwise the donor's picture would show around or through it.</summary>
        private void CheckCoverage()
        {
            try
            {
                Canvas c = mRect.GetComponentInParent<Canvas>();
                RectTransform cr = c != null ? (RectTransform)c.rootCanvas.transform : null;
                string why = null;
                if (!gameObject.activeInHierarchy) why = "the overlay object is not active";
                else if (cr == null) why = "no canvas above the overlay";
                else if (mRect.rect.width < 0.97f * cr.rect.width || mRect.rect.height < 0.97f * cr.rect.height)
                    why = "the overlay is " + mRect.rect.width.ToString("0") + "x" + mRect.rect.height.ToString("0") + " in a " + cr.rect.width.ToString("0") + "x" + cr.rect.height.ToString("0") + " canvas";
                Main.Log("RT UI: intro overlay coverage: " + (why == null ? "full canvas" : "NOT FULL: " + why) + " (screen " + Screen.width + "x" + Screen.height + ")");
                if (why != null) RtFallbacks.Note("intro overlay", "the movie picture may not be fully covered: " + why);
            }
            catch (Exception e) { Main.Log("RT UI: intro coverage check failed: " + e.Message); }
        }

        private static float ScreenAspect() { return Screen.height > 0 ? (float)Screen.width / Screen.height : 16f / 9f; }

        private void Apply(double t)
        {
            float H = mRect.rect.height, W = mRect.rect.width;
            double aspect = H > 1 ? W / H : ScreenAspect();
            double mapSec = mMapSeconds;
            // map stage
            UiMapView v = UiIntroMath.MapAt(t, mapSec, mVenueU, mVenueV, aspect);
            mMap.uvRect = new Rect((float)v.U0, (float)(1 - (v.V0 + v.H)), (float)v.W, (float)v.H);
            double mapAlpha = UiIntroMath.MapAlpha(t, mapSec, 0.7);
            mMapGroup.alpha = (float)mapAlpha;
            if (mapAlpha <= 0.001 && mMapGroup.gameObject.activeSelf) mMapGroup.gameObject.SetActive(false);
            float ms = (float)(0.085 * H * UiIntroMath.RingScale(t));
            mMarkerRect.anchorMin = mMarkerRect.anchorMax = new Vector2((float)v.DotX, (float)(1 - v.DotY));
            mMarkerRect.anchoredPosition = Vector2.zero;
            mMarkerRect.sizeDelta = new Vector2(ms, ms);
            mMarker.color = new Color(1, 1, 1, (float)UiIntroMath.RingAlpha(t, mapSec));

            // outline stage
            float side = 0.76f * H;
            mBox.sizeDelta = new Vector2(side, side);
            double p = mDecided ? UiIntroMath.RevealAt(t, mapSec, mRevealSeconds) : 0;
            if (mRaster.RevealTo(p)) mDirty = true;
            if (mDirty && (p >= 1.0 || Time.unscaledTime - mLastUpload > 0.04f))
            {
                mOutlineTex.LoadRawTextureData(mRaster.Rgba);
                mOutlineTex.Apply(false);
                mLastUpload = Time.unscaledTime;
                mDirty = false;
            }
            mFlag.color = new Color(1, 1, 1, (float)(0.22 * UiIntroMath.Smooth((p - 0.25) / 0.75)));
            float sp = 0.07f * side;
            mSparkRect.anchorMin = mSparkRect.anchorMax = new Vector2((float)(mRaster.HeadX / OutlineSize), (float)(mRaster.HeadY / OutlineSize));
            mSparkRect.anchoredPosition = Vector2.zero;
            mSparkRect.sizeDelta = new Vector2(sp, sp);
            mSpark.color = new Color(1f, 0.3f, 0.3f, (p > 0 && p < 1) ? 0.9f : 0f);
        }

        // ------------------------------------------------------------------------------------------------ movie clock

        private static FieldInfo sInfoField;
        private static bool sInfoSearched;

        /// <summary>
        /// Reads the Bink movie's frame counters through reflection (BinkWrapper.mBinkInfo, refreshed every frame in OnPostRender,
        /// BinkWrapper.cs:120). The struct may still hold the previous movie's numbers at first, so the reading is only trusted when
        /// the frame position agrees with this overlay's own timer.
        /// </summary>
        private void UpdateMovieClock(ref double t)
        {
            if (mOwnT < 0.12 || !BinkWrapper.HasInstance) return;
            if (!sInfoSearched) { sInfoSearched = true; sInfoField = AccessTools.Field(typeof(BinkWrapper), "mBinkInfo"); }
            if (sInfoField == null) return;
            object info = sInfoField.GetValue(BinkWrapper.instance);
            if (info == null) return;
            double frames = Num(info, "Frames"), frameNum = Num(info, "FrameNum"), rate = Num(info, "FrameRate"), div = Num(info, "FrameRateDiv");
            if (frames <= 0 || rate <= 0) return;
            double fps = rate / Math.Max(1.0, div);
            double len = frames / fps;
            double pos = Math.Max(0, frameNum - 1) / fps;
            if (!mMovieClockTrusted)
            {
                if (len >= 1.0 && len <= 120.0 && Math.Abs(pos - mOwnT) < 0.7) { mMovieClockTrusted = true; mMovieLength = len; }
                else return;
            }
            t = pos;
            mOwnT = pos;       // stay in step with the movie if it hitches
        }

        private static double Num(object boxed, string field)
        {
            FieldInfo f = boxed.GetType().GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (f == null) return 0;
            try { return Convert.ToDouble(f.GetValue(boxed)); } catch (Exception) { return 0; }
        }

        // ------------------------------------------------------------------------------------------------ helpers

        private static Texture2D RawTex(int size, byte[] rgba)
        {
            Texture2D t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            t.LoadRawTextureData(rgba);
            t.Apply(false);
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        private static Sprite SpriteOf(Texture2D t)
        {
            Sprite s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100f);
            s.hideFlags = HideFlags.HideAndDontSave;
            return s;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        private static T NewChild<T>(string name, Transform parent) where T : Component
        {
            RectTransform r = NewRect(name, parent);
            return r.gameObject.AddComponent<T>();
        }

        private static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
        }

        private void OnDestroy()
        {
            Kill(mMapTex); Kill(mMarkerTex); Kill(mSparkTex); Kill(mOutlineTex); Kill(mMaskTex);
            if (mMarker != null && mMarker.sprite != null) Destroy(mMarker.sprite);
            if (mSpark != null && mSpark.sprite != null) Destroy(mSpark.sprite);
            if (mFlagMask != null && mFlagMask.sprite != null) Destroy(mFlagMask.sprite);
        }

        private static void Kill(UnityEngine.Object o) { if (o != null) Destroy(o); }
    }

    /// <summary>Creates and removes the overlay; the Harmony patches call these.</summary>
    public static class UiIntro
    {
        private static GameObject sOverlay;

        public static void Remove()
        {
            if (sOverlay != null) UnityEngine.Object.Destroy(sOverlay);
            sOverlay = null;
        }

        /// <summary>The event circuit's pack layout if the overlay applies (setting on, pack circuit with a location), else null.</summary>
        public static PackLayout LayoutFor(Circuit c)
        {
            if (!Main.Settings.UiCustomIntro) return null;
            PackLayout l = ActiveVenue.For(c);
            return l != null && l.Pack.HasLocation ? l : null;
        }

        public static void Show(MovieLocationScreen screen)
        {
            Remove();
            Game g = Game.instance;
            if (g == null || g.player == null || g.player.team == null) return;
            RaceEventDetails ev = g.player.team.championship.GetCurrentEventDetails();
            Circuit circuit = ev != null ? ev.circuit : null;
            PackLayout layout = LayoutFor(circuit);
            if (layout == null) return;                                    // stock circuits: nothing is touched

            string map = Path.Combine(Main.ModPath, Path.Combine("ui", "worldmap.png"));
            if (!File.Exists(map)) { Main.Log("RT UI: intro overlay skipped, world map not found at " + map); RtFallbacks.Note("intro overlay", "not shown, world map file not found (" + map + "): the donor's movie plays as it is"); return; }
            LayoutPaths lp = layout.Paths;
            List<double> xs = new List<double>(), zs = new List<double>();
            for (int i = 0; i < lp.Track.Count; i++) { xs.Add(lp.Track[i].x); zs.Add(lp.Track[i].z); }
            if (xs.Count < 8) { Main.Log("RT UI: intro overlay skipped, layout has no track points"); RtFallbacks.Note("intro overlay", "not shown, the layout has no track points: the donor's movie plays as it is"); return; }
            double[] px, py;
            UiIntroMath.FitOutline(xs, zs, 1024, 56, 400, out px, out py);

            Sprite flag = null;
            try
            {
                Nationality nat = circuit.nationality;
                if (nat != null) flag = App.instance.atlasManager.GetSprite(AtlasManager.Atlas.Shared1, "Flags-64X64-" + nat.flagSpritePathName);
            }
            catch (Exception e) { Main.Log("RT UI: intro flag not found: " + e.Message); RtFallbacks.Note("intro overlay", "flag sprite not found, the outline is drawn without the flag: " + e.Message); }

            Transform parent; int index;
            ChooseHost(screen, out parent, out index);
            if (parent == null) { Main.Log("RT UI: intro overlay skipped, no canvas found in " + screen.name); RtFallbacks.Note("intro overlay", "not hosted, no canvas found in " + screen.name + ": the donor's movie plays as it is"); return; }

            GameObject go = new GameObject(UiIntroOverlay.ObjectName, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            UiIntroOverlay ov = go.AddComponent<UiIntroOverlay>();
            ov.Cfg = Main.Settings; ov.Lat = layout.Pack.Lat; ov.Lon = layout.Pack.Lon;
            ov.OutX = px; ov.OutY = py; ov.FlagSprite = flag; ov.WorldMapPath = map; ov.ScreenName = screen.GetType().Name;
            sOverlay = go;
            try { ov.Build(parent, index); }
            catch (Exception)
            {
                UnityEngine.Object.Destroy(go);
                sOverlay = null;
                throw;
            }
            LogHost(screen, parent, index);
        }

        /// <summary>
        /// Where to put the overlay: in the canvas that holds the screen's widgets, as low as possible: below the first child that
        /// holds a text, the flag or the championship logo, and not above the screen's `movie` object if it has one.
        /// </summary>
        private static void ChooseHost(MovieLocationScreen screen, out Transform parent, out int index)
        {
            parent = null; index = 0;
            Transform host = null;
            if (screen.movie != null && screen.movie.transform.parent is RectTransform && screen.movie.transform.parent.GetComponentInParent<Canvas>() != null)
                host = screen.movie.transform.parent;
            if (host == null)
            {
                Canvas[] cs = screen.GetComponentsInChildren<Canvas>(true);
                if (cs != null && cs.Length > 0) host = cs[0].transform;
            }
            if (host == null) return;
            parent = host;
            int first = host.childCount;
            for (int i = 0; i < host.childCount; i++)
            {
                Transform c = host.GetChild(i);
                if (c.name == UiIntroOverlay.ObjectName) continue;
                if (HoldsLabel(c)) { first = i; break; }
            }
            index = first;
            if (screen.movie != null && screen.movie.transform.parent == host) index = Math.Min(index, screen.movie.transform.GetSiblingIndex() + 1);
        }

        private static bool HoldsLabel(Transform t)
        {
            Graphic[] gs = t.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < gs.Length; i++)
                if (gs[i].GetType().Name.StartsWith("TextMeshPro", StringComparison.Ordinal)) return true;
            return t.GetComponentInChildren<Flag>(true) != null || t.GetComponentInChildren<UIChampionshipLogo>(true) != null;
        }

        private static void LogHost(MovieLocationScreen screen, Transform parent, int index)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < parent.childCount && i < 14; i++) { if (i > 0) sb.Append(", "); sb.Append(i).Append(':').Append(parent.GetChild(i).name); }
            Canvas c = parent.GetComponentInParent<Canvas>();
            Main.Log("RT UI: intro overlay hosted in '" + parent.name + "' (canvas " + (c != null ? c.name + " " + c.renderMode + " order " + c.sortingOrder : "none") +
                     ") at child index " + index + " of " + parent.childCount + "; movie object " + (screen.movie != null ? "'" + screen.movie.name + "'" : "none") +
                     "; children: [" + sb + "]");
        }
    }

    // ------------------------------------------------------------------------------------------------------
    // Harmony: MovieLocationScreen.OnEnter / OnExit (MovieLocationScreen.cs:17, :31; both public overrides declared there).
    // OnEnter has already queued and started the movie (BaseMovieScreen.OnEnter -> PlayNextMovieInQueue), so the postfix only
    // adds the picture. Stock circuits return before anything is created.
    // ------------------------------------------------------------------------------------------------------
    [HarmonyPatch(typeof(MovieLocationScreen), "OnEnter")]
    public static class Patch_IntroEnter
    {
        public static void Postfix(MovieLocationScreen __instance)
        {
            try { UiIntro.Show(__instance); }
            catch (Exception e) { UiIntro.Remove(); Main.Logger.Error("Real Tracks: intro overlay failed, the donor's movie plays as it is: " + e); RtFallbacks.Note("intro overlay", "failed, the donor's movie plays as it is: " + e.GetType().Name + " " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(MovieLocationScreen), "OnExit")]
    public static class Patch_IntroExit
    {
        public static void Postfix()
        {
            try { UiIntro.Remove(); }
            catch (Exception e) { Main.Logger.Error("Real Tracks: intro overlay removal failed: " + e); }
        }
    }
}
