using System.Diagnostics;
using Microsoft.Toolkit.Uwp.Notifications;
using RightClickConvert.Infrastructure;

namespace RightClickConvert.Shell;

/// <summary>
/// Toast notifications are the only feedback channel: the app runs as a WinExe from a
/// shell verb, so there is nowhere for console output to go.
/// </summary>
/// <remarks>
/// Uses the classic Win32 toast API rather than the Windows App SDK, which pulled in
/// roughly 140 MB of ONNX Runtime and DirectML for a feature this app does not use.
/// The compatibility layer creates the Start Menu shortcut and registers the COM
/// activator that unpackaged desktop apps need before Windows will display a toast.
/// </remarks>
public static class Notifier
{
    private const string OpenFolderAction = "openFolder";
    private static readonly TimeSpan ActivationTimeout = TimeSpan.FromSeconds(5);

    private static readonly ManualResetEventSlim Activated = new(initialState: false);
    private static string? activationArgument;

    /// <summary>
    /// Returns true when this process was launched by clicking a toast rather than by
    /// Explorer, in which case there is no conversion to run.
    /// </summary>
    public static bool TryHandleActivation()
    {
        try
        {
            ToastNotificationManagerCompat.OnActivated += OnActivated;

            if (!ToastNotificationManagerCompat.WasCurrentProcessToastActivated())
            {
                return false;
            }

            // The activation callback arrives on another thread, and this process would
            // otherwise exit before it does.
            if (Activated.Wait(ActivationTimeout) && activationArgument is not null)
            {
                OpenFolder(activationArgument);
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Write($"activation handling failed: {ex.Message}");
            return false;
        }
    }

    private static void OnActivated(ToastNotificationActivatedEventArgsCompat args)
    {
        activationArgument = args.Argument;
        Activated.Set();
    }

    private static void OpenFolder(string argument)
    {
        ToastArguments parsed = ToastArguments.Parse(argument);

        if (parsed.TryGetValue("action", out string? action)
            && action == OpenFolderAction
            && parsed.TryGetValue("path", out string? folder)
            && Directory.Exists(folder))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
    }

    public static void Success(string outputPath)
    {
        string folder = Path.GetDirectoryName(outputPath) ?? string.Empty;

        Show(new ToastContentBuilder()
            .AddText("Conversion complete")
            .AddText(Path.GetFileName(outputPath))
            .AddButton(new ToastButton()
                .SetContent("Open folder")
                .AddArgument("action", OpenFolderAction)
                .AddArgument("path", folder)));
    }

    public static void Failure(string headline, string detail)
    {
        Console.Error.WriteLine($"{headline}: {detail}");

        Show(new ToastContentBuilder()
            .AddText(headline)
            .AddText(detail));
    }

    private static void Show(ToastContentBuilder toast)
    {
        try
        {
            toast.Show();

            // Give the platform a moment to take delivery before the process exits.
            Thread.Sleep(250);
        }
        catch (Exception ex)
        {
            Log.Write($"notification failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes the Start Menu shortcut and COM activator registration created on first use.
    /// </summary>
    public static void Cleanup()
    {
        try
        {
            ToastNotificationManagerCompat.Uninstall();
        }
        catch (Exception ex)
        {
            Log.Write($"notification cleanup failed: {ex.Message}");
        }
    }
}
