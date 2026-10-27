using System;
using System.Runtime.InteropServices;
using Godot;

namespace OpenScctGame;

/// <summary>
/// Keeps a test run's window away from the person using the machine: it never takes focus, has no taskbar button and
/// sits off-screen, where it still renders and saves its frames (SVM_PLAY_SHOTS reads the game's own image, not the
/// screen). A window started minimised (game/override.cfg) is restored off-screen, because a minimised window does not
/// draw. Used for smoke runs and for windowed runs started with --svm-background, SVM_BACKGROUND=1 or SVM_PLAY_SHOTS;
/// SVM_SMOKE_FOCUS=1 turns it off for debugging.
/// </summary>
public partial class BackgroundWindow : Node
{
    public const string Flag = "--svm-background";

    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExAppWindow = 0x00040000;
    private const long WsExNoActivate = 0x08000000;
    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;
    private const int OffScreen = -32000;

    private int _frames;

    /// <summary>True when this run should stay in the background.</summary>
    public static bool Wanted(bool smokeRun, System.Collections.Generic.IEnumerable<string> args)
    {
        if (System.Environment.GetEnvironmentVariable("SVM_SMOKE_FOCUS") == "1")
        {
            return false;
        }

        foreach (string arg in args)
        {
            if (arg == Flag)
            {
                return true;
            }
        }

        return smokeRun ||
               !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("SVM_PLAY_SHOTS")) ||
               System.Environment.GetEnvironmentVariable("SVM_BACKGROUND") == "1";
    }

    public override void _Ready()
    {
        // As early as a script can run, before the first frame is drawn.
        Hide();
    }

    public override void _Process(double delta)
    {
        // Godot may still show or move the window around its first frames; repeat a few times, then stop.
        if (++_frames > 30)
        {
            SetProcess(false);
            return;
        }

        if (_frames is 1 or 2 or 5 or 15 or 30)
        {
            Hide();
        }
    }

    private static void Hide()
    {
        if (!OperatingSystem.IsWindows() || DisplayServer.GetName() == "headless")
        {
            return;
        }

        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus, true);
        var hwnd = new IntPtr(DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle));
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        long style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        long wanted = (style | WsExNoActivate | WsExToolWindow) & ~WsExAppWindow;
        bool restyle = wanted != style;
        if (restyle)
        {
            // The taskbar picks up a style change only while the window is hidden.
            ShowWindow(hwnd, SwHide);
            SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(wanted));
        }

        var place = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
        if (!GetWindowPlacement(hwnd, ref place))
        {
            return;
        }

        int w = place.Normal.Right - place.Normal.Left;
        int h = place.Normal.Bottom - place.Normal.Top;
        if (place.Normal.Left != OffScreen || place.ShowCmd != SwShowNoActivate || restyle)
        {
            place.Normal = new Rect { Left = OffScreen, Top = OffScreen, Right = OffScreen + w, Bottom = OffScreen + h };
            place.ShowCmd = SwShowNoActivate;
            SetWindowPlacement(hwnd, ref place);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public int Length;
        public int Flags;
        public int ShowCmd;
        public int MinX;
        public int MinY;
        public int MaxX;
        public int MaxY;
        public Rect Normal;
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int cmd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WindowPlacement place);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPlacement(IntPtr hWnd, ref WindowPlacement place);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);
}
