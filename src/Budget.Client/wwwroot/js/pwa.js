// Tells the app when the connection drops or returns, and when a new version is ready.
// Used by Connectivity.cs.

let waitingWorker = null;
let updateRequested = false;

export function start(dotnet) {
    const report = () => dotnet.invokeMethodAsync('OnConnectivityChanged', navigator.onLine);
    window.addEventListener('online', report);
    window.addEventListener('offline', report);
    report();

    if (!('serviceWorker' in navigator)) {
        return;
    }

    navigator.serviceWorker.addEventListener('controllerchange', () => {
        if (updateRequested) {
            window.location.reload();
        }
    });

    navigator.serviceWorker.register('service-worker.js', { updateViaCache: 'none' }).then(registration => {
        const offerUpdate = worker => {
            waitingWorker = worker;
            dotnet.invokeMethodAsync('OnUpdateAvailable');
        };

        // A newer version may already have been downloaded on an earlier visit.
        if (registration.waiting && navigator.serviceWorker.controller) {
            offerUpdate(registration.waiting);
        }

        registration.addEventListener('updatefound', () => {
            const worker = registration.installing;
            worker?.addEventListener('statechange', () => {
                // Only an update if a version is already running; otherwise it's the first install.
                if (worker.state === 'installed' && navigator.serviceWorker.controller) {
                    offerUpdate(worker);
                }
            });
        });

        // Check for a new version every hour while the app stays open.
        setInterval(() => registration.update().catch(() => { }), 60 * 60 * 1000);
    }).catch(() => { });
}

export function applyUpdate() {
    updateRequested = true;
    if (waitingWorker) {
        waitingWorker.postMessage({ type: 'SKIP_WAITING' });
    } else {
        window.location.reload();
    }
}
