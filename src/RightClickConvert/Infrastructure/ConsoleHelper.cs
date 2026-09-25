using System.Runtime.InteropServices;

namespace RightClickConvert.Infrastructure;

/// <summary>
/// The app is a WinExe so Explorer never flashes a console at the user, which also means
/// output is invisible when it is run from a terminal. Borrowing the parent's console
/// keeps --install, --list and error messages usable while testing.
/// </summary>
internal static class ConsoleHelper
{
    private const int AttachParentProcess = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(int processId);

    public static void AttachToParent()
    {
        try
        {
            AttachConsole(AttachParentProcess);
        }
        catch
        {
            // No parent console; output simply goes nowhere.
        }
    }
}
