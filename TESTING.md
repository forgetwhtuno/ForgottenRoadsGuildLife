# Erenshor Guild Life 0.1.3 — live-test checklist

## Automated/source tests

Run:

```powershell
powershell -ExecutionPolicy Bypass -File .\RUN_TESTS.ps1
```

Expected coverage includes roster identity/diff boundaries, no-guild behavior, Bulletin bounds/dedupe/persistence/recovery, stable living-guild selection/transitions, grouped/unavailable exclusion, temporary tracking loss, long-gap/no-catch-up behavior, V1->V2 persistence, retained UI ownership, read-only native authority, and optional-integration contracts.

## Native build

- [ ] `RUN_TESTS.ps1` reports all PASS.
- [ ] `BUILD_AND_INSTALL.ps1` compiles against the current installed `Assembly-CSharp.dll`, Unity assemblies, and Lunaris references.
- [ ] Only `ErenshorGuildLife.dll` is installed; no game assembly or save is modified.
- [ ] Lunaris loads version `0.1.3` without missing-member exceptions.

## Native roster / stable identity

- [ ] In a guild, the correct native guild roster appears.
- [ ] Zone/level are shown only when current tracking exposes them; missing data remains unknown.
- [ ] Activity ownership follows the same Sim across a zone/runtime-avatar replacement when `simIndex` remains stable.
- [ ] A temporarily missing `MyAvatar` does not mark the member dead/unavailable.
- [ ] A positively loaded dead Sim is not admitted to a new Guild Life activity.
- [ ] A guild member who joins the player's current party is not assigned off-screen Guild Life activity; an already-running activity involving that member is interrupted.
- [ ] Leaving/changing the native guild does not carry living-guild state into the new guild.

## Living-guild loop

- [ ] With `LivingGuildEnabled=true`, the first Activity appears shortly after a valid guild context is ready.
- [ ] At most `MaxConcurrentActivities` are active.
- [ ] Activity participants/types/end times remain stable while viewing the panel; opening/closing the UI does not reroll them.
- [ ] Completed activities produce deterministic Activity-feed entries and update only **Guild Life** XP/completion/setback/relationship/accomplishment state.
- [ ] Requests/opportunities appear when generated, remain discoverable, expire deterministically, and stay within the configured bounded list.
- [ ] Wait long enough for several activities and confirm there is no chat-frame spam or rapid runaway generation.
- [ ] Simulate a long pause/reload and confirm Guild Life does not replay hundreds of missed activities; scheduling resumes from current time.
- [ ] No Sim is spawned, teleported, pathed, animated, made to attack/heal, or granted native items/XP/resources by the activity system.

## Retained UI

- [ ] Roster, Bulletin, and Activity tabs all render in the existing retained panel.
- [ ] Roster rows show the member's current Guild Life activity where applicable.
- [ ] Activity tab shows current activities, opportunities, accomplishments/member progress, and recent events without looking like a debug console.
- [ ] Bulletin remains a separate provenance surface; Guild Life activity events are not silently copied into it.
- [ ] Collapse/expand, drag, resize, close/reset, screen clamping, and Suite fallback launcher behavior still work.
- [ ] No duplicate Canvas/EventSystem roots appear after Lunaris disable/enable/hot reload.

## Persistence / character switching

- [ ] Character A and Character B use separate sidecar state.
- [ ] V1 Bulletin data loads and is migrated to V2 without losing readable history.
- [ ] V2 retains stable-id member Guild Life progression, relationships, opportunities, accomplishments, and bounded Activity event history.
- [ ] Active runtime activities do **not** survive save/load as stuck ownership; a fresh schedule is reconstructed.
- [ ] Character switch/unload/plugin disable emits cleanup and leaves no current activity ownership.
- [ ] Malformed individual records are skipped; a fully invalid file is backed up as `.corrupt-*` and fails safely.

## Optional integrations

With Deep Sims absent:
- [ ] Everything above still works; no missing-type/startup error occurs.

With current Deep Sims installed:
- [ ] Guild Life still loads independently; no loader-order requirement appears.
- [ ] Existing Deep Sims behavior remains unaffected until it explicitly consumes the additive Guild Life Activity API.

With Journal absent:
- [ ] Meaningful activity events remain visible in Guild Life and no error/spam occurs.

With current Journal installed:
- [ ] Only meaningful Guild Life activity events can create Chronicle entries.
- [ ] Routine start/stop/expiry noise does not flood Chronicle.
- [ ] Reprocessing the same deterministic event ID does not duplicate the Chronicle event.

## Read-only native boundary

- [ ] No invite/kick/rank/recruit/create/leave/guild-quest/raid-start operation is performed.
- [ ] No native Sim progression, inventory, money, faction, combat, movement, AI, or Erenshor save state is mutated.
- [ ] The only Harmony patch remains the proven `CameraController.UsingUI()` retained-UI containment postfix.
