// In development the service worker caches nothing, so every build is picked up on reload.
// The published app uses service-worker.published.js (see the Viewer's .csproj).
self.addEventListener('fetch', () => { });
