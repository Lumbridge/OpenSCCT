using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace OpenScct.Launcher.App;

internal static class LibraryVisuals
{
    public static Bitmap Icon(string name, Color ink, int pixels = 23)
    {
        var image = new Bitmap(pixels, pixels);
        using var g = Graphics.FromImage(image);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.ScaleTransform(pixels / 24f, pixels / 24f);
        using var pen = new Pen(ink, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        switch (name)
        {
            case "games":
                g.DrawRectangle(pen, 3, 4, 4, 16);
                g.DrawRectangle(pen, 8, 4, 4, 16);
                g.DrawPolygon(pen, [new PointF(13,5), new(17,4), new(21,19), new(17,20)]);
                break;
            case "mods":
                g.DrawPolygon(pen, [new PointF(4,16),new(15,5),new(19,9),new(8,20),new(3,21)]);
                g.DrawLine(pen, 13,7,17,11);
                break;
            case "settings":
                g.DrawEllipse(pen, 5,5,14,14);
                g.DrawEllipse(pen, 9,9,6,6);
                for (int index = 0; index < 8; index++)
                {
                    double angle = index * Math.PI / 4;
                    float x = (float)Math.Cos(angle), y = (float)Math.Sin(angle);
                    g.DrawLine(pen, 12 + 7*x, 12 + 7*y, 12 + 9*x, 12 + 9*y);
                }
                break;
            case "folder":
                g.DrawPolygon(pen, [new PointF(2,6),new(9,6),new(11,8),new(21,8),new(21,19),new(2,19)]);
                break;
            case "website":
                g.DrawEllipse(pen, 3,3,18,18);
                g.DrawEllipse(pen, 8,3,8,18);
                g.DrawLine(pen, 3,12,21,12);
                break;
            case "help":
                g.DrawEllipse(pen, 3,3,18,18);
                g.DrawArc(pen, 8,7,7,6,180,275);
                g.DrawLine(pen, 12,13,12,14);
                g.DrawLine(pen, 12,17,12.1f,17);
                break;
        }
        return image;
    }

    public static void Round(Control control, int corners)
    {
        void Resize()
        {
            if (control.ClientSize.Width < 2 || control.ClientSize.Height < 2) return;
            int radius = Math.Min(control.LogicalToDeviceUnits(corners), Math.Min(control.Width, control.Height) / 2);
            using var path = new GraphicsPath();
            path.AddRoundedRectangle(control.ClientRectangle, new Size(radius, radius));
            control.Region = new Region(path);
        }
        control.SizeChanged += (_, _) => Resize();
        Resize();
    }
}

