/* ─────────────────────────────────────────────────────────────────────
   AI Guide — frontend logic
   ───────────────────────────────────────────────────────────────────── */
(function () {
    'use strict';

    const API = {
        ask:     '/api/ask',
        profile: '/api/profile',
        reset:   '/api/reset',
        health:  '/api/health'
    };

    const PERSONAS = {
        sage:        { label: 'The Sage',        defaultBg: 'dawn'   },
        philosopher: { label: 'The Philosopher', defaultBg: 'night'  },
        healer:      { label: 'The Healer',      defaultBg: 'dusk'   },
        elder:       { label: 'The Elder',       defaultBg: 'dawn'   },
        storyteller: { label: 'The Storyteller', defaultBg: 'lake'   }
    };

    /** Themed inline SVG used as the central portrait when no PNG is present. */
    const PERSONA_SVG = {
        sage: `<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 200 200'>
            <defs><radialGradient id='sk' cx='50%' cy='38%' r='50%'>
                <stop offset='0%' stop-color='%23f8e3b0'/><stop offset='100%' stop-color='%237a5a2e'/>
            </radialGradient></defs>
            <circle cx='100' cy='80' r='52' fill='url(%23sk)'/>
            <ellipse cx='100' cy='90' rx='60' ry='12' fill='%23ffffff' opacity='.5'/>
            <path d='M50 130 Q100 115 150 130 L155 200 L45 200 Z' fill='%23866a3a'/>
            <path d='M75 70 Q100 35 125 70' fill='%23ffffff' opacity='.85'/>
            <ellipse cx='90' cy='80' rx='3' ry='4' fill='%23222'/><ellipse cx='110' cy='80' rx='3' ry='4' fill='%23222'/>
            <path d='M85 100 Q100 110 115 100 Q100 116 85 100Z' fill='%23ffffff' opacity='.85'/>
        </svg>`,
        philosopher: `<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 200 200'>
            <circle cx='100' cy='80' r='52' fill='%23dec8a4'/>
            <path d='M48 60 Q100 8 152 60 Q132 70 100 70 Q68 70 48 60Z' fill='%23222'/>
            <ellipse cx='90' cy='85' rx='3' ry='4' fill='%23000'/><ellipse cx='110' cy='85' rx='3' ry='4' fill='%23000'/>
            <path d='M85 105 Q100 113 115 105' stroke='%23000' stroke-width='2' fill='none'/>
            <path d='M50 130 Q100 115 150 130 L155 200 L45 200 Z' fill='%23222950'/>
        </svg>`,
        healer: `<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 200 200'>
            <circle cx='100' cy='80' r='52' fill='%23d99a78'/>
            <path d='M40 65 Q100 25 160 65 L160 80 Q100 45 40 80Z' fill='%23ff8aa8'/>
            <ellipse cx='90' cy='85' rx='3' ry='4' fill='%23300'/><ellipse cx='110' cy='85' rx='3' ry='4' fill='%23300'/>
            <path d='M85 105 Q100 115 115 105' stroke='%23300' stroke-width='2' fill='none'/>
            <path d='M50 130 Q100 115 150 130 L155 200 L45 200 Z' fill='%23703528'/>
        </svg>`,
        elder: `<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 200 200'>
            <circle cx='100' cy='80' r='52' fill='%23d99a6a'/>
            <path d='M30 50 Q100 8 170 50 L160 60 Q100 30 40 60Z' fill='%23a83a26'/>
            <path d='M50 35 Q100 -10 150 35 L150 50 Q100 20 50 50Z' fill='%23f6c453'/>
            <ellipse cx='90' cy='85' rx='3' ry='4' fill='%23200'/><ellipse cx='110' cy='85' rx='3' ry='4' fill='%23200'/>
            <path d='M85 105 Q100 115 115 105' stroke='%23200' stroke-width='2' fill='none'/>
            <path d='M50 130 Q100 115 150 130 L155 200 L45 200 Z' fill='%23a83a26'/>
        </svg>`,
        storyteller: `<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 200 200'>
            <circle cx='100' cy='80' r='52' fill='%23d6b48c'/>
            <path d='M30 30 Q100 -20 170 30 L165 70 Q100 35 35 70Z' fill='%232b6ea3'/>
            <ellipse cx='90' cy='85' rx='3' ry='4' fill='%23001'/><ellipse cx='110' cy='85' rx='3' ry='4' fill='%23001'/>
            <path d='M85 105 Q100 113 115 105' stroke='%23001' stroke-width='2' fill='none'/>
            <path d='M50 130 Q100 115 150 130 L155 200 L45 200 Z' fill='%231e4c73'/>
        </svg>`
    };

    const els = {
        topbar:        document.querySelector('.topbar'),
        // avatar
        heroPortrait:  document.getElementById('heroPortrait'),
        hero:          document.getElementById('hero'),
        speechBubble:  document.getElementById('speechBubble'),
        speechText:    document.getElementById('speechText'),
        heroStatus:    document.getElementById('heroStatus'),
        waveCanvas:    document.getElementById('waveCanvas'),
        // composer
        composer:      document.getElementById('composer'),
        messageInput:  document.getElementById('messageInput'),
        micBtn:        document.getElementById('micBtn'),
        sendBtn:       document.getElementById('sendBtn'),
        // selectors
        avatarCards:   document.querySelectorAll('.avatar-card'),
        mindsetCards:  document.querySelectorAll('.mindset-card'),
        bgThumbs:      document.querySelectorAll('.bg-thumb'),
        // settings
        voiceSelect:   document.getElementById('voiceSelect'),
        speedRange:    document.getElementById('speedRange'),
        speedValue:    document.getElementById('speedValue'),
        userName:      document.getElementById('userName'),
        // memory
        memName:       document.getElementById('memName'),
        memTurns:      document.getElementById('memTurns'),
        resetBtn:      document.getElementById('resetBtn'),
        // chips
        chips:         document.querySelectorAll('.chip[data-prompt]'),
        // theme + lang
        themeToggle:   document.getElementById('themeToggle')
    };

    const state = {
        avatar: 'sage',
        mindset: 'balanced',
        bg: 'dawn',
        voice: 'warm',
        speed: 1.0,
        speaking: false,
        listening: false,
        wakeListening: false,
        recogniser: null,
        wakeRecogniser: null,
        synth: window.speechSynthesis,
        availableVoices: [],
        animFrame: null,
        analyser: null,
        audioCtx: null,
        sourceNode: null,
        currentUtter: null,
        amplitude: 0
    };

    // ── Health check ────────────────────────────────────────────────
    async function health() {
        try {
            const r = await fetch(API.health);
            return r.ok;
        } catch { return false; }
    }

    // ── Initial portrait + persona ──────────────────────────────────
    function setHeroPortrait(avatar) {
        const url = `/img/avatars/${avatar}.png`;
        // Try the PNG; if missing, fall back to inline SVG.
        const img = new Image();
        img.onload  = () => { els.heroPortrait.style.setProperty('--portrait', `url('${url}')`); };
        img.onerror = () => {
            const svg = PERSONA_SVG[avatar] || PERSONA_SVG.sage;
            const dataUri = `url("data:image/svg+xml;utf8,${svg.replace(/\n/g, '').replace(/"/g, '\'')}")`;
            els.heroPortrait.style.setProperty('--portrait', dataUri);
        };
        img.src = url;
    }
    function setBackground(bg) {
        state.bg = bg;
        els.hero.dataset.bg = bg;
        els.bgThumbs.forEach(b => b.classList.toggle('active', b.dataset.bg === bg));
    }

    // ── Load profile from server ───────────────────────────────────
    async function loadProfile() {
        try {
            const r = await fetch(API.profile);
            const p = await r.json();
            if (p.avatar)  selectAvatar(p.avatar, false);
            if (p.mindset) selectMindset(p.mindset, false);
            if (p.name)    els.userName.value = p.name;
            updateMemory(p);
        } catch { /* ignore */ }
    }

    function updateMemory(p) {
        els.memName.textContent  = p.name ? p.name : 'no name';
        els.memTurns.textContent = (p.historyLength ?? 0) + ' turns';
    }

    // ── Selection ──────────────────────────────────────────────────
    function selectAvatar(key, sync = true) {
        if (!PERSONAS[key]) return;
        state.avatar = key;
        els.avatarCards.forEach(c => c.classList.toggle('active', c.dataset.avatar === key));
        setHeroPortrait(key);
        const bg = PERSONAS[key].defaultBg;
        if (bg) setBackground(bg);
        if (sync) syncProfile();
    }
    function selectMindset(key, sync = true) {
        state.mindset = key;
        els.mindsetCards.forEach(c => c.classList.toggle('active', c.dataset.mindset === key));
        if (sync) syncProfile();
    }

    async function syncProfile() {
        try {
            await fetch(API.profile, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    avatar:  state.avatar,
                    mindset: state.mindset,
                    name:    els.userName.value || undefined
                })
            });
        } catch { /* ignore */ }
    }

    // ── Speech bubble + transcript ─────────────────────────────────
    function setBubble(text) { els.speechText.innerHTML = escapeHtml(text); }
    function setStatus(text, dotted = false) {
        els.heroStatus.innerHTML = escapeHtml(text) +
            (dotted ? '<span class="dots"><i></i><i></i><i></i></span>' : '');
    }
    function escapeHtml(s) {
        return String(s)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/\n/g, '<br>');
    }

    // ── Ask backend ────────────────────────────────────────────────
    async function ask(message) {
        if (!message || !message.trim()) return;
        els.sendBtn.disabled = true;
        els.messageInput.value = '';
        setBubble(message);
        setStatus('Thinking', true);

        try {
            const res = await fetch(API.ask, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    message,
                    avatar:  state.avatar,
                    mindset: state.mindset,
                    name:    els.userName.value || undefined
                })
            });
            const data = await res.json();
            const reply = data.reply || '(silence)';
            setBubble(reply);
            updateMemory({ name: data.name, historyLength: data.historyLength });
            setStatus(data.success ? 'Spoken' : 'Reply (Ollama unavailable)');
            await speak(reply);
            setStatus(continuousMode ? 'AI is listening' : 'Ready', continuousMode);
            if (continuousMode) startMicListener();
        } catch (err) {
            setBubble('I could not reach my mind right now. Make sure the server is running.');
            setStatus('Error');
        } finally {
            els.sendBtn.disabled = false;
        }
    }

    // ── Speech synthesis (TTS) ─────────────────────────────────────
    function pickVoice() {
        const voices = state.availableVoices;
        if (!voices || !voices.length) return null;
        const en = voices.filter(v => v.lang && v.lang.toLowerCase().startsWith('en'));
        const pool = en.length ? en : voices;
        const want = state.voice;
        const byName = {
            warm:   /samantha|jenny|google.*female|natural|aria|warm/i,
            bright: /victoria|google.*us|crisp|bright/i,
            deep:   /daniel|alex|google.*male|deep|grandpa/i
        }[want] || /./;
        return pool.find(v => byName.test(v.name)) || pool[0];
    }

    function speak(text) {
        return new Promise((resolve) => {
            if (!('speechSynthesis' in window) || !text) return resolve();
            try { state.synth.cancel(); } catch {}
            const u = new SpeechSynthesisUtterance(text);
            const v = pickVoice();
            if (v) u.voice = v;
            u.rate = state.speed;
            u.pitch = ({ warm: 1.05, bright: 1.15, deep: 0.85 })[state.voice] || 1;
            u.onstart = () => { state.speaking = true; els.hero.classList.add('speaking'); startWaveform(); };
            u.onend   = () => { state.speaking = false; els.hero.classList.remove('speaking'); stopWaveform(); resolve(); };
            u.onerror = () => { state.speaking = false; els.hero.classList.remove('speaking'); stopWaveform(); resolve(); };
            state.currentUtter = u;
            state.synth.speak(u);
        });
    }

    function loadVoices() {
        if (!('speechSynthesis' in window)) return;
        state.availableVoices = state.synth.getVoices();
        if (state.availableVoices.length === 0) {
            state.synth.onvoiceschanged = () => { state.availableVoices = state.synth.getVoices(); };
        }
    }

    // ── Waveform animation (synthetic) ─────────────────────────────
    function startWaveform() {
        const c = els.waveCanvas;
        const ctx = c.getContext('2d');
        const dpr = window.devicePixelRatio || 1;
        function resize() { c.width = c.clientWidth * dpr; c.height = c.clientHeight * dpr; }
        resize(); window.addEventListener('resize', resize);

        let t = 0;
        function draw() {
            ctx.clearRect(0, 0, c.width, c.height);
            const w = c.width, h = c.height;
            const baseAmp = state.speaking ? 0.55 : 0.10;
            // gradient
            const g = ctx.createLinearGradient(0, 0, w, 0);
            g.addColorStop(0,   '#8b6cf6');
            g.addColorStop(0.5, '#f6c453');
            g.addColorStop(1,   '#ff8aa8');
            ctx.strokeStyle = g;
            ctx.lineWidth = 2 * dpr;
            ctx.shadowBlur = 16 * dpr; ctx.shadowColor = 'rgba(246,196,83,.35)';
            ctx.beginPath();
            for (let x = 0; x < w; x += 2) {
                const phase = (x / w) * Math.PI * 4 + t;
                const wobble = Math.sin(phase) * Math.sin(phase / 3 + t * 0.7);
                const y = h / 2 + wobble * (h / 2) * baseAmp;
                if (x === 0) ctx.moveTo(x, y); else ctx.lineTo(x, y);
            }
            ctx.stroke();
            t += state.speaking ? 0.15 : 0.06;
            state.animFrame = requestAnimationFrame(draw);
        }
        cancelAnimationFrame(state.animFrame);
        state.animFrame = requestAnimationFrame(draw);
    }
    function stopWaveform() {
        // keep the idle ribbon animating gently — don't fully stop
        if (!state.animFrame) startWaveform();
    }

    // ── Speech recognition (mic + wake word) ───────────────────────
    let continuousMode = false;
    let wakeWordEnabled = true; // Always on by default like the reference

    const SR = window.SpeechRecognition || window.webkitSpeechRecognition;
    function makeRecogniser(continuous = false) {
        if (!SR) return null;
        const r = new SR();
        r.lang = 'en-US';
        r.interimResults = false;
        r.continuous = continuous;
        return r;
    }

    function startMicListener() {
        if (!SR) return alert('Sorry — your browser does not support speech recognition. Try Chrome or Edge.');
        if (state.listening) return;
        const r = makeRecogniser(false);
        state.recogniser = r;
        state.listening = true;
        els.micBtn.classList.add('listening');
        setStatus('Listening', true);
        r.onresult = (e) => {
            const text = e.results[0][0].transcript;
            stopMicListener();
            ask(text);
        };
        r.onerror = () => { stopMicListener(); };
        r.onend   = () => { stopMicListener(); };
        try { r.start(); } catch { stopMicListener(); }
    }
    function stopMicListener() {
        state.listening = false;
        els.micBtn.classList.remove('listening');
        if (state.recogniser) { try { state.recogniser.stop(); } catch {} state.recogniser = null; }
    }

    function startWakeWord() {
        if (!SR || !wakeWordEnabled) return;
        if (state.wakeRecogniser) return;
        const r = makeRecogniser(true);
        state.wakeRecogniser = r;
        r.interimResults = true;
        r.onresult = (e) => {
            for (let i = e.resultIndex; i < e.results.length; i++) {
                const t = e.results[i][0].transcript.toLowerCase();
                if (/\bhey\s*(baba|guide)\b/.test(t)) {
                    try { r.stop(); } catch {}
                    setStatus('Yes?', false);
                    setTimeout(() => startMicListener(), 250);
                    return;
                }
            }
        };
        r.onend = () => {
            state.wakeRecogniser = null;
            // Restart unless the user disabled wake word
            if (wakeWordEnabled && !state.listening && !state.speaking) {
                setTimeout(() => startWakeWord(), 600);
            }
        };
        r.onerror = () => {
            state.wakeRecogniser = null;
            if (wakeWordEnabled) setTimeout(() => startWakeWord(), 1200);
        };
        try { r.start(); } catch { state.wakeRecogniser = null; }
    }

    // ── Wire up events ─────────────────────────────────────────────
    function wire() {
        // Avatar selection
        els.avatarCards.forEach(c => c.addEventListener('click', () => selectAvatar(c.dataset.avatar)));
        // Mindset selection
        els.mindsetCards.forEach(c => c.addEventListener('click', () => selectMindset(c.dataset.mindset)));
        // Background
        els.bgThumbs.forEach(b => b.addEventListener('click', () => setBackground(b.dataset.bg)));
        // Voice settings
        els.voiceSelect.addEventListener('change', () => state.voice = els.voiceSelect.value);
        els.speedRange.addEventListener('input', () => {
            state.speed = parseFloat(els.speedRange.value);
            els.speedValue.textContent = state.speed.toFixed(1) + '×';
        });
        // Name
        els.userName.addEventListener('change', () => syncProfile());
        // Composer
        els.composer.addEventListener('submit', (e) => { e.preventDefault(); ask(els.messageInput.value); });
        els.micBtn.addEventListener('click', () => state.listening ? stopMicListener() : startMicListener());
        // Reset memory
        els.resetBtn.addEventListener('click', async () => {
            await fetch(API.reset, { method: 'POST' });
            els.userName.value = '';
            updateMemory({ historyLength: 0 });
            setBubble('Memory cleared. Let\'s begin again.');
        });
        // Chips
        els.chips.forEach(c => c.addEventListener('click', () => {
            els.messageInput.value = c.dataset.prompt;
            ask(c.dataset.prompt);
        }));
        // Theme toggle (visual only — page is dark by default)
        if (els.themeToggle) {
            els.themeToggle.addEventListener('change', () => {
                document.body.classList.toggle('light', els.themeToggle.checked);
            });
        }
    }

    // ── Boot ───────────────────────────────────────────────────────
    async function boot() {
        wire();
        loadVoices();
        startWaveform();
        await loadProfile();
        selectAvatar(state.avatar, false);
        setBackground(state.bg);
        setStatus('AI is listening', true);
        // a tiny pause then start wake-word listener (gives the page time to settle)
        setTimeout(() => startWakeWord(), 600);
        // health
        const ok = await health();
        if (!ok) setStatus('API offline', false);
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', boot);
    else boot();
})();
