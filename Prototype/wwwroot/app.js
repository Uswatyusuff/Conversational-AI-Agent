const chat = document.getElementById("chat");
const statusLine = document.getElementById("statusLine");
const sessionPill = document.getElementById("sessionPill");
const sendBtn = document.getElementById("sendBtn");
const msgInput = document.getElementById("msg");
const resetBtn = document.getElementById("resetBtn");
const newChatBtn = document.getElementById("newChatBtn");
const themeToggle = document.getElementById("themeToggle");
const themeLabel = document.getElementById("themeLabel");
const appShell = document.querySelector(".app-shell");
const toggleSidebarBtn = document.getElementById("toggleSidebarBtn");

if (toggleSidebarBtn) {
  toggleSidebarBtn.addEventListener("click", () => {
    appShell.classList.toggle("sidebar-hidden");
  });
}

let lastService = "Unknown";
let pendingSkeletonId = null;

let pendingPostcode = null;
let awaitingAddressSelection = false;
let lastAddresses = [];

let sessionId = localStorage.getItem("sessionId");
if (!sessionId) {
  sessionId = crypto.randomUUID();
  localStorage.setItem("sessionId", sessionId);
}
sessionPill.textContent = "Session: " + sessionId.slice(0, 8);

function applyTheme(theme) {
  document.documentElement.setAttribute("data-theme", theme);
  themeLabel.textContent = theme === "light" ? "Light mode" : "Dark mode";
  localStorage.setItem("theme", theme);
}

const savedTheme = localStorage.getItem("theme");
applyTheme(savedTheme === "light" ? "light" : "dark");

themeToggle.addEventListener("click", () => {
  const current = document.documentElement.getAttribute("data-theme");
  applyTheme(current === "dark" ? "light" : "dark");
});
if (toggleSidebarBtn) {
  toggleSidebarBtn.addEventListener("click", () => {
    appShell.classList.toggle("sidebar-hidden");
  });
}

