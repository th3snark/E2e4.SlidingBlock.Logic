# Contributing

Thanks for looking. Bug reports, fixes and new puzzle mechanics are welcome. For a vulnerability, follow [SECURITY.md](SECURITY.md) instead of opening an issue.

## What the library is for

`E2e4.SlidingBlock.Logic` knows how pieces move and how to search, never which puzzle is being played. A change belongs here if it adds a mechanic or a
search feature; a layout, a goal position, a colour or a book's numbering belongs to the application that uses the library (`LabPuzzles` in the Labs
shows how). Reading a diagram is the one exception, and it lives in the `E2e4.SlidingBlock.Logic.Text` namespace, with the symbols as parameters.

[docs/README.md](docs/README.md) explains the design; [docs/roadmap.md](docs/roadmap.md) lists what is still missing.

## Build and test

```
dotnet build E2e4.SlidingBlock.Logic.slnx
dotnet test E2e4.SlidingBlock.Logic.slnx
dotnet format whitespace E2e4.SlidingBlock.Logic.slnx --verify-no-changes
```

The tree must build with no warnings (warnings are errors) and the tests must pass. The benchmarks run with
`dotnet run -c Release --project benchmarks/E2e4.SlidingBlock.Logic.Benchmarks`; run them before and after a change to a hot path.

CI (`.github/workflows/ci.yml`) runs only when started by hand (`gh workflow run ci.yml --ref <branch>`), so please run the tests locally.

## Rules

- No package dependencies on `net8.0` and `net10.0`; `netstandard2.0` may depend only on `System.Memory`. No WPF or WinForms in the library, no I/O. The library targets `net10.0;net8.0;netstandard2.0`: `net10.0` is the primary target, and every `#if` or polyfill for the older ones lives in `src/E2e4.SlidingBlock.Logic/Compat/`. Bitboards go through `IBitboard<T>`, not generic-math operators.
- State types are structs on purpose and move generation writes into caller-supplied `Span<TMove>` buffers: keep the hot paths allocation-free.
- Public API needs XML documentation (a missing comment is a build error in the library project) and argument checks that throw `ArgumentException`
  and its subclasses, never `IndexOutOfRangeException` or `NullReferenceException`.
- Nullable is on. File-scoped namespaces and the `int` and `string` keywords are enforced by `.editorconfig`. Number formatting uses
  `CultureInfo.InvariantCulture`.
- Source files use CRLF line endings. Many editors write LF; `git ls-files --eol` shows the state, and `.gitattributes` converts on commit.
- Tests carry their own fixtures. Do not link source from the Labs or the benchmarks into the test project.
- Add a test with every fix or feature. The golden values in `GoldenTests` must not move without a reason in the commit message.
- Keep the documentation in `docs/` in step with the code when the object model changes, and add a line to [CHANGELOG.md](CHANGELOG.md).

## Pull requests

Describe what changes and why, and tick the checklist in the pull request template. Do not copy in third-party code, and keep real account names and
secrets out of commits.

## Licence

By contributing you agree that your work is released under the [MIT licence](LICENSE).
