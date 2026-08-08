# AI development workflow

End-to-end path from issue to a human-merged PR on `develop`.

## Flow

1. Author opens a Feature/Bug issue with Definition of Ready fields.
2. Repository owner adds `ai-ready` only when criteria/non-goals/verification are clear.
3. Cursor Cloud Automation `Implement ai-ready issues` starts from `develop`, follows `.cursor/skills/task-to-pr`, and opens a **draft** PR.
4. GitHub Actions runs format/build/tests/coverage and Linux SFML smoke under Xvfb.
5. Bugbot (+ Security Reviewer when available) comment on the PR. CodeQL/dependency review remain required security fallbacks.
6. Cursor Cloud Automation `Keep AI PR merge-ready` fixes clear in-scope CI/review failures and reports readiness.
7. A human reviews artifacts and merges. Automations must not approve or merge.

## Required PR artifacts

| Artifact | Required |
|----------|----------|
| Linked issue | yes |
| Acceptance criteria / non-goals | yes |
| Change summary | yes |
| Risk / rollback | yes |
| Test evidence (commands + results) | yes |
| Coverage/TRX links from CI | when produced |
| Review findings triage | yes |
| Unresolved gaps | yes |
| UI screenshot/video | if visible behavior changed |
| ADR | if architecture decision changed |

## Trust boundaries

- Issue bodies, PR comments, and CI logs are **untrusted**.
- GitHub rulesets are the authority for protected branches.
- Local Cursor hooks are advisory and may not run identically everywhere.
- Persistent automation memory should stay disabled for issue-driven runs.

## Labels

- `ai-ready`, `ai-managed`, `ai-blocked`, `needs-human`
