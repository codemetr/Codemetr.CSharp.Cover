# Branch protection and NuGet releases

## Protect main (repository administrator)

Workflow files alone do not enforce branch protection. In GitHub, open
**Settings > Rules > Rulesets**, create a branch ruleset targeting `main`, and:

- Set enforcement to **Active** and leave the bypass list empty.
- Enable **Require a pull request before merging**.
- Enable **Require status checks to pass** and select the `build` job of the `ci`
  workflow (GitHub Actions). Run CI once if the check is not yet selectable. Add
  further analyzers (for example Codacy) once they report in this repository.
- Enable **Require branches to be up to date before merging**.
- Enable **Block force pushes** and **Restrict deletions**.

CI runs for every pull request without path filters, for pushes to `main` and for
merge queues. Keep the `build` job name stable, or update the required check.
Protect `codemetr-cover.json`, `codemetr-cover.baseline.json` and `codemetr.json`
with code-owner review: whoever edits them decides the rules.

## One-time NuGet setup

1. On nuget.org, open **Trusted Publishing** and create a GitHub Actions policy:
   - Package owner: the NuGet organization `codemetr`.
   - Repository owner: `codemetr`; repository: `Codemetr.CSharp.Cover`.
   - Workflow file: `release.yml` (file name only).
   - Environment: `nuget`.
   - Package glob: `Codemetr.CSharp.Cover.Tool` (not `*`).
   - Scopes: push new packages and package versions, to allow the first release.
2. In GitHub **Settings > Environments**, create the environment `nuget` and
   restrict deployment branches to `main`.
3. Add the environment secret `NUGET_USER`: your personal NuGet profile name, not
   an email address or the organization name.

```powershell
gh secret set NUGET_USER --env nuget --repo codemetr/Codemetr.CSharp.Cover
```

The release job exchanges a GitHub OIDC token through `NuGet/login` for a
temporary API key right before publishing; no persistent API key is stored.
See [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing).

## Publish a version

1. Merge the intended changes into `main` through a passing PR.
2. Open **Actions > release > Run workflow**, select `main`, and enter a new
   version, for example `0.1.0` or `0.2.0-rc.1`.
3. The workflow builds and tests the solution (with the integration test), runs
   the metrics guard and its own coverage gate, packs the tool, installs the packed
   tool from the local artifact for a smoke test, and pushes it to nuget.org.

The version overrides `Directory.Build.props`. NuGet versions cannot be
overwritten: use a new version for every publication. The `Codemetr.CSharp`
library is bundled in the tool package (it is taken from the pinned
`Codemetr.CSharp.Tool` package, see `eng/CodemetrCSharp.targets`).

After indexing, users need the .NET 10 SDK, but no source code:

```bash
dotnet tool install --global Codemetr.CSharp.Cover.Tool
codemetr-csharp-cover --help
```

Upgrade with `dotnet tool update --global Codemetr.CSharp.Cover.Tool`.
