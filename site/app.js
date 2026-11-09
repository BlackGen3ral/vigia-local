const byId = id => document.getElementById(id);
const tokenPattern = /^(?:[a-f0-9]{64}|v1\.[a-f0-9]{64}\.[a-f0-9]{64})$/;
const invitePattern = /^[0-9A-HJKMNP-TV-Z]{12}$/;
const fragment = new URLSearchParams(location.hash.slice(1));
const pairFromLink = fragment.get("pair");
const previewFromLink = fragment.get("photo");
const showPanel = new URLSearchParams(location.search).has("app") || Boolean(pairFromLink);
let code = localStorage.getItem("vigia-pair") || "";
let installPrompt;
const service = "serviceWorker" in navigator ? navigator.serviceWorker.register("/sw.js").catch(() => null) : Promise.resolve(null);

if (pairFromLink || previewFromLink) history.replaceState(null, "", "/?app");
byId("marketing").hidden = showPanel;
byId("dashboard").hidden = !showPanel;

window.addEventListener("beforeinstallprompt", event => { event.preventDefault(); installPrompt = event; });
byId("install").addEventListener("click", async () => {
  if (installPrompt) {
    await installPrompt.prompt();
    installPrompt = null;
    return;
  }
  byId("install-help").textContent = matchMedia("(display-mode: standalone)").matches
    ? "O Vigia Local já está instalado neste aparelho."
    : "No iPhone, abra este site no Safari e use Compartilhar → Adicionar à Tela de Início.";
});

async function api(path, { token = code, method = "GET", data } = {}) {
  const headers = token ? { Authorization: "Bearer " + token } : {};
  if (data) headers["Content-Type"] = "application/json";
  const response = await fetch("/api/" + path, { method, headers, body: data && JSON.stringify(data), cache: "no-store" });
  const result = await response.json();
  if (!response.ok) throw new Error(result.error || "Serviço indisponível.");
  return result;
}

function renderEvents(result) {
  byId("connection").textContent = result.online ? "Computador da loja online"
    : result.lastSeen ? "Computador da loja offline" : "Aguardando o computador da loja";
  const list = byId("event-list");
  list.replaceChildren();
  byId("event-empty").hidden = result.events.length > 0;
  for (const event of result.events) {
    const item = document.createElement("li");
    const name = document.createElement("span");
    const time = document.createElement("time");
    const date = new Date(event.time);
    name.textContent = event.kind === "test" ? "Teste de aviso" : "Pessoa na entrada";
    time.dateTime = date.toISOString();
    time.textContent = date.toLocaleString("pt-BR", { dateStyle: "short", timeStyle: "short" });
    item.append(name, time);
    list.append(item);
  }
}

async function refresh() {
  if (!code) return;
  try { renderEvents(await api("events")); byId("feedback").textContent = ""; }
  catch (error) { byId("feedback").textContent = error.message; }
}

async function pushState() {
  const registration = await service;
  const supported = registration && "PushManager" in window && "Notification" in window;
  const subscription = supported && await registration.pushManager.getSubscription();
  byId("push").disabled = !code || !supported;
  byId("push-test").disabled = !code || !supported || Notification.permission !== "granted";
  byId("push").textContent = subscription ? "Desativar neste aparelho" : "Ativar neste aparelho";
  byId("push-status").textContent = !code ? "Vincule a loja para receber avisos." : !supported
    ? "Para receber avisos, use um navegador compatível. No iPhone, instale pela Tela de Início."
    : Notification.permission === "denied" ? "As notificações estão bloqueadas. Libere o Vigia nas configurações do aparelho."
    : subscription ? "Avisos ativados. O som segue as configurações de notificação do celular."
    : "Ative para receber alertas mesmo com o painel fechado.";
}

async function pair(value) {
  let token = value.trim();
  try { token = new URLSearchParams(new URL(token).hash.slice(1)).get("pair") || ""; } catch {}
  if (!tokenPattern.test(token)) {
    const invite = token.toUpperCase().replace(/[\s-]/g, "");
    if (!invitePattern.test(invite)) throw new Error("Digite os 12 caracteres do convite ou cole um link da loja.");
    ({ token } = await api("invite/redeem", { token: "", method: "POST", data: { code: invite } }));
  }
  const result = await api("events", { token });
  code = token;
  localStorage.setItem("vigia-pair", code);
  byId("pair-form").hidden = true;
  byId("unlink").hidden = false;
  byId("refresh").disabled = false;
  renderEvents(result);
  const subscription = await (await service)?.pushManager?.getSubscription();
  if (subscription && Notification.permission === "granted") {
    try { await api("subscription", { method: "POST", data: subscription.toJSON() }); }
    catch (error) { byId("feedback").textContent = error.message; }
  }
  if (/^[a-f0-9]{32}$/.test(previewFromLink ?? "")) {
    const image = byId("event-preview-image");
    image.addEventListener("load", () => { byId("event-preview").hidden = false; }, { once: true });
    image.src = "/api/thumbnail/" + previewFromLink;
  }
  await pushState();
}

byId("pair-form").addEventListener("submit", async event => {
  event.preventDefault();
  try { await pair(byId("pair").value); byId("pair").value = ""; byId("feedback").textContent = ""; }
  catch (error) { byId("feedback").textContent = error.message; }
});
byId("refresh").addEventListener("click", refresh);
byId("push").addEventListener("click", async () => {
  const button = byId("push");
  button.disabled = true;
  try {
    const permission = Notification.permission === "default" ? Notification.requestPermission() : Promise.resolve(Notification.permission);
    const registration = await service;
    const existing = await registration.pushManager.getSubscription();
    if (existing) {
      await api("subscription", { method: "DELETE", data: { endpoint: existing.endpoint } });
      await existing.unsubscribe();
    } else {
      if (await permission !== "granted") throw new Error("Permita notificações nas configurações do navegador.");
      const { key } = await api("push-key", { token: "" });
      const binary = Uint8Array.from(atob(key.replace(/-/g, "+").replace(/_/g, "/").padEnd(Math.ceil(key.length / 4) * 4, "=")), c => c.charCodeAt(0));
      const subscription = await registration.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: binary });
      try { await api("subscription", { method: "POST", data: subscription.toJSON() }); }
      catch (error) { await subscription.unsubscribe(); throw error; }
    }
    byId("feedback").textContent = "";
  } catch (error) { byId("feedback").textContent = error.message; }
  finally { await pushState(); }
});
byId("push-test").addEventListener("click", async () => {
  try {
    const registration = await service;
    await registration.showNotification("Teste no celular", {
      body: "Se aparecer sem som, confira o volume e as notificações do Vigia no aparelho.",
      icon: "/icon-192.png", silent: false, vibrate: [180, 90, 180], data: { url: "/?app" }
    });
  } catch (error) { byId("feedback").textContent = error.message; }
});
byId("unlink").addEventListener("click", async () => {
  const registration = await service;
  const subscription = await registration?.pushManager?.getSubscription();
  if (subscription) {
    try { await api("subscription", { method: "DELETE", data: { endpoint: subscription.endpoint } }); } catch {}
    await subscription.unsubscribe();
  }
  localStorage.removeItem("vigia-pair");
  location.replace("/?app");
});
if (showPanel) {
  const initial = code || pairFromLink;
  if (initial) pair(initial).catch(error => { byId("feedback").textContent = error.message; });
  setInterval(() => { if (!document.hidden) refresh(); }, 15000);
}
