const gameCanvas = document.getElementById('canvas');
// Request directly in the trusted DOM gesture. Godot also asks for capture; coalesce that request
// and retain raw movement instead of issuing another request with different options.
const nativePointerLock = gameCanvas.requestPointerLock?.bind(gameCanvas);
let pendingPointerLock = null;
const pointerStatus = document.getElementById('pointer-status');
function requestGamePointerLock() {
    if (document.pointerLockElement === gameCanvas) return Promise.resolve();
    if (pendingPointerLock) return pendingPointerLock;
    if (!nativePointerLock) return Promise.resolve();
    let requested;
    try { requested = nativePointerLock({unadjustedMovement: true}); }
    catch (error) { requested = Promise.reject(error); }
    pendingPointerLock = Promise.resolve(requested).catch(error => {
        if (error.name === 'NotSupportedError') return nativePointerLock();
        throw error;
    }).catch(error => {
        pointerStatus.textContent = error.name === 'WrongDocumentError'
            ? 'Bring the Chrome game window to the foreground, then retry mouse capture. '
            : 'Mouse capture was refused. An embedded browser may not support it. Keyboard and lesson buttons are available. ';
        const retry = document.createElement('button');
        retry.type = 'button';
        retry.textContent = 'Retry mouse capture';
        retry.addEventListener('click', () => { gameCanvas.focus(); requestGamePointerLock(); globalThis.openscctExports?.ActivateControls(); });
        pointerStatus.append(retry);
        pointerStatus.hidden = false;
        console.warn('Mouse capture:', error.name, error.message);
    }).finally(() => { pendingPointerLock = null; });
    return pendingPointerLock;
}
if (nativePointerLock) gameCanvas.requestPointerLock = requestGamePointerLock;
document.getElementById('sync-account-button').addEventListener('click', () => {
    globalThis.openscctExports?.UseAccountSettings();
    location.reload();
});
document.getElementById('play-button').addEventListener('click', () => {
    gameCanvas.focus();
    requestGamePointerLock();
    globalThis.openscctExports?.ActivateControls();
    document.getElementById('play-button').hidden = true;
});
document.getElementById('menu-button').addEventListener('click', () => globalThis.openscctExports?.OpenMenu());
document.getElementById('fullscreen-button').addEventListener('click', () => {
    const change = document.fullscreenElement ? document.exitFullscreen() : document.documentElement.requestFullscreen();
    change.catch(error => console.warn('Full screen:', error.message));
});
document.addEventListener('fullscreenchange', () => {
    document.getElementById('fullscreen-button').textContent = document.fullscreenElement ? 'Exit full screen' : 'Full screen';
});
let wasCaptured = false;
document.addEventListener('pointerlockchange', () => {
    const captured = document.pointerLockElement === gameCanvas;
    document.body.classList.toggle('playing', captured);
    if (captured) pointerStatus.hidden = true;
    if (wasCaptured && !captured) {
        if (document.hasFocus() && !document.hidden) globalThis.openscctExports?.OpenMenu();
        else globalThis.openscctExports?.DeactivateControls();
    }
    wasCaptured = captured;
});
const onlineDialog = document.getElementById('multiplayer-dialog');
async function refreshServers() {
    const status = document.getElementById('online-status');
    status.textContent = 'Loading servers…';
    try {
        const response = await fetch('servers', {cache:'no-store'});
        if (!response.ok) throw new Error('The browser multiplayer gateway is unavailable on this host.');
        const result = await response.json();
        const select = document.getElementById('online-server');
        select.replaceChildren(...result.servers.map(server => {
            const option = document.createElement('option');
            option.value = server.id;
            option.textContent = `${server.name} · ${server.map} · ${server.players} players${server.password ? ' · password' : ''}`;
            return option;
        }));
        const needsInvite = result.inviteOnly && location.origin !== 'https://master.lumbridge.co.uk';
        document.getElementById('online-invite').hidden = !needsInvite;
        document.getElementById('invite-label').hidden = !needsInvite;
        document.getElementById('online-join').disabled = !result.servers.length;
        status.textContent = result.servers.length ?
            (document.getElementById('account-link').getAttribute('href') === '/account' ? 'Your verified account will be used in matches.' : 'Sign in to use your verified account in matches.') :
            'No browser-enabled game servers are currently listed.';
    } catch (error) { status.textContent = error.message; }
}
globalThis.openscctOpenMultiplayer = () => {
    globalThis.openscctExports?.DeactivateControls();
    onlineDialog.showModal();
    refreshServers();
};
globalThis.openscctOnlineStatus = message => {
    if (message === 'This master needs its invite key.') {
        document.getElementById('online-invite').hidden = false;
        document.getElementById('invite-label').hidden = false;
    }
    document.getElementById('online-status').textContent = message;
    document.getElementById('online-join').disabled = false;
    if (!message) { onlineDialog.close(); gameCanvas.focus(); }
};
document.getElementById('multiplayer-button').addEventListener('click', globalThis.openscctOpenMultiplayer);
document.getElementById('online-refresh').addEventListener('click', refreshServers);
document.getElementById('online-close').addEventListener('click', () => onlineDialog.close());
document.getElementById('online-join').addEventListener('click', () => {
    if (!globalThis.openscctExports) { document.getElementById('online-status').textContent = 'The game is still loading.'; return; }
    document.getElementById('online-status').textContent = 'Connecting…';
    document.getElementById('online-join').disabled = true;
    globalThis.openscctExports?.JoinServer(document.getElementById('online-server').value,
        document.getElementById('online-password').value, document.getElementById('online-invite').value);
});
addEventListener('blur', () => globalThis.openscctExports?.DeactivateControls());
document.addEventListener('visibilitychange', () => {
    if (document.hidden) globalThis.openscctExports?.DeactivateControls();
});
addEventListener('pagehide', () => globalThis.openscctExports?.FlushSettings());
document.querySelectorAll('[data-action]').forEach(button => button.addEventListener('click', () => {
    globalThis.openscctExports?.LessonAction(button.dataset.action);
    if (button.dataset.action !== 'reset') gameCanvas.focus();
}));
const lessonArg = new URLSearchParams(location.search).get('lesson');
const browserLesson = ['vision-cancelling', 'spy-grab', 'merc'].includes(lessonArg) ? lessonArg : 'spy';
const GODOT_CONFIG = {
    'renderingDriver': 'webgpu',
    'args': ['--rendering-driver', 'webgpu', '--rendering-method',
        new URLSearchParams(location.search).has('forward-plus') ? 'forward_plus' : 'mobile', '--',
        '--svm-browser-content-url=' + new URL('content.zip', location.href).href,
        '--svm-browser-lesson=' + browserLesson,
        ...(new URLSearchParams(location.search).has('no-post') ? ['--svm-browser-no-post'] : [])],
    'canvasResizePolicy': 2,
    'ensureCrossOriginIsolationHeaders': false,
    'executable': 'godot',
    'experimentalVK': false,
    'focusCanvas': true,
    'gdextensionLibs': []
};
const GODOT_THREADS_ENABLED = false;

