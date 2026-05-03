/**
 * Experion SDK — drop-in recommendation agent for any website.
 *
 *   <script src="/experion.js"
 *           data-tenant="default"
 *           data-api="http://localhost:5077"
 *           data-user-id=""    (optional)
 *           data-auto-init="true"></script>
 *
 * Provides:
 *   - Identity (UserID or anonymous + IP/region cluster)
 *   - Passive activity tracking (pageview, click, scroll, dwell, form, input)
 *   - Circle gesture capture (Alt + drag to circle a region of the page)
 *   - Idle / time trigger (asks server for proactive nudge)
 *   - Floating sphere + chat sidebar UI
 *   - SignalR client for proactive nudges + action-executed echoes
 */
(function () {
  'use strict';

  // ── Boot config ────────────────────────────────────────────────────
  const script = document.currentScript || (function(){
    const s = document.getElementsByTagName('script');
    return s[s.length - 1];
  })();
  const cfg = {
    tenant:   script.getAttribute('data-tenant')   || 'default',
    api:      (script.getAttribute('data-api')     || '').replace(/\/$/, '') || window.location.origin,
    userId:   script.getAttribute('data-user-id')  || null,
    autoInit: (script.getAttribute('data-auto-init') || 'true') === 'true'
  };

  // ── Styles ─────────────────────────────────────────────────────────
  const css = `
.exp-sphere{position:fixed;bottom:24px;right:24px;width:64px;height:64px;border-radius:50%;
  background:radial-gradient(circle at 35% 35%,#06b6d4,#3b82f6 45%,#8b5cf6 80%,#a855f7);
  box-shadow:0 0 30px rgba(99,102,241,.45);cursor:pointer;z-index:2147483600;
  display:flex;align-items:center;justify-content:center;color:#fff;font:600 22px -apple-system,sans-serif;
  transition:transform .25s ease;user-select:none}
.exp-sphere:hover{transform:scale(1.08) translateY(-2px)}
.exp-sphere .exp-pulse{position:absolute;inset:-8px;border-radius:50%;border:2px solid rgba(99,102,241,.6);
  animation:exp-pulse 2.4s infinite ease-out;pointer-events:none}
@keyframes exp-pulse{0%{transform:scale(.85);opacity:.9}100%{transform:scale(1.4);opacity:0}}

.exp-panel{position:fixed;right:24px;bottom:104px;width:380px;max-height:580px;background:#fff;
  border-radius:14px;box-shadow:0 18px 45px rgba(15,23,42,.22);display:none;flex-direction:column;
  z-index:2147483601;font:14px/1.5 -apple-system,'Segoe UI',Roboto,sans-serif;overflow:hidden;
  border:1px solid #e2e8f0}
.exp-panel.open{display:flex;animation:exp-slide-up .25s ease}
@keyframes exp-slide-up{from{transform:translateY(20px);opacity:0}to{transform:translateY(0);opacity:1}}
.exp-panel header{padding:14px 16px;background:linear-gradient(135deg,#3b82f6,#8b5cf6);color:#fff;
  display:flex;align-items:center;gap:10px}
.exp-panel header .exp-mini{width:28px;height:28px;border-radius:50%;
  background:radial-gradient(circle at 35% 35%,#06b6d4,#3b82f6 60%,#a855f7)}
.exp-panel header .exp-title{flex:1;font-weight:600}
.exp-panel header button{background:transparent;border:0;color:#fff;cursor:pointer;font-size:18px;line-height:1}
.exp-msgs{flex:1;overflow-y:auto;padding:14px;background:#f8fafc;display:flex;flex-direction:column;gap:10px}
.exp-msg{max-width:88%;padding:10px 12px;border-radius:10px;white-space:pre-wrap;word-break:break-word;font-size:13px}
.exp-msg.ai{align-self:flex-start;background:#fff;border:1px solid #e2e8f0;color:#1e293b}
.exp-msg.user{align-self:flex-end;background:#dbeafe;color:#1e3a8a}
.exp-msg.nudge{align-self:flex-start;background:#fef3c7;border:1px solid #f59e0b;color:#78350f}
.exp-msg.system{align-self:center;background:transparent;color:#64748b;font-size:11px;font-style:italic}
.exp-meta{font-size:10px;color:#94a3b8;margin-top:6px}
.exp-pipeline{margin-top:6px;font-size:10px;background:#f1f5f9;border-radius:6px;padding:6px 8px;color:#334155}
.exp-pipeline div{display:flex;justify-content:space-between;gap:8px}
.exp-pipeline div span:first-child{font-weight:600}
.exp-suggestions{display:flex;gap:6px;flex-wrap:wrap;margin-top:8px}
.exp-chip{padding:4px 10px;border:1px solid #cbd5e1;background:#fff;border-radius:14px;font-size:11px;
  cursor:pointer;color:#334155}
.exp-chip:hover{border-color:#3b82f6;color:#1e40af;background:#eff6ff}
.exp-input{display:flex;gap:8px;padding:10px;border-top:1px solid #e2e8f0;background:#fff}
.exp-input input{flex:1;padding:8px 10px;border:1px solid #cbd5e1;border-radius:8px;font:13px sans-serif;outline:none}
.exp-input input:focus{border-color:#3b82f6}
.exp-input button{padding:8px 14px;border:0;background:#3b82f6;color:#fff;border-radius:8px;cursor:pointer;font-size:13px}
.exp-input button:disabled{opacity:.5;cursor:not-allowed}

.exp-overlay{position:fixed;inset:0;pointer-events:none;z-index:2147483599}
.exp-toast{position:fixed;bottom:104px;right:104px;background:#0f172a;color:#fff;padding:10px 14px;
  border-radius:10px;font:12px sans-serif;max-width:300px;z-index:2147483602;box-shadow:0 10px 25px rgba(0,0,0,.25);
  display:none;animation:exp-slide-up .25s ease}
.exp-toast.show{display:block}
.exp-toast b{color:#fbbf24}
`;
  const style = document.createElement('style');
  style.textContent = css;
  document.head.appendChild(style);

  // ── Helpers ───────────────────────────────────────────────────────
  const el = (tag, attrs = {}, ...kids) => {
    const e = document.createElement(tag);
    for (const [k, v] of Object.entries(attrs)) {
      if (k === 'class') e.className = v;
      else if (k.startsWith('on')) e.addEventListener(k.substring(2), v);
      else e.setAttribute(k, v);
    }
    for (const k of kids) {
      if (k == null) continue;
      e.appendChild(typeof k === 'string' ? document.createTextNode(k) : k);
    }
    return e;
  };

  function getAnonId() {
    let id = localStorage.getItem('exp-anon-id');
    if (!id) {
      id = 'anon-' + Math.random().toString(36).slice(2, 12);
      localStorage.setItem('exp-anon-id', id);
    }
    return id;
  }

  // ── State ─────────────────────────────────────────────────────────
  const state = {
    sessionId: null,
    userId: null,
    isAnonymous: true,
    region: null,
    tenantConfig: null,
    eventBuffer: [],
    eventTimer: null,
    isOpen: false,
    lastInteractionAt: Date.now(),
    idleTimer: null,
    panel: null,
    msgsEl: null,
    inputEl: null,
    sendBtn: null,
    sphereEl: null,
    toastEl: null,
    overlayEl: null,
    overlayCtx: null,
    capture: { active: false, points: [] }
  };

  // ── API ───────────────────────────────────────────────────────────
  async function api(path, body, method = 'POST') {
    const res = await fetch(cfg.api + '/api/experion' + path, {
      method,
      headers: { 'Content-Type': 'application/json' },
      body: body ? JSON.stringify(body) : null
    });
    if (!res.ok) throw new Error('api ' + path + ' ' + res.status);
    return res.json();
  }

  async function identify() {
    const body = { tenantId: cfg.tenant };
    if (cfg.userId) body.userId = cfg.userId;
    else body.anonId = getAnonId();
    const r = await api('/identify', body);
    state.sessionId = r.sessionId;
    state.userId = r.resolvedUserId;
    state.isAnonymous = r.isAnonymous;
    state.region = r.regionCluster;
    return r;
  }

  async function loadConfig() {
    try {
      const r = await fetch(cfg.api + '/api/experion/config/' + encodeURIComponent(cfg.tenant)).then(x => x.json());
      state.tenantConfig = r;
    } catch (e) {
      state.tenantConfig = { greeting: "Hi! I'm Experion.", idleNudgeSeconds: 30, nudgeCooldownSeconds: 60 };
    }
  }

  // ── Activity tracker ──────────────────────────────────────────────
  function track(type, extras = {}) {
    state.lastInteractionAt = Date.now();
    state.eventBuffer.push({
      type,
      url: location.href,
      timestamp: new Date().toISOString(),
      ...extras
    });
    if (state.eventBuffer.length >= 10) flushEvents();
  }

  async function flushEvents() {
    if (!state.sessionId || state.eventBuffer.length === 0) return;
    const batch = state.eventBuffer.splice(0, state.eventBuffer.length);
    try {
      await api('/track', { tenantId: cfg.tenant, sessionId: state.sessionId, events: batch });
    } catch (e) { /* silently retry on next flush */ }
  }

  function startTracker() {
    track('pageview', { meta: { title: document.title } });
    document.addEventListener('click', e => {
      const t = e.target;
      const sel = t.id ? '#' + t.id : (t.className ? '.' + String(t.className).split(' ')[0] : t.tagName);
      track('click', { selector: sel, text: (t.innerText || '').slice(0, 80) });
    }, true);
    let lastScroll = 0;
    window.addEventListener('scroll', () => {
      const now = Date.now();
      if (now - lastScroll < 500) return;
      lastScroll = now;
      track('scroll', { meta: { y: window.scrollY } });
    }, { passive: true });
    document.addEventListener('input', e => {
      const t = e.target;
      if (t.tagName !== 'INPUT' && t.tagName !== 'TEXTAREA') return;
      track('input', { selector: t.name || t.id || t.tagName, meta: { len: (t.value || '').length } });
    }, true);
    setInterval(flushEvents, 5000);
    window.addEventListener('beforeunload', () => {
      if (!state.sessionId || state.eventBuffer.length === 0) return;
      navigator.sendBeacon(cfg.api + '/api/experion/track',
        new Blob([JSON.stringify({ tenantId: cfg.tenant, sessionId: state.sessionId, events: state.eventBuffer })],
          { type: 'application/json' }));
    });
  }

  // ── Idle trigger ──────────────────────────────────────────────────
  function startIdleTimer() {
    setInterval(() => {
      const idleSec = (Date.now() - state.lastInteractionAt) / 1000;
      if (idleSec >= (state.tenantConfig?.idleNudgeSeconds || 30)) {
        state.lastInteractionAt = Date.now(); // reset; server applies its own cooldown
        track('idle', { meta: { idleSec: Math.round(idleSec) } });
      }
    }, 5000);
  }

  // ── UI ────────────────────────────────────────────────────────────
  function buildUI() {
    state.sphereEl = el('div', { class: 'exp-sphere', title: 'Talk to Experion' },
      el('div', { class: 'exp-pulse' }), 'E');
    state.sphereEl.addEventListener('click', () => togglePanel(true));
    document.body.appendChild(state.sphereEl);

    state.panel = el('div', { class: 'exp-panel' });
    const header = el('div', {},
      el('div', { class: 'exp-mini' }),
      el('div', { class: 'exp-title' }, 'Experion'),
      el('button', { onclick: () => togglePanel(false), title: 'Close' }, '×'));
    header.style.cssText = 'padding:14px 16px;background:linear-gradient(135deg,#3b82f6,#8b5cf6);color:#fff;display:flex;align-items:center;gap:10px';
    state.panel.appendChild(header);

    state.msgsEl = el('div', { class: 'exp-msgs' });
    state.panel.appendChild(state.msgsEl);

    state.inputEl = el('input', { type: 'text', placeholder: 'Ask Experion anything…' });
    state.inputEl.addEventListener('keydown', e => { if (e.key === 'Enter') sendMessage(); });
    state.sendBtn = el('button', { onclick: () => sendMessage() }, 'Send');
    const inputBox = el('div', { class: 'exp-input' }, state.inputEl, state.sendBtn);
    state.panel.appendChild(inputBox);

    document.body.appendChild(state.panel);

    state.toastEl = el('div', { class: 'exp-toast' });
    document.body.appendChild(state.toastEl);

    state.overlayEl = el('canvas', { class: 'exp-overlay' });
    state.overlayEl.width = window.innerWidth;
    state.overlayEl.height = window.innerHeight;
    document.body.appendChild(state.overlayEl);
    state.overlayCtx = state.overlayEl.getContext('2d');
    window.addEventListener('resize', () => {
      state.overlayEl.width = window.innerWidth;
      state.overlayEl.height = window.innerHeight;
    });

    addMessage('ai', state.tenantConfig?.greeting || "Hi! I'm Experion.",
      ['What can you do?', 'Tell me about pricing', 'Open my profile']);
  }

  function togglePanel(open) {
    state.isOpen = open;
    state.panel.classList.toggle('open', open);
    if (open) state.inputEl.focus();
  }

  function addMessage(kind, text, suggestions, meta) {
    const m = el('div', { class: 'exp-msg ' + kind });
    m.textContent = text;
    if (meta) {
      const metaEl = el('div', { class: 'exp-meta' });
      metaEl.textContent = meta;
      m.appendChild(metaEl);
    }
    state.msgsEl.appendChild(m);
    if (suggestions && suggestions.length) {
      const chips = el('div', { class: 'exp-suggestions' });
      for (const s of suggestions) {
        const c = el('div', { class: 'exp-chip' }, s);
        c.addEventListener('click', () => { state.inputEl.value = s; sendMessage(); });
        chips.appendChild(c);
      }
      state.msgsEl.appendChild(chips);
    }
    state.msgsEl.scrollTop = state.msgsEl.scrollHeight;
    return m;
  }

  function appendPipelineToLast(pipeline, intent, action, cacheHit, ms) {
    if (!pipeline || pipeline.length === 0) return;
    const last = state.msgsEl.querySelector('.exp-msg.ai:last-of-type');
    if (!last) return;
    const box = el('div', { class: 'exp-pipeline' });
    const head = el('div', {},
      el('span', {}, `intent=${intent}${action ? ' · ' + action : ''}${cacheHit ? ' · cache HIT' : ''}`),
      el('span', {}, `${ms}ms`));
    box.appendChild(head);
    for (const s of pipeline) {
      const row = el('div', {},
        el('span', {}, s.name),
        el('span', {}, `${s.elapsedMs}ms`));
      box.appendChild(row);
      const detail = el('div', {});
      detail.style.cssText = 'color:#64748b;padding-left:6px;font-size:9px';
      detail.textContent = s.detail;
      box.appendChild(detail);
    }
    last.appendChild(box);
    state.msgsEl.scrollTop = state.msgsEl.scrollHeight;
  }

  async function sendMessage(prefilled, source = 'chat', capturedText) {
    const message = (prefilled !== undefined ? prefilled : state.inputEl.value).trim();
    if (!message && !capturedText) return;
    state.inputEl.value = '';
    state.sendBtn.disabled = true;

    if (message) addMessage('user', message);
    addMessage('system', '…thinking');

    try {
      const r = await api('/process', {
        sessionId: state.sessionId,
        tenantId: cfg.tenant,
        userMessage: message,
        capturedText: capturedText || null,
        source,
        pageUrl: location.href,
        pageTitle: document.title
      });
      const last = state.msgsEl.querySelector('.exp-msg.system:last-of-type');
      if (last) last.remove();
      addMessage('ai', r.message || '(empty response)', r.suggestions || []);
      appendPipelineToLast(r.pipeline, r.intentType, r.actionKey, r.cacheHit, r.processingMs);
      window.dispatchEvent(new CustomEvent('experion:response', { detail: r }));
    } catch (e) {
      const last = state.msgsEl.querySelector('.exp-msg.system:last-of-type');
      if (last) last.remove();
      addMessage('ai', '⚠ Error: ' + e.message);
    } finally {
      state.sendBtn.disabled = false;
    }
  }

  // ── Circle gesture (Alt + drag) ──────────────────────────────────
  function startGestureCapture() {
    document.addEventListener('mousedown', e => {
      if (!e.altKey) return;
      e.preventDefault();
      state.capture = { active: true, points: [{ x: e.clientX, y: e.clientY }] };
    }, true);
    document.addEventListener('mousemove', e => {
      if (!state.capture.active) return;
      state.capture.points.push({ x: e.clientX, y: e.clientY });
      drawGesture();
    }, true);
    document.addEventListener('mouseup', () => {
      if (!state.capture.active) return;
      state.capture.active = false;
      const pts = state.capture.points;
      state.overlayCtx.clearRect(0, 0, state.overlayEl.width, state.overlayEl.height);
      if (pts.length < 10) return;
      const region = bounds(pts);
      const captured = extractDom(region);
      if (!captured.text) return;
      track('gesture', { meta: { region, len: captured.text.length } });
      togglePanel(true);
      addMessage('user', '(circled an area on the page)');
      sendMessage('Tell me about this circled content', 'gesture', captured.text);
    }, true);
  }

  function drawGesture() {
    const ctx = state.overlayCtx, pts = state.capture.points;
    ctx.clearRect(0, 0, state.overlayEl.width, state.overlayEl.height);
    if (pts.length < 2) return;
    ctx.beginPath();
    ctx.moveTo(pts[0].x, pts[0].y);
    for (const p of pts) ctx.lineTo(p.x, p.y);
    ctx.strokeStyle = 'rgba(59,130,246,0.85)';
    ctx.lineWidth = 3;
    ctx.setLineDash([8, 4]);
    ctx.stroke();
  }

  function bounds(pts) {
    let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
    for (const p of pts) {
      if (p.x < minX) minX = p.x; if (p.x > maxX) maxX = p.x;
      if (p.y < minY) minY = p.y; if (p.y > maxY) maxY = p.y;
    }
    return { left: minX, top: minY, width: maxX - minX, height: maxY - minY };
  }

  function extractDom(r) {
    const all = document.querySelectorAll('p, h1, h2, h3, h4, h5, h6, li, span, div, td, a, button, input, label');
    const seen = new Set();
    const parts = [];
    for (const e of all) {
      if (e.closest('.exp-panel,.exp-sphere,.exp-toast,.exp-overlay')) continue;
      const b = e.getBoundingClientRect();
      if (b.right < r.left || b.left > r.left + r.width || b.bottom < r.top || b.top > r.top + r.height) continue;
      const text = (e.innerText || e.value || '').trim();
      if (!text || text.length < 3) continue;
      if (seen.has(text)) continue;
      seen.add(text);
      parts.push(`<${e.tagName.toLowerCase()}> ${text.slice(0, 200)}`);
      if (parts.length >= 30) break;
    }
    return { text: parts.join('\n').slice(0, 2000) };
  }

  // ── SignalR (loaded dynamically) ──────────────────────────────────
  function showToast(html) {
    state.toastEl.innerHTML = html;
    state.toastEl.classList.add('show');
    clearTimeout(showToast._t);
    showToast._t = setTimeout(() => state.toastEl.classList.remove('show'), 6000);
  }

  function loadSignalR() {
    return new Promise((resolve, reject) => {
      if (window.signalR) return resolve();
      const s = document.createElement('script');
      s.src = 'https://cdn.jsdelivr.net/npm/@microsoft/signalr@8.0.0/dist/browser/signalr.min.js';
      s.onload = resolve;
      s.onerror = reject;
      document.head.appendChild(s);
    });
  }

  async function connectHub() {
    try {
      await loadSignalR();
      const conn = new signalR.HubConnectionBuilder()
        .withUrl(cfg.api + '/hubs/experion?userId=' + encodeURIComponent(state.userId))
        .withAutomaticReconnect()
        .build();
      conn.on('nudge', payload => {
        showToast('<b>Experion suggests:</b><br/>' + (payload.message || '').slice(0, 200));
        if (state.isOpen) addMessage('nudge', payload.message, payload.suggestions);
        window.dispatchEvent(new CustomEvent('experion:nudge', { detail: payload }));
      });
      conn.on('action_executed', payload => {
        showToast('<b>Action executed:</b> ' + payload.actionKey);
        window.dispatchEvent(new CustomEvent('experion:action', { detail: payload }));
      });
      await conn.start();
    } catch (e) {
      console.warn('[Experion] SignalR connection failed', e);
    }
  }

  // ── Public API ────────────────────────────────────────────────────
  const Experion = {
    cfg, state,
    open: () => togglePanel(true),
    close: () => togglePanel(false),
    ask: (q) => { togglePanel(true); sendMessage(q); },
    track,
    flush: flushEvents,
    sessionId: () => state.sessionId,
    userId: () => state.userId
  };
  window.Experion = Experion;

  // ── Bootstrap ─────────────────────────────────────────────────────
  async function init() {
    await loadConfig();
    await identify();
    buildUI();
    startTracker();
    startIdleTimer();
    startGestureCapture();
    await connectHub();
    window.dispatchEvent(new CustomEvent('experion:ready', {
      detail: { userId: state.userId, sessionId: state.sessionId, isAnonymous: state.isAnonymous }
    }));
  }

  if (cfg.autoInit) {
    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
    else init();
  } else {
    Experion.init = init;
  }
})();
