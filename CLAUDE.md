# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Scope

- The latest active code is only in `Source/TransferManagerCE 3.0/`. Older `Source/TransferManagerCE 1.x` and `2.x` folders are historical and should not be edited unless explicitly requested.
- This is a Cities: Skylines 1 mod that replaces/improves vanilla transfer matching so service and cargo transfers prefer realistic nearby/pathable matches.

## Build and validation commands

Run commands from the repository root unless noted.

```bash
# Build the active solution; also runs the project PostBuild copy into the local Cities: Skylines Mods folder
dotnet build "Source/TransferManagerCE 3.0/TransferManagerCE.sln" -c Debug --no-restore

# Release build
dotnet build "Source/TransferManagerCE 3.0/TransferManagerCE.sln" -c Release --no-restore

# Test-flavoured configurations available in the solution
dotnet build "Source/TransferManagerCE 3.0/TransferManagerCE.sln" -c "TEST Debug" --no-restore
dotnet build "Source/TransferManagerCE 3.0/TransferManagerCE.sln" -c "TEST Release" --no-restore
```

There is no separate unit-test project in the active tree. Validate changes by building and, for behavior/UI changes, loading the mod in Cities: Skylines and checking the affected panel or simulation path.

Known build notes:
- The project targets `net35` with SDK-style MSBuild and references Cities: Skylines assemblies from `../../../Managed/*.dll` plus bundled/reference DLLs under `Assemblies/` and `References/`.
- The solution imports shared code from `Source/SleepyCommon/SleepyCommon.shproj` via `SleepyCommon.projitems`.
- A successful local build may still warn about `CitiesHarmony.API` reference resolution while producing `TransferManagerCE.dll`.
- PostBuild copies `TransferManagerCE.dll` and `Assemblies/UnifiedUILib.dll` to `%LOCALAPPDATA%/Colossal Order/Cities_Skylines/Addons/Mods/TransferManagerCE`.

## High-level architecture

- `TransferManagerMod.cs` is the main mod entry point (`UserModBase`). On level load it checks conflicts, starts manager objects/threads, applies Harmony patches, creates UI buttons/tools, validates settings, and updates the path-distance cache. On unload it unpatches, stops threads, destroys queues/panels, and clears settings.
- `Harmony/Patcher.cs` owns the patch list under Harmony ID `Sleepy.TransferManagerCE`. It conditionally skips conflicting patches for mods such as Advanced Outside Connections and Smarter Fire Fighters, and separately manages reversible transpilers.
- `Harmony/TransferManager/*` intercepts vanilla `TransferManager.AddIncomingOffer`, `AddOutgoingOffer`, `MatchOffers`, and `StartTransfer`. When `SaveGameSettings.EnableNewTransferManager` is true, vanilla matching is replaced by the custom dispatcher; otherwise vanilla behavior continues.
- `CustomManager/` contains the replacement matching pipeline. `CustomTransferDispatcher` captures vanilla offer arrays into `TransferJob`s, `TransferJobQueue`/`TransferJobPool` manage work items, `TransferManagerThread` runs background matching threads, and `CustomTransferManager` performs matching by priority, distance, restrictions, warehouse/factory rules, and selected path-distance algorithm.
- `PathDistance/` implements network-aware distance and connectivity. `PathDistance` uses Dijkstra-style traversal over `NodeLinkGraph`; `PathDistanceCache` and `PathConnectedCache` cache network data by mode; `BuildingSpawnPointsIntegration.cs` integrates building spawn/access points into this distance model.
- `TransferOffers/`, `TransferRules/`, and `CustomManager/TransferRestrictions*` adjust incoming/outgoing offers and apply building, district, outside connection, warehouse, factory, import/export, and distance restrictions before matches are accepted.
- `Settings/` splits persistent per-save settings (`SaveGameSettings`) from user XML settings (`ModSettings`). `Serializer/TransferManagerSerializer.cs` saves per-save data under data ID `TransferManagerCE` with explicit data versions and tuple guards.
- `UI/`, `SelectionTool/`, `Renderers/`, `Status/`, `Data/`, `MatchLogging/`, and `TransferIssue/` implement the in-game panels, highlighting/selection modes, path/render overlays, status rows, match logs, and issue reporting.

## Implementation cautions

- Keep changes focused on `Source/TransferManagerCE 3.0/` unless the user explicitly asks to touch historical versions.
- Matching and path-distance code runs in simulation/background-thread contexts; avoid Unity UI access or unsafe game-state mutation from worker threads.
- `AddIncomingOffer`/`AddOutgoingOffer` patches intentionally call `TransferManagerUtils.CheckRoadAccess` in the simulation thread before background matching; preserve that separation when changing path-distance behavior.
- Save-game data is versioned manually. When adding/removing persisted fields in `SaveGameSettings`, building settings, or outside connection settings, update the relevant data version and load/save ordering carefully.
- Many Harmony patches are compatibility-sensitive. Check `Patcher.cs` and `ModSettings` feature flags before adding a patch or changing a patch target.
- UI panels use ColossalFramework/Unity objects and should be created/destroyed on the game/UI lifecycle paths already established by `TransferManagerMod` and panel classes.
