using System.Text.Json.Serialization;
using System.Globalization;
using System.Collections.Generic;

namespace NetworkMonitor.Objects;

public static class PlanLimits
{
    // ===== Hosts =====
    public const int FreeHosts         = 10;
    public const int StandardHosts     = 50;
    public const int ProfessionalHosts = 300;
    public const int EnterpriseHosts   = 500;
    public const int GodHosts          = 1000;

    // ===== Token limits (Max / Daily) =====
    public const int FreeMaxTokens         = 100_000;
    public const int FreeDailyTokens       = 25_000;

    public const int StandardMaxTokens     = 500_000;
    public const int StandardDailyTokens   = 100_000;

    public const int ProfessionalMaxTokens = 2_500_000;
    public const int ProfessionalDailyTokens = 500_000;

    public const int EnterpriseMaxTokens   = 7_500_000;
    public const int EnterpriseDailyTokens = 1_000_000;

    public const int GodMaxTokens          = 10_000_000;
    public const int GodDailyTokens        = 1_000_000;

    // ===== Context window (tokens) =====
    public const int FreeContextSize         = 12_000;
    public const int StandardContextSize     = 16_000;
    public const int ProfessionalContextSize = 32_000;
    public const int EnterpriseContextSize   = 32_000;
    public const int GodContextSize          = 128_000;
}

public static class PlanText
{
    // 100_000 => "100k", 2_500_000 => "2500k"
    public static string ToK(int value)
        => (value % 1000 == 0) ? (value / 1000).ToString(CultureInfo.InvariantCulture) + "k"
                               : value.ToString("N0", CultureInfo.InvariantCulture);
}

public class PlanMarketing
{
    [JsonPropertyName("title")]
    public string Title { get; init; }

    [JsonPropertyName("subheader")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Subheader { get; init; }

    [JsonPropertyName("price")]
    public string Price { get; init; }

    [JsonPropertyName("description")]
    public string[] Description { get; init; }

    [JsonPropertyName("buttonText")]
    public string ButtonText { get; init; }

    [JsonPropertyName("buttonVariant")]
    public string ButtonVariant { get; init; }

    public PlanMarketing(
        string title,
        string price,
        string[] description,
        string buttonText,
        string buttonVariant,
        string? subheader = null)
    {
        Title = title;
        Price = price;
        Description = description;
        ButtonText = buttonText;
        ButtonVariant = buttonVariant;
        Subheader = subheader;
    }
}
public static class TierCatalog
{
    public static readonly List<PlanMarketing> Tiers = new()
    {
        new PlanMarketing(
            "Free",
            "0",
            new[]
            {
                $"{PlanLimits.FreeHosts} hosts included",
                "ICMP, HTTP, DNS & SMTP ping checks",
                $"{PlanText.ToK(PlanLimits.FreeMaxTokens)} max tokens, {PlanText.ToK(PlanLimits.FreeDailyTokens)} daily",
                $"{PlanText.ToK(PlanLimits.FreeContextSize)}-token AI context for quick triage",
                "FreeLLM Assistant for basic setup & checks",
                "Limited AI security insights & health checks",
                "1-month response retention",
                "Must login every 3 months"
            },
            "Sign up for free",
            "outlined"
        ),

        new PlanMarketing(
            "Standard",
            "2",
            new[]
            {
                $"Monitor up to {PlanLimits.StandardHosts} hosts",
                "Local monitoring with Network Monitor & Quantum Secure Agents",
                "Advanced checks: ICMP, HTTP, DNS, Raw Connect, SMTP, Quantum-Ready",
                $"{PlanText.ToK(PlanLimits.StandardMaxTokens)} max tokens, {PlanText.ToK(PlanLimits.StandardDailyTokens)} daily",
                $"{PlanText.ToK(PlanLimits.StandardContextSize)}-token AI context for deeper analysis",
                "TurboLLM alerts with guided remediation",
                "AI recommendations for performance & security",
                "Email support • 6-month retention"
            },
            "Get started",
            "outlined"
        ),

        new PlanMarketing(
            "Professional",
            "5",
            new[]
            {
                "Everything in Standard, plus:",
                $"Up to {PlanLimits.ProfessionalHosts} hosts with advanced tracking",
                "Local & remote security assessments and pen-tests",
                "Full health checks: ICMP, HTTP, DNS, Raw Connect, SMTP, Quantum-Ready",
                $"{PlanText.ToK(PlanLimits.ProfessionalMaxTokens)} max tokens, {PlanText.ToK(PlanLimits.ProfessionalDailyTokens)} daily",
                $"{PlanText.ToK(PlanLimits.ProfessionalContextSize)}-token AI context for full-stack investigations",
                "Security & Penetration Expert LLMs for audits and threat hunting",
                "Predictive AI to preempt incidents",
                "Priority email support • 2-year retention"
            },
            "Get started",
            "contained",
            "Most popular"
        ),

        new PlanMarketing(
            "Enterprise",
            "10",
            new[]
            {
                "Everything in Professional, plus:",
                $"Up to {PlanLimits.EnterpriseHosts} hosts with dedicated support",
                $"{PlanText.ToK(PlanLimits.EnterpriseMaxTokens)} max tokens, {PlanText.ToK(PlanLimits.EnterpriseDailyTokens)} daily",
                $"{PlanText.ToK(PlanLimits.EnterpriseContextSize)}-token AI context for large-scale operations",
                "Exclusive Turbo AI for real-time, large-scale monitoring & deep pen-testing",
                "BusyBox command execution on agents for advanced automation",
                "Unlimited FreeLLM usage & dedicated high-priority monitor service agent",
                "Unlimited retention & export"
            },
            "Get started",
            "outlined"
        )
    };
}


