using System.Text.Json;
using Experion.Api.Providers;

namespace Experion.Api.Data;

public static class Seeder
{
    public static async Task SeedAsync(ExperionDbContext db, IEmbeddingProvider embed, CancellationToken ct = default)
    {
        if (!db.Tenants.Any())
        {
            db.Tenants.Add(new TenantConfig
            {
                TenantId = "default",
                Name = "Acme Marketplace",
                GreetingMessage = "Hi! I'm Experion. I can answer questions about Acme products, navigate the site, or take actions for you.",
                IdleNudgeSeconds = 25,
                NudgeCooldownSeconds = 45,
                KbContent = string.Join("\n\n", new[]
                {
                    "## Acme Marketplace Overview\nAcme Marketplace is a B2B platform that lets buyers browse, compare, and purchase enterprise solutions across categories like AI, Cloud, Security, and Analytics.",
                    "## Pricing\nAcme uses a subscription model. Plans are Free, Pro ($29/mo), and Enterprise (custom). Pro unlocks advanced search and saved comparisons.",
                    "## Refund Policy\nFull refunds available within 14 days of purchase. After 14 days, prorated refunds may be issued at our discretion.",
                    "## Categories\nMain product categories: AI Agents, Cloud Infrastructure, Cybersecurity, Data & Analytics, Productivity, Developer Tools.",
                    "## Cart & Checkout\nUsers can add items to a cart, apply coupon codes, and check out via card, bank transfer, or invoice billing.",
                    "## Account & Profile\nUsers can manage their profile, billing details, and team seats from the Account → Settings page.",
                    "## Support\nFor support, email support@acme.example or open a ticket from the in-app help widget."
                })
            });
        }

        if (!db.ActionMappings.Any())
        {
            var mappings = new List<(string Key, string Desc, string Phrases)>
            {
                ("add_to_cart",      "Add the currently viewed product to the cart.",
                                     "add to cart|put this in my cart|buy this|i want to buy this|add this product|add it to the basket"),
                ("checkout",         "Take the user to checkout.",
                                     "checkout|complete my order|pay now|finish my purchase|proceed to payment"),
                ("apply_coupon",     "Apply a discount or coupon code.",
                                     "apply coupon|use a discount|enter promo code|apply discount code|i have a coupon"),
                ("open_profile",     "Open the user profile / account settings page.",
                                     "open my profile|account settings|update my account|edit my profile|go to settings"),
                ("contact_support",  "Open a support ticket or email support.",
                                     "contact support|talk to a human|raise a ticket|i need help from support|open a support case"),
                ("logout",           "Sign the user out of the application.",
                                     "log me out|sign out|logout|end my session"),
            };

            foreach (var (key, desc, phrases) in mappings)
            {
                var corpus = $"{desc} {phrases.Replace('|', ' ')}";
                var vec = await embed.EmbedAsync(corpus, ct);
                db.ActionMappings.Add(new ActionMapping
                {
                    TenantId = "default",
                    ActionKey = key,
                    Description = desc,
                    Phrases = phrases,
                    Embedding = JsonSerializer.Serialize(vec),
                    Enabled = true
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
