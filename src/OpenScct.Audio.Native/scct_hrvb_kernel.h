#pragma once
// Adapted from reAL src/alrv.c, pinned b68e4d12c526c0155af702e92b95dcf78c0e790b.
// Copyright (c) 2026 Neth. MIT license; see adjacent LICENSE-reAL.txt.
// The retained single-network Creative transition adapter lives here so the
// immutable upstream coefficient/property implementation remains reproducible.

static inline float scct_ring_read(const alrv_ring *r, uint32_t d)
{
    uint32_t i = r->pos >= d ? r->pos - d : r->pos + r->len - d;
    return r->buf[i];
}

static inline void scct_ring_write(alrv_ring *r, float v)
{
    r->buf[r->pos] = v;
    if (++r->pos == r->len) r->pos = 0;
}


// Original HRvb uses one retained network, 512-frame gain/tap crossfades,
// and 17 successive delay stages. New targets arriving during a transition
// replace the pending target, rather than restarting the active transition.
struct ScctHrvbTransition {
    static constexpr int stage_frames = 512;
    static constexpr int stage_count = 17;
    alrv origin{}, goal{};
    alrv_params pending{};
    int position{stage_frames * stage_count};
    bool queued{};

    bool active() const { return position < stage_frames * stage_count; }
    static float blend(float from, float to, int frame, int duration) {
        if (frame >= duration) return to;
        const float slope = static_cast<float>((static_cast<double>(to) - from) / duration);
        return static_cast<float>(static_cast<double>(from) + static_cast<double>(slope) * frame);
    }
    void start(alrv &engine, const alrv_params &target) {
        origin = engine;
        goal = engine;
        alrv_set_params(&goal, &target);
        for (int i = 0; i < 4; ++i)
            if (origin.p.line_shelf_g[i] == goal.p.line_shelf_g[i]
                && origin.p.line_shelf_f[i] == goal.p.line_shelf_f[i]
                && origin.p.line_shelf_high[i] == goal.p.line_shelf_high[i])
                goal.shelf_a[i] = origin.shelf_a[i];
        position = 0;
    }
    void set(alrv &engine, const alrv_params &target) {
        if (active()) { pending = target; queued = true; }
        else start(engine, target);
    }
    void prepare(alrv &engine) const {
        if (!active()) return;
        auto &p = engine.p;
        p.refl_gain = blend(origin.p.refl_gain, goal.p.refl_gain, position, 512);
        for (int i = 0; i < 4; ++i) {
            p.late_tap_gain[i] = blend(origin.p.late_tap_gain[i], goal.p.late_tap_gain[i], position, 512);
            p.eap_gain[i] = blend(origin.p.eap_gain[i], goal.p.eap_gain[i], position, 512);
            p.lap_gain[i] = blend(origin.p.lap_gain[i], goal.p.lap_gain[i], position, 512);
            p.fdn_gain[i] = blend(origin.p.fdn_gain[i], goal.p.fdn_gain[i], position, 512);
        }
        for (int i = 0; i < 9; ++i) {
            p.early_out[i] = blend(origin.p.early_out[i], goal.p.early_out[i], position, 512);
            p.late_out[i] = blend(origin.p.late_out[i], goal.p.late_out[i], position, 512);
        }
        p.eap_coef = goal.p.eap_coef;
        p.lap_coef = blend(origin.p.lap_coef, goal.p.lap_coef, position, 512);
        p.fdn_rot = blend(origin.p.fdn_rot, goal.p.fdn_rot, position, 128);
        p.lap_rot = blend(origin.p.lap_rot, goal.p.lap_rot, position, 128);
        p.mod_freq = blend(origin.p.mod_freq, goal.p.mod_freq, position, 128);
        engine.mod_amp = blend(origin.mod_amp, goal.mod_amp, position, 128);
        engine.hp_a = blend(origin.hp_a, goal.hp_a, position, 128);
        // The scalar original LP kernels hold each coefficient for four
        // samples. The HP kernel advances it for every individual sample.
        engine.lp_a = blend(origin.lp_a, goal.lp_a, position & ~3, 128);
        // At a zero endpoint the original kernels fade their OUTPUT while
        // retaining the nonzero coefficient, then clear history at the end.
        // HP's zero-to-nonzero path likewise fades in the new filter output.
        if (position < 128) {
            if (goal.hp_a == 0) engine.hp_a = origin.hp_a;
            else if (origin.hp_a == 0) engine.hp_a = goal.hp_a;
            if (goal.lp_a == 0) engine.lp_a = origin.lp_a;
        }
        else if (position == 128) {
            if (goal.hp_a == 0) engine.hp_s = 0;
            if (goal.lp_a == 0) engine.lp_v = engine.lp_w = 0;
        }
        for (int i = 0; i < 4; ++i) {
            engine.damp_a[i] = blend(origin.damp_a[i], goal.damp_a[i], position & ~3, 128);
            if (goal.damp_a[i] == 0) {
                if (position < 128) engine.damp_a[i] = origin.damp_a[i];
                else if (position == 128) engine.damp_y[i] = 0;
            }
            engine.shelf_a[i] = goal.shelf_a[i];
            engine.shelf_c0[i] = blend(origin.shelf_c0[i], goal.shelf_c0[i], position, 128);
            engine.shelf_c1[i] = blend(origin.shelf_c1[i], goal.shelf_c1[i], position, 128);
        }
    }
    float output_weight(float old_coefficient, float new_coefficient, bool high_pass = false) const {
        if (!active() || position >= 128) return 1;
        if (new_coefficient == 0 && old_coefficient != 0) return 1 - position * (1.0f / 128);
        if (high_pass && old_coefficient == 0 && new_coefficient != 0) return (position + 1) * (1.0f / 128);
        return 1;
    }
    float tap(const alrv_ring &ring, int old_delay, int new_delay, int stage) const {
        auto clamp = [&](int delay) {
            uint32_t d = static_cast<uint32_t>(delay);
            if (d >= ring.len) d = ring.len - 8;
            return std::max(d, uint32_t{8});
        };
        const int frame = position - stage * stage_frames;
        if (frame < 0) return scct_ring_read(&ring, clamp(old_delay));
        if (frame >= stage_frames) return scct_ring_read(&ring, clamp(new_delay));
        const float from = scct_ring_read(&ring, clamp(old_delay));
        const float to = scct_ring_read(&ring, clamp(new_delay));
        const double weight = static_cast<double>(frame) / stage_frames;
        return static_cast<float>(static_cast<double>(from) * (1 - weight) + static_cast<double>(to) * weight);
    }
    void advance(alrv &engine) {
        if (!active()) return;
        if (++position == stage_frames * stage_count) {
            engine.p = goal.p;
            engine.hp_a = goal.hp_a; engine.lp_a = goal.lp_a;
            engine.mod_amp = goal.mod_amp;
            std::copy_n(goal.etap, 4, engine.etap);
            std::copy_n(goal.damp_a, 4, engine.damp_a);
            std::copy_n(goal.shelf_a, 4, engine.shelf_a);
            std::copy_n(goal.shelf_c0, 4, engine.shelf_c0);
            std::copy_n(goal.shelf_c1, 4, engine.shelf_c1);
            if (queued) { queued = false; start(engine, pending); }
        }
    }
};

