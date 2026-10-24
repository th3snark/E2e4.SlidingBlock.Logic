# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

`E2e4.SlidingBlock.Logic` is the sliding block puzzle engine (NuGet package of the same name, MIT): bitboard state structs and generic solvers
for the N-puzzle, Klotski, Rush Hour and polyominoes. `PROMPT.md` is the original brief it was built from.

## Layout

- `src/E2e4.SlidingBlock.Logic/` — the library (`net10.0`): `ISlidingBlockDomain` (`Abstractions.cs`), the per-puzzle state and domain types
  (`NPuzzle.cs`, `Klotski.cs`, `RushHour.cs`, `Polyomino.cs`, `PolyominoGoal.cs`), `PuzzleTopology`, `GridGeometry.cs`, `CompositeMove.cs` (rotation, push, carry; see `docs/extended-mechanics.md`), `Aliases.cs` (the `ulong` names of the generic polyomino types), `PuzzleSolver`, `PuzzleWalk`, `PlaySession` and `BitboardExtensions`.
- `src/E2e4.SlidingBlock.Logic.Labs.Wpf/` — WPF application with the three laboratories (`Lab1`, `Lab2`, `Lab3`). `LabPuzzles.cs` holds the layouts, goals and colours they play (plain C#, linked into the test project along with `Lab3/RaycastReport.cs`). Private, not packed; it sets
  `EnableWindowsTargeting` so the Linux CI runners can build it.
- `tests/E2e4.SlidingBlock.Logic.Test/` — xUnit tests (net10.0).
- `docs/` — markdown research notes and design explanations with Mermaid diagrams (start at `docs/README.md`); keep them in step with\r\n  the code when the object model changes.\r\n- `assets/icon.png` / `icon.svg` — the package icon: the E2e4 frame, a Klotski board and the Logic badge shared with E2e4.Chess.Logic.
  `assets/Build-Icon.ps1` rasterises the SVG (Windows PowerShell, `-STA`); edit the SVG, not the PNG.

## Rules

- The library knows how pieces move and how to search, never which puzzle is being played: no layouts or ASCII maps, no goal positions,
  no goal-relative heuristics, no colours. Goals are constructor data of the domains; each puzzle (the Puzzles app, the Labs) owns its definition.
- No dependencies, no WPF/WinForms in the library, no I/O. CI fails if the library gains a package dependency.
- State types are structs on purpose and move generation writes into caller-supplied `Span<TMove>` buffers: keep the hot paths allocation-free.
- Nullable is on and warnings are errors: the tree must build with 0 warnings. File-scoped namespaces and `int`/`string` keywords are enforced
  by `.editorconfig`.
- Number formatting uses `CultureInfo.InvariantCulture` (CA1305).
- Source files use CRLF line endings (the editor tools tend to write LF; convert before committing, `git ls-files --eol` shows the state).
- The version is `VersionPrefix` in `Directory.Build.props`; a release tag overrides it at pack time.

## Build and test

```
dotnet build E2e4.SlidingBlock.Logic.slnx
dotnet test E2e4.SlidingBlock.Logic.slnx
```

CI (`.github/workflows/ci.yml`) runs only when started by hand (`gh workflow run ci.yml --ref <branch>`; Actions minutes are limited), on
`ubuntu-24.04` and `windows-latest` (plus `ubuntu-26.04` with the `ubuntu-26` input). Windows also checks whitespace
formatting (`dotnet format whitespace --verify-no-changes`). Run the tests locally before pushing; the tag-triggered publish workflow tests too.

## Releasing

Pushing a `vX.Y.Z` tag runs `.github/workflows/publish.yml`: tests, pack and push to nuget.org via Trusted Publishing (no API key; the policy
names this repo, `publish.yml` and the package glob `E2e4.SlidingBlock.Logic*`; the repo secret `NUGET_USER` is the nuget.org user name).
**Only tag when the owner says so**: nuget.org cannot delete a version, only unlist it. Set up the nuget.org policy and the `nuget`
environment first. A manual run (`gh workflow run publish.yml --ref master`) only checks the login.
