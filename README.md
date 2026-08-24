# Erenshor Guild Life 0.1.3

Part of the **Forgotten Roads for Erenshor** mod collection.

Guild Life is a read-only native-guild companion with a bounded deterministic **living-guild** layer. Erenshor remains authoritative for real guild membership, Sims, progression, inventory, combat, movement, quests, raids, and saves. Guild Life adds visible mod-owned activity around that verified roster so the guild can feel active even when no LLM is installed.

## What it does

- retained-uGUI `GUILD LIFE` launcher and resizable panel; no production `OnGUI`/`GUILayout` and no global hotkey;
- read-only detection of the active character's native guild roster;
- member name plus native zone/level when those tracking facts are readable;
- stable Sim ownership from the current verified `SimPlayerTracking.simIndex` seam;
- a bounded deterministic living-guild scheduler for Training, Exploration, Patrol, Equipment Practice, Supply Survey, and Study;
- small temporary activity groups of one to three guild members, with at most a configured handful active at once;
- deterministic return results: success, minor setback, news, requests/opportunities, occasional minor disputes, and small **Guild Life** accomplishments;
- mod-owned Guild Life activity experience, completion/setback counts, and bounded pair rapport/shared-activity/dispute state;
- a retained **Activity** tab showing current activities, open opportunities, member Guild Life progression, and recent activity events;
- a separate verified **Bulletin** for native roster observations and facts other mods explicitly report through `GuildLifeApi.PostVerifiedEvent(...)`;
- bounded per-character V2 sidecar persistence with V1 import, backup, malformed-record tolerance, and corrupt-file recovery;
- optional reflection-only Journal Chronicle posting for meaningful deterministic Guild Life events;
- reflection-friendly living-guild read API for future Deep Sims or other optional consumers.

## What the guild simulation means

The living-guild layer is intentionally small. It does **not** pretend that an unseen native Erenshor expedition literally occurred. The system owns facts such as:

```text
Dancer and Fiora began Guild Life training.
Dancer and Fiora returned successfully.
A small Guild Life follow-up opportunity was created.
```

It does **not** claim:

```text
Dancer gained native Erenshor XP.
Fiora looted an item.
The group killed a named enemy.
The Sims physically travelled to another zone.
```

That distinction is deliberate. `ActivityExperience`, relationships, accomplishments, and opportunities are Guild Life state only. They never write native Sim level/XP, inventory, money, faction, movement, guild management, or saves.

## Activity lifecycle and identity

Activities are scheduled on a bounded cadence (default 75 seconds; first activity appears quickly after a valid guild context is established for live testing). The scheduler never runs a giant offline catch-up loop: after a long pause/load it advances from the current time and starts at most one newly due activity per tick.

Permanent member state is keyed by verified `SimPlayerTracking.simIndex`, not by display name. A name may only bridge a temporary missing tracking object for an identity that was previously verified. A member is excluded/interrupted when the current read positively shows that they:

- joined the player's current party;
- are loaded and dead/unavailable;
- left the native guild roster.

A missing runtime avatar by itself is treated as temporary absence, not proof of death. Guild Life never spawns, teleports, or moves a Sim to make the activity look real.

## Visible output

The retained panel has three views:

- **Roster** — native guild membership plus readable zone/level and current Guild Life activity;
- **Bulletin** — verified/native observations only;
- **Activity** — current deterministic activities, requests/opportunities, member Guild Life progression, accomplishments, and recent event history.

Meaningful activity events remain in the bounded persisted Activity history so the player can discover them later. The mod does not dump routine activity chatter into chat every frame.

## Persistence

Per-character state is stored under:

```text
plugins/config/ErenshorGuildLife/Characters/<character-key>/bulletin.dat
```

The V2 sidecar persists only appropriate long-lived mod state:

- Bulletin;
- stable-id member Guild Life progression;
- bounded relationship state;
- open opportunities;
- bounded Activity event history;
- activity/event counters and Guild Life accomplishments.

