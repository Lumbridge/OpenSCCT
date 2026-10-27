"""Runs another server script with inputs set first (inside ghserve.py through ghq.py --exec).

The request code is this file with WITH (a dict of globals) and SCRIPT (a path relative to the repo)
prepended by ghx.py; it lets the actions study reuse the typing scripts of re/ghidra/scripts/physics
on its own project copy without editing them.
"""

import os

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(OUT), '..', '..', '..'))  # noqa: F821
g = dict(globals())
g.update(WITH)  # noqa: F821
with open(os.path.join(REPO_ROOT, SCRIPT), encoding='utf-8') as fh:  # noqa: F821
    exec(compile(fh.read(), SCRIPT, 'exec'), g)  # noqa: F821
