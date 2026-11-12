// The wiki's 3D view of the spy's grab window (/wiki/spy-grab): the mercenary in his standing idle with his rifle,
// the floor around him coloured by what Use does there, and a spy that follows the pointer.
import * as THREE from './three.module.min.js';
import { OrbitControls } from './three-OrbitControls.js';
import { GLTFLoader } from './three-GLTFLoader.js';

// SPlayerController.CanGrabPawn: reach 160 uu (1 uu is 1 cm on these models), the merc's facing shifted by
// iGrabAreaShift (1200 as Reloaded sets it, 6.59 degrees to his right; the script's own 7200 is retail), and the
// spy's facing within 60 degrees of that (cos 0.5). BODY is how far his body stands turned from his aim in the
// standing idle (shoulders 38, hips 39 degrees), for the second arrow only.
const REACH = 1.6, SHIFT = 1200 / 65536 * 360, HALF = 60, BODY = 38;
const FROM = 180 + SHIFT - HALF, TO = 180 + SHIFT + HALF;   // clockwise from the merc's front, seen from above
const rad = d => d * Math.PI / 180;
const ASSETS = new URL('.', import.meta.url);

const host = document.getElementById('grab-window');
if (host) start(host);

function start(host) {
  const q = name => host.querySelector('[data-grab="' + name + '"]');
  const tagLayer = q('tags'), verdictEl = q('verdict'), detailEl = q('detail'), errorEl = q('error'), follow = q('follow'), panel = q('panel');
  const fail = text => { errorEl.textContent = text; };
  let renderer;
  try { renderer = new THREE.WebGLRenderer({ antialias: true }); }
  catch { fail('This browser could not start WebGL, so the 3D view is not shown. The text below describes the same window.'); return; }
  host.classList.add('grab-live');
  renderer.setPixelRatio(Math.min(devicePixelRatio, 2));
  host.insertBefore(renderer.domElement, host.firstChild);

  const scene = new THREE.Scene();
  scene.background = new THREE.Color(0x101612);
  const camera = new THREE.PerspectiveCamera(40, 1, 0.05, 100);
  const controls = new OrbitControls(camera, renderer.domElement);
  controls.target.set(0, 0.7, 0);
  controls.maxPolarAngle = Math.PI / 2 - 0.02;
  controls.enablePan = false;
  controls.enableZoom = false;   // the wheel scrolls the article
  controls.minDistance = 2; controls.maxDistance = 9;
  scene.add(new THREE.HemisphereLight(0xffffff, 0x30343c, 1.6));
  const sun = new THREE.DirectionalLight(0xffffff, 1.6); sun.position.set(2, 4, 3); scene.add(sun);

  const floor = new THREE.Mesh(new THREE.CircleGeometry(4, 96), new THREE.MeshBasicMaterial({ color: 0x161f19 }));
  floor.rotation.x = -Math.PI / 2; scene.add(floor);
  const grid = new THREE.GridHelper(8, 16, 0x2a3a30, 0x1f2b23); grid.position.y = 0.002; scene.add(grid);

  function resize() {
    const w = host.clientWidth, h = host.clientHeight;
    if (!w || !h) return;
    renderer.setSize(w, h, false);
    camera.aspect = w / h; camera.updateProjectionMatrix();
  }
  new ResizeObserver(resize).observe(host); resize();

  const overlay = new THREE.Group(); scene.add(overlay);
  const tags = [];
  function tag(text, pos) {
    const el = document.createElement('div'); el.className = 'grab-tag'; el.textContent = text;
    tagLayer.appendChild(el); tags.push({ el, pos });
  }
  let front = new THREE.Vector3(0, 0, 1), right = new THREE.Vector3(-1, 0, 0);
  const dir = deg => front.clone().multiplyScalar(Math.cos(rad(deg))).addScaledVector(right, Math.sin(rad(deg)));

  function sector(fromDeg, toDeg, radius, color, opacity, y) {
    const pts = [0, y, 0], idx = [], steps = Math.max(2, Math.round((toDeg - fromDeg) / 2));
    for (let i = 0; i <= steps; i++) { const d = dir(fromDeg + (toDeg - fromDeg) * i / steps).multiplyScalar(radius); pts.push(d.x, y, d.z); }
    for (let i = 1; i <= steps; i++) idx.push(0, i, i + 1);
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(pts, 3)); g.setIndex(idx);
    return new THREE.Mesh(g, new THREE.MeshBasicMaterial({ color, transparent: true, opacity, side: THREE.DoubleSide, depthWrite: false }));
  }
  function line(a, b, color, dashed) {
    const g = new THREE.BufferGeometry().setFromPoints([a, b]);
    const m = dashed ? new THREE.LineDashedMaterial({ color, dashSize: 0.06, gapSize: 0.05 }) : new THREE.LineBasicMaterial({ color });
    const l = new THREE.Line(g, m); if (dashed) l.computeLineDistances(); return l;
  }
  function buildOverlay() {
    overlay.add(sector(0, 360, REACH, 0x8a8880, 0.22, 0.004));
    overlay.add(sector(FROM, TO, REACH, 0x2fbf8f, 0.5, 0.008));
    const y = 0.012, at = (deg, r) => dir(deg).multiplyScalar(r).setY(y);
    overlay.add(line(at(FROM, 0), at(FROM, REACH), 0x2fbf8f), line(at(TO, 0), at(TO, REACH), 0x2fbf8f));
    overlay.add(line(at(180, 0.25), at(180, REACH + 0.25), 0xb4b2a9, true));
    overlay.add(new THREE.ArrowHelper(front, new THREE.Vector3(0, y, 0).addScaledVector(front, 0.35), 1.0, 0xd8e8dc, 0.18, 0.1));
    overlay.add(new THREE.ArrowHelper(dir(BODY), new THREE.Vector3(0, y, 0).addScaledVector(dir(BODY), 0.35), 0.75, 0x8a8880, 0.14, 0.08));
    // Directions are named from where he faces and aims (the pawn's rotation, the white arrow), which is what the
    // grab test uses. His body stands turned from that; the grey arrow shows it.
    const side = d => Math.abs(d - 180).toFixed(0) + '° to his ' + (d > 180 ? 'left' : 'right');
    tag('aim', at(0, 1.55)); tag('body', at(BODY, 1.3)); tag('straight behind', at(180, REACH + 0.45));
    tag("merc's left", at(270, REACH + 0.45)); tag("merc's right", at(90, REACH + 0.45));
    tag('window right: ' + side(FROM), at(FROM, REACH + 0.3)); tag('window left: ' + side(TO), at(TO, REACH + 0.3));
  }

  // The spy stands in a group that is turned to face the merc; a ring under him shows the outcome.
  const spy = new THREE.Group(); spy.visible = false; scene.add(spy);
  const ringMat = new THREE.MeshBasicMaterial({ color: 0x8a8880, transparent: true, opacity: 0.9, side: THREE.DoubleSide, depthWrite: false });
  const ring = new THREE.Mesh(new THREE.RingGeometry(0.3, 0.36, 48), ringMat); ring.rotation.x = -Math.PI / 2; ring.position.y = 0.016; spy.add(ring);

  const mixers = [];
  let paused = false;
  const loader = new GLTFLoader();
  const load = file => loader.loadAsync(new URL(file, ASSETS).href);
  const named = (root, n) => root.getObjectByName(n.replace(/ /g, '_')) ?? root.getObjectByName(n);
  function pose(gltf) {
    const root = gltf.scene;
    root.traverse(o => { if (o.isSkinnedMesh) o.frustumCulled = false; });
    if (gltf.animations.length) { const m = new THREE.AnimationMixer(root); m.clipAction(gltf.animations[0]).play(); m.update(0); mixers.push(m); }
    root.updateMatrixWorld(true);
    return root;
  }
  // Which way along the model's forward axis is the front, read off the posed shoulders.
  function frontSign(root) {
    const r = named(root, 'B R Clavicle')?.getWorldPosition(new THREE.Vector3()), l = named(root, 'B L Clavicle')?.getWorldPosition(new THREE.Vector3());
    return r && l ? Math.sign(new THREE.Vector3(0, 1, 0).cross(r.sub(l).setY(0)).z) || 1 : 1;
  }
  // Stands a posed model on the floor with its pelvis over the origin (an idle can sit a little off the root).
  function centre(root) {
    root.updateMatrixWorld(true);
    const pelvis = named(root, 'B Pelvis')?.getWorldPosition(new THREE.Vector3());
    if (pelvis) { root.position.x -= pelvis.x; root.position.z -= pelvis.z; }
    root.position.y -= new THREE.Box3().setFromObject(root).min.y;
    root.updateMatrixWorld(true);
  }

  load('grab-spy.glb').then(gltf => {
    const model = pose(gltf);
    if (frontSign(model) < 0) model.rotation.y = Math.PI;   // lookAt turns the group's +Z to the merc
    centre(model);
    spy.add(model);
  }).catch(() => fail('The spy model could not be loaded.'));

  Promise.all([load('grab-merc.glb'), load('grab-rifle.glb')]).then(([gltf, rifle]) => {
    const merc = pose(gltf); scene.add(merc);
    // The drawn rifle rides the right hand bone with no offset (SWeaponDefenseDummy: AttachToBone 'B R Hand', 0, 0).
    const hand = named(merc, 'B R Hand');
    if (hand) { rifle.scene.traverse(o => { if (o.isSkinnedMesh) o.frustumCulled = false; }); hand.add(rifle.scene); }
    centre(merc);
    // The pawn's facing is the model's own forward axis (the idle stance blades the shoulders about 40 degrees off
    // it, rifle forward); the shoulders only say which way along that axis is the front.
    front = new THREE.Vector3(0, 0, frontSign(merc));
    right = front.clone().cross(new THREE.Vector3(0, 1, 0)).normalize();
    buildOverlay(); view(true);
  }).catch(() => fail('The mercenary model could not be loaded.'));

  function view(top) {
    // Both views are turned to his aim: straight behind him is toward the bottom of the screen or the viewer.
    if (top) camera.position.copy(new THREE.Vector3(0, 6.2, 0).addScaledVector(front, -0.01));
    else camera.position.copy(dir(180 + SHIFT).multiplyScalar(4.2).setY(2.9));
    controls.target.set(0, top ? 0 : 0.7, 0); controls.update();
  }
  q('top').addEventListener('click', () => view(true));
  q('persp').addEventListener('click', () => view(false));
  q('pause').addEventListener('click', e => { paused = !paused; e.currentTarget.textContent = paused ? 'Play idle' : 'Pause idle'; });
  q('fold').addEventListener('click', e => {
    const folded = panel.classList.toggle('grab-folded');
    e.currentTarget.textContent = folded ? '+' : '−';
    e.currentTarget.setAttribute('aria-label', folded ? 'Expand the panel' : 'Collapse the panel');
    e.currentTarget.setAttribute('aria-expanded', String(!folded));
  });

  function idle() {
    spy.visible = false;
    verdictEl.textContent = follow.checked ? 'Move over the floor' : 'Spy hidden';
    verdictEl.dataset.outcome = 'none'; detailEl.textContent = ' ';
  }
  follow.addEventListener('change', idle);
  idle();
  const ray = new THREE.Raycaster(), ground = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0), hit = new THREE.Vector3();
  renderer.domElement.addEventListener('pointermove', e => {
    if (!follow.checked) return;
    const box = renderer.domElement.getBoundingClientRect();
    ray.setFromCamera(new THREE.Vector2((e.clientX - box.left) / box.width * 2 - 1, -((e.clientY - box.top) / box.height) * 2 + 1), camera);
    if (!ray.ray.intersectPlane(ground, hit)) return;
    const dist = Math.hypot(hit.x, hit.z);
    if (dist < 0.3 || dist > 3.8) { idle(); return; }
    spy.visible = true; spy.position.set(hit.x, 0, hit.z); spy.lookAt(0, 0, 0);
    const flat = new THREE.Vector3(hit.x, 0, hit.z).normalize();
    let deg = Math.atan2(flat.dot(right), flat.dot(front)) * 180 / Math.PI; if (deg < 0) deg += 360;
    const inWindow = deg >= FROM && deg <= TO, inReach = dist < REACH;
    ringMat.color.set(!inReach ? 0x5f5e5a : inWindow ? 0x2fbf8f : 0xd85a30);
    verdictEl.textContent = !inReach ? 'Out of reach' : inWindow ? 'Grab' : 'Elbow';
    verdictEl.dataset.outcome = !inReach ? 'none' : inWindow ? 'grab' : 'elbow';
    const off = deg - 180;
    detailEl.textContent = (dist * 100).toFixed(0) + ' units away, ' + (Math.abs(off) < 0.5 ? 'straight behind' : Math.abs(off).toFixed(0) + '° to his ' + (off > 0 ? 'left' : 'right') + ' of straight behind');
  });
  renderer.domElement.addEventListener('pointerleave', () => { if (follow.checked) idle(); });

  const clock = new THREE.Clock(), v = new THREE.Vector3();
  renderer.setAnimationLoop(() => {
    const dt = clock.getDelta(); if (!paused) for (const m of mixers) m.update(dt);
    controls.update(); renderer.render(scene, camera);
    const w = host.clientWidth, h = host.clientHeight;
    for (const t of tags) {
      v.copy(t.pos).project(camera);
      const x = (v.x * 0.5 + 0.5) * w, y = (-v.y * 0.5 + 0.5) * h;
      t.el.hidden = v.z >= 1 || x < 0 || x > w || y < 0 || y > h;
      t.el.style.left = x + 'px'; t.el.style.top = y + 'px';
    }
  });
}
