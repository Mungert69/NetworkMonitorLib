using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using NetworkMonitor.Objects.ServiceMessage;

namespace NetworkMonitor.Objects
{
    public static class PlanLimits
    {
        // Free
        public const int FreeHosts = 10;
        public const int FreeMaxTokens = 250_000;
        public const int FreeDailyTokens = 25_000;
        public const int FreeContext = 24_000;

        // Standard
        public const int StandardHosts = 50;
        public const int StandardMaxTokens = 500_000;
        public const int StandardDailyTokens = 100_000;
        public const int StandardContext = 32_000;

        // Professional
        public const int ProfessionalHosts = 300;
        public const int ProfessionalMaxTokens = 2_500_000;
        public const int ProfessionalDailyTokens = 500_000;
        public const int ProfessionalContext = 48_000;

        // Enterprise
        public const int EnterpriseHosts = 500;
        public const int EnterpriseMaxTokens = 7_500_000;
        public const int EnterpriseDailyTokens = 1_000_000;
        public const int EnterpriseContext = 64_000;

        // God (internal)
        public const int GodHosts = 1000;
        public const int GodMaxTokens = 10_000_000;
        public const int GodDailyTokens = 1_000_000;
        public const int GodContext = 128_000;
    }

    public static class PlanText
    {
        // 100_000 => "100k", 2_500_000 => "2500k"
        public static string ToK(int value)
            => (value % 1000 == 0) ? (value / 1000).ToString(CultureInfo.InvariantCulture) + "k"
                                   : value.ToString("N0", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Internal spec with ALL fields. This is our single source of truth.
    /// We derive both marketing JSON (PlanMarketing) and backend products from here.
    /// </summary>
    public sealed class PlanSpec
    {
        // Identity / display
        public string Name { get; init; } = "";
        public string? Subheader { get; init; }
        public string Price { get; init; } = "0";               // keep as string so React matches exactly
        public string ButtonText { get; init; } = "Get started";
        public string ButtonVariant { get; init; } = "outlined";
        public string[] Description { get; init; } = Array.Empty<string>();

        // Limits
        public int HostLimit { get; init; }
        public int MaxTokens { get; init; }
        public int DailyTokens { get; init; }
        public int ContextLimit { get; init; }

        // Payment / ops
        public bool Enabled { get; init; } = true;
        public string PriceId { get; init; } = "";
        public string SubscriptionUrl { get; init; } = "";
        public string SubscribeInstructions { get; init; } = "";
    }

    /// <summary>
    /// Frontend shape (camel case via JsonPropertyName) — exactly what React expects.
    /// </summary>
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

        public PlanMarketing(string title, string price, string[] description, string buttonText, string buttonVariant, string? subheader = null)
        {
            Title = title;
            Price = price;
            Description = description;
            ButtonText = buttonText;
            ButtonVariant = buttonVariant;
            Subheader = subheader;
        }
    }

