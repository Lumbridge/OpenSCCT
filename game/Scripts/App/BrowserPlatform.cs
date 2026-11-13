using System;

namespace OpenScctGame;

/// <summary>Browser host hooks; desktop startup does not install these.</summary>
public static class BrowserPlatform
{
    public static Action? RestoreSettings { get; set; }
    public static Action? FlushSettings { get; set; }
    public static Action<string>? ReportState { get; set; }
    public static Action<string>? StartRound { get; set; }
    public static Action<string>? LessonAction { get; set; }
    public static Action? OpenAccount { get; set; }
    public static string Lesson { get; set; } = "spy";
    public static string? AccountName { get; set; }
    public static OpenScctGame.Play.PlayRoot? CurrentPlay { get; set; }
    public static bool InputActivated { get; set; }
}
