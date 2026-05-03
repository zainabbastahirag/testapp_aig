# AG ONE Experion — Activity Mining & Recommendation Agent

## Architecture Overview

Experion is a **drop-in AI agent** that can be embedded in any website via a single
`<script>` tag. It passively mines user activity, builds behavioural profiles in
Azure Blob Storage, and proactively surfaces recommendations through a chat sidebar
and suggestion bubbles — all without requiring the host site to change any code.

---

## System Diagram

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         HOST WEBSITE (any site)                        │
│                                                                        │
│   ┌──────────────────────────────────────────────────────────────────┐ │
│   │                    Experion SDK  (experion.js)                    │ │
│   │                                                                  │ │
│   │  ┌────────────┐  ┌────────────┐  ┌────────────┐  ┌───────────┐ │ │
│   │  │  Activity   │  │  Circle    │  │  Identity  │  │  UI       │ │ │
│   │  │  Tracker    │  │  Gesture   │  │  Resolver  │  │  Manager  │ │ │
│   │  │            │  │  Capture   │  │  (ID/IP)   │  │  (Sphere  │ │ │
│   │  │ • pageview │  │            │  │            │  │   Sidebar │ │ │
│   │  │ • click    │  │ DOM frag → │  │ userId OR  │  │   Toasts) │ │ │
│   │  │ • scroll   │  │ API        │  │ fingerprint│  │           │ │ │
│   │  │ • form     │  │            │  │ + IP geo   │  │           │ │ │
│   │  │ • idle     │  │            │  │            │  │           │ │ │
│   │  └─────┬──────┘  └─────┬──────┘  └─────┬──────┘  └─────┬─────┘ │ │
│   │        │               │               │               │       │ │
│   │        └───────────────┴───────┬───────┴───────────────┘       │ │
│   │                                │                                │ │
│   │                    Event Buffer (batched POST)                   │ │
│   └────────────────────────────────┼────────────────────────────────┘ │
└────────────────────────────────────┼────────────────────────────────────┘
                                     │  HTTPS
                                     ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                        EXPERION API  (.NET 8)                          │
│                                                                        │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  ┌───────────┐ │
│  │  /activity   │  │  /chat       │  │  /recommend  │  │  /health  │ │
│  │  Ingestion   │  │  Ask &       │  │  Proactive   │  │  Setup    │ │
│  │  Controller  │  │  Circle      │  │  Suggestions │  │  Admin    │ │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘  └───────────┘ │
│         │                 │                 │                         │
│  ┌──────┴─────────────────┴─────────────────┴──────────────────────┐  │
│  │                      Middleware Pipeline                         │  │
│  │  TenantResolver → RateLimiter → ApiKeyValidator → Logging       │  │
│  └──────────────────────────┬──────────────────────────────────────┘  │
│                              │                                        │
│  ┌───────────────────────────┴─────────────────────────────────────┐  │
│  │                    Service / Domain Layer                        │  │
│  │                                                                 │  │
│  │  ┌─────────────────┐  ┌──────────────────┐  ┌───────────────┐  │  │
│  │  │  Activity       │  │  Recommendation  │  │  Chat          │  │  │
│  │  │  Mining Service │  │  Engine           │  │  Orchestrator │  │  │
│  │  │                 │  │                   │  │               │  │  │
│  │  │ • ingest batch  │  │ • rule engine     │  │ • DOM clean   │  │  │
│  │  │ • build profile │  │ • AI ranker       │  │ • context     │  │  │
│  │  │ • detect        │  │ • time triggers   │  │ • KB search   │  │  │
│  │  │   patterns      │  │ • action triggers │  │ • LLM call    │  │  │
│  │  └────────┬────────┘  └────────┬─────────┘  └───────┬───────┘  │  │
│  │           │                    │                     │          │  │
│  │  ┌────────┴────────────────────┴─────────────────────┴───────┐  │  │
│  │  │                  Infrastructure Layer                      │  │  │
│  │  │                                                            │  │  │
│  │  │  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌─────────────┐  │  │  │
│  │  │  │  Blob    │ │  Azure   │ │  Azure   │ │  Identity   │  │  │  │
│  │  │  │  Storage │ │  OpenAI  │ │  AI      │ │  Resolver   │  │  │  │
│  │  │  │  (user   │ │  GPT-4.1 │ │  Search  │ │  (IP→Geo)   │  │  │  │
│  │  │  │  logs)   │ │          │ │  (KB)    │ │             │  │  │  │
│  │  │  └──────────┘ └──────────┘ └──────────┘ └─────────────┘  │  │  │
│  │  └────────────────────────────────────────────────────────────┘  │  │
│  └─────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Data Flow