Runtime activity ownership and next-schedule timestamps are **not** persisted. They are reconstructed after a process/plugin load. Temporary zoning/readiness loss keeps the already-verified character context in memory while native reads pause; a genuine character change, unload, or plugin disable silently releases runtime-only ownership before the sidecar is saved. No Erenshor save is edited.

## Configuration

In addition to existing UI/roster settings:

- `LivingGuildEnabled` — enables the deterministic activity system (default `true`);
- `ActivityIntervalSeconds` — base activity-start cadence, clamped to 30–600 seconds (default `75`);
- `MaxConcurrentActivities` — 1–4 simultaneous Guild Life activities (default `2`).

## Public APIs

The existing verified Bulletin contract remains compatible:

```text
GuildLifeApi.ContractVersion = 1
GuildLifeApi.PostVerifiedEvent(source, category, actor, text) -> bool
```

The additive living-guild contract is:

```text
GuildLifeApi.ActivityContractVersion = 1
GuildLifeApi.LatestActivityEventSequence
GuildLifeApi.OldestActivityEventSequence
GuildLifeApi.GetCurrentActivities()
GuildLifeApi.GetActivityEventsAfter(sequence)
GuildLifeApi.GetOpenOpportunities()
GuildLifeApi.GetRelationships()
```

The returned payloads use primitive/string dictionaries so optional consumers can bind by reflection without a loader-level dependency.

## Deep Sims and Journal

Deep Sims is **not required** and no Deep Sims types are referenced at compile time. Without Deep Sims, the scheduler, Activity UI, opportunities, relationships, persistence, and event stream all work deterministically.

A future Deep Sims integration can consume the same activity events and decide how Sims react, remember, argue, or tell stories about them. Deep Sims should not decide whether the activity happened.

Journal is also optional. When the current `ErenshorJournal.JournalApi.AddChronicleEvent(...)` surface is discoverable, only meaningful Guild Life events are offered to Chronicle, using the deterministic Guild Life event ID for exactly-once behavior. Missing/unknown Journal versions are ignored safely.

## Native authority and permissions

Guild Life does not create a guild, invite/kick/recruit members, alter rank, start guild quests/raids, summon/move Sims, issue guild chat, mutate native progression, or write Erenshor saves.

It requests `FileAccess`, `Reflection`, and `Harmony`:

- `FileAccess` — mod-owned per-character sidecar;
- `Reflection` — read-only native guild/tracking state and optional integrations;
- `Harmony` — exactly one narrow retained-UI camera containment postfix on `CameraController.UsingUI()`.

The camera postfix can only change `false -> true` while Guild Life owns a real pointer gesture. `[HarmonyPrepare]` proves the installed shape first; otherwise the patch fails closed. No gameplay method is patched.

## Build / test

Native Lunaris is required for this source line.

```powershell
powershell -ExecutionPolicy Bypass -File .\RUN_TESTS.ps1
powershell -ExecutionPolicy Bypass -File .\BUILD_AND_INSTALL.ps1
```

`RUN_TESTS.ps1` exercises the Unity-free core and source contracts, including deterministic selection/transitions, grouped/unavailable handling, stable identity through temporary tracking loss, event bounds, long-gap behavior, V1/V2 persistence, read-only authority, retained UI ownership, and optional-integration boundaries.

`BUILD_AND_INSTALL.ps1` compiles against the **currently installed** Erenshor/Lunaris assemblies and installs only `ErenshorGuildLife.dll`. See `TESTING.md` for the live pass.

## Optional Forgotten Roads Hub integration

Forgotten Roads Hub remains optional. Guild Life exposes its existing versioned primitive control surface and keeps its standalone retained launcher as the recovery path. Hub controls open/close/reset/settings only; they do not expose native guild-management actions.

---

This is an unofficial, community-made mod for Erenshor and is not affiliated with or endorsed by the game's developer.
