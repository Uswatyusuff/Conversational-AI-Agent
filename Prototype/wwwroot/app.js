/* ═══════════════════════════════════════════════════════════════
   Bradford Council AI Agent — Advanced app.js
   ═══════════════════════════════════════════════════════════════ */

"use strict";

// ── DOM REFS ─────────────────────────────────────────────────────
const chat              = document.getElementById("chat");
const statusLine        = document.getElementById("statusLine");
const sessionPill       = document.getElementById("sessionPill");
const sessionTimeEl     = document.getElementById("sessionTime");
const msgCountEl        = document.getElementById("msgCount");
const sendBtn           = document.getElementById("sendBtn");
const msgInput          = document.getElementById("msg");
const charCountEl       = document.getElementById("charCount");
const resetBtn          = document.getElementById("resetBtn");
const newChatBtn        = document.getElementById("newChatBtn");
const appShell          = document.getElementById("appShell");
const toggleSidebarBtn  = document.getElementById("toggleSidebarBtn");
// Feedback modal
const feedbackModalOverlay = document.getElementById("feedbackModalOverlay");
const feedbackModal        = document.getElementById("feedbackModal");
const feedbackModalClose   = document.getElementById("feedbackModalClose");
const feedbackCancelBtn    = document.getElementById("feedbackCancelBtn");
const feedbackSubmitBtn    = document.getElementById("feedbackSubmitBtn");
const feedbackComment      = document.getElementById("feedbackComment");

// Settings
const settingsBtn       = document.getElementById("settingsBtn");
const settingsOverlay   = document.getElementById("settingsOverlay");
const settingsClose     = document.getElementById("settingsClose");
const fontSizeSlider    = document.getElementById("fontSizeSlider");
const fontSizeValue     = document.getElementById("fontSizeValue");
const animToggle        = document.getElementById("animToggle");
const typingToggle      = document.getElementById("typingToggle");
const timestampToggle   = document.getElementById("timestampToggle");

// Search
const searchToggleBtn   = document.getElementById("searchToggleBtn");
const searchWrap        = document.getElementById("searchWrap");
const searchInput       = document.getElementById("searchInput");
const searchResultsBar  = document.getElementById("searchResultsBar");
const searchResultsText = document.getElementById("searchResultsText");
const searchPrev        = document.getElementById("searchPrev");
const searchNext        = document.getElementById("searchNext");
const searchClear       = document.getElementById("searchClear");

// Scroll FAB
const scrollFab         = document.getElementById("scrollFab");

// Toast
const toastContainer    = document.getElementById("toastContainer");

// Voice
const micBtn            = document.getElementById("micBtn");
const voiceToggleBtn    = document.getElementById("voiceToggleBtn");

// ── STATE ─────────────────────────────────────────────────────────
let lastService              = "Unknown";
let pendingSkeletonId        = null;
let typingIndicatorId        = null;
let pendingPostcode          = null;
let awaitingAddressSelection = false;
let lastAddresses            = [];
let isMobile                 = window.innerWidth <= 768;
let messageCount             = 0;
let sessionStart             = new Date();
let pendingFeedbackHelpful = null;
let lastFocusedBeforeModal = null;

// Search state
let searchMatches    = [];
let searchIndex      = 0;

// Voice state
let voiceOutputEnabled = false;
let isRecording        = false;
let isTranscribing     = false;
let mediaRecorder      = null;
let mediaStream        = null;
let audioChunks        = [];
let currentAudio       = null;
let recordTimeoutId    = null;

// Prefs (persisted in localStorage)
const PREFS_KEY = "bca_prefs";
const defaultPrefs = {
  theme:      "dark",
  accent:     "blue",
  layout:     "default",
  bubble:     "default",
  fontSize:   14,
  animations: true,
  typing:     true,
  timestamps: true
};
let prefs = { ...defaultPrefs };
let lastFocusedBeforeSettings = null;

// ── INIT ──────────────────────────────────────────────────────────
function init() {
  loadPrefs();
  applyAllPrefs();
  initSession();
  initEventListeners();
  addWelcomeMessage();
  updateMsgCount();
  setInterval(updateSessionTime, 10000);
  updateSessionTime();
}

// ── PREFS ─────────────────────────────────────────────────────────
function loadPrefs() {
  try {
    const stored = localStorage.getItem(PREFS_KEY);
    if (stored) prefs = { ...defaultPrefs, ...JSON.parse(stored) };
  } catch {}
}

function savePrefs() {
  localStorage.setItem(PREFS_KEY, JSON.stringify(prefs));
}

function applyAllPrefs() {
  applyTheme(prefs.theme);
  applyAccent(prefs.accent);
  applyLayout(prefs.layout);
  applyBubbleStyle(prefs.bubble);
  applyFontSize(prefs.fontSize);
  applyAnimations(prefs.animations);
  syncSettingsPanelUI();
}

function applyTheme(theme) {
  prefs.theme = theme;
  document.documentElement.setAttribute("data-theme", theme);
  savePrefs();
}

function applyAccent(accent) {
  prefs.accent = accent;
  document.documentElement.setAttribute("data-accent", accent);
  savePrefs();
}

function applyLayout(layout) {
  prefs.layout = layout;
  document.documentElement.setAttribute("data-layout", layout);
  // Sync sidebar hidden state for centered layout
  if (layout === "centered") {
    appShell.classList.add("sidebar-hidden");
  } else if (!isMobile && appShell.classList.contains("sidebar-hidden") && layout !== "centered") {
    appShell.classList.remove("sidebar-hidden");
  }
  savePrefs();
}

function applyBubbleStyle(bubble) {
  prefs.bubble = bubble;
  document.documentElement.setAttribute("data-bubble", bubble);
  savePrefs();
}

function applyFontSize(size) {
  prefs.fontSize = size;
  document.documentElement.style.fontSize = size + "px";
  if (fontSizeSlider) fontSizeSlider.value = size;
  if (fontSizeValue) fontSizeValue.textContent = size + "px";
  savePrefs();
}

function applyAnimations(enabled) {
  prefs.animations = enabled;
  document.documentElement.setAttribute("data-animations", enabled ? "on" : "off");
  savePrefs();
}

function syncSettingsPanelUI() {
  document.querySelectorAll("[data-theme-pick]").forEach(btn => {
    btn.classList.toggle("active", btn.dataset.themePick === prefs.theme);
  });

  document.querySelectorAll(".accent-option").forEach(btn => {
    btn.classList.toggle("active", btn.dataset.accent === prefs.accent);
  });

  document.querySelectorAll(".layout-option").forEach(btn => {
    btn.classList.toggle("active", btn.dataset.layout === prefs.layout);
  });

  document.querySelectorAll(".bubble-style-btn").forEach(btn => {
    btn.classList.toggle("active", btn.dataset.bubble === prefs.bubble);
  });

  if (animToggle) animToggle.checked = prefs.animations;
  if (typingToggle) typingToggle.checked = prefs.typing;
  if (timestampToggle) timestampToggle.checked = prefs.timestamps;
  if (fontSizeSlider) fontSizeSlider.value = prefs.fontSize;
  if (fontSizeValue) fontSizeValue.textContent = prefs.fontSize + "px";
}
// ── SESSION ────────────────────────────────────────────────────────
let sessionId = localStorage.getItem("sessionId");
if (!sessionId) {
  sessionId = crypto.randomUUID();
  localStorage.setItem("sessionId", sessionId);
}

function initSession() {
  sessionPill.textContent = sessionId.slice(0, 8) + "…";
  updateSessionTime();
}

function updateSessionTime() {
  if (!sessionTimeEl) return;
  const diff = Math.floor((Date.now() - sessionStart) / 60000);
  sessionTimeEl.textContent = diff < 1 ? "Just now" : diff + "m ago";
}

function updateMsgCount() {
  if (msgCountEl) msgCountEl.textContent = String(messageCount);
}

// ── SETTINGS PANEL ────────────────────────────────────────────────


function openSettings() {
  lastFocusedBeforeSettings = document.activeElement;
  settingsOverlay.classList.add("open");
  settingsOverlay.setAttribute("aria-hidden", "false");
  syncSettingsPanelUI();
  document.body.classList.add("modal-open");
  // Inert the sidebar and main panel so Tab cannot escape the settings dialog
  document.querySelector(".sidebar")?.setAttribute("inert", "");
  document.querySelector(".main-panel")?.setAttribute("inert", "");
  const panel = document.getElementById("settingsPanel");
  const firstFocusable = getFocusableElements(panel)[0];
  if (firstFocusable) firstFocusable.focus();
}

function closeSettings() {
  settingsOverlay.classList.remove("open");
  settingsOverlay.setAttribute("aria-hidden", "true");
  document.body.classList.remove("modal-open");
  document.querySelector(".sidebar")?.removeAttribute("inert");
  document.querySelector(".main-panel")?.removeAttribute("inert");
  if (lastFocusedBeforeSettings) lastFocusedBeforeSettings.focus();
}
// ── TOAST ─────────────────────────────────────────────────────────
function showToast(message, duration = 2600) {
  const toast = document.createElement("div");
  toast.className = "toast";
  toast.textContent = message;
  toastContainer.appendChild(toast);
  setTimeout(() => {
    toast.classList.add("fade-out");
    toast.addEventListener("animationend", () => toast.remove());
  }, duration);
}

// ── UTILS ─────────────────────────────────────────────────────────
function escapeHtml(str) {
  return String(str)
    .replaceAll("&", "&amp;").replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;").replaceAll('"', "&quot;").replaceAll("'", "&#039;");
}

function maskSensitiveText(input) {
  if (!input) return "";
  let o = String(input);
  o = o.replace(/[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}/gi, "[EMAIL]");
  o = o.replace(/\b(?:\+44|0)\d[\d\s]{8,}\b/gi, "[PHONE]");
  o = o.replace(/\b[A-Z]{1,2}\d[A-Z\d]?\s?\d[A-Z]{2}\b/gi, "[POSTCODE]");
  o = o.replace(/\b(my name is|i am|i'm)\s+[a-z][a-z\s'-]*/gi, "$1 [NAME]");
  return o;
}

function formatTime(date = new Date()) {
  return date.toLocaleTimeString("en-GB", { hour: "2-digit", minute: "2-digit" });
}

function isUsefulAddress(text) {
  if (!text) return false;
  return !["close", "view our privacy notice", "privacy notice"].includes(text.trim().toLowerCase());
}

function updateStatus(text, busy = false) {
  statusLine.textContent = text;
  const dot = document.getElementById("statusDot");
  if (dot) dot.style.background = busy ? "var(--warning, #f59e0b)" : "var(--online)";
}

// ── MESSAGE RENDERING ─────────────────────────────────────────────
function addMessage(html, who, id = null) {
  const div = document.createElement("div");
  div.className = "msg " + (who === "you" ? "you" : "bot");
  if (id) div.dataset.id = id;

  // Label
  const label = document.createElement("div");
  label.className = "msg-label";
  label.textContent = who === "you" ? "You" : "Bradford Council AI";
  div.appendChild(label);

  const bubble = document.createElement("div");
  bubble.className = "bubble";
  bubble.innerHTML = html;
  div.appendChild(bubble);

  // Meta row (time + copy)
  const metaRow = document.createElement("div");
  metaRow.className = "msg-meta-row";

  const time = document.createElement("span");
  time.className = "msg-time" + (prefs.timestamps ? "" : " hidden");
  time.textContent = formatTime();
  metaRow.appendChild(time);

  if (who === "bot") {
    const copyBtn = document.createElement("button");
    copyBtn.className = "copy-btn";
    copyBtn.title = "Copy message";
    copyBtn.innerHTML = `<svg width="11" height="11" viewBox="0 0 12 12" fill="none"><rect x="4.5" y="4.5" width="6" height="6" rx="1" stroke="currentColor" stroke-width="1.2"/><path d="M2 7.5H1.5A.5.5 0 0 1 1 7V1.5A.5.5 0 0 1 1.5 1H7a.5.5 0 0 1 .5.5V2" stroke="currentColor" stroke-width="1.2"/></svg> Copy`;
    copyBtn.addEventListener("click", () => {
      const text = bubble.innerText;
      navigator.clipboard.writeText(text).then(() => {
        copyBtn.innerHTML = `<svg width="11" height="11" viewBox="0 0 12 12" fill="none"><path d="M2 6l3 3 5-5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/></svg> Copied!`;
        setTimeout(() => {
          copyBtn.innerHTML = `<svg width="11" height="11" viewBox="0 0 12 12" fill="none"><rect x="4.5" y="4.5" width="6" height="6" rx="1" stroke="currentColor" stroke-width="1.2"/><path d="M2 7.5H1.5A.5.5 0 0 1 1 7V1.5A.5.5 0 0 1 1.5 1H7a.5.5 0 0 1 .5.5V2" stroke="currentColor" stroke-width="1.2"/></svg> Copy`;
        }, 2000);
      });
    });
    metaRow.appendChild(copyBtn);
  }

  div.appendChild(metaRow);
  chat.appendChild(div);
  chat.scrollTop = chat.scrollHeight;

  messageCount++;
  updateMsgCount();
  updateScrollFab();

  return div;
}

function updateTimestampVisibility() {
  document.querySelectorAll(".msg-time").forEach(el => {
    el.classList.toggle("hidden", !prefs.timestamps);
  });
}

// ── TYPING INDICATOR ──────────────────────────────────────────────
function showTypingIndicator() {
  if (!prefs.typing) return;
  const id = "typing_" + crypto.randomUUID();
  typingIndicatorId = id;

  const div = document.createElement("div");
  div.className = "msg bot typing-indicator";
  div.dataset.id = id;

  const bubble = document.createElement("div");
  bubble.className = "bubble";
  bubble.innerHTML = `<div class="typing-dot"></div><div class="typing-dot"></div><div class="typing-dot"></div>`;
  div.appendChild(bubble);
  chat.appendChild(div);
  chat.scrollTop = chat.scrollHeight;
  return id;
}

function removeTypingIndicator() {
  if (!typingIndicatorId) return;
  const node = chat.querySelector(`[data-id="${typingIndicatorId}"]`);
  if (node) {
    node.style.animation = "msgIn 0.15s var(--ease) reverse both";
    setTimeout(() => node.remove(), 150);
  }
  typingIndicatorId = null;
}

// ── SKELETON ──────────────────────────────────────────────────────
function showSkeleton() {
  removeTypingIndicator();
  showTypingIndicator();

  const id = "sk_" + crypto.randomUUID();
  pendingSkeletonId = id;

  // We'll replace the typing indicator when bot replies
  setTimeout(() => {
    removeTypingIndicator();

    const div = document.createElement("div");
    div.className = "msg bot";
    div.dataset.id = id;

    const label = document.createElement("div");
    label.className = "msg-label";
    label.textContent = "Bradford Council AI";
    div.appendChild(label);

    const bubble = document.createElement("div");
    bubble.className = "bubble";
    bubble.innerHTML = `<div class="skeleton-wrap" aria-label="Loading response">
      <div class="skeleton-line s1"></div>
      <div class="skeleton-line s2"></div>
      <div class="skeleton-line s3"></div>
    </div>`;
    div.appendChild(bubble);

    const metaRow = document.createElement("div");
    metaRow.className = "msg-meta-row";
    const time = document.createElement("span");
    time.className = "msg-time" + (prefs.timestamps ? "" : " hidden");
    time.textContent = formatTime();
    metaRow.appendChild(time);
    div.appendChild(metaRow);

    chat.appendChild(div);
    chat.scrollTop = chat.scrollHeight;
    updateScrollFab();
  }, prefs.typing ? 1200 : 0);

  updateStatus("Analysing…", true);
  sendBtn.disabled = true;
}

function _getSkeletonNode() {
  return [...document.querySelectorAll(".msg.bot")].find(x => x.dataset.id === pendingSkeletonId);
}

function replaceSkeletonWithHtml(html) {
  const node = _getSkeletonNode();
  pendingSkeletonId = null;
  if (!node) { addMessage(html, "bot"); }
  else { node.querySelector(".bubble").innerHTML = html; }
  updateStatus("Ready to help");
  sendBtn.disabled = false;
  chat.scrollTop = chat.scrollHeight;
}

// ── BOT REPLY BUILDER ─────────────────────────────────────────────
/**
 * Converts a subset of Markdown to safe HTML for chat bubbles.
 * Handles: **bold**, *italic*, `code`, line-breaks.
 * Input is plain text (not pre-escaped), output is HTML.
 */
