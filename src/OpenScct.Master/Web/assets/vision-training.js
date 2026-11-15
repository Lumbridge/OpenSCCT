// A small, deterministic browser exercise, not the OpenSCCT simulation. Distances and gravity follow
// SPawnAttaque defaults and jump-fall-land.md; the forgiving input window is an authored teaching aid.
export const RULES = Object.freeze({ gravity: 15, before: 0.085, after: 0.115, recovery: 1.3, speed: 4 });

export class VisionTraining {
  constructor(height = 5.2) { this.height = height; this.reset(); }

  reset(height = this.height) {
    this.height = height;
    this.time = 0;
    this.x = 0; this.y = height; this.z = -0.85;
    this.vy = 0; this.phase = 'platform'; this.vision = 'normal';
    this.landAt = null; this.lastToggle = null; this.result = null;
    this.autoForward = false; this.moving = false; this.toggleCount = 0;
  }

  drop() {
    if (this.phase !== 'platform') return;
    this.z = 0.24; this.phase = 'falling'; this.autoForward = true;
  }

  toggle(mode) {
    if (!['night', 'thermal'].includes(mode)) return;
    this.vision = this.vision === mode ? 'normal' : mode;
    if (!['falling', 'landing', 'stagger'].includes(this.phase) || this.result?.success) return;
    this.lastToggle = this.time; this.toggleCount++;
    if (this.phase === 'landing' && this.time - this.landAt <= RULES.after + 1e-8)
      this.finish(true, this.time - this.landAt);
    else if (this.phase === 'stagger' && this.result?.reason === 'missed')
      this.result = { ...this.result, reason: 'late', offset: this.time - this.landAt };
  }

  cycle() { this.toggle(this.vision === 'night' ? 'thermal' : 'night'); }

  finish(success, offset = null) {
    this.result = { success, offset, reason: success ? 'cancelled' : offset === null ? 'missed' : 'early' };
    this.phase = success ? 'free' : 'stagger';
  }

  get untilLanding() {
    return this.phase === 'falling'
      ? (this.vy + Math.sqrt(this.vy ** 2 + 2 * RULES.gravity * this.y)) / RULES.gravity : null;
  }

  step(dt, input = {}) {
    if (!(dt > 0) || !Number.isFinite(dt)) return;
    let dx = Number(input.right || 0) - Number(input.left || 0);
    let dz = Number(input.forward || 0) - Number(input.back || 0);
    const length = Math.hypot(dx, dz);
    if (length > 1) { dx /= length; dz /= length; }
    this.moving = false;
    if (this.phase === 'platform') {
      this.x = Math.max(-1.55, Math.min(1.55, this.x + dx * RULES.speed * dt));
      this.z = Math.max(-4, this.z + dz * RULES.speed * dt);
      this.moving = length > 0;
      if (this.z > 0) this.phase = 'falling';
      this.time += dt;
      return;
    }

    if (this.phase === 'falling') {
      const contactIn = this.untilLanding, airTime = Math.min(dt, contactIn);
      this.x = Math.max(-6, Math.min(6, this.x + dx * RULES.speed * airTime));
      this.z = Math.max(0.24, Math.min(9, this.z + (length ? dz * RULES.speed : this.autoForward ? 2.4 : 0) * airTime));
      this.y = Math.max(0, this.y + this.vy * airTime - 0.5 * RULES.gravity * airTime ** 2);
      this.vy -= RULES.gravity * airTime;
      this.time += airTime;
      if (contactIn > dt + 1e-8) return;
      this.y = 0; this.vy = 0; this.landAt = this.time; this.phase = 'landing';
      if (this.lastToggle !== null && this.landAt - this.lastToggle <= RULES.before + 1e-8)
        this.finish(true, this.lastToggle - this.landAt);
      dt -= airTime;
    }

    this.time += dt;
    if (this.phase === 'landing' && this.time - this.landAt > RULES.after + 1e-8)
      this.finish(false, this.lastToggle === null ? null : this.lastToggle - this.landAt);
    if (this.phase === 'stagger' && this.time - this.landAt >= RULES.recovery) this.phase = 'free';
    if (this.phase === 'free') {
      const auto = this.autoForward && this.time - this.landAt < (this.result?.success ? 0.85 : RULES.recovery + 0.85);
      this.x = Math.max(-6, Math.min(6, this.x + dx * RULES.speed * dt));
      this.z = Math.max(0.24, Math.min(9, this.z + (length ? dz * RULES.speed : auto ? 2.4 : 0) * dt));
      this.moving = length > 0 || auto;
    }
  }
}
