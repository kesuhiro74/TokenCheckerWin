namespace TokenChecker.App;

// Accent color for the GitHub Copilot card's numbers + bar (and the tray %-bar):
// it sets the normal (<80%) "good" color, while the shared 80/95 severity
// escalation (amber/red) still applies. Blue is the default (see AppSettings);
// Green keeps value 0 only because it was the original look and the numeric
// values stay stable. Values are serialized by NAME (JsonStringEnumConverter),
// so older settings.json that stored "Blue"/"Sky"/"Slate" still parse correctly.
internal enum CopilotAccent
{
    Green = 0,
    Blue = 1,
    Sky = 2,
    Purple = 3,
    Slate = 4
}
