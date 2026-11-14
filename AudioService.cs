using System;
using System.Runtime.InteropServices;

namespace QuickMedia;

/// <summary>Sistem ana ses seviyesi / sessize alma (Core Audio).</summary>
public static class AudioService
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

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr notify);
        int UnregisterControlChangeNotify(IntPtr notify);
        int GetChannelCount(out uint count);
        int SetMasterVolumeLevel(float level, ref Guid ctx);
        int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        int GetMasterVolumeLevel(out float level);
        int GetMasterVolumeLevelScalar(out float level);
        int SetChannelVolumeLevel(uint ch, float level, ref Guid ctx);
        int SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
        int GetChannelVolumeLevel(uint ch, out float level);
        int GetChannelVolumeLevelScalar(uint ch, out float level);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    private static IAudioEndpointVolume? _ep;

    private static IAudioEndpointVolume? Get()
    {
        if (_ep != null) return _ep;
        try
        {
            var en = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            en.GetDefaultAudioEndpoint(0, 1, out var dev); // eRender, eMultimedia
            var iid = typeof(IAudioEndpointVolume).GUID;
            dev.Activate(ref iid, 23, IntPtr.Zero, out object o);
            _ep = (IAudioEndpointVolume)o;
        }
        catch { _ep = null; }
        return _ep;
    }

    private static T Try<T>(Func<IAudioEndpointVolume, T> f, T fallback)
    {
        var ep = Get();
        if (ep == null) return fallback;
        try { return f(ep); }
        catch { _ep = null; return fallback; } // cihaz değişmiş olabilir
    }

    public static bool GetMute() => Try(ep => { ep.GetMute(out bool m); return m; }, false);
    public static float GetVolume() => Try(ep => { ep.GetMasterVolumeLevelScalar(out float v); return v; }, 0f);

    public static void SetMute(bool mute) => Try(ep => { var g = Guid.Empty; ep.SetMute(mute, ref g); return 0; }, 0);
    public static void ToggleMute() => SetMute(!GetMute());

    public static void SetVolume(float v) => Try(ep =>
    {
        var g = Guid.Empty;
        ep.SetMasterVolumeLevelScalar(Math.Clamp(v, 0f, 1f), ref g);
        return 0;
    }, 0);
}
