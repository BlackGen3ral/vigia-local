import { buildPushPayload } from "@block65/webcrypto-web-push";

const encoder = new TextEncoder();
const tokenPattern = /^[a-f0-9]{64}$/;
const inviteAlphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
const invitePattern = /^[0-9A-HJKMNP-TV-Z]{12}$/;
const inviteLifetime = 7 * 86400000;
const json = (data, status = 200) => Response.json(data, { status, headers: { "Cache-Control": "no-store" } });
const error = (message, status) => json({ error: message }, status);

async function androidUpdate(request, env) {
  const url = new URL(request.url);
  const apk = url.pathname.match(/^\/api\/android\/apk\/(latest|v[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3})$/);
  if (request.method !== "GET" || (!apk && url.pathname !== "/api/android/update")) return null;
  const manifest = await env.ASSETS.fetch(new Request(new URL("/android-update.json", url)));
  if (!manifest.ok) throw new Error("Update manifest unavailable");
  const release = await body(manifest, 4096);
  if (!/^[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}$/.test(release.version ?? "") ||
      !/^[a-f0-9]{64}$/.test(release.sha256 ?? "") || !Number.isSafeInteger(release.size) ||
      release.size <= 0 || release.size > 24*1024*1024) throw new Error("Invalid APK manifest");
  if (!apk) return json({ ...release, url: url.origin + "/api/android/apk/v" + release.version });
  if (apk[1] !== "latest" && apk[1] !== "v" + release.version) return error("Versão substituída. Verifique a atualização novamente.", 409);
  const download = await env.ASSETS.fetch(new Request(new URL("/android-update/VigiaLocal-Android.apk", url)));
  if (!download.ok || !download.body) throw new Error("APK unavailable");
  return new Response(download.body, { headers: {
    "Content-Type": "application/vnd.android.package-archive",
    "Content-Disposition": `attachment; filename="VigiaLocal-Android-${release.version}.apk"`,
    "Content-Length": String(release.size), "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff"
  } });
}

async function hash(value) {
  return Array.from(new Uint8Array(await crypto.subtle.digest("SHA-256", encoder.encode(value))),
    byte => byte.toString(16).padStart(2, "0")).join("");
}

