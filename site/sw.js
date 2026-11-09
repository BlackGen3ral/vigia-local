self.addEventListener("install", event => event.waitUntil(self.skipWaiting()));
self.addEventListener("activate", event => event.waitUntil(self.clients.claim()));
self.addEventListener("push", event => {
  const alert = event.data?.json();
  if (!alert) return;
  const test = alert.kind === "test";
  event.waitUntil(self.registration.showNotification(test ? "Teste do Vigia Local" : "Pessoa na entrada", {
    body: test ? "Os avisos chegaram a este aparelho." : "Uma pessoa passou pela área marcada.",
    icon: "/icon-192.png", tag: alert.id, data: { url: "/?app" }
  }));
});
self.addEventListener("notificationclick", event => {
  event.notification.close();
  event.waitUntil(self.clients.openWindow(event.notification.data.url));
});
