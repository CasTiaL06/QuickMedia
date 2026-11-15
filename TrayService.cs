using System;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace QuickMedia;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _toggle;
    private readonly Forms.ToolStripMenuItem _styleMenu;

    public TrayService()
    {
        _icon = new Forms.NotifyIcon { Icon = LoadIcon(), Text = "QuickMedia", Visible = true };

        var back = Drawing.ColorTranslator.FromHtml("#1F242F");
        var fore = Drawing.ColorTranslator.FromHtml("#F2F4F8");
        var renderer = new Forms.ToolStripProfessionalRenderer(new DarkColors()) { RoundedEdges = false };

        var menu = new Forms.ContextMenuStrip
        {
            ShowImageMargin = false,
            Font = new Drawing.Font("Segoe UI", 9.5f),
            BackColor = back,
            ForeColor = fore,
            Renderer = renderer,
        };

        _toggle = new Forms.ToolStripMenuItem("Göster / Gizle");
        _toggle.Click += (_, _) => App.Flyout.Toggle();

        _styleMenu = new Forms.ToolStripMenuItem("Stil");
        if (_styleMenu.DropDown is Forms.ToolStripDropDownMenu dm) { dm.ShowImageMargin = false; dm.ShowCheckMargin = true; }
        _styleMenu.DropDown.BackColor = back;
        _styleMenu.DropDown.ForeColor = fore;
        _styleMenu.DropDown.Renderer = renderer;
        foreach (var (id, name) in FlyoutStyles.All)
        {
            var styleId = id;
            var item = new Forms.ToolStripMenuItem(name) { Tag = styleId };
            item.Click += (_, _) => App.SetStyle(styleId);
            _styleMenu.DropDownItems.Add(item);
        }

        var settings = new Forms.ToolStripMenuItem("Ayarlar");
        settings.Click += (_, _) => App.ShowSettings();

        var exit = new Forms.ToolStripMenuItem("Çıkış");
        exit.Click += (_, _) => App.Current.Shutdown();

        menu.Items.AddRange(new Forms.ToolStripItem[] { _toggle, _styleMenu, settings, new Forms.ToolStripSeparator(), exit });
        menu.Opening += (_, _) =>
        {
            _toggle.ShortcutKeyDisplayString = HotkeyService.Describe(HotkeyAction.Toggle);
            foreach (Forms.ToolStripMenuItem it in _styleMenu.DropDownItems)
                it.Checked = (string?)it.Tag == FlyoutStyles.Normalize(App.Settings.FlyoutStyle);
        };

        _icon.ContextMenuStrip = menu;
        _icon.MouseDoubleClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) App.ShowSettings(); };
    }

    public void Notify(string text) => _icon.ShowBalloonTip(4000, "QuickMedia", text, Forms.ToolTipIcon.Warning);

    /// <summary>Gömülü QuickMedia logosu (Assets/icon.ico); bulunamazsa çizilmiş yedek.</summary>
    private static Drawing.Icon LoadIcon()
    {
        try
        {
            var info = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/icon.ico"));
            using var st = info.Stream;
            return new Drawing.Icon(st, Forms.SystemInformation.SmallIconSize);
        }
        catch
        {
            using var bmp = new Drawing.Bitmap(32, 32);
            using (var g = Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var bg = new Drawing.SolidBrush(Drawing.Color.FromArgb(0x8A, 0xB4, 0xF8));
                using var fg = new Drawing.SolidBrush(Drawing.Color.FromArgb(0x17, 0x1B, 0x24));
                g.FillEllipse(bg, 1, 1, 30, 30);
                g.FillPolygon(fg, new[] { new Drawing.Point(12, 8), new Drawing.Point(12, 24), new Drawing.Point(25, 16) });
            }
            return (Drawing.Icon)Drawing.Icon.FromHandle(bmp.GetHicon()).Clone();
        }
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
        public override Drawing.Color CheckBackground => H("#2A3040");
        public override Drawing.Color CheckSelectedBackground => H("#343B4D");
    }
}