### 1. Activity Mining (Passive — Always On)

```
SDK tracks: pageview, click, scroll_depth, form_focus, idle_time, navigation
        │
        ▼  (batched every 10s or 20 events, whichever comes first)
POST /api/activity/ingest { userId?, sessionId, siteId, events[] }
        │
        ▼
IdentityResolver → resolve userId or generate from IP + fingerprint
        │
        ▼
BlobStorage: activity/{siteId}/{userId}/{date}.jsonl   (append-only log)
        │
        ▼
ActivityAnalyzer → update UserProfile in Blob
   activity/{siteId}/{userId}/profile.json
   {
     totalVisits, avgSessionDuration, topPages[],
     interests[], currentIntent, engagementScore,
     lastSeen, location { country, region, city }
   }
```

### 2. Recommendation Engine (Proactive — Triggered)

```
Triggers: after N events │ after idle 30s │ on page change │ on session start
        │
        ▼
GET /api/recommend { userId, sessionId, siteId, currentPage, recentEvents[] }
        │
        ▼
RecommendationEngine
   ├── RuleEngine (configurable per site)
   │     • new visitor → welcome + orientation
   │     • returning visitor → continue where left off
   │     • idle on form → offer help
   │     • browsing products → suggest comparison
   │
   ├── AIRanker (Azure OpenAI)
   │     • profile + recent activity → LLM picks best suggestion
   │     • KB docs from Azure AI Search for context
   │
   └── Output: Recommendation[]
         { type, title, message, action?, priority, dismissable }
        │
        ▼
SDK renders: toast bubble / sidebar suggestion / proactive chat message
```

### 3. Chat / Circle Gesture (Reactive — User Initiated)

```
User circles area on page  OR  types in chat sidebar
        │
        ▼
POST /api/chat/process   (circle gesture with DOM fragment)
POST /api/chat/ask       (follow-up or free-form question)
        │
        ▼
ChatOrchestrator
   ├── DomCleaner → structured text
   ├── ContextDetector → LLM classifies intent
   ├── KBSearch → Azure AI Search
   ├── ResponseGenerator → LLM produces answer (RAG)
   └── ActivityLogger → logs interaction for future recommendations
        │
        ▼
SDK renders: response in chat sidebar
```

---

## User Identity Resolution

```
┌─────────────────────────────────────────────────────┐
│                  Identity Flow                       │
│                                                     │
│  Has userId (logged in)?                            │
│    YES → use userId directly                        │
│    NO  → generate fingerprint:                      │
│           hash(userAgent + screenRes + timezone +   │
│                language + platform)                  │
│         + resolve IP → { country, region, city }    │
│         → synthetic ID: "anon_{hash}_{region}"      │
└─────────────────────────────────────────────────────┘
```

---

## Storage Layout (Azure Blob Storage)

```
experion-activity/
  └── {siteId}/
       └── {userId}/
            ├── profile.json              ← rolling user profile
            ├── 2026-05-03.jsonl          ← daily activity log (append)
            ├── 2026-05-02.jsonl
            └── recommendations.json      ← last served recommendations
```

---

## Configuration (per site / tenant)

```json
{
  "siteId": "ag-one-marketplace",
  "displayName": "AG ONE Marketplace",
  "features": {
    "activityMining": true,
    "circleGesture": true,
    "proactiveRecommendations": true,
    "chatbot": true
  },
  "recommendation": {
    "idleThresholdSeconds": 30,
    "eventBatchTrigger": 20,
    "maxSuggestionsPerSession": 5,
    "cooldownMinutes": 5
  },
  "ai": {
    "model": "gpt-4.1",
    "maxTokens": 2000,
    "kbIndex": "ag-experion-index"
  },
  "identity": {
    "requireUserId": false,
    "enableIpGeo": true
  }
}
```

---

## SDK Integration (Single Line)

```html
<script
  src="https://cdn.agone.com/experion.js"
  data-site-id="your-site-id"
  data-api-url="https://api.agone.com/api"
  data-api-key="your-api-key"
></script>
```

No other code changes needed. The SDK auto-initializes and handles everything.