function escapeHtml(str) {
  return String(str)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

function maskSensitiveText(input) {
  if (!input) return "";

  let output = String(input);

  output = output.replace(/[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}/gi, "[EMAIL]");
  output = output.replace(/\b(?:\+44|0)\d[\d\s]{8,}\b/gi, "[PHONE]");
  output = output.replace(/\b[A-Z]{1,2}\d[A-Z\d]?\s?\d[A-Z]{2}\b/gi, "[POSTCODE]");
  output = output.replace(/\b(my name is|i am|i'm)\s+[a-z][a-z\s'-]*/gi, "$1 [NAME]");

  return output;
}

function safeUserBubbleText(input) {
  return escapeHtml(maskSensitiveText(input));
}

function addMessage(html, who, id = null) {
  const div = document.createElement("div");
  div.className = "msg " + (who === "you" ? "you" : "bot");
  if (id) div.dataset.id = id;

  const bubble = document.createElement("div");
  bubble.className = "bubble";
  bubble.innerHTML = html;

  div.appendChild(bubble);
  chat.appendChild(div);
  chat.scrollTop = chat.scrollHeight;
  return div;
}

function updateStatus(text) {
  statusLine.textContent = text;
}

function isUsefulAddress(text) {
  if (!text) return false;
  const cleaned = text.trim().toLowerCase();

  const excluded = [
    "close",
    "view our privacy notice",
    "privacy notice"
  ];

  return !excluded.includes(cleaned);
}

function renderSuggestionChips(suggestions) {
  if (!Array.isArray(suggestions) || suggestions.length === 0) {
    return "";
  }

  const chips = suggestions.map((s) => {
    const safeText = escapeHtml(String(s));
    const encoded = encodeURIComponent(String(s));
    return `<button class="chip" onclick="sendSuggestion('${encoded}')">${safeText}</button>`;
  }).join("");

  return `<div class="chips">${chips}</div>`;
}

function sendSuggestion(encodedText) {
  const text = decodeURIComponent(encodedText);
  sendPreset(text);
}

window.sendSuggestion = sendSuggestion;

function showSkeleton() {
  const id = "sk_" + crypto.randomUUID();
  pendingSkeletonId = id;

  addMessage(
    `<div class="skeleton-wrap" aria-label="Loading">
      <div class="skeleton-line s1"></div>
      <div class="skeleton-line s2"></div>
      <div class="skeleton-line s3"></div>
    </div>`,
    "bot",
    id
  );

  updateStatus("AI is analysing your request...");
  sendBtn.disabled = true;
}

function replaceSkeletonWithHtml(html) {
  const node = [...document.querySelectorAll(".msg.bot")].find(
    x => x.dataset.id === pendingSkeletonId
  );

  pendingSkeletonId = null;

  if (!node) {
    addMessage(html, "bot");
    updateStatus("Ready to help");
    sendBtn.disabled = false;
    return;
  }

  const bubble = node.querySelector(".bubble");
  bubble.innerHTML = html;

  updateStatus("Ready to help");
  sendBtn.disabled = false;
}

function buildBotHtml(data) {
  lastService = data.service || "Unknown";

  const safeService = escapeHtml(lastService);
  const replyHtml = data.reply || "";

  let header = `<span class="tag">Service</span><b>${safeService}</b>`;
  let body = `<div style="margin-top:8px">${replyHtml}</div>`;

  let next = "";
  if (data.nextStepsUrl) {
    const safeUrl = escapeHtml(data.nextStepsUrl);
    next = `
      <div class="card">
        <b>Next steps</b><br />
        <a href="${safeUrl}" target="_blank" rel="noopener noreferrer">${safeUrl}</a>
      </div>
    `;
  }

  const feedback = `
    <div class="card">
      <b>Was this helpful?</b>
      <div class="chips" style="margin-top:8px">
        <button class="chip" onclick="feedback('Yes')">Yes</button>
        <button class="chip" onclick="feedback('No')">No</button>
      </div>
    </div>
  `;

  const suggestionChips = renderSuggestionChips(data.suggestions || []);
  return header + body + next + feedback + suggestionChips;
}

function replaceSkeletonWithBotReply(data) {
  const node = [...document.querySelectorAll(".msg.bot")].find(
    x => x.dataset.id === pendingSkeletonId
  );

  pendingSkeletonId = null;

  if (!node) {
    addBotReply(data);
    updateStatus("Ready to help");
    sendBtn.disabled = false;
    return;
  }

  const bubble = node.querySelector(".bubble");
  bubble.innerHTML = buildBotHtml(data);

  updateStatus("Ready to help");
  sendBtn.disabled = false;
}

function addBotReply(data) {
  addMessage(buildBotHtml(data), "bot");
}

async function searchPostcode(postcode) {
  showSkeleton();

  try {
    const res = await fetch(`/api/postcode/search?postcode=${encodeURIComponent(postcode)}&sessionId=${encodeURIComponent(sessionId)}`);
    const data = await res.json();

    if (!res.ok || data.error) {
      replaceSkeletonWithBotReply({
        service: "Waste & Bins",
        reply: `<b>Error:</b> ${escapeHtml(data.error || "Could not look up addresses for that postcode.")}`,
        nextStepsUrl: "",
        suggestions: ["Try another postcode", "When is my bin collection?"]
      });
      return;
    }

    const addresses = (data.addresses || []).filter(isUsefulAddress);
    lastAddresses = addresses;
    pendingPostcode = postcode;
    awaitingAddressSelection = true;
    lastService = "Waste & Bins";

    if (addresses.length === 0) {
      replaceSkeletonWithBotReply({
        service: "Waste & Bins",
        reply: "I could not find any selectable addresses for that postcode. Please try another postcode.",
        nextStepsUrl: "",
        suggestions: ["Try another postcode", "Report a missed bin"]
      });
      return;
    }

    const buttonsHtml = addresses.map((address, index) => {
      return `<button class="chip" onclick="selectAddress(${index})">${escapeHtml(address)}</button>`;
    }).join("");

    replaceSkeletonWithHtml(`
      <span class="tag">Service</span><b>Waste & Bins</b>
      <div style="margin-top:8px">
        I found these addresses for <b>${escapeHtml(postcode)}</b>. Please select one:
      </div>
      <div class="card">
        <div class="chips">${buttonsHtml}</div>
      </div>
    `);
  } catch (error) {
    replaceSkeletonWithBotReply({
      service: "Waste & Bins",
      reply: "<b>Error:</b> Could not reach the postcode lookup service.",
      nextStepsUrl: "",
      suggestions: ["Try again", "Enter a different postcode"]
    });
  }
}

async function selectAddress(index) {
  if (!awaitingAddressSelection) return;
  if (index < 0 || index >= lastAddresses.length) return;

  const address = lastAddresses[index];
    addMessage("[ADDRESS_SELECTED]", "you");
  

  showSkeleton();

  try {
    const res = await fetch(`/api/postcode/bin-result?postcode=${encodeURIComponent(pendingPostcode)}&address=${encodeURIComponent(address)}&sessionId=${encodeURIComponent(sessionId)}`);
    const data = await res.json();

    if (!res.ok) {
      replaceSkeletonWithBotReply({
        service: "Waste & Bins",
        reply: "<b>Error:</b> Could not retrieve the collection result.",
        nextStepsUrl: "",
        suggestions: ["Try again", "Use different address"]
      });
      return;
    }

    awaitingAddressSelection = false;
    const resultText = data.result || "No collection information found.";

    replaceSkeletonWithHtml(`
      <span class="tag">Service</span><b>Waste & Bins</b>
      <div style="margin-top:8px"><b>Address:</b> ${escapeHtml(address)}</div>
      <div class="card" style="white-space: pre-wrap; margin-top:10px">${escapeHtml(resultText)}</div>
      <div class="chips">
        <button class="chip" onclick="sendPreset('Use same address')">Use same address</button>
        <button class="chip" onclick="sendPreset('Use different address')">Use different address</button>
        <button class="chip" onclick="sendPreset('Report a missed bin')">Report a missed bin</button>
      </div>
    `);
  } catch (error) {
    replaceSkeletonWithBotReply({
      service: "Waste & Bins",
      reply: "<b>Error:</b> Could not reach the bin result service.",
      nextStepsUrl: "",
      suggestions: ["Try again", "Use different address"]
    });
  }
}

window.selectAddress = selectAddress;

async function send() {
  const message = msgInput.value.trim();
  if (!message) return;

  addMessage(safeUserBubbleText(message), "you");
  msgInput.value = "";

  showSkeleton();

  try {
    const res = await fetch("/api/chat", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ message, sessionId })
    });

    const data = await res.json();

    if (!res.ok) {
      replaceSkeletonWithBotReply({
        service: "Error",
        reply: `<b>Error:</b> ${escapeHtml(data.reply || "Request failed")}`,
        nextStepsUrl: "",
        suggestions: []
      });
      return;
    }

    if (typeof data.reply === "string" && data.reply.startsWith("POSTCODE_LOOKUP::")) {
      const postcode = data.reply.replace("POSTCODE_LOOKUP::", "").trim();

      const node = [...document.querySelectorAll(".msg.bot")].find(
        x => x.dataset.id === pendingSkeletonId
      );

      if (node) node.remove();
      pendingSkeletonId = null;
      sendBtn.disabled = false;
      updateStatus("AI is analysing your request...");

      await searchPostcode(postcode);
      return;
    }

    replaceSkeletonWithBotReply(data);
  } catch (error) {
    replaceSkeletonWithBotReply({
      service: "Error",
      reply: "<b>Error:</b> Could not reach the server.",
      nextStepsUrl: "",
      suggestions: []
    });
  }
}

