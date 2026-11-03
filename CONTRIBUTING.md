# Contributing to OpenSCCT

You can help by reporting bugs, testing a release, improving a guide or sending a code change.

## Report a problem

Search [existing issues](https://github.com/Lumbridge/OpenSCCT/issues) first, then
[open a report](https://github.com/Lumbridge/OpenSCCT/issues/new/choose) with:

- The game version, operating system, map and game mode.
- Steps another person can follow, expected behavior and what happened.
- Whether it happens offline, online, as host or as a joining player.
- Relevant logs, screenshots or a short clip, if available.

Remove tokens, passwords and personal information from logs. Do not attach original game
files, converted asset packs or decompiled output. For launcher problems, start with the
[launcher guide](docs/LAUNCHER.md).

## Make a change

1. Follow the [development guide](docs/DEVELOPMENT.md).
2. Create a focused branch, such as `fix/lobby-layout`, `feature/demo-filter` or
   `enhancement/setup-docs`.
3. Read the relevant [guide](docs/README.md) and [behavior specification](spec/README.md).
4. Make the smallest complete change, and update affected docs.
5. Run the [checks that fit the change](docs/TESTING.md). Record anything you could not test.
6. Open a pull request explaining the problem, the resulting behavior and your validation.

Keep unrelated fixes in separate pull requests. A screenshot or short clip helps reviewers
assess visible changes. For gameplay changes, include the map, side and steps used to check them.

## Code and evidence

Follow the [conventions](docs/CONVENTIONS.md). Ports retain original class and member names,
and link to a specification with evidence. Keep decoded facts, runtime measurements and
estimates distinct. Do not claim parity from a build or unit test alone.

Read the [provenance rules](re/PROVENANCE.md) before adding files. Original data, conversions,
traces and decompiled material belong in ignored local folders. Use synthetic fixtures for
tests that run in CI. Do not edit generated defaults or coverage reports by hand.

## Documentation changes

- Start with what the reader needs to do or understand.
- Use short sentences, useful headings and copyable commands. Say which folder to run them in.
- Link to the owning guide instead of repeating its instructions.
- Keep dates and environment details with historical results. Do not present an old test run
  as current status.
- Preserve specification IDs, evidence, public anchors and attribution.

Run `python tools/ci/docs.py` to check repository links and the README coverage table.
After refreshing the coverage report, run `python tools/ci/docs.py --write` to update that table.
