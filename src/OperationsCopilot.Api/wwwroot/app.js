/*
  Test console for POST /api/chat.

  The point of this page is not the chat bubble; it is the trace. Which tools the agent chose,
  what it retrieved, what it cost and how long it took are all first-class, because those are the
  things you are actually inspecting when you run a question through an agent by hand.
*/

(() => {
  'use strict';

  /** Sample questions, tagged by which sources a good answer needs. */
  const SAMPLES = [
    { text: 'Which products are running low on stock?', kind: 'db' },
    { text: 'How did each category sell over the last 30 days?', kind: 'db' },
    { text: 'Tell me about PT-1001.', kind: 'db' },
    { text: 'Who has to approve a 20% discount?', kind: 'kb' },
    { text: 'How long is the warranty on safety equipment?', kind: 'kb' },
    { text: 'How is the reorder threshold calculated?', kind: 'kb' },
    { text: 'Which products need reordering, and how much should I order according to our policy?', kind: 'both' },
    { text: 'Are any low-stock items at the critical level our inventory policy defines?', kind: 'both' },
    { text: 'PT-1006 is discontinued — how should I price the remaining stock?', kind: 'both' },
  ];

  const KIND_TITLES = {
    db: 'Should call a database tool',
    kb: 'Should search the knowledge base',
    both: 'Needs both live data and written policy',
  };

  const THEME_KEY = 'operations-copilot.theme';

  const dom = {
    root: document.documentElement,
    transcript: document.getElementById('transcript'),
    transcriptInner: document.getElementById('transcript-inner'),
    intro: document.getElementById('intro'),
    samples: document.getElementById('samples'),
    composer: document.getElementById('composer'),
    message: document.getElementById('message'),
    send: document.getElementById('send'),
    reset: document.getElementById('reset'),
    metrics: document.getElementById('metrics'),
    tools: document.getElementById('tools'),
    citations: document.getElementById('citations'),
    status: document.getElementById('status'),
    statusText: document.getElementById('status-text'),
    conversation: document.getElementById('conversation'),
    conversationValue: document.getElementById('conversation-value'),
    themeToggle: document.getElementById('theme-toggle'),
    themeIcon: document.getElementById('theme-icon'),
  };

  let conversationId = null;
  let inFlight = false;

  // ── Small DOM helpers ────────────────────────────────────────────────────

  /** Creates an element; `text` is always set with textContent, never parsed as HTML. */
  function el(tag, className, text) {
    const node = document.createElement(tag);
    if (className) {
      node.className = className;
    }
    if (text !== undefined && text !== null) {
      node.textContent = String(text);
    }
    return node;
  }

  function clear(node) {
    node.replaceChildren();
  }

  function scrollToEnd() {
    dom.transcript.scrollTop = dom.transcript.scrollHeight;
  }

  // ── Theme ────────────────────────────────────────────────────────────────

  function applyTheme(theme) {
    dom.root.setAttribute('data-theme', theme);
    dom.themeIcon.textContent = theme === 'dark' ? '◑' : '◐';
    dom.themeToggle.title = theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme';
  }

  function initTheme() {
    let stored = null;
    try {
      stored = localStorage.getItem(THEME_KEY);
    } catch {
      // Private browsing or blocked storage: fall back to the system preference.
    }

    const prefersLight = window.matchMedia('(prefers-color-scheme: light)').matches;
    applyTheme(stored || (prefersLight ? 'light' : 'dark'));

    dom.themeToggle.addEventListener('click', () => {
      const next = dom.root.getAttribute('data-theme') === 'dark' ? 'light' : 'dark';
      applyTheme(next);
      try {
        localStorage.setItem(THEME_KEY, next);
      } catch {
        // Not persisting a theme preference is not worth surfacing to the user.
      }
    });
  }

  // ── Health ───────────────────────────────────────────────────────────────

  async function checkHealth() {
    try {
      const response = await fetch('health');
      const text = (await response.text()).trim();

      dom.status.dataset.state = response.ok ? 'healthy' : 'down';
      dom.statusText.textContent = response.ok ? text || 'healthy' : 'unhealthy';
    } catch {
      dom.status.dataset.state = 'down';
      dom.statusText.textContent = 'unreachable';
    }
  }

  // ── Samples ──────────────────────────────────────────────────────────────

  function renderSamples() {
    SAMPLES.forEach((sample) => {
      const button = el('button', 'sample', sample.text);
      button.type = 'button';
      button.dataset.kind = sample.kind;
      button.title = KIND_TITLES[sample.kind];

      button.addEventListener('click', () => {
        dom.message.value = sample.text;
        resizeComposer();
        dom.message.focus();
      });

      dom.samples.append(button);
    });
  }

  // ── Transcript ───────────────────────────────────────────────────────────

  function addMessage(role, build) {
    dom.intro?.remove();

    const article = el('article', `message message--${role}`);
    article.append(el('span', 'message__role', role === 'user' ? 'you' : role));

    const body = el('div', 'message__body');
    build(body);
    article.append(body);

    dom.transcriptInner.append(article);
    scrollToEnd();

    return article;
  }

  function addUserMessage(text) {
    addMessage('user', (body) => body.append(el('p', null, text)));
  }

  function addAgentMessage(answer) {
    addMessage('agent', (body) => {
      const prose = el('div', 'prose');
      // Safe: Markdown.render escapes the source before reintroducing known-safe structures.
      prose.innerHTML = window.Markdown.render(answer);
      body.append(prose);

      prose.querySelectorAll('.cite').forEach((marker) => {
        marker.addEventListener('click', () => highlightCitation(marker.dataset.citation));
      });
    });
  }

  function addErrorMessage(title, detail) {
    addMessage('error', (body) => {
      body.append(el('strong', null, title));
      if (detail) {
        body.append(el('pre', null, detail));
      }
    });
  }

  function showThinking() {
    dom.intro?.remove();

    const panel = el('div', 'thinking');
    const bars = el('div', 'thinking__bars');
    bars.setAttribute('aria-hidden', 'true');
    for (let i = 0; i < 4; i++) {
      bars.append(document.createElement('i'));
    }

    const timer = el('span', 'thinking__timer', '0.0s');
    panel.append(bars, el('span', null, 'The agent is choosing tools…'), timer);

    dom.transcriptInner.append(panel);
    scrollToEnd();

    // Turns can take a few seconds when several tools are called; show that it is progressing.
    const startedAt = performance.now();
    const handle = window.setInterval(() => {
      timer.textContent = `${((performance.now() - startedAt) / 1000).toFixed(1)}s`;
    }, 100);

    return () => {
      window.clearInterval(handle);
      panel.remove();
    };
  }

  // ── Inspector rail ───────────────────────────────────────────────────────

  function renderMetrics(response) {
    clear(dom.metrics);

    const grid = el('div', 'metrics');
    const tokens = response.usage ? response.usage.totalTokens : null;

    const entries = [
      { value: formatDuration(response.latencyMs), label: 'latency' },
      { value: String(response.toolCalls.length), label: 'tools' },
      { value: tokens === null ? '—' : formatCount(tokens), label: 'tokens' },
    ];

    entries.forEach((entry) => {
      const metric = el('div', 'metric');
      const value = el('span', 'metric__value');

      if (typeof entry.value === 'object') {
        value.append(document.createTextNode(entry.value.number), el('small', null, entry.value.unit));
      } else {
        value.textContent = entry.value;
      }

      metric.append(value, el('span', 'metric__label', entry.label));
      grid.append(metric);
    });

    dom.metrics.append(grid);
  }

  function renderTools(toolCalls) {
    clear(dom.tools);

    if (toolCalls.length === 0) {
      dom.tools.append(el('p', 'rail-empty', 'The agent answered without calling a tool.'));
      return;
    }

    toolCalls.forEach((call) => {
      const card = el('div', `tool${call.succeeded ? '' : ' tool--failed'}`);

      const name = el('div', 'tool__name');
      name.append(
        el('span', 'tool__plugin', `${call.pluginName}.`),
        el('span', 'tool__function', call.functionName),
        el('span', 'tool__duration', `${call.durationMs} ms`),
      );
      card.append(name);

      const args = Object.entries(call.arguments || {});
      if (args.length > 0) {
        const list = el('div', 'tool__args');
        args.forEach(([key, value]) => {
          const chip = el('span', 'arg');
          chip.append(el('b', null, `${key}: `), document.createTextNode(value ?? 'null'));
          list.append(chip);
        });
        card.append(list);
      }

      if (call.error) {
        card.append(el('p', 'tool__error', call.error));
      }

      dom.tools.append(card);
    });
  }

  function renderCitations(citations) {
    clear(dom.citations);

    if (citations.length === 0) {
      dom.citations.append(
        el('p', 'rail-empty', 'No passages retrieved — this answer came from live data only.'),
      );
      return;
    }

    citations.forEach((citation) => {
      const reference = citation.reference.replace(/\D/g, '');
      const card = el('div', 'citation');
      card.id = `citation-${reference}`;

      const head = el('div', 'citation__head');
      head.append(
        el('span', 'citation__ref', citation.reference),
        el('span', 'citation__heading', citation.heading),
      );

      card.append(head, el('span', 'citation__source', citation.sourceFile));

      const score = el('div', 'score');
      const track = el('div', 'score__track');
      const fill = el('span', 'score__fill');
      fill.style.width = `${Math.round(Math.min(1, Math.max(0, citation.score)) * 100)}%`;
      track.append(fill);
      score.append(track, el('span', 'score__value', citation.score.toFixed(3)));

      card.append(score, el('p', 'citation__excerpt', citation.excerpt));
      dom.citations.append(card);
    });
  }

  function highlightCitation(reference) {
    const card = document.getElementById(`citation-${reference}`);
    if (!card) {
      return;
    }

    document.querySelectorAll('.citation.is-highlighted').forEach((node) => {
      node.classList.remove('is-highlighted');
    });

    card.classList.add('is-highlighted');
    card.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
  }

  // ── Formatting ───────────────────────────────────────────────────────────

  function formatDuration(milliseconds) {
    return milliseconds < 1000
      ? { number: String(milliseconds), unit: 'ms' }
      : { number: (milliseconds / 1000).toFixed(2), unit: 's' };
  }

  function formatCount(value) {
    return value >= 1000 ? `${(value / 1000).toFixed(1)}k` : String(value);
  }

  /** Turns a failed response into something worth reading, whichever shape it arrived in. */
  async function describeFailure(response) {
    let payload = null;
    try {
      payload = await response.json();
    } catch {
      return { title: `Request failed (HTTP ${response.status})`, detail: null };
    }

    if (payload.errors) {
      const detail = Object.entries(payload.errors)
        .map(([field, messages]) => `${field}: ${[].concat(messages).join(' ')}`)
        .join('\n');
      return { title: payload.title || 'Validation failed', detail };
    }

    return {
      title: payload.title || `Request failed (HTTP ${response.status})`,
      detail: payload.detail || null,
    };
  }

  // ── Sending ──────────────────────────────────────────────────────────────

  async function send(message) {
    if (inFlight || message.trim() === '') {
      return;
    }

    inFlight = true;
    dom.send.disabled = true;
    addUserMessage(message);

    const stopThinking = showThinking();

    try {
      const response = await fetch('api/chat', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ message, conversationId }),
      });

      stopThinking();

      if (!response.ok) {
        const failure = await describeFailure(response);
        addErrorMessage(failure.title, failure.detail);
        return;
      }

      const payload = await response.json();

      conversationId = payload.conversationId;
      dom.conversation.hidden = false;
      dom.conversationValue.textContent = conversationId.slice(0, 8);

      addAgentMessage(payload.answer);
      renderMetrics(payload);
      renderTools(payload.toolCalls);
      renderCitations(payload.citations);
    } catch (error) {
      stopThinking();
      addErrorMessage('Could not reach the API.', String(error));
    } finally {
      inFlight = false;
      dom.send.disabled = false;
      dom.message.focus();
    }
  }

  // ── Composer ─────────────────────────────────────────────────────────────

  function resizeComposer() {
    dom.message.style.height = 'auto';
    dom.message.style.height = `${dom.message.scrollHeight}px`;
  }

  function initComposer() {
    dom.message.addEventListener('input', resizeComposer);

    dom.message.addEventListener('keydown', (event) => {
      if (event.key === 'Enter' && !event.shiftKey) {
        event.preventDefault();
        dom.composer.requestSubmit();
      }
    });

    dom.composer.addEventListener('submit', (event) => {
      event.preventDefault();

      const message = dom.message.value.trim();
      if (message === '') {
        return;
      }

      dom.message.value = '';
      resizeComposer();
      send(message);
    });

    dom.reset.addEventListener('click', () => {
      conversationId = null;
      dom.conversation.hidden = true;
      clear(dom.transcriptInner);
      clear(dom.metrics);

      dom.metrics.append(el('p', 'rail-empty', 'No request yet.'));
      renderTools([]);
      renderCitations([]);
      dom.message.focus();
    });
  }

  // ── Start ────────────────────────────────────────────────────────────────

  initTheme();
  renderSamples();
  initComposer();
  checkHealth();
})();
