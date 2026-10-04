// Minimal game substitutes for exercising the production fuel state and icon selection.
// These tests do not validate Unity rendering, ship physics, or network transport.
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace BepInEx.Configuration
{
    public sealed class ConfigEntry<T>
    {
        public T Value;
        public ConfigEntry(T value) { Value = value; }
    }
    public struct KeyboardShortcut { public bool IsDown() => false; }
}

namespace UnityEngine
{
    public class Object { public static void Destroy(Object value) { } }
    public class Component : Object
    {
        public GameObject gameObject = null!;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
        public T GetComponentInChildren<T>() where T : Component => gameObject.GetComponent<T>();
    }
    public class MonoBehaviour : Component { }
    public sealed class GameObject : Object
    {
        private readonly Dictionary<Type, Component> _components = new();
        public bool activeSelf;
        public GameObject(string name, params Type[] components)
        {
            foreach (Type type in components) { Add(type); }
        }
        private Component Add(Type type)
        {
            Component component = (Component)Activator.CreateInstance(type, true)!;
            component.gameObject = this;
            _components[type] = component;
            return component;
        }
        public T AddComponent<T>() where T : Component => (T)Add(typeof(T));
        public T GetComponent<T>() where T : Component => _components.TryGetValue(typeof(T), out Component? value) ? (T)value : null!;
        public void SetActive(bool active) { activeSelf = active; }
    }
    public sealed class RectTransform : Component
    {
        public Vector2 anchoredPosition;
        public Vector2 sizeDelta;
        public void SetParent(RectTransform parent, bool worldPositionStays) { }
    }
    public sealed class CanvasRenderer : Component { }
    public readonly struct Vector2
    {
        public Vector2(float x, float y) { }
        public static Vector2 zero => new(0, 0);
    }
    public readonly struct Color
    {
        public readonly float r, g, b, a;
        public Color(float red, float green, float blue, float alpha) { r = red; g = green; b = blue; a = alpha; }
        public static Color white => new(1, 1, 1, 1);
    }
    public sealed class Sprite : Object { }
    public static class Mathf { public static float Max(float a, float b) => Math.Max(a, b); }
    public static class Time { public static float time; }
}

namespace UnityEngine.UI
{
    public sealed class Image : Component
    {
        public Sprite? sprite;
        public Color color;
        public bool raycastTarget;
        public RectTransform rectTransform => GetComponent<RectTransform>()!;
    }
}

public sealed class ZDO
{
    private readonly Dictionary<string, object> _values = new();
    public long Owner = 1;
    public long GetOwner() => Owner;
    public bool GetBool(string key, bool fallback) => _values.TryGetValue(key, out object? value) ? (bool)value : fallback;
    public float GetFloat(string key, float fallback) => _values.TryGetValue(key, out object? value) ? (float)value : fallback;
    public void Set(string key, bool value) { _values[key] = value; }
    public void Set(string key, float value) { _values[key] = value; }
}
public sealed class ZNetView : Component
{
    public ZDO Data = new();
    public bool Owner = true;
    public bool Valid = true;
    public bool IsValid() => Valid;
    public bool IsOwner() => Owner;
    public ZDO GetZDO() => Data;
    public void InvokeRPC(string name, long playerId) { }
}
public sealed class Ship : Component
{
    public enum Speed { Stop, Back, Slow, Half, Full }
    public readonly ShipControls m_shipControlls = new();
}
public sealed class ShipControls { public long GetUser() => 1; }
public sealed class Inventory
{
    public readonly Dictionary<string, int> Items = new();
    public int Count(string name) => Items.TryGetValue(name, out int count) ? count : 0;
    public bool HaveItem(string name, bool worldLevelBased) => Count(name) > 0;
    public void RemoveItem(string name, int amount, int quality, bool worldLevelBased) { Items[name] = Count(name) - amount; }
}
public sealed class Container : Component
{
    public readonly Inventory Inventory = new();
    public Inventory GetInventory() => Inventory;
}
public sealed class ItemDrop : Component
{
    public readonly ItemData m_itemData = new();
    public sealed class ItemData
    {
        public readonly SharedData m_shared = new();
        public readonly Sprite Icon = new();
        public Sprite GetIcon() => Icon;
    }
    public sealed class SharedData { public string m_name = ""; }
}
public sealed class ObjectDB
{
    public static ObjectDB? instance;
    public readonly Dictionary<string, GameObject> Items = new();
    public GameObject? GetItemPrefab(string name) => Items.TryGetValue(name, out GameObject? item) ? item : null;
}
public sealed class Player
{
    public static Player? m_localPlayer;
    public Ship? ControlledShip;
    public Ship? GetControlledShip() => ControlledShip;
    public bool TakeInput() => true;
    public long GetPlayerID() => 1;
    public void Message(MessageHud.MessageType type, string text) { }
}
public sealed class MessageHud { public enum MessageType { Center } }
public sealed class Hud
{
    public static Hud? m_instance;
    public readonly RectTransform m_shipWindIndicatorRoot = new();
}
namespace PraetorisClient
{
    internal static class PraetorisClientPlugin
    {
        internal static ConfigEntry<bool> SurtlingBoatsEnabled = new(true);
        internal static ConfigEntry<KeyboardShortcut> SurtlingBoatToggleKey = new(new());
        internal static ConfigEntry<bool> SurtlingBoatFreeFuel = new(false);
        internal static ConfigEntry<string> SurtlingBoatFuelItemPrefab = new("SurtlingCore");
        internal static ConfigEntry<float> SurtlingBoatSecondsPerFuelItem = new(300f);
        internal static ConfigEntry<float> SurtlingBoatBackBoost = new(1.5f);
        internal static ConfigEntry<float> SurtlingBoatSlowBoost = new(1.02f);
        internal static ConfigEntry<float> SurtlingBoatHalfBoost = new(1.2f);
        internal static ConfigEntry<float> SurtlingBoatFullBoost = new(1.5f);
        internal static ConfigEntry<string> SurtlingBoatMoltenFuelItemPrefab = new("MoltenCore");
        internal static ConfigEntry<float> SurtlingBoatMoltenSecondsPerFuelItem = new(300f);
        internal static ConfigEntry<float> SurtlingBoatMoltenBackBoost = new(2f);
        internal static ConfigEntry<float> SurtlingBoatMoltenSlowBoost = new(1.02f);
        internal static ConfigEntry<float> SurtlingBoatMoltenHalfBoost = new(1.4f);
        internal static ConfigEntry<float> SurtlingBoatMoltenFullBoost = new(2f);
    }
}
