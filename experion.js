/**
 * AG ONE Experion SDK v2.0
 * ─────────────────────────────────────────────────────────────────────
 * Standalone drop-in script: place on ANY website to get:
 *   1. Activity Mining   — automatic capture of page views, clicks,
 *                          searches, product views, scroll depth
 *   2. Recommendation Agent — proactive AI suggestions after N events
 *   3. Circle Gesture    — user draws a circle → contextual AI popup
 *   4. Chat Sidebar      — follow-up conversation + KB answers
 *
 * Usage (HTML):
 *   <script src="experion.js"
 *           data-api-url="https://your-api.com"
 *           data-user-id=""              (optional; leave blank for anonymous)
 *           data-site-id="my-site"
 *           data-kb-index="experion-kb"
 *           data-gesture="alt+drag"      (alt+drag | right-click-hold)
 *           data-nudge-events="8"        (proactive nudge after N events)
 *           data-auto-init="true"></script>
 *
 * Public API (window.Experion):
 *   Experion.init(config)        — manual init
 *   Experion.identify(userId)    — set authenticated user ID
 *   Experion.track(type, payload)— manual event track
 *   Experion.recommend()         — trigger recommendation fetch now
 *   Experion.destroy()           — remove SDK from page
 */
(function (global) {
    "use strict";

    // ── CONFIG ───────────────────────────────────────────────────────
    const DEFAULTS = {
        apiUrl: "",
        userId: "",
        sessionId: generateId(),
        siteId: "",
        kbIndex: "experion-kb",
        gesture: "alt+drag",
        nudgeAfterEvents: 8,
        batchIntervalMs: 3000,
        maxBatchSize: 20,
        autoInit: true,
    };

    const SKIP_TAGS = new Set([
        "SCRIPT", "STYLE", "NOSCRIPT", "IFRAME", "SVG", "PATH", "CIRCLE", "RECT",
        "LINE", "POLYGON", "POLYLINE", "ELLIPSE", "META", "LINK", "BR", "HR", "WBR",
    ]);
    const INTERACTIVE_TAGS = new Set([
        "INPUT", "BUTTON", "SELECT", "TEXTAREA", "A", "DETAILS", "SUMMARY", "LABEL",
    ]);
    const MEANINGFUL_TAGS = new Set([
        "H1", "H2", "H3", "H4", "H5", "H6", "P", "SPAN", "DIV", "LI", "TD", "TH",
        "TABLE", "TR", "THEAD", "TBODY", "FORM", "NAV", "MAIN", "ARTICLE", "SECTION",
        "ASIDE", "HEADER", "FOOTER", "FIGCAPTION", "BLOCKQUOTE", "PRE", "CODE",
        "IMG", "FIGURE", "UL", "OL", "DL", "DT", "DD", "STRONG", "EM", "B", "I",
        ...INTERACTIVE_TAGS,
    ]);

    // ── STYLES ───────────────────────────────────────────────────────
    const STYLES = `
/* ── Variables ── */
:root {
    --exp-brand: #06B6D4;
    --exp-brand-dark: #0891B2;
    --exp-bg: #FFFFFF;
    --exp-surface: #F8FAFC;
    --exp-border: #E2E8F0;
    --exp-text: #1E293B;
    --exp-muted: #64748B;
    --exp-radius: 12px;
    --exp-shadow: 0 4px 24px rgba(0,0,0,0.12);
}

/* ── Overlay canvas ── */
#exp-overlay {
    position: fixed; top: 0; left: 0; width: 100vw; height: 100vh;
    z-index: 2147483646; pointer-events: none; transition: opacity 0.3s;
}

/* ── Sphere button ── */
.exp-sphere-btn {
    position: fixed; bottom: 28px; right: 28px;
    width: 72px; height: 72px;
    border: none; background: transparent; cursor: pointer;
    z-index: 2147483644; padding: 0;
    animation: exp-float 4s ease-in-out infinite;
    user-select: none; touch-action: none;
}
.exp-sphere {
    width: 72px; height: 72px; border-radius: 50%; position: relative;
    background: radial-gradient(84% 84% at 38% 38%,
        #06B6D4 0%, #3B82F6 33%, #8B5CF6 66%, #A855F7 100%);
    box-shadow: 0 0 20px rgba(6,182,212,0.45), 0 0 40px rgba(59,130,246,0.25);
    transition: all 0.4s cubic-bezier(0.175,0.885,0.32,1.275);
    overflow: hidden; transform-style: preserve-3d;
    opacity: 0; transform: scale(0.8);
}
.exp-sphere.exp-ready { opacity: 1; transform: scale(1); }
.exp-sphere-btn:hover .exp-sphere { transform: scale(1.08) translateY(2px); box-shadow: 0 0 40px rgba(168,85,247,0.7); }
.exp-eye { position: absolute; width: 13px; height: 13px; background: #fff; border-radius: 50%; top: 26px; }
.exp-eye-l { left: 20px; } .exp-eye-r { left: 40px; }
@keyframes exp-float { 0%,100%{transform:translateY(0)} 50%{transform:translateY(-5px)} }
@keyframes exp-blink { 0%,90%,100%{transform:scaleY(1)} 95%{transform:scaleY(0.05)} }
.exp-eye { animation: exp-blink 4s infinite; }

/* ── Sidebar ── */
.exp-sidebar {
    position: fixed; right: 0; top: 0; bottom: 0; width: 360px;
    background: var(--exp-bg); border-left: 1px solid var(--exp-border);
    box-shadow: -4px 0 24px rgba(0,0,0,0.08);
    z-index: 2147483645; display: flex; flex-direction: column;
    transform: translateX(100%); transition: transform 0.3s ease;
    font-family: system-ui, -apple-system, sans-serif;
}
.exp-sidebar.exp-open { transform: translateX(0); }
.exp-sidebar.exp-wide { width: min(700px, 96vw); }

.exp-header {
    display: flex; align-items: center; gap: 12px; padding: 16px 20px;
    background: linear-gradient(135deg, #EFF6FF 0%, #F0FDF4 100%);
    border-bottom: 1px solid var(--exp-border); flex-shrink: 0;
}
.exp-logo {
    width: 44px; height: 44px; border-radius: 50%; flex-shrink: 0;
    background: linear-gradient(135deg, #06B6D4, #6366F1);
    display: flex; align-items: center; justify-content: center;
    box-shadow: 0 2px 12px rgba(6,182,212,0.35);
}
.exp-logo svg { width: 22px; height: 22px; fill: white; }
.exp-title-group { flex: 1; }
.exp-title { font-size: 15px; font-weight: 600; color: var(--exp-text); }
.exp-subtitle { font-size: 11px; color: var(--exp-muted); }
.exp-hdr-btns { display: flex; gap: 6px; }
.exp-icon-btn {
    width: 30px; height: 30px; border: none; background: transparent;
    cursor: pointer; border-radius: 6px; color: var(--exp-muted);
    display: flex; align-items: center; justify-content: center;
}
.exp-icon-btn:hover { background: var(--exp-border); color: var(--exp-text); }

.exp-messages {
    flex: 1; overflow-y: auto; padding: 16px;
    display: flex; flex-direction: column; gap: 12px;
}
.exp-msg {
    max-width: 88%; padding: 11px 14px; border-radius: 10px;
    font-size: 14px; line-height: 1.55; word-break: break-word;
}
.exp-msg.exp-ai { align-self: flex-start; background: var(--exp-surface); border: 1px solid var(--exp-border); color: var(--exp-text); }
.exp-msg.exp-user { align-self: flex-end; background: #DBEAFE; color: #1E40AF; }
.exp-msg a { color: var(--exp-brand-dark); }
.exp-msg ul { padding-left: 18px; margin: 6px 0; }

.exp-docs { margin-top: 8px; display: flex; flex-direction: column; gap: 6px; }
.exp-doc-card {
    background: #F0FDFA; border: 1px solid #99F6E4; border-radius: 8px;
    padding: 8px 12px; font-size: 12px; color: var(--exp-text);
}
.exp-doc-card a { font-weight: 600; color: #0D9488; text-decoration: none; }
.exp-doc-card a:hover { text-decoration: underline; }

.exp-rec-section { margin-top: 10px; }
.exp-rec-label { font-size: 11px; text-transform: uppercase; letter-spacing: 0.08em; color: var(--exp-muted); font-weight: 600; margin-bottom: 6px; }
.exp-rec-cards { display: flex; flex-direction: column; gap: 6px; }
.exp-rec-card {
    background: #FFF7ED; border: 1px solid #FED7AA; border-radius: 8px;
    padding: 10px 12px; font-size: 13px; cursor: pointer;
    transition: all 0.2s;
}
.exp-rec-card:hover { background: #FFEDD5; }
.exp-rec-card-title { font-weight: 600; color: #92400E; }
.exp-rec-card-desc { color: var(--exp-muted); font-size: 12px; margin-top: 2px; }
.exp-rec-card-cta { margin-top: 6px; font-size: 12px; color: #B45309; font-weight: 500; }

.exp-suggestions { display: flex; flex-wrap: wrap; gap: 6px; margin-top: 10px; }
.exp-suggestion {
    padding: 5px 11px; border: 1px solid var(--exp-border); border-radius: 20px;
    background: white; font-size: 12px; cursor: pointer; transition: all 0.2s;
}
.exp-suggestion:hover { border-color: var(--exp-brand); color: var(--exp-brand-dark); }

.exp-footer { padding: 12px 16px; border-top: 1px solid var(--exp-border); background: var(--exp-surface); flex-shrink: 0; }
.exp-input-row { display: flex; gap: 8px; }
.exp-input-wrap { flex: 1; display: flex; align-items: center; background: white; border: 1px solid var(--exp-border); border-radius: 24px; padding: 0 14px; }
.exp-input-wrap input { flex: 1; height: 38px; border: none; outline: none; font-size: 14px; background: transparent; color: var(--exp-text); }
.exp-send-btn { width: 38px; height: 38px; border-radius: 50%; border: none; background: var(--exp-brand); color: white; cursor: pointer; display: flex; align-items: center; justify-content: center; flex-shrink: 0; }
.exp-send-btn:hover { background: var(--exp-brand-dark); }
.exp-hint { font-size: 11px; color: var(--exp-muted); margin-top: 6px; text-align: center; }

/* ── Nudge toast ── */
.exp-nudge {
    position: fixed; bottom: 112px; right: 28px; width: 280px;
    background: white; border: 1px solid var(--exp-border); border-radius: var(--exp-radius);
    box-shadow: var(--exp-shadow); padding: 14px 16px; z-index: 2147483643;
    transform: translateY(20px); opacity: 0; transition: all 0.3s;
    font-family: system-ui, sans-serif;
}
.exp-nudge.exp-nudge-in { transform: translateY(0); opacity: 1; }
.exp-nudge-title { font-size: 13px; font-weight: 600; color: var(--exp-text); }
.exp-nudge-body { font-size: 12px; color: var(--exp-muted); margin-top: 4px; line-height: 1.4; }
.exp-nudge-rec { margin-top: 8px; display: flex; flex-direction: column; gap: 4px; }
.exp-nudge-item { font-size: 12px; padding: 6px 10px; background: var(--exp-surface); border-radius: 6px; cursor: pointer; color: var(--exp-text); border: 1px solid var(--exp-border); }
.exp-nudge-item:hover { border-color: var(--exp-brand); color: var(--exp-brand-dark); }
.exp-nudge-close { position: absolute; top: 8px; right: 8px; background: none; border: none; cursor: pointer; font-size: 14px; color: var(--exp-muted); }
.exp-typing { font-style: italic; color: var(--exp-muted); animation: exp-pulse 1.2s infinite; }
@keyframes exp-pulse { 0%,100%{opacity:1} 50%{opacity:0.4} }
`;

    // ══════════════════════════════════════════════════════════════════
    // UTILITIES
    // ══════════════════════════════════════════════════════════════════

    function generateId() {
        return Date.now().toString(36) + Math.random().toString(36).slice(2, 8);
    }

    function parseScriptConfig() {
        const s = document.currentScript || document.querySelector("script[data-api-url]");
        if (!s) return {};
        return {
            apiUrl: s.dataset.apiUrl || "",
            userId: s.dataset.userId || "",
            siteId: s.dataset.siteId || "",
            kbIndex: s.dataset.kbIndex || undefined,
            gesture: s.dataset.gesture || undefined,
            nudgeAfterEvents: s.dataset.nudgeEvents ? parseInt(s.dataset.nudgeEvents, 10) : undefined,
            autoInit: s.dataset.autoInit !== "false",
        };
    }

    function overlaps(rect, region) {
        return !(rect.right < region.left || rect.left > region.left + region.width ||
            rect.bottom < region.top || rect.top > region.top + region.height);
    }

    function isVisible(el) {
        const st = getComputedStyle(el);
        if (st.display === "none" || st.visibility === "hidden" || st.opacity === "0") return false;
        const r = el.getBoundingClientRect();
        return r.width > 0 && r.height > 0;
    }

    function extractDOM(region, max = 50) {
        const captured = [], seenTexts = new Set(), textParts = [];
        for (const el of document.querySelectorAll("*")) {
            if (captured.length >= max) break;
            if (SKIP_TAGS.has(el.tagName) || !MEANINGFUL_TAGS.has(el.tagName)) continue;
            if (!isVisible(el)) continue;
            const rect = el.getBoundingClientRect();
            if (!overlaps(rect, region)) continue;
            const text = (el.innerText || "").substring(0, 500).trim() || null;
            if (text && seenTexts.has(text)) continue;
            if (text) { seenTexts.add(text); textParts.push(text); }
            captured.push({
                tag: el.tagName,
                id: el.id || undefined,
                text: text || undefined,
                type: el.type || undefined,
                href: el.href || undefined,
                role: el.getAttribute("role") || undefined,
                ariaLabel: el.getAttribute("aria-label") || undefined,
                placeholder: el.placeholder || undefined,
                isInteractive: INTERACTIVE_TAGS.has(el.tagName),
                rect: { top: rect.top, left: rect.left, width: rect.width, height: rect.height },
            });
        }
        return { elements: captured, capturedText: textParts.join("\n").substring(0, 5000) };
    }

    function renderMarkdown(text) {
        // Minimal safe markdown renderer (bold, lists, newlines)
        return text
            .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
            .replace(/\*\*(.+?)\*\*/g, "<strong>$1</strong>")
            .replace(/\*(.+?)\*/g, "<em>$1</em>")
            .replace(/`(.+?)`/g, "<code>$1</code>")
            .replace(/^- (.+)$/gm, "<li>$1</li>")
            .replace(/(<li>.*<\/li>\n?)+/g, m => `<ul>${m}</ul>`)
            .replace(/\n/g, "<br>");
    }

    // ══════════════════════════════════════════════════════════════════
    // API CLIENT
    // ══════════════════════════════════════════════════════════════════

    class ApiClient {
        constructor(baseUrl) {
            this.base = baseUrl.replace(/\/$/, "");
        }

        async post(path, body) {
            const r = await fetch(this.base + path, {
                method: "POST",
                headers: { "Content-Type": "application/json", "X-SDK": "AGOneExperion/2.0" },
                body: JSON.stringify(body),
            });
            if (!r.ok) throw new Error(`API ${r.status} at ${path}`);
            return r.json();
        }

        trackBatch(events) { return this.post("/api/activity/track", events); }
        process(req) { return this.post("/api/experion/process", req); }
        ask(req) { return this.post("/api/experion/ask", req); }
        feedback(req) { return this.post("/api/experion/feedback", req); }
        recommend(req) { return this.post("/api/recommend", req); }
    }

    // ══════════════════════════════════════════════════════════════════
    // ACTIVITY MINER
    // ══════════════════════════════════════════════════════════════════

    class ActivityMiner {
        constructor(cfg, api, onNudge) {
            this.cfg = cfg;
            this.api = api;
            this.onNudge = onNudge;
            this.queue = [];
            this.totalEvents = 0;
            this.nudgeFired = false;
            this._listeners = [];
            this._timer = null;
        }

        start() {
            this._track("SessionStart");
            this._track("PageView", { title: document.title });

            const onScroll = this._debounce(() => {
                const depth = Math.round((window.scrollY / (document.body.scrollHeight - window.innerHeight)) * 100);
                this._track("Scroll", { depth });
            }, 500);

            const onClick = (e) => {
                const el = e.target.closest("a,button,input,select,textarea");
                if (!el) return;
                const payload = { tag: el.tagName, id: el.id || undefined, text: (el.innerText || el.value || "").substring(0, 80) };
                if (el.tagName === "A") payload.href = el.href;
                this._track("Click", payload);
            };

            const onInput = this._debounce((e) => {
                const el = e.target;
                if (el.tagName === "INPUT" && (el.type === "search" || el.name?.includes("search") || el.placeholder?.toLowerCase().includes("search"))) {
                    this._track("Search", { query: el.value.substring(0, 100) });
                }
            }, 800);

            const onVisibility = () => {
                if (document.visibilityState === "hidden") this._flushNow();
            };

            document.addEventListener("click", onClick, { passive: true });
            document.addEventListener("scroll", onScroll, { passive: true });
            document.addEventListener("input", onInput, { passive: true });
            document.addEventListener("visibilitychange", onVisibility);
            this._listeners = [
                ["click", onClick, document],
                ["scroll", onScroll, document],
                ["input", onInput, document],
                ["visibilitychange", onVisibility, document],
            ];

            this._timer = setInterval(() => this._flush(), this.cfg.batchIntervalMs);
        }

        stop() {
            clearInterval(this._timer);
            this._flushNow();
            this._listeners.forEach(([evt, fn, target]) => target.removeEventListener(evt, fn));
        }

        track(type, payload) { this._track(type, payload); }

        _track(type, payload = null) {
            this.queue.push({
                type,
                timestampUtc: new Date().toISOString(),
                pageUrl: location.href,
                pageTitle: document.title,
                payloadJson: payload ? JSON.stringify(payload) : null,
            });
            this.totalEvents++;
            if (this.queue.length >= this.cfg.maxBatchSize) this._flush();
            if (!this.nudgeFired && this.totalEvents >= this.cfg.nudgeAfterEvents) {
                this.nudgeFired = true;
                this._triggerNudge();
            }
        }

        _flush() {
            if (!this.queue.length) return;
            const batch = this.queue.splice(0, this.queue.length);
            this.api.trackBatch({
                userId: this.cfg.userId,
                sessionId: this.cfg.sessionId,
                siteId: this.cfg.siteId,
                events: batch,
            }).catch(() => { /* fire-and-forget */ });
        }

        _flushNow() { this._flush(); }

        async _triggerNudge() {
            try {
                const resp = await this.api.recommend({
                    userId: this.cfg.userId,
                    sessionId: this.cfg.sessionId,
                    pageUrl: location.href,
                    pageTitle: document.title,
                    siteId: this.cfg.siteId,
                    kbIndexName: this.cfg.kbIndex,
                    maxRecommendations: 3,
                });
                if (resp.success && resp.recommendations?.length) {
                    this.onNudge(resp.recommendations, resp.inferredIntent);
                }
            } catch { /* non-fatal */ }
        }

        _debounce(fn, ms) {
            let t;
            return (...args) => { clearTimeout(t); t = setTimeout(() => fn(...args), ms); };
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // GESTURE DETECTOR
    // ══════════════════════════════════════════════════════════════════

    class GestureDetector {
        constructor(gesture, onStart, onMove, onEnd, onDetected) {
            this.gesture = gesture;
            this.onStart = onStart; this.onMove = onMove;
            this.onEnd = onEnd; this.onDetected = onDetected;
            this.pts = []; this.active = false;
            this._handlers = [];
        }

        attach() {
            const onMove = (e) => { if (!this.active) return; this.pts.push({ x: e.clientX, y: e.clientY }); this.onMove(this.pts); };
            const onKey = (e) => {
                const key = this.gesture.split("+")[0];
                const pressed = (key === "alt" && e.altKey) || (key === "ctrl" && e.ctrlKey) || (key === "shift" && e.shiftKey);
                if (e.type === "keydown" && pressed && !this.active) this._start();
                if (e.type === "keyup" && !pressed && this.active) this._stop();
            };
            const onMouse = (e) => {
                if (this.gesture !== "right-click-hold" || e.button !== 2) return;
                if (e.type === "mousedown") { e.preventDefault(); this._start(); }
                else if (this.active) this._stop();
            };
            const onContext = (e) => { if (this.gesture === "right-click-hold") e.preventDefault(); };

            document.addEventListener("mousemove", onMove);
            document.addEventListener("keydown", onKey);
            document.addEventListener("keyup", onKey);
            document.addEventListener("mousedown", onMouse);
            document.addEventListener("mouseup", onMouse);
            document.addEventListener("contextmenu", onContext);
            this._handlers = [
                ["mousemove", onMove], ["keydown", onKey], ["keyup", onKey],
                ["mousedown", onMouse], ["mouseup", onMouse], ["contextmenu", onContext],
            ];
        }

        detach() {
            this._handlers.forEach(([evt, fn]) => document.removeEventListener(evt, fn));
        }

        _start() { this.pts = []; this.active = true; this.onStart(); }
        _stop() {
            this.active = false;
            this.onEnd();
            if (this.pts.length > 20) {
                const r = this._detectCircle();
                if (r) this.onDetected(r);
            }
            this.pts = [];
        }

        _detectCircle() {
            const { pts } = this;
            let sx = 0, sy = 0;
            pts.forEach(p => { sx += p.x; sy += p.y; });
            const cx = sx / pts.length, cy = sy / pts.length;
            const dists = pts.map(p => Math.hypot(p.x - cx, p.y - cy));
            const avg = dists.reduce((a, b) => a + b) / dists.length;
            const variance = dists.reduce((s, d) => s + (d - avg) ** 2, 0) / dists.length;
            if (Math.sqrt(variance) / avg > 0.38) return null;
            const xs = pts.map(p => p.x), ys = pts.map(p => p.y);
            return {
                top: Math.min(...ys), left: Math.min(...xs),
                width: Math.max(...xs) - Math.min(...xs),
                height: Math.max(...ys) - Math.min(...ys),
                centerX: cx, centerY: cy, radius: avg,
            };
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // OVERLAY CANVAS
    // ══════════════════════════════════════════════════════════════════

    class Overlay {
        constructor() { this.canvas = null; this.ctx = null; }

        show() {
            if (this.canvas) return;
            this.canvas = Object.assign(document.createElement("canvas"), {
                id: "exp-overlay", width: innerWidth, height: innerHeight,
            });
            document.body.appendChild(this.canvas);
            this.ctx = this.canvas.getContext("2d");
        }

        draw(pts) {
            if (!this.ctx || pts.length < 2) return;
            this.ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);
            this.ctx.beginPath();
            this.ctx.moveTo(pts[0].x, pts[0].y);
            pts.forEach(p => this.ctx.lineTo(p.x, p.y));
            this.ctx.strokeStyle = "rgba(6,182,212,0.85)";
            this.ctx.lineWidth = 2.5;
            this.ctx.setLineDash([6, 4]);
            this.ctx.stroke();
        }

        hide() {
            if (!this.canvas) return;
            this.canvas.style.opacity = "0";
            setTimeout(() => { this.canvas?.remove(); this.canvas = null; }, 300);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // SIDEBAR
    // ══════════════════════════════════════════════════════════════════

    class Sidebar {
        constructor(cfg, api) {
            this.cfg = cfg; this.api = api;
            this.open = false; this.wide = false;
            this._build();
        }

        _build() {
            this.el = document.createElement("div");
            this.el.className = "exp-sidebar";
            this.el.innerHTML = `
                <div class="exp-header">
                    <div class="exp-logo">
                        <svg viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg">
                            <circle cx="12" cy="12" r="10"/>
                            <circle cx="9" cy="11" r="1.5" fill="#06B6D4"/>
                            <circle cx="15" cy="11" r="1.5" fill="#06B6D4"/>
                            <path d="M9 15.5c1 1 5 1 6 0" stroke="#06B6D4" stroke-width="1.5" stroke-linecap="round" fill="none"/>
                        </svg>
                    </div>
                    <div class="exp-title-group">
                        <div class="exp-title">Experion</div>
                        <div class="exp-subtitle">AI Assistant</div>
                    </div>
                    <div class="exp-hdr-btns">
                        <button class="exp-icon-btn" id="exp-expand" title="Expand">
                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M15 3h6v6M9 21H3v-6M21 3l-7 7M3 21l7-7"/></svg>
                        </button>
                        <button class="exp-icon-btn" id="exp-clear" title="Clear">
                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M3 6h18M8 6V4h8v2M19 6l-1 14H6L5 6"/></svg>
                        </button>
                        <button class="exp-icon-btn" id="exp-close" title="Close">
                            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M18 6L6 18M6 6l12 12"/></svg>
                        </button>
                    </div>
                </div>
                <div class="exp-messages" id="exp-msgs">
                    ${this._welcomeMsg()}
                </div>
                <div class="exp-footer">
                    <div class="exp-input-row">
                        <div class="exp-input-wrap">
                            <input id="exp-input" type="text" placeholder="Ask Experion…" autocomplete="off"/>
                        </div>
                        <button class="exp-send-btn" id="exp-send">
                            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><path d="M22 2L11 13M22 2l-7 20-4-9-9-4 20-7z"/></svg>
                        </button>
                    </div>
                    <div class="exp-hint">Press Alt+Drag to circle any area on the page</div>
                </div>
            `;
            document.body.appendChild(this.el);

            this.el.querySelector("#exp-close").addEventListener("click", () => this.hide());
            this.el.querySelector("#exp-expand").addEventListener("click", () => this.toggleWide());
            this.el.querySelector("#exp-clear").addEventListener("click", () => this.clearMessages());
            this.el.querySelector("#exp-send").addEventListener("click", () => this._sendMessage());
            const inp = this.el.querySelector("#exp-input");
            inp.addEventListener("keydown", e => { if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); this._sendMessage(); } });
        }

        _welcomeMsg() {
            return `<div class="exp-msg exp-ai">
                <p><strong>Hello! I'm Experion.</strong></p>
                <p>I'm watching this page and learning what you're looking for. Circle any area to get instant AI context, or just ask me anything.</p>
                <div class="exp-suggestions">
                    <button class="exp-suggestion">What's on this page?</button>
                    <button class="exp-suggestion">Help me find something</button>
                </div>
            </div>`;
        }

        show() {
            this.open = true;
            this.el.classList.add("exp-open");
            setTimeout(() => this.el.querySelector("#exp-input")?.focus(), 300);
        }

        hide() {
            this.open = false;
            this.el.classList.remove("exp-open");
        }

        toggleWide() {
            this.wide = !this.wide;
            this.el.classList.toggle("exp-wide", this.wide);
        }

        clearMessages() {
            this.el.querySelector("#exp-msgs").innerHTML = this._welcomeMsg();
            this._attachSuggestionListeners();
        }

        addMessage(html, role = "ai") {
            const msgs = this.el.querySelector("#exp-msgs");
            const div = document.createElement("div");
            div.className = `exp-msg exp-${role}`;
            div.innerHTML = html;
            msgs.appendChild(div);
            msgs.scrollTop = msgs.scrollHeight;
            this._attachSuggestionListeners();
            return div;
        }

        showResult(result) {
            if (!result.success) {
                this.addMessage(`<p>⚠️ ${result.errorMessage || "Something went wrong."}</p>`);
                return;
            }

            let html = renderMarkdown(result.message || result.contextSummary || "Here's what I found:");

            // Related documents
            if (result.relatedDocuments?.length) {
                html += `<div class="exp-docs">`;
                result.relatedDocuments.slice(0, 3).forEach(d => {
                    html += `<div class="exp-doc-card">`;
                    if (d.url) html += `<a href="${d.url}" target="_blank" rel="noopener">${d.title}</a>`;
                    else html += `<strong>${d.title}</strong>`;
                    if (d.snippet) html += `<br>${d.snippet.substring(0, 120)}…`;
                    html += `</div>`;
                });
                html += `</div>`;
            }

            // Recommendations
            if (result.recommendations?.length) {
                html += `<div class="exp-rec-section"><div class="exp-rec-label">Suggested for you</div><div class="exp-rec-cards">`;
                result.recommendations.forEach(r => {
                    html += `<div class="exp-rec-card" data-url="${r.actionUrl || ""}">
                        <div class="exp-rec-card-title">${r.title}</div>
                        ${r.description ? `<div class="exp-rec-card-desc">${r.description}</div>` : ""}
                        ${r.actionLabel ? `<div class="exp-rec-card-cta">→ ${r.actionLabel}</div>` : ""}
                    </div>`;
                });
                html += `</div></div>`;
            }

            // Suggestions
            if (result.suggestions?.length) {
                html += `<div class="exp-suggestions">`;
                result.suggestions.forEach(s => {
                    html += `<button class="exp-suggestion">${s}</button>`;
                });
                html += `</div>`;
            }

            this.addMessage(html);
        }

        showTyping() {
            return this.addMessage('<span class="exp-typing">Experion is thinking…</span>');
        }

        removeTyping(el) { el?.remove(); }

        _sendMessage() {
            const inp = this.el.querySelector("#exp-input");
            const text = inp.value.trim();
            if (!text) return;
            inp.value = "";
            this.addMessage(renderMarkdown(text), "user");
            this._onAsk?.(text);
        }

        onAsk(fn) { this._onAsk = fn; }

        _attachSuggestionListeners() {
            this.el.querySelectorAll(".exp-suggestion:not([data-bound])").forEach(btn => {
                btn.dataset.bound = "1";
                btn.addEventListener("click", () => {
                    const inp = this.el.querySelector("#exp-input");
                    inp.value = btn.textContent;
                    this._sendMessage();
                });
            });
            this.el.querySelectorAll(".exp-rec-card:not([data-bound])").forEach(card => {
                card.dataset.bound = "1";
                card.addEventListener("click", () => {
                    const url = card.dataset.url;
                    if (url) window.open(url, "_blank", "noopener");
                });
            });
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // NUDGE TOAST
    // ══════════════════════════════════════════════════════════════════

    class NudgeToast {
        show(recommendations, intent, onItemClick) {
            const existing = document.getElementById("exp-nudge");
            if (existing) existing.remove();

            const el = document.createElement("div");
            el.id = "exp-nudge";
            el.className = "exp-nudge";

            const intentLabel = {
                ProductSearch: "looking for a product",
                Decision: "ready to decide",
                Learning: "exploring content",
                Exploration: "browsing",
            }[intent] || "visiting";

            let html = `<button class="exp-nudge-close" aria-label="Dismiss">✕</button>
                <div class="exp-nudge-title">Suggestions for you</div>
                <div class="exp-nudge-body">Based on what you've been ${intentLabel}:</div>
                <div class="exp-nudge-rec">`;

            recommendations.slice(0, 3).forEach(r => {
                html += `<button class="exp-nudge-item" data-url="${r.actionUrl || ""}">${r.title}</button>`;
            });
            html += `</div>`;
            el.innerHTML = html;

            document.body.appendChild(el);
            requestAnimationFrame(() => el.classList.add("exp-nudge-in"));

            el.querySelector(".exp-nudge-close").addEventListener("click", () => el.remove());
            el.querySelectorAll(".exp-nudge-item").forEach(btn => {
                btn.addEventListener("click", () => {
                    const url = btn.dataset.url;
                    if (url) window.open(url, "_blank", "noopener");
                    onItemClick?.(btn.textContent);
                    el.remove();
                });
            });

            setTimeout(() => el.remove(), 12000);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // MAIN SDK CLASS
    // ══════════════════════════════════════════════════════════════════

    class ExperionSDK {
        constructor() {
            this._cfg = null;
            this._api = null;
            this._miner = null;
            this._gesture = null;
            this._overlay = null;
            this._sidebar = null;
            this._nudge = null;
            this._styleEl = null;
        }

        init(userConfig = {}) {
            const scriptCfg = parseScriptConfig();
            this._cfg = Object.assign({}, DEFAULTS, scriptCfg, userConfig);

            if (!this._cfg.apiUrl) {
                console.warn("[Experion] No data-api-url specified. SDK will not make any network calls.");
            }

            this._injectStyles();
            this._api = new ApiClient(this._cfg.apiUrl || "http://localhost:5000");
            this._overlay = new Overlay();
            this._nudge = new NudgeToast();
            this._sidebar = new Sidebar(this._cfg, this._api);
            this._sidebar.onAsk(async (q) => { await this._askFollowUp(q); });

            this._miner = new ActivityMiner(this._cfg, this._api, (recs, intent) => {
                this._nudge.show(recs, intent, (title) => {
                    this._sidebar.show();
                    this._sidebar.addMessage(renderMarkdown(title), "user");
                    this._askFollowUp(title);
                });
            });
            this._miner.start();

            this._gesture = new GestureDetector(
                this._cfg.gesture,
                () => this._overlay.show(),
                (pts) => this._overlay.draw(pts),
                () => this._overlay.hide(),
                (region) => this._onCircle(region)
            );
            this._gesture.attach();

            this._buildSphereButton();
        }

        identify(userId) {
            if (this._cfg) this._cfg.userId = userId;
        }

        track(type, payload = null) {
            this._miner?.track(type, payload);
        }

        async recommend() {
            if (!this._api) return;
            const resp = await this._api.recommend({
                userId: this._cfg.userId,
                sessionId: this._cfg.sessionId,
                pageUrl: location.href,
                pageTitle: document.title,
                siteId: this._cfg.siteId,
                kbIndexName: this._cfg.kbIndex,
                maxRecommendations: 3,
            });
            if (resp.success && resp.recommendations?.length) {
                this._nudge.show(resp.recommendations, resp.inferredIntent, (title) => {
                    this._sidebar.show();
                    this._askFollowUp(title);
                });
            }
        }

        destroy() {
            this._miner?.stop();
            this._gesture?.detach();
            this._overlay?.hide();
            this._sidebar?.el?.remove();
            document.getElementById("exp-nudge")?.remove();
            document.querySelector(".exp-sphere-btn")?.remove();
            this._styleEl?.remove();
        }

        // ── Private ────────────────────────────────────────────────

        async _onCircle(region) {
            this._sidebar.show();
            const typing = this._sidebar.showTyping();
            this._miner?.track("CircleGesture", { top: region.top, left: region.left, width: region.width, height: region.height });

            try {
                const { elements, capturedText } = extractDOM(region, 50);
                const result = await this._api.process({
                    userId: this._cfg.userId,
                    sessionId: this._cfg.sessionId,
                    pageUrl: location.href,
                    pageTitle: document.title,
                    siteId: this._cfg.siteId,
                    kbIndexName: this._cfg.kbIndex,
                    circleRegion: region,
                    capturedElements: elements,
                    capturedText,
                });
                this._sidebar.removeTyping(typing);
                this._sidebar.showResult(result);
                // Update sessionId with server-assigned one
                if (result.sessionId) this._cfg.sessionId = result.sessionId;
            } catch (err) {
                this._sidebar.removeTyping(typing);
                this._sidebar.addMessage(`<p>⚠️ Could not process request: ${err.message}</p>`);
            }
        }

        async _askFollowUp(question) {
            const typing = this._sidebar.showTyping();
            try {
                const result = await this._api.ask({
                    sessionId: this._cfg.sessionId,
                    question,
                    kbIndexName: this._cfg.kbIndex,
                });
                this._sidebar.removeTyping(typing);
                this._sidebar.showResult(result);
            } catch (err) {
                this._sidebar.removeTyping(typing);
                this._sidebar.addMessage(`<p>⚠️ ${err.message}</p>`);
            }
        }

        _buildSphereButton() {
            const btn = document.createElement("button");
            btn.className = "exp-sphere-btn";
            btn.title = "Experion — AI Assistant";
            btn.innerHTML = `<div class="exp-sphere"><div class="exp-eye exp-eye-l"></div><div class="exp-eye exp-eye-r"></div></div>`;
            document.body.appendChild(btn);
            setTimeout(() => btn.querySelector(".exp-sphere").classList.add("exp-ready"), 500);
            btn.addEventListener("click", () => {
                if (this._sidebar.open) this._sidebar.hide();
                else this._sidebar.show();
            });

            // Draggable
            let ox, oy, dragging = false;
            btn.addEventListener("mousedown", e => {
                dragging = true; ox = e.clientX - btn.offsetLeft; oy = e.clientY - btn.offsetTop;
                btn.querySelector(".exp-sphere").classList.add("dragging");
            });
            document.addEventListener("mousemove", e => {
                if (!dragging) return;
                btn.style.left = (e.clientX - ox) + "px"; btn.style.top = (e.clientY - oy) + "px";
                btn.style.right = "auto"; btn.style.bottom = "auto";
            });
            document.addEventListener("mouseup", () => {
                dragging = false;
                btn.querySelector(".exp-sphere").classList.remove("dragging");
            });
        }

        _injectStyles() {
            if (document.getElementById("exp-styles")) return;
            this._styleEl = document.createElement("style");
            this._styleEl.id = "exp-styles";
            this._styleEl.textContent = STYLES;
            document.head.appendChild(this._styleEl);
        }
    }

    // ══════════════════════════════════════════════════════════════════
    // BOOTSTRAP
    // ══════════════════════════════════════════════════════════════════

    const sdk = new ExperionSDK();
    global.Experion = sdk;

    const scriptCfg = parseScriptConfig();
    if (scriptCfg.autoInit !== false) {
        if (document.readyState === "loading") {
            document.addEventListener("DOMContentLoaded", () => sdk.init());
        } else {
            sdk.init();
        }
    }

})(window);
