namespace NOVA.FIX.Core.Branding;

/// <summary>
/// SINGLE SOURCE OF TRUTH for official product creator information.
///
/// This information is part of the permanent product specification.
/// It must never be forgotten, removed, replaced with fictional information,
/// or silently modified during development.
///
/// All application components (About page, credits, installer metadata,
/// executable metadata, README, documentation, product information page)
/// MUST obtain creator details from this class. Do not duplicate conflicting
/// creator information anywhere else in the codebase.
///
/// The coding agent is a development tool, not the product's creator.
/// </summary>
public static class CreatorInformation
{
    /// <summary>Official creator / developer name.</summary>
    public const string CreatorName = "Danial Mousavi";

    /// <summary>Official Instagram handle.</summary>
    public const string InstagramHandle = "@dani.m7016";

    /// <summary>Official contact email (attribution only — never inserted into
    /// diagnostic reports and never transmitted anywhere).</summary>
    public const string ContactEmail = "daniel.865559@gmail.com";

    /// <summary>Official product name.</summary>
    public const string ProductName = "NOVA FIX";

    /// <summary>Product category.</summary>
    public const string ProductCategory =
        "Windows Diagnostic, Repair, Performance Optimization and System Maintenance Software.";

    /// <summary>Product tagline.</summary>
    public const string Tagline = "Diagnose. Repair. Optimize. Verify.";

    /// <summary>Attribution line shown in About/credits.</summary>
    public const string AttributionLine = "Created and Developed by Danial Mousavi";

    /// <summary>
    /// Renders the canonical About-page block. Copy-to-clipboard controls in the
    /// UI operate on these individual values.
    /// </summary>
    public static string RenderAboutBlock() =>
        $"{ProductName}\n" +
        $"{AttributionLine}\n" +
        $"Instagram: {InstagramHandle}\n" +
        $"Email: {ContactEmail}";

    /// <summary>Instagram URL for convenience links inside the app (opened only
    /// on explicit user action; never used automatically).</summary>
    public static string InstagramUrl => "https://instagram.com/dani.m7016";

    /// <summary>Mailto URL for the About page contact button (user-initiated only).</summary>
    public static string EmailMailtoUrl => $"mailto:{ContactEmail}";
}
