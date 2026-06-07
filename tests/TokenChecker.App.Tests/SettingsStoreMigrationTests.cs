using Xunit;

namespace TokenChecker.App.Tests;

// Locks down the one-shot legacy migrations. ApplyLegacyMigrations is driven
// directly from a JSON string (field-presence gating) so every branch is covered
// without touching the real settings.json. Confirms the removed legacy fields
// (TrayIconMode / CopilotWindowTrigger / ShowOnStartup) still migrate correctly
// and are NOT revived.
public class SettingsStoreMigrationTests
{
    [Fact]
    public void CompactMode_MigratesToDisplayMode_WhenDisplayModeAbsent()
    {
        var settings = new AppSettings { CompactMode = true };
        SettingsStore.ApplyLegacyMigrations("""{"CompactMode": true}""", settings);
        Assert.Equal(DisplayMode.Compact, settings.DisplayMode);
    }

    [Fact]
    public void CompactMode_NotMigrated_WhenDisplayModePresent()
    {
        var settings = new AppSettings { CompactMode = true, DisplayMode = DisplayMode.Normal };
        SettingsStore.ApplyLegacyMigrations("""{"CompactMode": true, "DisplayMode": "Normal"}""", settings);
        Assert.Equal(DisplayMode.Normal, settings.DisplayMode);
    }

    [Fact]
    public void ShowOnStartupFalse_MapsBothWindowsToHoverPreview()
    {
        var settings = new AppSettings();
        SettingsStore.ApplyLegacyMigrations("""{"ShowOnStartup": false}""", settings);
        Assert.Equal(WindowDisplayMode.HoverPreview, settings.ClaudeCodexDisplayMode);
        Assert.Equal(WindowDisplayMode.HoverPreview, settings.CopilotDisplayMode);
    }

    [Fact]
    public void CopilotWindowTrigger_MapsCopilotToHoverPreview()
    {
        var settings = new AppSettings();
        SettingsStore.ApplyLegacyMigrations("""{"CopilotWindowTrigger": "Click"}""", settings);
        Assert.Equal(WindowDisplayMode.HoverPreview, settings.CopilotDisplayMode);
    }

    [Fact]
    public void VisibleServicesCopilot_EnablesCopilotWindow()
    {
        var settings = new AppSettings { VisibleServices = ["Claude", "GitHub Copilot"] };
        SettingsStore.ApplyLegacyMigrations("""{"VisibleServices": ["Claude", "GitHub Copilot"]}""", settings);
        Assert.True(settings.CopilotWindowEnabled);
    }

    [Fact]
    public void TrayIconModeCopilot_EnablesCopilotWindow()
    {
        var settings = new AppSettings();
        SettingsStore.ApplyLegacyMigrations("""{"TrayIconMode": "Copilot"}""", settings);
        Assert.True(settings.CopilotWindowEnabled);
    }

    [Fact]
    public void CopilotWindowEnabledPresent_NoMigration()
    {
        var settings = new AppSettings { CopilotWindowEnabled = false, VisibleServices = ["GitHub Copilot"] };
        SettingsStore.ApplyLegacyMigrations("""{"CopilotWindowEnabled": false, "VisibleServices": ["GitHub Copilot"]}""", settings);
        Assert.False(settings.CopilotWindowEnabled);
    }
}