static void scct_hrvb_process(alrv *r, const float *in, float *const out[ALRV_NOUT], uint32_t frames, ScctHrvbTransition *transition = nullptr)
{
    const alrv_params *P = &r->p;
    const float *G = P->early_out, *H = P->late_out;
    float *Q = r->modbuf;
    const uint32_t mlen = r->mod_len;

    for (uint32_t n = 0; n < frames; n++) {
        if (transition) transition->prepare(*r);
        const bool changing = transition && transition->active();
        const float hp_a = r->hp_a, lp_a = r->lp_a;
        const float ke = P->eap_coef, kl = P->lap_coef, rl = P->lap_rot, rf = P->fdn_rot;
        float x = in[n];

        /* ---- input filters: room LF high-pass, room HF double low-pass */
        if (hp_a == 0.0f) x = 0.0f;
        else if (hp_a != 1.0f) {
            float s = r->hp_s * ((hp_a + hp_a) - 1.0f) + x;
            x = (s - r->hp_s) * hp_a;
            r->hp_s = s;
        }
        else r->hp_s = x;
        if (changing) x *= transition->output_weight(transition->origin.hp_a, transition->goal.hp_a, true);
        if (lp_a == 0.0f) x = 0.0f;
        else if (lp_a != 1.0f) {
            r->lp_v = r->lp_v * (1.0f - lp_a) + x * lp_a;
            r->lp_w = r->lp_v * lp_a + r->lp_w * (1.0f - lp_a);
            x = r->lp_w;
        }
        else { r->lp_v = r->lp_w; r->lp_w = x; }
        if (changing) x *= transition->output_weight(transition->origin.lp_a, transition->goal.lp_a);
        if (r->fade_pos < ALRV_FADE) x *= (float)r->fade_pos++ * (1.0f / ALRV_FADE);

        /* ---- input delay: early taps and late-reverb feed taps */
        float e[4], t[4];
        for (int j = 0; j < 4; j++) {
            e[j] = (changing ? transition->tap(r->in, transition->origin.etap[j], transition->goal.etap[j], 0) : scct_ring_read(&r->in, r->etap[j])) * P->refl_gain;
            t[j] = (changing ? transition->tap(r->in, transition->origin.p.late_tap_delay[j], transition->goal.p.late_tap_delay[j], 1 + j) : scct_ring_read(&r->in, (uint32_t)P->late_tap_delay[j])) * P->late_tap_gain[j];
        }
        scct_ring_write(&r->in, x);

        /* ---- early reflections: 4 allpasses */
        float ye[4];
        for (int i = 0; i < 4; i++) {
            float d = (changing ? transition->tap(r->eap[i], transition->origin.p.eap_delay[i], transition->goal.p.eap_delay[i], 5 + i) : scct_ring_read(&r->eap[i], (uint32_t)P->eap_delay[i])) * P->eap_gain[i];
            ye[i] = d + e[i] * -ke;
            scct_ring_write(&r->eap[i], e[i] + ye[i] * ke);
        }
        float o[ALRV_NOUT];
        o[ALRV_FC] = ye[0] * G[1];
        o[ALRV_CX] = ye[0] * G[5];
        o[ALRV_SL] = ye[1] * G[7];
        o[ALRV_SR] = ye[2] * G[3];
        o[ALRV_FR] = ye[3] * G[2];
        o[ALRV_BR] = scct_ring_read(&r->eout[0], r->eout_delay[0]) * G[4];
        o[ALRV_FL] = scct_ring_read(&r->eout[1], r->eout_delay[1]) * G[8];
        o[ALRV_BL] = scct_ring_read(&r->eout[2], r->eout_delay[2]) * G[6];
        scct_ring_write(&r->eout[0], ye[1]);
        scct_ring_write(&r->eout[1], ye[2]);
        scct_ring_write(&r->eout[2], ye[3]);

        /* ---- late reverb: FDN lines -> LF shelf -> + feed -> HF damping -> nested allpasses */
        float L[4], A[4];
        for (int i = 0; i < 4; i++) {
            float v = (changing ? transition->tap(r->fdn[i], transition->origin.p.fdn_delay[i], transition->goal.p.fdn_delay[i], 9 + i) : scct_ring_read(&r->fdn[i], (uint32_t)P->fdn_delay[i])) * P->fdn_gain[i];
            float a = r->shelf_a[i];
            float tt = v + r->shelf_t[i] * a;
            float ap = r->shelf_t[i] - tt * a;
            r->shelf_t[i] = tt;
            v = ap * r->shelf_c1[i] + v * r->shelf_c0[i];
            v += t[i];
            float da = r->damp_a[i];
            if (da == 0.0f) v = 0.0f;
            else if (da != 1.0f) { r->damp_y[i] = r->damp_y[i] * (1.0f - da) + v * da; v = r->damp_y[i]; }
            else r->damp_y[i] = v;
            if (changing) v *= transition->output_weight(transition->origin.damp_a[i], transition->goal.damp_a[i]);
            L[i] = v;
        }
        for (int i = 0; i < 4; i++) {
            // Retail's two loops both read the +0x338 schedule (9..12),
            // leaving its separately initialized 13..16 schedule unused.
            A[i] = (changing ? transition->tap(r->lap[i], transition->origin.p.lap_delay[i], transition->goal.p.lap_delay[i], 9 + i) : scct_ring_read(&r->lap[i], (uint32_t)P->lap_delay[i])) * P->lap_gain[i];
            A[i] = A[i] + L[i] * -kl;
            L[i] = L[i] + A[i] * kl;
        }
        /* rotation of the allpass states (fn 0x10064160) */
        { float a0 = L[0], a1 = L[1]; L[0] = rl * a1 + a0; L[1] = a1 - rl * a0; }
        { float a0 = L[2], a1 = L[3]; L[2] = rl * a1 + a0; L[3] = a1 - rl * a0; }
        { float a0 = L[0], a1 = L[2]; L[0] = rl * a1 + a0; L[2] = a1 - rl * a0; }
        { float a0 = L[1], a1 = L[3]; L[1] = rl * a1 + a0; L[3] = a1 - rl * a0; }
        for (int i = 0; i < 4; i++) scct_ring_write(&r->lap[i], L[i]);
        /* output rotation (fn 0x10064220) */
        { float a0 = A[0], a1 = A[1]; A[0] = rf * a1 + a0; A[1] = a1 - rf * a0; }
        { float a0 = A[2], a1 = A[3]; A[2] = rf * a1 + a0; A[3] = a1 - rf * a0; }
        { float a0 = A[0], a1 = A[3]; A[0] = rf * a1 + a0; A[3] = a1 - rf * a0; }
        { float a0 = A[1], a1 = A[2]; A[1] = a0 - a1 * rf; A[2] = rf * a0 + a1; }

        /* modulated delay on line 0 (inline loop in fn 0x10062580) */
        float fv = (float)r->mod_tap[1] + r->osc_y * r->mod_amp;
        long ic = (long)fv;                    /* FPU set to truncate here */
        float frac = fv - (float)ic;
        if (r->mod_amp != 0.0f) {
            float xo = r->osc_x - P->mod_freq * r->osc_y;
            r->osc_x = xo;
            r->osc_y = xo * P->mod_freq + r->osc_y;
        }
        uint32_t idx = (uint32_t)((mlen - (uint32_t)ic) + r->mod_wp) % mlen;
        float M = idx == 0 ? (1.0f - frac) * Q[0] + frac * Q[mlen - 1]
                           : (1.0f - frac) * Q[idx] + Q[idx - 1] * frac;
        float T1 = Q[((mlen - r->mod_tap[1]) + r->mod_wp) % mlen];   /* 6 ms */
        float T0 = Q[((mlen - r->mod_tap[0]) + r->mod_wp) % mlen];   /* 11.3 ms */
        if (r->mod_wp == mlen) { Q[0] = A[0]; r->mod_wp = 1; }
        else Q[r->mod_wp++] = A[0];
        A[0] = M;

        /* feedback, permuted (table at object +0x274) */
        scct_ring_write(&r->fdn[0], A[2]);
        scct_ring_write(&r->fdn[1], A[0]);
        scct_ring_write(&r->fdn[2], A[3]);
        scct_ring_write(&r->fdn[3], A[1]);

        o[ALRV_FC] += A[3] * H[1];
        o[ALRV_CX] += A[3] * H[5];
        o[ALRV_BR] += T0 * H[4];
        o[ALRV_SL] += T1 * H[7];
        o[ALRV_FL] += scct_ring_read(&r->lout[0], r->lout_delay[0]) * H[8];
        /* ALchemy multiplies these two taps by H[2] (SR) and H[3] (FR), i.e. it routes the 30 degree
         * pan slot to the side and the 90 degree slot to the front, mirrored against the early
         * reflections and the left side.  reAL fixes that; with an unpanned reverb H[2] == H[3] and
         * the output is unchanged (tests/test_alrv swaps them back to compare with ALchemy). */
        o[ALRV_SR] += scct_ring_read(&r->lout[0], r->lout_delay[1]) * H[3];
        o[ALRV_FR] += scct_ring_read(&r->lout[1], r->lout_delay[2]) * H[2];
        o[ALRV_BL] += scct_ring_read(&r->lout[1], r->lout_delay[3]) * H[6];
        scct_ring_write(&r->lout[0], A[1]);
        scct_ring_write(&r->lout[1], A[2]);

        for (int c = 0; c < ALRV_NOUT; c++) out[c][n] = o[c] * 40.0f;
        if (transition) transition->advance(*r);
    }
}

/* parameter computation lives in alrv_params.c */
