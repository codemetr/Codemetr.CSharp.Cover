# Changelog

## 0.1.1 - 2026-10-29

- Skipped tests of VSTest runs are counted: the TRX logger leaves `notExecuted` at 0 and records a skip only as
  `total` minus `executed`. Before, the report said "0 skipped" and `skippedTestsIncreased` never fired. Repositories
  whose baseline recorded 0 skips while tests were skipped now get that finding; record the skips with `baseline` or
  run the tests again.
- `--output` creates missing folders, so the complete report can go to an ignored folder such as `TestResults/`.

## 0.1.0 - 2026-10-29

- Tool `codemetr-csharp-cover` with `analyze`, `check`, `baseline` and `doctor`; JSON, text and GitHub output; exit
  codes 0/1/2; `--output` for the complete report.
- Runs tests with Coverlet per test project: `coverlet.collector` on VSTest, `coverlet.MTP` (10 or later) on
  Microsoft.Testing.Platform; xUnit v2/v3, NUnit and MSTest; never edits projects, names the exact fix.
- Reads Cobertura and OpenCover reports with TRX results; refuses stale, foreign or duplicate evidence and test
  projects without results in `check` and `baseline`.
- Coverable code comes from the source: unreported code counts as 0 % by default; exclusions are listed.
- Coverage per file, type and member with codemetr-csharp identities, uncovered line ranges, untaken branch
  conditions and a suggested test file; CRAP score per member.
- Diff coverage against a merge base plus the working tree, or a diff file.
- Gate rules with overrides; tighten-only baseline with member floors, debt, skip counts, exclusion inventory and
  recorded thresholds.
- Worklist `analyze --below LINE[,BRANCH]` at member, type or file level, independent of the gate.
