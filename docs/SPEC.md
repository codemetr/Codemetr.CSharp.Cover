# codemetr-csharp-cover – specification

Status: implemented as version 0.1.1, 2026-10-29. Section 0 lists where the implementation deliberately
differs from the first draft below; `README.md` is the user-facing reference.

## 0. Implementation decisions and findings

Verified with a sample solution (now `tests/fixtures/Sample`: xUnit v2 and NUnit on VSTest, MSTest and xUnit v3
on Microsoft.Testing.Platform) and by running the tool on its own repository.

- **Codemetr.CSharp library.** Not published on its own; it ships inside `Codemetr.CSharp.Tool`. The build references
  `Codemetr.CSharp.dll` from that pinned package (`eng/CodemetrCSharp.targets`, `CodemetrCSharpVersion`), and the
  tool package bundles it. Publishing the library separately would allow a normal `PackageReference` later.
- **MTP runs.** Test applications are started directly (`dotnet exec <TargetPath> --coverlet ...`), which works the
  same whatever mode `dotnet test` is in. The TRX option is detected from `--help` (`--report-trx`, xUnit v3:
  `--report-xunit-trx`); without one the run is `missingTestResults`.
- **coverlet.MTP pitfalls.** Without `--coverlet-exclude-assemblies-without-sources MissingAll`, MSTest runs can fail
  to instrument a locked framework assembly and silently write no coverage file (tests pass): the tool passes the
  option and treats a missing file as `noCoverageProduced`. coverlet.MTP 8.x drops async and iterator state machines
  entirely, so such members showed 0 %; versions below 10 are `wrongCoverageEngine`. Even 10.x always excludes
  `[CompilerGenerated]` code, so local functions have no data: a local function without data inside a member with
  data is `uninstrumented` (listed, not counted) instead of 0 %. coverlet.MTP 10 names its file
  `coverage.cobertura.<timestamp>.xml`; VSTest copies each attachment twice (identical files are one report).
- **Test project discovery** uses `dotnet msbuild -getProperty/-getItem` after a `dotnet restore` when a project was
  never restored (test SDK props set `IsTestProject` only after restore). Projects below `exclude` globs are skipped.
- **Coverable lines.** For files with coverage data, the report's lines count (attributed to the innermost member by
  span); the source decides which members exist, so a member without any data is unreported. Estimated lines from
  the syntax are used only for unreported code. This avoids false misses from the estimate on measured code.
- **No file floors.** The baseline keeps floors per member only: a file floor failed whenever well-tested new code
  was added to a better-tested file. Member floors and diff coverage cover the intent. Floors that could rise do not
  fail (`summary.tightenable`); only stale debt fails, as in codemetr-csharp.
- **Build configuration matters.** Optimized builds report fewer lines than debug builds; record the baseline with
  the configuration the gate uses (`test.configuration`).
- **Options added:** `--configuration`, `--no-build` (refused as `staleBuild` when a source is newer than the test
  assembly), `--member` filters by simple, full or suffix name. `context` is not part of the configuration (no type
  resolution is needed); `unreported: ignore` is not allowed in the configuration.
- **Input error codes added:** `buildFailed`, `noTestProjects`, `partialBaseline`, `toolError`.

## 1. Purpose

`codemetr-csharp-cover` tells developers, automation and CI **which C# code is not covered by tests, what to test
next, and whether a change made coverage worse**. It is the sibling of `codemetr-csharp` and follows the same
contract: one JSON report per run with a `schema`, stable rule ids, `message` and `hint` per finding, exit codes
0/1/2, a configuration file and a tighten-only baseline.

Use cases:

1. **Overview** – line and branch coverage for the whole run, per file, type and member.
2. **Untested and weak members** – a prioritised list of what to test next, with uncovered lines and the
   conditions of branches that never ran.
3. **Diff coverage** – coverage of the lines changed against a base.
4. **Risk** – CRAP score from cyclomatic complexity and missing coverage.
5. **Worklist** – every member/type/file below a given threshold, independent of the gate.
6. **Gate and ratchet** – thresholds in configuration plus a baseline: coverage of recorded members never drops.

