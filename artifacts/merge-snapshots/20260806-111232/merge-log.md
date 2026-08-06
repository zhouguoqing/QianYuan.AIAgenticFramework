# Merge snapshot
created: 2026-08-06T11:12:32.9768796+08:00
snapshot: artifacts\merge-snapshots\20260806-111232
local head: 35cec7378d662929b3aa3d6ae57af015c247afff
remote origin/main: 12f01075c6e591cf2a8a0f2631ddebebeccd107d
strategy: reset to cloud baseline, then reapply local tracked patch and untracked feature files selectively

## Corrected merge strategy
- User clarification: compare remote against remote history only; apply only new remote delta 35cec73..12f0107, keep local uncommitted work where remote did not change it.
- Fetched origin/main: 12f01075c6e591cf2a8a0f2631ddebebeccd107d.
- Remote incremental files recorded in: remote-incremental-35cec73-to-12f0107.diff.
- Reset to origin/main, replayed local tracked patch excluding overlapping files.
- Restored local versions of overlapping files from snapshot: ImagesController.cs, appsettings.json.
- Manually applied only remote new image fallback changes into those local files.
- Validation: git diff --check passed after EOF cleanup.
- Validation: dotnet build --no-restore passed with warnings only.
- Validation: dotnet test tests/QianYuan.Core.Tests/QianYuan.Core.Tests.csproj --no-build --filter ImageGenerationTests passed 4/4.
- Validation: npm.cmd run build in src/QianYuan.Web passed with chunk-size warnings only.
- Cleanup: git restore --staged . executed so all merged changes remain unstaged in the working tree.
