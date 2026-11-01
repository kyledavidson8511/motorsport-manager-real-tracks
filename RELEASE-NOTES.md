# Real Tracks 1.0.0

Date: 2026-11-01

## Release contents

Real Tracks 1.0.0 packages two additive Quick Race venues:

| Pack | In-game venue | Release status |
| --- | --- | --- |
| `austin` | Austin / COTA GP | Established M1 venue; user-approved visual and driving result. |
| `monaco` | Monte Carlo | Iteratively user-playtested through 0.2.12; promoted with its current visual and road-compatibility settings. |

No stock circuit is replaced, and the package contains no career-save migration or save-format change.

## Final Monaco release set

- Yacht-level generated harbour water; donor animated water remains an explicit experimental opt-in.
- Closed-track loading-card flag mask.
- 1.5 m visible road compatibility shoulder, extending road, kerbs, and run-off together.
- Road-edge diagnostic logging for rendered car-envelope excursions; it does not change AI handling.
- Monaco-only warm cream/muted ochre/pale-stone city palette and restrained roof materials.
- Monaco-only level coastal horizon, low quay-garage pit frontage, and muted dark-tarmac fallback apron.
- Generated concrete walls around Grand Hotel / Turn 9, with an adjustable 2.1 m default visual car-envelope clearance.

All Monaco-specific presentation features are enabled by default but independently switchable in the Unity Mod Manager Art panel. Austin retains its own existing art treatment, including its tricolour run-off.

## Verification before release

- Full C# compile against the local .NET 3.5-compatible Motorsport Manager toolchain.
- Package manifest verification for all tracked files before install.
- Installation only after confirming Motorsport Manager was closed.
- Installed manifest verification after copy.

The release package is `outputs/RealTracks-1.0.0.zip`. The prior installed 0.2.12 version is retained as `RealTracks.dll.bak-0.2.12` and `Info.json.bak-0.2.12`.

## What 1.0 does and does not promise

This is a stable two-track release based on real user playtests, not a claim that either venue is final simulation-grade recreation. In particular, generated art meshes are visual meshes rather than vehicle-collision geometry. Monaco's Turn 9 clearance solves the visible car/wall intersection by allowing the actual rendered car envelope before the wall; collision behavior remains governed by Motorsport Manager's generated road and its own vehicle systems.
