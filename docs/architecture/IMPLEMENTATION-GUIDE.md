# Experion v2 — Implementation Guide

## Quick Reference: What to Build

This document maps the architecture to concrete implementation tasks, organized by phase.

---

## Project Structure (Target)

```
experion/
├── src/
│   ├── Experion.API/                    # ASP.NET Core Web API
│   │   ├── Controllers/
│   │   │   ├── ActivityController.cs
│   │   │   ├── ChatController.cs
│   │   │   ├── FeedbackController.cs
│   │   │   ├── AdminController.cs
│   │   │   └── HealthController.cs
│   │   ├── Hubs/
│   │   │   └── RecommendationHub.cs     # SignalR Hub
│   │   ├── Middleware/
│   │   │   ├── TenantResolutionMiddleware.cs
│   │   │   ├── IdentityResolutionMiddleware.cs
│   │   │   └── RateLimitingMiddleware.cs
│   │   ├── Program.cs
│   │   ├── appsettings.json
│   │   └── Experion.API.csproj
│   │
│   ├── Experion.Core/                   # Domain (zero external dependencies)
│   │   ├── Interfaces/
│   │   │   ├── IActivityService.cs
│   │   │   ├── IChatOrchestrator.cs
│   │   │   ├── IRecommendationEngine.cs
│   │   │   ├── IIdentityResolver.cs
│   │   │   ├── IProfileStore.cs
│   │   │   ├── IActivityStore.cs
│   │   │   ├── IConversationStore.cs
│   │   │   ├── IKnowledgeBaseService.cs
│   │   │   └── IActionConnector.cs
│   │   ├── Models/
│   │   │   ├── ActivityEvent.cs
│   │   │   ├── ActivityBatch.cs
│   │   │   ├── UserProfile.cs
│   │   │   ├── SessionState.cs
│   │   │   ├── Recommendation.cs
│   │   │   ├── ChatMessage.cs
│   │   │   ├── ChatConversation.cs
│   │   │   └── TenantConfig.cs
│   │   ├── Enums/
│   │   │   ├── EventType.cs
│   │   │   ├── IntentType.cs
│   │   │   ├── RecommendationType.cs
│   │   │   └── TriggerType.cs
│   │   └── Experion.Core.csproj
│   │
│   ├── Experion.Infrastructure/         # External integrations
│   │   ├── Storage/
│   │   │   ├── BlobActivityStore.cs
│   │   │   ├── BlobProfileStore.cs
│   │   │   ├── BlobConversationStore.cs
│   │   │   └── BlobSummaryStore.cs
│   │   ├── AI/
│   │   │   ├── AzureOpenAIChatService.cs
│   │   │   ├── AzureOpenAIEmbeddingService.cs
│   │   │   └── PromptTemplateService.cs
│   │   ├── Search/
│   │   │   └── AzureAISearchService.cs
│   │   ├── Identity/
│   │   │   ├── FingerprintResolver.cs
│   │   │   └── GeoIpResolver.cs
│   │   ├── Connectors/
│   │   │   ├── IActionConnector.cs
│   │   │   ├── PowerAutomateConnector.cs
│   │   │   ├── WebhookConnector.cs
│   │   │   └── ConnectorRegistry.cs
│   │   ├── Recommendations/
│   │   │   ├── TriggerEvaluator.cs
│   │   │   ├── ContextAssembler.cs
│   │   │   ├── RecommendationGenerator.cs
│   │   │   └── DeliveryManager.cs
│   │   ├── BackgroundJobs/
│   │   │   ├── ProfileSummaryJob.cs
│   │   │   └── ActivityArchiveJob.cs
│   │   ├── Configuration/
│   │   │   └── ExperionSettings.cs
│   │   └── Experion.Infrastructure.csproj
│   │
│   └── Experion.Tests/                  # Unit + integration tests
│       └── Experion.Tests.csproj
│
├── sdk/
│   ├── src/
│   │   ├── core/
│   │   │   ├── EventBus.js
│   │   │   ├── Config.js
│   │   │   └── Logger.js
│   │   ├── identity/
│   │   │   ├── Fingerprinter.js
│   │   │   └── IdentityManager.js
│   │   ├── tracking/
│   │   │   ├── PageViewTracker.js
│   │   │   ├── ClickTracker.js
│   │   │   ├── ScrollTracker.js
│   │   │   ├── TimeTracker.js
│   │   │   ├── FormTracker.js
│   │   │   └── EventBatcher.js
│   │   ├── gesture/
│   │   │   ├── GestureDetector.js
│   │   │   └── DomExtractor.js
│   │   ├── transport/
│   │   │   ├── RestClient.js
│   │   │   ├── WebSocketClient.js
│   │   │   └── OfflineQueue.js
│   │   ├── ui/
│   │   │   ├── Sphere.js
│   │   │   ├── Sidebar.js
│   │   │   ├── RecommendationToast.js
│   │   │   └── Styles.js
│   │   └── index.js
│   ├── dist/
│   │   └── experion.min.js             # Built SDK bundle
│   ├── package.json
│   └── rollup.config.js               # Bundle tool config
│
├── docs/
│   └── architecture/                   # This folder
│
└── experion.sln
```

---

## Phase 1: Standalone Backend

**Goal**: Extract Experion from AGONEAIHub into its own deployable service.

