namespace TokenChecker.App;

// Writes a text file as atomically as the OS allows: serialize to a sibling temp
// file, then swap it into place in a single move. A crash or power loss mid-write
// leaves either the previous file or the complete new one — never a half-written
// file that would force a fallback-to-defaults on the next load.
//
// Shared by every store the app persists (settings.json / last_usage.json /
// copilot_usage.json) so the atomic-write rule lives in one place instead of being
// re-implemented per store. Callers still wrap this in their own try/catch:
// persistence must never take down the tray app.
internal static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Temp file in the SAME directory as the target so the final swap stays on
        // one volume (a cross-volume move is a copy, which is not atomic).
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, contents);

        // One unconditional same-volume rename (MoveFileEx with
        // MOVEFILE_REPLACE_EXISTING) — atomic whether or not the target already
        // exists. Avoids a File.Exists + File.Move TOCTOU where a file appearing
        // between the check and the move would throw and silently drop the write.
        File.Move(tempPath, path, overwrite: true);
    }
}
