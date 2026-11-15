using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace QuickMedia;

public enum HotkeyAction { Toggle, PlayPause, Next, Previous, VolumeUp, VolumeDown, OpenSettings }

/// <summary>None: kaydedilemedi · Native: RegisterHotKey ile · Compat: klavye kancasıyla (yedek mod)</summary>
public enum HotkeyMode { None, Native, Compat }

/// <summary>
/// Küresel kısayollar. Mesajlar hiç gösterilmeyen (yalnızca handle'ı oluşturulan) gizli bir pencereye gelir;
/// RegisterHotKey başarısız olursa (başka uygulama kullanıyor vb.) düşük seviye klavye kancasına geçilir.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int BaseId = 0x514D;
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly Window _host;          // asla Show() edilmez
    private readonly IntPtr _hwnd;
    private readonly HwndSource? _src;
    private readonly Dictionary<HotkeyAction, HotkeyMode> _modes = new();
    private readonly Dictionary<HotkeyAction, (ModifierKeys Mods, Key Key)> _hook = new();
    private readonly HashSet<int> _down = new();
    private readonly Native.LowLevelProc _proc;
    private IntPtr _hookHandle;

    public event Action<HotkeyAction>? Pressed;
    public int LastError { get; private set; }

    public HotkeyService()
    {
        _host = new Window
        {
            WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false,
            Width = 0, Height = 0, ResizeMode = ResizeMode.NoResize,
        };
        _hwnd = new WindowInteropHelper(_host).EnsureHandle();   // pencere oluşur ama görünmez
        _src = HwndSource.FromHwnd(_hwnd);
        _src?.AddHook(WndProc);
        _proc = HookProc;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            int idx = wParam.ToInt32() - BaseId;
            if (Enum.IsDefined(typeof(HotkeyAction), idx))
            {
                handled = true;
                var a = (HotkeyAction)idx;
                Log.Write("Kısayol tetiklendi: " + a);
                Pressed?.Invoke(a);
            }
        }
        return IntPtr.Zero;
    }

    // ---------- Kayıt ----------

    public HotkeyMode Register(HotkeyAction a, ModifierKeys mods, Key key)
    {
        Unregister(a);
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        string desc = $"{a}: {Describe(mods, key)}";

        if (Native.RegisterHotKey(_hwnd, BaseId + (int)a, (uint)mods | MOD_NOREPEAT, vk))
        {
            _modes[a] = HotkeyMode.Native;
            Log.Write("Kısayol kaydedildi (normal) — " + desc);
            return HotkeyMode.Native;
        }

        LastError = Marshal.GetLastWin32Error();
        if (EnsureHook())
        {
            _hook[a] = (mods, key);
            _modes[a] = HotkeyMode.Compat;
            Log.Write($"Kısayol kaydedildi (uyumluluk modu, RegisterHotKey hata {LastError}) — {desc}");
            return HotkeyMode.Compat;
        }

        _modes[a] = HotkeyMode.None;
        Log.Write($"Kısayol kaydedilemedi (hata {LastError}) — {desc}");
        return HotkeyMode.None;
    }

    public void Unregister(HotkeyAction a)
    {
        Native.UnregisterHotKey(_hwnd, BaseId + (int)a);
        _hook.Remove(a);
        _modes[a] = HotkeyMode.None;
        if (_hook.Count == 0) RemoveHook();
    }

    public void UnregisterAll()
    {
        foreach (HotkeyAction a in Enum.GetValues(typeof(HotkeyAction))) Unregister(a);
    }

    public HotkeyMode ModeOf(HotkeyAction a) => _modes.TryGetValue(a, out var m) ? m : HotkeyMode.None;

    /// <summary>Ayarlardaki tüm kısayolları kaydeder; kaydedilemeyenlerin sayısını döndürür.</summary>
    public int RegisterAllFromSettings()
    {
        int failed = 0;
        foreach (HotkeyAction a in Enum.GetValues(typeof(HotkeyAction)))
        {
            var (m, k) = Parse(App.Settings.GetHotkey(a));
            if (Register(a, m, k) == HotkeyMode.None) failed++;
        }
        return failed;
    }

    // ---------- Yedek: düşük seviye klavye kancası ----------

    private bool EnsureHook()
    {
        if (_hookHandle != IntPtr.Zero) return true;
        _hookHandle = Native.SetWindowsHookEx(13 /*WH_KEYBOARD_LL*/, _proc, Native.GetModuleHandle(null), 0);
        return _hookHandle != IntPtr.Zero;
    }

    private void RemoveHook()
    {
        if (_hookHandle == IntPtr.Zero) return;
        Native.UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
        _down.Clear();
    }

    private static ModifierKeys CurrentMods()
    {
        var m = ModifierKeys.None;
        if ((Native.GetAsyncKeyState(0x12) & 0x8000) != 0) m |= ModifierKeys.Alt;
        if ((Native.GetAsyncKeyState(0x11) & 0x8000) != 0) m |= ModifierKeys.Control;
        if ((Native.GetAsyncKeyState(0x10) & 0x8000) != 0) m |= ModifierKeys.Shift;
        if ((Native.GetAsyncKeyState(0x5B) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x5C) & 0x8000) != 0) m |= ModifierKeys.Windows;
        return m;
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && _hook.Count > 0)
            {
                int msg = wParam.ToInt32();
                int vk = Marshal.ReadInt32(lParam, 0);
                bool injected = (Marshal.ReadInt32(lParam, 8) & 0x10) != 0;

                if (!injected)
                {
                    if (msg == 0x100 || msg == 0x104)            // WM_KEYDOWN / WM_SYSKEYDOWN
                    {
                        var mods = CurrentMods();
                        foreach (var kv in _hook)
                        {
                            if (KeyInterop.VirtualKeyFromKey(kv.Value.Key) != vk || kv.Value.Mods != mods) continue;
                            if (_down.Add(vk))                    // tuş tekrarını yoksay
                            {
                                var action = kv.Key;
                                bool alt = mods.HasFlag(ModifierKeys.Alt);
                                Application.Current.Dispatcher.BeginInvoke(() =>
                                {
                                    if (alt) Native.CancelMenu();
                                    Log.Write("Kısayol tetiklendi (uyumluluk): " + action);
                                    Pressed?.Invoke(action);
                                });
                            }
                            return (IntPtr)1;                     // tuşu uygulamaya iletme
                        }
                    }
                    else if (msg == 0x101 || msg == 0x105)        // KEYUP
                    {
                        _down.Remove(vk);
                    }
                }
            }
        }
        catch { }
        return Native.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    // ---------- Metin / ayrıştırma ----------

    public static (ModifierKeys, Key) Parse(HotkeyBinding b)
    {
        var mods = (ModifierKeys)b.Modifiers;
        if (!Enum.TryParse(b.Key, out Key key)) key = Key.P;
        if (mods == ModifierKeys.None) mods = ModifierKeys.Alt;
        return (mods, key);
    }

    public static string Describe(ModifierKeys mods, Key key)
    {
        var parts = new List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        string k = key switch
        {
            Key.Right => "→", Key.Left => "←", Key.Up => "↑", Key.Down => "↓", Key.Space => "Boşluk",
            _ => key.ToString()
        };
        if (k.Length == 2 && k[0] == 'D' && char.IsDigit(k[1])) k = k[1].ToString();
        parts.Add(k);
        return string.Join(" + ", parts);
    }

    public static string Describe(HotkeyBinding b)
    {
        var (m, k) = Parse(b);
        return Describe(m, k);
    }

    public static string Describe(HotkeyAction a) => Describe(App.Settings.GetHotkey(a));

    public void Dispose()
    {
        UnregisterAll();
        RemoveHook();
        try { _src?.RemoveHook(WndProc); _host.Close(); } catch { }
    }
}
