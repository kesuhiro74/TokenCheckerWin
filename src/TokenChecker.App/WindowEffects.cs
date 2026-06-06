using System.Runtime.InteropServices;

namespace TokenChecker.App;

// Win11 DWM window effects. We round the borderless popup corners with the OS
// compositor (DWMWA_WINDOW_CORNER_PREFERENCE) instead of a GDI Region: a Region is
// a 1-bit mask, so a small radius produces a hard, stair-stepped corner that looks
// square (especially a light window over a dark background). DWM rounds with
// anti-aliasing and is DPI-correct, matching native Win11 / VS Code chrome.
internal static class WindowEffects
{
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;

    // DWM_WINDOW_CORNER_PREFERENCE
    private const int DWMWCP_DEFAULT = 0;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWCP_ROUND = 2;       // standard radius (~8px)
    private const int DWMWCP_ROUNDSMALL = 3;  // small radius (~4px)

    // Sentinel COLORREFs for DWMWA_BORDER_COLOR.
    private const int DWMWA_COLOR_DEFAULT = unchecked((int)0xFFFFFFFF);
    private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint hwnd, int attribute, ref int value, int size);

    // Round the window's corners via DWM. `small` picks the tighter radius. No-op
    // (silently) on OSes/handles that don't support it, so callers don't need to
    // guard by version — the window simply stays square there.
    public static void UseRoundedCorners(nint handle, bool small = true)
    {
        if (handle == 0)
        {
            return;
        }

        try
        {
            var pref = small ? DWMWCP_ROUNDSMALL : DWMWCP_ROUND;
            DwmSetWindowAttribute(handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch
        {
            // dwmapi missing / attribute unsupported: leave the window square.
        }
    }

    // Draw (or clear) a 1px window border in `color` via DWM. The border tracks the
    // DWM-rounded edge and corners exactly — used instead of a GDI outline because a
    // self-painted line on the window's outermost pixels is hidden by the rounded
    // edge. Pass null to remove the border.
    public static void SetBorderColor(nint handle, Color? color)
    {
        if (handle == 0)
        {
            return;
        }

        try
        {
            // COLORREF is 0x00BBGGRR; DWMWA_COLOR_NONE removes the border.
            var value = color is Color c
                ? c.R | (c.G << 8) | (c.B << 16)
                : DWMWA_COLOR_NONE;
            DwmSetWindowAttribute(handle, DWMWA_BORDER_COLOR, ref value, sizeof(int));
        }
        catch
        {
            // dwmapi missing / attribute unsupported: no border.
        }
    }
}
