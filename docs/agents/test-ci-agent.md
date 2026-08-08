# Test And CI Agent

## Mission

Keep verification cheap, documented, and enforced in CI.

## When to invoke

New test projects, CI workflow changes, coverage/artifacts, flaky tests, or verification documentation.

## Owns

- Unit test project structure
- `eng/verify.ps1` and README verification commands
- GitHub Actions quality gates design
- Coverage/TRX artifact expectations
- `docs/engineering/VERIFICATION_GAPS.md`

## Must preserve

- Core tests run without graphics/native windows
- SFML tests/smoke are isolated jobs/projects
- Failing checks report command, failure, and likely cause
- Do not weaken required checks to “make green”

## Baseline

- Core.Tests: headless simulation coverage
- Sfml.Tests: adapter-focused tests
- Client `--smoke-test` for short window loop

## Verification

- `pwsh eng/verify.ps1`
- Or `dotnet build` + `dotnet test` + smoke

## Escalation / git

- Native SFML CI failures → sfml-platform
- Draft PR only; never merge from this role
