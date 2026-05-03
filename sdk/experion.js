/**
 * Experion Agent SDK v2 — Drop-in Activity Mining & Recommendation Agent
 *
 * Features:
 *   1. Activity Tracker — passively records pageviews, clicks, scrolls, form interactions, idle
 *   2. Identity Resolver — uses userId (if available) or browser fingerprint + IP
 *   3. Circle Gesture — user circles an area, SDK captures DOM and sends for AI analysis
 *   4. Recommendation Engine — periodically fetches proactive suggestions
 *   5. Chat Sidebar — persistent UI for chatbot + recommendation display
 *
 * Usage (single script tag, zero host-site code changes):
 *   <script src="https://cdn.example.com/experion.js"
 *           data-site-id="your-site-id"
 *           data-api-url="https://api.example.com/api"
 *           data-user-id="optional-logged-in-user-id"
 *           data-api-key="optional-api-key">
 *   </script>
 */
(function () {
  "use strict";

  // ── CONFIG ────────────────────────────────────────────────────────
  const SCRIPT = document.currentScript;
  const CFG = {
    siteId:     SCRIPT?.getAttribute("data-site-id") || "default",
    apiUrl:     (SCRIPT?.getAttribute("data-api-url") || "/api").replace(/\/$/, ""),
    userId:     SCRIPT?.getAttribute("data-user-id") || null,
    apiKey:     SCRIPT?.getAttribute("data-api-key") || null,
    trigger:    SCRIPT?.getAttribute("data-trigger") || "alt+draw",
    batchSize:  20,
    batchMs:    10000,
    idleSec:    30,
    recCooldownMs: 300000,
  };

  // ── FINGERPRINT ───────────────────────────────────────────────────
  function fingerprint() {
    const raw = [
      navigator.userAgent,
      screen.width + "x" + screen.height,
      Intl.DateTimeFormat().resolvedOptions().timeZone,
      navigator.language,
      navigator.platform,
    ].join("|");
    let hash = 0;
    for (let i = 0; i < raw.length; i++) {
      hash = ((hash << 5) - hash + raw.charCodeAt(i)) | 0;
    }
    return "fp_" + Math.abs(hash).toString(36);
  }

  const CLIENT_FP = fingerprint();
  const SESSION_ID =
    "s_" + Date.now().toString(36) + Math.random().toString(36).slice(2, 6);

  // ── CONSTANTS ─────────────────────────────────────────────────────
  const SKIP_TAGS = new Set([
    "SCRIPT","STYLE","NOSCRIPT","IFRAME","SVG","PATH","CIRCLE","RECT",
    "LINE","POLYGON","POLYLINE","ELLIPSE","META","LINK","BR","HR","WBR",
  ]);
  const INTERACTIVE_TAGS = new Set([
    "INPUT","BUTTON","SELECT","TEXTAREA","A","DETAILS","SUMMARY","LABEL",
  ]);
  const MEANINGFUL_TAGS = new Set([
    "H1","H2","H3","H4","H5","H6","P","SPAN","DIV","LI","TD","TH",
    "TABLE","TR","THEAD","TBODY","FORM","NAV","MAIN","ARTICLE","SECTION",
    "ASIDE","HEADER","FOOTER","FIGCAPTION","BLOCKQUOTE","PRE","CODE",
    "IMG","FIGURE","UL","OL","DL","DT","DD","STRONG","EM","B","I",
    ...INTERACTIVE_TAGS,
  ]);

  // ── API CLIENT ────────────────────────────────────────────────────
  const api = {
    async post(path, body) {
      const headers = {
        "Content-Type": "application/json",
        "X-SDK-Name": "ExperionAgent",
      };
      if (CFG.apiKey) headers["X-API-Key"] = CFG.apiKey;
      const res = await fetch(`${CFG.apiUrl}${path}`, {
        method: "POST",
        headers,
        body: JSON.stringify(body),
      });
      if (!res.ok) throw new Error(`API ${res.status}`);
      return res.json();
    },
    ingest(events) {
      return this.post("/activity/ingest", {
        userId: CFG.userId,
        sessionId: SESSION_ID,
        siteId: CFG.siteId,
        clientFingerprint: CLIENT_FP,
        events,
      });
    },
    recommend(trigger, recentEvents) {
      return this.post("/recommend", {
        userId: CFG.userId,
        sessionId: SESSION_ID,
        siteId: CFG.siteId,
        clientFingerprint: CLIENT_FP,
        currentPageUrl: location.href,
        currentPageTitle: document.title,
        recentEvents,
        trigger,
      });
    },
    circleProcess(elements, region, capturedText) {
      return this.post("/chat/process", {
        userId: CFG.userId,
        sessionId: SESSION_ID,
        siteId: CFG.siteId,
        clientFingerprint: CLIENT_FP,
        pageUrl: location.href,
        pageTitle: document.title,
        circleRegion: region,
        capturedElements: elements,
        capturedText,
      });
    },
    ask(conversationId, question) {
      return this.post("/chat/ask", {
        userId: CFG.userId,
        sessionId: SESSION_ID,
        siteId: CFG.siteId,
        clientFingerprint: CLIENT_FP,
        conversationId,
        question,
      });
    },
    feedback(conversationId, positive) {
      return this.post("/chat/feedback", { conversationId, positive });
    },
  };

  // ── ACTIVITY TRACKER ──────────────────────────────────────────────
  const tracker = {
    buffer: [],
    timer: null,
    idleTimer: null,
    lastActivity: Date.now(),
    scrollDepths: new Map(),
    lastRecTime: 0,
    totalEvents: 0,

    init() {
      this.track("SessionStart", { url: location.href, title: document.title });
      this.startBatchTimer();
      this.startIdleDetector();
      this.attachListeners();
    },

    track(type, meta = {}) {
      this.buffer.push({
        type,
        timestamp: new Date().toISOString(),
        pageUrl: location.href,
        pageTitle: document.title,
        ...meta,
      });
      this.totalEvents++;
      this.lastActivity = Date.now();
      if (this.buffer.length >= CFG.batchSize) this.flush();
    },

    attachListeners() {
      // Page views (SPA-aware)
      let lastUrl = location.href;
      const checkNav = () => {
        if (location.href !== lastUrl) {
          lastUrl = location.href;
          this.track("PageView", { url: lastUrl, title: document.title });
          this.maybeRecommend("page_change");
        }
      };
      window.addEventListener("popstate", checkNav);
      const origPush = history.pushState;
      history.pushState = function () {
        origPush.apply(this, arguments);
        checkNav();
      };

      // Clicks
      document.addEventListener("click", (e) => {
        const el = e.target.closest("a, button, [role=button], input[type=submit]");
        if (el) {
          this.track("Click", {
            elementTag: el.tagName,
            elementText: (el.innerText || el.value || "").slice(0, 100),
            elementSelector: cssSelector(el),
          });
        }
      });

      // Scroll depth
      let scrollTick = false;
      window.addEventListener("scroll", () => {
        if (scrollTick) return;
        scrollTick = true;
        requestAnimationFrame(() => {
          scrollTick = false;
          const pct = Math.round(
            ((window.scrollY + window.innerHeight) /
              document.documentElement.scrollHeight) * 100
          );
          const key = location.pathname;
          const prev = this.scrollDepths.get(key) || 0;
          if (pct > prev && pct % 25 === 0) {
            this.scrollDepths.set(key, pct);
            this.track("ScrollDepth", { depth: pct });
          }
        });
      });

      // Form interactions
      document.addEventListener("focusin", (e) => {
        if (
          e.target.tagName === "INPUT" ||
          e.target.tagName === "TEXTAREA" ||
          e.target.tagName === "SELECT"
        ) {
          this.track("FormFocus", {
            elementTag: e.target.tagName,
            elementSelector: cssSelector(e.target),
            metadata: { type: e.target.type, name: e.target.name },
          });
        }
      });

      document.addEventListener("submit", (e) => {
        this.track("FormSubmit", { elementSelector: cssSelector(e.target) });
      });

      // Tab visibility
      document.addEventListener("visibilitychange", () => {
        this.track("TabSwitch", { metadata: { hidden: document.hidden } });
      });

      // Page unload
      window.addEventListener("beforeunload", () => {
        this.track("SessionEnd");
        this.flush(true);
      });
    },

    startBatchTimer() {
      this.timer = setInterval(() => this.flush(), CFG.batchMs);
    },

    startIdleDetector() {
      this.idleTimer = setInterval(() => {
        const idle = (Date.now() - this.lastActivity) / 1000;
        if (idle >= CFG.idleSec) {
          this.track("IdleStart", { metadata: { idleSeconds: idle } });
          this.maybeRecommend("idle");
        }
      }, CFG.idleSec * 1000);
    },

    async flush(sync = false) {
      if (this.buffer.length === 0) return;
      const batch = this.buffer.splice(0);
      try {
        if (sync && navigator.sendBeacon) {
          navigator.sendBeacon(
            `${CFG.apiUrl}/activity/ingest`,
            new Blob(
              [
                JSON.stringify({
                  userId: CFG.userId,
                  sessionId: SESSION_ID,
                  siteId: CFG.siteId,
                  clientFingerprint: CLIENT_FP,
                  events: batch,
                }),
              ],
              { type: "application/json" }
            )
          );
        } else {
          await api.ingest(batch);
        }
      } catch (err) {
        console.warn("[Experion] Ingest failed:", err.message);
        this.buffer.unshift(...batch);
      }
    },

    async maybeRecommend(trigger) {
      const now = Date.now();
      if (now - this.lastRecTime < CFG.recCooldownMs) return;
      this.lastRecTime = now;
      try {
        const recent = this.buffer.slice(-10);
        const res = await api.recommend(trigger, recent);
        if (res.success && res.recommendations?.length > 0) {
          ui.showRecommendations(res.recommendations);
        }
      } catch (err) {
        console.warn("[Experion] Recommend failed:", err.message);
      }
    },
  };

  // ── DOM EXTRACTION ────────────────────────────────────────────────
  function extractDOM(region, max = 50) {
    const all = document.querySelectorAll("*");
    const captured = [];
    const seen = new Set();
    const textParts = [];
    for (const el of all) {
      if (captured.length >= max) break;
      if (SKIP_TAGS.has(el.tagName)) continue;
      if (!MEANINGFUL_TAGS.has(el.tagName)) continue;
      if (!isVisible(el)) continue;
      const r = el.getBoundingClientRect();
      if (!overlaps(r, region)) continue;
      const text = el.innerText?.substring(0, 500)?.trim() || null;
      if (text && seen.has(text)) continue;
      if (text) { seen.add(text); textParts.push(text); }
      captured.push({
        tag: el.tagName, id: el.id || undefined, text: text || undefined,
        rect: { top: r.top, left: r.left, width: r.width, height: r.height },
        isInteractive: INTERACTIVE_TAGS.has(el.tagName),
      });
    }
    return { elements: captured, capturedText: textParts.join("\n").substring(0, 5000) };
  }

  function isVisible(el) {
    const s = getComputedStyle(el);
    if (s.display === "none" || s.visibility === "hidden" || s.opacity === "0") return false;
    const r = el.getBoundingClientRect();
    return r.width > 0 && r.height > 0;
  }

  function overlaps(rect, region) {
    return !(
      rect.right < region.left || rect.left > region.left + region.width ||
      rect.bottom < region.top || rect.top > region.top + region.height
    );
  }

  function cssSelector(el) {
    if (el.id) return "#" + el.id;
    let path = el.tagName.toLowerCase();
    if (el.className) path += "." + el.className.trim().split(/\s+/)[0];
    return path;
  }

  // ── GESTURE DETECTOR ──────────────────────────────────────────────
  const gesture = {
    points: [],
    active: false,

    init() {
      document.addEventListener("mousemove", (e) => this.onMove(e));
      document.addEventListener("keydown", (e) => this.onKey(e));
      document.addEventListener("keyup", (e) => this.onKey(e));
    },

    onKey(e) {
      const trig = CFG.trigger.split("+")[0];
      const held =
        (trig === "alt" && e.altKey) || (trig === "ctrl" && e.ctrlKey);
      if (e.type === "keydown" && held && !this.active) this.start();
      if (e.type === "keyup" && !held && this.active) this.stop();
    },

    onMove(e) {
      if (!this.active) return;
      this.points.push({ x: e.clientX, y: e.clientY });
      overlay.draw(this.points);
    },

    start() {
      this.points = [];
      this.active = true;
      overlay.show();
    },

    stop() {
      this.active = false;
      overlay.hide();
      if (this.points.length > 20) {
        const region = this.detectCircle();
        if (region) this.onDetected(region);
      }
      this.points = [];
    },

    detectCircle() {
      const pts = this.points;
      let sx = 0, sy = 0;
      pts.forEach((p) => { sx += p.x; sy += p.y; });
      const cx = sx / pts.length, cy = sy / pts.length;
      const dists = pts.map((p) => Math.sqrt((p.x - cx) ** 2 + (p.y - cy) ** 2));
      const avgR = dists.reduce((a, b) => a + b) / dists.length;
      const variance = dists.reduce((s, d) => s + (d - avgR) ** 2, 0) / dists.length;
      if (Math.sqrt(variance) / avgR > 0.35) return null;
      return {
        top: Math.min(...pts.map((p) => p.y)),
        left: Math.min(...pts.map((p) => p.x)),
        width: Math.max(...pts.map((p) => p.x)) - Math.min(...pts.map((p) => p.x)),
        height: Math.max(...pts.map((p) => p.y)) - Math.min(...pts.map((p) => p.y)),
      };
    },

    async onDetected(region) {
      tracker.track("CircleGesture", { metadata: region });
      const { elements, capturedText } = extractDOM(region);
      if (elements.length === 0) return;
      ui.openSidebar();
      ui.addMessage("ai", "Analyzing what you circled...", "loading");
      try {
        const res = await api.circleProcess(elements, region, capturedText);
        ui.removeLoading();
        if (res.success) {
          ui.currentConversationId = res.conversationId;
          ui.addMessage("ai", res.message, null, res.suggestions);
        } else {
          ui.addMessage("ai", res.errorMessage || "Something went wrong.");
        }
      } catch (err) {
        ui.removeLoading();
        ui.addMessage("ai", "Failed to analyze. Please try again.");
      }
    },
  };

  // ── OVERLAY ───────────────────────────────────────────────────────
  const overlay = {
    canvas: null,
    ctx: null,

    show() {
      if (this.canvas) return;
      this.canvas = document.createElement("canvas");
      Object.assign(this.canvas.style, {
        position: "fixed", top: 0, left: 0, width: "100vw", height: "100vh",
        zIndex: 2147483646, pointerEvents: "none",
      });
      this.canvas.width = window.innerWidth;
      this.canvas.height = window.innerHeight;
      document.body.appendChild(this.canvas);
      this.ctx = this.canvas.getContext("2d");
    },

    draw(points) {
      if (!this.ctx) return;
      this.ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);
      this.ctx.beginPath();
      this.ctx.moveTo(points[0].x, points[0].y);
      points.forEach((p) => this.ctx.lineTo(p.x, p.y));
      this.ctx.strokeStyle = "rgba(0, 210, 211, 0.8)";
      this.ctx.lineWidth = 2.5;
      this.ctx.setLineDash([6, 4]);
      this.ctx.stroke();
    },

    hide() {
      if (!this.canvas) return;
      this.canvas.style.opacity = "0";
      setTimeout(() => { this.canvas?.remove(); this.canvas = null; }, 300);
    },
  };

  // ── UI (Sphere + Sidebar) ─────────────────────────────────────────
  const ui = {
    currentConversationId: null,
    sidebarVisible: false,

    init() {
      this.injectStyles();
      this.createSphere();
      this.createSidebar();
    },

    injectStyles() {
      const style = document.createElement("style");
      style.textContent = `
:root {
  --exp-brand: rgba(0,210,211,0.8);
}
.exp-sphere {
  position:fixed; bottom:30px; right:30px; width:72px; height:72px;
  border:none; background:transparent; cursor:pointer; z-index:2147483640;
  animation: exp-float 4s ease-in-out infinite; padding:0; user-select:none;
}
.exp-sphere-body {
  width:72px; height:72px; border-radius:50%;
  background: radial-gradient(84% 84% at 40% 40%, #06B6D4 0%, #3B82F6 33%, #8B5CF6 67%, #A855F7 100%);
  box-shadow: 0 0 20px rgba(6,182,212,0.5), 0 0 35px rgba(59,130,246,0.3);
  transition: all 0.4s cubic-bezier(.175,.885,.32,1.275);
  position:relative; overflow:hidden;
}
.exp-sphere:hover .exp-sphere-body {
  transform: scale(1.12); box-shadow: 0 0 40px rgba(168,85,247,0.8);
}
.exp-eye {
  position:absolute; width:12px; height:12px; background:#fff; border-radius:50%;
  top:26px; z-index:2; box-shadow: 0 0 8px rgba(255,255,255,1);
  animation: exp-eye-cycle 10s infinite ease-in-out;
}
.exp-eye.left { left:20px; }
.exp-eye.right { left:40px; }
.exp-badge {
  position:absolute; top:-4px; right:-4px; min-width:20px; height:20px;
  background:#EF4444; color:#fff; border-radius:10px; font-size:11px;
  display:flex; align-items:center; justify-content:center; padding:0 5px;
  font-family:sans-serif; font-weight:700; opacity:0; transition:opacity 0.3s;
}
.exp-badge.show { opacity:1; }
@keyframes exp-float { 0%,100%{transform:translateY(0)} 50%{transform:translateY(-5px)} }
@keyframes exp-eye-cycle {
  0%,30%{transform:translateX(0);height:12px;border-radius:50%}
  35%{transform:scaleY(0.1)}
  40%,60%{transform:translateX(-6px);height:8px;border-radius:50% 50% 2px 2px}
  65%{transform:scaleY(0.1)}
  70%,90%{transform:translateX(6px);height:8px;border-radius:50% 50% 2px 2px}
  95%{transform:scaleY(0.1)}
  100%{transform:translateX(0);height:12px;border-radius:50%}
}

/* Sidebar */
.exp-sidebar {
  position:fixed; right:0; top:0; bottom:0; width:380px; height:100vh;
  background:#fff; box-shadow:-2px 0 15px rgba(0,0,0,0.12);
  z-index:2147483645; display:flex; flex-direction:column;
  transform:translateX(100%); transition:transform 0.3s ease;
  font-family:'Segoe UI','Noto Sans',system-ui,sans-serif;
}
.exp-sidebar.open { transform:translateX(0); }

.exp-sb-header {
  display:flex; align-items:center; padding:20px; height:80px;
  background:linear-gradient(90deg,#D8E7FE,#F8FED8); border-bottom:1px solid #E5E8F0;
}
.exp-sb-logo { display:flex; align-items:center; gap:12px; flex:1; }
.exp-sb-avatar {
  width:44px; height:44px; border-radius:50%;
  background:linear-gradient(148deg,#296DF5 3%,#84F4FB 92%);
  box-shadow:0 2px 30px 5px #C4DAFD; position:relative;
}
.exp-sb-avatar-eye {
  position:absolute; width:15%; height:15%; background:#fff; border-radius:50%; top:35%;
}
.exp-sb-avatar-eye.l { left:30%; }
.exp-sb-avatar-eye.r { right:30%; }
.exp-sb-title { font-weight:600; font-size:15px; color:#171D27; }
.exp-sb-subtitle { font-size:11px; color:#171D27; opacity:0.7; }
.exp-sb-close {
  width:28px; height:28px; border:none; background:transparent;
  cursor:pointer; color:#3C4C67; border-radius:6px; display:flex;
  align-items:center; justify-content:center;
}
.exp-sb-close:hover { background:rgba(0,0,0,0.06); }

.exp-sb-content {
  flex:1; overflow-y:auto; padding:16px; display:flex;
  flex-direction:column; gap:12px;
}

.exp-msg {
  max-width:88%; padding:10px 14px; border-radius:12px;
  font-size:13px; line-height:1.55; word-wrap:break-word;
}
.exp-msg.ai { align-self:flex-start; background:#F8FAFC; color:#1E293B; border:1px solid #E2E8F0; }
.exp-msg.user { align-self:flex-end; background:#E0F2FE; color:#0369A1; }
.exp-msg.loading { font-style:italic; color:#64748B; animation:exp-pulse 1.5s infinite; }
@keyframes exp-pulse { 0%,100%{opacity:1} 50%{opacity:.5} }

.exp-suggestions { display:flex; flex-wrap:wrap; gap:6px; margin-top:8px; }
.exp-sug-btn {
  padding:5px 10px; border-radius:14px; border:1px solid #CBD5E1;
  background:#fff; font-size:11px; cursor:pointer; transition:all 0.2s;
}
.exp-sug-btn:hover { border-color:#3B82F6; color:#3B82F6; background:#F0F9FF; }

/* Recommendation toast */
.exp-rec-toast {
  position:fixed; bottom:120px; right:30px; max-width:320px;
  background:#fff; border-radius:12px; box-shadow:0 4px 24px rgba(0,0,0,0.15);
  padding:14px 18px; z-index:2147483641; font-family:system-ui,sans-serif;
  animation: exp-slide-in 0.4s ease-out;
  border-left:4px solid #06B6D4;
}
.exp-rec-toast .exp-rec-title { font-weight:600; font-size:13px; color:#1E293B; }
.exp-rec-toast .exp-rec-msg { font-size:12px; color:#475569; margin-top:4px; line-height:1.4; }
.exp-rec-toast .exp-rec-dismiss {
  position:absolute; top:6px; right:8px; background:none; border:none;
  cursor:pointer; color:#94A3B8; font-size:16px;
}
@keyframes exp-slide-in { from{transform:translateX(100px);opacity:0} to{transform:translateX(0);opacity:1} }

.exp-sb-footer {
  padding:12px 16px; background:#F8FAFC; border-top:1px solid #E2E8F0;
  display:flex; gap:8px;
}
.exp-sb-input-wrap {
  flex:1; background:#fff; border:1px solid #CBD5E1; border-radius:8px; padding:0 10px;
}
.exp-sb-input-wrap input {
  width:100%; height:36px; border:none; outline:none; font-size:13px;
  font-family:inherit; background:transparent;
}
.exp-sb-send {
  width:36px; height:36px; border-radius:50%; border:none;
  background:#3B82F6; color:#fff; cursor:pointer; display:flex;
  align-items:center; justify-content:center; flex-shrink:0;
}
.exp-sb-send:hover { background:#2563EB; }
`;
      document.head.appendChild(style);
    },

    createSphere() {
      const btn = document.createElement("button");
      btn.className = "exp-sphere";
      btn.innerHTML = `
        <div class="exp-sphere-body">
          <div class="exp-eye left"></div>
          <div class="exp-eye right"></div>
        </div>
        <div class="exp-badge" id="exp-badge">0</div>`;
      btn.addEventListener("click", () => this.toggleSidebar());
      document.body.appendChild(btn);
      setTimeout(() => { btn.querySelector(".exp-sphere-body").style.opacity = 1; }, 100);
    },

    createSidebar() {
      const sb = document.createElement("div");
      sb.className = "exp-sidebar";
      sb.id = "exp-sidebar";
      sb.innerHTML = `
        <div class="exp-sb-header">
          <div class="exp-sb-logo">
            <div class="exp-sb-avatar">
              <div class="exp-sb-avatar-eye l"></div>
              <div class="exp-sb-avatar-eye r"></div>
            </div>
            <div>
              <div class="exp-sb-title">Experion</div>
              <div class="exp-sb-subtitle">AI Recommendation Agent</div>
            </div>
          </div>
          <button class="exp-sb-close" id="exp-sb-close">
            <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M18 6L6 18M6 6l12 12"/></svg>
          </button>
        </div>
        <div class="exp-sb-content" id="exp-sb-content">
          <div class="exp-msg ai">
            Hi! I'm <b>Experion</b> — your AI assistant. I learn from your browsing to offer helpful suggestions. You can also circle any area on the page (hold <b>Alt + draw</b>) and I'll explain it.
            <div class="exp-suggestions">
              <button class="exp-sug-btn" data-q="What can you do?">What can you do?</button>
              <button class="exp-sug-btn" data-q="Show me recommendations">Recommendations</button>
            </div>
          </div>
        </div>
        <div class="exp-sb-footer">
          <div class="exp-sb-input-wrap">
            <input type="text" id="exp-input" placeholder="Ask Experion anything..." />
          </div>
          <button class="exp-sb-send" id="exp-sb-send">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M22 2L11 13M22 2l-7 20-4-9-9-4 20-7z"/></svg>
          </button>
        </div>`;
      document.body.appendChild(sb);

      sb.querySelector("#exp-sb-close").addEventListener("click", () => this.closeSidebar());
      sb.querySelector("#exp-sb-send").addEventListener("click", () => this.sendMessage());
      sb.querySelector("#exp-input").addEventListener("keydown", (e) => {
        if (e.key === "Enter") this.sendMessage();
      });
      sb.addEventListener("click", (e) => {
        const sugBtn = e.target.closest(".exp-sug-btn");
        if (sugBtn) {
          const q = sugBtn.dataset.q || sugBtn.textContent;
          sb.querySelector("#exp-input").value = q;
          this.sendMessage();
        }
      });
    },

    toggleSidebar() {
      this.sidebarVisible ? this.closeSidebar() : this.openSidebar();
    },

    openSidebar() {
      document.getElementById("exp-sidebar").classList.add("open");
      this.sidebarVisible = true;
      this.setBadge(0);
    },

    closeSidebar() {
      document.getElementById("exp-sidebar").classList.remove("open");
      this.sidebarVisible = false;
    },

    addMessage(role, text, cls, suggestions) {
      const content = document.getElementById("exp-sb-content");
      const msg = document.createElement("div");
      msg.className = `exp-msg ${role}${cls ? " " + cls : ""}`;
      msg.innerHTML = text;
      if (suggestions?.length) {
        const sugDiv = document.createElement("div");
        sugDiv.className = "exp-suggestions";
        suggestions.forEach((s) => {
          const btn = document.createElement("button");
          btn.className = "exp-sug-btn";
          btn.textContent = s;
          btn.dataset.q = s;
          sugDiv.appendChild(btn);
        });
        msg.appendChild(sugDiv);
      }
      content.appendChild(msg);
      content.scrollTop = content.scrollHeight;
    },

    removeLoading() {
      const el = document.querySelector(".exp-msg.loading");
      if (el) el.remove();
    },

    setBadge(n) {
      const badge = document.getElementById("exp-badge");
      if (badge) {
        badge.textContent = n;
        badge.classList.toggle("show", n > 0);
      }
    },

    async sendMessage() {
      const input = document.getElementById("exp-input");
      const q = input.value.trim();
      if (!q) return;
      input.value = "";

      this.addMessage("user", q);
      this.addMessage("ai", "Thinking...", "loading");
      tracker.track("ChatMessage", { metadata: { question: q } });

      try {
        if (!this.currentConversationId) {
          this.currentConversationId = "c_" + Date.now().toString(36);
        }
        const res = await api.ask(this.currentConversationId, q);
        this.removeLoading();
        if (res.success) {
          if (res.conversationId) this.currentConversationId = res.conversationId;
          this.addMessage("ai", res.message, null, res.suggestions);
        } else {
          this.addMessage("ai", res.errorMessage || "Something went wrong.");
        }
      } catch {
        this.removeLoading();
        this.addMessage("ai", "Failed to get response. Please try again.");
      }
    },

    pendingRecs: [],

    showRecommendations(recs) {
      if (this.sidebarVisible) {
        recs.forEach((r) => {
          this.addMessage("ai",
            `<b>${r.title}</b><br>${r.message}` +
            (r.actionUrl ? `<br><a href="${r.actionUrl}" style="color:#3B82F6">${r.actionLabel || "Go"}</a>` : "")
          );
        });
      } else {
        recs.forEach((r, i) => {
          setTimeout(() => this.showToast(r), i * 2000);
        });
        this.setBadge(recs.length);
      }
    },

    showToast(rec) {
      const existing = document.querySelector(".exp-rec-toast");
      if (existing) existing.remove();

      const toast = document.createElement("div");
      toast.className = "exp-rec-toast";
      toast.innerHTML = `
        <button class="exp-rec-dismiss">&times;</button>
        <div class="exp-rec-title">${rec.title}</div>
        <div class="exp-rec-msg">${rec.message}</div>`;
      toast.querySelector(".exp-rec-dismiss").addEventListener("click", () => toast.remove());
      document.body.appendChild(toast);
      setTimeout(() => toast.remove(), 8000);
    },
  };

  // ── INIT ──────────────────────────────────────────────────────────
  function boot() {
    ui.init();
    tracker.init();
    gesture.init();

    // Fetch initial recommendations after a short delay
    setTimeout(() => tracker.maybeRecommend("session_start"), 3000);
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", boot);
  } else {
    boot();
  }
})();
