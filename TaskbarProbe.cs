using System;
using System.Collections.Generic;
using System.Windows.Automation;

namespace QuickMedia;

/// <summary>
/// Windows 11 görev çubuğundaki düğmelerin (Başlat, Ara, uygulamalar, Widgets, sistem tepsisi) yerlerini
/// UI Automation ile okuyup, soldaki boş alanı (piksel) bulur. Arka plan iş parçacığında çağrılmalıdır.
/// </summary>
internal static class TaskbarProbe
{
    /// <returns>(başlangıç, bitiş) piksel x değerleri ya da bulunamazsa null.</returns>
    public static Tuple<int, int>? FindFreeArea(Native.RECT tb)
    {
        try
        {
            var hwnd = Native.FindTaskbarWindow();
            if (hwnd == IntPtr.Zero) return null;

            var root = AutomationElement.FromHandle(hwnd);
            var buttons = root.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

            var spans = new List<(int L, int R)>();
            foreach (AutomationElement e in buttons)
            {
                var r = e.Current.BoundingRectangle;
                if (r.IsEmpty || r.Width < 8 || r.Height < 8) continue;
                if (r.Top < tb.top - 4 || r.Bottom > tb.bottom + 4) continue;   // görev çubuğunun dışında / gizli
                spans.Add(((int)r.Left, (int)r.Right));
            }
            if (spans.Count == 0) return null;

            spans.Sort((a, b) => a.L.CompareTo(b.L));
            int center = (tb.left + tb.right) / 2;
            int cursor = tb.left, best = 0, bestStart = 0, bestEnd = 0;
            foreach (var (l, r) in spans)
            {
                // ekranın sol yarısındaki en geniş boşluk
                if (cursor < center && l - cursor > best) { best = l - cursor; bestStart = cursor; bestEnd = l; }
                cursor = Math.Max(cursor, r);
            }
            return best >= 100 ? Tuple.Create(bestStart, bestEnd) : null;
        }
        catch { return null; }
    }
}
