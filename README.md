# Custom Map Vehicle Vendor Fix

Oxide plugin for Rust custom maps where vehicle vendors, spawners, and repairable Airwolf helipads are missing their links.

On server startup or plugin reload, it connects vendors to the nearest spawner and helicopter spawners to the nearest repairable Airwolf pad. Both searches are limited to **25 metres**. Existing live links are kept. Missing matches are reported in the server console.

## Install

Copy `CustomMapVehicleVendorFix.cs` into your server's `oxide/plugins` folder.

Requires a current version of Oxide with its publicizer enabled (the default).

## Search distance

To adjust the ranges, edit these values near the top of the plugin:

```csharp
const float VendorSearchRadius = 25f;
const float PadSearchRadius = 25f;
```

Keep them close to your map's actual placement distances to avoid linking unrelated entities.

Checked with a local test harness; still needs testing on a Rust server.

Based on [Pinkstink's original plugin](https://github.com/features-not-bugs/Rust-CustomMapVehicleVendorFix). MIT licensed.
