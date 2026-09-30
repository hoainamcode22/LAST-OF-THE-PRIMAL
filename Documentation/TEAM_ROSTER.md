# Team roster (Lead + agents), 2026-09-30

Read with `AGENT_PROTOCOL.md` (rules, bridge lock, deploy) and `Phase3/WAVE1_OWNERSHIP.md` (file ownership).
Agent role definitions: `.claude/agents/*.md` (same 49 agents / 73 skills as `E:\Claude-Code-Game-Studios`).

## Places
- Game logic, Unity project: `E:\LAST OF THE PRIMAL` (PC shell `$HOME/mnt/"LAST OF THE PRIMAL"`), code in `Assets/_Project/`.
- Blender sources, scripts, renders, exports: `E:\Model game khủng long` (Blender 5.2.1 LTS via Blender MCP).
- Studio framework (reference only, do not edit): `E:\Claude-Code-Game-Studios`.

## Bridges
- Blender: Blender MCP `execute_blender_code` into the open Blender. One agent in Blender at a time; save to a new or backup file before destructive edits.
- Unity: `PrimalEditorBridge` (editor must be open). Scripts `Tools/Bridge/run.sh` and `Tools/Bridge/deploy.sh`; at session start copy them to `$HOME/` (`cp "$HOME/mnt/LAST OF THE PRIMAL/Tools/Bridge/"*.sh "$HOME/"`). Methods are `Type.Method`, e.g. `PrimalEditorBridge.Ping`, `PrimalEditorBridge.ConsoleCheck`. Old result ids exist from earlier sessions: always prefix ids with agent + time.

## Agents (code, prefix, studio role, scope)
| Code | Prefix | Studio agent(s) | Scope |
|---|---|---|---|
| LEAD | L_ | producer + technical-director | plans, assigns, reviews evidence, merges, commits (no Claude trailers, no push) |
| CHAR | C_ | technical-artist + art-director | player / NPC models, rigs, clips in Blender, export staging |
| DINO | D_ | technical-artist + art-director | dinosaur models, rigs, clips, textures in Blender |
| ENV | E_ | technical-artist + level-designer | terrain, vegetation, rocks, environment kit (Blender + Unity builder) |
| WORLD | W_ | world-builder + level-designer | weather, day phases, water, volcano danger, discovery |
| STORY | T_ | narrative-director + writer | journal, notes, tutorial text, lore (original IP only) |
| U | U_ | gameplay-programmer + unity-specialist | combat, animation driver, input, IK, audio, VFX |
| SURV | S_ | gameplay-programmer + systems-designer | survival stats, items, crafting, campfire, save |
| RES | R_ | gameplay-programmer | resource nodes, harvesting, pickups, placement |
| AI | P_ | ai-programmer | dinosaur AI, perception, stimuli, creature save |
| BUILD | B_ | gameplay-programmer + unity-ui-specialist | building pieces, snapping, validation, build menu |
| QA | Q_ | qa-lead + qa-tester | compile + Console clean, short PlayMode checks, bug reports |

A task from the owner is split by the Lead into these codes; each agent edits only its own files and reports back with evidence (numbers, captures, Console result).
