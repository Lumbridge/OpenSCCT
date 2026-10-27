"""The sandbox side of a recording: the headless editor, Play Level under the play lock, and the
clean-up of exactly the processes this run started.

It drives the RE+ private test tools (headless.py, gamewait.py, game_tools.py, mcp_server.py,
live.py in the folder named by --re-tools or SVM_RE_TOOLS) the same way re/tools/reflect/dump.py
does, and follows the standing rules for the real game: editor Play Level only, on a hidden
desktop of its own, muted (game_tools mutes on attach), frame cap set for the run and restored
after, the play lock held only while a game runs, and only processes this run started are ended.
"""
from __future__ import annotations

import contextlib
import hashlib
import os
import subprocess
import sys
import tempfile
import time

DEFAULT_RE_TOOLS = os.path.expanduser(r'~\source\repos\SCCT_Versus_Reloaded_Editor_mb-main\tools\automation')
DEFAULT_SANDBOX = r'F:\SCCT-Automation\svm\Sandbox'
DEFAULT_DESKTOP = 'SVMHeadless'

# Files whose hashes go into every trace header (relative to the sandbox root).
BINARIES = (
    'System/SCCT Versus',
    'System/Reloaded.Core.dll',
    'System/ChaosTheory_Editor.exe',
    'System/Reloaded.Editor.dll',
    'System/Reloaded_Editor.exe',
    'System/SCCT_Versus.exe',
    'System/d3d9.dll',
)


def log(*parts):
    print('[difftest %s] %s' % (time.strftime('%H:%M:%S'), ' '.join(str(p) for p in parts)), file=sys.stderr,
          flush=True)


