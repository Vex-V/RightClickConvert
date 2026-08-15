using System.Diagnostics;
using System.Text;
using Microsoft.Win32;
using RightClickConvert.Infrastructure;

namespace RightClickConvert.Engines;

public static class LibreOfficeEngine
{
    private static readonly TimeSpan ConversionTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan QueueTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Explorer launches the verb once per selected file, so a 15-file selection would
    /// otherwise start 15 soffice processes at once. They are serialised instead.
    /// </summary>
    private const string SerialiseMutex = @"Local\RightClickConvert.LibreOffice";

    public static string? Locate()
    {
        string?[] candidates =
        [
            FromRegistry(@"SOFTWARE\LibreOffice\UNO\InstallPath"),
            FromRegistry(@"SOFTWARE\WOW6432Node\LibreOffice\UNO\InstallPath"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LibreOffice", "program", "soffice.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "LibreOffice", "program", "soffice.exe"),
        ];

        return candidates.FirstOrDefault(c => c is not null && File.Exists(c));
    }

    private static string? FromRegistry(string subKey)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(subKey);
            // The value is the "program" directory, not the executable itself.
            return key?.GetValue(null) is string dir
                ? Path.Combine(dir, "soffice.exe")
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <remarks>
    /// The work runs on a single pool thread rather than across awaits: the cross-process
    /// gate is a <see cref="Mutex"/>, which is thread-affine and throws if released from a
    /// different thread than the one that acquired it.
    /// </remarks>
    public static Task<string> ConvertAsync(string input, Target target, string outputDirectory) =>
        Task.Run(() => Convert(input, target, outputDirectory));

    private static string Convert(string input, Target target, string outputDirectory)
    {
        string soffice = Locate()
            ?? throw new InvalidOperationException(
                "LibreOffice was not found. Install it from libreoffice.org, then try again.");

        // Convert into a scratch directory so the final name can be chosen after the
        // fact — LibreOffice always writes <inputname>.<ext> and clobbers what is there.
        string scratch = Path.Combine(Paths.AppData, "scratch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);

        try
        {
            RunSerialised(soffice, input, target.Filter!, scratch);

            string produced = Directory.GetFiles(scratch).FirstOrDefault()
                ?? throw new InvalidOperationException(
                    $"LibreOffice reported success but produced no {target.Ext.ToUpperInvariant()} file.");

            string final = Paths.Unique(outputDirectory, Path.GetFileNameWithoutExtension(input), target.Ext);
            File.Move(produced, final);
            return final;
        }
        finally
        {
            try
            {
                Directory.Delete(scratch, recursive: true);
            }
            catch
            {
                // Leftover scratch directories are harmless.
            }
        }
    }

    private static void RunSerialised(string soffice, string input, string filter, string outputDirectory)
    {
        using var mutex = new Mutex(initiallyOwned: false, SerialiseMutex);
        bool held = false;
        try
        {
            try
            {
                held = mutex.WaitOne(QueueTimeout);
            }
            catch (AbandonedMutexException)
            {
                // A previous conversion died holding it; we now own it.
                held = true;
            }

            if (!held)
            {
                throw new TimeoutException("Timed out waiting for another conversion to finish.");
            }

            Run(soffice, input, filter, outputDirectory);
        }
        finally
        {
            if (held)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    private static void Run(string soffice, string input, string filter, string outputDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = soffice,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = outputDirectory,
        };

        // A private profile is what makes this work while the user has LibreOffice open.
        // Sharing the default profile makes headless conversion silently do nothing.
        psi.ArgumentList.Add($"-env:UserInstallation={ProfileUri()}");
        psi.ArgumentList.Add("--headless");
        psi.ArgumentList.Add("--norestore");
        psi.ArgumentList.Add("--nolockcheck");
        psi.ArgumentList.Add("--nodefault");
        psi.ArgumentList.Add("--convert-to");
        psi.ArgumentList.Add(filter);
        psi.ArgumentList.Add("--outdir");
        psi.ArgumentList.Add(outputDirectory);
        psi.ArgumentList.Add(input);

        Log.Write($"soffice {string.Join(' ', psi.ArgumentList)}");

        using var process = new Process { StartInfo = psi };

        // Drained via events rather than ReadToEnd so a full pipe buffer cannot deadlock
        // the synchronous wait below.
        var output = new StringBuilder();
        var error = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { output.AppendLine(e.Data); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { error.AppendLine(e.Data); } };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit((int)ConversionTimeout.TotalMilliseconds))
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Already gone.
            }

            throw new TimeoutException(
                $"LibreOffice did not finish within {ConversionTimeout.TotalSeconds:0} seconds.");
        }

        // Lets the redirected reads flush before the buffers are read.
        process.WaitForExit();

        Log.Write($"soffice exit {process.ExitCode} out={output.ToString().Trim()} err={error.ToString().Trim()}");

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"LibreOffice failed (exit {process.ExitCode}). {error.ToString().Trim()}".TrimEnd());
        }
    }

    private static string ProfileUri() =>
        new Uri(Path.Combine(Paths.AppData, "loprofile")).AbsoluteUri;
}