    public static class PlanCatalog
    {
        /// <summary>
        /// Define ALL plans once. Edit here to change copy/limits/payment.
        /// </summary>
        public static readonly List<PlanSpec> Plans = new()
        {
            new PlanSpec
            {
                Name = "Free",
                Price = "0",
                HostLimit = PlanLimits.FreeHosts,
                MaxTokens = PlanLimits.FreeMaxTokens,
                DailyTokens = PlanLimits.FreeDailyTokens,
                ContextLimit = PlanLimits.FreeContext,
                ButtonText = "Sign up for free",
                ButtonVariant = "outlined",
                Description = new[]
                {
                    $"{PlanLimits.FreeHosts} hosts included",
                    "Basic ICMP, HTTP, DNS, and SMTP Ping",
                    $"{PlanText.ToK(PlanLimits.FreeMaxTokens)} Max tokens, {PlanText.ToK(PlanLimits.FreeDailyTokens)} added daily for the Turbo AI Assistant",
                    "FreeLLM Assistant for basic monitoring tasks and network setup guidance",
                    "Limited AI-driven security insights and health checks",
                    "1-month response data retention",
                    "Login required every 3 months",
                    $"Up to {PlanLimits.FreeContext:N0}-token context for the assistant"
                },
                SubscriptionUrl="",
                PriceId=""
            },

            new PlanSpec
            {
                Name = "Standard",
                Price = "2",
                HostLimit = PlanLimits.StandardHosts,
                MaxTokens = PlanLimits.StandardMaxTokens,
                DailyTokens = PlanLimits.StandardDailyTokens,
                ContextLimit = PlanLimits.StandardContext,
                ButtonText = "Get started",
                ButtonVariant = "outlined",
                Description = new[]
                {
                    $"Monitor up to {PlanLimits.StandardHosts} hosts",
                    "Local monitoring with Network Monitor + Quantum Secure Agents",
                    "Advanced ICMP/HTTP/DNS/Raw Connect/SMTP Ping + Quantum-Ready checks",
                    $"{PlanText.ToK(PlanLimits.StandardMaxTokens)} Max tokens, fill {PlanText.ToK(PlanLimits.StandardDailyTokens)} daily for Turbo AI Assistant",
                    "TurboLLM for intelligent alerts and enriched diagnostics",
                    "FreeLLM for routine commands and troubleshooting",
                    "AI recommendations on performance and security",
                    "Email support",
                    "6-month response data retention",
                    $"Up to {PlanLimits.StandardContext:N0}-token context for the assistant"
                },
                SubscriptionUrl="",
                PriceId=""
            },

            new PlanSpec
            {
                Name = "Professional",
                Subheader = "Most popular",
                Price = "5",
                HostLimit = PlanLimits.ProfessionalHosts,
                MaxTokens = PlanLimits.ProfessionalMaxTokens,
                DailyTokens = PlanLimits.ProfessionalDailyTokens,
                ContextLimit = PlanLimits.ProfessionalContext,
                ButtonText = "Get started",
                ButtonVariant = "contained",
                Description = new[]
                {
                    "Everything in Standard, plus:",
                    $"Monitor up to {PlanLimits.ProfessionalHosts} hosts with advanced tracking",
                    "Local & remote security assessments via Network Monitor + Quantum Secure Agents",
                    "Comprehensive health checks (ICMP/HTTP/DNS/Raw Connect/SMTP/Quantum-Ready)",
                    $"{PlanText.ToK(PlanLimits.ProfessionalMaxTokens)} Max tokens, fill {PlanText.ToK(PlanLimits.ProfessionalDailyTokens)} daily for Turbo AI Assistant",
                    "Advanced Turbo AI diagnostics with guided workflows",
                    "Security & Penetration Expert LLMs for audits and threat detection",
                    "Predictive AI to catch issues before they escalate",
                    "Priority email support",
                    "2-year response data retention",
                    $"Up to {PlanLimits.ProfessionalContext:N0}-token context for the assistant"
                },
                SubscriptionUrl="",
                PriceId=""
            },

            new PlanSpec
            {
                Name = "Enterprise",
                Price = "10",
                HostLimit = PlanLimits.EnterpriseHosts,
                MaxTokens = PlanLimits.EnterpriseMaxTokens,
                DailyTokens = PlanLimits.EnterpriseDailyTokens,
                ContextLimit = PlanLimits.EnterpriseContext,
                ButtonText = "Get started",
                ButtonVariant = "outlined",
                Description = new[]
                {
                    "Everything in Professional, plus:",
                    $"Monitor up to {PlanLimits.EnterpriseHosts} hosts with dedicated support",
                    $"{PlanText.ToK(PlanLimits.EnterpriseMaxTokens)} Max tokens, fill {PlanText.ToK(PlanLimits.EnterpriseDailyTokens)} daily for Turbo AI Assistant",
                    "Access to the most advanced Turbo AI features for real-time, large-scale ops",
                    "BusyBox command execution on agents for deep automation",
                    "Unlimited FreeLLM usage for all queries",
                    "One high-priority dedicated monitor service agent (datacenter)",
                    "Unlimited response data retention & export",
                    $"Up to {PlanLimits.EnterpriseContext:N0}-token context for the assistant"
                },
                SubscriptionUrl="",
                PriceId=""
            },

            // Internal/God (not exposed to frontend)
            new PlanSpec
            {
                Name = "God",
                Price = "0",
                HostLimit = PlanLimits.GodHosts,
                MaxTokens = PlanLimits.GodMaxTokens,
                DailyTokens = PlanLimits.GodDailyTokens,
                ContextLimit = PlanLimits.GodContext,
                Enabled = false, // hidden
                Description = Array.Empty<string>()
            }
        };

