using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace QuickMedia;

internal static class Native
{
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr o);

    [StructLayout(LayoutKind.Sequential)]
    private struct DWM_BLURBEHIND
    {
        public uint dwFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fEnable;
        public IntPtr hRgnBlur;
        [MarshalAs(UnmanagedType.Bool)] public bool fTransitionOnMaximized;
    }
    [DllImport("dwmapi.dll")] private static extern int DwmEnableBlurBehindWindow(IntPtr hwnd, ref DWM_BLURBEHIND bb);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int size);
    [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);


    // ---- Düşük seviye klavye kancası (RegisterHotKey başarısız olursa yedek) ----
    internal delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc cb, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] private static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    /// <summary>Alt tuşu tek başına bırakılınca uygulamaların menü çubuğunu açmasını engeller.</summary>
    internal static void CancelMenu()
    {
        keybd_event(0xE8, 0, 0, UIntPtr.Zero);
        keybd_event(0xE8, 0, 2, UIntPtr.Zero);
    }

    public static void SetDarkTitleBar(IntPtr hwnd, bool dark)
    {
        int v = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref v, sizeof(int)); // DWMWA_USE_IMMERSIVE_DARK_MODE
    }

    /// <summary>Kartın etrafındaki yuvarlatılmış bölgeye masaüstü bulanıklığı uygular.</summary>
    public static void SetBlur(IntPtr hwnd, bool enable, int l, int t, int r, int b, int diameter)
    {
        var bb = new DWM_BLURBEHIND { dwFlags = 1, fEnable = enable }; // DWM_BB_ENABLE
        IntPtr rgn = IntPtr.Zero;
        if (enable)
        {
            rgn = CreateRoundRectRgn(l, t, r + 1, b + 1, diameter, diameter);
            bb.dwFlags |= 2; // DWM_BB_BLURREGION
            bb.hRgnBlur = rgn;
        }
        DwmEnableBlurBehindWindow(hwnd, ref bb);
        if (rgn != IntPtr.Zero) DeleteObject(rgn);
    }

    /// <summary>Tam ekran uygulama / oyun / sunum çalışıyor mu?</summary>
    public static bool IsFullscreenBusy()
    {
        try
        {
            if (SHQueryUserNotificationState(out int st) != 0) return false;
            return st == 2 || st == 3 || st == 4; // BUSY, D3D_FULL_SCREEN, PRESENTATION_MODE
        }
        catch { return false; }
    }

    private static readonly HashSet<string> Browsers = new()
    {
        "chrome", "msedge", "firefox", "opera", "brave", "vivaldi", "arc", "zen",
        "iexplore", "chromium", "waterfox", "librewolf", "yandex"
    };

    /// <summary>Görünür tarayıcı pencerelerinin başlıkları (üstteki pencere önce).</summary>
    public static List<string> GetBrowserWindowTitles()
    {
        var list = new List<string>();
        var cache = new Dictionary<uint, bool>();
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            int len = GetWindowTextLength(h);
            if (len == 0) return true;
            GetWindowThreadProcessId(h, out uint pid);
            if (!cache.TryGetValue(pid, out bool isBrowser))
            {
                try { isBrowser = Browsers.Contains(Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant()); }
                catch { isBrowser = false; }
                cache[pid] = isBrowser;
            }
            if (!isBrowser) return true;
            var sb = new StringBuilder(len + 1);
            GetWindowText(h, sb, sb.Capacity);
            list.Add(sb.ToString());
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
