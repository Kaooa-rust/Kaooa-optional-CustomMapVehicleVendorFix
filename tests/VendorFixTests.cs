using System;
using System.Reflection;
using Oxide.Plugins;
using UnityEngine;

public static class VendorFixTests
{
    private static readonly MethodInfo Initialize = typeof(CustomMapVehicleVendorFix).GetMethod("OnServerInitialized", BindingFlags.Instance | BindingFlags.NonPublic);

    private static void Reset()
    {
        Check(Facepunch.Pool.Outstanding == 0, "pooled collections leaked from previous run");
        UnityEngine.Object.Scene.Clear();
        UnityEngine.Object.SceneSearches = 0;
        BaseNetworkable.serverEntities = new EntityRegistry();
        RepairableVehiclePad.server_RepairableVehiclePads = new System.Collections.Generic.List<RepairableVehiclePad>();
        Transform.PositionReads = 0;
    }

    private static T At<T>(float x, float y = 0, float z = 0) where T : BaseNetworkable, new()
    {
        var entity = new T();
        entity.transform.position = new Vector3(x, y, z);
        return entity;
    }

    private static VehicleSpawner Helicopter(float x = 0)
    {
        var spawner = At<VehicleSpawner>(x);
        spawner.spawnerType = VehicleSpawner.VehicleSpawnerType.Helicopter;
        return spawner;
    }

    private static CustomMapVehicleVendorFix Run()
    {
        var plugin = new CustomMapVehicleVendorFix();
        Initialize.Invoke(plugin, null);
        Check(Facepunch.Pool.Outstanding == 0, "startup must return all pooled collections");
        return plugin;
    }

    private static void Check(bool condition, string reason)
    {
        if (!condition) throw new Exception(reason);
    }

    private static void VendorNearestAndBoundary()
    {
        Reset();
        var vendor = At<VehicleVendor>(0);
        At<VehicleSpawner>(24);
        var closest = At<VehicleSpawner>(3, 4);
        Run();
        Check(vendor.vehicleSpawner == closest && vendor.spawnerRef.Get() == closest, "vendor must use the nearest spawner");
        Check(vendor.CacheInvalidations == 1, "a changed vendor must invalidate its network cache");

        Reset();
        vendor = At<VehicleVendor>(0);
        var boundary = At<VehicleSpawner>(0, 25);
        Run();
        Check(vendor.vehicleSpawner == boundary, "a spawner exactly 25m away must be accepted");

        Reset();
        vendor = At<VehicleVendor>(0);
        At<VehicleSpawner>(0, 25.01f);
        var plugin = Run();
        Check(vendor.vehicleSpawner == null && plugin.Warnings.Count == 1, "a spawner beyond 25m in 3D must be rejected and reported");
    }

    private static void PadNearestAndBoundary()
    {
        Reset();
        var spawner = Helicopter();
        At<RepairableVehiclePad>(24);
        var closest = At<RepairableVehiclePad>(3, 4);
        At<RepairableVehiclePad>(1).ShortPrefabName = "unrelated_pad";
        At<RepairableVehiclePad>(2).IsDestroyed = true;
        Run();
        Check(spawner.repairableVehiclePad == closest && spawner.repairableVehiclePadRef.Get() == closest, "helipad matching must ignore wrong prefabs and destroyed pads");

        Reset();
        spawner = Helicopter();
        var boundary = At<RepairableVehiclePad>(0, 0, 25);
        Run();
        Check(spawner.repairableVehiclePad == boundary, "a pad exactly 25m away must be accepted");

        Reset();
        spawner = Helicopter();
        At<RepairableVehiclePad>(25.01f);
        var plugin = Run();
        Check(spawner.repairableVehiclePad == null && plugin.Warnings.Count == 1, "a pad beyond 25m must be rejected and reported");
    }

    private static void PreserveReferencesAndReload()
    {
        Reset();
        var vendor = At<VehicleVendor>(0);
        var existing = Helicopter(100);
        At<VehicleSpawner>(1);
        var existingPad = At<RepairableVehiclePad>(200);
        At<RepairableVehiclePad>(101);
        vendor.spawnerRef.Set(existing);
        existing.repairableVehiclePadRef.Set(existingPad);
        Run();
        Check(vendor.vehicleSpawner == existing && existing.repairableVehiclePad == existingPad, "valid existing references take priority over closer candidates and search radii");
        Run();
        Check(vendor.CacheInvalidations == 1 && existing.CacheInvalidations == 1, "reloading must not relink already consistent references");
    }

