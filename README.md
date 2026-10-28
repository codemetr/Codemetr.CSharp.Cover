# Codemetr.CSharp.Cover

[![ci](https://github.com/codemetr/Codemetr.CSharp.Cover/actions/workflows/ci.yml/badge.svg)](https://github.com/codemetr/Codemetr.CSharp.Cover/actions/workflows/ci.yml)

C# test coverage per **type and member** for coding agents and CI. It runs the tests with
[Coverlet](https://github.com/coverlet-coverage/coverlet) (open source; xUnit, NUnit and MSTest on VSTest and on
Microsoft.Testing.Platform) or reads existing Cobertura/OpenCover reports. It lays the coverage over the source and
reports what is not tested, what to test next, and whether a change made coverage worse. Like its sibling
[`codemetr-csharp`](https://github.com/codemetr/Codemetr.CSharp), it writes one JSON report per run, keeps rule ids
and exit codes stable, and has a tighten-only baseline.

| Package | Content |
| --- | --- |
| `Codemetr.CSharp.Cover.Tool` | .NET tool `codemetr-csharp-cover` |

Requires the .NET 10 SDK. Member identities (`Save(Order)`, `.ctor(int)`, `Total.get`) and cyclomatic complexity
come from the `Codemetr.CSharp` library, so both tools name code the same way.

## Install and run

```bash
dotnet tool install --global Codemetr.CSharp.Cover.Tool

codemetr-csharp-cover doctor                          # test projects, VSTest/MTP, Coverlet engine, exact fixes
codemetr-csharp-cover analyze --format text           # coverage overview and riskiest under-tested members
codemetr-csharp-cover analyze --below 80,60           # every member under 80 % line or 60 % branch coverage
codemetr-csharp-cover check --base origin/main        # gate: diff coverage, CRAP, ratchet, failed tests
codemetr-csharp-cover baseline                        # first run: record coverage floors; later: only raise them
codemetr-csharp-cover analyze --member OrderService.Submit --format text   # full line and branch map of one member
```

| Exit code | Meaning |
| --- | --- |
| 0 | passed (`analyze`: always, unless input error) |
| 1 | findings: diff coverage or thresholds missed, coverage dropped, failed tests, new exclusions, stale debt |
| 2 | input error or untrustworthy coverage data (see [input errors](#input-errors)) |

A crash never reports success. With exit 2, stdout carries a JSON document with `inputErrors` (code, message,
hint) and stderr repeats it.

## Where coverage comes from

**Running the tests (default).** The tool finds test projects with MSBuild evaluation (so `Directory.Build.props` and
central package management count), builds them and runs each with the engine of its platform:

| Platform | Typical frameworks | Engine (a package in the test project) | How it runs |
| --- | --- | --- | --- |
| VSTest | xUnit v2, NUnit, MSTest | `coverlet.collector` | `dotnet test --collect "XPlat Code Coverage" --logger trx` |
| Microsoft.Testing.Platform | xUnit v3, MSTest runner, NUnit runner | `coverlet.MTP` 10 or later | the test application with `--coverlet` and a TRX report |

The tool never edits a project. A missing or wrong engine is exit 2 with the exact `dotnet add ... package ...`
command; `doctor` lists them all. Existing runsettings and Coverlet settings stay in effect. Arguments after `--` go to
every test run; `--test` picks a solution, project or directory; `--no-build` reuses binaries but is refused when a
source is newer than the test assembly. Raw files stay in `TestResults/codemetr-cover/<run>/` for ReportGenerator.

**Reading reports (`--reports GLOB`).** For slow suites or CI jobs that already produced coverage. `check` and
`baseline` also need the TRX results of the same run (`--test-results GLOB`), because failed or skipped tests would
otherwise be invisible. Reports older than a source they cover, reports with paths outside the root, two result files
for one test project, or a test project without results are exit 2. `--path-map FROM=TO` translates paths of reports
written elsewhere; deterministic `/_/` paths map to the root automatically.

Reports of several test projects are merged: a line is covered when any project covers it. All paths in reports and
baselines are relative to the root with `/`.

## What is measured

**What counts as coverable comes from the source, not from the report.** A member that the source declares but the
coverage data does not mention has no coverage data:

| `unreported` | Effect |
| --- | --- |
| `zero` (default) | counts as 0 % and is listed in `unreported` |
| `report` | listed, not counted; the run is partial |
| `ignore` | left out and only counted (`analyze` only) |

So a test project that never ran, or a Coverlet filter that hides code, cannot raise the numbers. Intended exclusions
are `[ExcludeFromCodeCoverage]` (also `[GeneratedCode]`, `[CompilerGenerated]`) and `exclude` globs in the
configuration; both are listed in `exclusions`, and a new one is a finding once a baseline exists.

| Metric | Level | Definition |
| --- | --- | --- |
| Line coverage | run, file, type, member, diff | covered / coverable lines, rounded down to one decimal |
| Branch coverage | same | covered / total branch outcomes; `null` without branches, never 100 % |
| CRAP | member | `CC² × (1 − c)³ + CC`, CC = cyclomatic complexity, c = line coverage |
| Diff coverage | run | coverage of the changed coverable lines; `null` when the diff touches none |
| Gap | worklist item | lines and branches still to cover to reach the threshold |

Lines are attributed to the innermost member, so a local function is its own member and lambdas count toward their
member. Async methods and iterators (compiler-generated state machines) map back to their member by line.

## Diff coverage

`--base REF` compares against `merge-base(REF, HEAD)` plus uncommitted and untracked changes. The default is
`diff.base` from the configuration, else `HEAD`, which means only uncommitted work and fits a local agent loop. In
CI pass `--base origin/main` with `fetch-depth: 0`, or `--diff-file` with a unified diff. Base, merge base and head
are in `provenance.diff`. Changed lines in generated or excluded files are a finding (`unmeasuredChange`).

## Configuration

`codemetr-cover.json` in the root (or `--config FILE`). Every value is optional, `//` comments are allowed, unknown
members are rejected. Gate thresholds exist only here, never as command-line options.

```jsonc
{
  "rules": {
    "lineCoverage": 0,          // minimum of the whole run
    "branchCoverage": 0,
    "typeLineCoverage": 0,      // minimum per type declaration
    "memberLineCoverage": 0,    // minimum per member
    "diffLineCoverage": 80,     // minimum of the changed lines
    "diffBranchCoverage": 60,
    "crapScore": 30             // maximum per member
  },
  "overrides": [ { "files": "src/Legacy/**", "rules": { "crapScore": 60 } } ],
  "exclude": ["**/Migrations/**"],
  "unreported": "zero",
  "diff": { "base": "origin/main" },
  "test": { "target": "Shop.slnx", "configuration": "Release", "args": [] },
  "worklist": { "line": 80, "branch": 60, "level": "member" }
}
```

The values shown are the defaults (except `overrides`, `exclude`, `diff`, `test` and `worklist`). Totals start at 0
on purpose: on an existing code base the ratchet and diff coverage carry the gate from day one; raise totals
deliberately. Limits are inclusive. Test projects are never measured as code. Projects below `exclude` globs are not
run either.

## Baseline

`baseline` writes `codemetr-cover.baseline.json` from a complete run with passing tests:

- a **floor per member** (line and branch coverage): falling below it is `coverageDropped`. Deleting the tests of one
  member and adding trivial tests for another cannot hide behind an unchanged total;
- **debt**: current `crapScore`, `memberLineCoverage` and `typeLineCoverage` violations, reported as `baselined`
  while they do not get worse;
- **skipped tests per project** (more skips is a finding), the **exclusion inventory** and the **thresholds**
  (a looser threshold is `thresholdLowered`).

Once the file exists, `baseline` **only tightens**: floors rise and are added for new members, fixed debt and removed
exclusions leave, skip counts fall. Lower floors, new debt, new exclusions and more skips are refused (exit 1); the
safe updates are still written. Debt that improved or disappeared is `stale` and fails until the baseline is
tightened, as in `codemetr-csharp`. Floors that could rise only count in `summary.tightenable`. A recorded member that
moved into generated or excluded code is `noLongerMeasured` instead of silently dropped. Re-creating the baseline is
a policy decision for its code owners.

## Worklist

`analyze --below LINE[,BRANCH]` lists every member (`--level type|file`) under the thresholds, independent of
baseline and gate. Without values it uses `worklist` from the configuration. It is sorted by gap (`--sort crap|path`),
each item carries its gate status for information, its uncovered lines and a suggested test file. The worklist has
`"verdict": null`, its options are a usage error on `check` and `baseline`, and stdout is limited by `--top` while
`summary.below` and `worklist.total` always count everything; `--output FILE` holds the complete list.

## Partial runs

`PATH`, `--type`, `--files-from`, test filters in `TEST_ARGS`, a narrower `--test` and `--unreported report` make a
run partial: `"complete": false`, the reasons in `provenance.partial`, `PARTIAL` in the text headline. Totals and
removed members are not judged then, and `baseline` refuses. Use partial runs for quick feedback; the gate is a
complete run.

## Report

```json
{
  "schema": 1, "tool": "codemetr-csharp-cover", "version": "0.1.0", "command": "check", "passed": false, "complete": true,
  "provenance": {
    "source": "run", "head": "9f3c2e1", "dirty": true, "unreported": "zero",
    "testProjects": [ { "project": "tests/Shop.Tests/Shop.Tests.csproj", "platform": "vstest", "engine": "coverlet.collector",
                        "reports": ["TestResults/codemetr-cover/20261027T101500000/Shop.Tests/coverage.cobertura.xml"],
                        "tests": { "total": 214, "passed": 214, "failed": 0, "skipped": 2 } } ],
    "diff": { "base": "origin/main", "mergeBase": "c41a7d0", "head": "9f3c2e1", "workingTree": true, "files": 2, "changedLines": 31 },
    "partial": []
  },
  "summary": { "files": 41, "types": 52, "members": 388, "line": 82.4, "branch": 72.7, "diffLine": 64.2, "diffBranch": 33.3,
               "violations": 2, "new": 2, "worsened": 0, "baselined": 3, "stale": 0, "tightenable": 5, "unreported": 0,
               "excluded": 4, "skipped": 3, "unmapped": 0, "listed": 2, "omitted": 0 },
  "violations": [
    { "rule": "crapScore", "status": "new", "type": "Shop.Orders.OrderService", "member": "Refund(Order, decimal)",
      "file": "src/Orders/OrderService.cs", "line": 88, "value": 42, "limit": 30,
      "coverage": { "line": 0, "branch": 0 }, "complexity": 6, "crap": 42, "uncoveredLines": [[89, 104]],
      "branches": [ { "line": 90, "code": "if (amount > order.Total)", "covered": 0, "total": 2 } ],
      "testIn": "tests/Shop.Tests/Orders/OrderServiceTests.cs",
      "message": "Shop.Orders.OrderService.Refund(Order, decimal): CRAP 42 exceeds max 30 (complexity 6, line coverage 0.0%).",
      "hint": "Write tests that cover each branch and assert the result, or split the member to reduce its complexity; do not exclude, delete or move it to lower the score." }
  ],
  "diff": { "uncovered": [ { "file": "src/Orders/OrderService.cs", "type": "Shop.Orders.OrderService", "member": "Refund(Order, decimal)", "lines": [[90, 91]] } ],
            "unmeasuredChange": [], "removedMembers": [] },
  "unreported": [], "exclusions": [], "stale": [], "parseErrors": [], "inputErrors": [], "warnings": [],
  "hotspots": ["..."]
}
```

Within `schema` 1 fields are only added and rule ids never renamed. Findings carry at most 8 line ranges and 4
branch lines (`moreLines`, `moreBranches` count the rest). `--top` limits the lists on stdout; counts are complete,
`--output FILE` writes the complete report including `types` with every member. `testIn` is a hint, `hotspots` are
display strings. `--format text` prints `file:line: [status] rule: message`; `--format github` prints workflow
annotations and a job summary.

Rule ids: `lineCoverage`, `branchCoverage`, `typeLineCoverage`, `memberLineCoverage`, `diffLineCoverage`,
`diffBranchCoverage`, `crapScore`, `coverageDropped`, `testsFailed`, `skippedTestsIncreased`, `newCoverageExclusion`,
`unmeasuredChange`, `noLongerMeasured`, `thresholdLowered`.

### Input errors

`coverletMissing`, `wrongCoverageEngine`, `buildFailed`, `staleBuild`, `testRunCrashed`, `noTestsExecuted`,
`noCoverageProduced`, `noTestProjects`, `missingTestResults`, `missingTestProject`, `duplicateReport`,
`staleCoverage`, `foreignCoverage`, `unresolvedBase`, `parseError`, `partialBaseline`, `invalidConfig`, `usage`,
`toolError`.

## Using it as a gate

- Local and agent runs give fast feedback; the gate is a complete `check` in CI with the repository's configuration
  and baseline. Protect `codemetr-cover.json` and `codemetr-cover.baseline.json` with code-owner review.
- GitHub Actions, next to `codemetr-csharp`:

```yaml
- uses: actions/checkout@v4
  with: { fetch-depth: 0 }
- uses: actions/setup-dotnet@v4
  with: { global-json-file: global.json }
- run: dotnet tool restore
- run: dotnet tool run codemetr-csharp check --format text
- run: dotnet tool run codemetr-csharp-cover check --base origin/${{ github.base_ref || 'main' }} --format github --output cover.json
```

## Limits

- Coverage shows which lines ran, not whether tests assert anything.
- Cobertura gives branch counts per line, not which outcome ran; merging several test projects keeps the highest
  count per line, a lower bound.
- coverlet.MTP does not instrument compiler-generated code such as local functions. A local function without data
  inside a member with data is listed as `uninstrumented` and not counted, instead of showing 0 %. If another
  (VSTest) project reports it uncovered, it counts as uncovered; the report then warns that MTP and VSTest data
  are merged.
- Estimated lines of unreported code follow statements and may differ slightly from what an engine would report.
- No project files are read for preprocessor symbols; code in inactive `#if` branches is not measured.

## License

MIT
