using System;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace QuickMedia;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _toggle;

    public TrayService()
    {
        _icon = new Forms.NotifyIcon { Icon = MakeIcon(), Text = "QuickMedia", Visible = true };

        var menu = new Forms.ContextMenuStrip
        {
            ShowImageMargin = false,
            Font = new Drawing.Font("Segoe UI", 9.5f),
            BackColor = Drawing.ColorTranslator.FromHtml("#1F242F"),
            ForeColor = Drawing.ColorTranslator.FromHtml("#F2F4F8"),
            Renderer = new Forms.ToolStripProfessionalRenderer(new DarkColors()) { RoundedEdges = false },
        };

        _toggle = new Forms.ToolStripMenuItem("Göster / Gizle");
        _toggle.Click += (_, _) => App.Flyout.Toggle();

        var settings = new Forms.ToolStripMenuItem("Ayarlar");
        settings.Click += (_, _) => App.ShowSettings();

        var exit = new Forms.ToolStripMenuItem("Çıkış");
        exit.Click += (_, _) => App.Current.Shutdown();

        menu.Items.AddRange(new Forms.ToolStripItem[] { _toggle, settings, new Forms.ToolStripSeparator(), exit });
        menu.Opening += (_, _) => _toggle.ShortcutKeyDisplayString = HotkeyService.Describe(App.Settings);

        _icon.ContextMenuStrip = menu;
        _icon.MouseDoubleClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) App.ShowSettings(); };
    }

    public void Notify(string text) => _icon.ShowBalloonTip(4000, "QuickMedia", text, Forms.ToolTipIcon.Warning);

    private static Drawing.Icon MakeIcon()
    {
        using var bmp = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);
            using var bg = new Drawing.SolidBrush(Drawing.Color.FromArgb(0x8A, 0xB4, 0xF8));
            using var fg = new Drawing.SolidBrush(Drawing.Color.FromArgb(0x17, 0x1B, 0x24));
            g.FillEllipse(bg, 1, 1, 30, 30);
            g.FillPolygon(fg, new[] { new Drawing.Point(12, 8), new Drawing.Point(12, 24), new Drawing.Point(25, 16) });
        }
        return (Drawing.Icon)Drawing.Icon.FromHandle(bmp.GetHicon()).Clone();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    private sealed class DarkColors : Forms.ProfessionalColorTable
    {
        private static Drawing.Color H(string h) => Drawing.ColorTranslator.FromHtml(h);
        public override Drawing.Color ToolStripDropDownBackground => H("#1F242F");
        public override Drawing.Color MenuBorder => H("#343B4D");
        public override Drawing.Color MenuItemBorder => H("#2A3040");
        public override Drawing.Color MenuItemSelected => H("#2A3040");
        public override Drawing.Color MenuItemSelectedGradientBegin => H("#2A3040");
        public override Drawing.Color MenuItemSelectedGradientEnd => H("#2A3040");
        public override Drawing.Color ImageMarginGradientBegin => H("#1F242F");
        public override Drawing.Color ImageMarginGradientMiddle => H("#1F242F");
        public override Drawing.Color ImageMarginGradientEnd => H("#1F242F");
        public override Drawing.Color SeparatorDark => H("#343B4D");
        public override Drawing.Color SeparatorLight => H("#343B4D");
    }
}
