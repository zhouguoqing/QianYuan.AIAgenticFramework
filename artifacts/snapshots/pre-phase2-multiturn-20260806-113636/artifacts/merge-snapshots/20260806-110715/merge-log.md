snapshot: artifacts\merge-snapshots\20260806-110715
created: 2026-08-06T11:07:30.0094385+08:00
head before fetch: 35cec7378d662929b3aa3d6ae57af015c247afff

## Merge attempt log
- Fetch latest: blocked by sandbox approval service 503; used cached origin/main.
- Cached origin/main: 35cec7378d662929b3aa3d6ae57af015c247afff
- HEAD: 35cec7378d662929b3aa3d6ae57af015c247afff
- Upstream delta HEAD..origin/main: none.
- Merge strategy: cloud baseline already present; retained local feature overlay from snapshot.
- Backend validation: dotnet build --no-restore passed with 2 warnings.
- Web validation: npm.cmd run build passed.
- Desktop validation: skipped/failed because src/QianYuan.Desktop/node_modules is missing; no dependency install attempted.
