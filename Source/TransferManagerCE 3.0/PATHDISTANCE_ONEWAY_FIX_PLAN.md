# Path Distance One-Way Direction Fix

## Summary

This branch fixes one-way road direction handling in TMCE's path-distance based offer matching without changing the global transfer-mode strategy.

The implemented fix is intentionally narrow: when the currently matched offer is passive but a candidate offer is active, TMCE now evaluates directed connectivity and path distance from the active side instead of incorrectly searching from the passive side.

## What Changed

### 1. Path-distance matching now corrects search direction per offer/candidate pair

`CustomManager/CustomTransferManager.cs` now detects the specific case where the outer-loop `offer` is passive and a candidate is active.

- Normal candidates still use the existing search flow from `offer` to candidate nodes
- Reverse-direction candidates are collected separately
- For those reverse candidates, TMCE evaluates reachability from the active candidate node back to the passive offer node
- The final match still chooses the shortest reachable result across normal and reverse-direction candidates

This keeps the existing matching modes intact while correcting one-way directionality at the point where the wrong search origin mattered.

### 2. Connected LOS now uses the same directed reachability rule

`CustomManager/CustomTransferManager.cs` also updates the `ConnectedLineOfSight` connectivity pre-check.

- Normal candidate checks still use `offerNode -> candidateNode`
- Passive-offer / active-candidate checks now use `candidateNode -> offerNode`
- LOS distance scoring itself is unchanged

This keeps Connected LOS aligned with the same active-side direction rule used by path-distance matching.

## Behavior

When TMCE performs path-distance or connected-LOS matching:

1. Keep the existing transfer mode and outer matching loop order
2. Evaluate transfer restrictions as before
3. If the current offer is the traveling/active side, search from that offer as before
4. If the current offer is passive but the candidate is active, evaluate connectivity from the candidate side instead
5. Select the best reachable candidate without changing priority or global matching policy

## Why This Fix Exists

The bug was not that TMCE always built the wrong road graph; it was that path-based matching could start its directed search from the wrong side of a transfer.

For service-style transfers such as police dispatch on one-way roads, the actual traveler can be the candidate offer rather than the currently iterated offer. When that happened, TMCE could test reachability in the reverse direction and prefer offers that were only reachable backwards on a one-way network.

This fix corrects that search-origin mistake at the offer/candidate level without expanding into a broader match-strategy rewrite.

## Limits of the Current Fix

- This implementation is designed to stay small and low-risk
- It specifically fixes the passive-offer / active-candidate direction mismatch
- It does **not** attempt a full generic solution for every possible `Active/Active` combination
- Reverse-direction path-distance candidates are evaluated one by one, so large reverse-candidate sets may cost more than the normal single-search path
- Full build verification could not be completed in this workspace because the local shared project import `SleepyCommon.projitems` is missing from the current environment

## Files Touched

- `CustomManager/CustomTransferManager.cs`
- `PATHDISTANCE_ONEWAY_FIX_PLAN.md`

## Expected Result

TMCE should no longer prefer reverse-only police/service matches on one-way layouts when the true traveling side is an active candidate rather than the current outer-loop offer. In practice this should improve one-way dispatch behavior for `Crime` and similar service-pathing cases that depend on directed reachability from the active side.
