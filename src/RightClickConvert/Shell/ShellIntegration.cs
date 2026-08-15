using System.Runtime.InteropServices;
using Microsoft.Win32;
using RightClickConvert.Engines;
using RightClickConvert.Infrastructure;

namespace RightClickConvert.Shell;

/// <summary>
/// Writes the Explorer context-menu entries, generated from <see cref="Formats"/> so the
/// menu and the converter cannot drift apart.
/// </summary>
/// <remarks>
/// Entries go under HKCU\Software\Classes\SystemFileAssociations, which needs no admin
/// rights and applies regardless of which application owns the file type. On Windows 11
/// these are classic verbs, so they appear under "Show more options" rather than in the
/// compact context menu.
/// </remarks>
internal static class ShellIntegration
{
    private const string VerbName = "RightClickConvert";
    private const string MenuLabel = "Convert";

    public static int Install()
    {
        string exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the executable path.");

        if (LibreOfficeEngine.Locate() is null)
        {
            Console.WriteLine("Warning: LibreOffice was not found. Image conversions will work; document conversions will not.");
        }

        foreach ((string ext, Target[] targets) in Formats.Map)
        {
            string verbPath = VerbPath(ext);

            using (var verb = Registry.CurrentUser.CreateSubKey(verbPath))
            {
                verb.SetValue("MUIVerb", MenuLabel);
                verb.SetValue("Icon", $"{exe},0");
                // Resolved relative to HKCR, which merges in HKCU\Software\Classes.
                verb.SetValue("ExtendedSubCommandsKey", $@"SystemFileAssociations\.{ext}\shell\{VerbName}\Targets");
                verb.SetValue("MultiSelectModel", "Document");
            }

            // Subcommands are ordered by key name, not by creation order.
            int order = 10;
            foreach (Target target in targets)
            {
                string itemPath = $@"{verbPath}\Targets\shell\{order:D3}_{target.Ext}";

                using (var item = Registry.CurrentUser.CreateSubKey(itemPath))
                {
                    item.SetValue("MUIVerb", target.Label);
                }

                using (var command = Registry.CurrentUser.CreateSubKey($@"{itemPath}\command"))
                {
                    command.SetValue(string.Empty, $"\"{exe}\" --to {target.Ext} -- \"%1\"");
                }

                order += 10;
            }
        }

        NotifyShell();
        Console.WriteLine($"Installed 'Convert' for {Formats.Map.Count} file types.");
        Console.WriteLine("On Windows 11 it appears under 'Show more options'.");
        Log.Write($"installed for {Formats.Map.Count} extensions from {exe}");
        return 0;
    }

    public static int Uninstall()
    {
        int removed = 0;
        foreach (string ext in Formats.Map.Keys)
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(VerbPath(ext), throwOnMissingSubKey: false);
                removed++;
            }
            catch (Exception ex)
            {
                Log.Write($"uninstall .{ext}: {ex.Message}");
            }
        }

        Notifier.Cleanup();
        NotifyShell();
        Console.WriteLine($"Removed 'Convert' from {removed} file types.");
        Log.Write($"uninstalled from {removed} extensions");
        return 0;
    }

    private static string VerbPath(string ext) =>
        $@"Software\Classes\SystemFileAssociations\.{ext}\shell\{VerbName}";

    private const int AssociationChanged = 0x08000000;
    private const uint IdList = 0x0000;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

    /// <summary>Tells Explorer to reload associations so the menu appears without a restart.</summary>
    private static void NotifyShell()
    {
        try
        {
            SHChangeNotify(AssociationChanged, IdList, IntPtr.Zero, IntPtr.Zero);
        }
        catch
        {
            // Worst case the user has to restart Explorer.
        }
    }
}
