using System.Drawing;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;

namespace SelectiveVpn.Services;

internal static class TrayMenuTheme
{
    public static void Apply(Forms.ContextMenuStrip menu, string? theme)
    {
        var background = ColorOr(theme, "CardBackground", System.Drawing.Color.FromArgb(255, 255, 255));
        var foreground = ColorOr(theme, "WindowForeground", System.Drawing.Color.FromArgb(28, 36, 48));
        var hover = ColorOr(theme, "ListHoverBackground", System.Drawing.Color.FromArgb(228, 234, 243));
        var border = ColorOr(theme, "CardBorder", System.Drawing.Color.FromArgb(226, 230, 237));
        var danger = ColorOr(theme, "DangerForeground", System.Drawing.Color.FromArgb(194, 65, 65));

        menu.RenderMode = Forms.ToolStripRenderMode.Professional;
        menu.Renderer = new Forms.ToolStripProfessionalRenderer(new ColorTable(background, hover, border));
        menu.BackColor = background;
        menu.ForeColor = foreground;
        menu.ShowImageMargin = false;
        menu.ShowCheckMargin = false;
        menu.Font = new Font("Segoe UI", 9f, System.Drawing.FontStyle.Regular, GraphicsUnit.Point);

        foreach (Forms.ToolStripItem item in menu.Items)
        {
            if (item is Forms.ToolStripSeparator)
            {
                item.BackColor = background;
                continue;
            }

            item.BackColor = background;
            item.ForeColor = item.Tag as string == "exit" ? danger : foreground;
        }
    }

    private static System.Drawing.Color ColorOr(string? theme, string key, System.Drawing.Color fallback)
    {
        if (!AppTheme.TryGetColor(theme, key, out MediaColor color))
            return fallback;

        return System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
    }

    private sealed class ColorTable : Forms.ProfessionalColorTable
    {
        private readonly System.Drawing.Color _background;
        private readonly System.Drawing.Color _hover;
        private readonly System.Drawing.Color _border;

        public ColorTable(System.Drawing.Color background, System.Drawing.Color hover, System.Drawing.Color border)
        {
            _background = background;
            _hover = hover;
            _border = border;
        }

        public override System.Drawing.Color MenuBorder => _border;
        public override System.Drawing.Color MenuItemBorder => _border;
        public override System.Drawing.Color MenuItemSelected => _hover;
        public override System.Drawing.Color MenuItemSelectedGradientBegin => _hover;
        public override System.Drawing.Color MenuItemSelectedGradientEnd => _hover;
        public override System.Drawing.Color MenuItemPressedGradientBegin => _hover;
        public override System.Drawing.Color MenuItemPressedGradientMiddle => _hover;
        public override System.Drawing.Color MenuItemPressedGradientEnd => _hover;
        public override System.Drawing.Color ToolStripDropDownBackground => _background;
        public override System.Drawing.Color ImageMarginGradientBegin => _background;
        public override System.Drawing.Color ImageMarginGradientMiddle => _background;
        public override System.Drawing.Color ImageMarginGradientEnd => _background;
        public override System.Drawing.Color SeparatorDark => _border;
        public override System.Drawing.Color SeparatorLight => _border;
    }
}
