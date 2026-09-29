# Custom Map Vehicle Vendor Fix

Local working copy of the Rust Oxide plugin from
[rusthb/Rust-CustomMapVehicleVendorFix](https://github.com/rusthb/Rust-CustomMapVehicleVendorFix),
originally by Pinkstink. Current local plugin version: **1.1.2**.

## Behavior

- Runs once during server initialization, including plugin reload.
- Links vendors to the nearest live spawner within **25 metres**.
- Links helicopter spawners to the nearest live `airwolf_helipad.repairable` within **25 metres**.
- Uses three-dimensional distance and includes entities exactly on the 25m boundary.
- Keeps existing live references, even if they are outside the discovery radius, and repairs missing paired references.
- Reports missing matches without linking a more distant entity.
- Uses direct Oxide field access; the default Oxide publicizer is required for non-public game fields.

Only `CustomMapVehicleVendorFix.cs` belongs in the server's `oxide/plugins` directory.
The files in `tests` are local development fixtures, not server plugins.

## Local change history

Commit `0d6e933` records the working copy before the performance changes. It already includes the earlier agreed changes:

- Both discovery radii reduced from 100m to 25m.
- Reflection replaced with direct `repairableVehiclePadRef` access.
- The stray period inside the namespace removed.

Version 1.1.2 changes startup work:

- Collects live vendors and spawners in one pass over `BaseNetworkable.serverEntities`.
- Replaces two scene-search arrays with pooled lists, returned in `finally` even after an exception.
- Reads each candidate spawner's position once and each searching vendor's position once.
- Skips vendors whose two existing references already agree.
- Preserves the original lowest-instance-ID choice when vendor candidates are equally near, without sorting every candidate list.

Inspect the history or compare the plugin with its baseline:

```powershell
git log --oneline
git diff 0d6e933 HEAD -- CustomMapVehicleVendorFix.cs
git show --stat HEAD
```

The local baseline and optimization commits are preserved alongside the upstream history.
The publication branch is `codex/vendor-linking-cleanup` in
[rusthb/Rust-CustomMapVehicleVendorFix](https://github.com/rusthb/Rust-CustomMapVehicleVendorFix/tree/codex/vendor-linking-cleanup).

## Verification

Run in PowerShell 7; no downloaded test packages are required:

```powershell
pwsh -NoProfile -File .\tests\run.ps1
pwsh -NoProfile -File .\tests\run.ps1 -Performance
```

The harness compiles the actual plugin with minimal Rust/Unity stand-ins. The eight behavior groups cover nearest selection, 3D radius boundaries, existing references, field fallbacks, reloads, invalid entities, tied candidates, empty worlds, missing pad registries, and pool cleanup after an exception.

The 32-vendor / 32-spawner fixture produces these operation counts:

| Operation | Baseline | Version 1.1.2 |
| --- | ---: | ---: |
| Scene searches | 2 | 0 |
| Server-entity registry passes | 0 | 1 |
| Position reads | 2,112 | 96 |

These are deterministic operation counts, not timings or a measured server speedup. Matching still compares each unresolved vendor against the collected spawners. Pad discovery still uses Rust's repair-pad registry. This optimization affects startup/reload work; the plugin has no recurring update loop.

A broader synthetic fixture with 32 vendor/spawner/helipad sets reduced position reads from 3,232 to 1,152 (64.4%). Timed runs did not establish an overall speedup: for example, 8 sets plus 100,000 unrelated stand-in entities took about 0.69ms with the baseline and 1.37ms with version 1.1.2. These were medians of nine alternating batches with warmed pools and links reset before each run. The harness substitutes managed LINQ for Unity scene searches, plain C# position getters for native transforms, and no-op logging, so those timings cannot predict Rust server performance. They show why fewer position reads should not be presented as a measured startup-speed improvement. No ongoing server FPS gain is expected from this startup-only change.

The fixtures do not reproduce Unity object lifetime semantics, verify the installed Rust assemblies, or measure the cost of scanning a real server's entity registry. Compilation against the server's actual Oxide/Rust assemblies and an in-game reload remain to be checked.

API references: [Oxide pooling](https://docs.oxidemod.com/guides/developers/pooling),
[Unity scene-search ordering](https://docs.unity3d.com/ScriptReference/Object.FindObjectsOfType.html),
[Oxide publicizer defaults](https://github.com/OxideMod/Oxide.Core/blob/develop/src/Configuration/OxideConfig.cs).
