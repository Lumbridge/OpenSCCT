using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace OpenScct.Content;

/// <summary>Optional editor bake response. Recombines cached light paths when glass breaks; no gameplay class or SVMap version change.</summary>
public sealed class WindowLightTransfer
{
    public const string RelativePath = "render/window-lighting.json.gz";
    public int Version { get; set; } = 1;
    public List<Pane> Panes { get; set; } = [];
    public List<Batch> Batches { get; set; } = [];
    public sealed class Pane { public string Actor { get; set; } = ""; public float[] Tint { get; set; } = [1, 1, 1]; }
    public sealed class Resource
    {
        public string Kind { get; set; } = "texture";
        public string Name { get; set; } = "";
        public int Surface { get; set; }
    }
    public sealed class Batch
    {
        public bool Faithful { get; set; }
        public List<Resource> Resources { get; set; } = [];
        public List<Sample> Samples { get; set; } = [];
        public List<Fill> DirectFill { get; set; } = [];
        public List<Bounce> Bounces { get; set; } = [];
        public List<Fill> FinalFill { get; set; } = [];
    }
    public sealed class Sample
    {
        public float[] Base { get; set; } = [0, 0, 0];
        public float[] Direct { get; set; } = [0, 0, 0];
        public float[] Initial { get; set; } = [0, 0, 0];
        public List<Term> Terms { get; set; } = [];
        public List<Destination> Destinations { get; set; } = [];
    }
    public sealed class Term { public int[] Panes { get; set; } = []; public float[] Colour { get; set; } = [0, 0, 0]; }
    public sealed class Destination
    {
        public int Resource { get; set; }
        public int Element { get; set; }
        public float Weight { get; set; } = 1;
        public float Dither { get; set; }
        public float[]? Point { get; set; }
        public float[]? Normal { get; set; }
    }
    public sealed class Fill { public int Target { get; set; } public int[] Sources { get; set; } = []; }
    public sealed class Bounce
    {
        public int Target { get; set; }
        public int Source { get; set; }
        public float[] Gain { get; set; } = [0, 0, 0];
        public float[] Ambient { get; set; } = [0, 0, 0];
    }

    /// <summary>RGB per sparse sample, in the same stored units as the original lightmap/vertex bake.</summary>
    public float[][] Evaluate(Batch batch, IReadOnlySet<string> broken)
    {
        float[] flat = EvaluateFlat(batch, broken);
        var values = new float[batch.Samples.Count][];
        for (int i = 0; i < values.Length; i++) values[i] = [flat[i*3], flat[i*3+1], flat[i*3+2]];
        return values;
    }

    // Contiguous buffers keep a break from allocating an object per lightmap texel.
    public float[] EvaluateFlat(Batch batch, IReadOnlySet<string> broken)
    {
        int count = batch.Samples.Count;
        var delta = new float[count * 3];
        if (broken.Count == 0)
        {
            for (int i=0;i<count;i++) batch.Samples[i].Initial.CopyTo(delta,i*3);
            return delta;
        }
        for (int i = 0; i < count; i++)
        {
            Sample sample = batch.Samples[i];
            bool changed = false;
            foreach (Term term in sample.Terms) foreach (int p in term.Panes) changed |= broken.Contains(Panes[p].Actor);
            if (!changed) continue;
            float red=sample.Base[0], green=sample.Base[1], blue=sample.Base[2];
            foreach (Term term in sample.Terms)
            {
                double r=1,g=1,b=1;
                foreach (int p in term.Panes) if (!broken.Contains(Panes[p].Actor))
                { r*=Panes[p].Tint[0];g*=Panes[p].Tint[1];b*=Panes[p].Tint[2]; }
                red += batch.Faithful ? (float)(Math.Floor(Math.Min(255,term.Colour[0]*r))/255) : (float)(term.Colour[0]*r);
                green += batch.Faithful ? (float)(Math.Floor(Math.Min(255,term.Colour[1]*g))/255) : (float)(term.Colour[1]*g);
                blue += batch.Faithful ? (float)(Math.Floor(Math.Min(255,term.Colour[2]*b))/255) : (float)(term.Colour[2]*b);
            }
            delta[i*3]=(batch.Faithful?Math.Min(1,red):red)-sample.Direct[0];
            delta[i*3+1]=(batch.Faithful?Math.Min(1,green):green)-sample.Direct[1];
            delta[i*3+2]=(batch.Faithful?Math.Min(1,blue):blue)-sample.Direct[2];
        }
        static void FillIn(float[] values,List<Fill> fills)
        {
            foreach (Fill fill in fills)
            {
                int at=fill.Target*3;values[at]=values[at+1]=values[at+2]=0;
                foreach (int source in fill.Sources) if (source>=0)
                    for (int k=0;k<3;k++) values[at+k]+=values[source*3+k]/fill.Sources.Length;
            }
        }
        FillIn(delta,batch.DirectFill);
        var final=(float[])delta.Clone();
        foreach (Bounce bounce in batch.Bounces)
        {
            int source=bounce.Source*3,target=bounce.Target*3;
            if (delta[source]==0 && delta[source+1]==0 && delta[source+2]==0) continue;
            float[] before=batch.Samples[bounce.Source].Direct;
            for (int k=0;k<3;k++) final[target+k]+=bounce.Gain[k]
                *(Math.Max(0,before[k]+delta[source+k]-bounce.Ambient[k])-Math.Max(0,before[k]-bounce.Ambient[k]));
        }
        FillIn(final,batch.FinalFill);
        for (int i=0;i<count;i++) for (int k=0;k<3;k++) final[i*3+k]+=batch.Samples[i].Initial[k];
        return final;
    }

