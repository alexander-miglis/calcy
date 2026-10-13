using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace Calcy.App;

/// <summary>
/// Bug reports go to the project's GitHub issues. The form opens in the browser, already filled in with the version,
/// the system and the mode; nothing is sent until the user reviews and submits it there.
/// </summary>
internal static class BugReport
{
    public const string Repository = "https://github.com/alexander-miglis/calcy";

    /// <summary>"Windows 10.0.26300 (X64)", without anything that identifies the user.</summary>
    public static string SystemDescription =>
        $"{RuntimeInformation.OSDescription.Trim()} ({RuntimeInformation.OSArchitecture}), .NET {Environment.Version}";

    public static Uri NewIssueUri(string? mode) =>
        new($"{Repository}/issues/new?template=bug_report.yml" +
            $"&version={Uri.EscapeDataString(AppInfo.Description)}" +
            $"&os={Uri.EscapeDataString(SystemDescription)}" +
            $"&mode={Uri.EscapeDataString(string.IsNullOrEmpty(mode) ? "" : mode)}");

    /// <summary>The same details as text, for pasting into an e-mail or an existing issue.</summary>
    public static string Details(string? mode) =>
        $"{AppInfo.Description}\nSystem: {SystemDescription}\nMode: {(string.IsNullOrEmpty(mode) ? "unknown" : mode)}";

    /// <summary>Opens the pre-filled bug report form; falls back to copying the link when no browser can be started.</summary>
    public static async Task<bool> OpenAsync(Control from, string? mode)
    {
        var top = TopLevel.GetTopLevel(from);
        var uri = NewIssueUri(mode);
        if (top is not null && await top.Launcher.LaunchUriAsync(uri)) return true;
        if (top?.Clipboard is { } clip) await ClipboardExtensions.SetTextAsync(clip, uri.ToString());
        return false;
    }

    /// <summary>Copies the version, system and mode, for reports sent some other way.</summary>
    public static async Task CopyDetailsAsync(Control from, string? mode)
    {
        if (TopLevel.GetTopLevel(from)?.Clipboard is { } clip) await ClipboardExtensions.SetTextAsync(clip, Details(mode));
    }
}