async function viewerSignature(store, env) {
  const key = await crypto.subtle.importKey("raw", encoder.encode(env.ENROLL_KEY),
    { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  const signature = await crypto.subtle.sign("HMAC", key, encoder.encode("vigia-viewer-v1:" + store));
  return Array.from(new Uint8Array(signature), byte => byte.toString(16).padStart(2, "0")).join("");
}

async function redeemInvite(request, env) {
  let data;
  try { data = await body(request); } catch { return error("Convite inválido.", 400); }
  const code = typeof data?.code === "string" ? data.code.toUpperCase().replace(/[\s-]/g, "") : "";
  if (!invitePattern.test(code)) return error("Digite os 12 caracteres do convite.", 400);
  const record = await env.DB.prepare("SELECT store FROM invite_codes WHERE code_hash = ? AND expires_at > ?")
    .bind(await hash("invite:" + code), Date.now()).first();
  if (!record) return error("Convite inválido ou vencido. Peça outro código à loja.", 404);
  return json({ token: "v1." + record.store + "." + await viewerSignature(record.store, env) });
}

async function createInvite(token, store, now, env) {
  const period = Math.floor(now / inviteLifetime);
  const digest = await hash(`vigia-invite-v1:${period}:${token}`);
  const code = Array.from({ length: 12 }, (_, index) =>
    inviteAlphabet[parseInt(digest.slice(index * 2, index * 2 + 2), 16) & 31]).join("");
  const codeHash = await hash("invite:" + code);
  const expiresAt = now + inviteLifetime;
  await env.DB.batch([
    env.DB.prepare("DELETE FROM invite_codes WHERE store = ? AND expires_at <= ?").bind(store, now),
    env.DB.prepare("INSERT INTO invite_codes(code_hash, store, expires_at) VALUES(?, ?, ?) ON CONFLICT(code_hash) DO UPDATE SET expires_at=excluded.expires_at WHERE store=excluded.store")
      .bind(codeHash, store, expiresAt)
  ]);
  return json({ code: code.match(/.{4}/g).join("-") });
}

function sameSecret(a, b) {
  if (!tokenPattern.test(a ?? "") || !tokenPattern.test(b ?? "")) return false;
  let difference = 0;
  for (let i = 0; i < 64; i++) difference |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return difference === 0;
}

async function body(request, maxBytes = 4096) {
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
      if (length > maxBytes) throw new Error("Dados muito grandes.");
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

async function notify(env, store, event, jpeg) {
  const { results } = await env.DB.prepare("SELECT payload FROM subscriptions WHERE store = ? LIMIT 24").bind(store).all();
  if (jpeg) {
    try { event.thumbnail = await saveThumbnail(env, jpeg, store, event.id, Date.now()); }
    catch { console.warn(JSON.stringify({ event: "thumbnail_unavailable" })); }
  }
  const deliveries = await Promise.allSettled(results.map(async ({ payload }) => {
    const subscription = JSON.parse(payload);
    const options = await buildPushPayload({ data: JSON.stringify(event), options: { ttl: 120 } }, subscription,
      { subject: "https://github.com/BlackGen3ral/vigia-local", publicKey: env.VAPID_PUBLIC, privateKey: env.VAPID_PRIVATE });
    const response = await fetch(subscription.endpoint, { ...options, redirect: "error", signal: AbortSignal.timeout(8000) });
    if (response.status === 404 || response.status === 410)
      await env.DB.prepare("DELETE FROM subscriptions WHERE store = ? AND endpoint = ?").bind(store, subscription.endpoint).run();
    return response.status;
  }));
  const accepted = deliveries.filter(item => item.status === "fulfilled" && item.value >= 200 && item.value < 300).length;
  const rejected = deliveries.length - accepted;
  if (rejected) console.warn(JSON.stringify({ event: "push_rejected", rejected, accepted }));
  return { subscribed: results.length, accepted };
}

async function saveThumbnail(env, jpeg, store, eventId, now) {
  // Base64 of a JPEG, up to 24 KiB decoded.
  if (typeof jpeg !== "string" || jpeg.length > 32768 || jpeg.length % 4 !== 0 ||
      !/^\/9j\/[A-Za-z0-9+/]*={0,2}$/.test(jpeg)) return null;
  const token = crypto.randomUUID().replaceAll("-", "");
  await env.DB.prepare("INSERT INTO event_thumbnails(token,jpeg,expires_at,store,event_id) VALUES(?,?,?,?,?)")
    .bind(token, jpeg, now + 600000, store, eventId).run();
  return token;
}

async function api(request, env, ctx) {
  const path = new URL(request.url).pathname;
  const method = request.method;
  const update = await androidUpdate(request, env);
  if (update) return update;
  if (path === "/api/push-key" && method === "GET") return json({ key: env.VAPID_PUBLIC });
  const preview = method === "GET" && path.match(/^\/api\/thumbnail\/([a-f0-9]{32})$/);
  if (preview) {
    const image = await env.DB.prepare("SELECT jpeg FROM event_thumbnails WHERE token=? AND expires_at>?")
      .bind(preview[1], Date.now()).first();
    if (!image) return error("Imagem indisponível.", 404);
    return new Response(Uint8Array.from(atob(image.jpeg), character => character.charCodeAt(0)), {
      headers: { "Content-Type": "image/jpeg", "Cache-Control": "private, no-store", "X-Content-Type-Options": "nosniff" }
    });
  }
  if (path === "/api/invite/redeem" && method === "POST") return redeemInvite(request, env);
  const token = request.headers.get("Authorization")?.match(/^Bearer ([a-z0-9.]{64,132})$/)?.[1];
  if (!token) return error("Código de acesso necessário.", 401);
  const writer = method === "POST" && ["/api/activate", "/api/events", "/api/heartbeat", "/api/invite"].includes(path);
  let store;
  if (tokenPattern.test(token)) {
    const reader = writer ? await hash("read:" + token) : token;
    store = await hash("store:" + reader);
  } else {
    const viewer = !writer && token.match(/^v1\.([a-f0-9]{64})\.([a-f0-9]{64})$/);
    if (!viewer || !sameSecret(viewer[2], await viewerSignature(viewer[1], env)))
      return error("Código de acesso inválido.", 401);
    store = viewer[1];
  }
  if (writer && path === "/api/activate") {
    if (!sameSecret(request.headers.get("X-Vigia-Enrollment"), env.ENROLL_KEY)) return error("Chave de instalação inválida.", 403);
    await env.DB.prepare("INSERT OR IGNORE INTO stores(id) VALUES(?)").bind(store).run();
    return json({ ok: true });
  }
  const record = await env.DB.prepare("SELECT last_seen,total_entries,counting_since FROM stores WHERE id = ?").bind(store).first();
  if (!record) return error("Esta loja ainda não foi vinculada.", 404);
  const now = Date.now();
  if (writer && path === "/api/invite") return createInvite(token, store, now, env);
  if (writer && path === "/api/heartbeat") {
    if (now - record.last_seen >= 15_000)
      await env.DB.prepare("UPDATE stores SET last_seen = ? WHERE id = ?").bind(now, store).run();
    return json({ ok: true });
  }
  if (writer && path === "/api/events") {
    let event;
    try { event = await body(request, 35 * 1024); } catch { return error("Evento inválido.", 400); }
    if (!event || !/^[a-f0-9-]{36}$/.test(event.id ?? "") || !["arrival", "test"].includes(event.kind))
      return error("Evento inválido.", 400);
    // The INSERT and its trigger atomically reserve the notice slot and count the entry.
    // UUID deduplication prevents retries from counting or notifying twice.
    const inserted = await env.DB.prepare(
      `INSERT OR IGNORE INTO events(id,store,time,kind,notified)
       SELECT ?,?,?,?, CASE WHEN ?='arrival'
         THEN CASE WHEN last_arrival_notice <= ? THEN 1 ELSE 0 END
         ELSE 1 END FROM stores WHERE id=?
       AND (?='arrival' OR NOT EXISTS(SELECT 1 FROM events WHERE store=? AND kind='test' AND time>?))
       RETURNING notified`
    ).bind(event.id, store, now, event.kind, event.kind, now - 20000, store,
      event.kind, store, now - 3500).all();
    const accepted = inserted.results.length > 0;
    const shouldNotify = accepted && Boolean(inserted.results[0].notified);
    let push;
    if (accepted) {
      const alert = { id: event.id, kind: event.kind };
      if (event.kind === "test") push = await notify(env, store, alert);
      else if (shouldNotify) ctx.waitUntil(notify(env, store, alert, event.jpeg));
      else if (event.jpeg) ctx.waitUntil(saveThumbnail(env, event.jpeg, store, event.id, now));
      ctx.waitUntil(env.DB.prepare("DELETE FROM events WHERE store=? AND (time < ? OR id IN (SELECT id FROM events WHERE store=? ORDER BY time DESC LIMIT -1 OFFSET 200))")
        .bind(store, now - 14 * 86400000, store).run());
    }
    return json({ ok: true, accepted, notified: shouldNotify, push });
  }
  if (method === "GET" && path === "/api/events") {
    const cursor = Number(new URL(request.url).searchParams.get("after"));
    const after = Number.isSafeInteger(cursor) && cursor > 0 ? cursor : 0;
    const { results } = await env.DB.prepare(
      "SELECT e.id,e.time,e.kind,e.notified,t.token IS NOT NULL AS hasThumbnail FROM events e LEFT JOIN event_thumbnails t ON e.store=t.store AND e.id=t.event_id AND t.expires_at>? WHERE e.store=? AND e.time>? ORDER BY e.time DESC LIMIT 60"
    ).bind(now, store, Math.max(after, now - 14 * 86400000)).all();
    const events = results.map(event => ({ ...event, hasThumbnail: Boolean(event.hasThumbnail), notified: Boolean(event.notified) }));
    return json({ events, totalEntries: record.total_entries, countingSince: record.counting_since, noticeIntervalSeconds: 20, lastSeen: record.last_seen, online: now - record.last_seen < 95000 });
  }
  const mobilePhoto = method === "GET" && path.match(/^\/api\/event-thumbnail\/([a-f0-9-]{36})$/);
  if (mobilePhoto) {
    const photo = await env.DB.prepare("SELECT jpeg FROM event_thumbnails WHERE store=? AND event_id=? AND expires_at>?")
      .bind(store, mobilePhoto[1], now).first();
    if (!photo) return error("Imagem indisponível.", 404);
    return new Response(Uint8Array.from(atob(photo.jpeg), character => character.charCodeAt(0)), {
      headers: { "Content-Type": "image/jpeg", "Cache-Control": "private, no-store", "X-Content-Type-Options": "nosniff" }
    });
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
  async scheduled(_event, env) {
    await env.DB.prepare("DELETE FROM event_thumbnails WHERE expires_at <= ?").bind(Date.now()).run();
  },
  async fetch(request, env, ctx) {
    const url = new URL(request.url);
    if (!url.pathname.startsWith("/api/")) {
      if (url.hostname === "vigia-local.arthurgabrielfera.workers.dev" &&
          url.pathname === "/" && !url.searchParams.has("app") && request.method === "GET") {
        return Response.redirect("https://vigia-local.pages.dev/" + url.search, 302);
      }
      return env.ASSETS.fetch(request);
    }
    try { return await api(request, env, ctx); }
    catch {
      console.error(JSON.stringify({ event: "api_error" }));
      return error("Serviço temporariamente indisponível.", 503);
    }
  }
};
