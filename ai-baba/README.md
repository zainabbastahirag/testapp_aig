# AI Guide — Voice AI Avatar Assistant

A single ASP.NET Core 8 MVC project that runs a voice-driven AI companion fully on your machine, talking to a local **Ollama** LLM. Drop-in personas, mindsets, voice settings, wake-word, persistent memory.

![tech](https://img.shields.io/badge/.NET-8-512bd4) ![tech](https://img.shields.io/badge/Ollama-local-black) ![tech](https://img.shields.io/badge/Web%20Speech-API-f6c453)

## Features

- **5 personas** — Sage · Philosopher · Healer · Elder · Storyteller
- **5 mindsets** — Balanced · Logical · Spiritual · Motivational · Creative
- **Voice in / voice out** — Web Speech API (mic + speech synthesis)
- **Wake word** — say "Hey Baba" (or "Hey Guide") to start a conversation
- **Continuous mode**, configurable voice profile + speed
- **Animated central avatar** — breathing glow + waveform synced to speech
- **5 ambient backgrounds** — Dawn · Forest · Dusk · Night · Lake
- **Persistent memory** — name, persona, mindset, last 10 turns (per-session cookie)
- **Quick prompt chips** — Life · Mindset · Relationships · Career · Health · Spirituality
- **Friendly fallback** — UI still works (with a clear error message) when Ollama isn't running

## Project structure

```
ai-baba/
└── AIBaba/                           ← single ASP.NET Core 8 MVC project
    ├── Program.cs
    ├── appsettings.json              ← Ollama settings (BaseUrl, Model)
    ├── Controllers/
    │   ├── HomeController.cs         ← serves the SPA-style page
    │   └── AskController.cs          ← /api/ask, /api/profile, /api/reset
    ├── Services/
    │   ├── OllamaService.cs          ← real Ollama HTTP client
    │   ├── MemoryStore.cs            ← in-memory profile + history
    │   ├── PromptBuilder.cs          ← persona/mindset prompt templates
    │   └── SessionMiddleware.cs      ← per-visitor cookie session id
    ├── Models/AskModels.cs
    ├── Views/
    │   ├── _ViewImports.cshtml
    │   ├── _ViewStart.cshtml
    │   ├── Shared/_Layout.cshtml
    │   ├── Shared/Error.cshtml
    │   └── Home/Index.cshtml
    └── wwwroot/
        ├── css/style.css
        ├── js/app.js
        └── img/avatars/              ← drop your real PNG portraits here
```

## Run it

### 1. Install Ollama and pull a model

[https://ollama.com/download](https://ollama.com/download)

```bash
ollama pull llama3
ollama serve            # default port 11434
```

You can also use `mistral`, `phi3`, `gemma2`, etc. — just change the model name in `appsettings.json`:

```json
"Ollama": {
  "BaseUrl": "http://localhost:11434",
  "Model": "llama3",
  "TimeoutSeconds": 60
}
```

### 2. Run the app

```bash
cd AIBaba
dotnet run
```

Open <http://localhost:5088/>.

> If Ollama isn't running, the page still loads and you can try the UI. The reply will be a friendly "my mind is quiet" message until you start Ollama and reload.

## Voice & wake word

- The mic button uses `SpeechRecognition`. Click it and speak — the transcript is sent to `/api/ask`.
- Wake-word detection runs in the background. Say **"Hey Baba"** or **"Hey Guide"** and the assistant will start listening.
- Replies are spoken back via `SpeechSynthesis`. Voice profile (Calm & Warm / Bright & Crisp / Deep & Grounded) and speed (0.6×–1.4×) are tunable in the right panel.
- Web Speech is best supported in Chrome / Edge / Brave. On Firefox/Safari you'll still have text input, mic button shows an alert.

## Memory

Per-browser session (cookie `aibaba_sid`). The backend stores:

- **name** — auto-extracted from "my name is …" / "I'm …" or set via the input
- **avatar** + **mindset**
- **last 10 turns** (FIFO)

Click the small ↻ button in the right panel to wipe memory.

## Personas & mindsets

The `PromptBuilder` injects a persona description and mindset description into the system prompt. Persona = *who* the assistant is. Mindset = *how* they answer.

| Persona | System prompt fragment |
|---|---|
| Sage | calm, thoughtful, philosophical mentor |
| Philosopher | deep thinker, asks clarifying questions |
| Healer | compassionate, validates feelings first |
| Elder | tells short anecdotes and grounded common-sense lessons |
| Storyteller | weaves answers into vivid little stories |

| Mindset | System prompt fragment |
|---|---|
| Balanced | well-rounded perspective |
| Logical | step-by-step, concise reasoning |
| Spiritual | soulful, mindful, peaceful |
| Motivational | encouraging, ends with a small push |
| Creative | unexpected angles, imaginative |

## Custom portraits

Drop 512×512 PNG files into `AIBaba/wwwroot/img/avatars/` named:

```
sage.png        philosopher.png    healer.png    elder.png    storyteller.png
```

The frontend tries the PNG first; if missing, it falls back to a themed inline SVG silhouette. No code changes needed.

## API

| Method | Path | Purpose |
|---|---|---|
| `GET`  | `/api/health` | Health check |
| `GET`  | `/api/profile` | Current profile + memory length |
| `POST` | `/api/profile` | Update name / avatar / mindset |
| `POST` | `/api/ask` | Send a message (returns reply + memory state) |
| `POST` | `/api/reset` | Clear stored memory for this session |

`POST /api/ask` body:

```json
{ "message": "What is patience?", "name": "Adam", "avatar": "sage", "mindset": "spiritual" }
```

## Production notes

- `MemoryStore` is in-process. For multi-instance deployments, swap to SQL or Redis (`IMemoryStore` is an interface).
- For non-local Ollama, set `Ollama:BaseUrl` to your remote endpoint.
- Static assets are versioned via `asp-append-version="true"` so cache-busting Just Works.
