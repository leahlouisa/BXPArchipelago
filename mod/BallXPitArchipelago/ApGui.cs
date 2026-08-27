using System.Collections.Generic;
using UnityEngine;

namespace BallXPitArchipelago;

/// <summary>
/// The in-game connect screen: host/port/slot/password fields and a Connect button,
/// drawn every frame via Unity's legacy IMGUI (OnGUI). Collapses to a small status box
/// once connected. This is the primary way to connect - ApConfig only persists the last
/// values used so returning players don't have to retype them.
///
/// IL2CPP note: adding a custom MonoBehaviour to an IL2CPP game requires registering the
/// type with Il2CppInterop first (ClassInjector.RegisterTypeInIl2Cpp&lt;ApGui&gt;() in
/// Mod.OnInitializeMelon) and this class needs the IntPtr constructor below.
///
/// Text input note: UnityEngine.GUI.TextField / GUILayout.TextField throw
/// "System.NotSupportedException: Method unstripping failed" on this game's build - its
/// native IMGUI TextEditor internals were stripped at build time since the game itself
/// never uses legacy IMGUI text entry anywhere. Confirmed live (all 4 fields threw every
/// frame). Worked around with a hand-rolled field (DrawField below) that captures
/// mouse/keyboard events manually instead of using the built-in control.
/// </summary>
public class ApGui : MonoBehaviour
{
    public ApGui(System.IntPtr ptr) : base(ptr)
    {
    }

    private static string _host = "archipelago.gg";
    private static string _port = "38281";
    private static string _slot = "";
    private static string _password = "";
    private static string _status = "";
    private static string _focusedField;

    // Vanilla has no reusable "you unlocked something" popup - the closest things
    // (ItemUnlockUI, HarvestSummaryUI) are each tied to a specific screen and driven by
    // internal state this mod has no safe way to fabricate. This toast stack is a
    // purpose-built substitute instead: it works from any screen, at any time, for
    // anything ItemReceiver applies (not just things vanilla would normally announce).
    private const float ToastDurationSecs = 10f;
    private static readonly List<ToastEntry> _toasts = new();

    private struct ToastEntry
    {
        public string Text;
        public float ExpireAt;
    }

    internal static void ShowToast(string text)
    {
        _toasts.Add(new ToastEntry { Text = text, ExpireAt = Time.time + ToastDurationSecs });
    }

    internal static void Init(ApConfig config)
    {
        _host = config.Host;
        _port = config.Port.ToString();
        _slot = config.Slot;
        _password = config.Password ?? "";
    }

    private void OnGUI()
    {
        _toasts.RemoveAll(t => Time.time >= t.ExpireAt);
        if (_toasts.Count > 0)
            DrawToasts();

        var connected = ApConnection.Session != null;

        GUILayout.BeginArea(new Rect(10, 10, 260, 300));
        GUILayout.BeginVertical("box");
        GUILayout.Label("Ball X Pit Archipelago");

        if (connected)
        {
            GUILayout.Label($"Connected as {_slot}");
            if (GUILayout.Button("Disconnect"))
            {
                ApConnection.Disconnect();
                _status = "Disconnected.";
            }
        }
        else
        {
            _host = DrawField("host", "Host", _host);
            _port = DrawField("port", "Port", _port);
            _slot = DrawField("slot", "Slot Name", _slot);
            _password = DrawField("password", "Password (optional)", _password, isPassword: true);

            if (GUILayout.Button("Connect"))
                TryConnect();
        }

        if (!string.IsNullOrEmpty(_status))
            GUILayout.Label(_status);

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    private static Texture2D _toastBg;
    private static GUIStyle _toastStyle;

    private static void DrawToasts()
    {
        // Top-center rather than a corner: gameplay is bullet-hell-adjacent and the
        // player's eyes are on the middle of the screen, not the edges - a corner toast is
        // easy to miss entirely in peripheral vision during actual combat.
        if (_toastBg == null)
        {
            _toastBg = new Texture2D(1, 1);
            _toastBg.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.85f));
            _toastBg.Apply();
        }

        if (_toastStyle == null)
        {
            _toastStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _toastBg, textColor = Color.yellow },
                fontSize = 40,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(24, 24, 14, 14),
            };
        }

        const float width = 700;
        GUILayout.BeginArea(new Rect((Screen.width - width) / 2f, 60, width, Screen.height - 80));
        GUILayout.BeginVertical();
        foreach (var toast in _toasts)
        {
            GUILayout.Label(toast.Text, _toastStyle);
            GUILayout.Space(6);
        }
        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    private static string DrawField(string id, string label, string value, bool isPassword = false)
    {
        GUILayout.Label(label);

        var focused = _focusedField == id;
        var shown = isPassword ? new string('*', value.Length) : value;
        GUILayout.Box((focused ? "> " : "  ") + shown + (focused ? " <" : ""),
            GUILayout.Height(22), GUILayout.ExpandWidth(true));

        var boxRect = GUILayoutUtility.GetLastRect();
        var e = Event.current;

        if (e.type == EventType.MouseDown)
        {
            if (boxRect.Contains(e.mousePosition))
            {
                _focusedField = id;
                e.Use();
            }
            else if (focused)
            {
                _focusedField = null;
            }
        }

        if (focused && e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.Backspace)
            {
                if (value.Length > 0)
                    value = value[..^1];
            }
            else if (e.keyCode is KeyCode.Return or KeyCode.KeypadEnter or KeyCode.Tab or KeyCode.Escape)
            {
                _focusedField = null;
            }
            else if (!char.IsControl(e.character))
            {
                value += e.character;
            }

            e.Use();
        }

        return value;
    }

    private static void TryConnect()
    {
        if (!int.TryParse(_port, out var port))
        {
            _status = "Port must be a number.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_slot))
        {
            _status = "Slot name is required.";
            return;
        }

        var config = new ApConfig
        {
            Host = _host,
            Port = port,
            Slot = _slot,
            Password = string.IsNullOrEmpty(_password) ? null : _password,
        };

        var error = ApConnection.Connect(config, LocationHooks.Log);
        if (error != null)
        {
            _status = error;
            return;
        }

        _status = "Connected!";
        config.Save();
    }
}
