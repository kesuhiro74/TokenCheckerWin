namespace TokenChecker.App;

// How a popup window is surfaced from its dedicated tray icon (per window).
internal enum WindowDisplayMode
{
    // Shown whenever the app is running while the window is enabled; re-openable
    // (tray click / menu) if the user closes it.
    Always = 0,

    // Hovering the window's tray icon fades it in; moving the mouse off the window
    // hides it immediately (with a small grace for the icon->window move). Clicking
    // the tray icon pins it (treated as always-visible until unpinned/closed).
    HoverPreview = 1
}
