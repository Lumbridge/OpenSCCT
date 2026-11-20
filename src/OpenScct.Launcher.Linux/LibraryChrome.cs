using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace OpenScct.Launcher.Linux;

internal sealed partial class MainWindow
{
    private readonly Dictionary<string, Bitmap> _illustrations = new();
    private readonly Dictionary<string, TextBlock> _editionLabels = new();
    private readonly Dictionary<string, TextBlock> _editionStates = new();
    private readonly Dictionary<string, Border> _editionLights = new();
    private readonly Dictionary<Button, ShapePath> _navIcons = new();
    private readonly Dictionary<Button, TextBlock> _navLabels = new();
    private readonly ScrollViewer _libraryScroll = new();
    private static readonly IBrush Pending = Brush.Parse("#E8BA70");
    private static readonly IBrush SelectedRow = Brush.Parse("#263120");
    private static readonly IBrush SelectedEdge = Brush.Parse("#657F3D");

    private void LoadIllustrations()
    {
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var (id, file) in new[] { ("Ps2", "ps2"), ("Retail", "pc"), ("Game", "godot"), ("Unreal", "unreal") })
        {
            using Stream? art = assembly.GetManifestResourceStream($"OpenScct.Launcher.library-{file}.jpg");
            if (art is not null) _illustrations[id] = new Bitmap(art);
        }
        Closed += (_, _) => { foreach (Bitmap image in _illustrations.Values) image.Dispose(); };
    }

    private Bitmap? Illustration(string id) => _illustrations.GetValueOrDefault(id) ?? _artwork;

    private static ShapePath Glyph(string kind, IBrush? ink = null)
    {
        string shape = kind switch
        {
            "games" => "M3,4 L8,4 L8,20 L3,20 Z M11,4 L14,4 L14,20 L11,20 Z M16,5 L19,4 L23,19 L20,20 Z",
            "mods" => "M4,15 L15,4 L20,9 L9,20 L3,21 Z M12,7 L17,12",
            "settings" => "M3,6 L21,6 M3,12 L21,12 M3,18 L21,18 M8,3 L8,9 M16,9 L16,15 M10,15 L10,21",
            "web" => "M2,12 A10,10 0 1 0 22,12 A10,10 0 1 0 2,12 M12,2 C6,7 6,17 12,22 C18,17 18,7 12,2 M2,12 L22,12",
            "help" => "M2,12 A10,10 0 1 0 22,12 A10,10 0 1 0 2,12 M8.5,9 C8.5,4.5 16.5,4.5 16,9 C16,12 12,12 12,15 M12,17 L12,18",
            "folder" => "M2,5 L9,5 L11,8 L22,8 L22,20 L2,20 Z",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        return new ShapePath { Data = Geometry.Parse(shape), Stretch = Stretch.Uniform, Width = 17, Height = 17,
            Stroke = ink ?? Ui.Accent, StrokeThickness = 1.65, VerticalAlignment = VerticalAlignment.Center };
    }

    private Button Nav(Button button, string label, string icon)
    {
        var text = Ui.Text(label, 15);
        text.FontWeight = FontWeight.SemiBold;
        var symbol = Glyph(icon);
        button.Content = Ui.Row(7, symbol, text);
        AutomationProperties.SetName(button, label);
        _navIcons[button] = symbol;
        _navLabels[button] = text;
        return button;
    }

    private static Button IconButton(Button button, string icon, string label)
    {
        button.Content = Glyph(icon, Ui.Muted);
        button.Width = 36;
        button.Height = 36;
        button.Padding = new Thickness(8);
        AutomationProperties.SetName(button, label);
        ToolTip.SetTip(button, label);
        return button;
    }

    private static Border Light(IBrush color, double size = 7) => new()
    {
        Background = color, Width = size, Height = size, CornerRadius = new CornerRadius(size),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private Button EditionRow(string id, string label)
    {
        var title = Ui.Text(label, 15);
        title.FontWeight = FontWeight.SemiBold;
        var state = Ui.Text("", 12, Ui.Muted);
        var light = Light(Pending);
        var status = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 5 };
        status.Children.Add(light);
        Grid.SetColumn(state, 1); status.Children.Add(state);
        var description = Ui.Stack(title, status);
        description.Spacing = 4;
        description.VerticalAlignment = VerticalAlignment.Center;
        var tile = new Border { Width = 46, Height = 46, Background = Ui.Background, CornerRadius = new CornerRadius(24),
            BorderBrush = Ui.Edge, BorderThickness = new Thickness(1), ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center };
        if (Illustration(id) is { } image)
            tile.Child = new Image { Source = image, Stretch = Stretch.UniformToFill };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 11 };
        row.Children.Add(tile);
        Grid.SetColumn(description, 1);
        row.Children.Add(description);
        var button = Ui.Button(label);
        button.Content = row;
        button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.Padding = new Thickness(9, 8);
        button.MinHeight = 65;
        button.Click += (_, _) => SelectEdition(id);
        _editionLabels[id] = title;
        _editionStates[id] = state;
        _editionLights[id] = light;
        return button;
    }

    private static IBrush Shade(string from, string to, bool vertical = false) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(vertical ? 0 : 1, vertical ? 1 : 0, RelativeUnit.Relative),
        GradientStops = new GradientStops { new(Color.Parse(from), 0), new(Color.Parse(to), 1) },
    };

    private static TextBlock Caption(string text, IBrush? color = null)
    {
        var label = Ui.Text(text.ToUpperInvariant(), 11, color ?? Ui.Muted);
        label.FontWeight = FontWeight.Bold;
        label.LetterSpacing = 1.5;
        return label;
    }

    private static Control SourceStrip(string title, string detail, Button action)
    {
        var heading = Ui.Text(title, 17);
        heading.FontWeight = FontWeight.SemiBold;
        var copy = Ui.Stack(heading, Ui.Text(detail, 13, Ui.Muted));
        copy.Spacing = 3;
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 13 };
        content.Children.Add(Glyph("folder"));
        Grid.SetColumn(copy, 1); content.Children.Add(copy);
        Grid.SetColumn(action, 2); content.Children.Add(action);
        action.VerticalAlignment = VerticalAlignment.Center;
        return Ui.Panel(content);
    }

    private Control GameHero(string id, string platform, string title, string summary, Button primary, Control milestones)
    {
        var name = Ui.Text(title, 31);
        name.FontWeight = FontWeight.Bold;
        name.LineHeight = 36;
        var words = Ui.Stack(Caption(platform, Ui.Accent), name, Ui.Text(summary, 14, Brushes.WhiteSmoke));
        words.Spacing = 4;
        var top = new Grid { ColumnSpacing = 18, RowSpacing = 14 };
        primary.FontWeight = FontWeight.Bold;
        primary.Background = Ui.Accent;
        primary.Foreground = Ui.Background;
        primary.BorderBrush = Ui.Accent;
        primary.MinHeight = 43;
        primary.VerticalAlignment = VerticalAlignment.Center;
        primary.HorizontalAlignment = HorizontalAlignment.Left;
        top.Children.Add(words);
        top.Children.Add(primary);
        void ArrangeHero(double width)
        {
            bool stacked = width < 570;
            top.ColumnDefinitions = new ColumnDefinitions(stacked ? "*" : "*,Auto");
            top.RowDefinitions = new RowDefinitions(stacked ? "Auto,Auto" : "Auto");
            Grid.SetColumn(primary, stacked ? 0 : 1);
            Grid.SetRow(primary, stacked ? 1 : 0);
        }
        ArrangeHero(Width - 270);
        top.SizeChanged += (_, e) => { if ((e.PreviousSize.Width < 570) != (e.NewSize.Width < 570)) ArrangeHero(e.NewSize.Width); };
        var body = Ui.Stack(top, milestones);
        body.Spacing = 25;
        body.Margin = new Thickness(24, 24, 24, 20);
        body.VerticalAlignment = VerticalAlignment.Bottom;
        var field = new Grid { Height = 222 };
        if (Illustration(id) is { } image) field.Children.Add(new Image { Source = image, Stretch = Stretch.UniformToFill });
        field.Children.Add(new Border { Background = Shade("#CB0A110D", "#300D170F") });
        field.Children.Add(new Border { Background = Shade("#080E1810", "#DB0B130F", true) });
        field.Children.Add(body);
        return new Border { Child = field, CornerRadius = new CornerRadius(13), ClipToBounds = true,
            Background = Ui.Surface, BorderBrush = Ui.Edge, BorderThickness = new Thickness(1) };
    }

    private static void Milestones(WrapPanel row, params (string Label, bool Done)[] steps)
    {
        row.Children.Clear();
        bool next = true;
        foreach (var step in steps)
        {
            IBrush tone = step.Done ? Ui.Accent : next ? Pending : Ui.Muted;
            var item = Ui.Row(6, Light(tone), Ui.Text(step.Label, 13, tone));
            item.Margin = new Thickness(0, 3, 16, 3);
            AutomationProperties.SetName(item, step.Label + (step.Done ? " complete" : " pending"));
            row.Children.Add(item);
            if (!step.Done) next = false;
        }
    }

    private static WrapPanel Steps(params (string Label, bool Done)[] steps)
    {
        var row = new WrapPanel();
        Milestones(row, steps);
        return row;
    }
}
