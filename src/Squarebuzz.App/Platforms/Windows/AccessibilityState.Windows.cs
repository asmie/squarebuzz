using System.Runtime.InteropServices;

namespace Squarebuzz.App.Services;

/// <summary>
/// Windows' answer: the system screen-reader flag, which Narrator, NVDA and JAWS all raise while
/// they run.
/// </summary>
/// <remarks>
/// <para>
/// There is no WinRT API for "is a screen reader driving?"; the flag lives behind the Win32
/// <c>SystemParametersInfo(SPI_GETSCREENREADER)</c>, so this is the one place in the app that
/// P/Invokes. It is a single read of one boolean and cannot fail in a way that matters.
/// </para>
/// <para>
/// No change listener. The flag's change arrives as a <c>WM_SETTINGCHANGE</c> window message,
/// which would mean hooking the window procedure - a lot of machinery for a notification the app
/// already compensates for: <c>GamePage.OnAppearing</c> re-reads the state on every board open,
/// so a screen reader started while the app is in the background is honoured the next time a
/// board is shown. Only a screen reader started while a board is already on screen waits for the
/// player to leave and return. That is a smaller gap than the one this file closes, which was
/// "never".
/// </para>
/// </remarks>
public sealed partial class AccessibilityState
{
    private const uint SpiGetScreenReader = 0x0046;

    private static bool GetIsScreenReaderActive()
    {
        try
        {
            return SystemParametersInfoW(SpiGetScreenReader, 0, out var enabled, 0) && enabled != 0;
        }
        catch (Exception)
        {
            // A refused system call must not stop the game starting.
            return false;
        }
    }

    // PlatformInitialize and DisposePlatform are deliberately left unimplemented here: a partial
    // method with no body compiles to nothing, which is the honest statement of "there is no
    // change notification on this platform" - see the class remarks for why not.

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfoW(uint action, uint parameter, out int value, uint winIni);
}