    public static byte Encode(float stored, float dither = 0) => (byte)Math.Clamp(Math.Round(stored * 255.0 + dither), 0, 255);
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public byte[] Save()
    {
        using var bytes = new MemoryStream();
        using (var gzip = new GZipStream(bytes, CompressionLevel.SmallestSize, true)) JsonSerializer.Serialize(gzip, this, Options);
        return bytes.ToArray();
    }
    public static WindowLightTransfer Load(Stream bytes)
    {
        using var gzip = new GZipStream(bytes, CompressionMode.Decompress, true);
        using var decoded = new MemoryStream();
        byte[] buffer = new byte[65536]; int n;
        while ((n = gzip.Read(buffer)) > 0)
        {
            if (decoded.Length + n > 256 * 1024 * 1024) throw new InvalidDataException("Window light response exceeds 256 MiB");
            decoded.Write(buffer, 0, n);
        }
        decoded.Position = 0;
        var data = JsonSerializer.Deserialize<WindowLightTransfer>(decoded, Options) ?? throw new InvalidDataException("Empty window light response");
        data.Validate(); return data;
    }
    public void Validate()
    {
        static void Rgb(float[]? rgb) { if (rgb is null || rgb.Length != 3 || Array.Exists(rgb, x => !float.IsFinite(x))) throw new InvalidDataException("Invalid window light colour"); }
        if (Version != 1 || Panes is null || Batches is null || Panes.Count > 65536 || Batches.Count > 65536) throw new InvalidDataException("Unsupported window light response");
        foreach (Pane p in Panes)
        {
            if (p is null || string.IsNullOrEmpty(p.Actor)) throw new InvalidDataException("Invalid window actor");
            Rgb(p.Tint); if (Array.Exists(p.Tint, x => x < 0 || x > 1)) throw new InvalidDataException("Invalid window transmission");
        }
        foreach (Batch b in Batches)
        {
            if (b is null || b.Resources is null || b.Samples is null || b.DirectFill is null || b.FinalFill is null || b.Bounces is null)
                throw new InvalidDataException("Invalid window lighting batch");
            bool Index(int i) => i >= 0 && i < b.Samples.Count;
            foreach (Resource r in b.Resources)
                if (r is null || r.Kind is not ("texture" or "mesh") || string.IsNullOrEmpty(r.Name) || r.Name.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(r.Name))
                    throw new InvalidDataException("Invalid window lighting resource");
            foreach (Sample s in b.Samples)
            {
                if (s is null || s.Terms is null || s.Destinations is null) throw new InvalidDataException("Invalid window lighting sample");
                Rgb(s.Base); Rgb(s.Direct); Rgb(s.Initial);
                foreach (Term t in s.Terms)
                {
                    if (t is null || t.Panes is null || Array.Exists(t.Panes, i => i < 0 || i >= Panes.Count)) throw new InvalidDataException("Invalid window index");
                    Rgb(t.Colour);
                }
                foreach (Destination d in s.Destinations)
                {
                    if (d is null) throw new InvalidDataException("Invalid window lighting destination");
                    if (d.Point is not null) Rgb(d.Point);
                    if (d.Normal is not null) Rgb(d.Normal);
                    if (d.Resource < 0 || d.Resource >= b.Resources.Count || d.Element < 0 || !float.IsFinite(d.Weight) || d.Weight <= 0 || d.Weight > 1 || !float.IsFinite(d.Dither))
                        throw new InvalidDataException("Invalid window lighting destination");
                }
            }
            foreach (Fill f in b.DirectFill) if (f is null || f.Sources is null || !Index(f.Target) || f.Sources.Length == 0 || Array.Exists(f.Sources, i => i != -1 && !Index(i))) throw new InvalidDataException("Invalid light padding");
            foreach (Fill f in b.FinalFill) if (f is null || f.Sources is null || !Index(f.Target) || f.Sources.Length == 0 || Array.Exists(f.Sources, i => i != -1 && !Index(i))) throw new InvalidDataException("Invalid light padding");
            foreach (Bounce x in b.Bounces)
            {
                if (x is null || !Index(x.Target) || !Index(x.Source)) throw new InvalidDataException("Invalid bounce light");
                Rgb(x.Gain); Rgb(x.Ambient);
            }
        }
    }
}
