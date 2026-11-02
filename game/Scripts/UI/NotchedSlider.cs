using System;
using Godot;

namespace OpenScctGame.UI;

/// <summary>Pointer-only magnetic detents; keyboard and numeric edits retain the full step resolution.</summary>
public partial class NotchedSlider : HSlider
{
    public double[] Notches { get; set; } = [];

    public double Resolve(double value, bool released = false)
    {
        if (!released && !Input.IsMouseButtonPressed(MouseButton.Left)) return value;
        double width = Math.Max(1, Size.X - GetThemeIcon("grabber", "HSlider").GetWidth());
        double radius = Math.Min((MaxValue - MinValue) * 0.015, (MaxValue - MinValue) * 5 / width);
        double best = value, distance = radius;
        foreach (double notch in Notches)
        {
            double delta = Math.Abs(value - notch);
            if (notch >= MinValue && notch <= MaxValue && delta <= distance)
            {
                best = notch;
                distance = delta;
            }
        }
        SetValueNoSignal(best);
        return Value;
    }

    public override void _Draw()
    {
        float grabber = GetThemeIcon("grabber", "HSlider").GetWidth();
        foreach (double notch in Notches)
        {
            if (notch < MinValue || notch > MaxValue || MaxValue <= MinValue) continue;
            float ratio = (float)((notch - MinValue) / (MaxValue - MinValue));
            if (IsLayoutRtl()) ratio = 1 - ratio;
            float x = grabber / 2 + ratio * (Size.X - grabber);
            DrawLine(new Vector2(x, Size.Y / 2 + 4), new Vector2(x, Size.Y / 2 + 9), new Color(0.65f, 0.8f, 0.7f, 0.8f));
        }
    }

    public static double[] ForSetting(string key, double min, double max)
    {
        if (max == 100 && (min == 0 || key.Contains("opacity") || key == "hack_alpha")) return [0, 25, 50, 75, 100];
        if (key.EndsWith(".scale")) return [50, 75, 100, 125, 150, 175, 200];
        if (key == "resolution_scale" || key.Contains("alpha") || key.Contains("ratio") || key.Contains("deadzone") || key.Contains("threshold") && max <= 1)
            return [0, 0.25, 0.5, 0.75, 1];
        if (key.Contains("clutch") || key == "controller.vertical_scale" || key == "voice_input_gain" || key.EndsWith(".scope_vertical")) return [0.25, 0.5, 0.75, 1, 1.5, 2, 3, 4];
        return [];
    }
}