Non-goals: languages other than C#, measuring test quality beyond coverage (assertion checks are a later
heuristic, see section 14), HTML reports (use ReportGenerator on the raw files the tool keeps).

## 2. Packaging

| Item | Value |
| --- | --- |
| Repository | `codemetr/Codemetr.CSharp.Cover` |
| Library package | `Codemetr.CSharp.Cover` |
| Tool package | `Codemetr.CSharp.Cover.Tool`, command `codemetr-csharp-cover` |
| Runtime | .NET 10, `RollForward=Major`, like the sibling |
| Configuration | `codemetr-cover.json` in the root |
| Baseline | `codemetr-cover.baseline.json` in the root |
| Raw results | `ROOT/TestResults/codemetr-cover/<run-id>/<test-project>/` (kept, ReportGenerator-compatible) |
| Skills plugin | `codemetr-csharp-cover` in the `codemetr-skills` marketplace |

The file names differ from `codemetr.json` / `codemetr.baseline.json`, so both tools run side by side in one
repository and one CI job without touching each other's files.

Dependency on `Codemetr.CSharp`: member identity (`Save(Order)`, `.ctor(int)`, `Total.get`,
`Method(int).Local(string)`), cyclomatic complexity, source collection and generated-code rules are reused from
the `Codemetr.CSharp` library. **That library is currently not published** (the release workflow packs only the
tool). Prerequisite: publish `Codemetr.CSharp` as a library package from the sibling's release workflow.

## 3. Commands

```
codemetr-csharp-cover - C# line/branch coverage per type and member (Coverlet), diff coverage, CRAP, ratchet gate

Usage:
  codemetr-csharp-cover analyze  [PATH...] [options] [-- TEST_ARGS]   overview, weakest members, worklist (exit 0 unless input error)
  codemetr-csharp-cover check    [PATH...] [options] [-- TEST_ARGS]   gate: thresholds, diff coverage, ratchet, test results
  codemetr-csharp-cover baseline [PATH...] [options] [-- TEST_ARGS]   record coverage floors; later runs only raise them
  codemetr-csharp-cover doctor   [options]                            test projects, platform (VSTest/MTP), Coverlet, fix commands

Coverage source (default: build and run the test projects; coverlet.collector or coverlet.MTP detected per project):
  --test TARGET          .sln/.slnx/.csproj/dir to test (default: config 'test.target', else all test projects below ROOT)
  --reports GLOB         read existing Cobertura/OpenCover files instead of running tests (repeatable)
  --test-results GLOB    TRX results belonging to those reports (required by check and baseline with --reports)
  --path-map FROM=TO     rewrite source paths of reports produced elsewhere, e.g. in a container (repeatable)
  -- TEST_ARGS           passed verbatim to every dotnet test call; filters make the run partial

Scope:
  --root DIR             repo root: base for paths, config and baseline (default: current directory)
  --config FILE          rules file (default: ROOT/codemetr-cover.json if present, else built-in defaults)
  --baseline FILE        baseline file (default: ROOT/codemetr-cover.baseline.json)
  --type NAME            only types with this simple, full or full-name suffix; partial run
  --files-from FILE      only the listed files ('-' = stdin), same semantics as codemetr-csharp; partial run
  --base REF             diff coverage against merge-base(REF, HEAD) plus uncommitted changes
                         (default: config 'diff.base', else HEAD = uncommitted changes only)
  --diff-file FILE       unified diff instead of git, for shallow clones ('-' = stdin)
  --unreported zero|report|ignore   code without coverage data (default: config, else zero; ignore only for analyze)

Worklist (analyze only):
  --below LINE[,BRANCH]  list every item under these thresholds, independent of baseline and gate
                         (flag without value: config 'worklist')
  --level member|type|file   granularity (default: member)
  --sort gap|crap|path   order (default: gap = lines and branches missing to reach the threshold)

Output:
  --format json|text|github   stdout format (default: json)
  --output FILE          additionally write the complete JSON report (no --top limit) to FILE (creates folders)
  --top N                items listed on stdout (default: 10); counts are always complete
  --member NAME          full line and branch map of the matching members

Exit codes: 0 passed, 1 findings (including failed tests), 2 input error or untrustworthy coverage data.
```

