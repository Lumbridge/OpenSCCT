using System;
using System.Runtime.InteropServices;
using Godot;
using OpenScct.Content.Materials;

namespace OpenScctGame.Render.Materials;

/// <summary>Role-aware decoding and mip generation shared with the editor. Stock textures retain their loader.</summary>
public static class AuthoredTextureImage
{
    public static string Key(string slot, MaterialTexture binding) => (slot is "normal" or "detailNormal" ? "normal:" : "image:") + binding.Srgb;
    public static Image Decode(byte[] bytes, string slot, MaterialTexture binding)
    {
        Image image;
        var data = PngData.Read16(bytes);
        if (data is not null)
            image = Image.CreateFromData(data.Width, data.Height, false, Image.Format.Rgbaf, MemoryMarshal.AsBytes(data.Rgba.AsSpan()).ToArray());
        else
        {
            image = new Image();
            if (image.LoadPngFromBuffer(bytes) != Error.Ok) throw new System.IO.InvalidDataException("Could not decode material PNG.");
        }
        bool normal = slot is "normal" or "detailNormal";
        if (binding.Srgb && !normal)
        {
            image.Convert(Image.Format.Rgbaf);
            byte[] raw = image.GetData(); var values = MemoryMarshal.Cast<byte, float>(raw.AsSpan());
            for (int i = 0; i < values.Length; i++) if (i % 4 != 3) values[i] = Linear(values[i]);
            image.SetData(image.GetWidth(), image.GetHeight(), false, Image.Format.Rgbaf, raw);
            image.GenerateMipmaps();
            raw = image.GetData(); values = MemoryMarshal.Cast<byte, float>(raw.AsSpan());
            for (int i = 0; i < values.Length; i++) if (i % 4 != 3) values[i] = Display(values[i]);
            image.SetData(image.GetWidth(), image.GetHeight(), true, Image.Format.Rgbaf, raw);
            if (data is null) image.Convert(Image.Format.Rgba8);
        }
        else image.GenerateMipmaps(normal);
        return image;
    }
    private static float Linear(float c) => c <= .04045f ? c / 12.92f : MathF.Pow((c + .055f) / 1.055f, 2.4f);
    private static float Display(float c) => c <= .0031308f ? c * 12.92f : 1.055f * MathF.Pow(MathF.Max(0, c), 1 / 2.4f) - .055f;
}
