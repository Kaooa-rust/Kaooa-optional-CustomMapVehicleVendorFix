// Minimal stand-ins for external Rust/Unity APIs. These do not verify server compatibility.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public class Object
    {
        public static readonly List<Object> Scene = new List<Object>();
        public static int SceneSearches;
        private readonly int instanceId;
        public Object() { instanceId = Scene.Count + 1; Scene.Add(this); }
        public int GetInstanceID() { return instanceId; }
        public static T[] FindObjectsOfType<T>() where T : Object
        {
            SceneSearches++;
            return Scene.OfType<T>().OrderBy(x => x.GetInstanceID()).ToArray();
        }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y = 0, float z = 0) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public float sqrMagnitude { get { return x * x + y * y + z * z; } }
    }

    public class Transform
    {
        public static int PositionReads;
        private Vector3 value;
        public Vector3 position { get { PositionReads++; return value; } set { this.value = value; } }
    }
}

public class EntityRegistry : IEnumerable<BaseNetworkable>
{
    public readonly List<BaseNetworkable> Items = new List<BaseNetworkable>();
    public int Passes;
    public IEnumerator<BaseNetworkable> GetEnumerator() { Passes++; return Items.GetEnumerator(); }
    IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
}

public class BaseNetworkable : UnityEngine.Object
{
    public static EntityRegistry serverEntities = new EntityRegistry();
    public bool IsDestroyed;
    public object net = new object();
    public bool isServer = true;
    public string ShortPrefabName = "entity";
    public readonly UnityEngine.Transform transform = new UnityEngine.Transform();
    public int CacheInvalidations;
    public BaseNetworkable() { serverEntities.Items.Add(this); }
    public void InvalidateNetworkCache() { CacheInvalidations++; }
}

public struct EntityRef<T> where T : BaseNetworkable
{
    private T value;
    public T Get(bool server = true) { return value; }
    public void Set(T entity) { value = entity; }
}

public class VehicleVendor : BaseNetworkable
{
    public EntityRef<VehicleSpawner> spawnerRef;
    public VehicleSpawner vehicleSpawner;
    public bool FailGetSpawner;
    public VehicleSpawner GetVehicleSpawner()
    {
        if (FailGetSpawner) throw new InvalidOperationException("Simulated vendor failure");
        return spawnerRef.Get();
    }
}

public class VehicleSpawner : BaseNetworkable
{
    public enum VehicleSpawnerType { Boat, Helicopter }
    public VehicleSpawnerType spawnerType;
    public EntityRef<RepairableVehiclePad> repairableVehiclePadRef;
    public RepairableVehiclePad repairableVehiclePad;
    public bool IsPadUsable() { return repairableVehiclePad != null && repairableVehiclePad.IsRepaired; }
}

public class RepairableVehiclePad : BaseNetworkable
{
    public static List<RepairableVehiclePad> server_RepairableVehiclePads = new List<RepairableVehiclePad>();
    public bool IsRepaired = true;
    public RepairableVehiclePad() { ShortPrefabName = "airwolf_helipad.repairable"; server_RepairableVehiclePads.Add(this); }
}

namespace Facepunch
{
    public static class Pool
    {
        private static readonly Dictionary<Type, Stack<object>> Available = new Dictionary<Type, Stack<object>>();
        public static int Outstanding;
        public static T Get<T>() where T : class, new()
        {
            Outstanding++;
            Stack<object> items;
            return Available.TryGetValue(typeof(T), out items) && items.Count > 0 ? (T)items.Pop() : new T();
        }
        public static void FreeUnmanaged<T>(ref List<T> list)
        {
            list.Clear();
            Stack<object> items;
            if (!Available.TryGetValue(typeof(List<T>), out items)) Available[typeof(List<T>)] = items = new Stack<object>();
            items.Push(list);
            list = null;
            Outstanding--;
        }
    }
}

namespace ConVar { public static class vehicle { public static bool padrepairsrequired = true; } }

namespace Oxide.Plugins
{
    [AttributeUsage(AttributeTargets.Class)]
    public class InfoAttribute : Attribute { public InfoAttribute(string name, string author, string version) { } }
    [AttributeUsage(AttributeTargets.Class)]
    public class DescriptionAttribute : Attribute { public DescriptionAttribute(string description) { } }
    public class RustPlugin
    {
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Errors = new List<string>();
        public void Puts(string message) { }
        public void PrintWarning(string message) { Warnings.Add(message); }
        public void PrintError(string message) { Errors.Add(message); }
    }
}