### Key Changes
1. Create `Experion.API`, `Experion.Core`, `Experion.Infrastructure` projects
2. Move `ExperionOrchestrator`, `DomFragmentCleanerService` into new structure
3. Port the chat pipeline (context detection → KB search → response generation)
4. Set up Blob Storage clients (Azure.Storage.Blobs SDK)
5. Configure as standalone App Service deployment

### NuGet Packages
```xml
<!-- Experion.API -->
<PackageReference Include="Microsoft.AspNetCore.SignalR" />

<!-- Experion.Infrastructure -->
<PackageReference Include="Azure.Storage.Blobs" />
<PackageReference Include="Azure.Search.Documents" />
<PackageReference Include="Azure.AI.OpenAI" />
<PackageReference Include="MaxMind.GeoIP2" />
<PackageReference Include="Microsoft.Extensions.Caching.StackExchangeRedis" />
```

---

## Phase 2: Activity Mining

**Goal**: SDK sends behavioral events; backend stores and indexes them.

### SDK Work
- Implement `PageViewTracker`, `ClickTracker`, `ScrollTracker`, `TimeTracker`
- Implement `EventBatcher` (5-second batch interval)
- Implement `Fingerprinter` and `IdentityManager`

### Backend Work
- `ActivityController.cs` — POST /api/v2/activity/batch endpoint
- `BlobActivityStore.cs` — Append to JSONL append blobs
- `IdentityResolutionMiddleware.cs` — Resolve ProfileId on every request
- `GeoIpResolver.cs` — MaxMind GeoLite2 lookup
- `FingerprintResolver.cs` — Map fingerprint → ProfileId in Blob

---

## Phase 3: Recommendation Engine

**Goal**: Proactively suggest things to users based on their behavior.

### Backend Work
- `TriggerEvaluator.cs` — Evaluate rules (action count, time, patterns)
- `ContextAssembler.cs` — Build LLM context from profile + activity + KB
- `RecommendationGenerator.cs` — LLM call to produce suggestion
- `DeliveryManager.cs` — Dedup, frequency cap, channel selection
- `RecommendationHub.cs` — SignalR hub for push delivery
- `ProfileSummaryJob.cs` — Background job to generate LLM profile summaries

### SDK Work
- `WebSocketClient.js` — Connect to SignalR hub for push recommendations
- `RecommendationToast.js` — UI for displaying recommendations

---

## Phase 4: Multi-Tenant + Connectors

**Goal**: Support multiple websites from a single deployment. Replace hardcoded integrations.

### Backend Work
- `TenantConfig.cs` model + `AdminController.cs` for CRUD
- `TenantResolutionMiddleware.cs` — Extract tenant from API key
- `ConnectorRegistry.cs` — Plugin discovery and registration
- `PowerAutomateConnector.cs`, `WebhookConnector.cs` — Example connectors
- Per-tenant settings stored in Blob (not hardcoded in appsettings)

---

## Key Architecture Decisions

| Decision | Rationale |
|----------|-----------|
| **Blob Storage over SQL for activity** | Activity logs are high-volume append-only data. Blob append blobs are perfect. SQL would require partitioning and would cost more. |
| **JSONL format** | One JSON object per line. Easy to append, easy to stream-process, easy to compress. No read-modify-write race conditions. |
| **SignalR for push** | Native .NET support. Azure SignalR Service handles scaling. Falls back gracefully to long-polling. |
| **Separate Identity Resolution** | Decouples user tracking from business logic. Clean middleware pattern. |
| **LLM-generated profile summaries** | Instead of complex aggregation queries, let the LLM summarize activity logs into natural language. Cheaper than real-time aggregation and more useful for downstream LLM prompts. |
| **Action Connector Registry** | Prevents hardcoding integrations. New connectors can be added without modifying core logic. LLM discovers connectors as tools. |
| **Client-side fingerprinting** | More reliable than server-side. Survives IP changes. Privacy-respecting (one-way hash, no PII). |

---

## API Contract Summary

### POST /api/v2/activity/batch

```json
// Request
{
  "tenantId": "acme-corp",
  "sessionId": "sess_abc123",
  "userId": null,
  "fingerprint": "fp_xyz789",
  "events": [
    { "type": "page_view", "url": "/products", "title": "Products", "ts": "..." },
    { "type": "click", "selector": "#buy-btn", "text": "Buy Now", "ts": "..." }
  ]
}

// Response: 202 Accepted
{ "profileId": "fp_xyz789", "sessionId": "sess_abc123" }
```

### POST /api/v2/chat/message

```json
// Request
{
  "tenantId": "acme-corp",
  "sessionId": "sess_abc123",
  "conversationId": "conv_001",
  "message": "What integrations does Widget Pro support?"
}

// Response
{
  "success": true,
  "message": "Widget Pro supports REST API, GraphQL, and webhook integrations...",
  "relatedDocuments": [...],
  "suggestions": ["Tell me about pricing", "Show API examples"],
  "conversationId": "conv_001"
}
```

### WebSocket /api/v2/ws/recommendations

```json
// Server → Client push
{
  "type": "recommendation",
  "id": "rec_abc",
  "message": "I noticed you've been comparing pricing tiers. Would you like a side-by-side comparison?",
  "recommendationType": "content",
  "confidence": 0.85,
  "suggestedActions": ["Show comparison", "Not now"],
  "channel": "toast"
}
```
