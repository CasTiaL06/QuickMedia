using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace QuickMedia;

/// <summary>
/// Windows ana sesine dokunmaz; yalnızca çalan uygulamanın ses oturumunu (ses karıştırıcıdaki ayar) yönetir.
/// Tarayıcılarda tüm sekmeler tek oturumdur: tarayıcının sesi değişir.
/// </summary>
public static class AppVolume
{
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionManager2
    {
        int GetAudioSessionControl(IntPtr guid, uint flags, out IntPtr ctl);
        int GetSimpleAudioVolume(IntPtr guid, uint flags, out IntPtr vol);
        int GetSessionEnumerator(out IAudioSessionEnumerator e);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionEnumerator
    {
        int GetCount(out int count);
        int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioSessionControl2
    {
        // IAudioSessionControl (vtable sırası için gerekli)
        int GetState(out int state);
        int GetDisplayName(out IntPtr name);
        int SetDisplayName(IntPtr name, ref Guid ctx);
        int GetIconPath(out IntPtr path);
        int SetIconPath(IntPtr path, ref Guid ctx);
        int GetGroupingParam(out Guid g);
        int SetGroupingParam(ref Guid g, ref Guid ctx);
        int RegisterAudioSessionNotification(IntPtr n);
        int UnregisterAudioSessionNotification(IntPtr n);
        // IAudioSessionControl2
        int GetSessionIdentifier(out IntPtr id);
        int GetSessionInstanceIdentifier(out IntPtr id);
        int GetProcessId(out uint pid);
        int IsSystemSoundsSession();
        int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleAudioVolume
    {
        int SetMasterVolume(float level, ref Guid ctx);
        int GetMasterVolume(out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    private static readonly Dictionary<uint, string> NameCache = new();
    private static DateTime _cacheStamp = DateTime.MinValue;

    private static string ProcName(uint pid)
    {
        if ((DateTime.Now - _cacheStamp).TotalSeconds > 20) { NameCache.Clear(); _cacheStamp = DateTime.Now; }
        if (NameCache.TryGetValue(pid, out var n)) return n;
        try { n = Process.GetProcessById((int)pid).ProcessName; } catch { n = ""; }
        NameCache[pid] = n;
        return n;
    }

    private static bool Matches(string appId, string proc)
    {
        string a = appId.ToLowerInvariant(), p = proc.ToLowerInvariant();
        if (a.Length == 0 || p.Length == 0) return false;
        if (a.Contains(p)) return true;                                   // "spotify.exe" / "chrome" / "msedge" / "vlc.exe"
        if (p == "firefox" && a.Length >= 12 && a.All(Uri.IsHexDigit)) return true; // Firefox AUMID hash'tir
        if ((a.Contains("zunemusic") || a.Contains("zunevideo") || a.Contains("mediaplayer")) && (p.Contains("media") || p.Contains("zune"))) return true;
        if (a.Contains("applemusic") && p.Contains("apple")) return true;
        return false;
    }

    private static List<ISimpleAudioVolume> Find(string appId)
    {
        var list = new List<ISimpleAudioVolume>();
        if (string.IsNullOrEmpty(appId)) return list;
        try
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            en.GetDefaultAudioEndpoint(0, 1, out var dev);
            var iid = typeof(IAudioSessionManager2).GUID;
            dev.Activate(ref iid, 23, IntPtr.Zero, out object o);
            var mgr = (IAudioSessionManager2)o;
            mgr.GetSessionEnumerator(out var sessions);
            sessions.GetCount(out int n);
            for (int i = 0; i < n; i++)
            {
                sessions.GetSession(i, out object s);
                if (s is not IAudioSessionControl2 c2) continue;
                c2.GetProcessId(out uint pid);
                if (pid == 0) continue;
                if (Matches(appId, ProcName(pid)) && s is ISimpleAudioVolume sv) list.Add(sv);
            }
        }
        catch (Exception ex) { Log.Write("AppVolume: " + ex.Message); }
        return list;
    }

    // ---- Önbellekli (arka plan) okuma: COM çağrıları arayüz thread'ini bekletmesin ----
    private sealed record Snap(string Id, float Vol, bool Mute, DateTime At);
    private static volatile Snap? _cache;
    private static System.Threading.Timer? _poll;

    /// <summary>Çalan uygulamanın ses durumunu arka planda ~0,7 sn'de bir okumaya başlar.</summary>
    public static void StartPolling(Func<string> idProvider)
    {
        _poll = new System.Threading.Timer(_ =>
        {
            try
            {
                string id = idProvider();
                if (string.IsNullOrEmpty(id)) { _cache = null; return; }
                var l = Find(id);
                if (l.Count == 0) { _cache = null; return; }
                l[0].GetMasterVolume(out float v);
                l[0].GetMute(out bool m);
                _cache = new Snap(id, v, m, DateTime.Now);
            }
            catch { }
        }, null, 400, 700);
    }

    /// <summary>Önbellekteki durum (hızlı; arayüz için). Henüz okunmadıysa null.</summary>
    public static (float Volume, bool Muted)? GetState(string appId)
    {
        var c = _cache;
        if (c != null && c.Id == appId && (DateTime.Now - c.At).TotalSeconds < 4) return (c.Vol, c.Mute);
        return null;
    }

    /// <summary>Eşzamanlı (yavaş olabilir): yalnızca kısayol gibi tek seferlik işlemlerde.</summary>
    public static (float Volume, bool Muted)? GetStateNow(string appId)
    {
        var l = Find(appId);
        if (l.Count == 0) return null;
        try
        {
            l[0].GetMasterVolume(out float v);
            l[0].GetMute(out bool m);
            _cache = new Snap(appId, v, m, DateTime.Now);
            return (v, m);
        }
        catch { return null; }
    }

    // ---- Yazma: önbellek hemen güncellenir, gerçek ayar arka planda (birleştirilerek) uygulanır ----
    private static readonly object ApplyGate = new();
    private static string? _pendId;
    private static float? _pendVol;
    private static bool? _pendMute;
    private static bool _applying;

    private static void QueueApply(string id, float? vol, bool? mute)
    {
        lock (ApplyGate)
        {
            if (_pendId != id) { _pendVol = null; _pendMute = null; }
            _pendId = id;
            if (vol != null) _pendVol = vol;
            if (mute != null) _pendMute = mute;
            if (_applying) return;
            _applying = true;
        }
        System.Threading.Tasks.Task.Run(() =>
        {
            while (true)
            {
                string? pid; float? v; bool? m;
                lock (ApplyGate)
                {
                    if (_pendId == null) { _applying = false; return; }
                    pid = _pendId; v = _pendVol; m = _pendMute;
                    _pendId = null; _pendVol = null; _pendMute = null;
                }
                var g = Guid.Empty;
                foreach (var s in Find(pid))
                {
                    try
                    {
                        if (v != null) s.SetMasterVolume(Math.Clamp(v.Value, 0f, 1f), ref g);
                        if (m != null) s.SetMute(m.Value, ref g);
                    }
                    catch { }
                }
            }
        });
    }

    public static void SetVolume(string appId, float v)
    {
        v = Math.Clamp(v, 0f, 1f);
        var c = _cache;
        _cache = new Snap(appId, v, c != null && c.Id == appId && c.Mute, DateTime.Now);
        QueueApply(appId, v, null);
    }

    public static void SetMute(string appId, bool mute)
    {
        var c = _cache;
        _cache = new Snap(appId, c != null && c.Id == appId ? c.Vol : 0.5f, mute, DateTime.Now);
        QueueApply(appId, null, mute);
    }
}