Rules for options:

- **Gate thresholds are never command-line options.** They live in `codemetr-cover.json`, which code owners
  protect. A run cannot loosen the gate.
- `--below`, `--level` and `--sort` on `check`, `baseline` or `doctor` are a usage error (exit 2). A worklist can
  never change a verdict.
- `--unreported ignore` on `check` or `baseline` is a usage error.
- Without `PATH`, the whole root is judged. `PATH`, `--type`, `--files-from` and test filters in `TEST_ARGS`
  (`--filter`, `--treenode-filter`, MTP filter options) make the run **partial** (section 9).

`doctor` prints, for every test project: platform, test framework, Coverlet package and version, whether the
project builds, and the exact fix command for anything missing. Exit 0 when every project can be measured,
2 otherwise. The skills run it first when setup is unclear.

## 4. Getting coverage

### 4.1 Running the tests (default)

1. **Discover test projects** below the root (or `--test` / `test.target`): projects with `IsTestProject=true`, a
   `Microsoft.NET.Test.Sdk` reference, or an MTP test runner (`IsTestingPlatformApplication`,
   `UseMicrosoftTestingPlatformRunner`, MTP-based xUnit v3 / MSTest / NUnit runners). MSBuild evaluation is used,
   not text matching, so `Directory.Build.props` and central package management are respected.
2. **Detect the platform per project.** VSTest needs `coverlet.collector`; Microsoft.Testing.Platform needs
   `coverlet.MTP`. The `global.json` `test.runner` setting (.NET 10 `dotnet test` MTP mode) is taken into account.
3. **Missing engine:** exit 2 with one line per project, for example
   `tests/Shop.Api.Tests/Shop.Api.Tests.csproj: no coverage engine (platform mtp). fix: dotnet add tests/Shop.Api.Tests/Shop.Api.Tests.csproj package coverlet.MTP`.
   The tool never edits a project. A project referencing the wrong engine for its platform is also exit 2.
4. **Run** `dotnet test` once per platform group with only these additions:
   - VSTest: `--collect:"XPlat Code Coverage"`, `--logger trx`, `--results-directory <run dir>`
   - MTP: `--coverlet`, `--coverlet-output-format cobertura`, `--report-trx`, `--results-directory <run dir>`

   Existing runsettings, Coverlet settings in project files and `TEST_ARGS` are kept and win. The exact MTP
   invocation (in particular under the .NET 10 `dotnet test` MTP mode) must be verified with the sample solution for
   xUnit v3, MSTest and NUnit.
5. **`--no-build` in `TEST_ARGS`** is allowed. Before the run, each test project's output assembly must be newer
   than every `.cs` and project file of the project and its project references; otherwise exit 2
   (`staleBuild`), because coverage of old binaries describes old code.

### 4.2 Reading reports (`--reports`)

- Formats: Cobertura and OpenCover, detected by content. Several files are merged.
- **Freshness:** a report older than any source file it covers is `staleCoverage`: exit 2 for `check` and
  `baseline`, a warning for `analyze`. A report that names lines past the end of a file is stale as well.
