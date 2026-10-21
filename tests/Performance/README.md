# Performance benchmarks (KN-P07)

Local only: nothing here ships in the app.

```powershell
.\tests\Performance\Run.ps1              # 1k, 10k and 50k notes
.\tests\Performance\Run.ps1 -Sizes 1k    # quick check
```

The benchmarks are tests in `KillerNotes.Tests\Perf`, skipped in normal test runs unless
`KN_PERF=1`, which the script sets. Each size builds a synthetic notebook from a fixed seed,
times the everyday operations (open, sidebar, search, opening and saving a long note, typing,
global search, the graph, vault export) after a warm-up, records memory, and deletes the
notebook. Results land in `C:\Users\steve\scratch\kn-perf\<timestamp>\results-<size>.json`;
only the newest three runs are kept.

Targets (not release gates): search under 200 ms at 10,000 notes; typing under 50 ms at the
95th percentile. The finished summary lives in
`KillerHarness\validation\killernotes-performance-1.6.0.md`.
