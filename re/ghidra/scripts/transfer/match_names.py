"""Matches game functions to editor functions and carries the editor names over (plain Python 3.10).

The game is a link-time optimised release build: it inlines far more than the editor, passes
arguments in registers between its own functions, folds identical functions, and has no guard
blocks. Instruction shapes therefore differ and BSim scores are low even for true pairs. This
matcher combines evidence that survives those differences and only accepts a pair when
independent kinds of evidence agree:

* anchors: native exec thunks paired by native index (local/re-out/native-anchors.json)
* content: TF-IDF cosine over strings, imports (ANSI/wide folded), float constants, other
  immediates, read-only constants and [reg+disp] field offsets (class layouts are the same in
  both builds). The editor side is also scored with its direct callees folded in, which models
  the game inlining them.
* BSim: cosine of the exported BSim feature vectors, and the BSim nearest lists as candidates
* call graph: matched callees of the game function must be reachable from the editor candidate
  within three calls (inlining removes levels), matched callers likewise in the caller direction.
  A matched neighbour that is not reachable is a conflict and blocks the pair.
* data: global variables paired by votes from matched pairs; shared mapped globals agree,
  mapped globals the candidate never touches conflict.
* vtables: game and editor vtables paired by their matched slots; same-index slots of paired
  vtables become candidates.

A pair is accepted when it is the best candidate of both functions by a margin, has no conflict,
and meets one of the rules in RULES. Matching runs to a fixed point. With --holdout a share of the
native anchors is hidden and scored at the end, and the names already given by the physics and
net studies are checked against the transferred ones.

Run: python re/ghidra/scripts/transfer/match_names.py [--holdout 0.2] [--seed 1]
Output: local/ghidra-work/transfer/match/matches.json and metrics.json (local only).
"""

import argparse
import collections
import json
import os
import random
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import xfer  # noqa: E402

TOPK = 12
MIN_GAME_INSNS = 6
MIN_EDITOR_INSNS = 4
NEIGHBOUR_CAP = 80
MAX_ITERATIONS = 25


def log(msg):
    print('match: ' + msg, flush=True)


