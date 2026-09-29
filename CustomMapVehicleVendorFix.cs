using System;
using System.Collections.Generic;
using Facepunch;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Custom Map Vehicle Vendor Fix", "Pinkstink", "1.1.3")]
    [Description("Links vehicle vendors with spawners and repairable helipads on custom maps Updated by Pe7erS")]
    public class CustomMapVehicleVendorFix : RustPlugin
    {
        const float VendorSearchRadius = 25f;
        const float PadSearchRadius = 25f;

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
