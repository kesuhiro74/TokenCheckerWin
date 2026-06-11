namespace TokenChecker.App;

// GitHub Copilot plan. The bundled monthly AI-credit allowance is NOT exposed by
// the API, so the user picks a plan (or enters a custom cap) and the App overlays
// it at display time. None = no allowance (show raw used credits only).
internal enum CopilotPlan
{
    None = 0,
    Pro = 1,
    ProPlus = 2,
    Max = 3,
    Custom = 4,
    Free = 5
}
