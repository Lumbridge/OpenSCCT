// Run with: node --test tests/web/vision-training.test.mjs (Node 22+).
import test from 'node:test';
import assert from 'node:assert/strict';
import { VisionTraining, RULES } from '../../src/OpenScct.Master/Web/assets/vision-training.js';

const fallTime = Math.sqrt(2 * 5.2 / RULES.gravity);
const drop = () => { const e = new VisionTraining(); e.drop(); return e; };

test('ballistic contact time is independent of render frame rate', () => {
  for (const fps of [30, 60, 144]) {
    const e = drop();
    while (e.landAt === null) e.step(1 / fps);
    assert.ok(Math.abs(e.landAt - fallTime) < 1e-8);
    assert.equal(e.y, 0);
  }
});

test('a deliberate vision switch just before contact skips the stagger', () => {
  const e = drop(); e.step(fallTime - 0.07); e.toggle('night'); e.step(0.08);
  assert.equal(e.result.success, true); assert.equal(e.phase, 'free');
  assert.ok(Math.abs(e.result.offset + 0.07) < 1e-8);
});

test('switching from an active mode to normal also cancels', () => {
  const e = drop(); e.toggle('night'); e.step(fallTime + 0.06); e.toggle('night');
  assert.equal(e.vision, 'normal'); assert.equal(e.result.success, true);
});

test('night to thermal is a real discrete switch', () => {
  const e = drop(); e.toggle('night'); e.step(fallTime + 0.06); e.cycle();
  assert.equal(e.vision, 'thermal'); assert.equal(e.result.success, true);
});

test('both edges of the authored practice window are inclusive', () => {
  const early = drop(); early.step(fallTime - RULES.before); early.toggle('night'); early.step(RULES.before);
  assert.equal(early.result.success, true);
  const late = drop(); late.step(fallTime + RULES.after); late.toggle('thermal');
  assert.equal(late.result.success, true);
});

test('an early switch cannot stay buffered throughout the fall', () => {
  const e = drop(); e.step(0.1); e.toggle('night'); e.step(fallTime + 0.12);
  assert.equal(e.result.success, false); assert.equal(e.result.reason, 'early'); assert.equal(e.phase, 'stagger');
});

test('a later well timed correction can succeed after an early input', () => {
  const e = drop(); e.toggle('night'); e.step(fallTime + 0.04); e.toggle('thermal');
  assert.equal(e.result.success, true);
});

test('a late switch changes vision but cannot cancel the stagger', () => {
  const e = drop(); e.step(fallTime + 0.2); e.toggle('thermal');
  assert.equal(e.vision, 'thermal'); assert.equal(e.result.success, false);
  assert.equal(e.result.reason, 'late'); assert.equal(e.phase, 'stagger');
});

test('holding movement cannot walk out of a missed high landing', () => {
  const e = drop(); e.step(fallTime + 0.15);
  const z = e.z; e.step(0.5, { forward: true });
  assert.equal(e.z, z); assert.equal(e.phase, 'stagger');
  e.step(RULES.recovery, { forward: true });
  assert.equal(e.phase, 'free'); assert.ok(e.z > z);
});

test('a cancel restores movement immediately', () => {
  const e = drop(); e.step(fallTime + 0.03); e.toggle('night');
  const z = e.z; e.step(0.02, { forward: true });
  assert.ok(e.z > z); assert.equal(e.phase, 'free');
});

test('walk off the edge and Space drop use the same landing rules', () => {
  const e = new VisionTraining();
  for (let i = 0; i < 20 && e.phase === 'platform'; i++) e.step(0.02, { forward: true });
  assert.equal(e.phase, 'falling'); e.step(2);
  assert.equal(e.result.success, false);
});

test('retry clears vision, buffered input, outcome, and momentum', () => {
  const e = drop(); e.step(fallTime + 0.02); e.toggle('thermal'); e.reset(4.6);
  assert.equal(e.phase, 'platform'); assert.equal(e.vision, 'normal'); assert.equal(e.result, null);
  assert.equal(e.lastToggle, null); assert.equal(e.vy, 0); assert.equal(e.y, 4.6); assert.equal(e.time, 0);
});

test('invalid time steps and invalid modes cannot corrupt the exercise', () => {
  const e = drop();
  for (const dt of [-1, 0, NaN, Infinity]) e.step(dt);
  e.toggle('invalid'); assert.equal(e.time, 0); assert.equal(e.vision, 'normal');
});
