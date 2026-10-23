# E2e4.SlidingBlock.Logic documentation

Research notes and design explanations for the engine's object model. Read them in order, or jump to the topic you need.

| # | Document | What it covers |
|---|----------|----------------|
| 1 | [Why three representations](01-representations.md) | PSPACE-completeness, and why the N-puzzle, Rush Hour and Klotski/polyominoes each get their own 64-bit state layout |
| 2 | [LSB normalisation](02-lsb-normalisation.md) | Anchors as the least-significant set bit; proof that shape offsets are never negative |
| 3 | [Valid anchors and hook moves](03-valid-anchors-and-hook-moves.md) | `ValidAnchorsMask` instead of sentinel walls; the register-only BFS flood fill for multi-step corner moves |
| 4 | [Zobrist hashing and GroupAnchors](04-zobrist-and-group-anchors.md) | Removing the k! identical-piece symmetry with permutation-invariant hashing and a lossless canonical state |
| 5 | [Object model](05-object-model.md) | Topology vs state types, the domain interface and the solver, with class and flow diagrams |
| 6 | [The WPF laboratories](06-labs.md) | What `E2e4.SlidingBlock.Logic.Labs.Wpf` shows and how it maps onto the engine |
| 7 | [References](references.md) | Papers and prior work |
| - | [Glossary](glossary.md) | Terms and acronyms (LSB, BFS, IDA*, ...) with short definitions |

Diagrams are [Mermaid](https://mermaid.js.org/) and render directly on GitHub.
