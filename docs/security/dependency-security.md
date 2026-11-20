# Dependency And Secret Security

OPS-02 adds source dependency and secret scanning without deployment, image scanning, release automation or forced dependency upgrades.

## Automated Checks

`.github/workflows/security.yml` runs for pull requests and pushes on `develop` and `main`, plus every Monday at 05:00 UTC. The weekly run detects advisories published without a repository change.

`Dependency Security Scan` uses the .NET SDK vulnerability report for direct and transitive NuGet packages, then runs `npm audit` against the committed lockfile. `Secret Scan` runs Gitleaks over the complete fetched Git history with findings fully redacted.

Both jobs use read-only repository permissions. They do not publish reports or artifacts containing findings.

The official checkout and SDK setup actions used by both CI workflows are pinned to immutable release commit SHAs. Dependabot can propose reviewed updates without leaving execution tied to a mutable major-version tag.

## Severity Policy

Dependency scans block integration for reliable High or Critical advisories. Low and Moderate findings are reported but do not block the initial baseline.

`npm audit --audit-level=high` implements this threshold directly. `scripts/security/Test-NuGetVulnerabilities.ps1` reads the official structured .NET report, prints package, version, severity and advisory URL, and fails when a High or Critical finding exists.

If a package source, advisory source, scanner download or report parser is unavailable, the relevant check fails as a scanning failure. Source/network failure is not reported as a vulnerability, but inability to establish the baseline is not silently accepted.

## Secret Scanner

GamesHud uses the Gitleaks CLI rather than a third-party JavaScript action. CI downloads Gitleaks `8.30.1` from the official release, pins the Linux x64 archive SHA-256, verifies it before extraction and runs the CLI with `--redact=100`.

This keeps the scanner version reproducible, avoids additional action permissions and makes download/verification failure distinct from a detected leak. The checkout uses `fetch-depth: 0` so committed history is scanned, not only the current diff.

A detected secret fails the check. A potential historical leak must be investigated and rotated if real; GamesHud must never automatically rewrite Git history. Reports must identify type and location without reproducing the complete credential.

No Gitleaks allowlist is currently required. If a future synthetic fixture triggers a false positive, make the fixture obviously test-only or add the narrowest rule-specific exception. Do not ignore whole test, JSON or example-configuration directories.

## OPS-02 Remediation Baseline

The baseline was established on 2026-08-25.

### npm

The original lockfile contained two transitive High findings through `vite 8.1.4`:

| Package | Original | Fixed | Dependency chain | Advisories | Compatibility |
| --- | --- | --- | --- | --- | --- |
| `postcss` | `8.5.16` | `8.5.26` | `vite -> postcss` | [GHSA-r28c-9q8g-f849](https://github.com/advisories/GHSA-r28c-9q8g-f849), [GHSA-fxqj-rqcc-2cmp](https://github.com/advisories/GHSA-fxqj-rqcc-2cmp) | Transitive patch update inside Vite's existing `^8.5.16` range. |
| `nanoid` | `3.3.15` | `3.3.18` | `vite -> postcss -> nanoid` | [GHSA-28wg-ghj8-5hjv](https://github.com/advisories/GHSA-28wg-ghj8-5hjv), [GHSA-2v37-7h3g-55p8](https://github.com/advisories/GHSA-2v37-7h3g-55p8) | Transitive patch update inside PostCSS's compatible 3.x range. |

No direct frontend dependency or Vite version changed. The fixes required no override, major update or application migration. Final npm baseline: zero vulnerabilities reported by `npm audit`.

### NuGet

The original solution scan contained three transitive High packages:

| Package | Original | Remediation | Dependency chain | Advisory | Compatibility |
| --- | --- | --- | --- | --- | --- |
| `SQLitePCLRaw.lib.e_sqlite3` | `2.1.6` | `SQLitePCLRaw.bundle_e_sqlite3 2.1.13` | `Microsoft.EntityFrameworkCore.Sqlite -> SQLitePCLRaw.bundle_e_sqlite3 -> SQLitePCLRaw.lib.e_sqlite3` | [GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q) | Same 2.1 bundle line; EF Core 8 requires `>=2.1.6`. |
| `System.Net.Http` | `4.3.0` | `xunit 2.9.3` selects its `netstandard2.0` assets | `xunit 2.5.3 -> xunit.core -> NETStandard.Library 1.6.1 -> System.Net.Http` | [GHSA-7jgj-8wvc-jh57](https://github.com/advisories/GHSA-7jgj-8wvc-jh57) | xUnit v2 minor update; no test API migration. |
| `System.Text.RegularExpressions` | `4.3.0` | `xunit 2.9.3` selects its `netstandard2.0` assets | `xunit 2.5.3 -> xunit.core -> NETStandard.Library 1.6.1 -> System.Text.RegularExpressions` | [GHSA-cmhx-cq75-c4mj](https://github.com/advisories/GHSA-cmhx-cq75-c4mj) | xUnit v2 minor update; no test API migration. |

Final NuGet baseline: zero findings from the official .NET vulnerability report, including transitive packages.

## Repository Artifact Protection

`.gitignore` excludes local environment files, common private-key containers, SQLite runtime files, app-local `gameshud-data` and any accidental `system/secrets` directory. Migrations remain tracked. No legitimate database fixture currently exists; adding one later requires explicit review.

Current source and history audits found variable names, placeholders and synthetic test values, but no confirmed production credential. Secret names such as `Secrets__MasterKey`, `ServerPassword` and `WebhookUrl` are not themselves leaks.

## Dependency Update Policy

Scanning does not authorize blind updates. Every remediation must identify the advisory, dependency chain, fixed version and breaking risk, then pass build and tests. Never use `npm audit fix --force`, incompatible overrides or automatic major upgrades.

Dependabot checks NuGet, npm and GitHub Actions weekly against `develop`. Minor and patch updates are grouped to reduce noise. Dependabot opens reviewable PRs only; it does not auto-merge, deploy or update production.

Docker image scanning, SteamCMD trust, SBOMs, signing and release provenance remain outside OPS-02 and belong to later release/supply-chain work.