        /// <summary>
        /// What the frontend needs (/User/Tiers). Exactly like the original "tiers" object.
        /// </summary>
        public static List<PlanMarketing> GetMarketingTiers() =>
            Plans.Where(p => p.Enabled && p.Name != "God")
                 .Select(p => new PlanMarketing(
                     p.Name,
                     p.Price,
                     p.Description,
                     p.ButtonText,
                     p.ButtonVariant,
                     p.Subheader
                 ))
                 .ToList();

        /// <summary>
        /// What AccountTypeFactory needs for host/token/context limits.
        /// </summary>
        public static Dictionary<string, (int hostLimit, int tokenLimit, int dailyTokens, int contextSize)>
            BuildAccountTypeConfigurations() =>
            Plans.ToDictionary(
                p => p.Name,
                p => (p.HostLimit, p.MaxTokens, p.DailyTokens, p.ContextLimit)
            );

        /// <summary>
        /// What back-end product consumers need (UpdateProductObj with ProductObj entries).
        /// Keeps your ProductObj with backing fields intact.
        /// </summary>
        public static UpdateProductObj BuildProducts(IConfiguration config)
        {
            // From appsettings.json:
            // "PaymentServerUrl": "https://devpayment.readyforquantum.com",
            // "SubscriptionUrls": { "Standard": "...", "Professional": "...", "Enterprise": "..." },
            // "PriceIds":        { "Standard": "price_...", "Professional": "price_...", "Enterprise": "price_..." }

            var paymentServerUrl = config.GetValue<string>("PaymentServerUrl") ?? "";

            // Plan-name -> purchase URL
            var subscriptionUrls = config
                .GetSection("SubscriptionUrls")
                .Get<Dictionary<string, string>>()
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Plan-name -> Stripe price id
            var priceIds = config
                .GetSection("PriceIds")
                .Get<Dictionary<string, string>>()
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var products = Plans
                .Where(p => p.Enabled && !string.Equals(p.Name, "God", StringComparison.OrdinalIgnoreCase))
                .Select(p =>
                {
                    var prod = new ProductObj
                    {
                        // Prefer config overrides; otherwise fall back to PlanSpec
                        PriceId = (priceIds.TryGetValue(p.Name, out var priceIdFromConfig) && !string.IsNullOrWhiteSpace(priceIdFromConfig))
                                  ? priceIdFromConfig
                                  : (p.PriceId ?? ""),

                        ProductName = p.Name,
                        HostLimit = p.HostLimit,
                        Quantity = 1,
                        Description = string.Join(" | ", p.Description ?? Array.Empty<string>()),
                        Enabled = true,
                        Price = int.TryParse(p.Price, out var priceVal) ? priceVal : 0,

                        SubscriptionUrl = (subscriptionUrls.TryGetValue(p.Name, out var urlFromConfig) && !string.IsNullOrWhiteSpace(urlFromConfig))
                                          ? urlFromConfig
                                          : (p.SubscriptionUrl ?? ""),

                        SubscribeInstructions = p.SubscribeInstructions
                    };

                    return prod;
                })
                .ToList();

            return new UpdateProductObj
            {
                Products = products,
                PaymentServerUrl = paymentServerUrl
            };
        }


    }
}
