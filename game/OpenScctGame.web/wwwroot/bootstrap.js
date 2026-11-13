const gameCanvas = document.getElementById('canvas');
document.getElementById('sync-account-button').addEventListener('click', () => {
    globalThis.openscctExports?.UseAccountSettings();
    location.reload();
});
document.getElementById('play-button').addEventListener('click', () => {
    gameCanvas.focus();
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
    if (wasCaptured && !captured) globalThis.openscctExports?.DeactivateControls();
    wasCaptured = captured;
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
