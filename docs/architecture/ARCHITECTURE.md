# AG ONE Experion v2 — Architecture Redesign

## Executive Summary

Experion v2 is a **standalone, embeddable AI agent** that can be placed on **any website** to passively track user behavior (Activity Mining), build user profiles over time, and proactively surface contextual recommendations and answers through an intelligent chatbot. It works without requiring changes to the host website's backend.

---

## Table of Contents

1. [Problems with Current Architecture](#1-problems-with-current-architecture)
2. [Design Goals for v2](#2-design-goals-for-v2)
3. [High-Level Architecture](#3-high-level-architecture)
4. [Component Breakdown](#4-component-breakdown)
5. [Data Flow Diagrams](#5-data-flow-diagrams)
6. [Identity Resolution](#6-identity-resolution)
7. [Activity Mining Pipeline](#7-activity-mining-pipeline)
8. [Recommendation Engine](#8-recommendation-engine)
9. [SDK Design](#9-sdk-design)
10. [Backend Services](#10-backend-services)
11. [Storage Design](#11-storage-design)
12. [Deployment Architecture](#12-deployment-architecture)
13. [Security Considerations](#13-security-considerations)
14. [Migration Path from v1](#14-migration-path-from-v1)

---

## 1. Problems with Current Architecture

| Issue | Detail |
|-------|--------|
| **Tightly coupled to AG ONE** | The current SDK sends raw DOM fragments to a monolithic .NET API (`AGONEAIHub`). It cannot be dropped into an external website without that backend. |
| **Reactive only** | The system only responds when a user explicitly draws a circle gesture. There is no passive observation or proactive recommendations. |
| **No user memory** | Each request is stateless. The system has zero knowledge of what the user did before, their preferences, or their journey. |
| **Monolithic backend** | Experion logic lives inside `AGONEAIHub.Infrastructure` alongside unrelated services (Spot, Work, Learn). Scaling and deploying independently is impossible. |
| **No activity tracking** | The SDK captures DOM snapshots but does not track navigation, clicks, scroll depth, time-on-page, or any behavioral signals. |
| **Hardcoded integrations** | PowerAutomate webhook URLs and credentials are embedded directly in `ExperionOrchestrator.cs`. |
| **Single LLM pipeline** | Two serial LLM calls (context detection → response generation) with no separation between the recommendation concern and the conversational concern. |

---

## 2. Design Goals for v2

```
G1  Standalone         — Single <script> tag. No backend changes needed on host site.
G2  Activity Mining    — Passively track clicks, navigation, scrolls, form interactions,
                         time-on-page, and element visibility without user intervention.
G3  User Profiles      — Build persistent profiles keyed by UserID (authenticated) or
                         fingerprint+IP (anonymous), stored in Blob Storage.
G4  Proactive Agent    — After observing N actions or T seconds, surface a contextual
                         recommendation unprompted via the Experion sphere/sidebar.
G5  Conversational     — Full chatbot mode where users can ask questions and get
                         RAG-powered answers from a per-site knowledge base.
G6  Multi-tenant       — One Experion backend serves many websites. Each site has its
                         own tenant config, KB index, and activity namespace.
G7  Decoupled Backend  — Experion backend is a standalone microservice, not embedded
                         inside AGONEAIHub. Communicates via well-defined REST + WebSocket APIs.
G8  Extensible Actions — Plugin architecture for "Action Connectors" (post to social
                         media, trigger workflows, call product APIs) without hardcoding.
```

---

## 3. High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────────┐
│                        ANY WEBSITE (Host Page)                       │
│                                                                      │
│   ┌──────────────────────────────────────────────────────────────┐   │
│   │              Experion SDK  (single JS bundle)                │   │
│   │                                                              │   │
│   │  ┌─────────────┐  ┌──────────────┐  ┌───────────────────┐   │   │
│   │  │  Activity    │  │  Gesture     │  │  UI Layer         │   │   │
│   │  │  Tracker     │  │  Detector    │  │  (Sphere+Sidebar) │   │   │
│   │  │  Module      │  │  Module      │  │                   │   │   │
│   │  └──────┬───────┘  └──────┬───────┘  └────────┬──────────┘   │   │
│   │         │                 │                    │              │   │
│   │  ┌──────┴─────────────────┴────────────────────┴──────────┐  │   │
│   │  │                  SDK Core / Event Bus                   │  │   │
│   │  └──────────────────────────┬──────────────────────────────┘  │   │
│   │                             │                                 │   │
│   │  ┌──────────────────────────┴──────────────────────────────┐  │   │
│   │  │         Transport Layer (REST + WebSocket)              │  │   │
│   │  │         • Batched activity events (every 5s)            │  │   │
│   │  │         • Real-time chat messages                       │  │   │
│   │  │         • Push notifications (recommendations)          │  │   │
│   │  └──────────────────────────┬──────────────────────────────┘  │   │
│   └─────────────────────────────┼────────────────────────────────┘   │
│                                 │                                     │
└─────────────────────────────────┼─────────────────────────────────────┘
                                  │  HTTPS / WSS
                                  ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                     API GATEWAY / LOAD BALANCER                         │
│         • JWT / API-Key validation    • Rate limiting                   │
│         • Tenant identification       • CORS enforcement                │
│         • WebSocket upgrade           • Request routing                 │
└────────────┬──────────────────────┬──────────────────────┬──────────────┘
             │                      │                      │
             ▼                      ▼                      ▼
┌────────────────────┐ ┌────────────────────┐ ┌────────────────────────┐
│  Activity Ingest   │ │  Chat / Ask        │ │  Recommendation        │
│  Service           │ │  Service           │ │  Service               │
│                    │ │                    │ │                        │
│  • Receive batched │ │  • Conversational  │ │  • Periodic profile    │
│    activity events │ │    RAG pipeline    │ │    evaluation          │
│  • Validate &      │ │  • KB search +     │ │  • Pattern matching    │
│    enrich events   │ │    LLM generation  │ │    on activity stream  │
│  • Append to user  │ │  • Multi-turn      │ │  • Push suggestions    │
│    activity log    │ │    memory          │ │    via WebSocket       │
│  • Update session  │ │  • Tool execution  │ │  • Trigger-based       │
│    state           │ │    (connectors)    │ │    (N actions / T sec) │
└────────┬───────────┘ └────────┬───────────┘ └──────────┬─────────────┘
         │                      │                        │
         ▼                      ▼                        ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                         SHARED INFRASTRUCTURE                           │
│                                                                         │
│  ┌───────────────┐  ┌────────────────┐  ┌───────────────────────────┐  │
│  │  AI Layer     │  │  Azure AI      │  │  Identity Resolution      │  │
│  │               │  │  Search        │  │  Service                  │  │
│  │  • Azure      │  │               │  │                           │  │
│  │    OpenAI     │  │  • Per-tenant  │  │  • Authenticated: UserID  │  │
│  │    GPT-4.1    │  │    KB indexes  │  │  • Anonymous: Fingerprint │  │
│  │  • Prompt     │  │  • Vector +    │  │    + IP geolocation       │  │
│  │    templates  │  │    hybrid      │  │  • Session correlation    │  │
│  │  • Guardrails │  │    search      │  │  • Profile merging        │  │
│  └───────────────┘  └────────────────┘  └───────────────────────────┘  │
│                                                                         │
│  ┌───────────────┐  ┌────────────────┐  ┌───────────────────────────┐  │
│  │  Blob Storage │  │  Event /       │  │  Action Connector         │  │
│  │               │  │  Telemetry     │  │  Registry                 │  │
│  │  • Activity   │  │  Stream        │  │                           │  │
│  │    logs per   │  │               │  │  • Plugin-based actions   │  │
│  │    user (txt) │  │  • Event Hub / │  │  • PowerAutomate          │  │
│  │  • Session    │  │    Service Bus │  │  • Product APIs           │  │
│  │    snapshots  │  │  • Analytics   │  │  • Custom webhooks        │  │
│  │  • Profile    │  │    pipeline    │  │  • Approval workflow      │  │
│  │    summaries  │  │               │  │                           │  │
│  └───────────────┘  └────────────────┘  └───────────────────────────┘  │
│                                                                         │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │  Audit / Observability                                          │   │
│  │  • Full prompt trace   • Action logs   • Rollback markers       │   │
│  └──────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 4. Component Breakdown

### 4.1 Experion SDK (Client-Side JavaScript)

The SDK is a **single, self-contained JS file** (`experion.js`) with zero dependencies. It is loaded via a `<script>` tag with configuration attributes.

```html
<script
  src="https://cdn.agone.ai/experion/v2/experion.min.js"
  data-tenant-id="acme-corp"
  data-api-url="https://experion-api.agone.ai"
  data-api-key="ek_live_..."
  data-track="true"
  data-trigger="alt+click"
  data-auto-recommend="true"
  data-recommend-after-actions="10"
  data-recommend-after-seconds="60"
></script>
```

**SDK Modules:**

| Module | Responsibility |
|--------|---------------|
| **Activity Tracker** | Captures page views, clicks, scroll depth, form interactions, element visibility, time on page. Batches events and sends every 5 seconds. |
| **Identity Manager** | Resolves user identity: checks for auth token (UserID) → falls back to browser fingerprint + IP geolocation. Stores session ID in `sessionStorage`. |
| **Gesture Detector** | Existing circle-gesture detection (v1 feature, retained). |
| **DOM Extractor** | Captures DOM fragments for circled regions (v1 feature, retained). |
| **Transport Layer** | REST client for activity/chat. WebSocket client for receiving push recommendations. Handles reconnection and offline queuing. |
| **UI Layer** | The sphere (buddy), sidebar chat panel, recommendation toast notifications. |
| **Event Bus** | Internal pub/sub connecting all modules. Allows clean decoupling. |

### 4.2 API Gateway

Sits in front of all backend services. Handles:

- **Authentication**: Validates `X-API-Key` for tenant identification, optional JWT for user auth
- **Rate Limiting**: Per-tenant, per-IP quotas
- **CORS**: Dynamic origin allowlist per tenant
- **WebSocket Upgrade**: Routes persistent connections to the Recommendation Service
- **Request Routing**: Directs traffic to the correct microservice

Can be implemented with Azure API Management, YARP (.NET reverse proxy), or nginx.

### 4.3 Activity Ingest Service

**Purpose**: Receives batched activity events from the SDK, validates them, enriches them with server-side context, and persists them.

```
POST /api/v2/activity/batch
{
  "tenantId": "acme-corp",
  "sessionId": "sess_abc123",
  "userId": "user_456",            // null if anonymous
  "fingerprint": "fp_xyz789",      // always present
  "events": [
    {
      "type": "page_view",
      "url": "https://acme.com/products/widget",
      "title": "Widget Pro | Acme",
      "timestamp": "2026-05-03T08:01:00Z",
      "metadata": { "referrer": "https://google.com" }
    },
    {
      "type": "click",
      "selector": "#add-to-cart",
      "text": "Add to Cart",
      "url": "https://acme.com/products/widget",
      "timestamp": "2026-05-03T08:01:15Z"
    },
    {
      "type": "scroll",
      "depth": 75,
      "url": "https://acme.com/products/widget",
      "timestamp": "2026-05-03T08:01:20Z"
    }
  ]
}
```

**Processing pipeline:**
1. Validate tenant API key
2. Resolve user identity (see Section 6)
3. Append events to the user's activity log in Blob Storage
4. Update the session state (in-memory / Redis)
5. Publish enriched events to Event Stream for the Recommendation Service

### 4.4 Chat / Ask Service

**Purpose**: Handles conversational interactions. This is the evolved version of the current `ExperionOrchestrator`.

**Key differences from v1:**
- Multi-turn conversation memory (stored in Blob alongside activity)
- RAG with per-tenant KB index
- No hardcoded tool integrations — uses the Action Connector Registry
- Receives activity context from the user's profile to personalize responses

```
Pipeline:
  User Message
       │
       ▼
  ┌─────────────────┐
  │ Load User        │ ← Blob Storage (activity summary + conversation history)
  │ Context          │
  └────────┬─────────┘
           │
           ▼
  ┌─────────────────┐
  │ Intent           │ ← LLM Call #1: classify intent
  │ Classification   │   (question / action / recommendation / chitchat)
  └────────┬─────────┘
           │
       ┌───┴───┐
       ▼       ▼
  ┌────────┐ ┌──────────┐
  │Question│ │Action     │
  │ → RAG  │ │ → Connector│
  │ Search │ │   Execute │
  └───┬────┘ └─────┬─────┘
      │            │
      ▼            ▼
  ┌─────────────────┐
  │ Response         │ ← LLM Call #2: generate response with context
  │ Generation       │
  └────────┬─────────┘
           │
           ▼
  ┌─────────────────┐
  │ Log & Return     │ → Audit log + send to SDK
  └─────────────────┘
```

### 4.5 Recommendation Service

**Purpose**: The core new capability. Monitors user activity streams and proactively generates recommendations.

**How it works:**

```
  Activity Stream (from Ingest Service)
       │
       ▼
  ┌─────────────────────┐
  │  Trigger Evaluator   │
  │                      │
  │  Checks:             │
  │  • action_count >= N │  (e.g., 10 actions)
  │  • session_time >= T │  (e.g., 60 seconds)
  │  • pattern_match     │  (e.g., user visited 3 product pages)
  │  • idle_detected     │  (e.g., no activity for 30s on a page)
  └──────────┬───────────┘
             │ (trigger fires)
             ▼
  ┌─────────────────────┐
  │  Profile Loader      │ ← Load activity summary + user profile from Blob
  └──────────┬───────────┘
             │
             ▼
  ┌─────────────────────┐
  │  KB Search           │ ← Search tenant's Azure AI Search index
  │  (contextual)        │   using recent activity as query
  └──────────┬───────────┘
             │
             ▼
  ┌─────────────────────┐
  │  LLM Recommendation  │ ← Generate personalized suggestion
  │  Generation           │   based on activity + KB results
  └──────────┬───────────┘
             │
             ▼
  ┌─────────────────────┐
  │  Push via WebSocket  │ → SDK displays recommendation toast/bubble
  └─────────────────────┘
```

**Trigger Types (configurable per tenant):**

| Trigger | Description | Default |
|---------|-------------|---------|
| `action_count` | Fire after N tracked actions in session | 10 |
| `session_duration` | Fire after T seconds in session | 60s |
| `page_pattern` | Fire when user visits pages matching a pattern | 3 product pages |
| `idle_on_page` | Fire when user is idle for T seconds on a page | 30s |
| `exit_intent` | Fire when mouse moves toward browser close/tab | enabled |
| `repeat_visit` | Fire when user returns within N days | 7 days |
| `custom_event` | Fire on host-site custom events | disabled |

### 4.6 Identity Resolution Service

See Section 6 for full details.

### 4.7 Action Connector Registry

**Purpose**: Replaces hardcoded integrations (like the PowerAutomate Facebook webhook) with a plugin system.

```
┌───────────────────────────────────────┐
│         Action Connector Registry      │
│                                        │
│  ┌─────────────┐  ┌─────────────────┐ │
│  │ PowerAutomate│  │ Product API     │ │
│  │ Connector    │  │ Connector       │ │
│  └─────────────┘  └─────────────────┘ │
│                                        │
│  ┌─────────────┐  ┌─────────────────┐ │
│  │ Webhook      │  │ Email           │ │
│  │ Connector    │  │ Connector       │ │
│  └─────────────┘  └─────────────────┘ │
│                                        │
│  Interface:                            │
│  • Name, Description                   │
│  • InputSchema (JSON Schema)           │
│  • ExecuteAsync(input) → result        │
│  • RequiresApproval: bool              │
└───────────────────────────────────────┘
```

Each connector is registered at startup and exposed as an LLM tool definition. The LLM can autonomously decide to invoke connectors, but connectors flagged `RequiresApproval = true` will prompt the user for confirmation via the SDK before executing.

---

## 5. Data Flow Diagrams

### 5.1 Passive Activity Mining Flow

```
User browses website
        │
        ▼
SDK Activity Tracker (client-side)
  │ Captures: page_view, click, scroll, form_interact, visibility, time_on_page
  │ Buffers events locally (5-second batches)
  │
  ▼
POST /api/v2/activity/batch  ──────►  Activity Ingest Service
                                            │
                                   ┌────────┴────────┐
                                   ▼                  ▼
                           Blob Storage          Event Stream
                        (append to user's      (for real-time
                         activity log txt)      recommendation
                                                evaluation)
```

### 5.2 Proactive Recommendation Flow

```
Event Stream (new activity events)
        │
        ▼
Recommendation Service
  │ Evaluates triggers against session state
  │
  │ Trigger fires!
  │
  ├──► Load user profile (Blob Storage)
  ├──► Search KB (Azure AI Search)
  ├──► Generate recommendation (Azure OpenAI)
  │
  ▼
Push via WebSocket  ──────►  SDK displays recommendation
                              (toast notification or sphere pulse)
                                    │
                                    ▼
                            User interacts?
                            ├── Yes → Opens sidebar, starts chat
                            └── No  → Dismissed, logged as "ignored"
```

### 5.3 Conversational Chat Flow (user-initiated)

```
User clicks sphere / types message
        │
        ▼
SDK sends message via REST
        │
        ▼
POST /api/v2/chat/message
        │
        ▼
Chat Service
  ├──► Load user context (activity summary + conversation history)
  ├──► Classify intent (LLM)
  ├──► Search KB if needed (Azure AI Search)
  ├──► Execute action if needed (Action Connector)
  ├──► Generate response (LLM with full context)
  │
  ▼
Return response ──────►  SDK renders in sidebar
```

### 5.4 Circle-Gesture Flow (v1 retained)

```
User holds Alt + draws circle
        │
        ▼
SDK Gesture Detector + DOM Extractor
  │ Captures DOM elements within circle region
  │
  ▼
POST /api/v2/chat/circle-context
  │ Sends captured elements + page context
  │
  ▼
Chat Service
  │ Same pipeline as 5.3, but with DOM context injected
  │
  ▼
Return response ──────►  SDK renders in sidebar
```

---

## 6. Identity Resolution

The system must track users across sessions, even without authentication.

```
┌──────────────────────────────────────────────────────┐
│                Identity Resolution Flow                │
│                                                       │
│  Request arrives with:                                │
│  • X-User-ID header (if authenticated)                │
│  • X-Fingerprint header (always, generated by SDK)    │
│  • Source IP address (always)                         │
│                                                       │
│  ┌─────────────────────────────────────┐              │
│  │ Has X-User-ID?                      │              │
│  │  YES → Use as primary key           │              │
│  │        Merge any fingerprint-only   │              │
│  │        profiles into this user      │              │
│  │                                     │              │
│  │  NO  → Has known fingerprint?       │              │
│  │         YES → Use fingerprint as ID │              │
│  │         NO  → Create new anonymous  │              │
│  │               profile with:         │              │
│  │               • fingerprint         │              │
│  │               • IP-based geo:       │              │
│  │                 - country            │              │
│  │                 - region             │              │
│  │                 - city               │              │
│  │                 - timezone           │              │
│  └─────────────────────────────────────┘              │
│                                                       │
│  Result: Resolved UserProfileId                       │
│  Stored in: profiles/{tenantId}/{profileId}.json      │
└──────────────────────────────────────────────────────┘
```

**Browser Fingerprinting (SDK-side):**
The SDK generates a semi-stable fingerprint using:
- Screen resolution + color depth
- Timezone offset
- Language preferences
- Platform string
- Canvas fingerprint (hashed)
- WebGL renderer (hashed)

This is NOT a tracking cookie — it survives cookie clears but respects privacy by being one-way hashed.

**IP Geolocation (Server-side):**
When no UserID is present, the server resolves the client IP to:
- Country, Region, City
- Approximate latitude/longitude
- Timezone

Using a local MaxMind GeoLite2 database (no external API call needed).

---

## 7. Activity Mining Pipeline

### 7.1 What We Track

| Event Type | Data Captured | Privacy Level |
|-----------|--------------|---------------|
| `page_view` | URL, title, referrer, timestamp | Low |
| `click` | CSS selector, element text, coordinates | Low |
| `scroll` | Max depth percentage per page | Low |
| `time_on_page` | Duration in seconds | Low |
| `form_interact` | Field name (NOT value), interaction type | Medium |
| `element_visibility` | Which elements were in viewport for >3s | Low |
| `search_query` | On-site search terms | Medium |
| `circle_gesture` | Region coordinates, captured elements | Low |
| `custom` | Host-site defined events via SDK API | Varies |

**Privacy rules:**
- Never capture form field VALUES (passwords, credit cards, personal data)
- Never capture input content — only field names and interaction type (focus, blur, change)
- All text is truncated to 200 characters
- Sensitive selectors (password fields, credit card inputs) are auto-excluded

### 7.2 Blob Storage Layout

```
experion-activity/
├── {tenantId}/
│   ├── profiles/
│   │   ├── {profileId}.json          ← User profile (identity, geo, preferences)
│   │   └── ...
│   ├── activity/
│   │   ├── {profileId}/
│   │   │   ├── 2026-05-03.jsonl      ← Daily activity log (append-only, one JSON per line)
│   │   │   ├── 2026-05-02.jsonl
│   │   │   └── ...
│   │   └── ...
│   ├── sessions/
│   │   ├── {sessionId}.json          ← Session state (current page, action count, etc.)
│   │   └── ...
│   ├── conversations/
│   │   ├── {profileId}/
│   │   │   ├── {conversationId}.json ← Chat history
│   │   │   └── ...
│   │   └── ...
│   └── summaries/
│       ├── {profileId}.json          ← LLM-generated profile summary (updated periodically)
│       └── ...
```

**Why JSONL (JSON Lines) for activity logs:**
- Append-only writes (no read-modify-write race conditions)
- One event per line — easy to stream-process
- Blob Storage append blobs support concurrent appends
- Easy to compress and archive old logs

### 7.3 Profile Summary Generation

Every N events (configurable, default 50), a background job generates an LLM summary of the user's activity:

```json
{
  "profileId": "fp_xyz789",
  "tenantId": "acme-corp",
  "lastUpdated": "2026-05-03T08:15:00Z",
  "totalSessions": 5,
  "totalActions": 147,
  "summary": "Returning visitor interested in enterprise widget solutions. Has viewed Widget Pro and Widget Enterprise pages multiple times. Compared pricing tiers. Spent significant time on the integration documentation. Has not yet initiated a purchase or contacted sales.",
  "interests": ["widget-pro", "enterprise-tier", "api-integration"],
  "stage": "evaluation",
  "recommendedTopics": ["case-studies", "roi-calculator", "free-trial"],
  "geo": { "country": "US", "region": "California", "city": "San Francisco" }
}
```

---

## 8. Recommendation Engine

### 8.1 Architecture

```
┌───────────────────────────────────────────────────────────┐
│                    Recommendation Engine                     │
│                                                             │
│  ┌──────────────┐    ┌──────────────┐    ┌──────────────┐  │
│  │   Trigger     │    │  Context     │    │  Generator   │  │
│  │   Evaluator   │───►│  Assembler   │───►│  (LLM)       │  │
│  │              │    │              │    │              │  │
│  │  • Rules     │    │  • Profile   │    │  • System    │  │
│  │  • Patterns  │    │  • Activity  │    │    prompt    │  │
│  │  • Timers    │    │  • KB docs   │    │  • Generate  │  │
│  │  • ML scores │    │  • Page ctx  │    │    suggestion│  │
│  └──────────────┘    └──────────────┘    └──────────────┘  │
│                                                             │
│  ┌──────────────────────────────────────────────────────┐   │
│  │                   Delivery Manager                    │   │
│  │  • Dedup (don't repeat same recommendation)           │   │
│  │  • Frequency cap (max 1 per 5 minutes)                │   │
│  │  • Priority scoring (urgent > informational)          │   │
│  │  • Channel selection (toast / sphere pulse / sidebar)  │   │
│  └──────────────────────────────────────────────────────┘   │
└───────────────────────────────────────────────────────────────┘
```

### 8.2 Recommendation Types

| Type | Example | When |
|------|---------|------|
| **Content** | "Based on your interest in Widget Pro, you might want to read our integration guide." | After viewing 3+ product pages |
| **Action** | "Would you like me to schedule a demo with our sales team?" | After spending 5+ min on pricing page |
| **Navigation** | "Looking for something specific? Our comparison tool might help." | After visiting 5+ pages without clear goal |
| **Contextual Help** | "I notice you're on the API docs. Need help with authentication setup?" | Idle on docs page for 30+ seconds |
| **Return Visitor** | "Welcome back! Last time you were exploring enterprise features. Want to continue?" | Returning within 7 days |

### 8.3 System Prompt Template (Recommendation)

```
You are Experion, an intelligent AI assistant embedded on {tenantName}'s website.

Your role: Observe the user's behavior and provide helpful, contextual recommendations.

USER PROFILE:
{profileSummary}

RECENT ACTIVITY (last {N} actions):
{recentActivity}

CURRENT PAGE: {currentPageUrl} — {currentPageTitle}

KNOWLEDGE BASE RESULTS (related to user's activity):
{kbSnippets}

RULES:
- Be helpful, not intrusive. Your recommendation should feel like a knowledgeable friend offering advice.
- Reference specific things the user has done ("I noticed you've been looking at...").
- Provide ONE clear, actionable suggestion.
- Keep it under 2 sentences for the initial notification. Expand only if the user engages.
- If the user seems to have a clear goal, help them achieve it faster.
- If the user seems lost, offer navigation help.
- Never be pushy about sales. Be informational first.

Respond in JSON:
{
  "message": "Your recommendation text",
  "type": "content|action|navigation|help|welcome_back",
  "confidence": 0.0-1.0,
  "suggestedActions": ["action label 1", "action label 2"]
}
```

---

## 9. SDK Design (v2)

### 9.1 Public API

```javascript
// Auto-initialization via script attributes (simplest):
// <script src="experion.js" data-tenant-id="..." data-api-url="..." data-auto-init="true"></script>

// Programmatic initialization (advanced):
const experion = Experion.init({
  tenantId: 'acme-corp',
  apiUrl: 'https://experion-api.agone.ai',
  apiKey: 'ek_live_...',

  // Identity (optional — SDK will fingerprint if not provided)
  userId: null,                    // Set if user is authenticated
  userMetadata: {},                // Custom attributes (name, plan, etc.)

  // Activity tracking
  track: true,                     // Enable/disable activity mining
  trackEvents: ['page_view', 'click', 'scroll', 'time_on_page', 'form_interact'],
  batchIntervalMs: 5000,           // Send events every 5 seconds
  excludeSelectors: ['.private'],  // CSS selectors to never track

  // Recommendations
  autoRecommend: true,             // Enable proactive recommendations
  recommendAfterActions: 10,       // Trigger after N actions
  recommendAfterSeconds: 60,       // Trigger after N seconds

  // UI
  position: 'bottom-right',        // Sphere position
  theme: 'auto',                   // 'light' | 'dark' | 'auto'

  // Circle gesture (v1 feature)
  circleGesture: true,
  circleTrigger: 'alt+click',
});

// Runtime API
experion.identify('user_456', { name: 'John', plan: 'enterprise' });
experion.track('custom_event', { key: 'value' });
experion.showChat();
experion.hideChat();
experion.destroy();

// Event hooks (for host site integration)
experion.on('recommendation', (rec) => { /* custom handling */ });
experion.on('message', (msg) => { /* chat message received */ });
```

### 9.2 Module Architecture

```
experion.js (IIFE, ~45KB gzipped)
│
├── core/
│   ├── EventBus.js           ← Internal pub/sub
│   ├── Config.js             ← Merged script-attributes + programmatic config
│   └── Logger.js             ← Console logging with levels
│
├── identity/
│   ├── Fingerprinter.js      ← Browser fingerprint generation
│   └── IdentityManager.js    ← UserID / Fingerprint / Session management
│
├── tracking/
│   ├── PageViewTracker.js    ← Navigation & page lifecycle
│   ├── ClickTracker.js       ← Element click capture
│   ├── ScrollTracker.js      ← Scroll depth tracking
│   ├── TimeTracker.js        ← Time on page
│   ├── FormTracker.js        ← Form field interaction (no values)
│   └── EventBatcher.js       ← Batches & sends to API
│
├── gesture/
│   ├── GestureDetector.js    ← Circle gesture detection (v1)
│   └── DomExtractor.js       ← Capture DOM in circle region (v1)
│
├── transport/
│   ├── RestClient.js         ← HTTP client for activity & chat
│   ├── WebSocketClient.js    ← Real-time recommendation push
│   └── OfflineQueue.js       ← Queue events when offline
│
├── ui/
│   ├── Sphere.js             ← The floating buddy ball
│   ├── Sidebar.js            ← Chat panel
│   ├── RecommendationToast.js← Push notification UI
│   └── Styles.js             ← Injected CSS (encapsulated)
│
└── index.js                  ← Entry point, orchestrates initialization
```

---

## 10. Backend Services (Microservice Design)

### 10.1 Service Boundaries

Instead of embedding everything in `AGONEAIHub`, Experion v2 runs as an independent backend. For simplicity, it CAN start as a modular monolith with clear internal boundaries, then split into microservices later.

```
Experion.API/                            ← ASP.NET Core Web API
├── Controllers/
│   ├── ActivityController.cs            ← POST /api/v2/activity/batch
│   ├── ChatController.cs               ← POST /api/v2/chat/message
│   │                                      POST /api/v2/chat/circle-context
│   ├── RecommendationHub.cs             ← WebSocket hub (SignalR)
│   ├── FeedbackController.cs            ← POST /api/v2/feedback
│   ├── AdminController.cs               ← Tenant management, KB setup
│   └── HealthController.cs              ← GET /health
│
├── Middleware/
│   ├── TenantResolutionMiddleware.cs    ← Extract tenantId from API key
│   ├── IdentityResolutionMiddleware.cs  ← Resolve user profile
│   └── RateLimitingMiddleware.cs        ← Per-tenant rate limits
│
Experion.Core/                           ← Domain logic (no dependencies)
├── Interfaces/
│   ├── IActivityService.cs
│   ├── IChatOrchestrator.cs
│   ├── IRecommendationEngine.cs
│   ├── IIdentityResolver.cs
│   ├── IProfileStore.cs
│   ├── IKnowledgeBaseService.cs
│   └── IActionConnector.cs
│
├── Models/
│   ├── ActivityEvent.cs
│   ├── UserProfile.cs
│   ├── SessionState.cs
│   ├── Recommendation.cs
│   ├── ChatMessage.cs
│   └── TenantConfig.cs
│
├── Enums/
│   ├── EventType.cs
│   ├── IntentType.cs
│   ├── RecommendationType.cs
│   └── TriggerType.cs
│
Experion.Infrastructure/                 ← External integrations
├── Storage/
│   ├── BlobActivityStore.cs             ← Append activity to blob JSONL
│   ├── BlobProfileStore.cs              ← Read/write user profiles
│   ├── BlobConversationStore.cs         ← Chat history persistence
│   └── BlobSummaryStore.cs              ← Profile summaries
│
├── AI/
│   ├── AzureOpenAIChatService.cs        ← LLM calls
│   ├── AzureOpenAIEmbeddingService.cs   ← Embeddings for search
│   └── PromptTemplateService.cs         ← Template rendering
│
├── Search/
│   └── AzureAISearchService.cs          ← KB vector + hybrid search
│
├── Identity/
│   ├── FingerprintResolver.cs           ← Fingerprint → profile mapping
│   └── GeoIpResolver.cs                 ← IP → location (MaxMind)
│
├── Connectors/
│   ├── PowerAutomateConnector.cs
│   ├── WebhookConnector.cs
│   └── ConnectorRegistry.cs             ← Discovers and registers connectors
│
├── Recommendations/
│   ├── TriggerEvaluator.cs              ← Evaluate rules against session
│   ├── ContextAssembler.cs              ← Build LLM context from profile+KB
│   ├── RecommendationGenerator.cs       ← LLM call to generate suggestion
│   └── DeliveryManager.cs               ← Dedup, frequency cap, push
│
└── BackgroundJobs/
    ├── ProfileSummaryJob.cs             ← Periodically regenerate summaries
    └── ActivityArchiveJob.cs            ← Compress old activity logs
```

### 10.2 Key API Endpoints

| Method | Path | Purpose |
|--------|------|---------|
| `POST` | `/api/v2/activity/batch` | Receive batched activity events |
| `POST` | `/api/v2/chat/message` | Send/receive chat messages |
| `POST` | `/api/v2/chat/circle-context` | Process circle-gesture DOM capture |
| `POST` | `/api/v2/feedback` | Thumbs up/down on responses |
| `GET` | `/api/v2/profile/me` | Get current user's profile summary |
| `WS` | `/api/v2/ws/recommendations` | WebSocket for push recommendations |
| `POST` | `/api/v2/admin/tenant` | Create/update tenant config |
| `POST` | `/api/v2/admin/setup-index` | Setup KB indexer for tenant |
| `GET` | `/health` | Health check |

---

## 11. Storage Design

### 11.1 Blob Storage (Primary — Activity + Profiles)

**Why Blob Storage instead of SQL:**
- Activity logs are append-heavy, rarely queried directly
- Profile summaries are generated by LLM, not SQL joins
- Cost-effective for high-volume telemetry data
- Natural per-tenant isolation via container/path prefixes
- No schema migrations needed

**Containers:**

| Container | Content | Access Pattern |
|-----------|---------|---------------|
| `experion-activity` | Daily JSONL activity logs | Append-only writes, periodic reads for summarization |
| `experion-profiles` | User profile JSON files | Read on every request, write on identity changes |
| `experion-sessions` | Active session state | Frequent read/write (consider Redis for hot path) |
| `experion-conversations` | Chat history | Read/write during chat, append per message |
| `experion-summaries` | LLM-generated profile summaries | Read on every recommendation, write periodically |
| `experion-config` | Per-tenant configuration | Read on startup, cached in memory |

### 11.2 Azure AI Search (KB)

Per-tenant indexes for knowledge base content. Same architecture as v1 but namespaced:

```
Index naming: {tenantId}-kb-index
```

### 11.3 Optional: Redis (Hot Session State)

For production scale, active session state should live in Redis for sub-millisecond reads:

```
Key: session:{tenantId}:{sessionId}
TTL: 30 minutes
Value: { actionCount, lastActivity, currentPage, recommendationHistory }
```

### 11.4 Optional: SQL Server (Audit Logs)

Reuse the existing `PromptExecutionLog` table from AGONEAIHub for audit/compliance:

```
- All LLM calls logged
- Action connector executions logged
- Recommendation deliveries logged
```

---

## 12. Deployment Architecture

### 12.1 Azure Deployment

```
┌───────────────────────────────────────────────────────────────┐
│                      Azure Subscription                        │
│                                                                │
│  ┌─────────────────────┐    ┌──────────────────────────────┐  │
│  │  Azure CDN           │    │  Azure API Management         │  │
│  │  • experion.min.js   │    │  • Rate limiting               │  │
│  │  • SDK assets         │    │  • API key validation          │  │
│  │                      │    │  • CORS policies               │  │
│  └──────────┬───────────┘    └──────────────┬────────────────┘  │
│             │                               │                   │
│             │              ┌────────────────┼────────────────┐  │
│             │              │                │                │  │
│             │              ▼                ▼                │  │
│             │     ┌──────────────┐  ┌──────────────┐        │  │
│             │     │  App Service  │  │  App Service  │        │  │
│             │     │  (API)        │  │  (API)        │        │  │
│             │     │  Instance 1   │  │  Instance 2   │        │  │
│             │     └──────┬───────┘  └──────┬───────┘        │  │
│             │            │                 │                 │  │
│             │            └────────┬────────┘                 │  │
│             │                     │                          │  │
│  ┌──────────┴──────────┐  ┌──────┴─────────┐               │  │
│  │                      │  │                 │               │  │
│  │  Blob Storage        │  │  Azure OpenAI   │               │  │
│  │  • Activity logs     │  │  • GPT-4.1      │               │  │
│  │  • Profiles          │  │  • Embeddings   │               │  │
│  │  • Conversations     │  │                 │               │  │
│  │  • Config            │  │                 │               │  │
│  └──────────────────────┘  └─────────────────┘               │  │
│                                                                │
│  ┌──────────────────┐  ┌──────────────────┐                   │
│  │  Azure AI Search  │  │  Azure SignalR    │                   │
│  │  • Per-tenant KB  │  │  Service          │                   │
│  │    indexes        │  │  • WebSocket push │                   │
│  └──────────────────┘  └──────────────────┘                   │
│                                                                │
│  ┌──────────────────┐  ┌──────────────────┐                   │
│  │  Redis Cache      │  │  Application     │                   │
│  │  (optional)       │  │  Insights        │                   │
│  │  • Session state  │  │  • Telemetry     │                   │
│  └──────────────────┘  └──────────────────┘                   │
└───────────────────────────────────────────────────────────────┘
```

### 12.2 Scaling Strategy

| Component | Scaling Approach |
|-----------|-----------------|
| SDK (CDN) | Global CDN, infinitely scalable |
| API (Activity Ingest) | Horizontal scale-out, stateless |
| API (Chat) | Horizontal, but LLM calls are the bottleneck |
| Recommendation Engine | Background worker, scale by tenant count |
| WebSocket (SignalR) | Azure SignalR Service (managed, auto-scale) |
| Blob Storage | Infinitely scalable by design |
| Azure AI Search | Scale units per query volume |

---

## 13. Security Considerations

| Concern | Mitigation |
|---------|-----------|
| **Tenant Isolation** | All data paths include `tenantId`. API keys are tenant-scoped. |
| **User Privacy** | Never capture form values. Fingerprinting is one-way hashed. GDPR delete endpoint. |
| **API Key Security** | Keys are never exposed in client-side code beyond the `data-api-key` attribute (which is intentionally public, like Google Analytics). Server-side admin keys are separate. |
| **Credential Management** | No hardcoded secrets. Use Azure Key Vault for connection strings, webhook URLs. |
| **Rate Limiting** | Per-tenant, per-IP rate limits at the API Gateway level. |
| **XSS Protection** | SDK renders via DOM APIs, never `innerHTML` with user content. |
| **Content Security** | LLM outputs are sanitized before rendering. No raw HTML from AI. |
| **Data Retention** | Configurable per tenant. Auto-archive/delete old activity logs. |

---

## 14. Migration Path from v1

### Phase 1: Standalone Backend (Weeks 1-2 equivalent effort)
- Extract Experion code from `AGONEAIHub` into a standalone `Experion.API` project
- Set up Blob Storage for profiles and activity (replaces SQL dependency for these)
- Keep existing circle-gesture and chat functionality working
- Deploy as a separate App Service

### Phase 2: Activity Mining (Weeks 3-4 equivalent effort)
- Add tracking modules to the SDK
- Build Activity Ingest endpoint
- Implement Identity Resolution (fingerprint + IP geo)
- Store activity logs in Blob Storage

### Phase 3: Recommendation Engine (Weeks 5-6 equivalent effort)
- Build Trigger Evaluator and recommendation pipeline
- Add WebSocket/SignalR for push notifications
- Implement Profile Summary generation
- Add recommendation UI to SDK

### Phase 4: Multi-Tenant + Connectors (Weeks 7-8 equivalent effort)
- Add tenant configuration system
- Build Action Connector Registry
- Migrate hardcoded integrations to connectors
- Add admin dashboard for tenant management

---

## Appendix A: Comparison — v1 vs v2

| Aspect | v1 (Current) | v2 (Proposed) |
|--------|-------------|---------------|
| **Deployment** | Embedded in AGONEAIHub monolith | Standalone microservice |
| **SDK** | Circle gesture only | Activity tracking + gesture + chat + recommendations |
| **User Memory** | Stateless per request | Persistent profiles with activity history |
| **Intelligence** | Reactive (user must circle) | Proactive + Reactive |
| **Identity** | None | UserID + Fingerprint + IP Geo |
| **Storage** | SQL Server only | Blob Storage (activity) + SQL (audit) + Redis (sessions) |
| **Multi-tenant** | Single tenant | Multi-tenant by design |
| **Integrations** | Hardcoded PowerAutomate | Plugin-based Action Connectors |
| **Real-time** | REST only | REST + WebSocket |
| **Recommendations** | None | Rule-based triggers + LLM-generated suggestions |

---

## Appendix B: Technology Stack

| Layer | Technology | Justification |
|-------|-----------|---------------|
| SDK | Vanilla JavaScript (IIFE) | Zero dependencies, works everywhere |
| Backend API | ASP.NET Core 8 | Team expertise, existing ecosystem |
| Real-time | SignalR / Azure SignalR Service | Native .NET WebSocket support |
| LLM | Azure OpenAI GPT-4.1 | Existing integration, enterprise compliance |
| Search | Azure AI Search | Existing integration, vector + hybrid |
| Activity Storage | Azure Blob Storage (Append Blobs) | Cost-effective, append-optimized |
| Profile Cache | Azure Redis Cache | Sub-ms session lookups |
| CDN | Azure CDN / Cloudflare | SDK distribution |
| Observability | Application Insights | Existing integration |
| CI/CD | GitHub Actions + Azure DevOps | Existing pipeline |
