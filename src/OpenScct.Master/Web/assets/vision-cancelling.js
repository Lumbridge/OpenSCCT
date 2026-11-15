import * as THREE from './three.module.min.js';
import { GLTFLoader } from './three-GLTFLoader.js';
import { RULES, VisionTraining } from './vision-training.js';

const host = document.getElementById('vision-training');
if (host) start(host);

async function start(host) {
  const q = name => host.querySelector(`[data-vc="${name}"]`);
  const stage = q('stage'), exercise = new VisionTraining();
  let renderer;
  try { renderer = new THREE.WebGLRenderer({ antialias: true, powerPreference: 'low-power' }); }
  catch { q('error').textContent = 'This browser could not start WebGL. Try a browser with hardware acceleration enabled, or follow the written steps below.'; return; }
  renderer.setPixelRatio(Math.min(devicePixelRatio, 1.75));
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;
  const canvas = renderer.domElement;
  canvas.tabIndex = 0;
  canvas.setAttribute('role', 'application');
  canvas.setAttribute('aria-label', 'Playable spy training. W A S D move, Space drops, 1 night vision, 2 thermal, V switches vision, R retries, Escape pauses.');
  stage.prepend(canvas);

  const scene = new THREE.Scene();
  scene.background = new THREE.Color(0x111d18);
  scene.fog = new THREE.Fog(0x111d18, 22, 48);
  const camera = new THREE.PerspectiveCamera(47, 1, 0.1, 70);
  scene.add(new THREE.HemisphereLight(0xcde6d4, 0x242d31, 2.4));
  const sun = new THREE.DirectionalLight(0xf3f5dd, 3.1);
  sun.position.set(5, 13, -3); sun.castShadow = true;
  sun.shadow.mapSize.set(1024, 1024);
  Object.assign(sun.shadow.camera, { left: -9, right: 9, top: 10, bottom: -7, near: 1, far: 35 });
  sun.shadow.bias = -0.001; scene.add(sun);
  const rim = new THREE.PointLight(0x78d59a, 30, 16); rim.position.set(-4, 5, 4); scene.add(rim);

  const materials = [];
  const material = (color, extra = {}) => {
    const m = new THREE.MeshStandardMaterial({ color, roughness: 0.85, ...extra });
    materials.push({ m, color: new THREE.Color(color) }); return m;
  };
  const concrete = material(0x4c5c53), darkConcrete = material(0x293b31), metal = material(0x1b2b24, { metalness: 0.5 });
  const pale = material(0xb8d9bd), yellow = material(0xc5b873);
  function box(w, h, d, x, y, z, mat, parent = scene) {
    const mesh = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), mat);
    mesh.position.set(x, y, z); mesh.receiveShadow = true; mesh.castShadow = true; parent.add(mesh); return mesh;
  }
  box(22, 0.4, 26, 0, -0.22, 3, darkConcrete);
  const grid = new THREE.GridHelper(22, 22, 0x54735f, 0x344c3e); grid.position.set(0, 0.006, 3); scene.add(grid);
  // An open front exposes the ledge. Rear walls, ribs and floor marks make height and movement readable.
  box(22, 10, 0.3, 0, 4.8, 12, concrete);
  box(0.3, 10, 20, -10, 4.8, 2, darkConcrete);
  for (let x = -9; x <= 9; x += 3) {
    box(0.15, 9, 0.15, x, 4.4, 11.76, metal);
    box(1.9, 0.07, 0.08, x, 6.5, 11.55, material(0xbad4bc, { emissive: 0x82ae8a, emissiveIntensity: 0.7 }));
  }
  const platform = new THREE.Group(); scene.add(platform);
  box(3.6, 0.28, 4.6, 0, -0.14, -2.3, concrete, platform);
  box(3.6, 0.08, 0.13, 0, 0.015, -0.03, yellow, platform);
  for (const side of [-1, 1]) {
    for (const z of [-4.4, -2.5, -0.6]) box(0.07, 0.95, 0.07, side * 1.72, 0.48, z, metal, platform);
    box(0.11, 0.06, 3.9, side * 1.72, 0.98, -2.5, pale, platform);
    box(0.28, 5.8, 0.28, side * 1.52, -2.95, -3.8, metal, platform);
  }
  box(4.4, 0.015, 0.09, 0, 0.008, 1.8, pale);
  box(4.4, 0.015, 0.09, 0, 0.008, 4.2, pale);
  for (const x of [-2.2, 2.2]) box(0.09, 0.015, 2.5, x, 0.008, 3, pale);
  for (let z = 5; z < 10; z += 0.9) box(0.08, 0.015, 0.32, 0, 0.008, z, pale);
  const landingRing = new THREE.Mesh(new THREE.RingGeometry(0.46, 0.51, 48), new THREE.MeshBasicMaterial({ color: 0xa4e9b0, transparent: true, opacity: 0.65, side: THREE.DoubleSide }));
  landingRing.rotation.x = -Math.PI / 2; landingRing.position.y = 0.018; scene.add(landingRing);

  const spy = new THREE.Group(); scene.add(spy);
  let mixer, currentAction, model, ready = false, paused = false, lastVision = '', mode = 'guided';
  let successes = 0, attempts = 0, streak = 0, counted = false, demoRound = 0;
  const actions = {}, keys = new Set(), held = new Set();
  const viewTarget = new THREE.Vector3(), viewPosition = new THREE.Vector3();
  const resize = () => {
    const w = stage.clientWidth, h = stage.clientHeight;
    if (!w || !h) return;
    renderer.setSize(w, h, false); camera.aspect = w / h; camera.updateProjectionMatrix();
  };
  new ResizeObserver(resize).observe(stage); resize();

  function animate(name, immediate = false) {
    const next = actions[name];
    if (!next || next === currentAction) return;
    const previous = currentAction;
    next.reset().setEffectiveWeight(1).setEffectiveTimeScale(1).play();
    if (previous) previous.crossFadeTo(next, immediate ? 0.06 : 0.16, false);
    currentAction = next;
  }
  function verdict(title, detail, outcome = '') {
    q('verdict').textContent = title; q('detail').textContent = detail;
    q('result').dataset.outcome = outcome;
  }
  function stats() {
    q('successes').textContent = `${successes} / ${attempts}`; q('streak').textContent = String(streak);
  }
  function clearKeys() { keys.clear(); held.clear(); }
  function focus() { canvas.focus({ preventScroll: true }); }
  function reset() {
    clearKeys(); paused = false; counted = false; currentAction = null;
    mixer?.stopAllAction();
    const height = mode === 'challenge' ? 4.6 + Math.random() * 1.2 : Number(q('height').value);
    exercise.reset(height); platform.position.y = height;
    q('pause').textContent = 'Pause'; q('pause').setAttribute('aria-label', 'Pause training');
    q('drop').innerHTML = mode === 'demo' ? 'Play demonstration' : 'Start drop <kbd>Space</kbd>';
    q('result-label').textContent = mode === 'demo' ? 'WATCH THE DIFFERENCE' : 'YOUR NEXT MOVE';
    q('input-marker').hidden = true;
    q('timing-note').textContent = 'The highlighted area is your practice window. Tap once as your feet touch the floor.';
    verdict(mode === 'demo' ? 'Two ways to land' : 'Step off the ledge', mode === 'demo'
      ? 'Watch a normal landing, then the same drop with a vision cancel. Demonstrations do not count towards your stats.'
      : 'Press Space to drop, or hold W to walk off. Tap 1, 2 or V as you land.');
    animate('idle'); syncView(true);
  }
  function startDrop() {
    if (!ready) return;
    if (exercise.phase !== 'platform') reset();
    if (paused) setPaused(false);
    if (mode === 'demo') demoRound = 1;
    exercise.drop();
    q('drop').innerHTML = mode === 'demo' ? 'Replay demonstration' : 'Retry <kbd>R</kbd>';
    verdict(mode === 'demo' ? '1 / 2 · Normal landing' : 'Watch your feet', mode === 'demo'
      ? 'No vision input: the spy must finish the landing stagger before moving.'
      : mode === 'challenge' ? 'Switch vision at touchdown. There is no timing cue in Challenge.' : 'Tap once when your feet touch the floor. The landing cue helps you find the moment.');
    focus();
  }
  function changeVision(kind) {
    if (!ready || paused || mode === 'demo') return;
    kind ? exercise.toggle(kind) : exercise.cycle(); focus();
  }
  function setPaused(value) {
    if (!ready) return;
    paused = value; clearKeys();
    q('pause').textContent = paused ? 'Resume' : 'Pause';
    q('pause').setAttribute('aria-label', paused ? 'Resume training' : 'Pause training');
  }
  for (const button of host.querySelectorAll('.training-modes button')) button.addEventListener('click', () => {
    mode = button.dataset.mode; host.dataset.mode = mode; demoRound = 0;
    for (const b of host.querySelectorAll('.training-modes button')) b.setAttribute('aria-pressed', String(b === button));
    q('height').disabled = mode === 'challenge'; q('slow').disabled = mode === 'challenge';
    q('night').disabled = q('thermal').disabled = mode === 'demo' || !ready;
    q('mode-help').textContent = mode === 'demo' ? 'Compare the landing stagger with a successful cancel.' : mode === 'guided'
      ? 'Follow the landing cue, then try it without the hints.' : 'Varying drop heights. Full speed. No cue. Land three cancels in a row.';
    if (ready) reset();
  });
  q('drop').addEventListener('click', startDrop);
  q('night').addEventListener('click', () => changeVision('night'));
  q('thermal').addEventListener('click', () => changeVision('thermal'));
  q('pause').addEventListener('click', () => { setPaused(!paused); focus(); });
  q('height').addEventListener('change', () => { demoRound = 0; if (ready) reset(); });
  q('reset-stats').addEventListener('click', () => { successes = attempts = streak = 0; stats(); if (ready) reset(); });
  canvas.addEventListener('pointerdown', focus);
  host.addEventListener('keydown', e => {
    if (!ready || e.ctrlKey || e.altKey || e.metaKey || e.target.matches('input,select')) return;
    const movement = { KeyW: 'forward', KeyA: 'left', KeyS: 'back', KeyD: 'right', ArrowUp: 'forward', ArrowLeft: 'left', ArrowDown: 'back', ArrowRight: 'right' };
    const key = movement[e.code];
    if (key && e.target === canvas && mode !== 'demo') { e.preventDefault(); if (!paused) keys.add(key); return; }
    if (e.repeat) { if (['Space', 'Digit1', 'Digit2', 'KeyV', 'KeyR'].includes(e.code)) e.preventDefault(); return; }
    if (e.code === 'Space' && e.target === canvas) { e.preventDefault(); startDrop(); }
    else if (e.code === 'KeyR') { e.preventDefault(); startDrop(); }
    else if (e.code === 'Digit1') { e.preventDefault(); changeVision('night'); }
    else if (e.code === 'Digit2') { e.preventDefault(); changeVision('thermal'); }
    else if (e.code === 'KeyV') { e.preventDefault(); changeVision(); }
    else if (e.code === 'Escape' && e.target === canvas) { e.preventDefault(); setPaused(!paused); }
  });
  const keyName = { KeyW: 'forward', ArrowUp: 'forward', KeyA: 'left', ArrowLeft: 'left', KeyS: 'back', ArrowDown: 'back', KeyD: 'right', ArrowRight: 'right' };
  host.addEventListener('keyup', e => { if (keyName[e.code]) keys.delete(keyName[e.code]); });
  host.addEventListener('focusout', e => { if (!host.contains(e.relatedTarget)) clearKeys(); });
  for (const button of host.querySelectorAll('[data-move]')) {
    button.addEventListener('pointerdown', e => { if (!ready || paused || mode === 'demo') return; e.preventDefault(); button.setPointerCapture(e.pointerId); held.add(button.dataset.move); });
    for (const event of ['pointerup', 'pointercancel', 'lostpointercapture']) button.addEventListener(event, () => held.delete(button.dataset.move));
  }
  document.addEventListener('visibilitychange', () => { if (document.hidden) setPaused(true); });
  window.addEventListener('blur', () => { if (exercise.phase !== 'platform') setPaused(true); else clearKeys(); });
  canvas.addEventListener('webglcontextlost', e => { e.preventDefault(); setPaused(true); q('error').textContent = 'The 3D view lost its graphics context. Reload this page to restart training.'; });

  function syncView(snap = false) {
    spy.position.set(exercise.x, exercise.y, exercise.z);
    const targetY = Math.max(1.2, exercise.y * 0.7 + 0.8);
    viewTarget.set(exercise.x * 0.4, targetY, exercise.z * 0.4 + 0.4);
    viewPosition.set(exercise.x * 0.5 + 4.3, targetY + 3.5, exercise.z * 0.35 - 6.3);
    if (snap) camera.position.copy(viewPosition);
    else camera.position.lerp(viewPosition, 0.065);
    camera.lookAt(viewTarget);
    landingRing.position.x = exercise.x; landingRing.position.z = exercise.phase === 'platform' ? 2.2 : exercise.z;
    landingRing.visible = exercise.phase === 'falling' || exercise.phase === 'platform';
    stage.dataset.vision = exercise.vision;
    if (lastVision !== exercise.vision) {
      lastVision = exercise.vision;
      q('vision').textContent = `${exercise.vision.toUpperCase()} VISION`;
      for (const { m, color } of materials) {
        m.color.copy(color);
        if (exercise.vision === 'thermal') m.color.set(0x27345c).multiplyScalar(0.65 + color.g * 0.6);
      }
      model?.traverse(o => {
        if (!o.isMesh) return;
        for (const m of Array.isArray(o.material) ? o.material : [o.material]) {
          m.emissive?.set(exercise.vision === 'thermal' ? 0xf06a32 : 0x000000);
          m.emissiveIntensity = exercise.vision === 'thermal' ? 0.9 : 0;
        }
      });
    }
    q('night').setAttribute('aria-pressed', String(exercise.vision === 'night'));
    q('thermal').setAttribute('aria-pressed', String(exercise.vision === 'thermal'));
    const phaseText = { platform: 'ON THE LEDGE', falling: 'AIRBORNE', landing: 'TOUCHDOWN', stagger: 'LANDING STAGGER', free: exercise.result?.success ? 'CANCELLED · FREE TO MOVE' : 'RECOVERED' };
    q('phase').textContent = paused ? 'PAUSED' : phaseText[exercise.phase];
    const until = exercise.untilLanding;
    stage.dataset.cue = String(!paused && mode === 'guided' && ((until !== null && until <= RULES.before) || exercise.phase === 'landing'));
    q('stage-note').textContent = paused ? 'Paused · press Escape or Resume to continue' : exercise.phase === 'platform'
      ? `${exercise.height.toFixed(1)} m drop  /  SPACE TO DROP  /  W TO WALK OFF` : exercise.phase === 'falling'
      ? mode === 'challenge' ? 'Find the landing by watching the spy.' : 'Watch the distance between your feet and the floor.'
      : exercise.phase === 'stagger' || exercise.phase === 'landing' ? 'Movement locked · landing recovery' : 'Movement available · keep your route going';
    const offset = until !== null ? -until : exercise.landAt !== null ? exercise.time - exercise.landAt : -0.76;
    const percent = t => Math.max(0, Math.min(100, 45 + t / 1.7 * 100));
    q('playhead').style.left = `${percent(offset)}%`;
    if (exercise.lastToggle !== null) {
      q('input-marker').hidden = false;
      q('input-marker').style.left = `${percent(exercise.lastToggle - (exercise.landAt ?? exercise.time + (until ?? 0)))}%`;
    }
    if (exercise.result && !counted) {
      counted = true;
      if (mode !== 'demo') { attempts++; if (exercise.result.success) { successes++; streak++; } else streak = 0; stats(); }
      const { success, reason, offset } = exercise.result;
      const timing = offset === null ? '' : `${Math.round(Math.abs(offset) * 1000)} ms ${offset < 0 ? 'before' : 'after'} touchdown`;
      q('result-label').textContent = mode === 'demo' ? `DEMONSTRATION · ${demoRound} / 2` : 'LANDING FEEDBACK';
      verdict(success ? mode === 'challenge' && streak >= 3 ? 'Three in a row!' : 'Stagger cancelled' : reason === 'early' ? 'A little too early' : 'Landing stagger',
        success ? `${timing}. Your movement returned immediately. ${mode === 'demo' ? 'Choose Guided to try it yourself.' : 'Press R for another drop.'}`
          : reason === 'early' ? `${timing}. Wait until your feet reach the floor, then tap once. Press R to retry.` : 'No switch at touchdown. You have to wait for the landing animation. Press R to retry.', success ? 'success' : 'fail');
      q('timing-note').textContent = success ? `Vision input: ${timing}. The stagger was skipped.` : reason === 'early'
        ? `Vision input: ${timing}. Aim closer to the touchdown line.` : 'No vision input in the practice window. Try a single tap at touchdown.';
    }
    if (exercise.result?.reason === 'late') {
      verdict('A little too late', `${Math.round(exercise.result.offset * 1000)} ms after touchdown. Switch as your feet touch down. Press R to retry.`, 'fail');
      q('timing-note').textContent = 'Your vision switched after the practice window had closed.';
    }
    const action = exercise.phase === 'falling' ? 'fall' : ['landing', 'stagger'].includes(exercise.phase) ? 'land' : exercise.moving ? 'run' : 'idle';
    animate(action, exercise.result?.success);
  }

  try {
    const gltf = await new GLTFLoader().loadAsync(new URL('./training-spy.glb', import.meta.url).href);
    model = gltf.scene;
    model.traverse(o => { if (o.isMesh) { o.castShadow = true; o.frustumCulled = false; } });
    spy.add(model); mixer = new THREE.AnimationMixer(model);
    const clips = { idle: 'WaitStAlFd0', fall: 'fallstaldn0', land: 'landStAlHi0', run: 'JoggStAlFd0' };
    for (const [name, clipName] of Object.entries(clips)) {
      const clip = THREE.AnimationClip.findByName(gltf.animations, clipName);
      if (!clip) throw new Error(`Missing training animation ${clipName}`);
      actions[name] = mixer.clipAction(clip);
      if (name === 'land') { actions[name].setLoop(THREE.LoopOnce, 1); actions[name].clampWhenFinished = true; }
    }
    actions.idle.play(); mixer.update(0); model.updateMatrixWorld(true);
    const named = name => model.getObjectByName(name.replaceAll(' ', '_')) ?? model.getObjectByName(name);
    const r = named('B R Clavicle')?.getWorldPosition(new THREE.Vector3());
    const l = named('B L Clavicle')?.getWorldPosition(new THREE.Vector3());
    if (r && l && new THREE.Vector3(0, 1, 0).cross(r.sub(l).setY(0)).z < 0) model.rotation.y = Math.PI;
    model.updateMatrixWorld(true);
    const pelvis = named('B Pelvis')?.getWorldPosition(new THREE.Vector3());
    if (pelvis) { model.position.x -= pelvis.x; model.position.z -= pelvis.z; }
    model.position.y -= new THREE.Box3().setFromObject(model).min.y;
    ready = true;
    for (const name of ['drop', 'night', 'thermal', 'pause']) q(name).disabled = mode === 'demo' && ['night', 'thermal'].includes(name);
    reset();
  } catch (error) {
    console.error('Training could not load', error);
    q('phase').textContent = 'TRAINING UNAVAILABLE';
    q('stage-note').textContent = '';
    q('error').textContent = 'The spy model or animations could not load. Reload to try again. The written guide below is still available.';
  }

  let last = performance.now();
  renderer.setAnimationLoop(now => {
    const realDt = Math.min(0.05, Math.max(0, (now - last) / 1000)); last = now;
    if (ready && !paused) {
      const dt = realDt * (mode !== 'challenge' && q('slow').checked ? 0.6 : 1);
      if (mode === 'demo' && demoRound === 2 && exercise.phase === 'falling' && exercise.untilLanding <= dt + 0.025) exercise.toggle('night');
      const input = Object.fromEntries(['forward', 'back', 'left', 'right'].map(k => [k, mode !== 'demo' && (keys.has(k) || held.has(k))]));
      const previousPhase = exercise.phase;
      exercise.step(dt, input);
      if (previousPhase === 'platform' && exercise.phase === 'falling') {
        q('drop').innerHTML = 'Retry <kbd>R</kbd>';
        verdict('Watch your feet', 'Switch vision as you meet the floor. Hold W to feel when movement returns.');
      }
      if (mode === 'demo' && demoRound === 1 && exercise.landAt !== null && exercise.time - exercise.landAt > RULES.recovery + 1.1) {
        reset(); demoRound = 2; exercise.drop();
        verdict('2 / 2 · Vision cancel', 'This time, the spy switches vision on landing and continues immediately.');
      }
      syncView(); mixer.update(dt);
    } else if (ready) syncView();
    renderer.render(scene, camera);
  });
}
