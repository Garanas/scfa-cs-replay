/*
 * Retires the service worker of the old GitHub Pages deployment (the MudBlazor viewer
 * registered service-worker.js and cached the app offline). The current viewer has no service
 * worker; browsers that still run the old one fetch this file on their next visit, install it
 * because it differs, and it then clears the caches, unregisters itself and reloads the open
 * tabs so they get the current app. Deployed by .github/workflows/pages.yml; it can go once
 * returning visitors have had time to pick it up.
 */
self.addEventListener("install", () => self.skipWaiting());

self.addEventListener("activate", (event) => {
    event.waitUntil((async () => {
        const keys = await caches.keys();
        await Promise.all(keys.map((key) => caches.delete(key)));
        await self.registration.unregister();
        const windows = await self.clients.matchAll({ type: "window" });
        windows.forEach((client) => client.navigate(client.url));
    })());
});
