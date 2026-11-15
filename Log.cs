using System;
using System.IO;

namespace QuickMedia;

/// <summary>%AppData%\QuickMedia\log.txt — hata ayıklama günlüğü.</summary>
public static class Log
{
    private static readonly object Gate = new();
    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickMedia");
    public static string FilePath => Path.Combine(Dir, "log.txt");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > 400_000) File.Delete(FilePath);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
