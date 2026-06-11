namespace TokenChecker.App;

// Daily-burn severity bands for the Copilot today-delta SPARK ICON (the text stays
// the user's accent color; only the icon escalates green/amber/red). internal so
// TokenChecker.App.Tests can assert the boundary mapping.
internal enum DeltaSeverity
{
    Normal,
    Alert,
    Red
}
