using System;
using System.Collections.Generic;
using Facepunch;
using Newtonsoft.Json;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Custom Map Vehicle Vendor Fix", "Pinkstink/kaooa_fork", "1.2.0")]
    [Description("Links vehicle vendors with spawners and repairable helipads on custom maps Updated by Pe7erS")]
    public class CustomMapVehicleVendorFix : RustPlugin
    {
        const float VendorSearchRadius = 25f;
        const float PadSearchRadius = 25f;

        const string AirwolfPadPrefab = "assets/prefabs/npc/bandit/airwolf_helipad.repairable.prefab";
        const float PadStageScanInterval = 60f;

        Timer padStageTimer;
        bool advanceWarned;

        class PluginConfig
        {
            [JsonProperty("Automatically repair every repairable helipad across the entire map once the first helipad has been fully repaired?")]
            public bool RepairAllPads = false;

            [JsonProperty("Automatically repair nearby repairable helipads within a x radius whenever a helipad is fully repaired. This only affects helipads within the specified radius, not every helipad across the map.")]
            public float RepairRadius = 25f;
        }

        PluginConfig config;

        protected override void LoadDefaultConfig()
        {
            config = new PluginConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<PluginConfig>();
                if (config == null)
                    throw new Exception("empty config");
            }
            catch
            {
                PrintWarning("Config missing or invalid, creating default");
                LoadDefaultConfig();
            }
            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config, true);
        }

        struct SpawnerCandidate
        {
            public readonly VehicleSpawner Entity;
            public readonly Vector3 Position;

            public SpawnerCandidate(VehicleSpawner entity)
            {
                Entity = entity;
                Position = entity.transform.position;
            }
        }

        void OnServerInitialized()
        {
            var vehicleSpawners = Pool.Get<List<SpawnerCandidate>>();
            var vehicleVendors = Pool.Get<List<VehicleVendor>>();

            try
            {
                // Only server entities can be linked. Gather both types in one pass.
                foreach (var entity in BaseNetworkable.serverEntities)
                {
                    var spawner = entity as VehicleSpawner;
                    if (IsLive(spawner))
                        vehicleSpawners.Add(new SpawnerCandidate(spawner));
                    else
                    {
                        var vendor = entity as VehicleVendor;
                        if (IsLive(vendor))
                            vehicleVendors.Add(vendor);
                    }
                }

                LinkRepairPads(vehicleSpawners);
                LinkVendors(vehicleVendors, vehicleSpawners);
            }
            finally
            {
                Pool.FreeUnmanaged(ref vehicleVendors);
                Pool.FreeUnmanaged(ref vehicleSpawners);
            }

            padStageTimer?.Destroy();
            padStageTimer = timer.Every(PadStageScanInterval, SyncAirwolfPadStages);
            SyncAirwolfPadStages();
        }

        void Unload()
        {
            padStageTimer?.Destroy();
            padStageTimer = null;
        }

        void LinkVendors(List<VehicleVendor> vehicleVendors, List<SpawnerCandidate> vehicleSpawners)
        {
            int linkedCount = 0;
            foreach (var vehicleVendor in vehicleVendors)
            {
                if (!IsLive(vehicleVendor))
                    continue;

                var currentSpawner = vehicleVendor.GetVehicleSpawner();
                var vehicleSpawner = currentSpawner;
                if (!IsLive(vehicleSpawner))
                    vehicleSpawner = vehicleVendor.vehicleSpawner;

                if (IsLive(vehicleSpawner) && currentSpawner == vehicleSpawner && vehicleVendor.vehicleSpawner == vehicleSpawner)
                    continue;

                var vendorPosition = vehicleVendor.transform.position;
                if (!IsLive(vehicleSpawner))
                {
                    vehicleSpawner = null;
                    float closestDistanceSquared = VendorSearchRadius * VendorSearchRadius;
                    foreach (var candidate in vehicleSpawners)
                    {
                        if (!IsLive(candidate.Entity))
                            continue;
                        float distanceSquared = (candidate.Position - vendorPosition).sqrMagnitude;
                        if (distanceSquared > closestDistanceSquared)
                            continue;
                        // Preserve the old scene search's instance-ID tie ordering without sorting.
                        if (vehicleSpawner == null || distanceSquared < closestDistanceSquared ||
                            (distanceSquared == closestDistanceSquared && candidate.Entity.GetInstanceID() < vehicleSpawner.GetInstanceID()))
                        {
                            vehicleSpawner = candidate.Entity;
                            closestDistanceSquared = distanceSquared;
                        }
                    }
                }

                if (!IsLive(vehicleSpawner))
                {
                    PrintWarning($"No Vehicle Spawner within {VendorSearchRadius}m of Vendor @ {vendorPosition}");
                    continue;
                }

                vehicleVendor.spawnerRef.Set(vehicleSpawner);
                vehicleVendor.vehicleSpawner = vehicleSpawner;
                vehicleVendor.InvalidateNetworkCache();
                linkedCount++;
                Puts($"Set Vehicle Spawner for Vendor @ {vendorPosition}: {vehicleSpawner.ShortPrefabName} @ {vehicleSpawner.transform.position}");
            }
            Puts($"Linked {linkedCount} vehicle vendors");
        }

        static bool IsLive(BaseNetworkable entity)
        {
            return entity != null && !entity.IsDestroyed && entity.net != null && entity.isServer;
        }

        void LinkRepairPads(List<SpawnerCandidate> vehicleSpawners)
        {
            int checkedCount = 0;
            int linkedCount = 0;
            foreach (var candidate in vehicleSpawners)
            {
                var vehicleSpawner = candidate.Entity;
                if (!IsLive(vehicleSpawner))
                    continue;

                if (vehicleSpawner.spawnerType != VehicleSpawner.VehicleSpawnerType.Helicopter &&
                    vehicleSpawner.ShortPrefabName != "airwolfspawner")
                    continue;

                checkedCount++;
                try
                {
                    var currentPad = vehicleSpawner.repairableVehiclePadRef.Get(true);
                    var pad = currentPad;
                    if (!IsLive(pad))
                        pad = FindClosestPad(candidate.Position);

                    if (!IsLive(pad))
                    {
                        PrintWarning($"No Airwolf repair pad within {PadSearchRadius}m of spawner @ {candidate.Position}");
                        continue;
                    }

                    if (currentPad != pad)
                    {
                        vehicleSpawner.repairableVehiclePadRef.Set(pad);
                        vehicleSpawner.InvalidateNetworkCache();
                        linkedCount++;
                    }

                    Puts($"Airwolf @ {candidate.Position} -> pad @ {pad.transform.position}: " +
                         $"repaired={pad.IsRepaired}, usable={vehicleSpawner.IsPadUsable()}, " +
                         $"repairsRequired={ConVar.vehicle.padrepairsrequired}");
                }
                catch (Exception ex)
                {
                    PrintError($"Failed to link repair pad for Spawner @ {candidate.Position}: {ex.Message}");
                }
            }
            Puts($"Checked {checkedCount} helicopter spawners, linked {linkedCount} repair pads");
        }

        // Runs at startup, every PadStageScanInterval seconds, and on demand. Two independent options:
        //   RepairAllPads = true : once any Airwolf pad is fully repaired, every un-repaired pad on the map is repaired.
        //   RepairRadius  > 0    : every un-repaired pad within that radius of a fully repaired pad is repaired.
        // With RepairAllPads false and RepairRadius 0, pads are left exactly as they are.
        void SyncAirwolfPadStages()
        {
            if (config == null || (!config.RepairAllPads && config.RepairRadius <= 0f))
                return;
            if (RepairableVehiclePad.server_RepairableVehiclePads == null)
                return;

            var pads = Pool.Get<List<RepairableVehiclePad>>();
            try
            {
                foreach (var pad in RepairableVehiclePad.server_RepairableVehiclePads)
                {
                    if (IsLive(pad) && pad.PrefabName == AirwolfPadPrefab)
                        pads.Add(pad);
                }

                var toRepair = new HashSet<RepairableVehiclePad>();
                if (config.RepairAllPads)
                {
                    bool anyRepaired = false;
                    foreach (var pad in pads)
                    {
                        if (IsFinalStage(pad))
                        {
                            anyRepaired = true;
                            break;
                        }
                    }

                    if (anyRepaired)
                    {
                        foreach (var pad in pads)
                        {
                            if (!IsFinalStage(pad))
                                toRepair.Add(pad);
                        }
                    }
                }
                else
                {
                    float radiusSquared = config.RepairRadius * config.RepairRadius;

                    // Only pads that are repaired at the start of this run act as anchors.
                    foreach (var anchor in pads)
                    {
                        if (!IsFinalStage(anchor))
                            continue;

                        var anchorPosition = anchor.transform.position;
                        foreach (var other in pads)
                        {
                            if (other == anchor || IsFinalStage(other))
                                continue;
                            if ((other.transform.position - anchorPosition).sqrMagnitude <= radiusSquared)
                                toRepair.Add(other);
                        }
                    }
                }

                int advanced = 0;
                foreach (var pad in toRepair)
                {
                    if (AdvanceToFinalStage(pad))
                        advanced++;
                }

                if (advanced > 0)
                    Puts($"Advanced {advanced} Airwolf pad(s) to the repaired stage");
            }
            finally
            {
                Pool.FreeUnmanaged(ref pads);
            }
        }

        // Final stage = repaired (tarpaulin visuals). Confirmed from airwolf.dumppad output:
        // repaired pad -> IsRepaired=True, flags=Reserved1, currentState=3, "Repaired" child active;
        // unrepaired pad -> IsRepaired=False, flags=0, currentState=0, "Damaged" child active.
        static bool IsFinalStage(RepairableVehiclePad pad)
        {
            return pad.IsRepaired;
        }

        // Moves a pad to the repaired stage using the pad's own methods. They are invoked through reflection
        // (bool argument) because their parameter types weren't visible in the dump; the result is verified.
        bool AdvanceToFinalStage(RepairableVehiclePad pad)
        {
            foreach (var methodName in new[] { "ForceRepairedState", "SetRepaired" })
            {
                var method = typeof(RepairableVehiclePad).GetMethod(methodName,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
                if (method == null)
                    continue;

                var parameters = method.GetParameters();
                if (parameters.Length != 1 || parameters[0].ParameterType != typeof(bool))
                    continue;

                try
                {
                    method.Invoke(pad, new object[] { true });
                }
                catch (Exception ex)
                {
                    PrintError($"{methodName} failed on pad {pad.net.ID}: {ex.InnerException?.Message ?? ex.Message}");
                    continue;
                }

                if (pad.IsRepaired)
                {
                    pad.SendNetworkUpdate();
                    Puts($"Pad {pad.net.ID} @ {pad.transform.position} advanced to final stage via {methodName}");
                    return true;
                }
            }

            if (!advanceWarned)
            {
                advanceWarned = true;
                PrintWarning("Could not advance pad to final stage: ForceRepairedState/SetRepaired(bool) unavailable or had no effect. " +
                             "Run 'airwolf.dumppad' and share the output for the method signatures.");
            }
            return false;
        }

        // Admin command: runs the pad sync immediately instead of waiting for the 60 second timer.
        // (Does nothing while repair-all is false and the repair radius is 0.)
        [ConsoleCommand("airwolf.syncpads")]
        void CmdSyncPads(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !arg.IsAdmin)
                return;

            if (!config.RepairAllPads && config.RepairRadius <= 0f)
            {
                arg.ReplyWith("Repair-all is false and the repair radius is 0; pads left as they are.");
                return;
            }

            SyncAirwolfPadStages();
            arg.ReplyWith("Airwolf pad sync executed");
        }

        // Admin command: airwolf.repairall <true|false>  - sets (and saves) the config option.
        // true: once any pad is fully repaired, every un-repaired Airwolf pad on the map is repaired (checked now and every scan); false: off.
        [ConsoleCommand("airwolf.repairall")]
        void CmdRepairAll(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !arg.IsAdmin)
                return;

            if (!arg.HasArgs(1))
            {
                arg.ReplyWith($"airwolf.repairall is currently {config.RepairAllPads}. Usage: airwolf.repairall <true|false>");
                return;
            }

            config.RepairAllPads = arg.GetBool(0);
            SaveConfig();
            arg.ReplyWith($"airwolf.repairall set to {config.RepairAllPads}");

            if (config.RepairAllPads)
                SyncAirwolfPadStages();
        }

        // Admin command (in-game F1 console): airwolf.showradius [seconds]
        // Draws the repair radius around every fully repaired Airwolf pad (green sphere) and marks
        // un-repaired pads (red marker). Default 30 seconds.
        [ConsoleCommand("airwolf.showradius")]
        void CmdShowRadius(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
            {
                arg.ReplyWith("This command must be run in-game (it draws in your view)");
                return;
            }
            if (!player.IsAdmin)
                return;

            if (RepairableVehiclePad.server_RepairableVehiclePads == null)
            {
                arg.ReplyWith("No repairable pads list available");
                return;
            }

            float duration = Mathf.Clamp(arg.GetFloat(0, 30f), 1f, 300f);
            float radius = config.RepairRadius;
            int repaired = 0, unrepaired = 0;

            foreach (var pad in RepairableVehiclePad.server_RepairableVehiclePads)
            {
                if (!IsLive(pad) || pad.PrefabName != AirwolfPadPrefab)
                    continue;

                var position = pad.transform.position;
                if (IsFinalStage(pad))
                {
                    repaired++;
                    if (radius > 0f)
                        player.SendConsoleCommand("ddraw.sphere", duration, Color.green, position, radius);
                    player.SendConsoleCommand("ddraw.text", duration, Color.green, position + Vector3.up * 2f,
                        $"Repaired pad {pad.net.ID} (radius {(radius > 0f ? radius + "m" : "disabled")})");
                }
                else
                {
                    unrepaired++;
                    player.SendConsoleCommand("ddraw.sphere", duration, Color.red, position, 1f);
                    player.SendConsoleCommand("ddraw.text", duration, Color.red, position + Vector3.up * 2f,
                        $"Un-repaired pad {pad.net.ID}");
                }
            }

            arg.ReplyWith($"Showing {repaired} repaired / {unrepaired} un-repaired pad(s) for {duration:0}s" +
                          (radius > 0f ? $", radius {radius}m" : ", radius is 0 (disabled), no sphere drawn"));
        }

        // Diagnostic: logs the state of every Airwolf pad (flags, declared fields/properties, child objects)
        // so the correct stage mechanism can be identified.
        [ConsoleCommand("airwolf.dumppad")]
        void CmdDumpPads(ConsoleSystem.Arg arg)
        {
            if (arg.Connection != null && !arg.IsAdmin)
                return;

            if (RepairableVehiclePad.server_RepairableVehiclePads == null)
            {
                arg.ReplyWith("No repairable pads list available");
                return;
            }

            const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.DeclaredOnly;

            foreach (var pad in RepairableVehiclePad.server_RepairableVehiclePads)
            {
                if (!IsLive(pad) || pad.PrefabName != AirwolfPadPrefab)
                    continue;

                Puts($"=== Pad {pad.net.ID} @ {pad.transform.position} rot {pad.transform.rotation.eulerAngles} ===");
                Puts($"flags={pad.flags}, IsRepaired={pad.IsRepaired}");

                for (var type = pad.GetType(); type != null && type != typeof(BaseCombatEntity) && type != typeof(BaseEntity); type = type.BaseType)
                {
                    foreach (var f in type.GetFields(all))
                    {
                        object value;
                        try { value = f.GetValue(pad); } catch { value = "<err>"; }
                        Puts($"[{type.Name}] field {f.FieldType.Name} {f.Name} = {value}");
                    }
                    foreach (var pr in type.GetProperties(all))
                    {
                        if (pr.GetIndexParameters().Length > 0 || !pr.CanRead)
                            continue;
                        object value;
                        try { value = pr.GetValue(pad, null); } catch { value = "<err>"; }
                        Puts($"[{type.Name}] property {pr.PropertyType.Name} {pr.Name} = {value}");
                    }
                    foreach (var m in type.GetMethods(all))
                    {
                        if (!m.IsSpecialName)
                            Puts($"[{type.Name}] method {m.Name}({m.GetParameters().Length} params)");
                    }
                }

                foreach (Transform child in pad.transform)
                    Puts($"child: {child.name} active={child.gameObject.activeSelf}");
            }
            arg.ReplyWith("Pad dump written to console/log");
        }

        static RepairableVehiclePad FindClosestPad(Vector3 position)
        {
            if (RepairableVehiclePad.server_RepairableVehiclePads == null)
                return null;

            RepairableVehiclePad closestPad = null;
            float closestDistanceSquared = PadSearchRadius * PadSearchRadius;
            foreach (var candidate in RepairableVehiclePad.server_RepairableVehiclePads)
            {
                if (!IsLive(candidate) || candidate.ShortPrefabName != "airwolf_helipad.repairable")
                    continue;

                float distanceSquared = (candidate.transform.position - position).sqrMagnitude;
                if (distanceSquared > PadSearchRadius * PadSearchRadius)
                    continue;
                if (closestPad == null || distanceSquared < closestDistanceSquared)
                {
                    closestPad = candidate;
                    closestDistanceSquared = distanceSquared;
                }
            }
            return closestPad;
        }
    }
}
