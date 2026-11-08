using System;
using Godot;

namespace OpenScctGame.Render.Characters;

public static class FirstPersonFraming
{
    public static float VerticalFov(float horizontalDegrees, float referenceAspect = 4f / 3)
    {
        float half = Mathf.DegToRad(horizontalDegrees) * .5f;
        return Mathf.RadToDeg(2 * MathF.Atan(MathF.Tan(half) / referenceAspect));
    }
}
