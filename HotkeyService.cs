using System;
using System.Collections.Generic;
using System.Windows.Input;
using System.Windows.Interop;

namespace QuickMedia;

public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0x514D;
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly HwndSource _src;
    public event Action? Pressed;

    public HotkeyService()
    {
        // Mesaj-only pencere (HWND_MESSAGE)
        var p = new HwndSourceParameters("QuickMediaHotkey") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 };
        _src = new HwndSource(p);
        _src.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    public bool Register(ModifierKeys mods, Key key)
    {
        Unregister();
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        // ModifierKeys: Alt=1, Control=2, Shift=4, Windows=8  → Win32 MOD_* ile aynı değerler
        return Native.RegisterHotKey(_src.Handle, HotkeyId, (uint)mods | MOD_NOREPEAT, vk);
    }

    public bool RegisterFromSettings()
    {
        var (m, k) = Parse(App.Settings);
        return Register(m, k);
    }

    public void Unregister() => Native.UnregisterHotKey(_src.Handle, HotkeyId);

    public static (ModifierKeys, Key) Parse(AppSettings s)
    {
        var mods = (ModifierKeys)s.HotkeyModifiers;
        if (!Enum.TryParse(s.HotkeyKey, out Key key)) key = Key.P;
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
        string k = key.ToString();
        if (k.Length == 2 && k[0] == 'D' && char.IsDigit(k[1])) k = k[1].ToString();
        parts.Add(k);
        return string.Join(" + ", parts);
    }

    public static string Describe(AppSettings s)
    {
        var (m, k) = Parse(s);
        return Describe(m, k);
    }

    public void Dispose()
    {
        Unregister();
        _src.Dispose();
    }
}
