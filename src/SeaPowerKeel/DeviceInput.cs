using System;
using System.Reflection;
using UnityEngine;

namespace SeaPowerKeel;

/// <summary>
/// The keyboard and mouse read straight from the device. The globe build does not reliably hand keys and clicks to IMGUI, so the
/// hotkey did nothing and the report's buttons did not click; Keel reads them itself, through the Input System package where the
/// game has it, else the old Input class. Both found by name, so Keel references neither. IMGUI's own handling stays as it is.
/// </summary>
internal abstract class DeviceInput
{
    public enum Modifier { Ctrl, Shift, Alt }

    /// <summary>Is the key held now?</summary>
    public abstract bool Held(KeyCode key);

    public abstract bool Held(Modifier modifier);

    /// <summary>The left button pressed this frame, the pointer (y from the bottom), Esc pressed this frame.</summary>
    public abstract (bool click, Vector2 pos, bool esc) Pointer();

    /// <summary>The device reader this game has, or null when it has neither.</summary>
    public static DeviceInput Probe()
    {
        try
        {
            if (InputSystem.Find() is DeviceInput found)
                return found;
        }
        catch (Exception)
        {
            // no Input System package: try the old input
        }
        try
        {
            return OldInput.Find();
        }
        catch (Exception)
        {
            return null;            // old input switched off
        }
    }

    private const BindingFlags Instance = BindingFlags.Public | BindingFlags.Instance;
    private const BindingFlags Static = BindingFlags.Public | BindingFlags.Static;

    /// <summary>UnityEngine.InputSystem: Keyboard.current and Mouse.current, their controls by property name.</summary>
    private sealed class InputSystem : DeviceInput
    {
        private PropertyInfo keyboard, mouse;

        public static InputSystem Find()
        {
            PropertyInfo kb = Type.GetType("UnityEngine.InputSystem.Keyboard, Unity.InputSystem")?.GetProperty("current", Static);
            PropertyInfo m = Type.GetType("UnityEngine.InputSystem.Mouse, Unity.InputSystem")?.GetProperty("current", Static);
            return kb != null && m != null ? new InputSystem { keyboard = kb, mouse = m } : null;
        }

        private static object Get(object o, string name) => o?.GetType().GetProperty(name, Instance)?.GetValue(o);

        private static bool Flag(object device, string control, string state) => Get(Get(device, control), state) is bool b && b;

        // KeyCode.K -> "kKey", the Keyboard's property for it.
        public override bool Held(KeyCode key)
        {
            string name = key.ToString();
            return Flag(keyboard.GetValue(null), char.ToLowerInvariant(name[0]) + name.Substring(1) + "Key", "isPressed");
        }

        public override bool Held(Modifier modifier) =>
            Flag(keyboard.GetValue(null), modifier.ToString().ToLowerInvariant() + "Key", "isPressed");

        public override (bool click, Vector2 pos, bool esc) Pointer()
        {
            object m = mouse.GetValue(null);
            object position = Get(m, "position");
            Vector2 pos = position?.GetType().GetMethod("ReadValue", Type.EmptyTypes)?.Invoke(position, null) is Vector2 v ? v : default;
            return (Flag(m, "leftButton", "wasPressedThisFrame"), pos, Flag(keyboard.GetValue(null), "escapeKey", "wasPressedThisFrame"));
        }
    }

    /// <summary>UnityEngine.Input, which throws on every call when the game has switched the old input off.</summary>
    private sealed class OldInput : DeviceInput
    {
        private MethodInfo getKey, keyDown, mouseDown;
        private PropertyInfo mousePosition;

        public static OldInput Find()
        {
            Type input = Type.GetType("UnityEngine.Input, UnityEngine.InputLegacyModule");
            var found = new OldInput
            {
                getKey = input?.GetMethod("GetKey", new[] { typeof(KeyCode) }),
                keyDown = input?.GetMethod("GetKeyDown", new[] { typeof(KeyCode) }),
                mouseDown = input?.GetMethod("GetMouseButtonDown", new[] { typeof(int) }),
                mousePosition = input?.GetProperty("mousePosition", Static),
            };
            if (found.getKey == null || found.keyDown == null || found.mouseDown == null || found.mousePosition == null)
                return null;
            found.Held(KeyCode.Escape);      // throws right away when the old input is switched off
            return found;
        }

        public override bool Held(KeyCode key) => (bool)getKey.Invoke(null, new object[] { key });

        public override bool Held(Modifier modifier) => modifier switch
        {
            Modifier.Ctrl => Held(KeyCode.LeftControl) || Held(KeyCode.RightControl),
            Modifier.Shift => Held(KeyCode.LeftShift) || Held(KeyCode.RightShift),
            _ => Held(KeyCode.LeftAlt) || Held(KeyCode.RightAlt),
        };

        public override (bool click, Vector2 pos, bool esc) Pointer() =>
            ((bool)mouseDown.Invoke(null, new object[] { 0 }), (Vector3)mousePosition.GetValue(null), (bool)keyDown.Invoke(null, new object[] { KeyCode.Escape }));
    }
}
