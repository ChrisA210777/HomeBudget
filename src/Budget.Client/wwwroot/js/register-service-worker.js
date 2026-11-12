// Caches the app shell so it opens offline and can be installed (BUD-37, BUD-38).
// The page listens for updates once the app has started (js/pwa.js).
// In its own file, not inline, so the Content Security Policy allows no inline scripts (BUD-143).
if ('serviceWorker' in navigator) {
    navigator.serviceWorker.register('service-worker.js', { updateViaCache: 'none' });
}