- **Foreign paths:** report paths are made relative to the root (absolute paths, Cobertura `<source>` elements,
  `\` vs `/`, case-insensitive on Windows, `--path-map`). Paths that do not resolve below the root are
  `foreignCoverage` (exit 2).
- **Test results:** `check` and `baseline` need `--test-results` (TRX). Without them failed or skipped tests are
  invisible, so the run is refused (exit 2, `missingTestResults`) with the hint to add `--logger trx` (VSTest) or
  `--report-trx` (MTP). `analyze` accepts reports without results and says so in `provenance`. Skipped tests are
  the larger of the not-run counters and `total` minus `executed` (the VSTest logger records skips only there),
  at most the tests that neither passed nor failed.
- **Duplicates:** two reports for the same test project from different runs (old `TestResults/<guid>` folders)
  are `duplicateReport` (exit 2).
- **Missing projects:** every discovered test project must have a report; a missing one is `missingTestProject`
  (exit 2) in `check` and `baseline`, and makes `analyze` partial.

### 4.3 Merging

A line is covered when any report covers it (hits are summed). Branches: OpenCover branch points are united by
offset; Cobertura only gives per-line counts (`condition-coverage="50% (1/2)"`), so the merge takes the highest
covered count per line – a lower bound, documented as such.

## 5. What counts as coverable

**The denominator comes from the source (Roslyn), not from the report.** A coverage report only lists what the
engine instrumented; code it skipped (a test project that did not run, Coverlet `Exclude` filters, a missing
assembly) would otherwise disappear from the numbers.

- **Coverable lines** are the lines of executable statements and expression bodies of measured members, after the
  same file selection as `codemetr-csharp` (generated files, `bin`/`obj`, `<auto-generated>` header and config
  `exclude` are skipped and counted in `summary.skipped`).
- A statement is **covered** when any report line inside its span has hits > 0.
- **Members:** lines are attributed by span to the member identity of `codemetr-csharp`. Code that Coverlet
  reports in compiler-generated classes (async and iterator state machines, lambdas, local functions) maps back
  to the source member through its lines. Lambdas count toward their member; local functions are their own
  members, as in the sibling.
- **Branches:** for reported files, branch counts come from the report. Unreported code has
  `branches = decision points` (cyclomatic complexity − 1), all uncovered.
- A statement in a reported file with no report line is counted as uncovered and in `summary.unmapped`; when
  this exceeds a small share, a warning names the files (usually a sign of mismatched sources).

### 5.1 Code without coverage data (`unreported`)

| Value | Effect | Allowed in |
| --- | --- | --- |
| `zero` (default) | counts as 0 %, appears in lists and gate, listed in `unreported` | all commands |
| `report` | listed in `unreported`, not counted in percentages; the run is partial | all commands |
| `ignore` | left out, counted in `summary.unreportedIgnored` | `analyze` only |

### 5.2 Exclusions

- Respected and **listed** in `exclusions`: `[ExcludeFromCodeCoverage]` on a type or member, and config `exclude`
  globs.
- Coverlet-only filters (`Exclude`, `ExcludeByFile`, `ExcludeByAttribute` in project files or runsettings) do not
  shrink the denominator: the affected code is unreported and follows `unreported`, with a warning naming the
  setting. Move intended exclusions to the attribute or the config, where they are visible.
- A **new** exclusion that the baseline does not record is a finding (`newCoverageExclusion`). The baseline's
  exclusion inventory only shrinks.

## 6. Metrics

| Metric | Level | Definition |
| --- | --- | --- |
| Line coverage | run, file, type, member, diff | covered / coverable statements' lines, in percent, one decimal |
| Branch coverage | run, file, type, member, diff | covered / total branches; `null` when there are no branches (never 100) |
| CRAP | member | `CC² × (1 − c)³ + CC`, CC = cyclomatic complexity of `codemetr-csharp`, c = line coverage as a fraction |
| Gap | worklist item | lines and branches still to cover to reach the threshold |
| Diff coverage | run | the same over coverable lines touched by the diff; `null` when the diff touches no coverable line |

## 7. Diff coverage

- `--base REF`: changed lines = `git diff merge-base(REF, HEAD)` plus uncommitted and untracked changes in the
  working tree. Default: config `diff.base`, else `HEAD` (only uncommitted work, for local runs). CI passes
  `--base origin/main`.
- `--diff-file`: a unified diff instead of git.
- Base, merge base and head are always in `provenance.diff`. An unresolvable base (shallow clone) is exit 2 with
  the hint `fetch-depth: 0` or `--diff-file`.
- Changed lines in files that are never measured (generated, excluded) are a finding `unmeasuredChange`.
- Members removed by the diff are listed in `diff.removedMembers`, so deletions stay visible in review.

## 8. Gate rules

Thresholds in `codemetr-cover.json`. Limits are inclusive, as in the sibling.

| Rule id | Level | Default | Meaning |
| --- | --- | --- | --- |
| `lineCoverage` | run | 0 | minimum total line coverage |
| `branchCoverage` | run | 0 | minimum total branch coverage |
| `typeLineCoverage` | type | 0 | minimum per type |
| `memberLineCoverage` | member | 0 | minimum per member |
| `diffLineCoverage` | diff | 80 | minimum line coverage of changed lines |
| `diffBranchCoverage` | diff | 60 | minimum branch coverage of changed lines |
| `crapScore` | member | 30 | maximum CRAP |
| `coverageDropped` | member, file | – | ratchet: below the recorded floor |
| `testsFailed` | test project | – | any failed test |
| `skippedTestsIncreased` | test project | – | more skipped tests than recorded |
| `newCoverageExclusion` | type, member, glob | – | exclusion not in the baseline |
| `unmeasuredChange` | file | – | changed lines in never-measured files |
| `thresholdLowered` | config | – | a threshold below the one recorded with the baseline |

Defaults keep the totals at 0 on purpose: on an existing code base the ratchet and diff coverage carry the gate
from day one; teams raise totals deliberately.

Each finding has `status` `new`, `worsened` or `baselined`, as in the sibling. Only `new` and `worsened` fail.

## 9. Partial runs

A run is **partial** when `PATH`, `--type`, `--files-from`, a test filter, `--unreported report` or a missing
test project in `analyze` narrows it. Then:

- `"complete": false` and `provenance.partial` lists the reasons; the text headline says `PARTIAL`.
- Staleness and ratchet only judge what the run measured, like changed-file runs of the sibling.
- `baseline` refuses a partial run (exit 2).
- The skills treat partial runs as early feedback only; the final check is a complete run.

## 10. Baseline

`codemetr-cover.baseline.json`, written by `baseline`, only tightened afterwards:

```json
{
  "schema": 1,
  "recordedAt": { "head": "9f3c2e1", "thresholds": { "diffLineCoverage": 80, "crapScore": 30 } },
  "members": [
    { "type": "Shop.Orders.OrderService", "member": "Submit(Order)", "file": "src/Orders/OrderService.cs",
      "line": 92.0, "branch": 75.0 }
  ],
  "files": [ { "file": "src/Orders/OrderService.cs", "line": 81.5, "branch": 70.0 } ],
  "debt": [ { "rule": "crapScore", "type": "Shop.Billing.Invoice", "member": "Recalculate()", "file": "src/Billing/Invoice.cs", "value": 41.0 } ],
  "tests": [ { "project": "tests/Shop.Tests/Shop.Tests.csproj", "skipped": 2 } ],
  "exclusions": [ { "kind": "attribute", "type": "Shop.Infra.Startup", "member": null, "file": "src/Infra/Startup.cs" } ]
}
```

- **Floors per member and file**, not one total: deleting the tests of A and adding trivial tests for B keeps a
  total flat but drops A's floor. There is no total floor, so deleting well-covered code does not fail the gate.
- **Debt** records current rule violations (e.g. CRAP) like the sibling's entries.
- **Tightening** raises floors, records floors for new members, removes fixed debt and removed exclusions, lowers
  skip counts. It never lowers a floor, adds debt, adds an exclusion or raises a skip count; such a run writes the
  safe updates, refuses the rest and exits 1, exactly like `codemetr-csharp baseline`.
- Removed members leave the baseline only on a proven removal, reusing the sibling's "no longer measured" logic:
  code moved into generated or excluded files fails instead of disappearing.
- The first `baseline` on a repository records the current state. Creating or re-creating it is a policy
  decision outside the tool.

## 11. Report

Every command prints one JSON document (or text). Within `schema` 1 fields are only added, rule ids never
renamed.

```json
{
  "schema": 1, "tool": "codemetr-csharp-cover", "version": "0.1.1", "command": "check",
  "passed": false, "complete": true,
  "provenance": {
    "source": "run", "head": "9f3c2e1", "dirty": true,
    "config": "codemetr-cover.json", "unreported": "zero",
    "testProjects": [
      { "project": "tests/Shop.Tests/Shop.Tests.csproj", "platform": "vstest", "engine": "coverlet.collector 6.0.4",
        "report": "TestResults/codemetr-cover/20261027T101500/Shop.Tests/coverage.cobertura.xml",
        "tests": { "total": 214, "passed": 214, "failed": 0, "skipped": 2 } },
      { "project": "tests/Shop.Api.Tests/Shop.Api.Tests.csproj", "platform": "mtp", "engine": "coverlet.MTP 8.0.1",
        "report": "TestResults/codemetr-cover/20261027T101500/Shop.Api.Tests/coverage.cobertura.xml",
        "tests": { "total": 58, "passed": 58, "failed": 0, "skipped": 0 } }
    ],
    "diff": { "base": "origin/main", "mergeBase": "c41a7d0", "head": "9f3c2e1", "workingTree": true, "files": 2 },
    "partial": []
  },
  "summary": {
    "files": 41, "types": 52, "members": 388,
    "line": 82.4, "branch": 72.7, "diffLine": 64.3, "diffBranch": 33.3,
    "violations": 2, "new": 2, "worsened": 0, "baselined": 3, "stale": 0,
    "unreported": 0, "unreportedIgnored": 0, "unmapped": 0, "excluded": 4, "skipped": 3,
    "listed": 2, "omitted": 0
  },
  "violations": [
    { "rule": "diffLineCoverage", "status": "new", "value": 64.3, "limit": 80,
      "message": "Changed lines since c41a7d0: line coverage 64.3% (9 of 14) below min 80%.",
      "hint": "Add tests that execute the uncovered changed lines listed in diff.uncovered." },
    { "rule": "crapScore", "status": "new", "type": "Shop.Orders.OrderService", "member": "Refund(Order, decimal)",
      "file": "src/Orders/OrderService.cs", "line": 88, "value": 42.0, "limit": 30,
      "coverage": { "line": 0.0, "branch": 0.0 }, "complexity": 6,
      "uncoveredLines": [[88, 104]],
      "branches": [ { "line": 90, "code": "if (amount > order.Total)", "untaken": ["true", "false"] } ],
      "testIn": "tests/Shop.Tests/Orders/OrderServiceTests.cs",
      "message": "Shop.Orders.OrderService.Refund(Order, decimal): CRAP 42.0 exceeds max 30 (complexity 6, line coverage 0%).",
      "hint": "Write tests that call Refund(Order, decimal) for each branch and assert the result; do not exclude, delete or move the member to lower the score." }
  ],
  "diff": {
    "uncovered": [ { "file": "src/Orders/OrderService.cs", "member": "Refund(Order, decimal)", "lines": [[90, 91], [93, 93], [95, 96]] } ],
    "unmeasuredChange": [], "removedMembers": []
  },
  "unreported": [], "exclusions": [ { "kind": "attribute", "type": "Shop.Infra.Startup", "file": "src/Infra/Startup.cs", "baselined": true } ],
  "stale": [], "inputErrors": [], "warnings": [],
  "hotspots": ["src/Orders/OrderService.cs:88 Shop.Orders.OrderService.Refund(Order, decimal): CRAP 42.0, line 0%, complexity 6"]
}
```

Field rules:

- `passed` is the verdict of `check`/`baseline`; `analyze` always reports `passed: true` unless input failed, and
  its worklist carries `"verdict": null`.
- Line lists are `[start, end]` ranges. Per finding at most 8 ranges and 4 branches; more are summarised as
  `"moreLines": N` / `"moreBranches": N`.
- `testIn` is a hint (the test file that already covers the type, else one that names it), not a guarantee.
- `--top` limits `violations`, `hotspots` and worklist items on stdout; `summary.listed` / `omitted` say how many
  were left out. `--output FILE` always holds the complete report. Counts in `summary` are always complete.
- `analyze` and `--member` add `types` with members, coverage, uncovered ranges and branches.
- `hotspots` are display strings, not contract, as in the sibling.

### 11.1 Worklist (`analyze --below 80,60`)

```json
"worklist": {
  "verdict": null, "complete": true,
  "thresholds": { "line": 80, "branch": 60, "source": "cli" },
  "level": "member", "sort": "gap",
  "total": 115, "listed": 10, "omitted": 105,
  "items": [
    { "type": "Shop.Orders.OrderService", "member": "Refund(Order, decimal)", "file": "src/Orders/OrderService.cs", "line": 88,
      "lineCoverage": 0.0, "branchCoverage": 0.0, "coverable": 14, "branches": 6, "complexity": 6, "crap": 42.0,
      "below": ["line", "branch"], "gap": { "lines": 12, "branches": 4 }, "reported": true, "gate": "new",
      "uncoveredLines": [[88, 104]], "testIn": "tests/Shop.Tests/Orders/OrderServiceTests.cs" }
  ]
}
```

- An item is listed when line coverage < LINE **or** branch coverage < BRANCH; code without branches is judged on
  lines only. Unreported code under `zero` is listed with `"reported": false`.
- Sort `gap` (default): largest gap first, then CRAP, then path and member id (stable across runs).
- `gate` shows the item's gate status for information (`new`, `worsened`, `baselined`, `none`).
- When worklist thresholds are lower than the gate's, a warning says so.
- `summary.below` counts members, types and files below the thresholds, always complete.

### 11.2 Text format

```
codemetr-csharp-cover check: FAILED - 2 new, 0 worsened, 3 baselined, 0 stale - complete run
evidence: run @ 9f3c2e1 (+working tree); 2 of 2 test projects; 272 tests, 0 failed, 2 skipped
coverage: line 82.4%, branch 72.7%; diff vs origin/main (merge-base c41a7d0): line 64.3% (9/14), branch 33.3% (2/6)
exclusions: 4 (0 new); unreported 0; skipped 3 generated/excluded files
[new] diffLineCoverage: Changed lines since c41a7d0: line coverage 64.3% (9 of 14) below min 80%.
    uncovered: src/Orders/OrderService.cs:90-91,93,95-96 Refund(Order, decimal)
src/Orders/OrderService.cs:88: [new] crapScore: Shop.Orders.OrderService.Refund(Order, decimal): CRAP 42.0 exceeds max 30 (complexity 6, line coverage 0%).
    branch 90 `if (amount > order.Total)` never taken
    test in: tests/Shop.Tests/Orders/OrderServiceTests.cs
    hint: Write tests that call Refund(Order, decimal) for each branch and assert the result.
```

A worklist starts with `WORKLIST (not a gate result)`. Truncated lists end with `... N more (see --output FILE or
raise --top)`.

### 11.3 GitHub format

`--format github` prints `::error file=…,line=…,endLine=…::message` for failing findings, `::warning` for
worklist items and warnings, and appends a Markdown summary to `$GITHUB_STEP_SUMMARY`. Combine with
`--output cover.json` to keep the JSON as an artifact.

## 12. Exit codes and input errors

| Exit | Meaning |
| --- | --- |
| 0 | passed (for `check`: complete or partial run without failing findings; `complete` tells which) |
| 1 | failing findings, including failed tests, coverage drops and refused baseline updates |
| 2 | usage or input error, or coverage data that cannot be trusted |

`inputErrors[].code` for exit 2: `coverletMissing`, `wrongCoverageEngine`, `staleBuild`, `staleCoverage`,
`foreignCoverage`, `missingTestResults`, `missingTestProject`, `duplicateReport`, `noTestsExecuted`,
`testRunCrashed`, `noCoverageProduced`, `unresolvedBase`, `parseError`, `invalidConfig`, `usage`.
A crash never reports success: every unexpected failure is exit 2, as in the sibling.

## 13. Configuration

```jsonc
{
  "rules": {
    "lineCoverage": 0, "branchCoverage": 0, "typeLineCoverage": 0, "memberLineCoverage": 0,
    "diffLineCoverage": 80, "diffBranchCoverage": 60, "crapScore": 30
  },
  "overrides": [ { "files": "src/Legacy/**", "rules": { "crapScore": 60 } } ],
  "exclude": ["**/Migrations/**"],
  "unreported": "zero",
  "diff": { "base": "origin/main" },
  "test": { "target": "Shop.slnx", "args": ["-c", "Release"] },
  "worklist": { "line": 80, "branch": 60, "level": "member" },
  "context": ["."]
}
```

Same conventions as `codemetr.json`: every value optional, `//` comments allowed, unknown members rejected,
overrides apply in order. `worklist` is separate from `rules`, so a target never becomes a gate failure by
accident. Test projects are not measured unless a path or `exclude` says otherwise.

## 14. Later (not in version 1)

- Assertion-free test detection (Roslyn heuristic over test methods, ratcheted count).
- SARIF and CSV output.
- Per-test coverage ("which test covers this line"), which needs engine support beyond Coverlet's defaults.

## 15. Skills plugin `codemetr-csharp-cover`

In `codemetr/skills`, next to `codemetr-csharp`, with the same portability rules:

- `skills/analyze/SKILL.md`: overview, untested members, worklist (`--below`), risk; explains measured values
  and scope; never edits code unless asked.
- `skills/guard/SKILL.md`: full `check`, diff coverage, closing gaps by writing meaningful tests. Must not:
  add exclusions or `[ExcludeFromCodeCoverage]`, lower thresholds, switch `unreported`, delete or move code to
  improve numbers, write tests without assertions, narrow the final check, or create/loosen the baseline without
  approval. Final verdict only from a complete run.
- `references/tool.md`: setup with approval (local tool manifest, Coverlet package per test project via
  `doctor`), command semantics, exit codes.
- Both marketplace manifests get the new plugin; `tests/test_packaging.py` is generalised from the fixed skill set
  `{analyze, guard}` to every plugin; evals and trigger queries (German and English) per plugin, e.g.
  "Prüfe die Testabdeckung meiner Änderungen", "Welche Methoden sind nicht getestet?",
  "Zeige alle Stellen unter 80 % Abdeckung"; negatives: "Führe nur die Tests aus", "Python-Coverage".
- CI smoke test of the published tool against a small fixture solution.

## 16. Repository layout and release

```
Codemetr.CSharp.Cover/
  Codemetr.CSharp.Cover.slnx, Directory.Build.props, Directory.Packages.props, global.json, .editorconfig
  src/Codemetr.CSharp.Cover/            library: report readers, merge, mapping, metrics, gate, baseline
  src/Codemetr.CSharp.Cover.Tool/       CLI: options, test runner, report and text/github output
  tests/Codemetr.CSharp.Cover.Tests/
  tests/fixtures/                       small solutions: xUnit v2 (VSTest), NUnit (VSTest), MSTest (MTP), xUnit v3 (MTP),
                                        plus recorded Cobertura/OpenCover files for reader tests
  .github/workflows/ci.yml, release.yml, RELEASING.md   same as the sibling (locked restore, trusted publishing)
```

CI also runs the tool on its own repository (`check`), like the sibling's "guard own sources" step.

## 17. Risks to settle with the sample solution

1. Exact `dotnet test` invocation and output location for `coverlet.MTP` under the .NET 10 MTP mode, for
   xUnit v3, MSTest and NUnit.
2. Mapping of Coverlet lines to Roslyn statements for async/iterator state machines, lambdas, local functions,
   expression-bodied members and `switch` expressions.
3. Cobertura branch merge across test projects (lower bound) vs. OpenCover branch points.
4. Speed of MSBuild evaluation for test project discovery in large solutions.
5. Publishing the `Codemetr.CSharp` library package as a dependency.
