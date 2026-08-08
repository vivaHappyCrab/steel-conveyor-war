# Contributing to Steel Conveyor War

## Branches

- `develop` — default integration branch
- `main` — release branch
- Feature branches: `ai/<issue>-slug` or `feature/<slug>`

All changes land through pull requests. Do not push directly to `develop` or `main`.

## Workflow

1. Open a GitHub issue using a template (Definition of Ready).
2. Owner adds `ai-ready` when the issue is ready for an agent/human implementer.
3. Implement on a branch from `develop`, run `pwsh eng/verify.ps1`.
4. Open a **draft** PR using the PR template and attach evidence.
5. Wait for CI + Bugbot/security feedback; fix in-scope issues.
6. A human performs the final merge to `develop`.

## Labels

- `ai-ready` — owner-approved for automation/implementation
- `ai-managed` — actively handled by an automation/agent
- `ai-blocked` — waiting on human decision
- `needs-human` — missing DoR or ambiguous requirements

## Artifacts required on PRs

- Linked issue + acceptance criteria + non-goals
- Summary, risk/rollback, test evidence
- UI proof for visible changes; ADR for architecture changes

## Local commands

```powershell
pwsh eng/verify.ps1
dotnet build SteelConveyorWar.sln -c Release
dotnet test SteelConveyorWar.sln -c Release
dotnet run --project src/SteelConveyorWar.Client -- --smoke-test
```

## License

- Source code: MIT (`LICENSE`)
- `resources/` assets: All Rights Reserved (`resources/LICENSE.md`) unless a file says otherwise
