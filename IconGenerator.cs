using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace Bticon
{
    public enum IconStyle
    {
        Number,
        Battery
    }

    public enum IconSize
    {
        Small  = 32,  // Pequeño  – clásico (original)
        Normal = 64,  // Normal   – predeterminado actual
        Large  = 96   // Grande   – muy visible
    }

    public static class IconGenerator
    {
        public static Icon CreateBatteryIcon(
            byte? batteryPercentage,
            bool isConnected,
            IconStyle style = IconStyle.Number,
            IconSize iconSize = IconSize.Normal)
        {
            int size = (int)iconSize;
            float scale = size / 64.0f; // Factor de escala relativo al canvas de referencia (64 px)

            using var bitmap = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(Color.Transparent);

                if (!isConnected || !batteryPercentage.HasValue)
                {
                    DrawDisconnected(g, size, scale);
                }
                else
                {
                    byte level = Math.Min((byte)100, batteryPercentage.Value);
                    Color statusColor = GetStatusColor(level);

                    if (style == IconStyle.Number)
                        DrawNumberStyle(g, size, scale, level, statusColor);
                    else
                        DrawBatteryStyle(g, size, scale, level, statusColor);
                }
            }

            IntPtr hIcon = bitmap.GetHicon();
            return Icon.FromHandle(hIcon);
        }

        private static Color GetStatusColor(byte level)
        {
            if (level > 50) return Color.FromArgb(46, 204, 113);  // Verde
            if (level > 20) return Color.FromArgb(243, 156, 18);  // Ámbar
            return Color.FromArgb(231, 76, 60);                   // Rojo
        }

        // ------------------------------------------------------------------
        // Estilo: Número grande + barra inferior
        // ------------------------------------------------------------------
        private static void DrawNumberStyle(Graphics g, int size, float scale, byte level, Color statusColor)
        {
            string text = level.ToString();

            // Fuente escalada: 38 px para 1-2 dígitos, 30 px para 3 dígitos en base 64 px
            float fontSize = Math.Max(7f, (text.Length >= 3 ? 30f : 38f) * scale);
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);

            SizeF textSize = g.MeasureString(text, font);
            float textX = (size - textSize.Width) / 2f;
            float textY = (size - textSize.Height) / 2f - 5f * scale;

            // Sombra para legibilidad en temas claros y oscuros
            using (var shadow = new SolidBrush(Color.FromArgb(200, 0, 0, 0)))
                g.DrawString(text, font, shadow, textX + 1.5f * scale, textY + 1.5f * scale);

            using (var brush = new SolidBrush(Color.White))
                g.DrawString(text, font, brush, textX, textY);

            // Barra de estado inferior
            int barH   = Math.Max(2, (int)Math.Round(7f * scale));
            int barMrg = Math.Max(1, (int)Math.Round(3f * scale));
            int barW   = size - barMrg * 2;
            int barY   = size - barH - Math.Max(1, (int)Math.Round(3f * scale));

            using (var bg = new SolidBrush(Color.FromArgb(140, 50, 50, 50)))
                g.FillRectangle(bg, barMrg, barY, barW, barH);

            int fillW = Math.Max(2, (int)Math.Round(barW * level / 100.0));
            using (var fill = new SolidBrush(statusColor))
                g.FillRectangle(fill, barMrg, barY, fillW, barH);

            using (var border = new Pen(Color.FromArgb(180, 20, 20, 20), 1))
                g.DrawRectangle(border, barMrg, barY, barW, barH);
        }

        // ------------------------------------------------------------------
        // Estilo: Icono clásico de batería
        // ------------------------------------------------------------------
        private static void DrawBatteryStyle(Graphics g, int size, float scale, byte level, Color statusColor)
        {
            int bodyX  = (int)Math.Round(4  * scale);
            int bodyY  = (int)Math.Round(14 * scale);
            int bodyW  = size - (int)Math.Round(14 * scale);
            int bodyH  = (int)Math.Round(36 * scale);
            int radius = Math.Max(2, (int)Math.Round(5 * scale));

            int capX = bodyX + bodyW;
            int capY = bodyY + (int)Math.Round(8  * scale);
            int capW = Math.Max(2, (int)Math.Round(6  * scale));
            int capH = (int)Math.Round(20 * scale);

            using (var capBrush = new SolidBrush(Color.FromArgb(200, 200, 200)))
                g.FillRectangle(capBrush, capX, capY, capW, capH);

            using (var pen = new Pen(Color.White, Math.Max(1f, 2.5f * scale)))
            using (var path = GetRoundedRect(new Rectangle(bodyX, bodyY, bodyW, bodyH), radius))
                g.DrawPath(pen, path);

            int pad   = Math.Max(1, (int)Math.Round(3 * scale));
            int innerX = bodyX + pad;
            int innerY = bodyY + pad;
            int innerW = bodyW - pad * 2;
            int innerH = bodyH - pad * 2;
            int fillW  = Math.Max(1, (int)Math.Round(innerW * level / 100.0));

            using (var fill = new SolidBrush(statusColor))
                g.FillRectangle(fill, innerX, innerY, fillW, innerH);

            string text = $"{level}%";
            float fontSize = Math.Max(5f, 17f * scale);
            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            SizeF ts = g.MeasureString(text, font);
            float tx = (size - capW - ts.Width) / 2f;
            float ty = bodyY + (bodyH - ts.Height) / 2f;

            using (var shadow = new SolidBrush(Color.FromArgb(220, 0, 0, 0)))
                g.DrawString(text, font, shadow, tx + 1.5f * scale, ty + 1.5f * scale);
            using (var brush = new SolidBrush(Color.White))
                g.DrawString(text, font, brush, tx, ty);
        }

        // ------------------------------------------------------------------
        // Desconectado: silueta de auriculares + cruz roja
        // ------------------------------------------------------------------
        private static void DrawDisconnected(Graphics g, int size, float scale)
        {
            float penW = Math.Max(1f, 3.5f * scale);

            using (var pen = new Pen(Color.FromArgb(160, 160, 160), penW))
            {
                float aX = 8 * scale, aY = 7 * scale, aW = 48 * scale, aH = 36 * scale;
                g.DrawArc(pen, aX, aY, aW, aH, 180, 180);

                DrawRoundedRectangle(g, pen, (int)(6 * scale), (int)(27 * scale),
                    (int)(12 * scale), (int)(22 * scale), (int)(4 * scale), (int)(4 * scale));
                DrawRoundedRectangle(g, pen, (int)(46 * scale), (int)(27 * scale),
                    (int)(12 * scale), (int)(22 * scale), (int)(4 * scale), (int)(4 * scale));
            }

            using (var redPen = new Pen(Color.FromArgb(231, 76, 60), penW))
            {
                float x1 = 38 * scale, y1 = 38 * scale, x2 = 58 * scale, y2 = 58 * scale;
                g.DrawLine(redPen, x1, y1, x2, y2);
                g.DrawLine(redPen, x2, y1, x1, y2);
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------
        private static GraphicsPath GetRoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void DrawRoundedRectangle(Graphics g, Pen pen, int x, int y, int w, int h, int rx, int ry)
        {
            using var path = GetRoundedRect(new Rectangle(x, y, w, h), Math.Min(rx, ry));
            g.DrawPath(pen, path);
        }
    }
}
