self.addEventListener("install", event => event.waitUntil(self.skipWaiting()));
self.addEventListener("activate", event => event.waitUntil(self.clients.claim()));
self.addEventListener("push", event => {
  const alert = event.data?.json();
  if (!alert) return;
  const test = alert.kind === "test";
  const people = alert.people ?? 1;
  const thumbnail = typeof alert.thumbnail === "string" && /^[a-f0-9]{32}$/.test(alert.thumbnail)
    ? alert.thumbnail : null;
  event.waitUntil(self.registration.showNotification(test ? "Teste do Vigia Local" : "Entrada registrada", {
    body: test ? "Os avisos chegaram a este aparelho." : people === 1 ? "Uma pessoa chegou." : `${people} pessoas chegaram.`,
    icon: "/icon-192.png", tag: test ? "vigia-test" : "vigia-arrival", renotify: true,
    silent: false, vibrate: [180, 90, 180],
    ...(thumbnail ? { image: "/api/thumbnail/" + thumbnail } : {}),
    data: { url: thumbnail ? "/?app#photo=" + thumbnail : "/?app" }
  }));
});
self.addEventListener("notificationclick", event => {
  event.notification.close();
  event.waitUntil(self.clients.openWindow(event.notification.data.url));
});