function markdownToHtml(text) {
  if (!text) return "";
  // Escape HTML entities first to prevent XSS
  let s = text
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;");
  // Bold: **text** or __text__
  s = s.replace(/\*\*(.+?)\*\*/g, "<strong>$1</strong>");
  s = s.replace(/__(.+?)__/g, "<strong>$1</strong>");
  // Italic: *text* or _text_ (single, not adjacent to word char on outside)
  s = s.replace(/\*([^*\n]+?)\*/g, "<em>$1</em>");
  s = s.replace(/_([^_\n]+?)_/g, "<em>$1</em>");
  // Inline code: `code`
  s = s.replace(/`([^`\n]+?)`/g, "<code>$1</code>");
  // Line breaks: convert \n to <br>
  s = s.replace(/\n/g, "<br>");
  return s;
}

function buildBotHtml(data) {
  lastService = data.service || "Unknown";
  const safeService = escapeHtml(lastService);
  const replyHtml   = markdownToHtml(data.reply || "");

  let next = "";
  if (data.nextStepsUrl) {
    const safeUrl = escapeHtml(data.nextStepsUrl);
    next = `<div class="card">
      <strong style="font-size:13px">Next steps</strong>
      <div style="margin-top:6px">
        <a href="${safeUrl}" target="_blank" rel="noopener noreferrer" style="font-size:13px;word-break:break-all">${safeUrl}</a>
      </div>
    </div>`;
  }

    const feedback = `<div class="card" style="margin-top:10px">
    <strong style="font-size:13px">Was this helpful?</strong>
    <div class="chips" style="margin-top:8px">
      <button class="chip" onclick="feedback('Yes')" aria-label="Submit positive feedback for this response">
        <svg width="11" height="11" viewBox="0 0 12 12" fill="none"><path d="M2 6l3 3 5-5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/></svg> Yes
      </button>
      <button class="chip" onclick="feedback('No')" aria-label="Submit negative feedback for this response and add a comment">
        <svg width="11" height="11" viewBox="0 0 12 12" fill="none"><path d="M3 3l6 6M9 3l-6 6" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/></svg> No
      </button>
    </div>
  </div>`;
  const chips = renderSuggestionChips(data.suggestions || []);

  return `<span class="tag">${safeService}</span>
    <div style="margin-top:8px">${replyHtml}</div>
    ${next}${feedback}${chips}`;
}

function replaceSkeletonWithBotReply(data) {
  const node = _getSkeletonNode();
  pendingSkeletonId = null;
  const html = buildBotHtml(data);
  if (!node) { addMessage(html, "bot"); }
  else { node.querySelector(".bubble").innerHTML = html; }
  updateStatus("Ready to help");
  sendBtn.disabled = false;
  chat.scrollTop = chat.scrollHeight;
  // Speak the reply if voice output is enabled.
  // Internal tool signals (POSTCODE_LOOKUP::, LOCATION_LOOKUP::) are stripped inside speakText.
  speakText(data.reply || "");
}

// ── SUGGESTION CHIPS ──────────────────────────────────────────────
function renderSuggestionChips(suggestions) {
  if (!Array.isArray(suggestions) || !suggestions.length) return "";
  const chips = suggestions.map(s => {
    const safe = escapeHtml(String(s));
    const enc  = encodeURIComponent(String(s));
    return `<button class="chip" onclick="sendSuggestion('${enc}')" aria-label="${safe}">${safe}</button>`;
  }).join("");
  return `<div class="chips">${chips}</div>`;
}
window.sendSuggestion = (enc) => sendPreset(decodeURIComponent(enc));

// ── POSTCODE LOOKUP ───────────────────────────────────────────────
async function searchPostcode(postcode) {
  showSkeleton();
  try {
    const res  = await fetch(`/api/postcode/search?postcode=${encodeURIComponent(postcode)}&sessionId=${encodeURIComponent(sessionId)}`);
    const data = await res.json();
    if (!res.ok || data.error) {
      replaceSkeletonWithBotReply({ service: "Waste & Bins", reply: `<b>Error:</b> ${escapeHtml(data.error || "Could not look up addresses.")}`, suggestions: ["Try another postcode"] });
      return;
    }
    const addresses = (data.addresses || []).filter(isUsefulAddress);
    lastAddresses = addresses; pendingPostcode = postcode; awaitingAddressSelection = true; lastService = "Waste & Bins";
    if (!addresses.length) {
      replaceSkeletonWithBotReply({ service: "Waste & Bins", reply: "No addresses found for that postcode.", suggestions: ["Try another postcode"] });
      return;
    }
    const btns = addresses.map((a, i) => `<button class="chip" onclick="selectAddress(${i})">${escapeHtml(a)}</button>`).join("");
    replaceSkeletonWithHtml(`<span class="tag">Waste &amp; Bins</span>
      <div style="margin-top:8px">Addresses for <b>${escapeHtml(postcode)}</b> — please select yours:</div>
      <div class="card"><div class="chips">${btns}</div></div>`);
  } catch {
    replaceSkeletonWithBotReply({ service: "Waste & Bins", reply: "<b>Error:</b> Could not reach the postcode service.", suggestions: ["Try again"] });
  }
}

window.selectAddress = async function(index) {
  if (!awaitingAddressSelection || index < 0 || index >= lastAddresses.length) return;
  const address = lastAddresses[index];
  addMessage("[Address selected]", "you");
  showSkeleton();
  try {
    const res = await fetch(`/api/postcode/bin-result?postcode=${encodeURIComponent(pendingPostcode)}&address=${encodeURIComponent(address)}&sessionId=${encodeURIComponent(sessionId)}`);
    const data = await res.json();
    if (!res.ok) {
      replaceSkeletonWithBotReply({ service: "Waste & Bins", reply: "<b>Error:</b> Could not retrieve collection result.", suggestions: ["Try again"] });
      return;
    }
    awaitingAddressSelection = false;
    const resultText = data.result || "No collection information found.";
    replaceSkeletonWithHtml(`<span class="tag">Waste &amp; Bins</span>
      <div style="margin-top:8px"><b>Address:</b> ${escapeHtml(address)}</div>
      <div class="card" style="white-space:pre-wrap;margin-top:10px">${escapeHtml(resultText)}</div>
      <div class="chips" style="margin-top:10px">
        <button class="chip" onclick="sendPreset('Use same address')">Same address</button>
        <button class="chip" onclick="sendPreset('Use different address')">Different address</button>
        <button class="chip" onclick="sendPreset('Report a missed bin')">Report missed bin</button>
      </div>`);
  } catch {
    replaceSkeletonWithBotReply({ service: "Waste & Bins", reply: "<b>Error:</b> Could not reach the bin result service.", suggestions: ["Try again"] });
  }
};

// --- SEARCH LOCATION --------

async function searchLocation(postcode, type) {
  showSkeleton();

  try {
    const res = await fetch(`/api/location/nearby?postcode=${encodeURIComponent(postcode)}&type=${encodeURIComponent(type)}`);
    const data = await res.json();

    if (!res.ok || data.error) {
      replaceSkeletonWithBotReply({
        service: "Location",
        reply: `<b>Error:</b> ${escapeHtml(data.error || "Could not look up nearby services.")}`,
        suggestions: ["Try another postcode", "Find nearest library", "Find council office"]
      });
      return;
    }

    let html = `<span class="tag">Location</span>
      <div style="margin-top:8px"><b>Results for:</b> ${escapeHtml(postcode)}</div>`;

    function renderSection(title, items) {
      if (!Array.isArray(items) || !items.length) return "";

      return `
        <div class="card" style="margin-top:10px">
          <strong>${escapeHtml(title)}</strong>
          <div style="margin-top:10px; display:flex; flex-direction:column; gap:10px;">
            ${items.map(item => `
              <div class="card" style="margin-top:0">
                <div><strong>${escapeHtml(item.name || item.Name || "")}</strong></div>
                <div style="margin-top:4px">📍 ${escapeHtml(item.address || item.Address || "")}</div>
                ${(item.phone || item.Phone) ? `<div>📞 ${escapeHtml(item.phone || item.Phone)}</div>` : ""}
                ${(item.openingHours || item.OpeningHours) ? `<div>🕒 ${escapeHtml(item.openingHours || item.OpeningHours)}</div>` : ""}
                ${(item.estimatedDistanceMiles ?? item.EstimatedDistanceMiles) != null ? `<div>📏 ~${escapeHtml(String(item.estimatedDistanceMiles ?? item.EstimatedDistanceMiles))} miles</div>` : ""}
                ${(item.notes || item.Notes) ? `<div>ℹ️ ${escapeHtml(item.notes || item.Notes)}</div>` : ""}
                ${(item.mapUrl || item.MapUrl) ? `<div style="margin-top:6px"><a href="${escapeHtml(item.mapUrl || item.MapUrl)}" target="_blank" rel="noopener noreferrer">Open in map</a></div>` : ""}
              </div>
            `).join("")}
          </div>
        </div>
      `;
    }

    html += renderSection("Council Offices", data.councilOffices || data.CouncilOffices);
    html += renderSection("Libraries", data.libraries || data.Libraries);
    html += renderSection("Recycling Centres", data.recyclingCentres || data.RecyclingCentres);

    if (
      !(data.councilOffices || data.CouncilOffices)?.length &&
      !(data.libraries || data.Libraries)?.length &&
      !(data.recyclingCentres || data.RecyclingCentres)?.length
    ) {
      html += `<div class="card" style="margin-top:10px">No nearby services found.</div>`;
    }

    html += `
      <div class="chips" style="margin-top:10px">
        <button class="chip" onclick="sendPreset('Find nearest library')">Find nearest library</button>
        <button class="chip" onclick="sendPreset('Find council office')">Find council office</button>
        <button class="chip" onclick="sendPreset('Find recycling centre')">Find recycling centre</button>
        <button class="chip" onclick="sendPreset('Find nearby schools')">Find nearby schools</button>
      </div>
    `;

    replaceSkeletonWithHtml(html);
  } catch {
    replaceSkeletonWithBotReply({
      service: "Location",
      reply: "<b>Error:</b> Could not reach the location service.",
      suggestions: ["Try again", "Find nearest library", "Find council office"]
    });
  }
}


// ── VOICE PIPELINE ────────────────────────────────────────────────
// Internal tool signals that should never be spoken aloud.
const _VOICE_STRIP = [
  /POSTCODE_LOOKUP::[^\s]*/g,
  /LOCATION_LOOKUP::([^:\s]*::?[^\s]*)?/g,
];

function stripVoiceSignals(raw) {
  let text = raw || "";
  for (const pat of _VOICE_STRIP) text = text.replace(pat, "");
  // Strip HTML tags and collapse whitespace.
  text = text.replace(/<[^>]+>/g, " ").replace(/\s+/g, " ").trim();
  return text;
}

function getSupportedMimeType() {
  if (typeof MediaRecorder === "undefined") return "";
  const types = [
    "audio/webm;codecs=opus",
    "audio/webm",
    "audio/ogg;codecs=opus",
    "audio/mp4",
  ];
  return types.find(t => MediaRecorder.isTypeSupported(t)) || "";
}

// ── resetMicButton ─────────────────────────────────────────────────
// Single source of truth for returning the mic button to idle state.
// Called from every error path, onstop early-return, and transcription finally.
function resetMicButton() {
  isRecording    = false;
  isTranscribing = false;
  micBtn.disabled = false;
  micBtn.classList.remove("recording", "transcribing");
  micBtn.setAttribute("aria-label", "Record voice input");
  micBtn.setAttribute("aria-pressed", "false");
}

// ── cleanupRecordingState ──────────────────────────────────────────
// Releases the MediaStream and resets all recording state variables.
// Does NOT touch isTranscribing — call resetMicButton() for full UI reset.
function cleanupRecordingState() {
  if (recordTimeoutId) {
    clearTimeout(recordTimeoutId);
    recordTimeoutId = null;
  }
  if (mediaStream) {
    mediaStream.getTracks().forEach(t => t.stop());
    mediaStream = null;
  }
  mediaRecorder = null;
  audioChunks   = [];
  isRecording   = false;
}

async function startRecording() {
  // Bug-fix: guard against re-entry while getUserMedia is in flight or transcribing.
  if (isRecording || isTranscribing) return;

  // Disable immediately to prevent a double-click race before getUserMedia resolves.
  micBtn.disabled = true;

  // ── Upfront environment checks ─────────────────────────────────
  // getUserMedia requires a secure context (HTTPS or localhost).
  if (!window.isSecureContext) {
    showToast("Microphone requires HTTPS or localhost — cannot record on plain HTTP.");
    console.warn("[Voice] Not a secure context — getUserMedia will be blocked.");
    resetMicButton();
    return;
  }

  // MediaRecorder must exist (not present in some older/embedded browsers).
  if (typeof MediaRecorder === "undefined") {
    showToast("Voice recording is not supported in this browser. Please use Chrome or Edge.");
    console.warn("[Voice] MediaRecorder API not available.");
    resetMicButton();
    return;
  }

  // getUserMedia must be available (may be absent even on HTTPS in some contexts).
  if (!navigator.mediaDevices?.getUserMedia) {
    showToast("Microphone API not available in this browser.");
    console.warn("[Voice] navigator.mediaDevices.getUserMedia not found.");
    resetMicButton();
    return;
  }

  try {
    // Bug-fix: was `currentStream` (undeclared) — must be `mediaStream` (declared in state).
    mediaStream = await navigator.mediaDevices.getUserMedia({ audio: true });
    audioChunks = [];

    const mimeType = getSupportedMimeType();
    const recorder = mimeType
      ? new MediaRecorder(mediaStream, { mimeType })
      : new MediaRecorder(mediaStream);

    mediaRecorder = recorder;

    recorder.ondataavailable = (e) => {
      if (e.data && e.data.size > 0) audioChunks.push(e.data);
    };

    recorder.onstop = async () => {
      const finalMime   = recorder.mimeType || "audio/webm";
      const finalChunks = [...audioChunks];

      // Release stream and recorder resources.
      cleanupRecordingState();

      // Bug-fix: early returns previously left micBtn.disabled = true (set by stopRecording).
      // resetMicButton() re-enables it in all paths, then transcribeAndSend re-applies
      // the transcribing state if needed.

      if (!finalChunks.length) {
        resetMicButton();
        updateStatus("Ready to help");
        showToast("No audio recorded — please try again.");
        return;
      }

      const blob = new Blob(finalChunks, { type: finalMime });
      if (!blob.size) {
        resetMicButton();
        updateStatus("Ready to help");
        showToast("Recording was empty — please try again.");
        return;
      }

      await transcribeAndSend(blob, finalMime);
    };

    recorder.onerror = (evt) => {
      // Bug-fix: was `currentStream` (undeclared) — now handled by cleanupRecordingState.
      console.error("[Voice] MediaRecorder error:", evt?.error);
      cleanupRecordingState();
      resetMicButton();
      updateStatus("Ready to help");
      showToast("Recording failed — please try again.");
    };

    recorder.start();
    isRecording = true;

    micBtn.classList.remove("transcribing");
    micBtn.classList.add("recording");
    micBtn.disabled = false;    // re-enable now that recording has started
    micBtn.setAttribute("aria-label", "Stop recording");
    micBtn.setAttribute("aria-pressed", "true");
    updateStatus("Listening…", true);

  } catch (err) {
    // Bug-fix: was only distinguishing NotAllowedError.
    // Now maps every known DOMException name to a user-readable message,
    // and always logs the real error to the console for debugging.
    console.error("[Voice] getUserMedia error:", err?.name, err?.message);

    let msg;
    switch (err?.name) {
      case "NotAllowedError":
      case "PermissionDeniedError":
        msg = "Microphone access denied. Click the 🔒 icon in your browser address bar and allow microphone, then try again.";
        break;
      case "NotFoundError":
      case "DevicesNotFoundError":
        msg = "No microphone found. Please connect a microphone and try again.";
        break;
      case "NotReadableError":
      case "TrackStartError":
        msg = "Microphone is in use by another application. Please close it and try again.";
        break;
      case "OverconstrainedError":
        msg = "Microphone does not meet requirements. Try a different device.";
        break;
      case "SecurityError":
        msg = "Microphone blocked for security reasons. Please use HTTPS or localhost.";
        break;
      case "AbortError":
        msg = "Microphone request was cancelled.";
        break;
      default:
        msg = `Could not start microphone (${err?.name || "unknown error"}). Check the browser console for details.`;
    }

    showToast(msg);
    updateStatus("Ready to help");
    // Bug-fix: missing micBtn reset in catch — button stayed disabled.
    resetMicButton();
  }
}

function stopRecording() {
  if (!mediaRecorder || !isRecording) return;
  if (mediaRecorder.state !== "recording") return;

  // Transition to transcribing state before stopping so the user sees
  // immediate visual feedback while onstop fires asynchronously.
  micBtn.classList.remove("recording");
  micBtn.classList.add("transcribing");
  micBtn.disabled = true;
  micBtn.setAttribute("aria-label", "Transcribing voice input");
  micBtn.setAttribute("aria-pressed", "false");
  updateStatus("Transcribing…", true);

  mediaRecorder.stop();
}

async function transcribeAndSend(blob, mimeType) {
  // Guard: should never be called twice, but protect defensively.
  if (isTranscribing) return;

  isTranscribing = true;
  micBtn.disabled = true;
  micBtn.classList.remove("recording");
  micBtn.classList.add("transcribing");

  try {
    const form = new FormData();
    const ext = mimeType.includes("ogg") ? ".ogg"
              : mimeType.includes("mp4") ? ".mp4"
              : ".webm";
    form.append("audio", blob, "recording" + ext);

    const res = await fetch("/api/voice/transcribe", { method: "POST", body: form });

    if (!res.ok) {
      // Bug-fix: was always "Transcription failed." — now gives actionable detail.
      let msg = "Transcription failed.";
      if (res.status === 503) {
        msg = "Voice service not configured — the OpenAI API key may be missing. See setup guide.";
      } else if (res.status === 401) {
        msg = "Voice service authentication failed — check your OpenAI API key.";
      } else if (res.status === 429) {
        msg = "OpenAI rate limit reached — please wait a moment and try again.";
      } else if (res.status >= 500) {
        msg = `Transcription server error (${res.status}) — please try again.`;
      }
      console.error("[Voice] /api/voice/transcribe returned", res.status);
      showToast(msg);
      updateStatus("Ready to help");
      return;
    }

    const data       = await res.json();
    const transcript = (data.transcript || "").trim();

    if (!transcript) {
      showToast("No speech detected — please speak clearly and try again.");
      updateStatus("Ready to help");
      return;
    }

    msgInput.value = transcript;
    updateStatus("Thinking…", true);
    send();   // hands off to the existing text chat pipeline unchanged

  } catch (err) {
    console.error("[Voice] transcribeAndSend fetch error:", err);
    showToast("Could not reach transcription service. Check your network connection.");
    updateStatus("Ready to help");
  } finally {
    // Always restore the mic button regardless of success, failure, or empty result.
    resetMicButton();
  }
}
async function speakText(rawText) {
  if (!voiceOutputEnabled) return;
  const text = stripVoiceSignals(rawText);
  if (!text) return;

  // Stop any currently playing audio before starting a new one.
  if (currentAudio) {
    currentAudio.pause();
    currentAudio = null;
  }

  try {
    updateStatus("Speaking…", true);
    const res = await fetch("/api/voice/speak", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ text })
    });
    if (!res.ok) { updateStatus("Ready to help"); return; }

    const blob = await res.blob();
    const url  = URL.createObjectURL(blob);
    currentAudio = new Audio(url);
    currentAudio.onended = () => {
      URL.revokeObjectURL(url);
      currentAudio = null;
      updateStatus("Ready to help");
    };
    currentAudio.onerror = () => {
      updateStatus("Ready to help");
    };
    await currentAudio.play();
  } catch {
    updateStatus("Ready to help");
  }
}

function toggleVoiceOutput() {
  voiceOutputEnabled = !voiceOutputEnabled;
  voiceToggleBtn.setAttribute("aria-pressed", voiceOutputEnabled ? "true" : "false");
  voiceToggleBtn.setAttribute("aria-label", voiceOutputEnabled ? "Disable voice output" : "Enable voice output");
  voiceToggleBtn.classList.toggle("active", voiceOutputEnabled);
  showToast(voiceOutputEnabled ? "Voice output on" : "Voice output off");

  // If turned off mid-playback, stop audio.
  if (!voiceOutputEnabled && currentAudio) {
    currentAudio.pause();
    currentAudio = null;
    updateStatus("Ready to help");
  }
}

// ── SEND MESSAGE ──────────────────────────────────────────────────
async function send() {
  const message = msgInput.value.trim();
  if (!message || sendBtn.disabled) return;

  addMessage(escapeHtml(maskSensitiveText(message)), "you");
  msgInput.value = "";
  charCountEl.classList.remove("visible", "warn");

  if (isMobile) appShell.classList.remove("sidebar-visible");

  showSkeleton();

  try {
    const res  = await fetch("/api/chat", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ message, sessionId })
    });
    const data = await res.json();

    if (!res.ok) {
      replaceSkeletonWithBotReply({ service: "Error", reply: `<b>Error:</b> ${escapeHtml(data.reply || "Request failed")}`, suggestions: [] });
      return;
    }

    if (typeof data.reply === "string" && data.reply.startsWith("POSTCODE_LOOKUP::")) {
      const postcode = data.reply.replace("POSTCODE_LOOKUP::", "").trim();
      const node = _getSkeletonNode();
      if (node) node.remove();
      pendingSkeletonId = null;
      sendBtn.disabled = false;
      updateStatus("Looking up address…", true);
      await searchPostcode(postcode);
      return;
    }
    if (typeof data.reply === "string" && data.reply.startsWith("LOCATION_LOOKUP::")) {
  const parts = data.reply.split("::");
  const postcode = parts[1] || "";
  const type = parts[2] || "all";

  const node = _getSkeletonNode();
  if (node) node.remove();
  pendingSkeletonId = null;
  sendBtn.disabled = false;
  updateStatus("Looking up nearby services…", true);

  await searchLocation(postcode, type);
  return;
}
    replaceSkeletonWithBotReply(data);
  } catch {
    replaceSkeletonWithBotReply({ service: "Error", reply: "<b>Error:</b> Could not reach the server.", suggestions: [] });
  }
}

function sendPreset(text) { msgInput.value = text; send(); }
window.sendPreset = sendPreset;

// ── FEEDBACK ─────────────────────────────────────────────────────
function getFocusableElements(container) {
  return [...container.querySelectorAll(
    'button:not([disabled]), [href], input:not([disabled]), textarea:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])'
  )].filter(el => el.offsetParent !== null);
}

function trapFocus(container, event) {
  const focusable = getFocusableElements(container);
  if (!focusable.length) return;

  const first = focusable[0];
  const last = focusable[focusable.length - 1];

  if (event.key === "Tab") {
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }
}

function openFeedbackModal(helpful) {
  pendingFeedbackHelpful = helpful;
  lastFocusedBeforeModal = document.activeElement;
  feedbackComment.value = "";
  feedbackModalOverlay.classList.add("open");
  feedbackModalOverlay.setAttribute("aria-hidden", "false");
  document.body.classList.add("modal-open");
  // Inert the whole app shell so Tab cannot reach content behind the modal
  document.getElementById("appShell")?.setAttribute("inert", "");
  setTimeout(() => feedbackComment.focus(), 30);
}

function closeFeedbackModal() {
  feedbackModalOverlay.classList.remove("open");
  feedbackModalOverlay.setAttribute("aria-hidden", "true");
  document.body.classList.remove("modal-open");
  document.getElementById("appShell")?.removeAttribute("inert");
  pendingFeedbackHelpful = null;
  feedbackComment.value = "";
  if (lastFocusedBeforeModal) lastFocusedBeforeModal.focus();
}

async function submitFeedback(helpful, comment = "") {
  try {
    await fetch("/api/feedback", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        service: lastService,
        helpful,
        comment,
        sessionId
      })
    });

    showToast(
      helpful === "Yes"
        ? "Thanks for the positive feedback! ✓"
        : "Thanks — we'll use this to improve."
    );
  } catch {
    showToast("Couldn't save feedback right now.");
  }
}

window.feedback = function(helpful) {
  if (helpful === "Yes") {
    submitFeedback("Yes", "");
    return;
  }
  openFeedbackModal("No");
};

// ── RESET ─────────────────────────────────────────────────────────
function resetChat() {
  // Generate a fresh sessionId so the new conversation has no server-side memory
  // from the previous chat. The old sessionId is simply abandoned — it will expire
  // naturally via the 30-minute TTL in ConversationMemory.
  sessionId = crypto.randomUUID();
  localStorage.setItem("sessionId", sessionId);
  if (sessionPill) sessionPill.textContent = sessionId.slice(0, 8) + "…";

  chat.innerHTML = "";
  pendingPostcode = null; awaitingAddressSelection = false; lastAddresses = [];
  lastService = "Unknown"; pendingSkeletonId = null; typingIndicatorId = null;
  sendBtn.disabled = false; messageCount = 0;
  updateMsgCount(); updateStatus("Ready to help");
  sessionStart = new Date(); updateSessionTime();
  clearSearch();
  addWelcomeMessage();
  showToast("New conversation started.");
}

// ── WELCOME MESSAGE ───────────────────────────────────────────────
function addWelcomeMessage() {
  addMessage(
    `<span class="tag">Bradford Council</span>
     <div style="margin-top:9px;line-height:1.65">
       Hello! I can help with Bradford Council services — Council Tax, bins, benefits, school admissions, planning, and libraries.
     </div>
     <div class="chips" style="margin-top:11px">
       <button class="chip" onclick="sendPreset('How do I check my Council Tax balance?')">Council Tax</button>
       <button class="chip" onclick="sendPreset('When is my bin collection day?')">Bin day</button>
       <button class="chip" onclick="sendPreset('How do I apply for Council Tax Support?')">Benefits</button>
       <button class="chip" onclick="sendPreset('How do I report a missed bin?')">Report issue</button>
     </div>`,
    "bot"
  );
}

// ── SEARCH ────────────────────────────────────────────────────────
function openSearch() {
  searchWrap.classList.add("open");
  searchInput.focus();
  searchToggleBtn.classList.add("active");
}
function closeSearch() {
  searchWrap.classList.remove("open");
  searchInput.value = "";
  clearSearch();
  searchToggleBtn.classList.remove("active");
}
function clearSearch() {
  searchMatches = []; searchIndex = 0;
  searchResultsBar.hidden = true;
  document.querySelectorAll(".highlight-match").forEach(el => {
    const parent = el.parentNode;
    parent.replaceChild(document.createTextNode(el.textContent), el);
    parent.normalize();
  });
}

function runSearch(query) {
  clearSearch();
  if (!query.trim()) return;

  const q = query.toLowerCase();
  const bubbles = [...document.querySelectorAll(".msg .bubble")];
  const matches = [];

  bubbles.forEach(bubble => {
    const walker = document.createTreeWalker(bubble, NodeFilter.SHOW_TEXT, null);
    let node;
    while ((node = walker.nextNode())) {
      const text = node.textContent;
      const lower = text.toLowerCase();
      let idx = lower.indexOf(q);
      if (idx !== -1) {
        // Highlight all occurrences in this text node
        const frag = document.createDocumentFragment();
        let last = 0;
        while (idx !== -1) {
          frag.appendChild(document.createTextNode(text.slice(last, idx)));
          const mark = document.createElement("mark");
          mark.className = "highlight-match";
          mark.textContent = text.slice(idx, idx + q.length);
          frag.appendChild(mark);
          matches.push(mark);
          last = idx + q.length;
          idx = lower.indexOf(q, last);
        }
        frag.appendChild(document.createTextNode(text.slice(last)));
        node.parentNode.replaceChild(frag, node);
      }
    }
  });

  searchMatches = matches;
  if (matches.length) {
    searchIndex = 0;
    highlightCurrent();
    searchResultsBar.hidden = false;
    searchResultsText.textContent = `${matches.length} result${matches.length === 1 ? "" : "s"}`;
  } else {
    searchResultsBar.hidden = false;
    searchResultsText.textContent = "No results";
  }
}

function highlightCurrent() {
  searchMatches.forEach((m, i) => m.classList.toggle("current", i === searchIndex));
  if (searchMatches[searchIndex]) {
    searchMatches[searchIndex].scrollIntoView({ behavior: "smooth", block: "center" });
  }
  searchResultsText.textContent = `${searchIndex + 1} / ${searchMatches.length}`;
}

// ── SCROLL FAB ────────────────────────────────────────────────────
function updateScrollFab() {
  const atBottom = chat.scrollHeight - chat.scrollTop - chat.clientHeight < 80;
  scrollFab.hidden = atBottom;
}

// ── CHAR COUNT ────────────────────────────────────────────────────
msgInput.addEventListener("input", () => {
  const len = msgInput.value.length;
  const max = 1000;
  if (len > 800) {
    charCountEl.textContent = `${len}/${max}`;
    charCountEl.classList.add("visible");
    charCountEl.classList.toggle("warn", len >= 950);
  } else {
    charCountEl.classList.remove("visible", "warn");
  }
});

// ── EVENT LISTENERS ───────────────────────────────────────────────
function initEventListeners() {
  // Sidebar toggle
  toggleSidebarBtn?.addEventListener("click", () => {
    if (isMobile) appShell.classList.toggle("sidebar-visible");
    else appShell.classList.toggle("sidebar-hidden");
  });

  // Close sidebar on overlay click (mobile)
  appShell.addEventListener("click", (e) => {
    if (isMobile && appShell.classList.contains("sidebar-visible") && !e.target.closest(".sidebar")) {
      appShell.classList.remove("sidebar-visible");
    }
  });

  // Send
  sendBtn.addEventListener("click", send);
  msgInput.addEventListener("keydown", e => { if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); send(); } });

  // Voice input (mic)
  micBtn?.addEventListener("click", () => {
  if (isTranscribing) return;

  if (isRecording) stopRecording();
  else startRecording();
});

  // Voice output toggle
  voiceToggleBtn?.addEventListener("click", toggleVoiceOutput);

  // Reset
  resetBtn.addEventListener("click", resetChat);
  newChatBtn.addEventListener("click", resetChat);

  // Preset buttons
  document.querySelectorAll("[data-preset]").forEach(btn => {
    btn.addEventListener("click", () => { if (btn.dataset.preset) sendPreset(btn.dataset.preset); });
  });

  // Settings panel
  settingsBtn?.addEventListener("click", openSettings);
  settingsClose?.addEventListener("click", closeSettings);
  settingsOverlay?.addEventListener("click", e => { if (e.target === settingsOverlay) closeSettings(); });

  // Theme picker
  document.querySelectorAll("[data-theme-pick]").forEach(btn => {
    btn.addEventListener("click", () => { applyTheme(btn.dataset.themePick); syncSettingsPanelUI(); showToast(`Theme: ${btn.dataset.themePick}`); });
  });

  
  // Accent picker
document.querySelectorAll(".accent-option").forEach(btn => {
  btn.addEventListener("click", () => {
    applyAccent(btn.dataset.accent);
    syncSettingsPanelUI();
    showToast(`Accent: ${btn.dataset.accent}`);
  });
});
  // Layout picker
  document.querySelectorAll("[data-layout]").forEach(btn => {
    if (btn.closest(".layout-picker")) {
      btn.addEventListener("click", () => { applyLayout(btn.dataset.layout); syncSettingsPanelUI(); showToast(`Layout: ${btn.dataset.layout}`); });
    }
  });

  // Bubble style
  document.querySelectorAll("[data-bubble]").forEach(btn => {
    if (btn.closest(".bubble-style-row")) {
      btn.addEventListener("click", () => { applyBubbleStyle(btn.dataset.bubble); syncSettingsPanelUI(); });
    }
  });

  // Font size
  fontSizeSlider?.addEventListener("input", () => {
    applyFontSize(parseInt(fontSizeSlider.value, 10));
  });

  // Toggles
  animToggle?.addEventListener("change", () => {
    prefs.animations = animToggle.checked;
    applyAnimations(prefs.animations);
    savePrefs();
  });
  typingToggle?.addEventListener("change", () => {
    prefs.typing = typingToggle.checked; savePrefs();
  });
  timestampToggle?.addEventListener("change", () => {
    prefs.timestamps = timestampToggle.checked;
    updateTimestampVisibility(); savePrefs();
  });

  // Search
  searchToggleBtn?.addEventListener("click", () => {
    searchWrap.classList.contains("open") ? closeSearch() : openSearch();
  });
  searchInput?.addEventListener("input", () => runSearch(searchInput.value));
  searchInput?.addEventListener("keydown", e => {
    if (e.key === "Escape") closeSearch();
    if (e.key === "Enter") {
      if (searchMatches.length) {
        searchIndex = (searchIndex + 1) % searchMatches.length;
        highlightCurrent();
      }
    }
  });
  searchPrev?.addEventListener("click", () => {
    if (!searchMatches.length) return;
    searchIndex = (searchIndex - 1 + searchMatches.length) % searchMatches.length;
    highlightCurrent();
  });
  searchNext?.addEventListener("click", () => {
    if (!searchMatches.length) return;
    searchIndex = (searchIndex + 1) % searchMatches.length;
    highlightCurrent();
  });
  searchClear?.addEventListener("click", closeSearch);

  // Scroll FAB
  scrollFab?.addEventListener("click", () => {
    chat.scrollTo({ top: chat.scrollHeight, behavior: "smooth" });
  });
  chat.addEventListener("scroll", updateScrollFab);

  // Keyboard shortcut: Ctrl/Cmd+K to focus input
    document.addEventListener("keydown", e => {
    if ((e.ctrlKey || e.metaKey) && e.key === "k") { e.preventDefault(); msgInput.focus(); }
    if ((e.ctrlKey || e.metaKey) && e.key === "f") { e.preventDefault(); openSearch(); }

    if (settingsOverlay?.classList.contains("open")) {
      if (e.key === "Escape") closeSettings();
      trapFocus(document.getElementById("settingsPanel"), e);
    }

    if (feedbackModalOverlay?.classList.contains("open")) {
      if (e.key === "Escape") closeFeedbackModal();
      trapFocus(feedbackModal, e);
    }
  });
  // Feedback modal
  feedbackModalClose?.addEventListener("click", closeFeedbackModal);
  feedbackCancelBtn?.addEventListener("click", closeFeedbackModal);

  feedbackSubmitBtn?.addEventListener("click", async () => {
    const comment = feedbackComment.value.trim();
    await submitFeedback(pendingFeedbackHelpful || "No", comment);
    closeFeedbackModal();
  });

  feedbackModalOverlay?.addEventListener("click", (e) => {
    if (e.target === feedbackModalOverlay) closeFeedbackModal();
  });

  // Resize
  window.addEventListener("resize", () => { isMobile = window.innerWidth <= 768; });
}
  
// ── START ─────────────────────────────────────────────────────────
init();

// ═══════════════════════════════════════════════════════════════
// FORM ASSISTANT PANEL
// Communicates with:  GET  /api/form/types
//                     POST /api/form/start
//                     POST /api/form/step
//                     DELETE /api/form/cancel?sessionId=
// ═══════════════════════════════════════════════════════════════

(function () {
  "use strict";

  // ── DOM refs ─────────────────────────────────────────────────
  const overlay  = document.getElementById("fpOverlay");
  const panel    = document.getElementById("fpPanel");
  const body     = document.getElementById("fpBody");
  const closeBtn = document.getElementById("fpClose");

  if (!overlay || !panel || !body) return; // guard: elements not found

  // ── State ─────────────────────────────────────────────────────
  let formActive  = false; // true while a form session is in progress
  let lastFocused = null;  // element to restore focus to on close

  // ── Open / close ─────────────────────────────────────────────
  function openPanel() {
  lastFocused = document.activeElement;
  overlay.setAttribute("aria-hidden", "false");
  overlay.classList.add("open");
  document.getElementById("appShell")?.setAttribute("inert", "");
  panel.focus();
  if (!formActive) showTypeSelector();
}

function closePanel() {
  if (formActive) cancelSession();
  overlay.setAttribute("aria-hidden", "true");
  overlay.classList.remove("open");
  document.getElementById("appShell")?.removeAttribute("inert");
  if (lastFocused) setTimeout(() => lastFocused.focus(), 0);
}
  // ── Cancel active session on server ──────────────────────────
  async function cancelSession() {
    formActive = false;
    try {
      await fetch(
        `/api/form/cancel?sessionId=${encodeURIComponent(sessionId)}`,
        { method: "DELETE" }
      );
    } catch (_) { /* best-effort */ }
  }

  // ── Screen: form type selector ────────────────────────────────
  async function showTypeSelector() {
    formActive = false;
    setBody(`<div class="fp-loading">Loading forms…</div>`);
    try {
      const res  = await fetch("/api/form/types");
      const data = await res.json();
      renderTypeSelector(data.types || []);
    } catch (_) {
      setBody(`<div class="fp-error">Could not load form types — is the server running?</div>`);
    }
  }

  function renderTypeSelector(types) {
    const items = types.map(t => `
      <button class="fp-type-btn" data-key="${esc(t.key)}" type="button">
        ${esc(t.label)}
        <svg width="14" height="14" viewBox="0 0 14 14" fill="none" aria-hidden="true">
          <path d="M5 3l4 4-4 4" stroke="currentColor" stroke-width="1.6"
                stroke-linecap="round" stroke-linejoin="round"/>
        </svg>
      </button>`).join("");

    setBody(`
      <div class="fp-intro">
        <div class="fp-intro-icon">📋</div>
        <h3>Which form do you need help with?</h3>
        <p>I'll guide you step by step. You can review your answers before you submit.</p>
      </div>
      <div class="fp-type-list">${items}</div>
    `);

    body.querySelectorAll(".fp-type-btn").forEach(btn =>
      btn.addEventListener("click", () => startForm(btn.dataset.key))
    );
  }

  // ── Screen: start form ────────────────────────────────────────
  async function startForm(formType) {
    setBody(`<div class="fp-loading">Starting form…</div>`);
    try {
      const res  = await fetch("/api/form/start", {
        method:  "POST",
        headers: { "Content-Type": "application/json" },
        body:    JSON.stringify({ sessionId, formType }),
      });
      const step = await res.json();
      formActive = true;
      renderStep(step);
    } catch (_) {
      setBody(`<div class="fp-error">Could not start the form. Please try again.</div>`);
    }
  }

  // ── Screen: question step ─────────────────────────────────────
  function renderStep(step) {
    if (step.isComplete) { renderSummary(step.summary); return; }

    const total   = step.totalSteps  || 1;
    const current = step.stepNumber  || 1;
    const pct     = Math.round(((current - 1) / total) * 100);

    const hasOpts = Array.isArray(step.options) && step.options.length > 0;
    const isDate  = step.fieldType === "date";

    const optionsHtml = hasOpts
      ? `<div class="fp-options">
           ${step.options.map(o =>
             `<button class="fp-option-btn" data-value="${esc(o)}" type="button">${esc(o)}</button>`
           ).join("")}
         </div>`
      : `<div class="fp-input-wrap">
           <input id="fpAnswer" class="fp-input"
                  type="${isDate ? "date" : "text"}"
                  placeholder="${isDate ? "" : "Type your answer…"}"
                  autocomplete="off" />
         </div>
         <div class="fp-action-row">
           <button class="fp-submit-btn" id="fpSubmit" type="button">Continue →</button>
         </div>`;

    setBody(`
      <div class="fp-progress-wrap" role="progressbar"
           aria-valuenow="${pct}" aria-valuemin="0" aria-valuemax="100"
           aria-label="Form progress: step ${current} of ${total}">
        <div class="fp-progress-bar">
          <div class="fp-progress-fill" style="width:${pct}%"></div>
        </div>
        <span class="fp-progress-label">Step ${current} of ${total}</span>
      </div>

      <div class="fp-question">
        <label class="fp-question-text" for="fpAnswer">${esc(step.nextQuestion)}</label>
        ${step.hint ? `<p class="fp-hint">${esc(step.hint)}</p>` : ""}
      </div>

      ${optionsHtml}

      <button class="fp-cancel-link" id="fpCancelLink" type="button">
        Cancel and exit form
      </button>
    `);

    // Wire option buttons
    body.querySelectorAll(".fp-option-btn").forEach(btn =>
      btn.addEventListener("click", () => {
        btn.classList.add("selected");
        submitAnswer(btn.dataset.value);
      })
    );

    // Wire text/date input
    const input  = body.querySelector("#fpAnswer");
    const submit = body.querySelector("#fpSubmit");
    if (input && submit) {
      input.addEventListener("keydown", e => {
        if (e.key === "Enter") { e.preventDefault(); submitAnswer(input.value.trim()); }
      });
      submit.addEventListener("click", () => submitAnswer(input.value.trim()));
      setTimeout(() => input.focus(), 50);
    }

    // Wire cancel link
    body.querySelector("#fpCancelLink")?.addEventListener("click", () => {
      cancelSession().then(showTypeSelector);
    });
  }

  // ── Submit an answer ──────────────────────────────────────────
  async function submitAnswer(answer) {
    if (!answer) return;
    setBody(`<div class="fp-loading">Saving…</div>`);
    try {
      const res  = await fetch("/api/form/step", {
        method:  "POST",
        headers: { "Content-Type": "application/json" },
        body:    JSON.stringify({ sessionId, answer }),
      });
      const step = await res.json();
      renderStep(step);
    } catch (_) {
      setBody(`<div class="fp-error">Something went wrong. Please try again.</div>`);
    }
  }

  // ── Screen: completed summary ─────────────────────────────────
  function renderSummary(summary) {
    formActive = false;
    const fields    = summary?.fields || {};
    const fieldRows = Object.entries(fields)
      .map(([k, v]) => `<tr><th>${esc(k)}</th><td>${esc(v)}</td></tr>`)
      .join("");

    const linkBtn = summary?.nextStepsUrl
      ? `<a href="${esc(summary.nextStepsUrl)}" target="_blank" rel="noopener"
            class="fp-submit-btn fp-link-btn">
           Submit on Bradford Council website →
         </a>`
      : "";

    setBody(`
      <div class="fp-summary">
        <div class="fp-summary-icon">✅</div>
        <h3>${esc(summary?.formTitle || "Form Complete")}</h3>
        <p class="fp-summary-msg">${esc(summary?.message || "Your form answers have been prepared.")}</p>
        ${fieldRows
          ? `<table class="fp-summary-table" aria-label="Your form answers">
               <tbody>${fieldRows}</tbody>
             </table>`
          : ""}
        ${linkBtn}
        <button class="fp-restart-btn" id="fpRestart" type="button">
          Start another form
        </button>
      </div>
    `);

    body.querySelector("#fpRestart")?.addEventListener("click", showTypeSelector);
  }

  // ── Helpers ───────────────────────────────────────────────────
  function setBody(html) { body.innerHTML = html; }

  function esc(s) {
    return String(s ?? "")
      .replace(/&/g, "&amp;").replace(/</g, "&lt;")
      .replace(/>/g, "&gt;").replace(/"/g, "&quot;");
  }

  // ── Event wiring ──────────────────────────────────────────────
  closeBtn?.addEventListener("click", closePanel);

  overlay.addEventListener("click", e => {
    if (e.target === overlay) closePanel();
  });

  document.addEventListener("keydown", e => {
    if (!overlay.classList.contains("open")) return;
    if (e.key === "Escape") { closePanel(); return; }
    if (e.key === "Tab")    trapFocus(panel, e);
  });

  // ── Expose to global scope (called by sidebar + quickstart) ──
  window.openFormPanel = openPanel;

})();

// ═══════════════════════════════════════════════════════════════════
//  APPOINTMENT PANEL  (IIFE)
//  Uses: GET /api/appointment/types
//        GET /api/appointment/slots?appointmentType=
//        POST /api/appointment/confirm
// ═══════════════════════════════════════════════════════════════════
(function () {
  "use strict";

  // ── DOM refs ─────────────────────────────────────────────────
  const overlay  = document.getElementById("apOverlay");
  const panel    = document.getElementById("apPanel");
  const body     = document.getElementById("apBody");
  const closeBtn = document.getElementById("apClose");

  if (!overlay || !panel || !body) return; // guard: elements not found

  // ── State ─────────────────────────────────────────────────────
  let lastFocused = null; // element to restore focus to on close

  // Booking state (reset on each open)
  let apState = {};

  function resetState() {
    apState = {
      step: "type",          // type | date | time | name | phone | email | confirm | success
      appointmentType: "",
      typeName: "",
      date: "",
      time: "",
      name: "",
      phone: "",
      email: "",
      slots: null,           // { availableDates, availableTimes }
    };
  }

  // ── Open / close ─────────────────────────────────────────────
  function openPanel() {
    lastFocused = document.activeElement;
    overlay.setAttribute("aria-hidden", "false");
    overlay.classList.add("open");
    document.getElementById("appShell")?.setAttribute("inert", "");
    panel.focus();
    resetState();
    showTypeSelector();
  }

  function closePanel() {
    overlay.setAttribute("aria-hidden", "true");
    overlay.classList.remove("open");
    document.getElementById("appShell")?.removeAttribute("inert");
    if (lastFocused) setTimeout(() => lastFocused.focus(), 0);
  }

  // ── Helper: progress bar HTML ─────────────────────────────────
  const TOTAL_STEPS = 6; // type, date, time, name, phone, email (confirm/success are end states)
  const STEP_IDX = { type: 1, date: 2, time: 3, name: 4, phone: 5, email: 6 };

  function progressBar(step) {
    const idx = STEP_IDX[step] || 0;
    if (!idx) return "";
    const pct = Math.round((idx / TOTAL_STEPS) * 100);
    return `
      <div class="fp-progress-wrap" aria-label="Step ${idx} of ${TOTAL_STEPS}">
        <div class="fp-progress-bar">
          <div class="fp-progress-fill" style="width:${pct}%"></div>
        </div>
        <span class="fp-progress-label">Step ${idx} of ${TOTAL_STEPS}</span>
      </div>`;
  }

  // ── Screen: appointment type selector ────────────────────────
  async function showTypeSelector() {
    setBody(`<div class="fp-loading">Loading appointment types…</div>`);
    try {
      const res  = await fetch("/api/appointment/types");
      const data = await res.json();
      renderTypeSelector(data.types || []);
    } catch (_) {
      setBody(`<div class="fp-error">Could not load appointment types — is the server running?</div>`);
    }
  }

  function renderTypeSelector(types) {
    const items = types.map(t => `
      <button class="fp-type-btn" data-key="${esc(t.id || t.Id)}" data-name="${esc(t.name || t.Name)}" type="button">
        <span class="fp-type-label">${esc(t.name || t.Name)}</span>
        <span class="fp-type-desc">${esc(t.description || t.Description || "")}</span>
        ${(t.durationMinutes || t.DurationMinutes) ? `<span class="fp-type-meta">⏱ ${esc(String(t.durationMinutes || t.DurationMinutes))} min</span>` : ""}
      </button>`).join("");

    setBody(`
      <div class="fp-intro">
        <p>Choose the type of appointment you need.</p>
      </div>
      ${progressBar("type")}
      <div class="fp-type-list">${items}</div>
      <a href="#" class="fp-cancel-link" id="apCancelLink">Cancel</a>
    `);

    body.querySelectorAll(".fp-type-btn").forEach(btn => {
      btn.addEventListener("click", () => {
        apState.appointmentType = btn.dataset.key;
        apState.typeName        = btn.dataset.name;
        apState.step = "date";
        loadSlots();
      });
    });
    body.querySelector("#apCancelLink")?.addEventListener("click", e => { e.preventDefault(); closePanel(); });
  }

  // ── Load slots then show date selection ───────────────────────
  async function loadSlots() {
    setBody(`<div class="fp-loading">Checking available slots…</div>`);
    try {
      const res   = await fetch(`/api/appointment/slots?appointmentType=${encodeURIComponent(apState.appointmentType)}`);
      const data  = await res.json();
      apState.slots = data;
      showDateSelector();
    } catch (_) {
      setBody(`<div class="fp-error">Could not load available slots. Please try again.</div>`);
    }
  }

  // ── Screen: date selection ────────────────────────────────────
  function showDateSelector() {
    const dates = apState.slots?.availableDates || apState.slots?.AvailableDates || [];
    if (!dates.length) {
      setBody(`<div class="fp-error">No available dates found for this appointment type.</div>`);
      return;
    }

    const items = dates.map(d => `
      <button class="fp-option-btn" data-val="${esc(d)}" type="button">${esc(d)}</button>`).join("");

    setBody(`
      <div class="fp-intro">
        <p><strong>${esc(apState.typeName)}</strong></p>
        <p>Choose a date for your appointment.</p>
      </div>
      ${progressBar("date")}
      <p class="fp-question-text">Select a date:</p>
      <div class="fp-options">${items}</div>
      <a href="#" class="fp-cancel-link" id="apCancelLink">← Back to types</a>
    `);

    body.querySelectorAll(".fp-option-btn").forEach(btn => {
      btn.addEventListener("click", () => {
        apState.date = btn.dataset.val;
        apState.step = "time";
        showTimeSelector();
      });
    });
    body.querySelector("#apCancelLink")?.addEventListener("click", e => { e.preventDefault(); resetState(); showTypeSelector(); });
  }

  // ── Screen: time selection ────────────────────────────────────
  function showTimeSelector() {
    const times = apState.slots?.availableTimes || apState.slots?.AvailableTimes || [];
    if (!times.length) {
      setBody(`<div class="fp-error">No available times found.</div>`);
      return;
    }

    const items = times.map(t => `
      <button class="fp-option-btn" data-val="${esc(t)}" type="button">${esc(t)}</button>`).join("");

    setBody(`
      <div class="fp-intro">
        <p><strong>${esc(apState.typeName)}</strong> — <em>${esc(apState.date)}</em></p>
        <p>Choose a time slot.</p>
      </div>
      ${progressBar("time")}
      <p class="fp-question-text">Select a time:</p>
      <div class="fp-options ap-time-grid">${items}</div>
      <a href="#" class="fp-cancel-link" id="apCancelLink">← Back to dates</a>
    `);

    body.querySelectorAll(".fp-option-btn").forEach(btn => {
      btn.addEventListener("click", () => {
        apState.time = btn.dataset.val;
        apState.step = "name";
        showNameInput();
      });
    });
    body.querySelector("#apCancelLink")?.addEventListener("click", e => { e.preventDefault(); showDateSelector(); });
  }

  // ── Screen: name input ────────────────────────────────────────
  function showNameInput() {
    setBody(`
      <div class="fp-intro">
        <p>Almost there — just a few details.</p>
      </div>
      ${progressBar("name")}
      <label class="fp-question-text" for="apNameInput">Your full name</label>
      <input class="fp-input" id="apNameInput" type="text" placeholder="e.g. Jane Smith"
             autocomplete="name" aria-required="true" />
      <p class="fp-hint">As it appears on your identification.</p>
      <button class="fp-submit-btn" id="apNameNext" type="button">Continue →</button>
      <a href="#" class="fp-cancel-link" id="apCancelLink">← Back to times</a>
    `);
    const input = body.querySelector("#apNameInput");
    input?.focus();

    body.querySelector("#apNameNext")?.addEventListener("click", () => {
      const val = input?.value.trim() || "";
      if (!val) { input?.classList.add("fp-input-error"); input?.focus(); return; }
      input?.classList.remove("fp-input-error");
      apState.name = val;
      apState.step = "phone";
      showPhoneInput();
    });
    input?.addEventListener("keydown", e => { if (e.key === "Enter") body.querySelector("#apNameNext")?.click(); });
    body.querySelector("#apCancelLink")?.addEventListener("click", e => { e.preventDefault(); showTimeSelector(); });
  }

  // ── Screen: phone input ───────────────────────────────────────
  function showPhoneInput() {
    setBody(`
      <div class="fp-intro">
        <p>We may need to contact you before your appointment.</p>
      </div>
      ${progressBar("phone")}
      <label class="fp-question-text" for="apPhoneInput">Your phone number</label>
      <input class="fp-input" id="apPhoneInput" type="tel" placeholder="e.g. 01274 123456"
             autocomplete="tel" />
      <p class="fp-hint">Optional — leave blank to skip.</p>
      <button class="fp-submit-btn" id="apPhoneNext" type="button">Continue →</button>
      <a href="#" class="fp-cancel-link" id="apCancelLink">← Back</a>
    `);
    const input = body.querySelector("#apPhoneInput");
    input?.focus();

    body.querySelector("#apPhoneNext")?.addEventListener("click", () => {
      apState.phone = input?.value.trim() || "";
      apState.step = "email";
      showEmailInput();
    });
    input?.addEventListener("keydown", e => { if (e.key === "Enter") body.querySelector("#apPhoneNext")?.click(); });
    body.querySelector("#apCancelLink")?.addEventListener("click", e => { e.preventDefault(); showNameInput(); });
  }

  // ── Screen: email input ───────────────────────────────────────
  function showEmailInput() {
    setBody(`
      <div class="fp-intro">
        <p>We'll send a confirmation to this address.</p>
      </div>
      ${progressBar("email")}
      <label class="fp-question-text" for="apEmailInput">Your email address</label>
      <input class="fp-input" id="apEmailInput" type="email" placeholder="e.g. jane@example.com"
             autocomplete="email" />
      <p class="fp-hint">Optional — leave blank to skip.</p>
      <button class="fp-submit-btn" id="apEmailNext" type="button">Review booking →</button>
      <a href="#" class="fp-cancel-link" id="apCancelLink">← Back</a>
    `);
    const input = body.querySelector("#apEmailInput");
    input?.focus();

    body.querySelector("#apEmailNext")?.addEventListener("click", () => {
      apState.email = input?.value.trim() || "";
      apState.step = "confirm";
      showConfirmScreen();
    });
    input?.addEventListener("keydown", e => { if (e.key === "Enter") body.querySelector("#apEmailNext")?.click(); });
    body.querySelector("#apCancelLink")?.addEventListener("click", e => { e.preventDefault(); showPhoneInput(); });
  }

  // ── Screen: confirmation review ───────────────────────────────
  function showConfirmScreen() {
    const rows = [
      ["Type",  apState.typeName],
      ["Date",  apState.date],
      ["Time",  apState.time],
      ["Name",  apState.name],
      apState.phone ? ["Phone", apState.phone] : null,
      apState.email ? ["Email", apState.email] : null,
    ]
      .filter(Boolean)
      .map(([k, v]) => `<tr><th scope="row">${esc(k)}</th><td>${esc(v)}</td></tr>`)
      .join("");

    setBody(`
      <div class="fp-intro">
        <p>Please review your booking details before confirming.</p>
      </div>
      <div class="fp-summary-preview">
        <table class="fp-summary-table" aria-label="Booking summary">
          <tbody>${rows}</tbody>
        </table>
      </div>
      <button class="fp-submit-btn" id="apConfirmBtn" type="button">Confirm booking</button>
      <a href="#" class="fp-cancel-link" id="apCancelLink">← Edit details</a>
    `);

    body.querySelector("#apConfirmBtn")?.addEventListener("click", submitBooking);
    body.querySelector("#apCancelLink")?.addEventListener("click", e => { e.preventDefault(); showEmailInput(); });
  }

  // ── Submit booking to API ─────────────────────────────────────
  async function submitBooking() {
    setBody(`<div class="fp-loading">Confirming your appointment…</div>`);
    try {
      const payload = {
        SessionId:       sessionId,
        AppointmentType: apState.appointmentType,
        Date:            apState.date,
        Time:            apState.time,
        Name:            apState.name,
        Phone:           apState.phone,
        Email:           apState.email,
      };
      const res  = await fetch("/api/appointment/confirm", {
        method:  "POST",
        headers: { "Content-Type": "application/json" },
        body:    JSON.stringify(payload),
      });
      const data = await res.json();
      if (data.success === false || data.Success === false) {
        setBody(`<div class="fp-error">${esc(data.message || data.Message || "Booking failed — please try again.")}</div>`);
      } else {
        showSuccessScreen(data);
      }
    } catch (_) {
      setBody(`<div class="fp-error">Network error — please check your connection and try again.</div>`);
    }
  }

  // ── Screen: success ───────────────────────────────────────────
  function showSuccessScreen(data) {
    const ref  = esc(data.reference || data.Reference || "N/A");
    const type = esc(data.appointmentType || data.AppointmentType || apState.typeName);
    const date = esc(data.date || data.Date || apState.date);
    const time = esc(data.time || data.Time || apState.time);
    const name = esc(data.name || data.Name || apState.name);

    const optRows = [
      (data.phone || data.Phone || apState.phone) ? `<tr><th scope="row">Phone</th><td>${esc(data.phone || data.Phone || apState.phone)}</td></tr>` : "",
      (data.email || data.Email || apState.email) ? `<tr><th scope="row">Email</th><td>${esc(data.email || data.Email || apState.email)}</td></tr>` : "",
    ].join("");

    setBody(`
      <div class="fp-summary">
        <div class="fp-summary-icon">✅</div>
        <h3>Appointment Confirmed</h3>
        <p class="fp-summary-msg">Your appointment has been booked. Please make a note of your reference number.</p>
        <table class="fp-summary-table" aria-label="Booking confirmation details">
          <tbody>
            <tr><th scope="row">Reference</th><td><strong>${ref}</strong></td></tr>
            <tr><th scope="row">Type</th><td>${type}</td></tr>
            <tr><th scope="row">Date</th><td>${date}</td></tr>
            <tr><th scope="row">Time</th><td>${time}</td></tr>
            <tr><th scope="row">Name</th><td>${name}</td></tr>
            ${optRows}
          </tbody>
        </table>
        <p class="fp-hint" style="margin-top:12px">Please bring proof of identity. Arrive 5 minutes early.</p>
        <button class="fp-restart-btn" id="apRestart" type="button">Book another appointment</button>
        <button class="fp-link-btn" id="apClose2" type="button">Close</button>
      </div>
    `);

    body.querySelector("#apRestart")?.addEventListener("click", () => { resetState(); showTypeSelector(); });
    body.querySelector("#apClose2")?.addEventListener("click", closePanel);
  }

  // ── Helpers ───────────────────────────────────────────────────
  function setBody(html) { body.innerHTML = html; }

  function esc(s) {
    return String(s ?? "")
      .replace(/&/g, "&amp;").replace(/</g, "&lt;")
      .replace(/>/g, "&gt;").replace(/"/g, "&quot;");
  }

  // ── Event wiring ──────────────────────────────────────────────
  closeBtn?.addEventListener("click", closePanel);

  overlay.addEventListener("click", e => {
    if (e.target === overlay) closePanel();
  });

  document.addEventListener("keydown", e => {
    if (!overlay.classList.contains("open")) return;
    if (e.key === "Escape") { closePanel(); return; }
    if (e.key === "Tab")    trapFocus(panel, e);
  });

  // ── Expose to global scope ────────────────────────────────────
  window.openAppointmentPanel = openPanel;

})();

// ═══════════════════════════════════════════════════════════════════
//  NEARBY SERVICES PANEL  (IIFE)
//  Reuses: GET /api/location/nearby?postcode=&type=
//  type = "all" | "library" | "council_office" | "recycling_centre"
//  Response shape: NearbyServicesResponse (PascalCase from .NET)
//    { Postcode, CouncilOffices[], Libraries[], RecyclingCentres[],
//      Schools[], LocationNote, Error }
//  Each item: { Name, Address, Phone, OpeningHours, Notes,
//               EstimatedDistanceMiles, MapUrl, Website }
// ═══════════════════════════════════════════════════════════════════
(function () {
  "use strict";

  // ── DOM refs ─────────────────────────────────────────────────
  const overlay  = document.getElementById("nsOverlay");
  const panel    = document.getElementById("nsPanel");
  const body     = document.getElementById("nsBody");
  const closeBtn = document.getElementById("nsClose");

  if (!overlay || !panel || !body) return;

  // ── State ─────────────────────────────────────────────────────
  let lastFocused = null;
  let nsState     = {};

  // Category metadata — drives the selector screen
  const CATEGORIES = [
    {
      key:   "library",
      label: "Libraries",
      desc:  "Books, Wi-Fi, computers, local events",
      icon:  "📚",
    },
    {
      key:   "council_office",
      label: "Council offices",
      desc:  "In-person council services and enquiries",
      icon:  "🏛️",
    },
    {
      key:   "recycling_centre",
      label: "Recycling centres",
      desc:  "Household waste, bulky items, garden waste",
      icon:  "♻️",
    },
    {
      key:   "all",
      label: "All nearby services",
      desc:  "Libraries, offices, and recycling centres together",
      icon:  "📍",
    },
  ];

  function resetState(opts) {
    nsState = {
      step:     "category",  // category | postcode | results
      category: opts?.category || "all",
    };
  }

  // ── Open / close ─────────────────────────────────────────────
  function openPanel(opts) {
    lastFocused = document.activeElement;
    overlay.setAttribute("aria-hidden", "false");
    overlay.classList.add("open");
    document.getElementById("appShell")?.setAttribute("inert", "");
    panel.focus();
    resetState(opts);
    // If caller pre-selected a category skip straight to postcode input
    if (opts?.category && opts.category !== "all") {
      showPostcodeInput();
    } else {
      showCategorySelector();
    }
  }

  function closePanel() {
    overlay.setAttribute("aria-hidden", "true");
    overlay.classList.remove("open");
    document.getElementById("appShell")?.removeAttribute("inert");
    if (lastFocused) setTimeout(() => lastFocused.focus(), 0);
  }

  // ── Screen: category selector ─────────────────────────────────
  function showCategorySelector() {
    nsState.step = "category";

    const items = CATEGORIES.map(c => `
      <button class="fp-type-btn ns-cat-btn" data-key="${esc(c.key)}" type="button">
        <span class="ns-cat-icon" aria-hidden="true">${esc(c.icon)}</span>
        <span class="fp-type-label">${esc(c.label)}</span>
        <span class="fp-type-desc">${esc(c.desc)}</span>
      </button>`).join("");

    setBody(`
      <div class="fp-intro">
        <p>Find Bradford Council services close to your address. Choose a category to get started.</p>
      </div>
      <div class="fp-type-list">${items}</div>
    `);

    body.querySelectorAll(".ns-cat-btn").forEach(btn => {
      btn.addEventListener("click", () => {
        nsState.category = btn.dataset.key;
        showPostcodeInput();
      });
    });
  }

  // ── Screen: postcode input ────────────────────────────────────
  function showPostcodeInput() {
    nsState.step = "postcode";
    const cat    = CATEGORIES.find(c => c.key === nsState.category) || CATEGORIES[3];

    setBody(`
      <div class="fp-intro">
        <p>Enter your postcode to find <strong>${esc(cat.label.toLowerCase())}</strong> near you.</p>
      </div>

      <label class="fp-question-text" for="nsPostcodeInput">Your postcode</label>
      <input class="fp-input sf-postcode-input" id="nsPostcodeInput" type="text"
             placeholder="e.g. BD3 8QX" maxlength="8"
             autocomplete="postal-code" aria-required="true"
             style="text-transform:uppercase" />
      <p class="fp-hint">Bradford district postcodes: BD1–BD21, LS29.</p>

      <button class="fp-submit-btn" id="nsSearchBtn" type="button">Search →</button>
      <a href="#" class="fp-cancel-link" id="nsBackLink">← Change category</a>
    `);

    const input = body.querySelector("#nsPostcodeInput");
    input?.addEventListener("input", () => { input.value = input.value.toUpperCase(); });
    input?.focus();

    body.querySelector("#nsSearchBtn")?.addEventListener("click", () => doSearch(input));
    input?.addEventListener("keydown", e => { if (e.key === "Enter") doSearch(input); });
    body.querySelector("#nsBackLink")?.addEventListener("click",
      e => { e.preventDefault(); showCategorySelector(); });
  }

  // ── Trigger search ────────────────────────────────────────────
  async function doSearch(inputEl) {
    const postcode = inputEl?.value.trim();
    if (!postcode) {
      inputEl?.classList.add("fp-input-error");
      inputEl?.focus();
      return;
    }
    inputEl?.classList.remove("fp-input-error");
    await showResults(postcode);
  }

  // ── Screen: results ───────────────────────────────────────────
  async function showResults(postcode) {
    nsState.step = "results";
    const cat    = CATEGORIES.find(c => c.key === nsState.category) || CATEGORIES[3];

    setBody(`<div class="fp-loading">Searching for ${esc(cat.label.toLowerCase())} near ${esc(postcode)}…</div>`);

    try {
      const url = `/api/location/nearby?postcode=${encodeURIComponent(postcode)}&type=${encodeURIComponent(nsState.category)}`;
      const res  = await fetch(url);
      const data = await res.json();

      if (!res.ok || data.error || data.Error) {
        showError(data.error || data.Error || "Search failed — please check the postcode and try again.", postcode);
        return;
      }

      renderResults(data, postcode);
    } catch (_) {
      showError("Network error — please check your connection and try again.", postcode);
    }
  }

  // ── Error state ───────────────────────────────────────────────
  function showError(message, postcode) {
    setBody(`
      <div class="ns-error-state">
        <div class="ns-error-icon" aria-hidden="true">⚠️</div>
        <p>${esc(message)}</p>
        ${postcode ? `<p class="fp-hint">Postcode tried: <strong>${esc(postcode)}</strong></p>` : ""}
        <button class="fp-submit-btn" id="nsRetryBtn" type="button">Try another postcode</button>
        <a href="#" class="fp-cancel-link" id="nsBackLink2">← Change category</a>
      </div>
    `);
    body.querySelector("#nsRetryBtn")?.addEventListener("click", showPostcodeInput);
    body.querySelector("#nsBackLink2")?.addEventListener("click",
      e => { e.preventDefault(); showCategorySelector(); });
  }

  // ── Render results ────────────────────────────────────────────
  function renderResults(data, postcode) {
    // PascalCase from .NET serialiser; guard both casings defensively
    const offices   = data.CouncilOffices   || data.councilOffices   || [];
    const libraries = data.Libraries        || data.libraries        || [];
    const recycling = data.RecyclingCentres || data.recyclingCentres || [];
    const schools   = data.Schools          || data.schools          || [];
    const note      = data.LocationNote     || data.locationNote     || "";

    const allEmpty = !offices.length && !libraries.length && !recycling.length && !schools.length;

    if (allEmpty) {
      showError(`No services found near ${postcode}. Please try a different postcode.`, "");
      return;
    }

    // Location note banner (approximate distances warning)
    const noteBanner = note
      ? `<div class="ns-note-banner" role="note">ℹ️ ${esc(note)}</div>`
      : "";

    // Build section HTML
    let sectionsHtml = "";
    if (offices.length)   sectionsHtml += renderSection("🏛️ Council Offices",        offices);
    if (libraries.length) sectionsHtml += renderSection("📚 Libraries",              libraries);
    if (recycling.length) sectionsHtml += renderSection("♻️ Recycling Centres",      recycling);
    if (schools.length)   sectionsHtml += renderSection("🏫 Nearby Schools",         schools);

    const pc = data.Postcode || data.postcode || postcode;

    setBody(`
      <div class="ns-results-header">
        <span>Results near <strong>${esc(pc)}</strong></span>
        <span class="sf-results-count">${offices.length + libraries.length + recycling.length + schools.length} found</span>
      </div>
      ${noteBanner}
      ${sectionsHtml}
      <div class="sf-divider"></div>
      <div class="sf-actions">
        <button class="fp-option-btn sf-action-btn" id="nsNewSearchBtn" type="button">🔍 New search</button>
        <button class="fp-option-btn sf-action-btn" id="nsChangeCatBtn" type="button">← Change category</button>
      </div>
    `);

    body.querySelector("#nsNewSearchBtn")?.addEventListener("click", showPostcodeInput);
    body.querySelector("#nsChangeCatBtn")?.addEventListener("click", showCategorySelector);
  }

  // ── Render a single category section ─────────────────────────
  function renderSection(title, items) {
    if (!items.length) return "";

    const cards = items.map(item => {
      const name    = item.Name    || item.name    || "";
      const address = item.Address || item.address || "";
      const phone   = item.Phone   || item.phone   || "";
      const hours   = item.OpeningHours || item.openingHours || "";
      const notes   = item.Notes   || item.notes   || "";
      const mapUrl  = item.MapUrl  || item.mapUrl  || "";
      const website = item.Website || item.website || "";
      const dist    = item.EstimatedDistanceMiles ?? item.estimatedDistanceMiles;

      const distBadge = dist != null
        ? `<span class="sf-card-distance">~${esc(String(dist))} mi</span>`
        : "";

      const phoneRow = phone
        ? `<div class="sf-card-row"><span class="sf-card-icon">📞</span><a href="tel:${esc(phone.replace(/\s/g,''))}">${esc(phone)}</a></div>`
        : "";

      const hoursRow = hours
        ? `<div class="sf-card-row"><span class="sf-card-icon">🕒</span>${esc(hours)}</div>`
        : "";

      const notesRow = notes
        ? `<div class="sf-card-notes">${esc(notes)}</div>`
        : "";

      const mapLink = mapUrl
        ? `<a class="sf-card-map" href="${esc(mapUrl)}" target="_blank" rel="noopener noreferrer" aria-label="Open ${esc(name)} in Google Maps">Open in map ↗</a>`
        : "";

      const webLink = website
        ? `<a class="sf-card-web" href="${esc(website)}" target="_blank" rel="noopener noreferrer">Website ↗</a>`
        : "";

      return `
        <article class="sf-card" aria-label="${esc(name)}">
          <div class="sf-card-header">
            <span class="sf-card-name">${esc(name)}</span>
            ${distBadge}
          </div>
          <div class="sf-card-row"><span class="sf-card-icon">📍</span>${esc(address)}</div>
          ${phoneRow}
          ${hoursRow}
          ${notesRow}
          ${(mapLink || webLink) ? `<div class="sf-card-links">${mapLink}${webLink}</div>` : ""}
        </article>`;
    }).join("");

    return `
      <div class="ns-section">
        <h4 class="sf-section-title">${esc(title)}</h4>
        <div class="sf-card-list">${cards}</div>
      </div>`;
  }

  // ── Helpers ───────────────────────────────────────────────────
  function setBody(html) { body.innerHTML = html; }

  function esc(s) {
    return String(s ?? "")
      .replace(/&/g, "&amp;").replace(/</g, "&lt;")
      .replace(/>/g, "&gt;").replace(/"/g, "&quot;");
  }

  // ── Event wiring ──────────────────────────────────────────────
  closeBtn?.addEventListener("click", closePanel);

  overlay.addEventListener("click", e => {
    if (e.target === overlay) closePanel();
  });

  document.addEventListener("keydown", e => {
    if (!overlay.classList.contains("open")) return;
    if (e.key === "Escape") { closePanel(); return; }
    if (e.key === "Tab")    trapFocus(panel, e);
  });

  // ── Expose to global scope ────────────────────────────────────
  window.openNearbyPanel = openPanel;

  // ── Chat interceptor ──────────────────────────────────────────
  // Wraps sendPreset so nearby-service chip/preset clicks open the panel.
  const _origSendPreset = window.sendPreset;
  window.sendPreset = function (text) {
    if (isNearbyIntent(text)) {
      openPanel(detectNearbyOpts(text));
      return;
    }
    _origSendPreset(text);
  };

  // Capture-phase hook on send button + Enter key for typed messages.
  (function hookSend() {
    const btn   = document.getElementById("sendBtn");
    const input = document.getElementById("msg");
    if (!btn || !input) return;

    btn.addEventListener("click", interceptSend, { capture: true });
    input.addEventListener("keydown", e => {
      if (e.key === "Enter" && !e.shiftKey) interceptSend(e);
    }, { capture: true });

    function interceptSend(e) {
      const text = (input?.value || "").trim();
      if (!text || !isNearbyIntent(text)) return;
      e.stopImmediatePropagation();
      e.preventDefault();
      input.value = "";
      openPanel(detectNearbyOpts(text));
    }
  })();

  // ── Intent detection ──────────────────────────────────────────
  function isNearbyIntent(text) {
    const t = text.toLowerCase();
    return (
      t === "nearby services" ||
      t.includes("find nearby services") ||
      t.includes("nearby services") ||
      // library
      t.includes("find my nearest library") ||
      t.includes("find nearest library") ||
      t.includes("find a library") ||
      t.includes("find library") ||
      t.includes("library near") ||
      t.includes("nearest library") ||
      // council office
      t.includes("find council office") ||
      t.includes("find a council office") ||
      t.includes("council office near") ||
      t.includes("nearest council office") ||
      // recycling centre
      t.includes("find recycling centre") ||
      t.includes("find a recycling centre") ||
      t.includes("recycling centre near") ||
      t.includes("nearest recycling centre") ||
      t.includes("find recycling center") ||
      // generic "nearest X" / "near me" patterns for council services
      (t.includes("nearest") && (t.includes("library") || t.includes("council office") || t.includes("recycling"))) ||
      (t.includes("near me") && (t.includes("library") || t.includes("council office") || t.includes("recycling")))
    );
  }

  function detectNearbyOpts(text) {
    const t = text.toLowerCase();
    if (t.includes("library"))                              return { category: "library" };
    if (t.includes("council office") || t.includes("office")) return { category: "council_office" };
    if (t.includes("recycling"))                            return { category: "recycling_centre" };
    return { category: "all" };
  }

})();

// ═══════════════════════════════════════════════════════════════════
//  SCHOOL FINDER PANEL  (IIFE)
//  Uses: GET /api/school/find?postcode=&type=
//        GET /api/school/admissions
// ═══════════════════════════════════════════════════════════════════
(function () {
  "use strict";

  // ── DOM refs ─────────────────────────────────────────────────
  const overlay  = document.getElementById("sfOverlay");
  const panel    = document.getElementById("sfPanel");
  const body     = document.getElementById("sfBody");
  const closeBtn = document.getElementById("sfClose");

  if (!overlay || !panel || !body) return;

  // ── State ─────────────────────────────────────────────────────
  let lastFocused  = null;
  let sfState      = {};
  let admissionsCache = null; // lazy-loaded once

  function resetState() {
    sfState = {
      step:       "type",   // type | postcode | results | admissions
      schoolType: "all",    // all | primary | secondary
      postcode:   "",
    };
  }

  // ── Open / close ─────────────────────────────────────────────
  function openPanel(opts) {
    lastFocused = document.activeElement;
    overlay.setAttribute("aria-hidden", "false");
    overlay.classList.add("open");
    document.getElementById("appShell")?.setAttribute("inert", "");
    panel.focus();
    resetState();
    // Allow caller to pre-select school type
    if (opts?.schoolType) sfState.schoolType = opts.schoolType;
    if (opts?.screen === "admissions") {
      goToAdmissionsPanel();
    } else {
      showTypeSelector();
    }
  }

  function closePanel() {
    overlay.setAttribute("aria-hidden", "true");
    overlay.classList.remove("open");
    document.getElementById("appShell")?.removeAttribute("inert");
    if (lastFocused) setTimeout(() => lastFocused.focus(), 0);
  }

  // ── Route to dedicated Admissions panel (preferred) or fallback ─
  function goToAdmissionsPanel() {
    if (typeof window.openAdmissionsPanel === "function") {
      closePanel(); // close School Finder first
      setTimeout(() => window.openAdmissionsPanel(), 30);
    } else {
      showAdmissions(); // fallback to internal screen
    }
  }

  // ── Screen: school type selector ─────────────────────────────
  function showTypeSelector() {
    sfState.step = "type";
    setBody(`
      <div class="fp-intro">
        <p>Find schools in the Bradford district. Choose a school phase to get started.</p>
      </div>

      <div class="fp-type-list">
        <button class="fp-type-btn" data-type="primary" type="button">
          <span class="fp-type-label">Primary schools</span>
          <span class="fp-type-desc">Reception to Year 6 · ages 4–11</span>
        </button>
        <button class="fp-type-btn" data-type="secondary" type="button">
          <span class="fp-type-label">Secondary schools</span>
          <span class="fp-type-desc">Year 7 to Year 13 · ages 11–18</span>
        </button>
        <button class="fp-type-btn" data-type="all" type="button">
          <span class="fp-type-label">All schools</span>
          <span class="fp-type-desc">Show primary and secondary together</span>
        </button>
      </div>

      <div class="sf-divider"></div>

      <button class="fp-link-btn sf-admissions-link" id="sfAdmissionsBtn" type="button">
        📚 View admissions info &amp; deadlines
      </button>
    `);

    body.querySelectorAll(".fp-type-btn").forEach(btn => {
      btn.addEventListener("click", () => {
        sfState.schoolType = btn.dataset.type;
        showPostcodeInput();
      });
    });
    body.querySelector("#sfAdmissionsBtn")?.addEventListener("click", goToAdmissionsPanel);
  }

  // ── Screen: postcode input ────────────────────────────────────
  function showPostcodeInput() {
    sfState.step = "postcode";
    const label = sfState.schoolType === "primary"   ? "primary schools"
                : sfState.schoolType === "secondary" ? "secondary schools"
                :                                      "schools";

    setBody(`
      <div class="fp-intro">
        <p>Enter your postcode to find <strong>${esc(label)}</strong> near you.</p>
      </div>

      <label class="fp-question-text" for="sfPostcodeInput">Your postcode</label>
      <input class="fp-input sf-postcode-input" id="sfPostcodeInput" type="text"
             placeholder="e.g. BD3 8QX" maxlength="8"
             autocomplete="postal-code" aria-required="true"
             style="text-transform:uppercase" />
      <p class="fp-hint">Bradford district postcodes: BD1–BD21, LS29.</p>

      <button class="fp-submit-btn" id="sfSearchBtn" type="button">Search →</button>
      <a href="#" class="fp-cancel-link" id="sfBackLink">← Change phase</a>
    `);

    const input = body.querySelector("#sfPostcodeInput");
    // Auto-uppercase while typing
    input?.addEventListener("input", () => { input.value = input.value.toUpperCase(); });
    input?.focus();

    body.querySelector("#sfSearchBtn")?.addEventListener("click", () => doSearch(input));
    input?.addEventListener("keydown", e => { if (e.key === "Enter") doSearch(input); });
    body.querySelector("#sfBackLink")?.addEventListener("click", e => { e.preventDefault(); showTypeSelector(); });
  }

  // ── Trigger API search ────────────────────────────────────────
  async function doSearch(inputEl) {
    const postcode = inputEl?.value.trim();
    if (!postcode) {
      inputEl?.classList.add("fp-input-error");
      inputEl?.focus();
      return;
    }
    inputEl?.classList.remove("fp-input-error");
    sfState.postcode = postcode;
    await showResults();
  }

  // ── Screen: results ───────────────────────────────────────────
  async function showResults() {
    sfState.step = "results";
    const label = sfState.schoolType === "primary"   ? "primary schools"
                : sfState.schoolType === "secondary" ? "secondary schools"
                :                                      "schools";

    setBody(`<div class="fp-loading">Searching for ${esc(label)} near ${esc(sfState.postcode)}…</div>`);

    try {
      const url = `/api/school/find?postcode=${encodeURIComponent(sfState.postcode)}&type=${encodeURIComponent(sfState.schoolType)}&count=6`;
      const res  = await fetch(url);
      const data = await res.json();

      if (!res.ok || data.error) {
        setBody(`<div class="fp-error">${esc(data.error || "Search failed — please check the postcode and try again.")}</div>`);
        return;
      }
      renderResults(data);
    } catch (_) {
      setBody(`<div class="fp-error">Network error — please check your connection and try again.</div>`);
    }
  }

  function renderResults(data) {
    const schools = data.schools || [];
    const label   = data.schoolType === "primary"   ? "Primary schools"
                  : data.schoolType === "secondary" ? "Secondary schools"
                  :                                   "Schools";

    const noResults = `
      <div class="sf-no-results">
        <p>No ${esc(label.toLowerCase())} found near <strong>${esc(data.postcode)}</strong>.</p>
        <p>Try a different postcode, or contact Bradford School Admissions on <strong>01274 439200</strong>.</p>
        <button class="fp-submit-btn" id="sfRetryBtn" type="button">Try another postcode</button>
      </div>`;

    if (!schools.length) {
      setBody(noResults);
      body.querySelector("#sfRetryBtn")?.addEventListener("click", showPostcodeInput);
      return;
    }

    const cards = schools.map(s => {
      const dist  = s.estimatedDistanceMiles != null
        ? `<span class="sf-card-distance">~${esc(String(s.estimatedDistanceMiles))} mi</span>`
        : "";
      const phone = s.phone
        ? `<div class="sf-card-row"><span class="sf-card-icon">📞</span><a href="tel:${esc(s.phone.replace(/\s/g,''))}">${esc(s.phone)}</a></div>`
        : "";
      const notes = s.notes
        ? `<div class="sf-card-notes">${esc(s.notes)}</div>`
        : "";
      const mapLink = s.mapUrl
        ? `<a class="sf-card-map" href="${esc(s.mapUrl)}" target="_blank" rel="noopener noreferrer" aria-label="View ${esc(s.name)} on Google Maps">View on map ↗</a>`
        : "";
      const webLink = s.website
        ? `<a class="sf-card-web" href="${esc(s.website)}" target="_blank" rel="noopener noreferrer">School website ↗</a>`
        : "";

      return `
        <article class="sf-card" aria-label="${esc(s.name)}">
          <div class="sf-card-header">
            <span class="sf-card-name">${esc(s.name)}</span>
            ${dist}
          </div>
          <div class="sf-card-row"><span class="sf-card-icon">📍</span>${esc(s.address)}</div>
          ${phone}
          ${notes}
          ${(mapLink || webLink) ? `<div class="sf-card-links">${mapLink}${webLink}</div>` : ""}
        </article>`;
    }).join("");

    setBody(`
      <div class="sf-results-header">
        <span>${esc(label)} near <strong>${esc(data.postcode)}</strong></span>
        <span class="sf-results-count">${schools.length} found</span>
      </div>
      <p class="fp-hint" style="margin-bottom:12px">Distances are estimates. Verify with each school directly.</p>

      <div class="sf-card-list">${cards}</div>

      <div class="sf-divider"></div>

      <div class="sf-actions">
        <button class="fp-option-btn sf-action-btn" id="sfNewSearchBtn" type="button">🔍 New search</button>
        <button class="fp-option-btn sf-action-btn" id="sfAdmissionsBtn2" type="button">📚 Admissions info</button>
      </div>
    `);

    body.querySelector("#sfNewSearchBtn")?.addEventListener("click", showPostcodeInput);
    body.querySelector("#sfAdmissionsBtn2")?.addEventListener("click", goToAdmissionsPanel);
  }

  // ── Screen: admissions info ───────────────────────────────────
  async function showAdmissions() {
    sfState.step = "admissions";
    setBody(`<div class="fp-loading">Loading admissions information…</div>`);

    try {
      if (!admissionsCache) {
        const res = await fetch("/api/school/admissions");
        admissionsCache = await res.json();
      }
      renderAdmissions(admissionsCache);
    } catch (_) {
      setBody(`<div class="fp-error">Could not load admissions information.</div>`);
    }
  }

  function renderAdmissions(data) {
    // ── Deadline table ──────────────────────────────────────────
    const deadlineRows = (data.deadlines || []).map(d => `
      <tr>
        <td><strong>${esc(d.phase)}</strong></td>
        <td>${esc(d.opens)}</td>
        <td>${esc(d.closes)}</td>
        <td>${esc(d.offersDate)}</td>
      </tr>`).join("");

    const deadlineTable = deadlineRows ? `
      <div class="sf-section">
        <h4 class="sf-section-title">📅 Key Deadlines</h4>
        <div class="sf-table-wrap">
          <table class="fp-summary-table sf-deadline-table" aria-label="Admissions deadlines">
            <thead>
              <tr>
                <th scope="col">Phase</th>
                <th scope="col">Opens</th>
                <th scope="col">Closes</th>
                <th scope="col">Offers</th>
              </tr>
            </thead>
            <tbody>${deadlineRows}</tbody>
          </table>
        </div>
      </div>` : "";

    // ── Action links ────────────────────────────────────────────
    const linkCards = (data.links || []).map(l => `
      <a class="sf-link-card" href="${esc(l.url)}" target="_blank" rel="noopener noreferrer">
        <span class="sf-link-icon">${esc(l.icon)}</span>
        <span class="sf-link-text">
          <strong>${esc(l.label)}</strong>
          <span>${esc(l.description)}</span>
        </span>
        <span class="sf-link-arrow" aria-hidden="true">↗</span>
      </a>`).join("");

    // ── Contact ─────────────────────────────────────────────────
    const contact = data.contact ? `
      <div class="sf-contact">
        <strong>${esc(data.contact.team)}</strong>
        <a href="tel:${esc((data.contact.phone || "").replace(/\s/g,''))}">${esc(data.contact.phone)}</a>
      </div>` : "";

    setBody(`
      <div class="fp-intro">
        <p>Bradford Council school admissions guidance and application links.</p>
      </div>

      ${deadlineTable}

      <div class="sf-section">
        <h4 class="sf-section-title">🔗 Apply &amp; get help</h4>
        <div class="sf-link-list">${linkCards}</div>
      </div>

      ${contact}
      <div class="sf-divider"></div>

      <div class="sf-actions">
        <button class="fp-option-btn sf-action-btn" id="sfFindSchoolBtn" type="button">🏫 Find schools near me</button>
      </div>
    `);

    body.querySelector("#sfFindSchoolBtn")?.addEventListener("click", showTypeSelector);
  }

  // ── Helpers ───────────────────────────────────────────────────
  function setBody(html) { body.innerHTML = html; }

  function esc(s) {
    return String(s ?? "")
      .replace(/&/g, "&amp;").replace(/</g, "&lt;")
      .replace(/>/g, "&gt;").replace(/"/g, "&quot;");
  }

  // ── Event wiring ──────────────────────────────────────────────
  closeBtn?.addEventListener("click", closePanel);

  overlay.addEventListener("click", e => {
    if (e.target === overlay) closePanel();
  });

  document.addEventListener("keydown", e => {
    if (!overlay.classList.contains("open")) return;
    if (e.key === "Escape") { closePanel(); return; }
    if (e.key === "Tab")    trapFocus(panel, e);
  });

  // ── Expose to global scope ────────────────────────────────────
  window.openSchoolPanel = openPanel;

  // ── Chat message interceptor ──────────────────────────────────
  // Wraps the global sendPreset so school-finder messages open the
  // panel instead of (or alongside) the normal chat flow.
  const _origSendPreset = window.sendPreset;
  window.sendPreset = function (text) {
    if (isSchoolIntent(text)) {
      const opts = detectSchoolOpts(text);
      openPanel(opts);
      return; // swallow — panel handles everything
    }
    _origSendPreset(text);
  };

  // Also intercept the Enter/send flow for typed messages.
  // We hook into the send button's click via a capture listener so we
  // can inspect the input *before* the normal send() fires.
  (function hookSendButton() {
    const btn   = document.getElementById("sendBtn");
    const input = document.getElementById("msg");
    if (!btn || !input) return;

    btn.addEventListener("click", interceptSend, { capture: true });
    input.addEventListener("keydown", e => {
      if (e.key === "Enter" && !e.shiftKey) interceptSend(e);
    }, { capture: true });

    function interceptSend(e) {
      const text = input?.value.trim() || "";
      if (!text || !isSchoolIntent(text)) return; // let normal send() handle it
      e.stopImmediatePropagation();     // block the normal send() listener
      e.preventDefault();
      input.value = "";
      const opts = detectSchoolOpts(text);
      openPanel(opts);
    }
  })();

  function isSchoolIntent(text) {
    const t = text.toLowerCase();
    return (
      t.includes("school finder") ||
      t.includes("find school") ||
      t.includes("schools near") ||
      t.includes("primary school") && (t.includes("find") || t.includes("near") || t.includes("search") || t.includes("show") || t.includes("list")) ||
      t.includes("secondary school") && (t.includes("find") || t.includes("near") || t.includes("search") || t.includes("show") || t.includes("list")) ||
      t.includes("find primary") ||
      t.includes("find secondary") ||
      t.includes("nearby school") ||
      t.includes("local school") && t.includes("find")
    );
  }

  function detectSchoolOpts(text) {
    const t = text.toLowerCase();
    if (t.includes("primary"))   return { schoolType: "primary" };
    if (t.includes("secondary")) return { schoolType: "secondary" };
    return { schoolType: "all" };
  }

})();

// ═══════════════════════════════════════════════════════════════════
//  SCHOOL ADMISSIONS PANEL  (IIFE)
//  Screens: home | apply | in-year | deadlines | starting-school
//  Uses: GET /api/school/admissions  (lazy, cached)
// ═══════════════════════════════════════════════════════════════════
(function () {
  "use strict";

  // ── DOM refs ─────────────────────────────────────────────────
  const overlay  = document.getElementById("saOverlay");
  const panel    = document.getElementById("saPanel");
  const body     = document.getElementById("saBody");
  const closeBtn = document.getElementById("saClose");

  if (!overlay || !panel || !body) return;

  // ── State ─────────────────────────────────────────────────────
  let lastFocused    = null;
  let saScreen       = "home";
  let dataCache      = null; // lazy: { links, deadlines, contact }

  // ── Open / close ─────────────────────────────────────────────
  function openPanel(opts) {
    lastFocused = document.activeElement;
    overlay.setAttribute("aria-hidden", "false");
    overlay.classList.add("open");
    document.getElementById("appShell")?.setAttribute("inert", "");
    panel.focus();
    saScreen = opts?.screen || "home";
    showScreen(saScreen);
  }

  function closePanel() {
    overlay.setAttribute("aria-hidden", "true");
    overlay.classList.remove("open");
    document.getElementById("appShell")?.removeAttribute("inert");
    if (lastFocused) setTimeout(() => lastFocused.focus(), 0);
  }

  // ── Router ────────────────────────────────────────────────────
  function showScreen(screen) {
    switch (screen) {
      case "apply":           return showApply();
      case "in-year":         return showInYear();
      case "deadlines":       return showDeadlines();
      case "starting-school": return showStartingSchool();
      default:                return showHome();
    }
  }

  // ── Fetch admissions data (once) ─────────────────────────────
  async function fetchData() {
    if (dataCache) return dataCache;
    const res  = await fetch("/api/school/admissions");
    dataCache  = await res.json();
    return dataCache;
  }

  // ══════════════════════════════════════════════════════════════
  //  SCREEN: HOME — four topic cards
  // ══════════════════════════════════════════════════════════════
  function showHome() {
    const topics = [
      {
        screen: "apply",
        icon:   "📝",
        title:  "Apply for a school place",
        desc:   "Reception (age 4), Year 7 (age 11), or new to Bradford",
      },
      {
        screen: "in-year",
        icon:   "🔄",
        title:  "In-year transfer",
        desc:   "Changing school outside the normal admissions round",
      },
      {
        screen: "deadlines",
        icon:   "📅",
        title:  "Key deadlines",
        desc:   "Application windows and offer dates for 2025–26",
      },
      {
        screen: "starting-school",
        icon:   "🎒",
        title:  "Starting school",
        desc:   "Reception readiness, free meals, uniform support",
      },
    ];

    const cards = topics.map(t => `
      <button class="sa-topic-card" data-screen="${esc(t.screen)}" type="button"
              aria-label="${esc(t.title)}">
        <span class="sa-topic-icon" aria-hidden="true">${esc(t.icon)}</span>
        <span class="sa-topic-body">
          <span class="sa-topic-title">${esc(t.title)}</span>
          <span class="sa-topic-desc">${esc(t.desc)}</span>
        </span>
        <span class="sa-topic-arrow" aria-hidden="true">
          <svg width="12" height="12" viewBox="0 0 12 12" fill="none">
            <path d="M4 2l4 4-4 4" stroke="currentColor" stroke-width="1.5"
                  stroke-linecap="round" stroke-linejoin="round"/>
          </svg>
        </span>
      </button>`).join("");

    setBody(`
      <div class="fp-intro">
        <p>Bradford Council manages school admissions for all maintained schools. Choose a topic below.</p>
      </div>
      <nav class="sa-topic-list" aria-label="Admissions topics">${cards}</nav>
      <div class="sf-divider"></div>
      <div class="sa-footer-link">
        <a href="https://www.bradford.gov.uk/education-and-skills/school-admissions/"
           target="_blank" rel="noopener noreferrer" class="sa-external-link">
          Bradford Council school admissions ↗
        </a>
        <span class="sa-footer-phone">📞 01274 439200</span>
      </div>
    `);

    body.querySelectorAll(".sa-topic-card").forEach(btn => {
      btn.addEventListener("click", () => {
        saScreen = btn.dataset.screen;
        showScreen(saScreen);
      });
    });
  }

  // ══════════════════════════════════════════════════════════════
  //  SCREEN: APPLY FOR A SCHOOL PLACE
  // ══════════════════════════════════════════════════════════════
  function showApply() {
    const APPLY_URL = "https://www.bradford.gov.uk/education-and-skills/school-admissions/apply-for-a-place-at-one-of-bradford-districts-schools/";

    setBody(`
      <div class="fp-intro">
        <p>Apply online for a school place in Bradford. You can list up to <strong>3 preferences</strong>.</p>
      </div>

      <!-- Primary -->
      <div class="sa-phase-card sa-phase-primary">
        <div class="sa-phase-header">
          <span class="sa-phase-icon" aria-hidden="true">🏫</span>
          <div class="sa-phase-meta">
            <strong class="sa-phase-title">Primary school (Reception)</strong>
            <span class="sa-phase-age">Ages 4–5 · starting September 2026</span>
          </div>
        </div>
        <div class="sa-date-row">
          <div class="sa-date-chip">
            <span class="sa-date-label">Applications open</span>
            <span class="sa-date-value">1 November 2025</span>
          </div>
          <div class="sa-date-chip">
            <span class="sa-date-label">Deadline</span>
            <span class="sa-date-value sa-date-deadline">15 January 2026</span>
          </div>
          <div class="sa-date-chip">
            <span class="sa-date-label">Offers made</span>
            <span class="sa-date-value">16 April 2026</span>
          </div>
        </div>
        <ol class="sa-steps" aria-label="How to apply for primary school">
          <li class="sa-step">Apply online at the Bradford admissions portal</li>
          <li class="sa-step">List up to 3 school preferences in order</li>
          <li class="sa-step">Read each school's admissions criteria (distance, siblings, faith)</li>
        </ol>
        <p class="sa-note">⚠️ Living close to a school does not guarantee a place. Check each school's oversubscription criteria carefully.</p>
      </div>

      <!-- Secondary -->
      <div class="sa-phase-card sa-phase-secondary">
        <div class="sa-phase-header">
          <span class="sa-phase-icon" aria-hidden="true">🏛️</span>
          <div class="sa-phase-meta">
            <strong class="sa-phase-title">Secondary school (Year 7)</strong>
            <span class="sa-phase-age">Ages 11–12 · starting September 2026</span>
          </div>
        </div>
        <div class="sa-date-row">
          <div class="sa-date-chip">
            <span class="sa-date-label">Applications open</span>
            <span class="sa-date-value">1 September 2025</span>
          </div>
          <div class="sa-date-chip">
            <span class="sa-date-label">Deadline</span>
            <span class="sa-date-value sa-date-deadline">31 October 2025</span>
          </div>
          <div class="sa-date-chip">
            <span class="sa-date-label">Offers made</span>
            <span class="sa-date-value">1 March 2026</span>
          </div>
        </div>
        <ol class="sa-steps" aria-label="How to apply for secondary school">
          <li class="sa-step">Apply online at the Bradford admissions portal</li>
          <li class="sa-step">List up to 3 preferences</li>
          <li class="sa-step">Check if any preferred school is selective (grammar or faith)</li>
        </ol>
        <p class="sa-note">ℹ️ Bradford Grammar School is independent — contact them directly about their entrance process.</p>
      </div>

      <a class="sa-cta-btn" href="${esc(APPLY_URL)}" target="_blank" rel="noopener noreferrer">
        Apply online now ↗
      </a>

      <div class="sf-divider"></div>
      <div class="sa-nav-row">
        <button class="fp-option-btn sf-action-btn" id="saBackHome" type="button">← All topics</button>
        <button class="fp-option-btn sf-action-btn" id="saGoDeadlines" type="button">📅 View deadlines</button>
      </div>
    `);

    body.querySelector("#saBackHome")?.addEventListener("click", showHome);
    body.querySelector("#saGoDeadlines")?.addEventListener("click", showDeadlines);
  }

  // ══════════════════════════════════════════════════════════════
  //  SCREEN: IN-YEAR TRANSFER
  // ══════════════════════════════════════════════════════════════
  function showInYear() {
    const APPLY_URL = "https://www.bradford.gov.uk/education-and-skills/school-admissions/apply-for-a-place-at-one-of-bradford-districts-schools/";

    setBody(`
      <div class="fp-intro">
        <p>An <strong>in-year transfer</strong> lets you apply for a school place outside the normal admissions round — for example if your child has moved to Bradford or needs to change school mid-year.</p>
      </div>

      <div class="sa-info-section">
        <h4 class="sa-section-heading">When to use this</h4>
        <ul class="sa-checklist">
          <li class="sa-checklist-item">Your family has moved into the Bradford district</li>
          <li class="sa-checklist-item">Your child needs to change school for welfare or other reasons</li>
          <li class="sa-checklist-item">You are applying at a different time than the main admissions round</li>
        </ul>
      </div>

      <div class="sa-info-section">
        <h4 class="sa-section-heading">How to apply</h4>
        <ol class="sa-steps sa-steps-numbered">
          <li class="sa-step">
            <span class="sa-step-num" aria-hidden="true">1</span>
            <span class="sa-step-text">Complete the in-year application form online</span>
          </li>
          <li class="sa-step">
            <span class="sa-step-num" aria-hidden="true">2</span>
            <span class="sa-step-text">Contact your preferred school directly to check for available places</span>
          </li>
          <li class="sa-step">
            <span class="sa-step-num" aria-hidden="true">3</span>
            <span class="sa-step-text">If no places are available, Bradford Council will coordinate a suitable placement</span>
          </li>
        </ol>
      </div>

      <div class="sa-contact-box">
        <span class="sa-contact-label">Bradford School Admissions</span>
        <a class="sa-contact-phone" href="tel:01274439200">📞 01274 439200</a>
      </div>

      <a class="sa-cta-btn" href="${esc(APPLY_URL)}" target="_blank" rel="noopener noreferrer">
        Start in-year application ↗
      </a>

      <div class="sf-divider"></div>
      <button class="fp-option-btn sf-action-btn" id="saBackHome2" type="button">← All topics</button>
    `);

    body.querySelector("#saBackHome2")?.addEventListener("click", showHome);
  }

  // ══════════════════════════════════════════════════════════════
  //  SCREEN: DEADLINES  (data from /api/school/admissions)
  // ══════════════════════════════════════════════════════════════
  async function showDeadlines() {
    setBody(`<div class="fp-loading">Loading deadline information…</div>`);
    try {
      const data = await fetchData();
      renderDeadlines(data);
    } catch (_) {
      setBody(`
        <div class="ns-error-state">
          <div class="ns-error-icon">⚠️</div>
          <p>Could not load deadline information. Please try again.</p>
          <button class="fp-submit-btn" id="saRetry" type="button">Retry</button>
        </div>`);
      body.querySelector("#saRetry")?.addEventListener("click", showDeadlines);
    }
  }

  function renderDeadlines(data) {
    const APPLY_URL = "https://www.bradford.gov.uk/education-and-skills/school-admissions/apply-for-a-place-at-one-of-bradford-districts-schools/";
    const deadlines = data.deadlines || [];

    const cards = deadlines.map(d => `
      <div class="sa-deadline-card">
        <div class="sa-deadline-phase">${esc(d.phase)}</div>
        <div class="sa-deadline-dates">
          <div class="sa-dl-row">
            <span class="sa-dl-label">Applications open</span>
            <span class="sa-dl-value">${esc(d.opens)}</span>
          </div>
          <div class="sa-dl-row sa-dl-important">
            <span class="sa-dl-label">⏰ Deadline</span>
            <span class="sa-dl-value">${esc(d.closes)}</span>
          </div>
          <div class="sa-dl-row">
            <span class="sa-dl-label">Offers made</span>
            <span class="sa-dl-value">${esc(d.offersDate)}</span>
          </div>
        </div>
      </div>`).join("");

    setBody(`
      <div class="fp-intro">
        <p>Key dates for the <strong>2025–26 Bradford school admissions</strong> round.</p>
      </div>

      <div class="sa-deadline-list" aria-label="Admissions deadlines">${cards}</div>

      <div class="sa-info-section">
        <h4 class="sa-section-heading">What happens after the deadline?</h4>
        <ul class="sa-checklist">
          <li class="sa-checklist-item">Late applications are considered after all on-time applications</li>
          <li class="sa-checklist-item">You will receive an offer letter or email on the national offer day</li>
          <li class="sa-checklist-item">You can appeal if you do not get your preferred school</li>
        </ul>
      </div>

      <div class="sa-info-section">
        <h4 class="sa-section-heading">Missed the deadline?</h4>
        <p class="sa-body-text">Contact the School Admissions team as soon as possible. Late applications are still processed, but after all on-time applications have been considered.</p>
        <div class="sa-contact-box" style="margin-top:10px">
          <span class="sa-contact-label">Bradford School Admissions</span>
          <a class="sa-contact-phone" href="tel:01274439200">📞 01274 439200</a>
        </div>
      </div>

      <a class="sa-cta-btn" href="${esc(APPLY_URL)}" target="_blank" rel="noopener noreferrer">
        Apply online now ↗
      </a>

      <div class="sf-divider"></div>
      <div class="sa-nav-row">
        <button class="fp-option-btn sf-action-btn" id="saBackHome3" type="button">← All topics</button>
        <button class="fp-option-btn sf-action-btn" id="saGoApply" type="button">📝 How to apply</button>
      </div>
    `);

    body.querySelector("#saBackHome3")?.addEventListener("click", showHome);
    body.querySelector("#saGoApply")?.addEventListener("click", showApply);
  }

  // ══════════════════════════════════════════════════════════════
  //  SCREEN: STARTING SCHOOL
  // ══════════════════════════════════════════════════════════════
  function showStartingSchool() {
    const APPLY_URL    = "https://www.bradford.gov.uk/education-and-skills/school-admissions/apply-for-a-place-at-one-of-bradford-districts-schools/";
    const MEALS_URL    = "https://www.bradford.gov.uk/education-and-skills/free-school-meals/";
    const ADMISSIONS_URL = "https://www.bradford.gov.uk/education-and-skills/school-admissions/";

    setBody(`
      <div class="fp-intro">
        <p>Everything you need to know about getting your child ready for their first term at a Bradford school.</p>
      </div>

      <div class="sa-info-section">
        <h4 class="sa-section-heading">🗓️ When can my child start school?</h4>
        <p class="sa-body-text">Children in England must start school in the September after their 4th birthday (Reception year). In Bradford you apply during the autumn of the year before.</p>
        <div class="sa-age-guide">
          <div class="sa-age-row">
            <span class="sa-age-badge">Age 4–5</span>
            <span>Reception — apply by <strong>15 January 2026</strong></span>
          </div>
          <div class="sa-age-row">
            <span class="sa-age-badge">Age 11–12</span>
            <span>Year 7 — apply by <strong>31 October 2025</strong></span>
          </div>
        </div>
      </div>

      <div class="sa-info-section">
        <h4 class="sa-section-heading">📋 What to prepare</h4>
        <ul class="sa-checklist">
          <li class="sa-checklist-item">Proof of address (utility bill or tenancy agreement dated within 3 months)</li>
          <li class="sa-checklist-item">Child's birth certificate or passport</li>
          <li class="sa-checklist-item">Baptism certificate (if applying for a faith school)</li>
          <li class="sa-checklist-item">Any Education, Health and Care (EHC) plan documentation</li>
        </ul>
      </div>

      <div class="sa-info-section">
        <h4 class="sa-section-heading">🍽️ Free school meals</h4>
        <p class="sa-body-text">Your child may be entitled to free school meals if your household receives:</p>
        <ul class="sa-checklist">
          <li class="sa-checklist-item">Universal Credit (with net earnings under £7,400/year)</li>
          <li class="sa-checklist-item">Income Support or income-related JSA / ESA</li>
          <li class="sa-checklist-item">Child Tax Credit (without Working Tax Credit)</li>
        </ul>
        <p class="sa-body-text" style="margin-top:8px">All children in Reception, Year 1, and Year 2 are entitled to <strong>Universal Free School Meals</strong> regardless of household income.</p>
        <a class="sa-inline-link" href="${esc(MEALS_URL)}" target="_blank" rel="noopener noreferrer">Apply for free school meals ↗</a>
      </div>

      <div class="sa-info-section">
        <h4 class="sa-section-heading">👕 Uniform support</h4>
        <p class="sa-body-text">Bradford Council offers a school uniform grant for eligible families. Contact the School Admissions team or your school office for details.</p>
        <div class="sa-contact-box">
          <span class="sa-contact-label">Bradford School Admissions</span>
          <a class="sa-contact-phone" href="tel:01274439200">📞 01274 439200</a>
        </div>
      </div>

      <a class="sa-cta-btn" href="${esc(APPLY_URL)}" target="_blank" rel="noopener noreferrer">
        Apply for a school place ↗
      </a>

      <div class="sf-divider"></div>
      <div class="sa-nav-row">
        <button class="fp-option-btn sf-action-btn" id="saBackHome4" type="button">← All topics</button>
        <button class="fp-option-btn sf-action-btn" id="saGoSchools" type="button">🏫 Find schools near me</button>
      </div>
    `);

    body.querySelector("#saBackHome4")?.addEventListener("click", showHome);
    body.querySelector("#saGoSchools")?.addEventListener("click", () => {
      closePanel();
      setTimeout(() => window.openSchoolPanel?.(), 30);
    });
  }

  // ── Helpers ───────────────────────────────────────────────────
  function setBody(html) { body.innerHTML = html; }

  function esc(s) {
    return String(s ?? "")
      .replace(/&/g, "&amp;").replace(/</g, "&lt;")
      .replace(/>/g, "&gt;").replace(/"/g, "&quot;");
  }

  // ── Event wiring ──────────────────────────────────────────────
  closeBtn?.addEventListener("click", closePanel);

  overlay.addEventListener("click", e => {
    if (e.target === overlay) closePanel();
  });

  document.addEventListener("keydown", e => {
    if (!overlay.classList.contains("open")) return;
    if (e.key === "Escape") { closePanel(); return; }
    if (e.key === "Tab")    trapFocus(panel, e);
  });

  // ── Expose to global scope ────────────────────────────────────
  window.openAdmissionsPanel = openPanel;

  // ── sendPreset wrapper ────────────────────────────────────────
  const _origSendPreset = window.sendPreset;
  window.sendPreset = function (text) {
    if (isAdmissionsIntent(text)) {
      openPanel(detectAdmissionsOpts(text));
      return;
    }
    _origSendPreset(text);
  };

  // ── Capture-phase send button / Enter interceptor ─────────────
  (function hookSend() {
    const btn   = document.getElementById("sendBtn");
    const input = document.getElementById("msg");
    if (!btn || !input) return;

    btn.addEventListener("click", intercept, { capture: true });
    input.addEventListener("keydown", e => {
      if (e.key === "Enter" && !e.shiftKey) intercept(e);
    }, { capture: true });

    function intercept(e) {
      const text = (input?.value || "").trim();
      if (!text || !isAdmissionsIntent(text)) return;
      e.stopImmediatePropagation();
      e.preventDefault();
      input.value = "";
      openPanel(detectAdmissionsOpts(text));
    }
  })();

  // ── Intent detection ──────────────────────────────────────────
  function isAdmissionsIntent(text) {
    const t = text.toLowerCase();
    return (
      // school admissions (general)
      t === "school admissions" ||
      t.includes("school admissions") ||
      // applying for school place
      t.includes("apply for a school place") ||
      t.includes("apply for school place") ||
      t.includes("how do i apply for a school") ||
      t.includes("apply for reception") ||
      t.includes("year 7 application") ||
      t.includes("apply for year 7") ||
      t.includes("school place") && (t.includes("apply") || t.includes("how") || t.includes("get")) ||
      // in-year transfer
      t.includes("in-year transfer") ||
      t.includes("in year transfer") ||
      t.includes("in-year") ||
      t.includes("transfer school") ||
      // deadlines
      t.includes("school deadline") ||
      t.includes("admissions deadline") ||
      (t.includes("deadline") && (t.includes("school") || t.includes("primary") || t.includes("secondary"))) ||
      t.includes("when are the deadlines") ||
      t.includes("when is the deadline") ||
      // starting school
      t.includes("starting school") ||
      t.includes("start school") ||
      t.includes("starting reception") ||
      t.includes("free school meals") && !t.includes("find") ||
      // appeals
      t.includes("school appeal") ||
      t.includes("appeal school")
    );
  }

  function detectAdmissionsOpts(text) {
    const t = text.toLowerCase();
    if (t.includes("in-year") || t.includes("in year") || t.includes("transfer school"))
      return { screen: "in-year" };
    if (t.includes("deadline") || t.includes("when are"))
      return { screen: "deadlines" };
    if (t.includes("starting school") || t.includes("start school") ||
        t.includes("starting reception") || t.includes("free school meals"))
      return { screen: "starting-school" };
    if (t.includes("apply") || t.includes("school place") || t.includes("reception") ||
        t.includes("year 7") || t.includes("appeal"))
      return { screen: "apply" };
    return { screen: "home" };
  }

})();

// ═══════════════════════════════════════════════════════════════════
// COUNCIL TAX PAYMENT CALCULATOR PANEL
// ═══════════════════════════════════════════════════════════════════
(function () {
  "use strict";

  // ── Band data (mirrors CouncilTaxCalculatorService) ──────────────
  const BANDS = [
    { band: "A", annual: 1234.56 },
    { band: "B", annual: 1440.32 },
    { band: "C", annual: 1646.08 },
    { band: "D", annual: 1851.84 },
    { band: "E", annual: 2263.36 },
    { band: "F", annual: 2674.88 },
    { band: "G", annual: 3086.40 },
    { band: "H", annual: 3703.68 },
  ];
  BANDS.forEach(b => { b.monthly = Math.round((b.annual / 10) * 100) / 100; });

  // ── State ─────────────────────────────────────────────────────────
  let monthlyBill = 0;
  let missedMonths = 0;
  let lastFocused = null;

  // ── DOM refs ──────────────────────────────────────────────────────
  const overlay  = document.getElementById("ctOverlay");
  const panel    = document.getElementById("ctPanel");
  const body     = document.getElementById("ctBody");
  const closeBtn = document.getElementById("ctClose");
  const appShell = document.getElementById("appShell");

  // ── Open / close ─────────────────────────────────────────────────
 function openPanel() {
  monthlyBill = 0;
  missedMonths = 0;
  lastFocused = document.activeElement;
  overlay.setAttribute("aria-hidden", "false");
  overlay.classList.add("open");
  appShell && appShell.setAttribute("inert", "");
  panel.removeAttribute("inert");
  showBillEntry();
  requestAnimationFrame(() => panel.focus());
}

function closePanel() {
  overlay.setAttribute("aria-hidden", "true");
  overlay.classList.remove("open");
  appShell && appShell.removeAttribute("inert");
  panel.setAttribute("inert", "");
  if (lastFocused && typeof lastFocused.focus === "function") {
    lastFocused.focus();
  }
}

  closeBtn.addEventListener("click", closePanel);
  overlay.addEventListener("click", e => { if (e.target === overlay) closePanel(); });
  overlay.addEventListener("keydown", e => { if (e.key === "Escape") closePanel(); });
  panel.addEventListener("keydown", e => trapFocus(panel, e));

  // ── Screen 1: Bill entry ─────────────────────────────────────────
  function showBillEntry() {
    const bandOptions = BANDS.map(b =>
      `<button class="ct-band-btn fp-option-btn" data-band="${b.band}" type="button"
         aria-label="Band ${b.band} – £${b.monthly.toFixed(2)} per month">
         <span class="ct-band-letter">${b.band}</span>
         <span class="ct-band-amount">£${b.monthly.toFixed(2)}<small>/mo</small></span>
       </button>`
    ).join("");

    body.innerHTML = `
      <div class="fp-intro">
        <p>Let's work out your Council Tax position. First, tell me your monthly payment.</p>
      </div>
      <div class="ct-entry-tabs" role="tablist" aria-label="How to enter your bill">
        <button class="ct-tab ct-tab--active" id="ctTabAmount" role="tab" aria-selected="true"
                aria-controls="ctPaneAmount" type="button">Enter amount</button>
        <button class="ct-tab" id="ctTabBand" role="tab" aria-selected="false"
                aria-controls="ctPaneBand" type="button">Select band</button>
      </div>

      <div id="ctPaneAmount" role="tabpanel" aria-labelledby="ctTabAmount">
        <div class="ct-amount-row">
          <span class="ct-currency">£</span>
          <input class="fp-input ct-amount-input" id="ctAmountInput" type="number"
                 min="1" max="5000" step="0.01" placeholder="e.g. 185.18"
                 aria-label="Monthly Council Tax amount in pounds" />
        </div>
        <p class="fp-hint">Check your latest Council Tax bill or Direct Debit letter.</p>
      </div>

      <div id="ctPaneBand" role="tabpanel" aria-labelledby="ctTabBand" hidden>
        <p class="fp-hint">Choose the band shown on your Council Tax bill:</p>
        <div class="ct-band-grid">${bandOptions}</div>
        <p class="ct-band-unknown">
          <a href="#" id="ctDontKnowBand" class="fp-cancel-link">I don't know my band</a>
        </p>
      </div>

      <div class="ct-selected-preview" id="ctSelectedPreview" aria-live="polite"></div>

      <button class="fp-submit-btn" id="ctBillNext" type="button" disabled>
        Next: Missed payments →
      </button>
    `;

    const tabAmount  = body.querySelector("#ctTabAmount");
    const tabBand    = body.querySelector("#ctTabBand");
    const paneAmount = body.querySelector("#ctPaneAmount");
    const paneBand   = body.querySelector("#ctPaneBand");
    const amountInput = body.querySelector("#ctAmountInput");
    const nextBtn    = body.querySelector("#ctBillNext");
    const preview    = body.querySelector("#ctSelectedPreview");

    // Tab switching
    tabAmount.addEventListener("click", () => {
      tabAmount.classList.add("ct-tab--active");
      tabAmount.setAttribute("aria-selected", "true");
      tabBand.classList.remove("ct-tab--active");
      tabBand.setAttribute("aria-selected", "false");
      paneAmount.removeAttribute("hidden");
      paneBand.setAttribute("hidden", "");
      monthlyBill = 0;
      preview.innerHTML = "";
      nextBtn.disabled = true;
      amountInput.focus();
    });

    tabBand.addEventListener("click", () => {
      tabBand.classList.add("ct-tab--active");
      tabBand.setAttribute("aria-selected", "true");
      tabAmount.classList.remove("ct-tab--active");
      tabAmount.setAttribute("aria-selected", "false");
      paneBand.removeAttribute("hidden");
      paneAmount.setAttribute("hidden", "");
      monthlyBill = 0;
      nextBtn.disabled = true;
    });

    // Amount input validation
    amountInput.addEventListener("input", () => {
      const v = parseFloat(amountInput.value);
      if (v > 0 && v <= 5000) {
        monthlyBill = Math.round(v * 100) / 100;
        preview.innerHTML = `<span class="ct-preview-pill">Monthly bill: <strong>£${monthlyBill.toFixed(2)}</strong></span>`;
        nextBtn.disabled = false;
      } else {
        monthlyBill = 0;
        preview.innerHTML = "";
        nextBtn.disabled = true;
      }
    });

    amountInput.addEventListener("keydown", e => {
      if (e.key === "Enter" && !nextBtn.disabled) nextBtn.click();
    });

    // Band buttons
    paneBand.querySelectorAll(".ct-band-btn").forEach(btn => {
      btn.addEventListener("click", () => {
        paneBand.querySelectorAll(".ct-band-btn").forEach(b => b.classList.remove("ct-band-btn--selected"));
        btn.classList.add("ct-band-btn--selected");
        const bandName = btn.dataset.band;
        const found    = BANDS.find(b => b.band === bandName);
        if (found) {
          monthlyBill = found.monthly;
          preview.innerHTML = `<span class="ct-preview-pill">Band ${found.band} — <strong>£${found.monthly.toFixed(2)}/mo</strong> (£${found.annual.toFixed(2)}/yr)</span>`;
          nextBtn.disabled = false;
        }
      });
    });

    // Don't know band → switch to amount tab
    body.querySelector("#ctDontKnowBand").addEventListener("click", e => {
      e.preventDefault();
      tabAmount.click();
    });

    nextBtn.addEventListener("click", () => {
      if (monthlyBill > 0) showMissedMonths();
    });
  }

  // ── Screen 2: Missed months ───────────────────────────────────────
  function showMissedMonths() {
    body.innerHTML = `
      <div class="fp-intro">
        <p>How many months of Council Tax have you missed?</p>
      </div>
      <div class="ct-missed-grid">
        <button class="ct-missed-btn fp-option-btn" data-months="1" type="button">1 month</button>
        <button class="ct-missed-btn fp-option-btn" data-months="2" type="button">2 months</button>
        <button class="ct-missed-btn fp-option-btn" data-months="3" type="button">3 months</button>
        <button class="ct-missed-btn fp-option-btn" data-months="6" type="button">6 months</button>
      </div>
      <div class="ct-missed-custom">
        <label class="fp-label" for="ctMissedInput">Or enter a number:</label>
        <div class="ct-amount-row">
          <input class="fp-input ct-amount-input" id="ctMissedInput" type="number"
                 min="1" max="24" step="1" placeholder="e.g. 4"
                 aria-label="Number of months missed" />
          <span class="ct-months-suffix">months</span>
        </div>
      </div>
      <div class="ct-selected-preview" id="ctMissedPreview" aria-live="polite"></div>
      <div class="ct-nav-row">
        <a href="#" class="fp-cancel-link" id="ctBackToBill">← Back</a>
        <button class="fp-submit-btn ct-submit-right" id="ctMissedNext" type="button" disabled>
          See my plan →
        </button>
      </div>
    `;

    const missedBtns   = body.querySelectorAll(".ct-missed-btn");
    const missedInput  = body.querySelector("#ctMissedInput");
    const nextBtn      = body.querySelector("#ctMissedNext");
    const preview      = body.querySelector("#ctMissedPreview");

    function selectMonths(n) {
      missedMonths = n;
      missedBtns.forEach(b => b.classList.remove("ct-missed-btn--selected"));
      const found = Array.from(missedBtns).find(b => parseInt(b.dataset.months) === n);
      if (found) found.classList.add("ct-missed-btn--selected");
      const arrears = Math.round(monthlyBill * n * 100) / 100;
      preview.innerHTML = `<span class="ct-preview-pill">${n} month${n !== 1 ? "s" : ""} × £${monthlyBill.toFixed(2)} = <strong>£${arrears.toFixed(2)} arrears</strong></span>`;
      nextBtn.disabled = false;
    }

    missedBtns.forEach(btn => {
      btn.addEventListener("click", () => {
        missedInput.value = "";
        selectMonths(parseInt(btn.dataset.months));
      });
    });

    missedInput.addEventListener("input", () => {
      const v = parseInt(missedInput.value);
      if (v >= 1 && v <= 24) {
        missedBtns.forEach(b => b.classList.remove("ct-missed-btn--selected"));
        selectMonths(v);
      } else {
        missedMonths = 0;
        preview.innerHTML = "";
        nextBtn.disabled = true;
      }
    });

    missedInput.addEventListener("keydown", e => {
      if (e.key === "Enter" && !nextBtn.disabled) nextBtn.click();
    });

    body.querySelector("#ctBackToBill").addEventListener("click", e => {
      e.preventDefault();
      showBillEntry();
    });

    nextBtn.addEventListener("click", () => {
      if (missedMonths > 0 && monthlyBill > 0) showResults();
    });
  }

  // ── Screen 3: Results ─────────────────────────────────────────────
  function showResults() {
    const arrears   = Math.round(monthlyBill * missedMonths * 100) / 100;
    const catchUp3  = Math.round((arrears / 3 + monthlyBill) * 100) / 100;
    const catchUp6  = Math.round((arrears / 6 + monthlyBill) * 100) / 100;

    const nextSteps = [
      "Contact Bradford Council before the debt increases further",
      "Check if you qualify for Council Tax Support (reduction)",
      "Set up a payment plan via Direct Debit to avoid further arrears",
      "If you've received a court summons, act immediately",
    ];

    body.innerHTML = `
      <div class="ct-results-header">
        <svg width="20" height="20" viewBox="0 0 20 20" fill="none" aria-hidden="true">
          <rect x="2" y="5" width="16" height="11" rx="2" stroke="currentColor" stroke-width="1.5"/>
          <path d="M6 5V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v1" stroke="currentColor" stroke-width="1.4"/>
          <circle cx="10" cy="10.5" r="2" stroke="currentColor" stroke-width="1.3"/>
        </svg>
        <h2 class="ct-results-title">Your Payment Summary</h2>
      </div>

      <div class="ct-summary-card">
        <div class="ct-summary-row">
          <span class="ct-summary-label">Monthly payment</span>
          <span class="ct-summary-value">£${monthlyBill.toFixed(2)}</span>
        </div>
        <div class="ct-summary-row">
          <span class="ct-summary-label">Months missed</span>
          <span class="ct-summary-value">${missedMonths} month${missedMonths !== 1 ? "s" : ""}</span>
        </div>
        <div class="ct-summary-row ct-summary-row--total">
          <span class="ct-summary-label">Total arrears</span>
          <span class="ct-summary-value ct-arrears-amount">£${arrears.toFixed(2)}</span>
        </div>
      </div>

      <h3 class="ct-section-title">Catch-up Plans</h3>
      <div class="ct-plans-grid">
        <div class="ct-plan-card ct-plan-card--3">
          <div class="ct-plan-badge">3-month</div>
          <div class="ct-plan-amount">£${catchUp3.toFixed(2)}<span>/mo</span></div>
          <p class="ct-plan-desc">Pay off arrears over 3 months while staying current</p>
          <div class="ct-plan-breakdown">
            Normal: £${monthlyBill.toFixed(2)} + extra £${(arrears / 3).toFixed(2)}/mo
          </div>
        </div>
        <div class="ct-plan-card ct-plan-card--6">
          <div class="ct-plan-badge">6-month</div>
          <div class="ct-plan-amount">£${catchUp6.toFixed(2)}<span>/mo</span></div>
          <p class="ct-plan-desc">More manageable spread over 6 months</p>
          <div class="ct-plan-breakdown">
            Normal: £${monthlyBill.toFixed(2)} + extra £${(arrears / 6).toFixed(2)}/mo
          </div>
        </div>
      </div>

      <h3 class="ct-section-title">Recommended Next Steps</h3>
      <ul class="ct-steps-list">
        ${nextSteps.map(s => `<li class="ct-step-item"><span class="ct-step-check" aria-hidden="true">✓</span>${s}</li>`).join("")}
      </ul>

      <div class="ct-actions">
        <a class="ct-cta-btn ct-cta-btn--primary"
           href="https://www.bradford.gov.uk/council-tax/council-tax-support/apply-for-council-tax-support/"
           target="_blank" rel="noopener noreferrer">
          <svg width="14" height="14" viewBox="0 0 14 14" fill="none" aria-hidden="true">
            <path d="M2 7h10M7 2l5 5-5 5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>
          </svg>
          Apply for Council Tax Support
        </a>
        <a class="ct-cta-btn ct-cta-btn--secondary"
           href="https://www.bradford.gov.uk/council-tax/paying-your-council-tax/set-up-a-direct-debit-for-council-tax/"
           target="_blank" rel="noopener noreferrer">
          <svg width="14" height="14" viewBox="0 0 14 14" fill="none" aria-hidden="true">
            <rect x="1" y="3" width="12" height="8" rx="1.5" stroke="currentColor" stroke-width="1.3"/>
            <path d="M1 6h12" stroke="currentColor" stroke-width="1.2"/>
          </svg>
          Set Up Direct Debit
        </a>
        <a class="ct-cta-btn ct-cta-btn--urgent"
           href="https://www.bradford.gov.uk/council-tax/council-tax-arrears/"
           target="_blank" rel="noopener noreferrer">
          <svg width="14" height="14" viewBox="0 0 14 14" fill="none" aria-hidden="true">
            <path d="M7 1.5l5.5 10H1.5L7 1.5Z" stroke="currentColor" stroke-width="1.3" stroke-linejoin="round"/>
            <path d="M7 6v3M7 10.5v.5" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"/>
          </svg>
          Speak to Someone About Arrears
        </a>
      </div>

      <div class="ct-results-footer">
        <button class="fp-cancel-link" id="ctRecalculate" type="button">← Recalculate</button>
        <button class="fp-cancel-link" id="ctDone" type="button">Done</button>
      </div>
    `;

    body.querySelector("#ctRecalculate").addEventListener("click", () => showBillEntry());
    body.querySelector("#ctDone").addEventListener("click", closePanel);
  }

  // ── Intent detection ──────────────────────────────────────────────
  function isCtCalculatorIntent(text) {
    const t = text.toLowerCase().trim();
    return (
      t.includes("council tax arrears") ||
      t.includes("can't afford my council tax") ||
      t.includes("cannot afford my council tax") ||
      t.includes("cant afford my council tax") ||
      t.includes("missed council tax") ||
      t.includes("council tax payment plan") ||
      t.includes("payment calculator") ||
      t.includes("council tax calculator") ||
      t.includes("council tax payment calculator") ||
      t.includes("behind on council tax") ||
      t.includes("council tax debt") ||
      t.includes("late council tax") ||
      (t.includes("council tax") && t.includes("arrears")) ||
      (t.includes("council tax") && t.includes("missed")) ||
      (t.includes("council tax") && t.includes("can't afford")) ||
      (t.includes("council tax") && t.includes("cannot afford")) ||
      (t.includes("council tax") && t.includes("payment plan")) ||
      (t.includes("council tax") && t.includes("catch up"))
    );
  }

  // ── Wire chat interceptor ─────────────────────────────────────────
  // ── Wire chat interceptor ─────────────────────────────────────────
const _prevSendPreset = window.sendPreset;
window.sendPreset = function (text) {
  if (isCtCalculatorIntent(text)) {
    openPanel();
    return;
  }
  if (typeof _prevSendPreset === "function") _prevSendPreset(text);
};

// Capture-phase hook on send button + Enter key
if (sendBtn) {
  sendBtn.addEventListener("click", function ctCapture(e) {
    const raw = msgInput ? msgInput.value.trim() : "";
    if (!raw || !isCtCalculatorIntent(raw)) return;

    e.preventDefault();
    e.stopImmediatePropagation();
    msgInput.value = "";
    openPanel();
  }, { capture: true });
}

if (msgInput) {
  msgInput.addEventListener("keydown", function ctKeyCapture(e) {
    if (e.key !== "Enter" || e.shiftKey) return;

    const raw = msgInput.value.trim();
    if (!raw || !isCtCalculatorIntent(raw)) return;

    e.preventDefault();
    e.stopImmediatePropagation();
    msgInput.value = "";
    openPanel();
  }, { capture: true });
}

  // Capture-phase hook on the send button
  // Expose globally
  window.openCtPanel = openPanel;

})();

// ═══════════════════════════════════════════════════════════════════
// HOUSING SUPPORT PANEL
// Mirrors HousingNavigatorService.cs decision-tree logic client-side.
// ═══════════════════════════════════════════════════════════════════
(function () {
  "use strict";

  // ── DOM refs ──────────────────────────────────────────────────────
  const overlay  = document.getElementById("hsOverlay");
  const panel    = document.getElementById("hsPanel");
  const body     = document.getElementById("hsBody");
  const closeBtn = document.getElementById("hsClose");
  const appShell = document.getElementById("appShell");

  // ── State ─────────────────────────────────────────────────────────
  let lastFocused   = null;
  let routeHistory  = [];   // stack of body.innerHTML for back navigation
  let _bypassIntercept = false;

  // ── Open / close ──────────────────────────────────────────────────
  function openPanel(opts) {
    opts = opts || {};
    lastFocused  = document.activeElement;
    routeHistory = [];
    overlay.removeAttribute("aria-hidden");
    overlay.classList.add("open");
    appShell && appShell.setAttribute("inert", "");
    panel.removeAttribute("inert");
    if (opts.route) {
      showRoute(opts.route);
    } else {
      showHome();
    }
    requestAnimationFrame(() => panel.focus());
  }

  function closePanel() {
    overlay.setAttribute("aria-hidden", "true");
    overlay.classList.remove("open");
    appShell && appShell.removeAttribute("inert");
    panel.setAttribute("inert", "");
    if (lastFocused && typeof lastFocused.focus === "function") {
      lastFocused.focus();
    }
  }

  closeBtn.addEventListener("click", closePanel);
  overlay.addEventListener("click", e => { if (e.target === overlay) closePanel(); });
  overlay.addEventListener("keydown", e => { if (e.key === "Escape") closePanel(); });
  panel.addEventListener("keydown", e => trapFocus(panel, e));

  // ── Navigation helpers ────────────────────────────────────────────
  function navigate(renderFn) {
    routeHistory.push(body.innerHTML);
    renderFn();
    requestAnimationFrame(() => panel.focus());
  }

  function goBack() {
    if (routeHistory.length > 0) {
      body.innerHTML = routeHistory.pop();
      wireListeners();
    } else {
      showHome();
    }
    requestAnimationFrame(() => panel.focus());
  }

  // Wire all data-* action listeners after any innerHTML update
  function wireListeners() {
    body.querySelectorAll("[data-back]").forEach(el => {
      el.addEventListener("click", e => { e.preventDefault(); goBack(); });
    });
    body.querySelectorAll("[data-route]").forEach(el => {
      el.addEventListener("click", () => navigate(() => showRoute(el.dataset.route)));
    });
    body.querySelectorAll("[data-subroute]").forEach(el => {
      el.addEventListener("click", () => navigate(() => showSubRoute(el.dataset.subroute)));
    });
  }

  // ── Home screen ───────────────────────────────────────────────────
  function showHome() {
    routeHistory = [];
    const cats = [
      { route: "rough_sleeping",   icon: "🚨", title: "Homeless Tonight",    desc: "Nowhere to sleep, rough sleeping",  urgent: true  },
      { route: "eviction",         icon: "⚠️", title: "Eviction Risk",        desc: "Notice to leave, at risk of losing home" },
      { route: "domestic_abuse",   icon: "💜", title: "Domestic Abuse",       desc: "Safety & emergency housing help",  urgent: true  },
      { route: "temp_accommodation",icon: "🏨", title: "Emergency Housing",   desc: "Temporary or emergency accommodation" },
      { route: "affordability",    icon: "💰", title: "Can't Afford Rent",    desc: "Housing benefit, rent arrears" },
      { route: "repairs",          icon: "🔧", title: "Property Repairs",     desc: "Council repairs, damp, mould" },
      { route: "find_home",        icon: "🏡", title: "Find a Home",          desc: "Housing register, council house" },
      { route: "general",          icon: "ℹ️",  title: "General Advice",       desc: "Any other housing question" },
    ];

    const tiles = cats.map(c =>
      `<button class="hs-cat-card${c.urgent ? " hs-cat-card--urgent" : ""}"
               data-route="${c.route}" type="button">
         <span class="hs-cat-icon" aria-hidden="true">${c.icon}</span>
         <span class="hs-cat-body">
           <strong class="hs-cat-title">${c.title}</strong>
           <span class="hs-cat-desc">${c.desc}</span>
         </span>
         <svg class="hs-cat-arrow" width="11" height="11" viewBox="0 0 12 12" fill="none" aria-hidden="true">
           <path d="M4 2l4 4-4 4" stroke="currentColor" stroke-width="1.5"
                 stroke-linecap="round" stroke-linejoin="round"/>
         </svg>
       </button>`
    ).join("");

    body.innerHTML =
      `<div class="fp-intro"><p>What type of housing help do you need?</p></div>
       <div class="hs-cat-grid">${tiles}</div>
       <div class="hs-footer-contact">
         <span class="hs-footer-label">Housing Options Team</span>
         <a href="tel:01274435999" class="hs-footer-phone">📞 01274 435999</a>
       </div>`;

    wireListeners();
  }

  // ── Route screen dispatcher ───────────────────────────────────────
  function showRoute(route) {
    const fn = routeRenderers[route] || routeRenderers.general;
    body.innerHTML = fn();
    wireListeners();
  }

  // ── Sub-route dispatcher ──────────────────────────────────────────
  function showSubRoute(key) {
    const fn = subRouteRenderers[key];
    if (fn) { body.innerHTML = fn(); wireListeners(); }
  }

  // ── Template helpers ──────────────────────────────────────────────
  function backBtn(label) {
    return `<button class="hs-back-btn" data-back="1" type="button">← ${label || "Back"}</button>`;
  }

  function phoneCard(label, num, note) {
    const tel = num.replace(/\s/g, "");
    return `<div class="hs-contact-card">
      <div class="hs-contact-label">${label}</div>
      <a href="tel:${tel}" class="hs-phone-num">${num}</a>
      ${note ? `<div class="hs-contact-note">${note}</div>` : ""}
    </div>`;
  }

  function addressCard(addr, hours) {
    return `<div class="hs-address-card">
      <svg width="13" height="13" viewBox="0 0 13 13" fill="none" aria-hidden="true">
        <path d="M6.5 1a4 4 0 0 1 4 4c0 2.8-4 7-4 7S2.5 7.8 2.5 5a4 4 0 0 1 4-4Z"
              stroke="currentColor" stroke-width="1.3"/>
        <circle cx="6.5" cy="5" r="1.3" stroke="currentColor" stroke-width="1.2"/>
      </svg>
      <div>
        <div class="hs-address-text">${addr}</div>
        ${hours ? `<div class="hs-address-hours">${hours}</div>` : ""}
      </div>
    </div>`;
  }

  function stepsHtml(steps) {
    return `<ol class="hs-steps">${steps.map((s, i) =>
      `<li class="hs-step"><span class="hs-step-n" aria-hidden="true">${i + 1}</span><span>${s}</span></li>`
    ).join("")}</ol>`;
  }

  function infoNote(html, mod) {
    return `<div class="hs-note${mod ? " hs-note--" + mod : ""}">${html}</div>`;
  }

  function actionBtns(btns) {
    const inner = btns.map(b => {
      if (b.route)     return `<button class="hs-action-btn" data-route="${b.route}" type="button">${b.label}</button>`;
      if (b.subroute)  return `<button class="hs-action-btn" data-subroute="${b.subroute}" type="button">${b.label}</button>`;
      if (b.href)      return `<a class="hs-action-btn hs-action-btn--ext" href="${b.href}" target="_blank" rel="noopener noreferrer">${b.label}</a>`;
      return "";
    }).join("");
    return `<div class="hs-actions">${inner}</div>`;
  }

  function extLink(label, url) {
    return `<a class="hs-ext-link" href="${url}" target="_blank" rel="noopener noreferrer">
      <svg width="11" height="11" viewBox="0 0 12 12" fill="none" aria-hidden="true">
        <path d="M2 10L10 2M10 2H4M10 2v6" stroke="currentColor" stroke-width="1.4"
              stroke-linecap="round" stroke-linejoin="round"/>
      </svg>${label}</a>`;
  }

  function sectionTitle(t) { return `<h3 class="hs-section-title">${t}</h3>`; }

  // ── Route renderers ───────────────────────────────────────────────
  const routeRenderers = {

    rough_sleeping: () =>
      `<div class="hs-urgency-banner">
         <svg width="15" height="15" viewBox="0 0 16 16" fill="none" aria-hidden="true">
           <path d="M8 2L1 14h14L8 2Z" stroke="currentColor" stroke-width="1.4" stroke-linejoin="round"/>
           <path d="M8 6v4M8 11.5v.5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
         </svg>
         This is urgent — please act now
       </div>
       ${sectionTitle("If you have nowhere to sleep tonight")}
       ${phoneCard("Emergency Homeless Line", "01274 435999", "24 hours, 7 days a week")}
       ${addressCard("Argus Chambers, 1 Filey Street, Bradford, BD1 5NL", "Walk-in during office hours")}
       ${sectionTitle("Sleeping rough outreach")}
       ${infoNote(`Refer someone sleeping rough via <a href="https://www.streetlink.org.uk/" target="_blank" rel="noopener noreferrer" class="hs-inline-link">Streetlink.org.uk</a>`)}
       ${actionBtns([
         { subroute: "call_info",      label: "What happens when I call?" },
         { subroute: "what_to_bring",  label: "What do I need to bring?" },
         { href: "https://www.bradford.gov.uk/housing/homelessness/how-to-get-help-if-you-are-homeless/", label: "Official guidance ↗" },
       ])}
       ${backBtn("All housing help")}`,

    eviction: () =>
      `<div class="hs-warning-banner">
         <svg width="14" height="14" viewBox="0 0 14 14" fill="none" aria-hidden="true">
           <path d="M7 2L1.5 12h11L7 2Z" stroke="currentColor" stroke-width="1.3" stroke-linejoin="round"/>
           <path d="M7 6v3M7 10.5v.5" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"/>
         </svg>
         Don't wait — the sooner you act, the more options you have
       </div>
       ${sectionTitle("Get advice now")}
       ${phoneCard("Housing Options Team", "01274 435999", "Mon–Fri, 8:30 AM – 5:00 PM")}
       ${phoneCard("Citizens Advice (free)", "0800 144 8848", "Free legal advice on eviction")}
       ${sectionTitle("Immediate steps")}
       ${stepsHtml([
         "Read your notice — check the date and type (Section 21 or Section 8)",
         "Contact the Housing Options Team as soon as possible",
         "Get free legal advice from Citizens Advice",
         "Do <strong>not</strong> leave your home until you have spoken to an advisor",
       ])}
       ${infoNote("Your landlord must follow the correct legal process. <strong>You do not have to leave immediately</strong> when you receive a notice.", "important")}
       ${actionBtns([
         { subroute: "private_tenant",  label: "I rent privately" },
         { subroute: "council_tenant",  label: "I'm in council housing" },
         { route:    "rough_sleeping",  label: "I need emergency housing" },
         { href: "https://www.bradford.gov.uk/housing/homelessness/how-to-get-help-if-you-are-homeless/", label: "Official guidance ↗" },
       ])}
       ${backBtn("All housing help")}`,

    domestic_abuse: () =>
      `<div class="hs-safety-banner">
         <svg width="15" height="15" viewBox="0 0 16 16" fill="none" aria-hidden="true">
           <circle cx="8" cy="8" r="6.5" stroke="currentColor" stroke-width="1.5"/>
           <path d="M8 5v4M8 10.5v.5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round"/>
         </svg>
         If you are in immediate danger, <strong>call 999 now</strong>
       </div>
       ${sectionTitle("Specialist support")}
       ${phoneCard("Bradford Domestic Violence Service", "01274 730 930", "Local specialist support")}
       ${phoneCard("National Domestic Abuse Helpline", "0808 2000 247", "Free, 24 hours")}
       ${sectionTitle("Emergency housing")}
       ${phoneCard("Housing Options Team", "01274 435999", "For emergency accommodation requests")}
       ${infoNote("Bradford Council can provide <strong>emergency accommodation</strong> and specialist support. You do <strong>not</strong> need proof of abuse to ask for help.")}
       ${actionBtns([
         { route: "rough_sleeping", label: "Emergency housing" },
         { href: "https://www.bradford.gov.uk/housing/homelessness/how-to-get-help-if-you-are-homeless/", label: "Official guidance ↗" },
       ])}
       ${backBtn("All housing help")}`,

    temp_accommodation: () =>
      `${sectionTitle("Request temporary accommodation")}
       ${phoneCard("Housing Options Team", "01274 435999", "Mon–Thu 8:30 AM – 5:00 PM · Fri 8:30 AM – 4:30 PM")}
       ${infoNote(`<strong>Out-of-hours emergencies:</strong> call <a href="tel:01274435999" class="hs-inline-link">01274 435999</a> — 24-hour line`)}
       ${addressCard("Argus Chambers, 1 Filey Street, Bradford, BD1 5NL", "Walk-in during office hours")}
       ${sectionTitle("What to expect")}
       ${infoNote("You will be asked about your housing situation and vulnerabilities. The council has a duty to help if you are eligible under the <strong>Housing Act 1996</strong>.")}
       ${actionBtns([
         { subroute: "priority_need",       label: "What is priority need?" },
         { subroute: "housing_assessment",  label: "What happens at the assessment?" },
         { href: "https://www.bradford.gov.uk/housing/homelessness/how-to-get-help-if-you-are-homeless/", label: "Official guidance ↗" },
       ])}
       ${backBtn("All housing help")}`,

    affordability: () =>
      `${sectionTitle("Help with rent payments")}
       <div class="hs-benefit-grid">
         <div class="hs-benefit-card">
           <strong class="hs-benefit-title">Housing Benefit</strong>
           <p class="hs-benefit-desc">May cover part or all of your rent if you're on a low income.</p>
         </div>
         <div class="hs-benefit-card">
           <strong class="hs-benefit-title">Universal Credit — Housing Element</strong>
           <p class="hs-benefit-desc">Included in your UC payment to help cover rent.</p>
         </div>
         <div class="hs-benefit-card">
           <strong class="hs-benefit-title">Discretionary Housing Payment</strong>
           <p class="hs-benefit-desc">Extra one-off help if your benefit doesn't cover full rent. Apply through Bradford Council.</p>
         </div>
       </div>
       ${phoneCard("Bradford Council Benefits Team", "01274 435999", "For applications and advice")}
       ${actionBtns([
         { href: "https://www.bradford.gov.uk/benefits/applying-for-benefits/housing-benefit-and-council-tax-reduction/", label: "Apply for Housing Benefit ↗" },
       ])}
       ${backBtn("All housing help")}`,

    repairs: () =>
      `${sectionTitle("Report a repair — council property")}
       ${phoneCard("Repairs Line", "01274 527777", "Available 24 hours for emergencies")}
       ${extLink("Report a repair online", "https://www.bradford.gov.uk/housing/advice-for-tenants/getting-repairs-done/")}
       ${infoNote(`For <strong>emergency repairs</strong> (no heating, flooding, unsafe structure) call <a href="tel:01274527777" class="hs-inline-link">01274 527777</a> at any time.`, "urgent")}
       ${sectionTitle("Private tenants")}
       ${infoNote("Your landlord is legally responsible for most repairs. If they won't act, contact Environmental Health.")}
       ${phoneCard("Environmental Health", "01274 431000", "If your private landlord refuses to act")}
       ${actionBtns([
         { href: "https://www.bradford.gov.uk/housing/advice-for-tenants/getting-repairs-done/", label: "Report online ↗" },
       ])}
       ${backBtn("All housing help")}`,

    find_home: () =>
      `${sectionTitle("Join the Housing Register")}
       ${stepsHtml([
         "Check eligibility — you must live in or have a local connection to Bradford",
         "Register online at the Bradford Council website",
         "Provide proof of identity, address, and income",
         "You'll be placed in a priority band based on your housing need",
       ])}
       ${infoNote("Average waiting times vary by area and property size. Being on the register does not guarantee a property.")}
       ${actionBtns([
         { href: "https://www.bradford.gov.uk/housing/finding-a-home/how-can-i-find-a-home/", label: "Start housing application ↗" },
         { subroute: "priority_banding", label: "What is priority banding?" },
       ])}
       ${backBtn("All housing help")}`,

    general: () =>
      `<div class="fp-intro"><p>Bradford Council can help with a wide range of housing needs. Choose a topic below.</p></div>
       <div class="hs-general-list">
         <button class="hs-general-btn" data-route="rough_sleeping"   type="button">🚨 Homeless or rough sleeping</button>
         <button class="hs-general-btn" data-route="eviction"         type="button">⚠️ Eviction or notice to leave</button>
         <button class="hs-general-btn" data-route="domestic_abuse"   type="button">💜 Domestic abuse support</button>
         <button class="hs-general-btn" data-route="temp_accommodation" type="button">🏨 Temporary accommodation</button>
         <button class="hs-general-btn" data-route="affordability"    type="button">💰 Can't afford rent</button>
         <button class="hs-general-btn" data-route="repairs"          type="button">🔧 Property repairs</button>
         <button class="hs-general-btn" data-route="find_home"        type="button">🏡 Find a home / Housing Register</button>
       </div>
       ${phoneCard("Housing Options Team", "01274 435999", "General housing enquiries")}
       ${extLink("Bradford Council Housing", "https://www.bradford.gov.uk/housing/housing/")}
       ${backBtn("All housing help")}`,
  };

  // ── Sub-route renderers ───────────────────────────────────────────
  const subRouteRenderers = {

    call_info: () =>
      `${sectionTitle("What happens when you call?")}
       ${stepsHtml([
         "A housing officer asks about your situation — where you slept, your health, any dependants",
         "They carry out an initial housing assessment over the phone",
         "If you're in priority need, emergency accommodation may be arranged",
         "You'll be asked to come in for a full assessment as soon as possible",
         "Bring any documents you have — you don't need everything right away",
       ])}
       ${infoNote("The call usually takes 15–30 minutes. Try to find somewhere quiet to make it.")}
       ${backBtn("Back to emergency housing")}`,

    what_to_bring: () =>
      `${sectionTitle("What to bring to a housing assessment")}
       <ul class="hs-checklist">
         <li class="hs-check-item"><span class="hs-check-box" aria-hidden="true">□</span>Photo ID (passport, driving licence, or birth certificate)</li>
         <li class="hs-check-item"><span class="hs-check-box" aria-hidden="true">□</span>Proof of address (if you have one)</li>
         <li class="hs-check-item"><span class="hs-check-box" aria-hidden="true">□</span>Any eviction notice or court papers</li>
         <li class="hs-check-item"><span class="hs-check-box" aria-hidden="true">□</span>Proof of income or benefits (UC letter, payslip)</li>
         <li class="hs-check-item"><span class="hs-check-box" aria-hidden="true">□</span>Details of everyone in your household (including children)</li>
         <li class="hs-check-item"><span class="hs-check-box" aria-hidden="true">□</span>Medical documentation if you have health vulnerabilities</li>
       </ul>
       ${infoNote("Don't worry if you can't bring everything — the council can still help you.")}
       ${backBtn("Back to emergency housing")}`,

    private_tenant: () =>
      `${sectionTitle("Private tenants facing eviction")}
       ${infoNote("Your landlord must give you proper legal notice before you have to leave.")}
       ${stepsHtml([
         "A <strong>Section 21</strong> notice gives you at least 2 months to leave (no-fault eviction)",
         "A <strong>Section 8</strong> notice is used when the tenancy agreement has been broken — check the reason",
         "Even after the notice expires, your landlord must get a court order before you have to leave",
         "Contact the Housing Options Team and Citizens Advice immediately",
       ])}
       ${phoneCard("Citizens Advice Bradford", "0800 144 8848", "Free legal advice — call now")}
       ${backBtn("Back to eviction help")}`,

    council_tenant: () =>
      `${sectionTitle("Council tenants facing eviction")}
       ${infoNote("Bradford Council must follow a strict legal process before any eviction. You have rights as a secure tenant.")}
       ${stepsHtml([
         "You should receive a Notice Seeking Possession — not an immediate eviction order",
         "Contact your Housing Officer as soon as possible",
         "You may be able to resolve arrears or disputes through mediation",
         "The council must apply to the court before any eviction can take place",
       ])}
       ${phoneCard("Housing Options Team", "01274 435999", "For Bradford Council tenants")}
       ${backBtn("Back to eviction help")}`,

    priority_need: () =>
      `${sectionTitle("What is priority need?")}
       ${infoNote("Under the Housing Act 1996, some households have a 'priority need' — the council has a stronger duty to house them.")}
       <div class="hs-benefit-grid">
         <div class="hs-benefit-card"><strong class="hs-benefit-title">Households with children</strong><p class="hs-benefit-desc">Families with dependent children usually have priority need.</p></div>
         <div class="hs-benefit-card"><strong class="hs-benefit-title">Pregnant women</strong><p class="hs-benefit-desc">Pregnancy counts as priority need.</p></div>
         <div class="hs-benefit-card"><strong class="hs-benefit-title">Vulnerability</strong><p class="hs-benefit-desc">Mental health issues, physical disability, or age (under 21 / over 60) may qualify.</p></div>
         <div class="hs-benefit-card"><strong class="hs-benefit-title">Emergency</strong><p class="hs-benefit-desc">Made homeless by flood, fire, or other emergency.</p></div>
       </div>
       ${backBtn("Back")}`,

    housing_assessment: () =>
      `${sectionTitle("What happens at a housing assessment?")}
       ${stepsHtml([
         "A housing officer reviews your full circumstances — housing history, finances, health",
         "They decide whether you are 'homeless', 'threatened with homelessness', or 'intentionally homeless'",
         "If you qualify, they identify what duty the council has to help you",
         "You may be placed in temporary accommodation while a longer-term solution is found",
         "You'll receive a personalised housing plan outlining next steps",
       ])}
       ${infoNote("The assessment can take 1–2 hours. Bring all documentation you have.")}
       ${backBtn("Back")}`,

    priority_banding: () =>
      `${sectionTitle("Housing priority banding")}
       ${infoNote("Bradford uses a banding system to prioritise applicants based on need.")}
       <div class="hs-benefit-grid">
         <div class="hs-benefit-card hs-benefit-card--band-a"><strong class="hs-benefit-title">Band A — Emergency</strong><p class="hs-benefit-desc">Life-threatening situation, emergency medical need, significant risk of harm.</p></div>
         <div class="hs-benefit-card hs-benefit-card--band-b"><strong class="hs-benefit-title">Band B — Urgent</strong><p class="hs-benefit-desc">Severe overcrowding, serious medical condition, domestic abuse, risk of homelessness.</p></div>
         <div class="hs-benefit-card"><strong class="hs-benefit-title">Band C — Moderate need</strong><p class="hs-benefit-desc">Some housing need, such as moving closer to family or work.</p></div>
         <div class="hs-benefit-card"><strong class="hs-benefit-title">Band D — Low need</strong><p class="hs-benefit-desc">No immediate housing need — longest waiting times.</p></div>
       </div>
       ${backBtn("Back to find a home")}`,
  };

  // ── Intent detection (mirrors HousingNavigatorService.DetectHousingNode) ──
  function detectHousingRoute(text) {
    const t = text.toLowerCase().trim();
    if (t.includes("rough sleeping") || t.includes("sleeping rough") ||
        t.includes("nowhere to sleep") || t.includes("no where to sleep") ||
        t.includes("no place to stay") || t.includes("outside tonight") ||
        t.includes("im homeless") || t.includes("i'm homeless") ||
        t.includes("i am homeless") || t.includes("homeless tonight") ||
        (t.includes("street") && t.includes("homeless")))
      return "rough_sleeping";

    if (t.includes("eviction") || t.includes("being evicted") || t.includes("evicted") ||
        t.includes("section 21") || t.includes("section 8") ||
        t.includes("notice to leave") || t.includes("notice to quit") ||
        t.includes("landlord wants me out") ||
        t.includes("lose my home") || t.includes("losing my home") ||
        t.includes("at risk of losing") || t.includes("eviction risk"))
      return "eviction";

    if (t.includes("domestic abuse") || t.includes("domestic violence") ||
        t.includes("fleeing violence") || t.includes("unsafe at home") ||
        t.includes("not safe at home"))
      return "domestic_abuse";

    if (t.includes("temporary accommodation") || t.includes("emergency accommodation") ||
        t.includes("emergency housing") || t.includes("hostel") || t.includes("night shelter"))
      return "temp_accommodation";

    if (t.includes("can't afford rent") || t.includes("cant afford rent") ||
        t.includes("behind on rent") || t.includes("rent arrears") ||
        t.includes("struggling to pay rent"))
      return "affordability";

    if (t.includes("repair") || (t.includes("fix") && t.includes("house")) ||
        t.includes("damp") || t.includes("mould") ||
        (t.includes("broken") && (t.includes("boiler") || t.includes("window") || t.includes("door"))))
      return "repairs";

    if (t.includes("find a home") || t.includes("need a home") ||
        t.includes("looking for housing") || t.includes("council house") ||
        t.includes("social housing") || t.includes("housing list") ||
        t.includes("housing register") || t.includes("waiting list"))
      return "find_home";

    return "general";
  }

  function isHousingIntent(text) {
    const t = text.toLowerCase().trim();
    return (
      t.includes("homeless") ||
      t.includes("rough sleep") || t.includes("sleeping rough") ||
      t.includes("nowhere to sleep") || t.includes("no place to stay") ||
      t.includes("evict") ||
      t.includes("domestic abuse") || t.includes("domestic violence") ||
      t.includes("fleeing violence") || t.includes("unsafe at home") ||
      t.includes("emergency housing") || t.includes("housing support") ||
      t.includes("housing advice") ||
      t.includes("hostel") || t.includes("night shelter") ||
      t.includes("temporary accommodation") || t.includes("emergency accommodation") ||
      t.includes("behind on rent") || t.includes("rent arrears") ||
      (t.includes("can't afford") && t.includes("rent")) ||
      (t.includes("cant afford") && t.includes("rent")) ||
      t.includes("repair") || t.includes("damp") || t.includes("mould") ||
      t.includes("housing register") || t.includes("housing list") ||
      t.includes("council house") || t.includes("social housing") ||
      t.includes("section 21") || t.includes("section 8") ||
      t.includes("notice to leave") || t.includes("notice to quit") ||
      t.includes("landlord wants me out") ||
      t.includes("lose my home") || t.includes("losing my home") ||
      t.includes("i am homeless") || t.includes("im homeless") || t.includes("i'm homeless")
    );
  }

  // ── Wire chat interceptor ─────────────────────────────────────────
  const _prevSendPreset = window.sendPreset;
  window.sendPreset = function (text) {
    if (!_bypassIntercept && isHousingIntent(text)) {
      openPanel({ route: detectHousingRoute(text) });
      return;
    }
    if (typeof _prevSendPreset === "function") _prevSendPreset(text);
  };

  // Capture-phase hook
  const sendBtn = document.getElementById("sendBtn");
  if (sendBtn) {
    sendBtn.addEventListener("click", function hsCapture(e) {
      if (_bypassIntercept) return;
      const input = document.getElementById("msg");
      const raw   = input ? (input.value || "").trim() : "";
      if (raw && isHousingIntent(raw)) {
        e.stopImmediatePropagation();
        if (input) input.value = "";
        openPanel({ route: detectHousingRoute(raw) });
      }
    }, { capture: true });
  }

  // Expose globally
  window.openHousingPanel = openPanel;

})();
