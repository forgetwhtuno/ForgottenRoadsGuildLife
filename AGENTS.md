# AGENTS.md — Erenshor Guild Life

Instructions for AI/coding agents working in this repository. Read this before making changes.

## What this mod is

A read-only native guild-presence layer plus a bounded, deterministic **mod-owned living-guild simulation** for Erenshor (native Lunaris plugin, .NET Framework 4.8, C# 5 effective language level via `csc`). It displays the player's native guild roster, keeps a verified-event Bulletin, and separately records deterministic Guild Life activities/events. The living-guild layer never grants or mutates native XP, items, resources, movement, guild rank, quests, raids, or other Erenshor state.

## Core design boundary

- Guild Life is **read-only**. It does not create a guild, invite/kick members, alter rank, recruit Sims, start guild quests or raids, move/summon Sims, or send guild chat.
- It does not mutate native guild/gameplay state or write any Erenshor save. Player-facing UI is retained Unity uGUI and uses no Harmony click/camera containment; UI behavior must never become a guild-management or gameplay hook.
- Native guild objects are resolved through **reflection**, and the code must fail closed (not throw, not fabricate data) if a game update changes the expected shape. See `src/GuildReader.cs`.
- The **Bulletin** only records native roster observations or events a caller already verified via `GuildLifeApi.PostVerifiedEvent(...)`; it does not accept generated prose as provenance. The separate **Activity feed** may originate deterministic Guild Life simulation events because Guild Life itself owns that bounded mod state. These events must be labeled/treated as Guild Life activities, never as proof that Erenshor granted XP, loot, travel, combat wins, or other native outcomes.

## What Erenshor remains authoritative for

Guild membership, rank, invites, recruitment, native progression/economy, movement, combat, quests, raids, and all guild-management actions. Guild Life reads those boundaries and may advance only its own explicitly labeled deterministic activity context.

## Forbidden

- Do not add any guild-management write path (invite, kick, rank change, recruit, raid start) — that is the mod's entire reason for existing as "read-only."
- Do not add Harmony/game hooks merely for retained-UI input handling, and never add code that writes to an Erenshor save or native guild-management state.
- Do not invent the shape of native guild types; if reflection can't find the expected member, degrade gracefully and report a diagnostic string instead of guessing.
- Do not commit `bin/`, `obj/`, `refs/`, compiled DLLs, game assemblies, or anything under a live Erenshor/Lunaris install path. `.gitignore` already covers the standard cases.
- No secrets, personal file paths, tokens, or real names in source, docs, or commit messages.
- Do not commit or push changes unrelated to the task at hand.

## Important source files

- `src/GuildReader.cs` — reflection-based native guild roster access; the fail-closed boundary lives here.
- `src/GuildLifeCore.cs` — join/leave detection, bulletin bounding logic.
- `src/GuildStore.cs` — local sidecar persistence (`plugins/config/ErenshorGuildLife/`).
- `src/GuildLifeApi.cs` — the public `PostVerifiedEvent(...)` surface for other mods.
- `src/GuildWindow.cs`, `src/GuildLauncher.cs`, `src/RetainedUiKit.cs` — retained uGUI with read-only roster/bulletin presentation and unload-safe drag/resize behavior.
- `src/GuildModels.cs` — data shapes.
- `src/ErenshorGuildLifePlugin.cs` — native Lunaris plugin entry point, native-guild observation scheduling, and retained-UI lifecycle.
- `src/GuildLifeControlApi.cs`, `src/SuiteUiPolicy.cs` — optional Hub-facing primitive control surface and standalone UI readiness/fallback policy.
- `src/GuildLifeSuiteAuraProvider.cs` — thin Lunaris Aura transport adapter over `GuildLifeControlApi` (`describe`/settings/action endpoints; no guild-management logic).

## Build / test procedure

- Deterministic core tests: `powershell -ExecutionPolicy Bypass -File .\RUN_TESTS.ps1` (standalone `csc` compile + run, no game/Lunaris dependency).
- Full plugin build: `powershell -ExecutionPolicy Bypass -File .\BUILD_AND_INSTALL.ps1` — locates the current Erenshor/Lunaris install and **installs over the live plugin DLL**. Don't use it as a plain compile check.
- The shipped build compiles with the legacy .NET Framework `csc.exe` (effectively C# 5) despite the `.csproj` claiming `LangVersion 7.3`. Avoid string interpolation, `nameof`, null-conditional operators, expression-bodied members, and inline `out` variables.
- Compile and run the deterministic tests before claiming a change works.

## Compatibility boundaries

- Does not alter guild nameplates and does not add guild-management commands (that overlaps existing public mods — see `MOD_OVERLAP_NOTES.md`).
- Deep Sims may optionally react to a verified bulletin event but must not become an authority on guild history through this mod.
- Other deterministic mods (Crafting, PvP) may post verified guild-related facts via the API; Guild Life never originates those facts itself.
