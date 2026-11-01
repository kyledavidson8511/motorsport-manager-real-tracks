using System.Collections.Generic;
using UnityEngine;

namespace RealTracks
{
    /// <summary>
    /// Shared runtime state between the packages. Owned by the orchestrator.
    /// - WP2 (Registration) answers "is this circuit a pack circuit?"
    /// - WP3 (VenueBuilder) fills <see cref="Built"/> after building a venue.
    /// - WP4 (AutoTest) reads both for its detectors.
    /// </summary>
    public static class ActiveVenue
    {
        /// <summary>The pack layout for a circuit, or null for stock circuits.</summary>
        public static PackLayout For(Circuit c)
        {
            return c == null || c.circuitID < 9000 ? null : PackRegistry.FindLayoutByCircuitId(c.circuitID);
        }

        /// <summary>The circuit of the event being loaded or raced (null outside an event).</summary>
        public static Circuit CurrentCircuit
        {
            get
            {
                if (Game.instance == null || Game.instance.sessionManager == null) return null;
                RaceEventDetails ev = Game.instance.sessionManager.eventDetails;
                return ev != null ? ev.circuit : null;
            }
        }

        /// <summary>The pack layout of the current event, or null if the current event is a stock circuit.</summary>
        public static PackLayout CurrentLayout { get { return For(CurrentCircuit); } }

        /// <summary>Set by WP3 when it has built a pack venue into a scene; cleared when another scene loads.</summary>
        public static BuiltVenue Built;
    }

    /// <summary>What WP3 built (for WP4's detectors and WP2's minimap).</summary>
    public class BuiltVenue
    {
        public PackLayout Layout;
        public int SceneInstanceId;
        /// <summary>World-space rectangle (x/z) the minimap sprite covers, as set on CircuitScene.trackBounds.</summary>
        public Rect MinimapWorldBounds;
        /// <summary>The generated road (Env_Track layer, with MeshCollider) objects.</summary>
        public readonly List<GameObject> RoadObjects = new List<GameObject>();
        /// <summary>Counts for the log/report: e.g. "terrainChunks", "grandstands", "buildings", "trees", "donorRenderersHidden".</summary>
        public readonly Dictionary<string, int> Counts = new Dictionary<string, int>();
        public readonly List<string> Warnings = new List<string>();
    }
}
