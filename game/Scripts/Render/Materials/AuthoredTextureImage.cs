using System;
using System.Runtime.InteropServices;
using Godot;
using OpenScct.Content.Materials;

namespace OpenScctGame.Render.Materials;

/// <summary>Role-aware decoding and mip generation shared with the editor. Stock textures retain their loader.</summary>
public static class AuthoredTextureImage
{
    public static string Key(string slot, MaterialTexture binding, MaterialAnimation? animation = null) => (slot is "normal" or "detailNormal" ? "normal:" : "image:") + binding.Srgb + ":" + (slot == "environment" ? "1:1" : $"{animation?.Columns ?? 1}:{animation?.Rows ?? 1}");
    public static Image Decode(byte[] bytes, string slot, MaterialTexture binding, MaterialAnimation? animation = null)
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
            Mipmaps(image, slot == "environment" ? null : animation, false);
            raw = image.GetData(); values = MemoryMarshal.Cast<byte, float>(raw.AsSpan());
            for (int i = 0; i < values.Length; i++) if (i % 4 != 3) values[i] = Display(values[i]);
            image.SetData(image.GetWidth(), image.GetHeight(), image.HasMipmaps(), Image.Format.Rgbaf, raw);
            if (data is null) image.Convert(Image.Format.Rgba8);
        }
        else Mipmaps(image, slot == "environment" ? null : animation, normal);
        return image;
    }
    private static void Mipmaps(Image image, MaterialAnimation? animation, bool normal)
    {
        int columns = animation?.Columns ?? 1, rows = animation?.Rows ?? 1;
        if (columns == 1 && rows == 1) { image.GenerateMipmaps(normal); return; }
        int width = image.GetWidth(), height = image.GetHeight(), fw = width / columns, fh = height / rows;
        // Non-power-of-two frame grids use base-level filtering; there is no rectangular cell layout in the
        // final few native atlas mip levels. The shader clamps before those levels can mix animation frames.
        if (!System.Numerics.BitOperations.IsPow2((uint)fw) || !System.Numerics.BitOperations.IsPow2((uint)fh)) return;
        if (image.GetFormat() != Image.Format.Rgbaf) image.Convert(Image.Format.Rgba8);
        int bytesPerPixel = image.GetFormat() == Image.Format.Rgbaf ? 16 : 4;
        using var combined = Image.CreateEmpty(width, height, true, image.GetFormat());
        byte[] output = combined.GetData(); int max = (int)MathF.Log2(Math.Min(fw, fh));
        for (int row = 0; row < rows; row++) for (int column = 0; column < columns; column++)
        {
            using var frame = image.GetRegion(new Rect2I(column * fw, row * fh, fw, fh)); frame.GenerateMipmaps(normal); byte[] pixels = frame.GetData();
            for (int level = 0; level <= max; level++)
            {
                int w = fw >> level, h = fh >> level, stride = (width >> level) * bytesPerPixel;
                int from = (int)frame.GetMipmapOffset(level), to = (int)combined.GetMipmapOffset(level) + (row * h * (width >> level) + column * w) * bytesPerPixel;
                for (int y = 0; y < h; y++) pixels.AsSpan(from + y * w * bytesPerPixel, w * bytesPerPixel).CopyTo(output.AsSpan(to + y * stride));
            }
        }
        image.SetData(width, height, true, image.GetFormat(), output);
    }
    private static float Linear(float c) => c <= .04045f ? c / 12.92f : MathF.Pow((c + .055f) / 1.055f, 2.4f);
    private static float Display(float c) => c <= .0031308f ? c * 12.92f : 1.055f * MathF.Pow(MathF.Max(0, c), 1 / 2.4f) - .055f;
}
