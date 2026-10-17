using TokenChecker.Core;

namespace TokenChecker.App;

// Decides WHICH Copilot plan/allowance the UI shows: the one auto-detected from
// GitHub (ServiceExtras.PlanLabel / CreditAllowance, filled by the provider's
// best-effort /copilot_internal/user read) or the one picked in settings.
//
// Pure and static on purpose — TrayApplicationContext exposes exactly two
// instance wrappers over it (EffectiveCopilotAllowance / EffectiveCopilotPlanTitle)
// and EVERY consumer of the allowance (card, tray %-bar and warning spark, tray
// tooltip, CopilotUsageTracker projection / today's delta, daily budget) goes
// through those, so a detected allowance can never be applied in one place and
// missed in another. The settings dialog is the one exception: it edits the
// fallback value itself.
internal static class CopilotPlanResolver
{
    // The extras the displayed numbers come from: the current sample when it is
    // Available, otherwise the last successful one (which is also what the card
    // reads its used credits out of). A non-Available current sample carries no
    // extras anyway, but being explicit keeps the rule in one place.
    internal static ServiceExtras? PickExtras(ServiceUsage? current, ServiceUsage? fallback)
        => current?.Status == ProviderStatus.Available
            ? current.Extras
            : fallback?.Extras;

    // Auto-detected allowance wins over the settings plan; neither -> null (the
    // card then shows raw credits and the hint to pick a plan).
    //
    // The API reports a whole number of credits (entitlement); it is rounded
    // away from zero and accepted only when positive and representable as int,
    // because every downstream consumer takes int? (an absurd value therefore
    // degrades to the settings plan instead of overflowing).
    internal static int? Allowance(ServiceExtras? extras, int? settingsAllowance)
    {
        if (extras?.CreditAllowance is decimal detected && detected > 0m && detected <= int.MaxValue)
        {
            return (int)Math.Round(detected, MidpointRounding.AwayFromZero);
        }

        return settingsAllowance;
    }

    // The card's title: the auto-detected plan label when GitHub supplied one,
    // otherwise the title derived from the settings plan. A label is never built
    // from a raw plan code (see CopilotInternalUserParser).
    internal static string Title(ServiceExtras? extras, string settingsTitle)
        => string.IsNullOrWhiteSpace(extras?.PlanLabel) ? settingsTitle : extras!.PlanLabel!;
}