function sendPreset(text) {
  msgInput.value = text;
  send();
}

window.sendPreset = sendPreset;

async function feedback(helpful) {
  const comment = prompt("Optional: add a short comment (or press Cancel).") || "";

  try {
    await fetch("/api/feedback", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ service: lastService, helpful, comment, sessionId })
    });

    addMessage(`Thanks — feedback saved for <b>${escapeHtml(lastService)}</b>.`, "bot");
  } catch (error) {
    addMessage("Thanks — I could not save feedback right now, but your response was noted.", "bot");
  }
}

window.feedback = feedback;

function resetChat() {
  chat.innerHTML = "";
  pendingPostcode = null;
  awaitingAddressSelection = false;
  lastAddresses = [];
  lastService = "Unknown";

  addWelcomeMessage();
}

function addWelcomeMessage() {
  addMessage(
    `<span class="tag">Agent</span> <b>Bradford AI Agent</b>
     
     <div style="margin-top:10px; line-height:1.5">
       Hi! I can help with Bradford Council services including:
       <br><br>
       • Council Tax  
       • Waste & Bins  
       • Benefits & Support  
       • School Admissions  
       • Planning  
       • Libraries  
       • Housing  
     </div>

     <div class="meta" style="margin-top:10px">
       Try: “When is my bin day?” or “Check my council tax balance”
     </div>`,
    "bot"
  );
}

document.querySelectorAll("[data-preset]").forEach((button) => {
  button.addEventListener("click", () => {
    const preset = button.getAttribute("data-preset");
    if (preset) sendPreset(preset);
  });
});

sendBtn.addEventListener("click", send);

msgInput.addEventListener("keydown", (e) => {
  if (e.key === "Enter") {
    e.preventDefault();
    send();
  }
});

resetBtn.addEventListener("click", resetChat);
newChatBtn.addEventListener("click", resetChat);

if (toggleSidebarBtn) {
  toggleSidebarBtn.addEventListener("click", () => {
    appShell.classList.toggle("sidebar-hidden");
  });
}

addWelcomeMessage();