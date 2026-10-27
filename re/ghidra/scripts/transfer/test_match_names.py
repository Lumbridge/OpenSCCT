"""Unit tests for the name-transfer helpers, on synthetic data only (no game files needed).

Run: python -m pytest re/ghidra/scripts/transfer/test_match_names.py
"""

import collections
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import match_names as mn  # noqa: E402
import xfer  # noqa: E402


def test_floatish_accepts_real_constants_only():
    assert xfer.floatish(0x3f800000) == '1'
    assert xfer.floatish(0x42c80000) == '100'
    assert xfer.floatish(0) is None
    assert xfer.floatish(0x00000010) is None      # a small integer, not a float
    assert xfer.floatish(0xffffffff) is None


def test_imports_fold_ansi_wide_and_winsock():
    n = xfer._normalise_import
    assert n('user32.dll!SendMessageA') == n('user32.dll!SendMessageW') == 'user32.dll!SendMessage'
    assert n('msvcr70.dll!_wtoi') == 'msvcr70.dll!atoi'
    assert n('ws2_32.dll!#111') == n('wsock32.dll!#111') == 'winsock!#111'
    assert n('kernel32.dll!GetLastError') == 'kernel32.dll!GetLastError'


def test_split_name():
    assert xfer.split_name('AActor::Tick') == ('AActor', 'Tick')
    assert xfer.split_name('A::B::C') == ('A::B', 'C')
    assert xfer.split_name('GetRotationCoords') == ('', 'GetRotationCoords')


def _program(funcs):
    feats = {'base': 0x1000, 'blocks': [['.text', 0x1000, 0x2000, True], ['.rdata', 0x2000, 0x3000, False]],
             'functions': funcs, 'vtables': []}
    return xfer.Program('t', feats, {}, {})


def test_features_offsets_immediates_and_strings():
    f = {'a': 0x1100, 'src': 'default', 'size': 10, 'thunk': False, 'n': 3, 'calls': [],
         'str': ['Hello'], 'c': ['i3f800000', 'iffffffe0'], 't': 'MOV EAX;ESI,1a4\nPUSH 5\nADD ESP;c'}
    p = _program([f])
    feats = p.static[0x1100]
    assert feats['S:Hello'] == 1
    assert feats['O:1a4'] == 1                 # [esi+0x1a4] is a field offset
    assert feats['K:5'] == 1                   # pushed immediate
    assert 'K:c' not in feats                  # stack adjustment is ignored
    assert feats['F:1'] == 1                   # 1.0f immediate
    assert not any(k.startswith('K:ffff') for k in feats)   # frame offsets are ignored


def test_reach_respects_depth():
    funcs = [{'a': a, 'src': 'default', 'size': 1, 'thunk': False, 'n': 1, 'calls': c, 't': 'RET'}
             for a, c in ((0x1100, [0x1200]), (0x1200, [0x1300]), (0x1300, []))]
    p = _program(funcs)
    assert p.reach(0x1100, 'out', 1) == {0x1200}
    assert p.reach(0x1100, 'out', 2) == {0x1200, 0x1300}
    assert p.reach(0x1300, 'in', 2) == {0x1200, 0x1100}


class _AlignStub(mn.Matcher):
    """Just enough of a Matcher to run the vtable alignment."""

    def __init__(self, partners, sims):
        self._p = partners
        self._s = sims

    def partners(self, ga):
        return self._p.get(ga, ())

    def matched_e(self, ea):
        return any(ea in v for v in self._p.values())

    def _content_sim(self, ga, ea):
        return self._s.get((ga, ea), 0.0)


def test_band_align_finds_editor_only_slots():
    # The editor table has two extra slots (E1 and E2) after slot 1.
    game = ['g0', 'g1', 'g2', 'g3', 'g4']
    editor = ['e0', 'e1', 'E1', 'E2', 'e2', 'e3', 'e4']
    m = _AlignStub({'g0': ('e0',), 'g4': ('e4',)}, {('g1', 'e1'): 0.9, ('g2', 'e2'): 0.8, ('g3', 'e3'): 0.9})
    al = dict(m._band_align(game, editor))
    assert [editor[al[i]] for i in range(5)] == ['e0', 'e1', 'e2', 'e3', 'e4']


def test_band_align_refuses_shorter_editor_table():
    m = _AlignStub({}, {})
    assert m._band_align(['a', 'b'], ['x']) is None


def test_rules_need_two_kinds_of_evidence():
    base = collections.defaultdict(float, {'agree': 0, 'dagree': 0, 'conf': 0, 'dconf': 0, 'vt': 0, 'ustr': 0})
    s = dict(base, stat=0.9, bsim=0.9)
    assert mn.Matcher.rule(s, 0.3, True, 50, 50) == 'content'
    s = dict(base, stat=0.9, bsim=0.2)
    assert mn.Matcher.rule(s, 0.3, True, 50, 50) == 'content-large'
    assert mn.Matcher.rule(s, 0.3, True, 10, 10) is None          # too small for content alone
    s = dict(base, stat=0.5, bsim=0.3, agree=3)
    assert mn.Matcher.rule(s, 0.1, True, 50, 50) is None          # graph rules only after strict rounds
    assert mn.Matcher.rule(s, 0.1, False, 50, 50) == 'graph+content'


def test_conflicts_block_unless_outweighed():
    base = {'agree': 0, 'dagree': 0, 'conf': 1, 'dconf': 0, 'vt': 0, 'ustr': 0, 'stat': 0.9, 'bsim': 0.9}
    assert mn.Matcher.rule(dict(base), 0.3, False, 50, 50) is None
    assert mn.Matcher.rule(dict(base, agree=5), 0.3, False, 50, 50) == 'content'
