# BSP Path Distance Integration

## Summary

This branch integrates BuildingSpawnPoints (BSP) into TransferManagerCE's path-distance based offer matching.

The goal is to make TMCE evaluate building endpoints using the player's configured BSP spawn/unspawn points instead of relying only on the default building access position. This improves candidate selection when a building's real truck entry/exit is far from its vanilla road access.

## What Changed

### 1. BSP endpoint lookup via reflection

`PathDistance/BuildingSpawnPointsIntegration.cs` adds a reflection-based bridge to the BSP mod.

- No compile-time dependency on BSP
- Initialization is cached after first use
- Missing mod, incompatible versions, or reflection failures all fall back safely
- Matching code never throws if BSP is unavailable

### 2. Building node resolution now prefers BSP points

`PathDistance/PathNode.cs` now tries BSP first for building-derived endpoints.

- `Active` endpoints use BSP spawn points
- `Passive` endpoints use BSP unspawn points
- If a valid BSP point is found, TMCE resolves the road segment from that world position
- If BSP fails at any step, TMCE falls back to the original building segment/node logic

This applies to:

- building offers
- citizen offers that resolve to a building
- the Path Distance tool when selecting buildings

It does not change:

- vehicle offers
- net segment offers
- outside connection node lookup

### 3. BSP-specific vehicle category mapping

`PathDistance/PathDistanceTypes.cs` adds a dedicated transfer-reason to vehicle-category mapping for BSP point lookup.

This allows TMCE to query the most appropriate BSP point type for services such as hearses, ambulances, police, garbage, mail, and cargo traffic while still preserving the broader TMCE path-position fallback logic.

### 4. Path Distance tool consistency

`SelectionTool/SelectionMode/SelectModePathDistanceSelectCandidates.cs` now uses the same active/passive direction handling as the real path-distance calculation.

That keeps candidate overlays and node tooltips aligned with the actual matching result.

## Behavior

When TMCE performs path-distance matching:

1. Resolve the offer endpoint to a building node
2. If the endpoint comes from a building, try BSP first
3. Use spawn vs unspawn based on whether the endpoint is active or passive
4. Resolve a road segment from the BSP world position
5. Use that segment/node for path-distance candidate evaluation
6. If anything fails, use the original TMCE logic

## Why This Feature Exists

Vanilla building access often does not reflect the real place where service or cargo vehicles enter and leave a building.

When BSP custom points are used, the old TMCE path-distance calculation could still measure from the default building access and choose a less realistic candidate. This branch closes that gap by making candidate selection start from the same player-defined endpoint that vehicles are expected to use.

## Limitations

- This branch improves offer matching, not the game's low-level vehicle pathfinder
- Actual vehicle routing after dispatch is still handled by the game's normal pathfinding systems
- BSP is only consulted for building-derived endpoints
- Safe fallback remains the default whenever BSP data cannot be used

## Files Touched

- `PathDistance/BuildingSpawnPointsIntegration.cs`
- `PathDistance/PathDistanceTypes.cs`
- `PathDistance/PathNode.cs`
- `PathDistance/PathDistanceTest.cs`
- `SelectionTool/SelectionMode/SelectModePathDistanceSelectCandidates.cs`
- `.gitignore`

## Expected Result

With BSP installed and configured, TMCE should choose suppliers/consumers based on more realistic road access points for buildings. In practice this should improve match quality for warehouses, factories, and other buildings whose effective entry/exit is far from the vanilla access point.
