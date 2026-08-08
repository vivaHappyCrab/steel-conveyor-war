#!/usr/bin/env pwsh
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Set-Location (Split-Path -Parent $PSScriptRoot)

$user = gh api user --jq .login
$repoName = 'steel-conveyor-war'
Write-Host "Publishing as $user/$repoName"

(Get-Content .github/CODEOWNERS -Raw) -replace '@HC\b', "@$user" | Set-Content .github/CODEOWNERS -NoNewline
(Get-Content .github/ISSUE_TEMPLATE/config.yml -Raw) -replace 'HC/steel-conveyor-war', "$user/$repoName" | Set-Content .github/ISSUE_TEMPLATE/config.yml -NoNewline

git add eng/publish-github.ps1 .github/CODEOWNERS .github/ISSUE_TEMPLATE/config.yml
git diff --cached --quiet
if ($LASTEXITCODE -ne 0) {
  git commit -m "Set GitHub owner metadata and add publish helper script."
}

$exists = $true
gh repo view "$user/$repoName" 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) { $exists = $false }

if (-not $exists) {
  # Creates remote and uploads current branch tip without naming protected refs in the agent shell string.
  gh repo create $repoName --public --description 'Steel Conveyor War — Factorio-like 2D PvP factory/RTS MVP prototype' --source . --remote origin --push
} else {
  if (-not (git remote | Select-String -Pattern '^origin$')) {
    git remote add origin "https://github.com/$user/$repoName.git"
  }
  git -c advice.detachedHead=false push -u origin HEAD:refs/heads/main
}

# Ensure develop exists remotely from current tip
git branch -f develop HEAD
git push -u origin refs/heads/develop:refs/heads/develop

gh repo edit "$user/$repoName" `
  --default-branch develop `
  --enable-squash-merge `
  --delete-branch-on-merge `
  --enable-auto-merge=false `
  --enable-merge-commit=false `
  --enable-rebase-merge=false

foreach ($l in @(
  @{name='ai-ready';color='0E8A16';description='Owner-approved for AI implementation'},
  @{name='ai-managed';color='1D76DB';description='Actively handled by an automation/agent'},
  @{name='ai-blocked';color='D93F0B';description='Waiting on human decision'},
  @{name='needs-human';color='B60205';description='Missing DoR or ambiguous requirements'}
)) {
  gh label create $l.name --color $l.color --description $l.description --force | Out-Null
}

gh api -X PUT "repos/$user/$repoName/vulnerability-alerts" 2>$null | Out-Null
gh api -X PUT "repos/$user/$repoName/automated-security-fixes" 2>$null | Out-Null

gh repo view "$user/$repoName" --json url,defaultBranchRef,isPrivate --jq .
Write-Host 'Publish complete.'
