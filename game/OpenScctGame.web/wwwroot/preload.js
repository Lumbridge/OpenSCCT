globalThis.openscctLoadSettings = scope => localStorage.getItem('openscct.settings.v2.' + scope) || '{}';
globalThis.openscctSaveSettings = (scope, data) => localStorage.setItem('openscct.settings.v2.' + scope, data);
globalThis.openscctLoadPendingProfile = id => localStorage.getItem('openscct.profile.pending.' + id) || 'null';
globalThis.openscctSavePendingProfile = (id, data) => data ? localStorage.setItem('openscct.profile.pending.' + id, data) : localStorage.removeItem('openscct.profile.pending.' + id);
globalThis.openscctAccountStatus = (name, message, needsChoice = false) => {
    const account = document.getElementById('account-link');
    if (account) { account.textContent = name || 'Sign in'; account.href = name ? '/account' : '/auth/player/login?returnTo=' + encodeURIComponent(location.pathname + location.search); }
    const status = document.getElementById('account-status');
    if (status) status.textContent = message;
    const resolve = document.getElementById('sync-account-button');
    if (resolve) resolve.hidden = !needsChoice;
};
globalThis.openscctOpenAccount = () => {
    const link = document.getElementById('account-link');
    if (link) window.top.location.href = link.href;
};
globalThis.openscctSignedOut = () => location.reload();
globalThis.openscctRuntimeReady = async () => {
    const exports = await globalThis.getDotnetRuntime(0).getAssemblyExports('OpenScctGame.web.dll');
    globalThis.openscctExports = exports.OpenScctGame.Web.BrowserExports;
    document.getElementById('multiplayer-button').disabled = false;
};
const localDiagnostics = /^127\.(?:\d{1,3}\.){2}\d{1,3}$/.test(location.hostname) || ['localhost', '[::1]'].includes(location.hostname);
const diagnosticInterval = new URLSearchParams(location.search).has('profile') ? 0 : 2000;
let lastDiagnostic = 0;
globalThis.openscctReportState = json => {
    const state = JSON.parse(json);
    window.__openscctProbe = state;
    const button = document.getElementById('play-button');
    if (button) button.hidden = !state.loaded || state.captured || state.menu || state.inputActive;
    const menu = document.getElementById('menu-button');
    if (menu) menu.disabled = !state.loaded;
    if (state.error) globalThis.openscctGameFailed?.(state.error);
    else if (state.loaded || state.accountConflict) globalThis.openscctGameReady?.();
    const coach = document.getElementById('lesson-coach');
    if (coach) {
        coach.hidden = !state.lesson || state.menu;
        if (state.lesson) {
            const lesson = state.lesson;
            document.getElementById('lesson-title').textContent = lesson.kind === 'spy-grab' ? 'Grabbing practice' : 'Vision cancelling';
            document.getElementById('lesson-feedback').textContent = lesson.feedback;
            const vision = {0: 'Normal vision', 5: 'Normal vision', 34: 'Night vision', 36: 'Thermal vision'}[lesson.vision] || 'Vision ' + lesson.vision;
            const action = lesson.state.includes('Landing') ? 'Landing recovery' : lesson.state.includes('Falling') ? 'Falling' : lesson.state === 's_GrabbingPlayer' ? 'Holding mercenary' : lesson.state.includes('Grabbing') ? 'Starting grab' : 'Ready';
            document.getElementById('lesson-detail').textContent = (lesson.distance == null ? vision : Math.round(lesson.distance) + ' / 160 units') + ' · ' + action;
            document.getElementById('lesson-detail').hidden = lesson.kind === 'spy-grab' && !lesson.debug;
            const results = document.getElementById('lesson-results');
            results.hidden = lesson.kind !== 'vision-cancelling' || !lesson.history?.length;
            if (!results.hidden) {
                const summary = document.createElement('p');
                summary.textContent = `Attempts: ${lesson.attempts} · Baseline: ${lesson.baselineMs == null ? 'try without vision' : Math.round(lesson.baselineMs) + ' ms'} · Best: ${lesson.bestMs == null ? '—' : Math.round(lesson.bestMs) + ' ms'}`;
                const list = document.createElement('ol');
                for (const attempt of lesson.history.slice(-3).reverse()) {
                    const row = document.createElement('li');
                    row.textContent = `#${attempt.Number} ${attempt.Result} · ${Math.round(attempt.RecoveryMs)} ms` +
                        (attempt.SwitchMs == null ? '' : ` · switch ${attempt.SwitchMs < 0 ? Math.round(-attempt.SwitchMs) + ' ms early' : Math.round(attempt.SwitchMs) + ' ms after touchdown'}`);
                    list.append(row);
                }
                results.replaceChildren(summary, list);
            }
            for (const control of coach.querySelectorAll('button')) {
                control.disabled = !lesson.ready || control.dataset.action === 'drop' && lesson.recoveryMs != null;
                control.hidden = control.dataset.lesson && control.dataset.lesson !== lesson.kind;
            }
            const moving = document.getElementById('lesson-moving');
            moving.textContent = lesson.moving ? 'Stop mercenary' : 'Moving mercenary';
            moving.setAttribute('aria-pressed', String(lesson.moving));
            coach.querySelector('[data-action="debug"]').setAttribute('aria-pressed', String(lesson.debug));
        }
    }
    if (localDiagnostics && performance.now() - lastDiagnostic > diagnosticInterval) {
        lastDiagnostic = performance.now();
        console.log('OPENSCCT_BROWSER_PROBE ' + json);
    }
};
// Local development evidence only. A hosted game never uploads console messages.
if (localDiagnostics) for (const level of ['log', 'warn', 'error']) {
    const original = console[level].bind(console);
    console[level] = (...args) => {
        original(...args);
        const message = args.map(value => String(value)).join(' ');
        fetch('/__log', {method: 'POST', headers: {'Content-Type': 'application/json'},
            body: JSON.stringify({level, message, time: performance.now()})}).catch(() => {});
    };
}
function reportRuntimeFailure(error) {
    if (error instanceof WebAssembly.RuntimeError)
        globalThis.openscctGameFailed?.('The game could not start. Reload the page to try again.');
}
addEventListener('error', event => {
    console.error('PAGE_ERROR', event.message);
    reportRuntimeFailure(event.error);
});
addEventListener('unhandledrejection', event => {
    console.error('PAGE_REJECTION', event.reason);
    reportRuntimeFailure(event.reason);
});
if (localDiagnostics) console.log('OPENSCCT_BROWSER_ENV', navigator.userAgent, 'secure=' + isSecureContext,
    'webgpu=' + Boolean(navigator.gpu));
if (localDiagnostics) navigator.gpu?.requestAdapter().then(adapter => console.log('OPENSCCT_BROWSER_ADAPTER',
    JSON.stringify(adapter ? {info: adapter.info, maxStorageBuffersPerShaderStage:
        adapter.limits.maxStorageBuffersPerShaderStage, maxSampledTexturesPerShaderStage:
        adapter.limits.maxSampledTexturesPerShaderStage} : null)));
// Opt-in: fetch the .gz siblings the 2dog publish writes (engine, pack
// and _framework binaries) and inflate them in the page. For hosts that
// serve everything uncompressed. Publishing with
// TwoDogWebPrecompressKeepOriginals=false switches it on in the app
// bundle. A host that already content-encodes would compress them a
// second time.
const TWODOG_PCK_GZ = false;
// The hints must name the files the engine fetches, or both download.
const twodogGz = TWODOG_PCK_GZ && typeof DecompressionStream !== 'undefined';
for (const file of ['godot.wasm', 'godot.pck']) {
    const hint = document.createElement('link');
    hint.rel = 'preload';
    hint.as = 'fetch';
    hint.crossOrigin = 'anonymous';
    hint.href = twodogGz ? file + '.gz' : file;
    document.head.appendChild(hint);
}