(function () {
    performance.mark('2dog:boot');

    const statusOverlay = document.getElementById('status');
    const statusBar = document.getElementById('status-bar');
    const statusNotice = document.getElementById('status-notice');

    let initializing = true;
    let statusMode = '';

    function setStatusMode(mode) {
        if (statusMode === mode || !initializing) {
            return;
        }
        if (mode === 'hidden') {
            statusOverlay.remove();
            initializing = false;
            return;
        }
        statusBar.style.display = mode === 'progress' ? 'block' : 'none';
        statusNotice.style.display = mode === 'notice' ? 'block' : 'none';
        statusMode = mode;
    }

    function displayFailureNotice(err) {
        console.error(err);
        statusNotice.textContent = typeof err?.message === 'string' ? err.message : String(err);
        setStatusMode('notice');
        initializing = false;
    }
    globalThis.openscctGameReady = () => setStatusMode('hidden');
    globalThis.openscctGameFailed = displayFailureNotice;

    let downloadsDone = false;

    function reportProgress(current, total) {
        if (!downloadsDone && total > 0 && current >= total) {
            downloadsDone = true;
            performance.mark('2dog:downloads-done');
        }
    }

    // The gz pack path re-points this at a merged reporter.
    let engineProgress = reportProgress;

    function startWithGzPack(fileSizes, makeEngine) {
        // A rejected probe (network error, service worker failure)
        // falls back to the uncompressed pack exactly like a 404.
        return fetch('godot.pck.gz').catch(() => null).then((response) => {
            if (!response || !response.ok || !response.body) {
                console.warn('2dog: godot.pck.gz unavailable, falling back to godot.pck');
                return makeEngine('').startGame({});
            }
            const engine = makeEngine('.gz');
            // The engine only downloads the wasm here; this page fetches
            // and inflates the pack, merging both into one download
            // timing (compressed pack bytes against its Content-Length).
            let pckLoaded = 0;
            let wasmLoaded = 0;
            let wasmTotal = fileSizes['godot.wasm'] || 0;
            const pckTotal = Number(response.headers.get('Content-Length')) || 0;
            const report = () => reportProgress(wasmLoaded + pckLoaded,
                wasmTotal > 0 && pckTotal > 0 ? wasmTotal + pckTotal : 0);
            engineProgress = (current, total) => {
                wasmLoaded = current;
                if (total > 0) wasmTotal = total;
                report();
            };
            const counted = new TransformStream({
                transform(chunk, controller) {
                    pckLoaded += chunk.byteLength;
                    report();
                    controller.enqueue(chunk);
                },
            });
            const pack = new Response(
                response.body.pipeThrough(counted).pipeThrough(new DecompressionStream('gzip'))).arrayBuffer();
            return Promise.all([
                engine.init(GODOT_CONFIG['executable']),
                pack.then((buffer) => engine.preloadFile(buffer, 'godot.pck')),
            ]).then(() => engine.start({ 'args': ['--main-pack', 'godot.pck'].concat(GODOT_CONFIG['args']) }));
        });
    }

    function reportTimings() {
        performance.mark('2dog:first-frame');
        const total = performance.measure('2dog:total', '2dog:boot', '2dog:first-frame').duration;
        let text = '2dog: first frame ' + Math.round(total) + ' ms after boot';
        if (downloadsDone) {
            const tail = performance.measure('2dog:tail', '2dog:downloads-done', '2dog:first-frame').duration;
            text += ' (' + Math.round(total - tail) + ' ms downloading, ' + Math.round(tail) + ' ms starting)';
        }
        console.log(text);
        // Detail table on demand (?2dog-timing). transferSize reads 0
        // for memory-cache hits - measure with caching disabled.
        if (!new URLSearchParams(location.search).has('2dog-timing')) {
            return;
        }
        const rows = performance.getEntriesByType('resource')
            .filter((entry) => entry.transferSize > 100000)
            .sort((a, b) => b.transferSize - a.transferSize)
            .map((entry) => ({
                file: entry.name.replace(/.*\//, ''),
                'wire kB': Math.round(entry.transferSize / 1000),
                ms: Math.round(entry.duration),
            }));
        if (rows.length) console.table(rows);
    }

    const missing = Engine.getMissingFeatures({
        threads: GODOT_THREADS_ENABLED,
        renderingDriver: GODOT_CONFIG['renderingDriver'],
    });

    if (missing.length !== 0) {
        const missingMsg = 'Error\nThe following features required to run Godot projects on the Web are missing:\n';
        displayFailureNotice(missingMsg + missing.join('\n'));
        return;
    }

    // Exact sizes written by the publish split the first-frame timing
    // into downloading and starting (Content-Length is the compressed
    // size on hosts that gzip, so headers lie).
    fetch('twodog.sizes.json')
        .then((response) => (response.ok ? response.json() : {}))
        .catch(() => ({}))
        .then((fileSizes) => {
            // The suffix decides which files the engine fetches, so
            // the Engine is only constructed after the pack probe has
            // picked a path - a '.gz'-configured engine in the
            // fallback would re-request the sibling that just 404'd.
            const makeEngine = (suffix) => {
                GODOT_CONFIG['fileSizes'] = fileSizes;
                GODOT_CONFIG['onProgress'] = (current, total) => engineProgress(current, total);
                // Newer engines also inflate .gz siblings themselves
                // (godot.wasm.gz included); older ones ignore this key.
                GODOT_CONFIG['precompressedSuffix'] = suffix;
                return new Engine(GODOT_CONFIG);
            };
            setStatusMode('progress');
            const started = twodogGz
                ? startWithGzPack(fileSizes, makeEngine)
                : makeEngine('').startGame({});
            return started.then(() => {
                reportTimings();
                const text = document.getElementById('loading-copy');
                if (text) text.textContent = 'Loading TestRange';
                if (window.__openscctProbe?.loaded) setStatusMode('hidden');
            }, displayFailureNotice);
        })
        .catch(displayFailureNotice);
}());
