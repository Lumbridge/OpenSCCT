using System;
using System.Runtime.InteropServices;
using Godot;

namespace OpenScctGame.Controls;

internal static class InputFocusTrace
{
    internal static bool IsForeground(Window window)
    {
        if (!OperatingSystem.IsWindows() || DisplayServer.GetName() == "headless") return window.HasFocus();
        var handle = (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, window.GetWindowId());
        return handle != 0 && GetForegroundWindow() == handle;
    }

    internal static string Native(Window window)
    {
        if (!OperatingSystem.IsWindows() || DisplayServer.GetName() == "headless") return string.Empty;
        var handle = (nint)DisplayServer.WindowGetNativeHandle(DisplayServer.HandleType.WindowHandle, window.GetWindowId());
        return $"nativeWindow={handle} nativeForeground={GetForegroundWindow()} nativeFocus={GetFocus()} nativeCapture={GetCapture()}";
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetFocus();
    [DllImport("user32.dll")] private static extern nint GetCapture();
}
