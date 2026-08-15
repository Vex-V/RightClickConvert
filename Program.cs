
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;


public class DocConverter
{
    public static async Task<int> Main()
    {

        AppNotificationManager.Default.Register();

        string inputFile = "test1.docx";
        var psi = new ProcessStartInfo
        {
            FileName = """C:\Program Files\LibreOffice\program\soffice.exe""",
            Arguments = $"--headless --convert-to pdf \"{inputFile}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };

        process.Start();

        string stdout = await process.StandardOutput.ReadToEndAsync();
        string stderr = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var notification = new AppNotificationBuilder()
            .AddText(
                process.ExitCode == 0
                    ? "Process completed"
                    : "Process error")
            .BuildNotification();

        AppNotificationManager.Default.Show(notification);

        if (process.ExitCode != 0)
        {
            Console.Error.WriteLine(
                $"soffice failed (exit {process.ExitCode}): {stderr}");

            AppNotificationManager.Default.Unregister();
            return 1;
        }

        AppNotificationManager.Default.Unregister();

        Console.WriteLine(stdout);
        return 0;
    }
}