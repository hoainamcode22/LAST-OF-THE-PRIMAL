# Agent protocol (Lead + agents), 2026-09-29

The Lead (main Claude session) plans, assigns, reviews every agent report against evidence (numbers, captures, tests)
and merges. Agents never edit files owned by another agent; a needed change goes into the agent's report as a request.

## Shared tools and places
- Cloud mirror of the Unity code: `/home/claude/pf/src` (`Scripts/`, `Tests/`, `Shaders/`). Docs mirror: `/home/claude/pf/docs/Documentation/`.
- Compile check: `cd /home/claude/pf && ./cc.sh all`. It compiles the whole mirror: if errors appear only in files another agent owns, wait a minute and retry; never "fix" their files.
- PC project: `E:\LAST OF THE PRIMAL` (PC shell `$HOME/mnt/"LAST OF THE PRIMAL"`). Blender folder: `E:\Model game khủng long`.
- Deploy: never `mkzip.py` (it pushes everybody's changed files). Build a zip by hand with only your files: entries keyed relative to `src/` (e.g. `Scripts/AI/DinoSenses.cs`, `Art/...` for assets), plus `_manifest.json` = {rel: md5}. Write it to `/mnt/user-data/outputs/pf_up_<agent><N>.zip`, copy it with `mcp__remote-devices__device_commit_files` to `E:\LAST OF THE PRIMAL\Tools\pf_up_<agent><N>.zip`, run `$HOME/deploy.sh pf_up_<agent><N>` in the PC shell (extract into Assets/_Project, refresh, wait for compile, print errors).

## Unity bridge lock (one agent at a time)
The editor bridge (`$HOME/run.sh`, `$HOME/deploy.sh`) must be used by one agent at a time. The lock is a file in
`$HOME/mnt/"LAST OF THE PRIMAL"/Library/PrimalBridge/`:
- acquire: `cd "$HOME/mnt/LAST OF THE PRIMAL/Library/PrimalBridge" && mv -n bridge.lock.free bridge.lock.taken && [ -f bridge.lock.taken ] && [ ! -f bridge.lock.free ] && echo "<agent> $(date -u +%H:%M:%S)" > bridge.lock.taken && echo GOT`
  (only proceed when it prints GOT; otherwise `cat bridge.lock.taken` to see who holds it, wait 60-120 s and retry);
- hold it for one deploy / test / capture cycle (aim for under 10 minutes), then release:
  `cd "$HOME/mnt/LAST OF THE PRIMAL/Library/PrimalBridge" && echo "free $(date -u +%H:%M:%S)" > bridge.lock.taken && mv -n bridge.lock.taken bridge.lock.free`;
- never release a lock you do not hold. A lock older than 25 minutes may be reported to the Lead, not broken.
- `run.sh <id> ...` returns immediately if `result_<id>.json` already exists: always use fresh ids with your agent prefix (e.g. `U_17`, `P_4`).
- PlayMode tests: `$HOME/run.sh <id> PrimalTestRunner.RunPlayMode "<ClassName or empty for all>" 3`, poll `playmode_tests.txt` for DONE. Never `WaitForEndOfFrame` in tests; `[Timeout]` on every UnityTest; island tests call `TestScenes.UseTestSaves()`.

## Rules
No git commands (the Lead commits, without Claude trailers). No push. Never kill processes, never run Unity in batch mode,
never run `PrimalGameplayBuilder`. No deleting files on the PC (not permitted). Original work only. Concise writing, no em-dashes.

## Console check (added 2026-09-29, PC phase)
`$HOME/run.sh <fresh id> PrimalEditorBridge.ConsoleCheck "" 5` returns the editor Console counts ("N entries, E errors,
W warnings") plus the first unique error lines, and writes every entry to `Library/PrimalBridge/console.txt`. Run it
after each deploy / builder run. Only errors that your change caused are yours to fix; report others to the Lead.
Owner testing policy for the PC phase: no full PlayMode suites; compile + Console + asset import checks after each major
step; a targeted PlayMode test only when nothing else can check the change.