    private static void PreserveFieldFallbacks()
    {
        Reset();
        var vendor = At<VehicleVendor>(0);
        var existing = Helicopter(100);
        var pad = At<RepairableVehiclePad>(200);
        vendor.vehicleSpawner = existing;
        existing.repairableVehiclePad = pad;
        Run();
        Check(vendor.spawnerRef.Get() == existing && existing.repairableVehiclePadRef.Get() == pad, "valid direct fields must repair missing EntityRefs");
    }

    private static void InvalidEntitiesAndNonHelicopters()
    {
        Reset();
        var vendor = At<VehicleVendor>(0);
        At<VehicleSpawner>(1).IsDestroyed = true;
        At<VehicleSpawner>(2).net = null;
        At<VehicleSpawner>(3).isServer = false;
        var valid = At<VehicleSpawner>(10);
        At<RepairableVehiclePad>(10);
        var deadVendor = At<VehicleVendor>(10);
        deadVendor.IsDestroyed = true;
        Run();
        Check(vendor.vehicleSpawner == valid && deadVendor.vehicleSpawner == null, "invalid entities must be excluded");
        Check(valid.repairableVehiclePad == null, "non-helicopter spawners must not acquire a helipad");
    }

    private static void EqualDistanceKeepsOriginalOrdering()
    {
        Reset();
        var vendor = At<VehicleVendor>(0);
        var first = At<VehicleSpawner>(5);
        At<VehicleSpawner>(-5);
        BaseNetworkable.serverEntities.Items.Reverse();
        Run();
        Check(vendor.vehicleSpawner == first, "equal-distance vendor matches must retain Unity instance-ID ordering");
    }

    private static void EmptyWorldAndNoPadRegistry()
    {
        Reset();
        Run();
        var spawner = Helicopter();
        RepairableVehiclePad.server_RepairableVehiclePads = null;
        var plugin = Run();
        Check(spawner.repairableVehiclePad == null && plugin.Warnings.Count == 1, "a missing pad registry must be handled without an exception");
    }

    private static void CleanupAfterException()
    {
        Reset();
        At<VehicleVendor>(0).FailGetSpawner = true;
        bool failed = false;
        try { Run(); }
        catch (TargetInvocationException ex) { failed = ex.InnerException is InvalidOperationException; }
        Check(failed, "fixture must exercise a real startup exception");
        Check(Facepunch.Pool.Outstanding == 0, "all borrowed collections must be returned when startup throws");
        Reset();
        var vendor = At<VehicleVendor>(0);
        var spawner = At<VehicleSpawner>(1);
        Run();
        Check(vendor.vehicleSpawner == spawner, "a later startup must work after an exception and pooled-list reuse");
    }

    public static void RunAll()
    {
        Action[] tests = { VendorNearestAndBoundary, PadNearestAndBoundary, PreserveReferencesAndReload,
            PreserveFieldFallbacks, InvalidEntitiesAndNonHelicopters, EqualDistanceKeepsOriginalOrdering,
            EmptyWorldAndNoPadRegistry, CleanupAfterException };
        foreach (var test in tests) { test(); Console.WriteLine("PASS " + test.Method.Name); }
        Console.WriteLine(tests.Length + " behavior tests passed against Rust/Unity stand-ins.");
    }

    public static void RunPerformance()
    {
        Reset();
        var vendors = new VehicleVendor[32];
        var spawners = new VehicleSpawner[32];
        for (int i = 0; i < 32; i++)
        {
            vendors[i] = At<VehicleVendor>(i * 100);
            spawners[i] = At<VehicleSpawner>(i * 100 + 1);
        }
        Run();
        for (int i = 0; i < 32; i++) Check(vendors[i].vehicleSpawner == spawners[i], "performance fixture must still link every vendor correctly");
        Console.WriteLine("32 vendors / 32 spawners: scene searches=" + UnityEngine.Object.SceneSearches +
            ", registry passes=" + BaseNetworkable.serverEntities.Passes + ", position reads=" + Transform.PositionReads);
        Check(Transform.PositionReads <= 128, "position access should scale with entity count, not vendors multiplied by spawners");
        Check(UnityEngine.Object.SceneSearches == 0, "startup should use the server registry without allocating scene-search arrays");
        Check(BaseNetworkable.serverEntities.Passes == 1, "startup should collect vendor and spawner candidates in one server-registry pass");
    }
}
