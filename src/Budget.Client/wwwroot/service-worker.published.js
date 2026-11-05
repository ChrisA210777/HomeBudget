// Caches the app shell (the framework files, scripts, styles and icons listed in
// service-worker-assets.js) so the app opens offline and starts quickly.
//
// It never caches API responses or any other household data: requests that aren't for a
// listed asset always go to the network.

self.importScripts('./service-worker-assets.js');
self.addEventListener('install', event => event.waitUntil(onInstall()));
self.addEventListener('activate', event => event.waitUntil(onActivate()));
self.addEventListener('fetch', event => event.respondWith(onFetch(event)));

// The page asks a waiting worker to take over when the person chooses "Reload" on the
// "Update available" banner.
self.addEventListener('message', event => {
    if (event.data?.type === 'SKIP_WAITING') {
        self.skipWaiting();
    }
});

const cacheNamePrefix = 'offline-cache-';
const cacheName = `${cacheNamePrefix}${self.assetsManifest.version}`;
const offlineAssetsInclude = [/\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/, /\.css$/, /\.woff2?$/, /\.png$/, /\.svg$/, /\.ico$/, /\.blat$/, /\.dat$/, /\.webmanifest$/];
const offlineAssetsExclude = [/^service-worker\.js$/];

// Navigations to these go to the server, never to the cached app: signing in and out,
// the sign-in provider's callback, the API and the health check.
const serverPaths = ['/account/', '/signin-', '/bff/', '/api/', '/health'];

const base = '/';
const baseUrl = new URL(base, self.origin);
const manifestUrlList = self.assetsManifest.assets.map(asset => new URL(asset.url, baseUrl).href);

async function onInstall() {
    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(pattern => pattern.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(pattern => pattern.test(asset.url)))
        // index.html is rewritten when the app is built, so its hash isn't checked.
        .map(asset => new Request(asset.url, asset.url.endsWith('.html')
            ? { cache: 'no-cache' }
            : { integrity: asset.hash, cache: 'no-cache' }));
    const cache = await caches.open(cacheName);
    await cache.addAll(assetsRequests);
}

async function onActivate() {
    const cacheKeys = await caches.keys();
    await Promise.all(cacheKeys
        .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
        .map(key => caches.delete(key)));
}

async function onFetch(event) {
    const request = event.request;
    if (request.method !== 'GET') {
        return fetch(request);
    }

    const url = new URL(request.url);
    const isServerPath = url.origin === self.origin && serverPaths.some(path => url.pathname.startsWith(path));

    // Client-side routes (e.g. /income) are served from the cached index.html.
    const shouldServeIndexHtml = request.mode === 'navigate'
        && !isServerPath
        && !manifestUrlList.some(listed => listed === request.url);

    const cache = await caches.open(cacheName);
    const cached = await cache.match(shouldServeIndexHtml ? 'index.html' : request);
    return cached || fetch(request);
}
