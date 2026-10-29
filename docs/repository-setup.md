# Protect main

In GitHub Settings → Rules → Rulesets, import [`main-ruleset.json`](main-ruleset.json), review the settings and save it. Alternatively, create an active branch ruleset targeting the default branch:

- Require a pull request before merging. Use zero required approvals for a solo-maintainer repository to avoid requiring self-approval.
- Require status checks: `Website checks` and `Windows build` (select them after the first checks run).
- Require branches to be up to date before merging.
- Block force pushes and branch deletion.
- Do not add bypass actors unless a specific recovery need exists.

These instructions do not activate protection. GitHub account access with repository administration permission is required to save the ruleset. The connected code tools do not provide a ruleset-write operation.

Keep the existing GitHub Pages publication from `main` while applying this setup. With protection active, changes reach the published branch through a pull request after successful checks. No donation service or new public binary is configured by this change.
