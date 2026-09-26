# PRIMAL FRONTIER (repo: LAST OF THE PRIMAL) -- Game Studio Agent Architecture

Original single-player prehistoric survival game. Unity 6.3 LTS (6000.3.10f1), URP, C#, PC first.
Work is organised with the Claude Code Game Studios agent framework (49 agents, 73 skills) copied into `.claude/`.

## Source of truth

- Project brief (all 54 sections, scope rules, Definition of Done): `design/PRIMAL_FRONTIER_BRIEF.md`
- Game concept (studio GDD format): `design/gdd/game-concept.md`
- Vertical slice plan + agent allocation: `production/milestones/vertical-slice-plan.md`
- Live status report: `Documentation/PRIMAL_FRONTIER_STATUS.md`

**Scope rule (overrides everything):** one small island (~500 x 500 m), first night only. No new biomes, islands,
multiplayer, vehicles, online systems, base-building expansion or extra dinosaurs. Original IP only; ARK is a genre
reference and nothing may be copied from it.

## Technology Stack

- **Engine**: Unity 6000.3.10f1 (Unity 6.3 LTS)
- **Render pipeline**: URP 17.3 (PC_RPAsset / PC_Renderer)
- **Language**: C# (.NET Standard 2.1, Unity 6 API)
- **Input**: Unity Input System 1.18 (keyboard + mouse primary)
- **AI navigation**: com.unity.ai.navigation (NavMesh)
- **Version Control**: Git, trunk-based on `main`, checkpoint commits per phase
- **Asset Pipeline**: Blender 5.2 (via Blender MCP) -> FBX -> Unity. Blender sources live in `E:\Model game khủng long`
  (`PRIMAL_FRONTIER_WORLD.blend`, `scripts/`, `textures/`, `export/`). Unity side importer: `Assets/_Project/Scripts/Editor/`.

## Project Structure

@.claude/docs/directory-structure.md

## Engine Version Reference

@docs/engine-reference/unity/VERSION.md

## Technical Preferences

@.claude/docs/technical-preferences.md

## Coordination Rules

@.claude/docs/coordination-rules.md

## Collaboration Protocol

**User-driven collaboration, not autonomous execution.**
Every task follows: **Question -> Options -> Decision -> Draft -> Approval**

- Agents MUST ask "May I write this to [filepath]?" before using Write/Edit tools
- Agents MUST show drafts or summaries before requesting approval
- Multi-file changes require explicit approval for the full changeset
- No commits without user instruction (phase checkpoints are pre-approved by the brief)

See `docs/COLLABORATIVE-DESIGN-PRINCIPLE.md` for full protocol and examples.

## Coding Standards

@.claude/docs/coding-standards.md

## Context Management

@.claude/docs/context-management.md
