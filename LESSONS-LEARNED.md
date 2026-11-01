# Real Tracks 1.0.0 — continuation lessons learned

Date: 2026-11-01

## Scope and evidence

This retrospective covers the continuation from the Monaco iteration-2 handoff through versions 0.2.7–0.2.12, promoted here as 1.0.0. It supplements rather than replaces `outputs/lessons-learned.md`, which records the earlier reverse-engineering, Austin M1/M1.1, and flywheel work.

Evidence used for this review:

- the iteration-2 handoff and the existing M1 lessons document;
- Monaco user playtests 2–6, their screenshots, and the two supplied lap/pit recordings;
- the game’s `RT WATER`, `RT ROAD EDGE`, and `RT ART` logs;
- clean compiles, staged package manifests, and post-install checksum verification.

## What worked

### 1. The user playtest was the correct visual oracle

The original Monaco run was structurally sound but did not look believable at the camera angles the game actually uses. The useful feedback was concrete: floating water, a rectangular loading flag, cars beyond rendered road edges, lower-slope hairpin barriers, noisy roof palettes, exposed horizon wedges, a tall generic pit building, COTA-style overrun paint, and cars visually passing through a wall. Each item mapped to a visual outcome the player could observe in seconds.

The productive cadence was: preserve the evidence, identify whether it was simulation, geometry, material, or camera-dependent, make the smallest isolated change, compile/package/checksum, and return a testable build. That avoided speculative large reworks and kept Austin stable.

### 2. Runtime observability made subjective reports diagnosable

Three pieces of instrumentation made the continuation materially stronger:

- The water verifier established which generated surface was at the authored sea level and exposed the donor-material distinction.
- The road-edge monitor measured a rendered vehicle envelope against authored road width without altering driving behavior. It confirmed that the early waterfront and hairpin observations were real visual mismatches rather than only camera illusion.
- Stage logs identified exact art choices: building-material counts, pit dimensions, skirt configuration, and the source/length of hairpin wall runs.

The lesson is not that logs replace screenshots; it is that screenshots say *what looked wrong*, while logs say *which component actually produced it*. Both were required.

### 3. Separate simulation from presentation explicitly

The continuation repeatedly encountered a tempting but incorrect shortcut: treat visible scenery as if it were the game’s collision system. Generated terrain, buildings, kerbs, barriers, water, and run-off are presentation meshes. The generated road on the `Env_Track` layer is what supports vehicles. Consequently:

- a barrier that appears down a slope can be a source-placement issue even if cars drive plausibly;
- adding a prettier barrier does not automatically make it collidable;
- a car can look as if it crosses a wall when the wall sits inside the visible car envelope;
- widening the visual/drivable compatibility shoulder and leaving a correctly sized apron can solve the player-facing defect without asserting a physics change.

This distinction should be written into every future track acceptance plan before building art.

### 4. Pack-specific art needs pack-specific defaults

The first generic art pass made technical sense but exposed an aesthetic failure: Monaco inherited a countryside horizon, tall generic pit façade, and Austin-like red/white/blue fallback run-off. Those were not bad assets in isolation; they were bad *for Monaco*. The resulting approach is a good pattern:

- preserve reusable base systems;
- add narrow pack checks only where the reference demands it;
- keep each override default-on but independently switchable in UMM;
- log the active special treatment at build time.

This kept Austin’s accepted appearance unchanged while letting Monaco converge toward its own dense coastal/street-circuit identity.

### 5. Package integrity and safe installation became routine

Every shipped continuation build used the same release discipline: build with the local compatible compiler, stage from a known-good package, update manifest checksums, verify every manifest entry, verify the zip, check that the game is closed, back up the installed DLL and `Info.json`, install, then verify the installed manifest. This was deliberately repetitive, and it prevented a bad package or a locked DLL from becoming a hidden variable in visual testing.

The supporting principle remains: never solve a modding problem by deleting or overwriting broadly in the Steam installation. Each release has a named rollback pair.

## What did not work at first

### 1. A technically corrected water mesh did not guarantee a corrected harbour image

The initial sea-level fix logged an authored water surface at the correct height, while the user still saw water far above the yachts. The active animated donor surface—not the generated sea mesh—was the visual culprit. The corrective lesson: when a rendering symptom persists despite a correct measurement, inventory every potentially visible renderer/material and make the fallback/experimental path unmistakable in the UI and logs.

### 2. The first hairpin wall fix trusted donor barrier geometry

The imported barrier followed a lower terrain contour instead of the generated road edge. The initial road-edge replacement still used donor tiles, so its source geometry could remain visually displaced. The final behavior bypasses donor tiling for this special run and emits concrete strips from the current calculated road edges. The reusable lesson: for a location-specific safety boundary, derive it from the live road model, not an unrelated donor asset or raw imported contour.

### 3. "Road edge" was too narrow a definition for visual correctness

The 1.5 m compatibility shoulder made the large road-excursion problem much better, but a 0.55 m gap beyond it still allowed a car body to overlap the rendered hairpin wall. The final 2.1 m car-envelope clearance treats the road as what the player sees around a moving vehicle, not only the centreline’s mathematical boundary. Future monitors and barrier rules should model the car body from the first implementation, not retrofit it afterward.

### 4. Hub cameras expose different defects from race cameras

The race view concealed the horizon skirt; the pre-race hub showed its full extent as hard beige/blue wedges. Likewise, an aerial view made saturated roof fields and repeated blocks much more obvious than a trackside camera. A venue needs an explicit camera matrix: loading card, pre-race hub, setup view, high aerial race camera, close race camera, harbour/water view, pit approach, and any landmark view. Do not infer hub quality from a race-only capture.

### 5. Generic visual language drifted between venues

The fallback red/white/blue run-off is correct for Austin but wrong for Monaco. The lesson is to name generic defaults honestly and test them against the next track before assuming they are universally neutral. Material families should have a declared intended context (for example, `austin_paint`, `monaco_tarmac`, or generic) rather than a generic name that silently carries a prior venue’s identity.

## Release-process improvements for the next tracks

1. Start every new track with a one-page visual reference matrix: palette, roof language, road-edge/kerb style, run-off type, water/horizon type, pits, barriers, landmarks, and camera angles.
2. Add an automated `RT ART` summary that explicitly names every pack-specific override and whether it is active, so a log immediately distinguishes a generic fallback from an intended local style.
3. Capture the hub/setup view and a dense-traffic corner in every automatic art tour. These caught the horizon and car-envelope defects that a scenic flyover missed.
4. Keep visual collision acceptance separate: either test actual collision physics through the game’s road/AI systems, or test visual clearance using the vehicle envelope. Never write a pass statement that implies one proves the other.
5. Keep the current release discipline: source version, clean compile, staged manifest, zip checksum, game-closed check, backups, installed checksum, then player test.
6. Promote a venue only after user validation of the visual matrix, not merely after a stack of source-level checks. For 1.0, Austin has established approval and Monaco has iterative approval of the latest visual direction; its remaining limitations are documented rather than hidden.

## Durable conclusion

The main continuation lesson is that a real-track mod is an integration product, not simply a road mesh. It must reconcile game physics, camera framing, donor-scene artifacts, procedural geometry, materials, and the player’s real-world memory of a place. The reliable method was to make each layer observable, change only the layer that owned the defect, retain a reversible package trail, and let the player’s in-game eye make the final call.
