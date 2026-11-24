# Night Dock: notes for agents

- Team and roles: /mnt/project-files/agents/night-dock.md (Studio Head orchestrates; each agent has one job).
- Design + all numbers: /mnt/project-files/night-dock/design-m1.md. `Balance.cs` must match it.
- Scope: Milestone 1 only. New ideas go to the "Later" list in design-m1.md, not into code.
- Rules live in `Assets/NightDock/Scripts/Sim` (no UnityEngine references; asmdef enforces it).
  Run `dotnet test` and `dotnet run` in `tools/simcheck` after any rules or balance change.
- Every colour cue needs a second cue (letters, text, icons). The player has trouble telling colours apart.
- Builds run on the owner's PC (Unity CLI / batchmode), never GitHub Actions.
- Every build goes through the QA Tester before the owner sees it.
- Original game: no names, art, UI or levels from existing games.
