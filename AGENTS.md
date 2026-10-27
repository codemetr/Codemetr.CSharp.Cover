# Codemetr.CSharp.Cover agent rules

Global workspace instructions apply. Read `.github/RELEASING.md` before PR or release work.

- Keep the report contract: `schema` 1 only gains fields, rule ids and input error codes are never renamed, exit
  codes stay 0 passed, 1 findings, 2 input error. A crash must never report success.
- The gate must not be weakened to pass: no command-line thresholds, no silent narrowing, no baseline that loosens.
  New escape hatches need a finding or an input error that makes them visible.
- Never edit a consumer's test projects from the tool; report the exact fix instead.
- Run `dotnet test` and, before merging, the self checks of `.github/workflows/ci.yml`:
  `CODEMETR_COVER_INTEGRATION=1` for the integration test, `codemetr-csharp check src` for metrics and
  `codemetr-csharp-cover check` for coverage.
- Update `README.md`, `CHANGELOG.md` and `docs/SPEC.md` with behaviour changes, and the skills in
  `codemetr/skills` (plugin `codemetr-csharp-cover`) when commands, options or exit codes change.
