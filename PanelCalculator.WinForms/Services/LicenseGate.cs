using System;
using System.IO;
using PanelCalculator.Core.Security;
using PanelCalculator.Data;
using PanelCalculator.Data.Security;
using PanelCalculator.WinForms.Forms;
using RabKit.Branding;

namespace PanelCalculator.WinForms.Services;

/// <summary>
/// Boundary between Program.Main and the license / activation flow. Kept
/// separate so the activation policy (debug bypass, env var override,
/// developer-mode flag file) can be reasoned about and tested in one place.
/// </summary>
internal static class LicenseGate
{
    /// <summary>
    /// Marker file checked at startup. If present, the license check is
    /// skipped — for engineers running Release builds on dev machines.
    /// Place under repo-root/Tools/ so it lives next to the keygen tool.
    /// </summary>
    public const string DevBypassFlagRelativePath = "Tools/dev-bypass.flag";

    /// <summary>
    /// Env var that FORCES the license check on even in Debug builds.
    /// Used by CI / acceptance test runs that need to exercise the gate.
    /// Value must be exactly "1" to enable.
    /// </summary>
    public const string EnforceEnvVar = "PANELCALC_ENFORCE_LICENSE";

    /// <summary>
    /// Outcome of the gate evaluation.
    /// </summary>
    public enum Outcome
    {
        /// <summary>Continue startup as normal — license valid OR bypass active.</summary>
        Pass,
        /// <summary>User cancelled the activation dialog. Caller should exit gracefully.</summary>
        UserCancelled,
    }

    /// <summary>
    /// Main entry point. Reads license from <paramref name="context"/>,
    /// honours bypass rules, and shows <see cref="ActivationForm"/> if needed.
    /// </summary>
    public static Outcome RunGate(PanelCalculatorContext context, Action<string> logWarning)
    {
        bool enforceOverride = string.Equals(
            Environment.GetEnvironmentVariable(EnforceEnvVar), "1", StringComparison.Ordinal);

        // 1. Developer bypass — only when NOT forced.
        if (!enforceOverride && IsDeveloperBypassActive())
        {
            logWarning(
                $"License check bypassed (Debug build OR {DevBypassFlagRelativePath} present). " +
                $"Set {EnforceEnvVar}=1 to override.");
            return Outcome.Pass;
        }

        // 2. Read existing license, if any.
        var stored = context.Settings.FirstOrDefault(s => s.SettingKey == LicenseSettings.LicenseKeySettingName);
        if (stored != null && !string.IsNullOrWhiteSpace(stored.SettingValue))
        {
            var fp = MachineKeyProvider.GetHardwareFingerprintBytes();
            var result = LicenseService.ValidateLicense(stored.SettingValue, fp);
            if (result.IsValid)
            {
                // W4: also seed EditionContext.CurrentLicense so the title bar
                // and policy gates can read tier/edition info. Failure here is
                // non-fatal — LicenseService already accepted the license, and
                // an EditionMismatch / Expired would be a configuration bug
                // we want to log not block the customer over.
                TrySeedEditionContext(stored.SettingValue, fp, logWarning);
                return Outcome.Pass;
            }

            // Otherwise fall through to ActivationForm — the stored license
            // no longer matches this machine (hardware changed / DB cloned).
        }

        // 3. Block: show activation dialog.
        using var dlg = new ActivationForm(context);
        dlg.ShowDialog();
        if (!dlg.ActivationSuccess)
            return Outcome.UserCancelled;

        // After successful activation, seed EditionContext so the title bar
        // tier label + downstream policy gates have the freshly-activated
        // license metadata available. Re-read from DB (ActivationForm writes
        // the license to Settings before closing).
        var fresh = context.Settings.FirstOrDefault(s => s.SettingKey == LicenseSettings.LicenseKeySettingName);
        if (fresh != null && !string.IsNullOrWhiteSpace(fresh.SettingValue))
        {
            var fpAfter = MachineKeyProvider.GetHardwareFingerprintBytes();
            TrySeedEditionContext(fresh.SettingValue, fpAfter, logWarning);
        }
        return Outcome.Pass;
    }

    /// <summary>
    /// W4: bridge LicenseService (which validates Ed25519 + hw fingerprint)
    /// to EditionContext (which adds edition/tier/industry/expiry/manifest
    /// cross-check). Splits the decode from the validate so both layers
    /// can share the same bytes without double-work.
    /// </summary>
    private static void TrySeedEditionContext(string licenseKey, byte[] fingerprint, Action<string> logWarning)
    {
        try
        {
            if (!EditionContext.IsInitialized)
            {
                logWarning("EditionContext not initialized — skipping V2 claim seeding.");
                return;
            }

            var decoded = LicenseDecoder.Decode(licenseKey);
            var input = new DecodedLicenseInput(
                Payload: decoded.Payload,
                Signature: decoded.Signature,
                FormatVersion: decoded.Version,
                HardwareFingerprint: decoded.HardwareFingerprint,
                CustomerName: decoded.CustomerName,
                IssueDateUtc: decoded.IssueDateUtc,
                EditionId: decoded.EditionId,
                Tier: decoded.Tier,
                Industry: decoded.Industry,
                ExpiresAtUtc: decoded.ExpiresAtUtc,
                Features: decoded.Features);

            var ed = EditionContext.ValidateAndSetLicense(input, fingerprint);
            if (!ed.IsValid)
            {
                logWarning($"EditionContext validation failed: {ed.Reason} — {ed.Detail}");
            }
        }
        catch (Exception ex)
        {
            logWarning($"EditionContext seeding threw: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Bypass policy: active when either
    ///   (a) the build is Debug, OR
    ///   (b) the file <see cref="DevBypassFlagRelativePath"/> exists alongside the EXE
    ///       OR at the repository root (whichever resolves first).
    /// </summary>
    public static bool IsDeveloperBypassActive()
    {
#if DEBUG
        return true;
#else
        return ResolveBypassFlagPaths().Any(File.Exists);
#endif
    }

    /// <summary>Candidate locations the gate scans for the bypass flag file.</summary>
    private static System.Collections.Generic.IEnumerable<string> ResolveBypassFlagPaths()
    {
        // 1. Next to the EXE
        var exeDir = AppContext.BaseDirectory;
        yield return Path.Combine(exeDir, "dev-bypass.flag");
        yield return Path.Combine(exeDir, DevBypassFlagRelativePath);

        // 2. Walk up to find a repo root containing a "Tools" folder (dev scenario)
        var dir = new DirectoryInfo(exeDir);
        for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
        {
            yield return Path.Combine(dir.FullName, DevBypassFlagRelativePath);
        }
    }
}
