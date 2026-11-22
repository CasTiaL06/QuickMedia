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

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uCallbackMessage;
        public uint uEdge;
        public RECT rc;
        public IntPtr lParam;
    }
    [DllImport("shell32.dll")] private static extern IntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);

    /// <summary>Ana görev çubuğunun piksel dikdörtgeni ve kenarı (0 sol, 1 üst, 2 sağ, 3 alt).</summary>
    internal static bool GetTaskbar(out RECT rc, out uint edge)
    {
        var d = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
        if (SHAppBarMessage(5 /*ABM_GETTASKBARPOS*/, ref d) == IntPtr.Zero) { rc = default; edge = 3; return false; }
        rc = d.rc;
        edge = d.uEdge;
        return true;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string? title);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr h, uint cmd);

    internal delegate void WinEventDelegate(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint evMin, uint evMax, IntPtr hmod, WinEventDelegate cb, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);

    /// <summary>Ön plandaki pencere değişince (başka uygulamaya tıklama vb.) geri çağrılır.</summary>
    internal static IntPtr HookForeground(WinEventDelegate cb) => SetWinEventHook(0x0003 /*EVENT_SYSTEM_FOREGROUND*/, 0x0003, IntPtr.Zero, cb, 0, 0, 0 /*OUTOFCONTEXT*/);
    internal static void Unhook(IntPtr hook) { if (hook != IntPtr.Zero) UnhookWinEvent(hook); }

    // ---- Görev çubuğuna gömme (Başlat menüsü açılınca kontrol kaybolmasın) ----
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hwnd);
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT p);

    /// <summary>Pencereyi görev çubuğunun alt penceresi yapar. Önce WS_CHILD stili, sonra SetParent (Windows belgelerindeki sıra).</summary>
    internal static bool Embed(IntPtr hwnd, IntPtr parent)
    {
        int style = GetWindowLong(hwnd, -16);
        style = (style & ~unchecked((int)0x80000000)) | 0x40000000;   // WS_POPUP kaldır, WS_CHILD ekle
        SetWindowLong(hwnd, -16, style);
        SetParent(hwnd, parent);
        return GetParent(hwnd) == parent;     // SetParent önceki üst pencereyi döndürür; başarıyı buradan anlarız
    }

    internal static void Unembed(IntPtr hwnd)
    {
        SetParent(hwnd, IntPtr.Zero);
        int style = GetWindowLong(hwnd, -16);
        style = (style & ~0x40000000) | unchecked((int)0x80000000);   // WS_CHILD kaldır, WS_POPUP ekle
        SetWindowLong(hwnd, -16, style);
    }

    /// <summary>Alt pencereyi (üst pencereye göre) konumlar ve kardeşlerinin en üstüne alır.</summary>
    internal static void PlaceChild(IntPtr hwnd, int x, int y, int w, int h) => SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h, 0x0010 | 0x0040); // NOACTIVATE | SHOWWINDOW
    internal static bool IsTopChild(IntPtr hwnd) => GetWindow(hwnd, 3 /*GW_HWNDPREV*/) == IntPtr.Zero;
    internal static void RaiseChild(IntPtr hwnd) => SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    internal static IntPtr WindowAt(int x, int y) => WindowFromPoint(new POINT { x = x, y = y });

    internal static IntPtr FindTaskbarWindow() => FindWindow("Shell_TrayWnd", null);

    /// <summary>Pencere görev çubuğunun z-sırasında üstünde mi? (Değilse yeniden öne alınmalı; her seferinde almak titreşim yapar.)</summary>
    internal static bool IsAboveTaskbar(IntPtr hwnd)
    {
        var tb = FindTaskbarWindow();
        if (tb == IntPtr.Zero || hwnd == IntPtr.Zero) return true;
        int guard = 0;
        for (var h = GetWindow(tb, 3 /*GW_HWNDPREV*/); h != IntPtr.Zero && guard < 500; h = GetWindow(h, 3), guard++)
            if (h == hwnd) return true;
        return false;
    }

    /// <summary>Pencereyi piksel koordinatlarında yerleştirir ve en üste alır (odak çalmadan).</summary>
    internal static void PlaceTopmost(IntPtr hwnd, int x, int y, int w, int h)
    {
        if (hwnd != IntPtr.Zero) SetWindowPos(hwnd, new IntPtr(-1), x, y, w, h, 0x0010); // NOACTIVATE
    }

    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    /// <summary>Pencereyi diğer "her zaman üstte" pencerelerin de önüne alır (odak çalmadan).</summary>
    internal static void KeepOnTop(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero) SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); // NOSIZE|NOMOVE|NOACTIVATE
    }

    /// <summary>Windows 11: başlık çubuğunu pencere rengiyle bütünleştirir (Windows 10'da yok sayılır).</summary>
    internal static void SetCaption(IntPtr hwnd, System.Windows.Media.Color bg, System.Windows.Media.Color text)
    {
        int cap = bg.R | (bg.G << 8) | (bg.B << 16);
        int txt = text.R | (text.G << 8) | (text.B << 16);
        int border = cap;
        DwmSetWindowAttribute(hwnd, 35, ref cap, sizeof(int));    // DWMWA_CAPTION_COLOR
        DwmSetWindowAttribute(hwnd, 36, ref txt, sizeof(int));    // DWMWA_TEXT_COLOR
        DwmSetWindowAttribute(hwnd, 34, ref border, sizeof(int)); // DWMWA_BORDER_COLOR
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

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(long pt, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder sb, int max);

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);

    /// <summary>Windows'un "tam ekran uygulama" bildirimi (oyun, sunum...). Tarayıcı tam ekran videosunu her zaman yakalamaz.</summary>
    public static bool IsNotificationBusy()
    {
        try
        {
            if (SHQueryUserNotificationState(out int st) != 0) return false;
            return st == 2 || st == 3 || st == 4; // BUSY, D3D_FULL_SCREEN, PRESENTATION_MODE
        }
        catch { return false; }
    }

    /// <summary>
    /// Ön plandaki pencere ana monitörün tamamını kaplıyor mu? Tarayıcıda (YouTube vb.) tam ekran video,
    /// F11 ve çerçevesiz oyunlar bu şekilde yakalanır. Masaüstü / görev çubuğu / kendi pencerelerimiz sayılmaz.
    /// </summary>
    public static bool ForegroundCoversMonitor()
    {
        try
        {
            var h = GetForegroundWindow();
            if (h == IntPtr.Zero) return false;

            GetWindowThreadProcessId(h, out uint pid);
            if (pid == (uint)Environment.ProcessId) return false;

            var sb = new StringBuilder(64);
            GetClassName(h, sb, sb.Capacity);
            string cls = sb.ToString();
            if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;

            if (!GetWindowRect(h, out var r)) return false;
            var mon = MonitorFromWindow(h, 2 /*MONITOR_DEFAULTTONEAREST*/);
            var primary = MonitorFromPoint(0, 1 /*MONITOR_DEFAULTTOPRIMARY*/);
            if (mon != primary) return false;   // görev çubuğu ana monitörde: başka ekrandaki tam ekran onu ilgilendirmez

            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(mon, ref mi)) return false;
            var m = mi.rcMonitor;
            return r.left <= m.left && r.top <= m.top && r.right >= m.right && r.bottom >= m.bottom;
        }
        catch { return false; }
    }

    /// <summary>Tam ekran uygulama / oyun / sunum / tam ekran video çalışıyor mu?</summary>
    public static bool IsFullscreenBusy() => IsNotificationBusy() || ForegroundCoversMonitor();

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
