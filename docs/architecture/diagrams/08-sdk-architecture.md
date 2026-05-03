# SDK Internal Architecture

```mermaid
graph TD
    subgraph INIT["Initialization"]
        SCRIPT["&lt;script src='experion.js'<br/>data-tenant-id='...'<br/>data-api-url='...'&gt;"]
        CONFIG["Config Module<br/>Merge script attrs + defaults"]
        SCRIPT --> CONFIG
    end

    CONFIG --> CORE

    subgraph CORE["Core"]
        EB["Event Bus<br/>(Internal pub/sub)"]
        LOG["Logger"]
    end

    subgraph IDENTITY["Identity Module"]
        FP["Fingerprinter<br/>• Screen, timezone<br/>• Canvas hash<br/>• WebGL hash"]
        IDM["Identity Manager<br/>• Check for auth userId<br/>• Generate fingerprint<br/>• Manage sessionId"]
        FP --> IDM
    end

    subgraph TRACKING["Activity Tracking"]
        PV["PageView Tracker<br/>• URL changes<br/>• pushState/popState<br/>• hashChange"]
        CL["Click Tracker<br/>• Element selector<br/>• Text content<br/>• Coordinates"]
        SC["Scroll Tracker<br/>• Max depth %<br/>• Throttled (200ms)"]
        TT["Time Tracker<br/>• Visible time only<br/>• visibilitychange API"]
        FT["Form Tracker<br/>• Field names only<br/>• focus/blur/change<br/>• NO values captured"]
        BATCH["Event Batcher<br/>• Buffer events<br/>• Flush every 5s<br/>• Flush on unload"]
    end

    subgraph GESTURE["Circle Gesture (v1)"]
        GD["Gesture Detector<br/>• Alt+drag trigger<br/>• Circle recognition"]
        DE["DOM Extractor<br/>• Capture elements<br/>  in circle region"]
    end

    subgraph TRANSPORT["Transport Layer"]
        REST["REST Client<br/>• POST /activity/batch<br/>• POST /chat/message<br/>• POST /feedback"]
        WS["WebSocket Client<br/>• SignalR connection<br/>• Auto-reconnect<br/>• Receive push recs"]
        OQ["Offline Queue<br/>• Queue when offline<br/>• Flush when back<br/>• localStorage backed"]
    end

    subgraph UI_LAYER["UI Layer"]
        SPH["Sphere (Buddy)<br/>• Floating ball<br/>• Eye animations<br/>• Drag-to-move<br/>• Pulse on rec"]
        SB["Sidebar<br/>• Chat panel<br/>• Message history<br/>• Suggestion chips"]
        RT["Recommendation Toast<br/>• Slide-in notification<br/>• Action buttons<br/>• Auto-dismiss"]
        STY["Styles<br/>• Shadow DOM or<br/>  scoped CSS<br/>• Theme support"]
    end

    CORE --> IDENTITY
    CORE --> TRACKING
    CORE --> GESTURE
    CORE --> TRANSPORT
    CORE --> UI_LAYER

    PV -->|events| EB
    CL -->|events| EB
    SC -->|events| EB
    TT -->|events| EB
    FT -->|events| EB
    GD -->|circle_detected| EB

    EB -->|activity_events| BATCH
    EB -->|circle_event| DE
    DE -->|dom_fragment| REST

    BATCH -->|batched_events| REST
    BATCH -->|offline| OQ
    OQ -->|reconnect| REST

    WS -->|recommendation| EB
    EB -->|show_recommendation| RT
    EB -->|open_chat| SB
    EB -->|sphere_pulse| SPH

    IDM -->|headers| REST
    IDM -->|headers| WS

    style CORE fill:#dbeafe,stroke:#3b82f6,stroke-width:2px
    style TRACKING fill:#d1fae5,stroke:#10b981
    style GESTURE fill:#fef3c7,stroke:#f59e0b
    style TRANSPORT fill:#ede9fe,stroke:#8b5cf6
    style UI_LAYER fill:#fce7f3,stroke:#ec4899
    style IDENTITY fill:#fff7ed,stroke:#ea580c
```

## SDK Size Budget

| Module | Estimated Size (minified + gzipped) |
|--------|-------------------------------------|
| Core (EventBus, Config, Logger) | ~2 KB |
| Identity (Fingerprinter, Manager) | ~3 KB |
| Tracking (all trackers + batcher) | ~8 KB |
| Gesture (detector + DOM extractor) | ~5 KB |
| Transport (REST + WS + offline) | ~6 KB |
| UI (Sphere + Sidebar + Toast + CSS) | ~18 KB |
| **Total** | **~42 KB** |

Target: Under 50 KB gzipped for fast loading on any website.