class Matcher:
    def __init__(self, game, editor, idf, nearest, anchors, refl_vt=()):
        self.g = game
        self.e = editor
        self.idf = idf
        self.nearest = nearest
        self.gs = {a: xfer.normalise(idf.raw(f)) for a, f in game.static.items()}
        self.es_raw = {a: idf.raw(f) for a, f in editor.static.items()}
        self.es = {a: xfer.normalise(v) for a, v in self.es_raw.items()}
        self._incl = {}
        self._csim = {}
        self._gtri = {}
        self._etri = {}
        self.m = {}            # game -> editor (accepted, one to one)
        self.minv = {}         # editor -> game
        self.anchor = collections.defaultdict(set)    # game -> editor set (natives, may fold)
        self.anchor_inv = collections.defaultdict(set)
        for ga, ea in anchors:
            if ga in game.funcs and ea in editor.funcs:
                self.anchor[ga].add(ea)
                self.anchor_inv[ea].add(ga)
        self.info = {}         # game -> evidence record of the accepted pair
        self.datamap = {}      # game data address -> editor data address
        self.vtpairs = {}      # game vtable address -> editor vtable address
        self.refl_vt = list(refl_vt)
        self.vt_slot = {}
        self.vt_all = {}
        self.static_top = {}
        self.unique_strings = self._unique_strings()

    # -- partners ---------------------------------------------------------------------------
    def partners(self, ga):
        s = self.anchor.get(ga)
        if s:
            return s
        ea = self.m.get(ga)
        return (ea,) if ea is not None else ()

    def gpartner(self, ea):
        s = self.anchor_inv.get(ea)
        if s:
            return s
        ga = self.minv.get(ea)
        return (ga,) if ga is not None else ()

    def matched_g(self, ga):
        return ga in self.m or ga in self.anchor

    def matched_e(self, ea):
        return ea in self.minv or ea in self.anchor_inv

    # -- features ---------------------------------------------------------------------------
    def _unique_strings(self):
        gc = collections.Counter(k for f in self.g.static.values() for k in f if k[0] == 'S')
        ec = collections.Counter(k for f in self.e.static.values() for k in f if k[0] == 'S')
        return frozenset(k for k, c in gc.items() if c == 1 and ec.get(k) == 1)

    def incl(self, ea):
        v = self._incl.get(ea)
        if v is None:
            acc = dict(self.es_raw.get(ea, {}))
            for c in self.e.callees.get(ea, ()):
                if self.e.is_hub(c):
                    continue
                for k, x in self.es_raw.get(c, {}).items():
                    acc[k] = acc.get(k, 0.0) + 0.6 * x
            v = xfer.normalise(acc)
            self._incl[ea] = v
        return v

    def compute_static_top(self):
        try:
            import numpy as np
            import scipy.sparse as sp
        except ImportError:
            log('numpy/scipy not available, static candidates come from the call graph only')
            return
        gkeys = sorted(self.gs)
        ekeys = sorted(a for a in self.es if not self.e.funcs[a]['thunk'])
        eidx = {a: i for i, a in enumerate(ekeys)}
        nf = len(self.idf.weight)

        def matrix(keys, vecs):
            rows, cols, vals = [], [], []
            for i, a in enumerate(keys):
                for k, x in vecs[a].items():
                    rows.append(i)
                    cols.append(k)
                    vals.append(x)
            return sp.csr_matrix((vals, (rows, cols)), shape=(len(keys), nf), dtype=np.float32)

        G = matrix(gkeys, self.gs)
        E = matrix(ekeys, self.es)
        # Inclusive editor matrix: own features plus 0.6 x the features of non-hub callees.
        rows, cols = [], []
        for i, a in enumerate(ekeys):
            for c in self.e.callees.get(a, ()):
                j = eidx.get(c)
                if j is not None and not self.e.is_hub(c):
                    rows.append(i)
                    cols.append(j)
        A = sp.csr_matrix((np.full(len(rows), 0.6, dtype=np.float32), (rows, cols)), shape=(len(ekeys), len(ekeys)))
        Eraw = matrix(ekeys, self.es_raw)
        Ei = Eraw + A @ Eraw
        n = np.sqrt(np.asarray(Ei.multiply(Ei).sum(1)).ravel())
        n[n == 0] = 1
        Ei = sp.diags(1 / n).astype(np.float32) @ Ei
        ET = E.T.tocsr()
        EiT = Ei.T.tocsr()
        block = 800
        for start in range(0, len(gkeys), block):
            Gb = G[start:start + block]
            for M in (ET, EiT):
                S = (Gb @ M).toarray()
                k = min(TOPK, S.shape[1] - 1)
                idx = np.argpartition(-S, k, axis=1)[:, :k]
                for r in range(S.shape[0]):
                    ga = gkeys[start + r]
                    lst = self.static_top.setdefault(ga, set())
                    for j in idx[r]:
                        if S[r, j] > 0.2:
                            lst.add(ekeys[j])
        log('static candidates for %d game functions' % len(self.static_top))

    # -- data and vtables -------------------------------------------------------------------
    def rebuild_datamap(self):
        """Pairs global data addresses by votes from the matched pairs. The editor side of a pair
        also counts the data its non-hub callees touch, since the game may have inlined them."""
        support = collections.defaultdict(collections.Counter)
        seen = collections.Counter()
        single = {}
        pairs = [(ga, ea) for ga, s in self.anchor.items() for ea in s] + list(self.m.items())
        for ga, ea in pairs:
            gd = set(self.g.drefs.get(ga, ()))
            if not gd:
                continue
            own = set(self.e.drefs.get(ea, ()))
            ed = set(own)
            for c in self.e.callees.get(ea, ()):
                if not self.e.is_hub(c):
                    ed.update(self.e.drefs.get(c, ()))
            if not ed or len(ed) > 120:
                continue
            if len(gd) == 1 and len(own) == 1:
                y = next(iter(gd))
                single.setdefault(y, set()).add(next(iter(own)))
            for y in gd:
                seen[y] += 1
                for x in ed:
                    support[y][x] += 1
        dm = {}
        for y, cnt in support.items():
            top = cnt.most_common(2)
            x, v = top[0]
            second = top[1][1] if len(top) > 1 else 0
            if v >= 2 and v >= 0.6 * seen[y] and second <= 0.5 * v:
                dm[y] = x
            elif v == 1 and len(single.get(y, ())) == 1 and x in single[y] and seen[y] == 1:
                dm[y] = x
        # Keep the map one to one: drop editor addresses claimed by several game addresses.
        claimed = collections.Counter(dm.values())
        self.datamap = {y: x for y, x in dm.items() if claimed[x] == 1}
        self.rebuild_vtables()

    def _align(self, gsl, esl):
        """Same-function slot pairs of a game and an editor vtable: the longest chain of matched
        slots increasing in both tables, with the gaps between chain links filled one to one when
        they have the same length on both sides (the editor adds editor-only virtuals)."""
        hits = []
        epos = collections.defaultdict(list)
        for j, s in enumerate(esl):
            epos[s].append(j)
        for i, s in enumerate(gsl):
            for p in self.partners(s):
                for j in epos.get(p, ()):
                    hits.append((i, j))
        if not hits:
            return []
        hits.sort()
        # Longest chain strictly increasing in i and j (patience sorting on j).
        import bisect
        tails, tails_idx, prev = [], [], [None] * len(hits)
        for k, (i, j) in enumerate(hits):
            pos = bisect.bisect_left(tails, j)
            # Avoid two hits with the same i in one chain.
            while pos > 0 and hits[tails_idx[pos - 1]][0] == i:
                pos -= 1
            if pos == len(tails):
                tails.append(j)
                tails_idx.append(k)
            elif j < tails[pos]:
                tails[pos] = j
                tails_idx[pos] = k
            else:
                continue
            prev[k] = tails_idx[pos - 1] if pos > 0 else None
        chain = []
        k = tails_idx[-1] if tails_idx else None
        while k is not None:
            chain.append(hits[k])
            k = prev[k]
        chain.reverse()
        out = []
        for (i0, j0), (i1, j1) in zip(chain, chain[1:]):
            if i1 - i0 == j1 - j0:
                for d in range(1, i1 - i0):
                    out.append((i0 + d, j0 + d))
        if chain:
            i_last, j_last = chain[-1]
            if len(gsl) - i_last == len(esl) - j_last:
                for d in range(1, len(gsl) - i_last):
                    out.append((i_last + d, j_last + d))
        return out

    def _content_sim(self, ga, ea):
        key = (ga, ea)
        v = self._csim.get(key)
        if v is None:
            gv = self.gs.get(ga, {})
            stat = max(xfer.dot(gv, self.es.get(ea, {})), xfer.dot(gv, self.incl(ea)) if gv else 0.0)
            bv, evv = self.g.vec.get(ga), self.e.vec.get(ea)
            bsim = xfer.dot(bv, evv) if bv and evv else 0.0
            v = 0.5 * stat + 0.5 * bsim
            self._csim[key] = v
        return v

    def _band_align(self, gsl, esl):
        """Aligns every game slot to an editor slot when the editor table is longer by d (the
        editor build adds editor-only virtuals): a dynamic programme over the number of editor
        slots skipped so far, scoring matched pairs +1, pairs matched elsewhere -1 and other pairs
        by content similarity. Returns [(i, j)] or None when the shapes do not allow it."""
        n, d = len(gsl), len(esl) - len(gsl)
        if n == 0 or d < 0 or d > 12:
            return None
        neg = -1e9

        def sim(i, j):
            g, e = gsl[i], esl[j]
            ps = self.partners(g)
            if ps:
                return 1.0 if e in ps else -1.0
            if self.matched_e(e):
                return -1.0
            return self._content_sim(g, e) - 0.25

        best = [[neg] * (d + 1) for _ in range(n)]
        back = [[0] * (d + 1) for _ in range(n)]
        for k in range(d + 1):
            best[0][k] = sim(0, k)
        for i in range(1, n):
            for k in range(d + 1):
                pk = max(range(k + 1), key=lambda q: best[i - 1][q])
                best[i][k] = best[i - 1][pk] + sim(i, i + k)
                back[i][k] = pk
        k = max(range(d + 1), key=lambda q: best[n - 1][q])
        out = []
        for i in range(n - 1, -1, -1):
            out.append((i, i + k))
            k = back[i][k]
        out.reverse()
        return out

    def rebuild_vtables(self):
        gvt = {v['a']: v['slots'] for v in self.g.vtables}
        evt = {v['a']: v['slots'] for v in self.e.vtables}
        pairs = {}
        for ga, ea, _ in self.refl_vt:
            if ga in gvt and ea in evt:
                pairs[ga] = ea
        for y, x in self.datamap.items():
            if y in gvt and x in evt and y not in pairs:
                pairs[y] = x
        claimed = collections.Counter(pairs.values())
        self.vtpairs = {g: e for g, e in pairs.items() if claimed[e] == 1}
        votes = collections.defaultdict(collections.Counter)   # game func -> editor funcs at aligned slots
        for ga, ea in self.vtpairs.items():
            gsl, esl = gvt[ga], evt[ea]
            al = self._band_align(gsl, esl)
            if al is None:
                al = self._align(gsl, esl)
            for i, j in al:
                votes[gsl[i]][esl[j]] += 1
        slot = {}
        for gf, cnt in votes.items():
            top = cnt.most_common(2)
            if len(top) == 1 or top[0][1] >= 3 * top[1][1]:
                slot[gf] = top[0][0]
        self.vt_slot = slot
        self.vt_all = votes

    # -- scoring ----------------------------------------------------------------------------
    def score(self, ga, ea):
        gv = self.gs.get(ga, {})
        s_self = xfer.dot(gv, self.es.get(ea, {}))
        s_incl = xfer.dot(gv, self.incl(ea)) if gv else 0.0
        stat = max(s_self, s_incl)
        bv = self.g.vec.get(ga)
        evv = self.e.vec.get(ea)
        bsim = xfer.dot(bv, evv) if bv and evv else 0.0
        agree = conf = 0
        out3 = out4 = None
        for c in self.g.callees.get(ga, ()):
            ps = self.partners(c)
            if not ps:
                continue
            if out3 is None:
                out3 = self.e.reach(ea, 'out', 3)
            if any(p in out3 for p in ps):
                agree += 1
            else:
                if out4 is None:
                    out4 = self.e.reach(ea, 'out', 4)
                if not any(p in out4 for p in ps):
                    conf += 1
        in3 = in4 = None
        for c in self.g.callers.get(ga, ()):
            ps = self.partners(c)
            if not ps:
                continue
            if in3 is None:
                in3 = self.e.reach(ea, 'in', 3)
            if any(p in in3 for p in ps):
                agree += 1
            else:
                if in4 is None:
                    in4 = self.e.reach(ea, 'in', 4)
                if not any(p in in4 for p in ps):
                    conf += 1
        dagree = dconf = 0
        gd = self.g.drefs.get(ga, ())
        if gd:
            ed = None
            for y in set(gd):
                x = self.datamap.get(y)
                if x is None:
                    continue
                if ed is None:
                    ed = set(self.e.drefs.get(ea, ()))
                    for c in self.e.callees.get(ea, ()):
                        if not self.e.is_hub(c):
                            ed.update(self.e.drefs.get(c, ()))
                if x in ed:
                    dagree += 1
                else:
                    dconf += 1
        vt = 1 if self.vt_slot.get(ga) == ea else 0
        ustr = 0
        gf = self.g.static.get(ga, {})
        ef = self.e.static.get(ea, {})
        for k in gf:
            if k in self.unique_strings and k in ef:
                ustr += 1
        conflicts = conf + dconf
        tok = self.tok_sim(ga, ea) if (stat >= 0.5 or bsim >= 0.5) else 0.0
        total = (0.55 * stat + 0.45 * bsim + 0.15 * tok + 0.06 * min(agree + dagree, 8) + 0.35 * vt
                 + 0.05 * min(ustr, 2) - 0.4 * conflicts)
        return {'total': total, 'stat': stat, 'self': s_self, 'incl': s_incl, 'bsim': bsim, 'tok': tok,
                'agree': agree, 'conf': conf, 'dagree': dagree, 'dconf': dconf, 'vt': vt, 'ustr': ustr}

    def _trigrams(self, prog, a, cache):
        t = cache.get(a)
        if t is None:
            lines = prog.funcs[a]['t'].splitlines()
            t = frozenset(zip(lines, lines[1:], lines[2:]))
            cache[a] = t
        return t

    def tok_sim(self, ga, ea):
        """Jaccard of address-free instruction trigrams: separates identical sibling bodies that
        differ only in an offset or constant (the X, Y and Z variants of one routine)."""
        a = self._trigrams(self.g, ga, self._gtri)
        b = self._trigrams(self.e, ea, self._etri)
        if not a or not b:
            return 0.0
        return len(a & b) / float(len(a | b))

    # -- candidates -------------------------------------------------------------------------
    def candidates(self, ga):
        out = set()
        for ea, _ in self.nearest.get(ga, ()):
            out.add(ea)
        out.update(self.static_top.get(ga, ()))
        out.update(self.vt_all.get(ga, ()))
        # Call-graph neighbourhoods of matched neighbours.
        for p in self.g.callers.get(ga, ()):
            if self.g.is_hub(ga):
                break
            for ep in self.partners(p):
                if self.e.is_hub(ep):
                    continue
                n = 0
                for c in self.e.reach(ep, 'out', 2):
                    out.add(c)
                    n += 1
                    if n >= NEIGHBOUR_CAP:
                        break
        for c in self.g.callees.get(ga, ()):
            if self.g.is_hub(c):
                continue
            for ec in self.partners(c):
                n = 0
                for p in self.e.reach(ec, 'in', 2):
                    out.add(p)
                    n += 1
                    if n >= NEIGHBOUR_CAP:
                        break
        return [ea for ea in out if not self.matched_e(ea) and ea in self.e.funcs
                and not self.e.funcs[ea]['thunk'] and self.e.funcs[ea]['n'] >= MIN_EDITOR_INSNS]

    # -- acceptance -------------------------------------------------------------------------
    @staticmethod
    def rule(s, margin, strict, ng, ne, tier2=False):
        """Name of the acceptance rule the pair meets, or None. Conflicts (matched neighbours or
        mapped data the candidate cannot reach) are tolerated only against three times as much
        agreeing evidence: the game devirtualises some calls, which looks like a conflict."""
        ev = s['agree'] + s['dagree'] + 2 * s['vt']
        conflicts = s['conf'] + s['dconf']
        ratio = ng / float(max(ne, 1))
        if conflicts and conflicts > (ev - 2) // 3:
            # Tier 2 tolerates one conflict against strong agreement or near-identical content.
            if not (tier2 and conflicts == 1 and (ev >= 4 or (s['stat'] >= 0.9 and s['bsim'] >= 0.7
                                                               and margin >= 0.3))):
                return None
            return 't2:conflict-tolerated' if margin >= 0.1 else None
        if s['stat'] >= 0.65 and s['bsim'] >= 0.6 and margin >= 0.1:
            return 'content'
        if s['stat'] >= 0.75 and ng >= 25 and ne >= 25 and 0.4 <= ratio <= 2.5 and margin >= 0.15:
            return 'content-large'
        if s['ustr'] and s['stat'] >= 0.5 and margin >= 0.1 and (s['bsim'] >= 0.4 or ev >= 1):
            return 'unique-string'
        if strict:
            return None
        if (s['stat'] >= 0.6 and s['bsim'] >= 0.45 and margin >= 0.12 and ng >= 15 and ne >= 15
                and 0.5 <= ratio <= 2.0):
            return 'content-moderate'
        if ev >= 1 and s['stat'] >= 0.9 and margin >= 0.15:
            return 'graph+content'
        if ev >= 2 and (s['stat'] >= 0.45 or s['bsim'] >= 0.55) and margin >= 0.05:
            return 'graph+content'
        if ev >= 4 and (s['stat'] >= 0.3 or s['bsim'] >= 0.4) and margin >= 0.05:
            return 'graph'
        if s['vt'] and margin >= 0.03 and conflicts == 0:
            # Aligned vtable slot plus one independent kind of agreement.
            if s['stat'] >= 0.5 or s['bsim'] >= 0.6:
                return 'vtable+content'
            if s['agree'] + s['dagree'] >= 2 or (s['agree'] + s['dagree'] >= 1 and margin >= 0.2):
                return 'vtable+graph'
            if ev >= 3 and (s['stat'] >= 0.3 or s['bsim'] >= 0.45):
                return 'vtable'
        if not tier2:
            return None
        if conflicts:
            # Within the tolerance checked above: only call-graph evidence that outweighs it.
            if s['agree'] + s['dagree'] >= 6 and margin >= 0.25 and 0.25 <= ratio <= 4.0:
                return 't2:graph-strong'
            return None
        # Tier 2: one strong kind of evidence plus a large margin.
        if s['vt'] and margin >= 0.2 and (s['stat'] >= 0.25 or s['bsim'] >= 0.4):
            return 't2:vtable+margin'
        if s['agree'] + s['dagree'] >= 6 and margin >= 0.2 and 0.25 <= ratio <= 4.0:
            return 't2:graph-strong'
        if s['stat'] >= 0.55 and s['bsim'] >= 0.55 and margin >= 0.2 and ng >= 8:
            return 't2:content-margin'
        if s['bsim'] >= 0.85 and s['stat'] >= 0.45 and margin >= 0.2 and ng >= 10:
            return 't2:bsim-strong'
        if s.get('tok', 0) >= 0.9 and s['stat'] >= 0.9 and margin >= 0.04 and ng >= 8:
            return 't2:identical-body'
        if s['bsim'] >= 0.6 and margin >= 0.3 and ng >= 10 and (s['agree'] + s['dagree'] >= 1 or s['stat'] >= 0.3):
            return 't2:bsim-margin'
        if s['agree'] + s['dagree'] >= 3 and margin >= 0.25 and (s['stat'] >= 0.3 or s['bsim'] >= 0.4):
            return 't2:graph+margin'
        return None

    def iterate(self, strict, tier2=False):
        todo = [ga for ga, f in self.g.funcs.items()
                if not self.matched_g(ga) and not f['thunk'] and f['n'] >= MIN_GAME_INSNS]
        best_g = {}
        best_e = {}
        for ga in todo:
            cands = self.candidates(ga)
            if not cands:
                continue
            scored = sorted(((self.score(ga, ea), ea) for ea in cands), key=lambda t: -t[0]['total'])
            s1, e1 = scored[0]
            s2 = scored[1][0]['total'] if len(scored) > 1 else -1.0
            best_g[ga] = (e1, s1, s1['total'] - max(s2, 0.0))
            for s, ea in scored:
                cur = best_e.get(ea)
                if cur is None:
                    best_e[ea] = [ga, s['total'], -1.0]
                elif s['total'] > cur[1]:
                    best_e[ea] = [ga, s['total'], cur[1]]
                elif s['total'] > cur[2]:
                    cur[2] = s['total']
        accepted = []
        for ga, (ea, s, margin_g) in best_g.items():
            be = best_e.get(ea)
            if be is None or be[0] != ga:
                continue
            margin_e = s['total'] - max(be[2], 0.0)
            margin = min(margin_g, margin_e)
            r = self.rule(s, margin, strict, self.g.funcs[ga]['n'], self.e.funcs[ea]['n'], tier2)
            if r:
                accepted.append((ga, ea, r, s, margin))
        for ga, ea, r, s, margin in accepted:
            if ga in self.m or ea in self.minv:
                continue
            self.m[ga] = ea
            self.minv[ea] = ga
            rec = {k: (round(v, 4) if isinstance(v, float) else v) for k, v in s.items()}
            rec['rule'] = r
            rec['margin'] = round(margin, 4)
            self.info[ga] = rec
        return len(accepted)

    def run(self, tier2=True):
        """Strict rounds first, then all tier 1 rules to a fixed point, then (optionally) tier 2
        rules to a fixed point, so tier 2 sees the most call-graph evidence."""
        self.compute_static_top()
        self.rebuild_datamap()
        it = 0
        strict_rounds = 2
        phase = 'strict'
        while it < MAX_ITERATIONS:
            t = time.time()
            if phase == 'strict' and it >= strict_rounds:
                phase = 'full'
            n = self.iterate(phase == 'strict', phase == 'tier2')
            for ga in self.m:
                if 'iteration' not in self.info[ga]:
                    self.info[ga]['iteration'] = it
            self.rebuild_datamap()
            log('iteration %d (%s): +%d, %d matched, %d data, %d vtables, %.0fs'
                % (it, phase, n, len(self.m), len(self.datamap), len(self.vtpairs), time.time() - t))
            it += 1
            if n == 0 and phase == 'full':
                if not tier2:
                    break
                phase = 'tier2'
            elif n == 0 and phase == 'tier2':
                break