def sha256(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for block in iter(lambda: f.read(1 << 20), b''):
            h.update(block)
    return h.hexdigest()


def load_tools(re_tools):
    """Imports the RE+ tools once and returns them as a namespace."""
    if re_tools not in sys.path:
        sys.path.insert(0, re_tools)
    import game_tools  # noqa: F401
    import gamewait  # noqa: F401
    import headless  # noqa: F401
    import live  # noqa: F401
    import mcp_server  # noqa: F401

    class Tools:
        pass
    t = Tools()
    t.game_tools, t.gamewait, t.headless, t.live, t.mcp_server = game_tools, gamewait, headless, live, mcp_server
    t.path = re_tools
    return t


def drop_stale_name_caches(pids, since):
    """The RE+ driver caches each game's name table by process id (scctdrive, in the temp folder). A
    cache written before this game started belongs to an earlier process that had the same id, and
    would give every object the wrong name: remove it, so the driver scans the names afresh."""
    dropped = []
    for pid in pids:
        path = os.path.join(tempfile.gettempdir(), 'scct-automation', 'out', '_drive_names_%d.pkl' % pid)
        try:
            if os.path.getmtime(path) < since:
                os.remove(path)
                dropped.append(path)
                log('removed a stale name cache of an earlier process with pid', pid)
        except OSError:
            pass
    return dropped


def binaries(root):
    out = {}
    for rel in BINARIES:
        path = os.path.join(root, *rel.split('/'))
        out[rel] = sha256(path) if os.path.isfile(path) else None
    return out


class Sandbox:
    """One headless editor session in the sandbox. Use as a context manager: on exit every process
    this session saw start from the sandbox is ended (the sandbox must be idle when it begins, so
    nothing else is touched), and the frame cap in SCCT_Versus.config is restored."""

    def __init__(self, tools, root=DEFAULT_SANDBOX, desktop=DEFAULT_DESKTOP, fps=60, wait_idle=1800):
        self.t = tools
        self.root = os.path.abspath(root)
        self.desktop = desktop
        self.fps = fps
        self.wait_idle = wait_idle
        self.started_pids = set()
        self.editor_pid = None
        self.live = None
        self.report = {'sandbox': self.root, 'desktop': desktop}
        self._config = None

    # -- life cycle -------------------------------------------------------------------------------

    def _wait_until_idle(self):
        """Another worker may be using the sandbox (an editor for collision traces, say): wait."""
        deadline = time.time() + self.wait_idle
        told = False
        while True:
            busy = self.t.headless.sandbox_pids(self.root)
            if not busy:
                return
            if time.time() > deadline:
                raise SystemExit('the sandbox %s stayed busy for %ds: %s' % (self.root, self.wait_idle, busy))
            if not told:
                log('the sandbox is in use (pids %s); waiting for it to be idle' % busy)
                told = True
            time.sleep(5)

    def __enter__(self):
        self._wait_until_idle()
        config = os.path.join(self.root, 'System', 'SCCT_Versus.config')
        with open(config, 'rb') as f:
            self._config = (config, f.read())
        if self.fps:
            self.report['frameCap'] = self.t.headless.frame_cap(self.root, self.fps)
        launched = self.t.headless.launch_editor(self.root, self.desktop)
        self.editor_pid = launched['pid']
        self.started_pids.add(self.editor_pid)
        self.report['editor'] = launched
        log('editor', launched)
        # One connection only: the editor's channel is a single pipe instance.
        self.live = self.t.live.Live(pid=self.editor_pid)
        return self

    def __exit__(self, *exc):
        try:
            if self.live:
                self.live.close()
        finally:
            self.report['killed'] = self.end_own_processes()
            if self._config:
                path, data = self._config
                with open(path, 'wb') as f:
                    f.write(data)
        return False

    def note_processes(self):
        """Every process now running from the sandbox was started by this session (it was idle at
        the start and launch_editor refuses a second editor)."""
        for pid in self.t.headless.sandbox_pids(self.root):
            self.started_pids.add(pid)

    def end_own_processes(self):
        self.note_processes()
        mine = sorted(self.started_pids)
        for pid in mine:
            subprocess.run(['taskkill', '/PID', str(pid), '/T', '/F'], capture_output=True)
        deadline = time.time() + 15
        while time.time() < deadline and set(self.t.headless.sandbox_pids(self.root)) & set(mine):
            time.sleep(0.3)
        return {'ended': mine, 'left': sorted(set(self.t.headless.sandbox_pids(self.root)) & set(mine))}

    # -- editor -----------------------------------------------------------------------------------

    def map_path(self, name):
        return name if os.path.isabs(name) else os.path.join(self.root, 'Packages', 'MapsEd', name + '.sdc')

    def load_map(self, name):
        path = self.map_path(name)
        t = time.time()
        reply = self.live.raw('map.load', {'path': path})
        if not reply.get('ok'):
            raise SystemExit('map.load %s failed: %s' % (path, reply.get('error')))
        info = {'path': path, 'sha256': sha256(path), 'seconds': round(time.time() - t, 1)}
        self.report.setdefault('maps', []).append(info)
        log('map loaded', info)
        return info

    # -- game -------------------------------------------------------------------------------------

    @contextlib.contextmanager
    def play(self, side, lock_timeout=3600, pawn_timeout=240):
        """Play Level as `side` under the play lock; yields a game_tools.Session whose local player
        has a pawn. The game is quit (forced: a headless window cannot be closed politely) before
        the lock is let go."""
        gt, gw, hl = self.t.game_tools, self.t.gamewait, self.t.headless
        with gw.play_lock(self.root, timeout=lock_timeout, log=log):
            info = {'side': side, 'lockTaken': time.strftime('%H:%M:%S')}
            t = time.time()
            gt.start_game(self.root, lambda: self.live.raw('play', {'side': side}), timeout=lock_timeout,
                          say=lambda m: log(m))
            self.note_processes()
            info['gameUpSeconds'] = round(time.time() - t, 1)
            info['staleNameCaches'] = drop_stale_name_caches(gt.sandbox_games(self.root), t)
            session = gt.Session(self.root)
            try:
                deadline = time.time() + pawn_timeout
                state = None
                while time.time() < deadline:
                    game_side = set(hl.sandbox_pids(self.root)) - {self.editor_pid}
                    boxes = hl.dialogs(self.desktop, game_side)
                    if boxes:
                        info['dialogs'] = boxes
                        raise SystemExit('a dialog opened on the hidden desktop: %s' % boxes)
                    try:
                        state = session.wait(timeout=20)
                        break
                    except gt.GameError as error:
                        log('waiting for the pawn:', error)
                if state is None:
                    raise SystemExit('no pawn within %ds' % pawn_timeout)
                info['pawnSeconds'] = round(time.time() - t, 1)
                info['pawn'] = state.get('pawn')
                info['gamePid'] = session.find()
                self.started_pids.add(info['gamePid'])
                autoplay = os.path.join(self.root, 'Packages', 'Maps', 'Autoplay.sdc')
                info['autoplaySha256'] = sha256(autoplay) if os.path.isfile(autoplay) else None
                log('pawn ready', state.get('pawn'))
                self.report.setdefault('games', []).append(info)
                yield session, info
            finally:
                info['gameQuit'] = session.quit(force=True)
                info['lockReleased'] = time.strftime('%H:%M:%S')
