using Xunit;

namespace TokenChecker.App.Tests;

// Exercises the atomic-write contract AtomicFile gives every store: a write either
// fully lands or leaves the previous file intact, and never leaves a stray .tmp on
// the happy path. AtomicFile is internal but visible here via InternalsVisibleTo.
// Each test works inside its own temp directory and removes it in a finally.
public class AtomicFileTests
{
    [Fact]
    public void WriteAllText_CreatesFileWithContents_WhenPathIsNew()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "settings.json");
            const string contents = "{\"hello\":\"world\"}";

            AtomicFile.WriteAllText(path, contents);

            Assert.True(File.Exists(path));
            Assert.Equal(contents, File.ReadAllText(path));
        }
        finally
        {
            CleanupTempDir(dir);
        }
    }

    [Fact]
    public void WriteAllText_ReplacesContents_WhenFileExists()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "data.json");
            File.WriteAllText(path, "old contents");

            AtomicFile.WriteAllText(path, "new contents");

            Assert.Equal("new contents", File.ReadAllText(path));
        }
        finally
        {
            CleanupTempDir(dir);
        }
    }

    [Fact]
    public void WriteAllText_LeavesNoTempFile_OnSuccess()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "usage.json");

            AtomicFile.WriteAllText(path, "payload");

            // The sibling temp (path + ".tmp") must have been swapped away.
            Assert.False(File.Exists(path + ".tmp"));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally
        {
            CleanupTempDir(dir);
        }
    }

    [Fact]
    public void WriteAllText_Throws_AndKeepsOriginalIntact_WhenTargetIsLocked()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "locked.json");
            const string original = "original payload";
            File.WriteAllText(path, original);

            // Hold the target open with FileShare.None: on Windows the final
            // File.Move onto the in-use target fails, so WriteAllText must throw
            // and the original file content must survive untouched. The failure can
            // surface as IOException or UnauthorizedAccessException depending on how
            // the OS reports the sharing violation, so assert on the common base.
            using (var locker = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var ex = Assert.ThrowsAny<Exception>(() => AtomicFile.WriteAllText(path, "should not land"));
                Assert.True(
                    ex is IOException or UnauthorizedAccessException,
                    $"Expected IOException or UnauthorizedAccessException but got {ex.GetType().Name}.");
            }

            Assert.Equal(original, File.ReadAllText(path));
        }
        finally
        {
            CleanupTempDir(dir);
        }
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "TokenCheckerWin.AtomicFileTests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void CleanupTempDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup; a leftover temp dir must not fail the test.
        }
    }
}
