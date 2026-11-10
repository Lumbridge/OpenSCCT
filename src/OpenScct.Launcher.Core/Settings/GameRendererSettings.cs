using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using OpenScct.Launcher.Install;

namespace OpenScct.Launcher.Settings;

/// <summary>The renderer preference shared with the game's next-start override file.</summary>
public static class GameRendererSettings
{
    public const string Vulkan = "Vulkan";
    public static readonly string[] Choices = [Vulkan];

    private const string FileName = "render_override.cfg";
    private const string DriverKey = "rendering_device/driver.windows";

    /// <summary>
    /// Reads the next-start renderer: Vulkan, whatever the file names (Direct3D 12 was a choice until game 0.8.125).
    /// An override file that cannot be read is still reported.
    /// </summary>
    public static string Load(string userDataDirectory)
    {
        _ = Read(userDataDirectory);
        return Vulkan;
    }

    /// <summary>Changes only the Windows driver, keeping latency settings and other override content.</summary>
    public static void Save(string userDataDirectory, string renderer)
    {
        string driver = ToDriverName(renderer);
        string content = Read(userDataDirectory);
        string newline = content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        List<Line> lines = SplitLines(content);
        bool rendering = false;
        bool replaced = false;
        int insertAt = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            Line line = lines[i];
            if (Section(line.Text) is { } section)
            {
                rendering = section == "rendering";
                if (rendering) insertAt = i + 1;
                continue;
            }

            if (!rendering) continue;
            insertAt = i + 1;
            if (!IsDriver(line.Text, out int equals)) continue;
            int start = equals + 1;
            while (start < line.Text.Length && char.IsWhiteSpace(line.Text[start])) start++;
            int end = start + BeforeComment(line.Text[start..]).TrimEnd().Length;
            lines[i] = line with { Text = line.Text[..start] + "\"" + driver + "\"" + line.Text[end..] };
            replaced = true;
        }

        if (!replaced)
        {
            if (insertAt < 0)
            {
                EndLastLine(lines, newline);
                if (lines.Count > 0 && lines[^1].Text.Length > 0) lines.Add(new Line("", newline));
                lines.Add(new Line("[rendering]", newline));
                insertAt = lines.Count;
            }
            else if (insertAt == lines.Count)
            {
                EndLastLine(lines, newline);
            }

            lines.Insert(insertAt, new Line(DriverKey + "=\"" + driver + "\"", newline));
        }

        var result = new StringBuilder();
        foreach (Line line in lines) result.Append(line.Text).Append(line.Ending);
        AtomicFile.WriteAllText(Path.Combine(userDataDirectory, FileName), result.ToString());
    }

    /// <summary>The Godot engine argument for one of the supported renderer labels.</summary>
    public static string ToDriverName(string renderer) => renderer switch
    {
        Vulkan => "vulkan",
        _ => throw new ArgumentException("Vulkan is the only renderer.", nameof(renderer)),
    };

    private static string Read(string userDataDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(userDataDirectory);
        try
        {
            return File.ReadAllText(Path.Combine(userDataDirectory, FileName));
        }
        catch (FileNotFoundException) { return ""; }
        catch (DirectoryNotFoundException) { return ""; }
    }

    private static bool IsDriver(string line, out int equals)
    {
        equals = line.IndexOf('=');
        return equals >= 0 && line[..equals].Trim() == DriverKey;
    }

    private static string? Section(string line)
    {
        string value = BeforeComment(line).Trim();
        return value.Length >= 2 && value[0] == '[' && value[^1] == ']' ? value[1..^1] : null;
    }

    private static string BeforeComment(string line)
    {
        bool quoted = false;
        bool escaped = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (escaped) { escaped = false; continue; }
            if (quoted && c == '\\') { escaped = true; continue; }
            if (c == '"') quoted = !quoted;
            else if (!quoted && c is ';' or '#') return line[..i];
        }

        return line;
    }

    private static void EndLastLine(List<Line> lines, string newline)
    {
        if (lines.Count > 0 && lines[^1].Ending.Length == 0)
            lines[^1] = lines[^1] with { Ending = newline };
    }

    private static List<Line> SplitLines(string content)
    {
        var lines = new List<Line>();
        int start = 0;
        while (start < content.Length)
        {
            int end = start;
            while (end < content.Length && content[end] is not ('\r' or '\n')) end++;
            int next = end;
            if (next < content.Length)
            {
                if (content[next++] == '\r' && next < content.Length && content[next] == '\n') next++;
            }

            lines.Add(new Line(content[start..end], content[end..next]));
            start = next;
        }

        return lines;
    }

    private readonly record struct Line(string Text, string Ending);
}
