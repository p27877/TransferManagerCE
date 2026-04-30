# Goods Route via Warehouses

## Summary

This branch adds an optional routing mode that forces supported supply-chain materials to flow through warehouses instead of allowing every valid producer and consumer to match directly.

The feature is controlled by a new save-game setting named `RouteSupplyViaWarehouses`.

## Goal

In some cities, direct matching lets raw materials, intermediate products, or final goods skip warehouse buffering too often. That can reduce the usefulness of storage buildings and make supply distribution harder to shape.

This branch adds a stricter routing option so warehouses can act as the intended handoff points in the supply chain.

## What Changed

### 1. New warehouse-routing setting

`Settings/SaveGameSettings.cs` adds:

- `RouteSupplyViaWarehouses`
- save data version bump from 39 to 40
- serialization, deserialization, and debug output support

`Settings/SettingsUI.cs` adds a checkbox and description in the warehouse options panel so the feature can be enabled per save.

### 2. New supply-chain material classification

`CustomManager/TransferManagerModes.cs` adds `IsSupplyChainWarehouseRoutingMaterial(...)`.

Supported materials include:

- raw materials: `Oil`, `Ore`, `ForestProducts`, `Crops`
- generic industry inputs/outputs: `Coal`, `Petrol`, `Food`, `Lumber`
- Industries DLC intermediates: `Flours`, `Paper`, `PlanedTimber`, `Petroleum`, `Plastics`, `Glass`, `Metals`, `AnimalProducts`
- final products: `Goods`, `LuxuryProducts`, `Fish`

### 3. Producer and consumer building detection

`Helpers/BuildingTypeHelper.cs` adds:

- `IsSupplyChainProducer(...)`
- `IsSupplyChainConsumer(...)`

These helpers classify buildings by role so TMCE can tell whether a match is trying to bypass warehouses.

Examples:

- extractors are treated as producers of raw materials
- generic processors consume raw materials and produce processed materials
- generic factories consume processed materials and produce `Goods`
- commercial buildings consume `Goods`, `LuxuryProducts`, and `Food`
- fish buildings and unique factories are handled explicitly

### 4. Matching restriction to enforce warehouse routing

`CustomManager/TransferRestrictions.cs` adds a new exclusion reason: `SupplyWarehouseRouting`.

When `RouteSupplyViaWarehouses` is enabled for a supported material:

- direct producer → consumer matches are blocked
- direct producer/outside connection matches are blocked
- direct outside connection → consumer matches are blocked
- matches where either side is already a warehouse are still allowed

This keeps supported materials moving through warehouse offers instead of bypassing storage.

## Behavior

When the option is off, TMCE behaves as before.

When the option is on:

1. Check whether the transfer reason is in the supported supply-chain set
2. Allow the match immediately if one side is a warehouse
3. Otherwise block direct matches that would bypass warehouse storage
4. Let TMCE continue searching for a valid warehouse-mediated match

## Why This Feature Exists

The feature gives players tighter control over logistics for industry and goods chains.

Instead of letting factories, extractors, outside connections, and end consumers connect directly whenever restrictions allow it, this mode makes warehouses the preferred transfer hub for the supported materials.

## Limitations

- The feature is optional and disabled by default
- It only applies to the supported supply-chain materials
- It changes match eligibility, not vehicle pathfinding
- It depends on building-type heuristics, so modded or unusual buildings may still need follow-up tuning

## Files Touched

- `CustomManager/TransferManagerModes.cs`
- `CustomManager/TransferRestrictions.cs`
- `Helpers/BuildingTypeHelper.cs`
- `Settings/SaveGameSettings.cs`
- `Settings/SettingsUI.cs`
- `Locales/en.csv`
- `Serializer/TransferManagerSerializer.cs`

## Expected Result

With the option enabled, raw materials, processed materials, and supported goods should use warehouse storage as an intermediate routing step more consistently, improving controllability of supply-chain flow in warehouse-heavy cities.
