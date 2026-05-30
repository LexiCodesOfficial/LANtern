using System.Diagnostics;
using System.Runtime.InteropServices;
using Lantern.Application.Abstractions;
using Lantern.Domain;

namespace Lantern.Infrastructure.Notifications;

public sealed class DesktopNotificationService : INotificationService
{
    public bool IsEnabled { get; set; } = true;

    public Task NotifyAsync(NetworkEvent networkEvent, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return Task.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (OperatingSystem.IsWindows())
            {
                ShowWindowsToast(networkEvent.Title, networkEvent.Description);
            }
            else if (OperatingSystem.IsMacOS())
            {
                Start("osascript", $"-e \"display notification \\\"{Escape(networkEvent.Description)}\\\" with title \\\"LANtern: {Escape(networkEvent.Title)}\\\"\"");
            }
            else if (OperatingSystem.IsLinux())
            {
                Start("notify-send", $"\"LANtern: {Escape(networkEvent.Title)}\" \"{Escape(networkEvent.Description)}\"");
            }
        }
        catch
        {
            // Notifications are a convenience. Inventory writes must continue if the OS declines one.
        }

        return Task.CompletedTask;
    }

    private static void ShowWindowsToast(string title, string message)
    {
        var escapedTitle = XmlEscape($"LANtern: {title}");
        var escapedMessage = XmlEscape(message);
        var script = $"""
            [Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] > $null;
            [Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] > $null;
            $xml = New-Object Windows.Data.Xml.Dom.XmlDocument;
            $xml.LoadXml('<toast><visual><binding template="ToastGeneric"><text>{escapedTitle}</text><text>{escapedMessage}</text></binding></visual></toast>');
            $toast = New-Object Windows.UI.Notifications.ToastNotification $xml;
            [Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier('LANtern').Show($toast);
            """;
        Start("powershell.exe", $"-NoProfile -NonInteractive -WindowStyle Hidden -Command \"{script.Replace("\"", "\\\"").Replace(Environment.NewLine, " ")}\"");
    }

    private static void Start(string fileName, string arguments)
    {
        Process.Start(new ProcessStartInfo(fileName, arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static string XmlEscape(string value)
        => System.Security.SecurityElement.Escape(value) ?? string.Empty;

    private static string Escape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
