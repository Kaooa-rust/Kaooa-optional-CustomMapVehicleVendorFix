using System;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Custom Map Vehicle Vendor Fix", "Pinkstink", "1.1.1")]
    [Description("Links vehicle vendors with spawners and repairable helipads on custom maps Updated by Pe7erS")]
    public class CustomMapVehicleVendorFix : RustPlugin
    {
        const float VendorSearchRadius = 25f;
        const float PadSearchRadius = 25f;

        void OnServerInitialized()
        {
            var vehicleSpawners = UnityEngine.Object.FindObjectsOfType<VehicleSpawner>();
            var vehicleVendors = UnityEngine.Object.FindObjectsOfType<VehicleVendor>();

            LinkRepairPads(vehicleSpawners);

            int linkedCount = 0;
            foreach (var vehicleVendor in vehicleVendors)
            {
                if (!IsLive(vehicleVendor))
                    continue;

                var currentSpawner = vehicleVendor.GetVehicleSpawner();
                var vehicleSpawner = currentSpawner;
                if (!IsLive(vehicleSpawner))
                    vehicleSpawner = vehicleVendor.vehicleSpawner;

                if (!IsLive(vehicleSpawner))
                {
                    vehicleSpawner = null;
                    float closestDistanceSquared = VendorSearchRadius * VendorSearchRadius;
                    foreach (var candidate in vehicleSpawners)
                    {
                        if (!IsLive(candidate))
                            continue;
                        float distanceSquared = (candidate.transform.position - vehicleVendor.transform.position).sqrMagnitude;
                        if (distanceSquared > VendorSearchRadius * VendorSearchRadius)
                            continue;
                        if (vehicleSpawner == null || distanceSquared < closestDistanceSquared)
                        {
                            vehicleSpawner = candidate;
                            closestDistanceSquared = distanceSquared;
                        }
                    }
                }

                if (!IsLive(vehicleSpawner))
                {
                    PrintWarning($"No Vehicle Spawner within {VendorSearchRadius}m of Vendor @ {vehicleVendor.transform.position}");
                    continue;
                }
                if (currentSpawner == vehicleSpawner && vehicleVendor.vehicleSpawner == vehicleSpawner)
                    continue;

                vehicleVendor.spawnerRef.Set(vehicleSpawner);
                vehicleVendor.vehicleSpawner = vehicleSpawner;
                vehicleVendor.InvalidateNetworkCache();
                linkedCount++;
                Puts($"Set Vehicle Spawner for Vendor @ {vehicleVendor.transform.position}: {vehicleSpawner.ShortPrefabName} @ {vehicleSpawner.transform.position}");
            }
            Puts($"Linked {linkedCount} vehicle vendors");
        }

        static bool IsLive(BaseNetworkable entity)
        {
            return entity != null && !entity.IsDestroyed && entity.net != null && entity.isServer;
        }

        void LinkRepairPads(VehicleSpawner[] vehicleSpawners)
        {
            int checkedCount = 0;
            int linkedCount = 0;
            foreach (var vehicleSpawner in vehicleSpawners)
            {
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
                        pad = vehicleSpawner.repairableVehiclePad;
                    if (!IsLive(pad))
                        pad = FindClosestPad(vehicleSpawner.transform.position);

                    if (!IsLive(pad))
                    {
                        PrintWarning($"No Airwolf repair pad within {PadSearchRadius}m of spawner @ {vehicleSpawner.transform.position}");
                        continue;
                    }

                    if (currentPad != pad || vehicleSpawner.repairableVehiclePad != pad)
                    {
                        vehicleSpawner.repairableVehiclePadRef.Set(pad);
                        vehicleSpawner.repairableVehiclePad = pad;
                        vehicleSpawner.InvalidateNetworkCache();
                        linkedCount++;
                    }

                    Puts($"Airwolf @ {vehicleSpawner.transform.position} -> pad @ {pad.transform.position}: " +
                         $"repaired={pad.IsRepaired}, usable={vehicleSpawner.IsPadUsable()}, " +
                         $"repairsRequired={ConVar.vehicle.padrepairsrequired}");
                }
                catch (Exception ex)
                {
                    PrintError($"Failed to link repair pad for Spawner @ {vehicleSpawner.transform.position}: {ex.Message}");
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
