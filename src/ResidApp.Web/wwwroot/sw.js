// Service worker mínimo de ResidApp. Ver docs/decisiones-arquitectura/directrices-pwa-movil.md.
//
// Alcance deliberadamente limitado: cachea el shell estático de la aplicación y sirve /Home/Offline
// como fallback de navegación cuando no hay red. NO intercepta peticiones POST (los envíos de
// formularios clínicos nunca pasan por aquí) y NO cachea ni reintenta escrituras: la única defensa
// ante un reintento por pérdida de cobertura es la idempotencia por operationId en el servidor
// (dbo.idempotency_operations), no este service worker.

const CACHE_NAME = "residapp-shell-v2";
const OFFLINE_URL = "/Home/Offline";
const SHELL_ASSETS = [
  OFFLINE_URL,
  "/css/site.css",
  "/css/residapp-theme.css",
  "/ResidApp.Web.styles.css",
  "/js/site.js",
  "/js/tema.js",
  "/images/logo.svg",
  "/lib/bootstrap/dist/css/bootstrap.min.css",
  "/lib/bootstrap/dist/js/bootstrap.bundle.min.js",
];

self.addEventListener("install", (event) => {
  event.waitUntil(
    caches
      .open(CACHE_NAME)
      .then((cache) => cache.addAll(SHELL_ASSETS))
      .then(() => self.skipWaiting())
  );
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) => Promise.all(keys.filter((key) => key !== CACHE_NAME).map((key) => caches.delete(key))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener("fetch", (event) => {
  // Solo GET: los envíos de formularios (POST) siguen su curso normal, sin pasar por el service worker.
  if (event.request.method !== "GET") {
    return;
  }

  if (event.request.mode === "navigate") {
    event.respondWith(fetch(event.request).catch(() => caches.match(OFFLINE_URL)));
    return;
  }

  // Red primero: con conexión siempre llega la versión actual. Sin red, la copia del shell; ignoreSearch porque el
  // layout pide las hojas y scripts con ?v= (asp-append-version) y el shell los guarda sin él.
  event.respondWith(
    fetch(event.request).catch(() =>
      caches.match(event.request, { ignoreSearch: true }).then((cached) => cached ?? Response.error())
    )
  );
});
