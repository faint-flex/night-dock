# Night Dock

An original overnight freight-terminal tycoon for Windows, built in Unity 6.

Trailers roll in at 10 PM. Route every pallet to the right outbound door, load the last stop first,
stay under the weight limit, and get every truck out before the 6:00 AM cutoff. Spend the night's
profit on forklifts, a faster scanner or another door, then run the next shift.

Milestone 1, "One Good Shift", is a gray box: plain boxes on purpose, so the loop gets fun before art.

## Layout
- `Assets/NightDock/Scripts/Sim` - the rules engine, pure C# (no Unity), unit tested.
- `Assets/NightDock/Scripts/Game` - the Unity side: gray-box terminal, forklifts, camera, HUD. Builds itself at runtime.
- `Assets/NightDock/Tests/EditMode` - NUnit tests for the rules (run in Unity's Test Runner).
- `Assets/NightDock/Editor/NightDockBuild.cs` - setup and Windows build entry points.
- `tools/simcheck` - runs the same tests and a balance bot without Unity (`dotnet test`, `dotnet run`).

## Build (Windows, from the repo folder)
```
"C:\Program Files\Unity\Hub\Editor\<version>\Editor\Unity.exe" -batchmode -quit -projectPath . -executeMethod NightDock.EditorTools.NightDockBuild.BuildWindows -logFile build.log
```
Output: `Builds/NightDock-<VERSION>/NightDock.exe`.

Screenshot run (autopilot plays one shift and saves screenshots, then quits):
```
Builds\NightDock-0.1.0\NightDock.exe -ndshots C:\path\to\shots -screen-width 1920 -screen-height 1080
```

## Controls
Click a pallet, then a door (or press 1-7; 0 = staging, X = cancel). Click a door to see its load and
dispatch it (Enter). Space pause, F fast, WASD / screen edge pan, scroll zoom, Q/E rotate,
middle-drag orbit, Esc menu.