def editor_names(editor):
    """Editor names: the exported user names plus the physics and net study names."""
    names = dict(editor.names)
    for study in ('physics', 'net'):
        sec = xfer.annotation_programs(study).get('editor')
        for fn in (sec or {}).get('functions', []):
            a = int(fn['address'], 16)
            names.setdefault(a, xfer.full_name(fn))
    return names


def study_game_names():
    out = {}
    for study in ('physics', 'net'):
        sec = xfer.annotation_programs(study).get('game')
        for fn in (sec or {}).get('functions', []):
            out[int(fn['address'], 16)] = (study, xfer.full_name(fn))
    return out


def same_name(a, b):
    na = a.replace('::', '.').lower()
    nb = b.replace('::', '.').lower()
    return na == nb or na.split('.')[-1] == nb.split('.')[-1] and na.split('.')[-2:-1] == nb.split('.')[-2:-1]


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.split('\n')[0])
    ap.add_argument('--holdout', type=float, default=0.0, help='share of native anchors hidden and scored')
    ap.add_argument('--seed', type=int, default=1)
    ap.add_argument('--install', help='reference install (default: binlock default)')
    ap.add_argument('--out', default=xfer.WORK)
    ap.add_argument('--tier1', action='store_true', help='tier 1 rules only (no tier 2 pass)')
    args = ap.parse_args(argv)
    t0 = time.time()
    game, editor = xfer.load_programs(args.install)
    idf = xfer.Idf([game, editor])
    nearest = xfer.load_nearest()
    pairs = xfer.load_native_pairs()
    log('loaded %d game and %d editor functions, %d native pairs, %.0fs'
        % (len(game.funcs), len(editor.funcs), len(pairs), time.time() - t0))
    rnd = random.Random(args.seed)
    held = set()
    if args.holdout > 0:
        games = sorted({p[0] for p in pairs})
        held = set(rnd.sample(games, int(len(games) * args.holdout)))
    anchors = [(ga, ea) for ga, ea, _, _ in pairs if ga not in held]
    ctors, refl_vt = xfer.load_reflection_pairs()
    anchors += [(ga, ea) for ga, ea, _ in ctors if ga in game.funcs and ea in editor.funcs and ga not in held]
    m = Matcher(game, editor, idf, nearest, anchors, refl_vt)
    m.run(tier2=not args.tier1)

    enames = editor_names(editor)
    truth = collections.defaultdict(set)
    for ga, ea, _, _ in pairs:
        truth[ga].add(ea)
    metrics = {'matched': len(m.m), 'anchors': len(m.anchor), 'datamap': len(m.datamap),
               'vtables': len(m.vtpairs), 'rules': collections.Counter(i['rule'] for i in m.info.values())}
    if held:
        ok = bad = miss = 0
        wrong = []
        for ga in held:
            if ga in m.m:
                if m.m[ga] in truth[ga]:
                    ok += 1
                else:
                    bad += 1
                    wrong.append({'game': '%x' % ga, 'got': '%x' % m.m[ga], 'want': ['%x' % x for x in truth[ga]],
                                  'info': m.info[ga]})
            else:
                miss += 1
        # Also count held-out editor natives claimed by some other game function.
        held_e = {ea for ga in held for ea in truth[ga]}
        stolen = sum(1 for ga, ea in m.m.items() if ea in held_e and ga not in held)
        metrics['holdout'] = {'held': len(held), 'correct': ok, 'wrong': bad, 'unmatched': miss,
                              'editorNativeTakenByOther': stolen,
                              'precision': round(ok / max(1, ok + bad + stolen), 4), 'wrongPairs': wrong[:60]}
    study = study_game_names()
    sres = {'checked': 0, 'agree': 0, 'disagree': [], 'unnamedEditor': 0, 'unmatched': 0}
    for ga, (who, nm) in study.items():
        if ga not in m.m:
            sres['unmatched'] += 1
            continue
        en = enames.get(m.m[ga])
        if not en:
            sres['unnamedEditor'] += 1
            continue
        sres['checked'] += 1
        if same_name(en, nm):
            sres['agree'] += 1
        else:
            sres['disagree'].append({'game': '%x' % ga, 'study': who, 'studyName': nm, 'editorName': en,
                                     'info': m.info[ga]})
    metrics['studyNames'] = sres
    named = sum(1 for ga, ea in m.m.items() if ea in enames and ga not in game.names and ga not in study)
    metrics['newNames'] = named
    os.makedirs(args.out, exist_ok=True)
    out = []
    for ga in sorted(m.m):
        ea = m.m[ga]
        rec = {'game': '%x' % ga, 'editor': '%x' % ea, 'editorName': enames.get(ea), 'gameName': game.names.get(ga)}
        rec.update(m.info[ga])
        out.append(rec)
    suffix = ('' if not held else '.holdout%d' % int(args.holdout * 100)) + ('.tier1' if args.tier1 else '')
    with open(os.path.join(args.out, 'matches%s.json' % suffix), 'w', encoding='utf-8', newline='\n') as fh:
        json.dump({'matches': out, 'datamap': {'%x' % y: '%x' % x for y, x in sorted(m.datamap.items())},
                   'vtables': {'%x' % y: '%x' % x for y, x in sorted(m.vtpairs.items())}}, fh, indent=0)
    with open(os.path.join(args.out, 'metrics%s.json' % suffix), 'w', encoding='utf-8', newline='\n') as fh:
        json.dump(metrics, fh, indent=1, default=str)
    log('matched %d, new names %d, rules %s' % (len(m.m), named, dict(metrics['rules'])))
    if held:
        h = metrics['holdout']
        log('holdout: %d held, %d correct, %d wrong, %d unmatched, %d taken by others, precision %.3f'
            % (h['held'], h['correct'], h['wrong'], h['unmatched'], h['editorNativeTakenByOther'], h['precision']))
    log('study names: %d checked, %d agree, %d disagree, %d unnamed editor, %d unmatched'
        % (sres['checked'], sres['agree'], len(sres['disagree']), sres['unnamedEditor'], sres['unmatched']))
    log('done in %.0fs' % (time.time() - t0))
    return 0


if __name__ == '__main__':
    sys.exit(main())
