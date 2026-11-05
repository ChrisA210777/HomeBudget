// In development the service worker does nothing, so you always get the latest build.
// The published app uses service-worker.published.js instead.
self.addEventListener('fetch', () => { });
self.addEventListener('message', event => {
    if (event.data?.type === 'SKIP_WAITING') {
        self.skipWaiting();
    }
});
