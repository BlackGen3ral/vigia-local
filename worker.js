import { buildPushPayload } from "@block65/webcrypto-web-push";

const encoder = new TextEncoder();
const tokenPattern = /^[a-f0-9]{64}$/;
const json = (data, status = 200) => Response.json(data, { status, headers: { "Cache-Control": "no-store" } });
const error = (message, status) => json({ error: message }, status);

async function hash(value) {
  return Array.from(new Uint8Array(await crypto.subtle.digest("SHA-256", encoder.encode(value))),
    byte => byte.toString(16).padStart(2, "0")).join("");
}

function sameSecret(a, b) {
  if (!tokenPattern.test(a ?? "") || !tokenPattern.test(b ?? "")) return false;
  let difference = 0;
  for (let i = 0; i < 64; i++) difference |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return difference === 0;
}

async function body(request) {
  if (!request.headers.get("Content-Type")?.startsWith("application/json")) throw new Error("JSON esperado.");
  const reader = request.body.getReader();
  const decoder = new TextDecoder();
  let length = 0;
  let contents = "";
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) return JSON.parse(contents + decoder.decode());
      length += value.length;
      if (length > 4096) throw new Error("Dados muito grandes.");
      contents += decoder.decode(value, { stream: true });
    }
  } finally {
    await reader.cancel().catch(() => {});
  }
}

function validEndpoint(endpoint) {
  try {
    const url = new URL(endpoint);
    return url.protocol === "https:" && !url.port && !url.username && !url.password &&
      /^(fcm\.googleapis\.com|android\.googleapis\.com|updates\.push\.services\.mozilla\.com|web\.push\.apple\.com|[a-z0-9.-]+\.notify\.windows\.com)$/.test(url.hostname);
  } catch {
    return false;
  }
}

function validSubscription(value) {
  return value && typeof value.endpoint === "string" && value.endpoint.length <= 1000 && validEndpoint(value.endpoint) &&
    /^[A-Za-z0-9_-]{87}$/.test(value.keys?.p256dh ?? "") && /^[A-Za-z0-9_-]{22}$/.test(value.keys?.auth ?? "");
}

async function notify(env, store, event) {
  const { results } = await env.DB.prepare("SELECT payload FROM subscriptions WHERE store = ? LIMIT 24").bind(store).all();
  await Promise.allSettled(results.map(async ({ payload }) => {
    const subscription = JSON.parse(payload);
    const options = await buildPushPayload({ data: JSON.stringify(event), options: { ttl: 120 } }, subscription,
      { subject: "https://github.com/BlackGen3ral/vigia-local", publicKey: env.VAPID_PUBLIC, privateKey: env.VAPID_PRIVATE });
    const response = await fetch(subscription.endpoint, { ...options, redirect: "error", signal: AbortSignal.timeout(8000) });
    if (response.status === 404 || response.status === 410)
      await env.DB.prepare("DELETE FROM subscriptions WHERE store = ? AND endpoint = ?").bind(store, subscription.endpoint).run();
    if (!response.ok && response.status !== 404 && response.status !== 410)
      console.warn(JSON.stringify({ event: "push_rejected", status: response.status }));
  }));
}

async function api(request, env, ctx) {
  const path = new URL(request.url).pathname;
  const method = request.method;
  if (path === "/api/push-key" && method === "GET") return json({ key: env.VAPID_PUBLIC });
  const token = request.headers.get("Authorization")?.match(/^Bearer ([a-f0-9]{64})$/)?.[1];
  if (!token) return error("Código de acesso necessário.", 401);
  const writer = method === "POST" && ["/api/activate", "/api/events", "/api/heartbeat"].includes(path);
  const reader = writer ? await hash("read:" + token) : token;
  const store = await hash("store:" + reader);
  if (writer && path === "/api/activate") {
    if (!sameSecret(request.headers.get("X-Vigia-Enrollment"), env.ENROLL_KEY)) return error("Chave de instalação inválida.", 403);
    await env.DB.prepare("INSERT OR IGNORE INTO stores(id) VALUES(?)").bind(store).run();
    return json({ ok: true });
  }
  const record = await env.DB.prepare("SELECT last_seen FROM stores WHERE id = ?").bind(store).first();
  if (!record) return error("Esta loja ainda não foi vinculada.", 404);
  const now = Date.now();
  if (writer && path === "/api/heartbeat") {
    if (now - record.last_seen >= 15_000)
      await env.DB.prepare("UPDATE stores SET last_seen = ? WHERE id = ?").bind(now, store).run();
    return json({ ok: true });
  }
  if (writer && path === "/api/events") {
    let event;
    try { event = await body(request); } catch { return error("Evento inválido.", 400); }
    if (!event || !/^[a-f0-9-]{36}$/.test(event.id ?? "") || !["arrival", "test"].includes(event.kind))
      return error("Evento inválido.", 400);
    const inserted = await env.DB.prepare(
      "INSERT OR IGNORE INTO events(id,store,time,kind) SELECT ?,?,?,? WHERE NOT EXISTS(SELECT 1 FROM events WHERE store=? AND time>?)"
    ).bind(event.id, store, now, event.kind, store, now - 3500).run();
    if (inserted.meta.changes) {
      ctx.waitUntil(notify(env, store, { id: event.id, kind: event.kind }));
      ctx.waitUntil(env.DB.prepare("DELETE FROM events WHERE store=? AND (time < ? OR id IN (SELECT id FROM events WHERE store=? ORDER BY time DESC LIMIT -1 OFFSET 200))")
        .bind(store, now - 14 * 86400000, store).run());
    }
    return json({ ok: true, accepted: Boolean(inserted.meta.changes) });
  }
  if (method === "GET" && path === "/api/events") {
    const { results } = await env.DB.prepare("SELECT id,time,kind FROM events WHERE store=? AND time>=? ORDER BY time DESC LIMIT 60")
      .bind(store, now - 14 * 86400000).all();
    return json({ events: results, lastSeen: record.last_seen, online: now - record.last_seen < 95000 });
  }
  if (path === "/api/subscription" && ["POST", "DELETE"].includes(method)) {
    let data;
    try { data = await body(request); } catch { return error("Notificações inválidas.", 400); }
    if (method === "DELETE") {
      await env.DB.prepare("DELETE FROM subscriptions WHERE store=? AND endpoint=?").bind(store, String(data?.endpoint ?? "")).run();
      return json({ ok: true });
    }
    if (!validSubscription(data)) return error("Navegador de notificações incompatível.", 400);
    const saved = await env.DB.prepare(
      "INSERT INTO subscriptions(store,endpoint,payload) SELECT ?,?,? WHERE EXISTS(SELECT 1 FROM subscriptions WHERE store=? AND endpoint=?) OR (SELECT COUNT(*) FROM subscriptions WHERE store=?) < 24 ON CONFLICT(store,endpoint) DO UPDATE SET payload=excluded.payload"
    ).bind(store, data.endpoint, JSON.stringify(data), store, data.endpoint, store).run();
    return saved.meta.changes ? json({ ok: true }) : error("Limite de aparelhos da loja atingido.", 409);
  }
  return error("Recurso indisponível.", 404);
}

export default {
  async fetch(request, env, ctx) {
    if (!new URL(request.url).pathname.startsWith("/api/")) return env.ASSETS.fetch(request);
    try { return await api(request, env, ctx); }
    catch {
      console.error(JSON.stringify({ event: "api_error", path: new URL(request.url).pathname }));
      return error("Serviço temporariamente indisponível.", 503);
    }
  }
};
