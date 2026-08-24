$ErrorActionPreference = "Stop"
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

function Find-Csc {
    foreach ($path in @(
        "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
        "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    )) {
        if (Test-Path $path) { return $path }
    }
    throw "csc.exe not found. Install the .NET Framework Developer Pack or Visual Studio Build Tools."
}

$csc = Find-Csc
$out = Join-Path $env:TEMP "ErenshorGuildLifeCoreTests.exe"
& $csc /nologo /target:exe /out:$out `
    (Join-Path $ScriptRoot "src\GuildModels.cs") `
    (Join-Path $ScriptRoot "src\GuildLifeCore.cs") `
    (Join-Path $ScriptRoot "src\GuildActivityEngine.cs") `
    (Join-Path $ScriptRoot "src\GuildStore.cs") `
    (Join-Path $ScriptRoot "src\OptionalJournalBridge.cs") `
    (Join-Path $ScriptRoot "tests\GuildLifeCoreTests.cs")
if ($LASTEXITCODE -ne 0) { throw "Guild Life core tests did not compile." }
& $out
if ($LASTEXITCODE -ne 0) { throw "Guild Life core tests failed." }

# Unity-free retained-UI visibility/fallback, action routing, strict bool mutation parsing, gesture cleanup, and normalized-position recovery policy.
$suiteUiOut = Join-Path $env:TEMP "ErenshorGuildLife.SuiteUiPolicyTests.exe"
& $csc /nologo /target:exe /out:$suiteUiOut `
    (Join-Path $ScriptRoot "src\SuiteUiPolicies.cs") `
    (Join-Path $ScriptRoot "tests\SuiteUiPolicyTests.cs")
if ($LASTEXITCODE -ne 0) { throw "Suite UI policy tests did not compile." }
& $suiteUiOut
if ($LASTEXITCODE -ne 0) { throw "Suite UI policy tests failed." }

# Source-level authority boundary guard. Guild Life may inspect native guild state but must not
# expose or call native guild mutation operations in this read-only product.
$guildSource = (Get-ChildItem (Join-Path $ScriptRoot "src") -Filter "*.cs" | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
$forbiddenPatterns = @(
    '\.Invite\s*\(',
    '\.Kick\s*\(',
    '\.Recruit\s*\(',
    '\.CreateGuild\s*\(',
    '\.LeaveGuild\s*\(',
    '\.StartRaid\s*\(',
    '\.StartGuildQuest\s*\(',
    '\.SetValue\s*\('
)
foreach ($pattern in $forbiddenPatterns) {
    if ($guildSource -match $pattern) { throw "Read-only boundary test failed on forbidden source pattern: $pattern" }
}

# Guild Life is allowed EXACTLY ONE Harmony patch: the narrow, fail-closed retained-UI camera
# containment postfix on CameraController.UsingUI. Any other patch target would be a gameplay
# authority expansion, so the guard enumerates every [HarmonyPatch(...)] in src and rejects
# anything that is not that seam. This replaces the old blanket "no HarmonyPatch anywhere" rule.
$harmonyPatchAttributes = [regex]::Matches($guildSource, '\[HarmonyPatch\([^\]]*\)\]')
foreach ($attribute in $harmonyPatchAttributes) {
    if ($attribute.Value -notmatch '^\[HarmonyPatch\(typeof\(CameraController\),\s*"UsingUI"\)\]$') {
        throw "Read-only boundary test failed: unexpected Harmony patch target: $($attribute.Value)"
    }
}
if ($harmonyPatchAttributes.Count -gt 1) { throw "Read-only boundary test failed: more than one Harmony patch declared." }
foreach ($pattern in @('HarmonyPrefix', 'HarmonyTranspiler', 'HarmonyFinalizer', 'HarmonyReversePatch')) {
    if ($guildSource -match $pattern) { throw "Read-only boundary test failed: only a containment postfix is permitted, found $pattern." }
}
if ($guildSource -match 'LunarisPermission\.Network') { throw "Read-only boundary test failed: Guild Life requests network permission." }
Write-Host "PASS: Guild Life read-only authority source guard"

# Living-guild provenance/optional-integration contract. The deterministic activity stream is
# mod-owned and separate from the externally verified Bulletin; Deep Sims/Journal must remain optional.
if ($guildSource -match 'using\s+(DeepSims|ErenshorJournal)' -or $guildSource -match 'LunarisPermission\.Network') {
    throw "Guild Life optional-integration guard failed: hard AI/Journal/network dependency found."
}
$activityApiSource = Get-Content (Join-Path $ScriptRoot "src\GuildLifeApi.cs") -Raw
if ($activityApiSource -notmatch 'ActivityContractVersion\s*=\s*1' -or
    $activityApiSource -notmatch 'GetCurrentActivities' -or
    $activityApiSource -notmatch 'GetActivityEventsAfter' -or
    $activityApiSource -notmatch 'OldestActivityEventSequence' -or
    $activityApiSource -notmatch 'GetOpenOpportunities' -or
    $activityApiSource -notmatch 'GetRelationships') {
    throw "Guild Life living-activity API contract is incomplete."
}
$activityEngineSource = Get-Content (Join-Path $ScriptRoot "src\GuildActivityEngine.cs") -Raw
if ($activityEngineSource -match 'GameData|SimPlayer|UnityEngine|Harmony|Lunaris') {
    throw "Guild Life deterministic activity core reached into game/loader APIs."
}
$livingPluginSource = Get-Content (Join-Path $ScriptRoot "src\ErenshorGuildLifePlugin.cs") -Raw
if ($livingPluginSource -match 'AppendBulletin\([^\)]*"Guild Life"') {
    throw "Guild Life provenance guard failed: deterministic activity output was mixed into the verified Bulletin."
}
Write-Host "PASS: Guild Life living-activity provenance/optional-integration guard"

$readerSource = Get-Content (Join-Path $ScriptRoot "src\GuildReader.cs") -Raw
$pluginSource = Get-Content (Join-Path $ScriptRoot "src\ErenshorGuildLifePlugin.cs") -Raw
if ($readerSource -match 'gameObject\.name') { throw "Guild snapshot mapping guard failed: scene GameObject name fallback returned." }
$memberNameMatch = [regex]::Match($readerSource, 'private\s+static\s+string\s+MemberName\(object\s+value\)[\s\S]*?(?=private\s+static\s+bool\s+Contains)')
if (-not $memberNameMatch.Success) { throw "Guild snapshot mapping guard failed: MemberName mapper was not found." }
if ($memberNameMatch.Value -match 'Convert\.ToString\(value\)') { throw "Guild snapshot mapping guard failed: unverified member-object ToString fallback returned." }
if ($readerSource -notmatch 'Read\(string\s+verifiedPlayerName\)') { throw "Guild snapshot mapping guard failed: reader is not bound to verified character identity." }
if ($pluginSource -notmatch 'private\s+void\s+UnloadCharacter\(\)[\s\S]*?_snapshot\s*=\s*null;') { throw "Guild lifecycle guard failed: character unload no longer clears guild snapshot." }
if ($pluginSource -match 'else\s*\{\s*if\s*\(_characterKey\.Length\s*>\s*0\)\s*UnloadCharacter\(\)') { throw "Guild lifecycle guard failed: temporary gameplay-readiness loss unloads character state during zoning." }
if ($pluginSource -notmatch 'ReleaseGuildActivityOwnership') { throw "Guild lifecycle guard failed: technical runtime ownership release path is missing." }
if ($pluginSource -notmatch 'Instance\s*!=\s*null\s*&&\s*Instance\s*!=\s*this') { throw "Guild lifecycle guard failed: duplicate plugin initialization is not rejected." }
Write-Host "PASS: Guild Life snapshot/lifecycle source guard"

# Header collapse uses an owned Image-chevron; it must not depend on TMP triangle coverage.
$guildWindowSource = Get-Content (Join-Path $ScriptRoot "src\GuildWindow.cs") -Raw
$guildUiSource = Get-Content (Join-Path $ScriptRoot "src\RetainedUiKit.cs") -Raw
if ($guildWindowSource -notmatch 'AddVerticalChevron\(_collapseChevron,\s*!_collapsed\)' -or
    $guildUiSource -notmatch 'internal\s+static\s+void\s+AddVerticalChevron') {
    throw "Guild Life release polish guard failed: glyph-safe collapse chevron is missing."
}
Write-Host "Guild Life release polish collapse-icon guard: PASS" -ForegroundColor Green
$launcherVisual = Get-Content (Join-Path $ScriptRoot "src\StandaloneLauncherVisual.cs") -Raw
$launcherSource = Get-Content (Join-Path $ScriptRoot "src\GuildLauncher.cs") -Raw
if ($launcherVisual -notmatch 'Width\s*=\s*154f' -or $launcherVisual -notmatch 'Height\s*=\s*32f' -or
    $launcherVisual -notmatch 'GripWidth\s*=\s*20f' -or $launcherVisual -notmatch '"GripDot"' -or
    $launcherSource -notmatch 'StyleGrip\(grip\)' -or $launcherSource -notmatch 'StyleButton\(button, _label\)') {
    throw "Guild Life Forgotten Roads launcher visual contract failed."
}
Write-Host "Guild Life Forgotten Roads launcher visual contract: PASS" -ForegroundColor Green

# Retained-UI gesture ownership contract. Guild Life must claim native UI-drag ownership on
# pointer-down, BEFORE the EventSystem drag threshold, or the first drag delta leaks into the game
# camera. Ownership must then release on every physical/lifecycle path.
$dragClass = [regex]::Match($guildUiSource, 'internal sealed class SuiteDragHandler[\s\S]*?(?=internal sealed class SuiteResizeHandler)')
$resizeClass = [regex]::Match($guildUiSource, 'internal sealed class SuiteResizeHandler[\s\S]*?(?=internal sealed class|\z)')
if (-not $dragClass.Success -or $dragClass.Value -notmatch 'OnPointerDown[\s\S]*?_gesture\.Press\(\)[\s\S]*?ClaimOwnership\(\)') {
    throw "Guild Life input guard failed: window/launcher drag does not claim ownership on pointer-down."
}
if ($dragClass.Value -notmatch 'OnPointerUp[\s\S]*?EndDrag' -or $dragClass.Value -notmatch 'OnDisable[\s\S]*?EndDrag' -or
    $dragClass.Value -notmatch 'OnDestroy[\s\S]*?EndDrag' -or $dragClass.Value -notmatch 'private\s+void\s+EndDrag[\s\S]*?Release\(\)') {
    throw "Guild Life input guard failed: drag ownership cleanup path is incomplete."
}
if (-not $resizeClass.Success -or $resizeClass.Value -notmatch 'OnPointerDown[\s\S]*?_gesture\.Press\(\)[\s\S]*?ClaimOwnership\(\)' -or
    $resizeClass.Value -notmatch 'OnPointerUp[\s\S]*?EndResize' -or $resizeClass.Value -notmatch 'OnDisable[\s\S]*?EndResize' -or
    $resizeClass.Value -notmatch 'OnDestroy[\s\S]*?EndResize') {
    throw "Guild Life input guard failed: resize ownership claim/cleanup path is incomplete."
}
if ($guildUiSource -notmatch 'InputButton\.Left' -or $guildUiSource -notmatch 'Input\.GetMouseButton\(0\)' -or
    $guildUiSource -notmatch 'OnApplicationFocus' -or $guildUiSource -notmatch 'OnApplicationPause') {
    throw "Guild Life gesture guard failed: left-only physical/focus/pause lifecycle missing."
}
$ownershipSource = Get-Content (Join-Path $ScriptRoot "src\GuildLifeUiGestureOwnership.cs") -Raw
if ($ownershipSource -notmatch 'ProcessOwnersKey' -or $ownershipSource -notmatch 'RestoreBaseline' -or
    $ownershipSource -match 'DraggingUIElement\s*=\s*false') {
    throw "Guild Life ownership guard failed: shared native-baseline restoration is missing, so a sibling mod or native owner could be cleared."
}
Write-Host "Guild Life retained-UI gesture ownership contract: PASS" -ForegroundColor Green

# Narrow camera containment contract. The runtime IL proof still runs fail-closed at Harmony prepare
# time; these checks prevent regression to a guessed or non-monotonic camera patch.
$cameraSource = Get-Content (Join-Path $ScriptRoot "src\GuildLifeCameraUiPatch.cs") -Raw
if ($cameraSource -notmatch '\[HarmonyPatch\(typeof\(CameraController\),\s*"UsingUI"\)\]' -or
    $cameraSource -notmatch '\[HarmonyPrepare\]' -or
    $cameraSource -notmatch 'if\s*\(!__result\s*&&\s*GuildLifeUiGestureOwnership\.OwnsPointerGesture\)') {
    throw "Guild Life camera guard failed: fail-closed monotonic UsingUI postfix missing."
}
foreach ($token in @('UIWindows', 'activeSelf', 'ModernControls', 'releaseMouse', 'GetAxis', 'DraggingUIElement')) {
    if ($cameraSource -notmatch [regex]::Escape($token)) { throw "Guild Life camera guard failed: native proof token missing: $token" }
}
if ($pluginSource -notmatch '_harmony\.PatchAll\(\)' -or $pluginSource -notmatch '_harmony\.UnpatchSelf\(\)') {
    throw "Guild Life camera lifecycle guard failed: Harmony patch/unpatch lifecycle is incomplete."
}
if ($pluginSource -notmatch 'LunarisPermission\.Harmony') {
    throw "Guild Life permission guard failed: the camera containment patch requires the declared Harmony permission."
}
Write-Host "Guild Life camera containment contract: PASS" -ForegroundColor Green
