using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ErenshorGuildLife;

internal static class GuildLifeCoreTests
{
    private static int _assertions;

    public static int Main()
    {
        try
        {
            TestRosterDiff();
            TestGuildChangeDoesNotFakeDelta();
            TestSameNameDifferentGuildIdsDoNotFakeDelta();
            TestSameGuildIdAllowsRosterDiffWithoutName();
            TestNoGuildDoesNotProduceRosterDelta();
            TestBulletinDuplicateSuppression();
            TestBulletinBound();
            TestBulletinPayloadBound();
            TestStoreRoundTripAndBackup();
            TestStoreMalformedRecordPreservesReadableEntries();
            TestStoreCorruptHeaderFallsBack();
            TestCharacterKeyWithSlot();
            TestCharacterKeyWithoutSlot();
            TestCharacterKeySanitizesUnsafeCharacters();
            TestCharacterKeyFallsBackForEmptyInput();
            TestLegacyClaimImportsOnce();
            TestLegacyClaimSkippedWhenNoLegacyFile();
            TestLegacyClaimNeverOverwritesExistingCharacterData();
            TestLauncherDragAndButtonRectsDoNotOverlap();
            TestLivingGuildStartsAndCompletesDeterministically();
            TestLivingGuildExcludesGroupedAndUnavailableMembers();
            TestLivingGuildRequiresResolvedStableIdentityForNewOwnership();
            TestLivingGuildReusesVerifiedIdentityDuringTemporaryTrackingLoss();
            TestLivingGuildInterruptsConflictingOwnership();
            TestLivingGuildDoesNotRunAwayAfterLongGap();
            TestLivingGuildEventIdsStayMonotonicAcrossGuildChange();
            TestLivingGuildTechnicalReleaseIsSilent();
            TestOptionalJournalAbsentSafe();
            TestLivingGuildStoreRoundTrip();
            Console.WriteLine("PASS Erenshor Guild Life core - " + _assertions.ToString() + " assertions");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL Erenshor Guild Life core: " + ex.Message);
            return 1;
        }
    }

    private static void TestCharacterKeyWithSlot()
    {
        string key = GuildLifeCore.ComposeCharacterKey("Aveline", 2);
        Equal("slot2_aveline", key, "slot-qualified key");
    }

    private static void TestCharacterKeyWithoutSlot()
    {
        string key = GuildLifeCore.ComposeCharacterKey("Aveline", -1);
        Equal("aveline", key, "name-only key when slot unresolved");
    }

    private static void TestCharacterKeySanitizesUnsafeCharacters()
    {
        string key = GuildLifeCore.SafeCharacterKey("We!rd Name-42");
        True(key.IndexOf('!') < 0 && key.IndexOf(' ') < 0 && key.IndexOf('-') < 0, "unsafe characters replaced");
        Equal("we_rd_name_42", key, "sanitized key");
    }

    private static void TestCharacterKeyFallsBackForEmptyInput()
    {
        Equal("player", GuildLifeCore.SafeCharacterKey(string.Empty), "empty character key fallback");
        Equal("player", GuildLifeCore.SafeCharacterKey("   "), "whitespace character key fallback");
        Equal("player", GuildLifeCore.SafeCharacterKey(null), "null character key fallback");
    }

    private static void TestLegacyClaimImportsOnce()
    {
        string root = Path.Combine(Path.GetTempPath(), "GuildLifeTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string legacy = Path.Combine(root, "bulletin.dat");
            string marker = Path.Combine(root, "bulletin.dat.claimed");
            string targetA = Path.Combine(root, "Characters", "a", "bulletin.dat");
            string targetB = Path.Combine(root, "Characters", "b", "bulletin.dat");
            File.WriteAllText(legacy, "ERENSHOR_GUILD_LIFE_V1");

            True(LegacyBulletinClaim.TryClaim(legacy, marker, targetA), "first character claims legacy data");
            True(File.Exists(targetA), "legacy data copied to first character");
            True(File.Exists(legacy), "legacy file preserved, not deleted");
            True(File.Exists(marker), "claim marker written");

            True(!LegacyBulletinClaim.TryClaim(legacy, marker, targetB), "second character cannot claim");
            True(!File.Exists(targetB), "second character starts fresh, no import");
        }
        finally { TryDelete(root); }
    }

    private static void TestLegacyClaimSkippedWhenNoLegacyFile()
    {
        string root = Path.Combine(Path.GetTempPath(), "GuildLifeTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            string legacy = Path.Combine(root, "bulletin.dat");
            string marker = Path.Combine(root, "bulletin.dat.claimed");
            string target = Path.Combine(root, "Characters", "a", "bulletin.dat");
            True(!LegacyBulletinClaim.TryClaim(legacy, marker, target), "no legacy file means no claim");
            True(!File.Exists(target), "nothing imported");
        }
        finally { TryDelete(root); }
    }

    private static void TestLegacyClaimNeverOverwritesExistingCharacterData()
    {
        string root = Path.Combine(Path.GetTempPath(), "GuildLifeTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string legacy = Path.Combine(root, "bulletin.dat");
            string marker = Path.Combine(root, "bulletin.dat.claimed");
            string target = Path.Combine(root, "Characters", "a", "bulletin.dat");
            File.WriteAllText(legacy, "ERENSHOR_GUILD_LIFE_V1");
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.WriteAllText(target, "already has its own data");

            True(!LegacyBulletinClaim.TryClaim(legacy, marker, target), "existing character data blocks claim");
            Equal("already has its own data", File.ReadAllText(target), "existing character data left untouched");
        }
        finally { TryDelete(root); }
    }

    private static void TestLauncherDragAndButtonRectsDoNotOverlap()
    {
        PureRect drag = LauncherLayout.DragRect();
        PureRect button = LauncherLayout.ButtonRect();
        True(!drag.Overlaps(button), "launcher drag rect and button rect must not overlap");
        True(drag.Width > 0f && drag.Height > 0f, "drag rect has positive area");
        True(button.Width > 0f && button.Height > 0f, "button rect has positive area");
        True(drag.X + drag.Width <= LauncherLayout.Width, "drag rect stays within launcher width");
        True(button.X + button.Width <= LauncherLayout.Width, "button rect stays within launcher width");
    }

    private static void TestRosterDiff()
    {
        GuildSnapshot a = Guild("Test", 12, "A", "B");
        GuildSnapshot b = Guild("Test", 12, "B", "C");
        GuildRosterDelta delta = GuildLifeCore.DiffRosters(a, b);
        Equal(1, delta.Joined.Count, "one joined");
        Equal("C", delta.Joined[0], "C joined");
        Equal(1, delta.Left.Count, "one left");
        Equal("A", delta.Left[0], "A left");
    }

    private static void TestGuildChangeDoesNotFakeDelta()
    {
        GuildRosterDelta delta = GuildLifeCore.DiffRosters(Guild("One", 1, "A"), Guild("Two", 2, "B"));
        Equal(0, delta.Joined.Count, "guild switch no join inference");
        Equal(0, delta.Left.Count, "guild switch no leave inference");
    }

    private static void TestSameNameDifferentGuildIdsDoNotFakeDelta()
    {
        GuildRosterDelta delta = GuildLifeCore.DiffRosters(Guild("Knights", 10, "A"), Guild("Knights", 11, "B"));
        Equal(0, delta.Joined.Count, "different authoritative guild ids block join inference");
        Equal(0, delta.Left.Count, "different authoritative guild ids block leave inference");
    }

    private static void TestSameGuildIdAllowsRosterDiffWithoutName()
    {
        GuildSnapshot a = Guild(string.Empty, 21, "A");
        GuildSnapshot b = Guild(string.Empty, 21, "A", "B");
        GuildRosterDelta delta = GuildLifeCore.DiffRosters(a, b);
        Equal(1, delta.Joined.Count, "same authoritative guild id allows roster diff");
        Equal("B", delta.Joined[0], "new member mapped under same guild id");
    }

    private static void TestNoGuildDoesNotProduceRosterDelta()
    {
        GuildSnapshot none = new GuildSnapshot();
        none.RuntimeAvailable = true;
        none.InGuild = false;
        GuildRosterDelta delta = GuildLifeCore.DiffRosters(Guild("Test", 5, "A"), none);
        Equal(0, delta.Joined.Count, "no-guild state does not infer joins");
        Equal(0, delta.Left.Count, "no-guild state does not infer departures");
    }

    private static void TestBulletinDuplicateSuppression()
    {
        GuildLifeDocument doc = new GuildLifeDocument();
        DateTime now = DateTime.UtcNow;
        True(GuildLifeCore.AppendBulletin(doc, now, "PvP", "Victory", "A", "Won."), "first append");
        True(!GuildLifeCore.AppendBulletin(doc, now.AddSeconds(2), "PvP", "Victory", "A", "Won."), "short duplicate suppressed");
        True(GuildLifeCore.AppendBulletin(doc, now.AddSeconds(20), "PvP", "Victory", "A", "Won."), "later event admitted");
    }

    private static void TestBulletinBound()
    {
        GuildLifeDocument doc = new GuildLifeDocument();
        DateTime now = DateTime.UtcNow;
        for (int i = 0; i < GuildLifeCore.MaxBulletinEntries + 15; i++)
            GuildLifeCore.AppendBulletin(doc, now.AddSeconds(i * 20), "T", "C", "A", "event " + i.ToString());
        Equal(GuildLifeCore.MaxBulletinEntries, doc.Bulletin.Count, "bulletin bounded");
        True(doc.Bulletin[0].Text.IndexOf("event 15", StringComparison.Ordinal) >= 0, "oldest trimmed");
    }

    private static void TestBulletinPayloadBound()
    {
        GuildLifeDocument doc = new GuildLifeDocument();
        True(GuildLifeCore.AppendBulletin(doc, DateTime.UtcNow, new string('s', 90), new string('c', 90), new string('a', 130), new string('x', 500) + "\0tail"), "bounded event accepted");
        GuildBulletinEntry value = doc.Bulletin[0];
        True(value.Source.Length <= 64, "source bounded");
        True(value.Category.Length <= 64, "category bounded");
        True(value.Actor.Length <= 96, "actor bounded");
        True(value.Text.Length <= 320, "text bounded");
        True(value.Text.IndexOf('\0') < 0, "NUL removed from bulletin text");
    }

    private static void TestStoreRoundTripAndBackup()
    {
        string root = Path.Combine(Path.GetTempPath(), "GuildLifeTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "bulletin.dat");
            GuildStore store = new GuildStore(path);
            GuildLifeDocument doc = new GuildLifeDocument();
            GuildLifeCore.AppendBulletin(doc, DateTime.UtcNow, "Erenshor", "Roster", "A", "A joined.");
            store.Save(doc);
            GuildLifeCore.AppendBulletin(doc, DateTime.UtcNow.AddSeconds(20), "Erenshor", "Roster", "B", "B joined.");
            store.Save(doc);

            string warning;
            GuildLifeDocument loaded = store.Load(out warning);
            Equal(string.Empty, warning, "round trip warning");
            Equal(2, loaded.Bulletin.Count, "round trip preserves bulletin");
            True(File.Exists(path + ".bak"), "second save preserves backup");
        }
        finally { TryDelete(root); }
    }

    private static void TestStoreMalformedRecordPreservesReadableEntries()
    {
        string root = Path.Combine(Path.GetTempPath(), "GuildLifeTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "bulletin.dat");
            string source = Convert.ToBase64String(Encoding.UTF8.GetBytes("Erenshor"));
            string category = Convert.ToBase64String(Encoding.UTF8.GetBytes("Roster"));
            string actor = Convert.ToBase64String(Encoding.UTF8.GetBytes("A"));
            string text = Convert.ToBase64String(Encoding.UTF8.GetBytes("Readable event."));
            File.WriteAllText(path,
                "ERENSHOR_GUILD_LIFE_V1\n" +
                "E|" + DateTime.UtcNow.Ticks.ToString() + "|" + source + "|" + category + "|" + actor + "|" + text + "\n" +
                "E|bad-ticks|%%%|%%%|%%%|%%%\n", Encoding.UTF8);

            GuildStore store = new GuildStore(path);
            string warning;
            GuildLifeDocument loaded = store.Load(out warning);
            True(!string.IsNullOrEmpty(warning), "malformed record should report warning");
            Equal(1, loaded.Bulletin.Count, "readable bulletin survives malformed neighbor");
            Equal("Readable event.", loaded.Bulletin[0].Text, "readable text preserved");
            Equal(0, Directory.GetFiles(root, "bulletin.dat.corrupt-*").Length, "single malformed record should not quarantine whole file");
        }
        finally { TryDelete(root); }
    }

    private static void TestStoreCorruptHeaderFallsBack()
    {
        string root = Path.Combine(Path.GetTempPath(), "GuildLifeTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "bulletin.dat");
            File.WriteAllText(path, "not guild data");
            GuildStore store = new GuildStore(path);
            string warning;
            GuildLifeDocument loaded = store.Load(out warning);
            True(!string.IsNullOrEmpty(warning), "invalid header reports warning");
            Equal(0, loaded.Bulletin.Count, "invalid header falls back to empty bulletin");
            Equal(1, Directory.GetFiles(root, "bulletin.dat.corrupt-*").Length, "invalid data preserved as corrupt backup");
        }
        finally { TryDelete(root); }
    }

    private static void TestLivingGuildStartsAndCompletesDeterministically()
    {
        DateTime now = new DateTime(638900000000000000L, DateTimeKind.Utc);
        GuildLifeDocument a = new GuildLifeDocument();
        GuildLifeDocument b = new GuildLifeDocument();
        GuildSnapshot snap = LivingGuild("Knights", 77, Member(10, "A"), Member(20, "B"), Member(30, "C"));
        List<GuildActivityEvent> eventsA; List<GuildActivityEvent> eventsB;
        GuildActivityEngine.Tick(a, snap, now, "char", 60, 2, out eventsA);
        GuildActivityEngine.Tick(b, snap, now, "char", 60, 2, out eventsB);
        GuildActivityEngine.Tick(a, snap, now.AddSeconds(9), "char", 60, 2, out eventsA);
        GuildActivityEngine.Tick(b, snap, now.AddSeconds(9), "char", 60, 2, out eventsB);
        Equal(1, a.CurrentActivities.Count, "living guild starts one bounded activity");
        Equal(1, b.CurrentActivities.Count, "deterministic twin starts one activity");
        Equal(a.CurrentActivities[0].Type, b.CurrentActivities[0].Type, "activity selection deterministic");
        Equal(a.CurrentActivities[0].ParticipantIds[0], b.CurrentActivities[0].ParticipantIds[0], "participant selection deterministic");
        GuildActivityEngine.Tick(a, snap, now.AddSeconds(120), "char", 60, 2, out eventsA);
        True(a.ActivityEvents.Count >= 2, "start and completion events retained");
        True(a.MemberStates[0].ActivityExperience + a.MemberStates[1].ActivityExperience + a.MemberStates[2].ActivityExperience > 0, "mod-owned member progress advances");
    }

    private static void TestLivingGuildExcludesGroupedAndUnavailableMembers()
    {
        DateTime now = new DateTime(638900100000000000L, DateTimeKind.Utc);
        GuildLifeDocument doc = new GuildLifeDocument();
        GuildMemberSnapshot grouped = Member(1, "Grouped"); grouped.GroupedWithPlayer = true;
        GuildMemberSnapshot dead = Member(2, "Dead"); dead.KnownUnavailable = true;
        GuildMemberSnapshot free = Member(3, "Free");
        GuildSnapshot snap = LivingGuild("Test", 2, grouped, dead, free);
        List<GuildActivityEvent> events;
        GuildActivityEngine.Tick(doc, snap, now, "char", 60, 2, out events);
        GuildActivityEngine.Tick(doc, snap, now.AddSeconds(9), "char", 60, 2, out events);
        Equal(1, doc.CurrentActivities.Count, "one activity starts with one eligible member");
        Equal(1, doc.CurrentActivities[0].ParticipantIds.Count, "conflicting members excluded");
        Equal(3, doc.CurrentActivities[0].ParticipantIds[0], "free member selected");
    }

    private static void TestLivingGuildRequiresResolvedStableIdentityForNewOwnership()
    {
        DateTime now = new DateTime(638900150000000000L, DateTimeKind.Utc);
        GuildLifeDocument doc = new GuildLifeDocument();
        GuildMemberSnapshot unresolved = new GuildMemberSnapshot();
        unresolved.Name = "Aveline"; unresolved.StableId = -1; unresolved.TrackingResolved = false;
        GuildSnapshot snap = LivingGuild("Test", 22, unresolved);
        List<GuildActivityEvent> events;
        GuildActivityEngine.Tick(doc, snap, now, "char", 60, 1, out events);
        GuildActivityEngine.Tick(doc, snap, now.AddSeconds(9), "char", 60, 1, out events);
        Equal(0, doc.CurrentActivities.Count, "unresolved display name cannot authorize new activity ownership");
    }

    private static void TestLivingGuildReusesVerifiedIdentityDuringTemporaryTrackingLoss()
    {
        DateTime now = new DateTime(638900200000000000L, DateTimeKind.Utc);
        GuildLifeDocument doc = new GuildLifeDocument();
        GuildSnapshot resolved = LivingGuild("Test", 3, Member(42, "Aveline"));
        List<GuildActivityEvent> events;
        GuildActivityEngine.Tick(doc, resolved, now, "char", 60, 1, out events);
        GuildActivityEngine.Tick(doc, resolved, now.AddSeconds(9), "char", 60, 1, out events);
        Equal(1, doc.CurrentActivities.Count, "resolved stable identity owns activity before tracking loss");
        GuildMemberSnapshot missing = new GuildMemberSnapshot(); missing.Name = "Aveline"; missing.StableId = -1; missing.TrackingResolved = false;
        GuildSnapshot temporary = LivingGuild("Test", 3, missing);
        GuildActivityEngine.Tick(doc, temporary, now.AddSeconds(10), "char", 60, 1, out events);
        Equal(1, doc.CurrentActivities.Count, "temporarily missing tracking does not erase existing stable ownership");
        Equal(42, doc.CurrentActivities[0].ParticipantIds[0], "previously verified stable id retained");
    }

    private static void TestLivingGuildInterruptsConflictingOwnership()
    {
        DateTime now = new DateTime(638900300000000000L, DateTimeKind.Utc);
        GuildLifeDocument doc = new GuildLifeDocument();
        GuildSnapshot snap = LivingGuild("Test", 4, Member(5, "A"));
        List<GuildActivityEvent> events;
        GuildActivityEngine.Tick(doc, snap, now, "char", 60, 1, out events);
        GuildActivityEngine.Tick(doc, snap, now.AddSeconds(9), "char", 60, 1, out events);
        Equal(1, doc.CurrentActivities.Count, "activity active before conflict");
        GuildMemberSnapshot grouped = Member(5, "A"); grouped.GroupedWithPlayer = true;
        GuildActivityEngine.Tick(doc, LivingGuild("Test", 4, grouped), now.AddSeconds(10), "char", 60, 1, out events);
        Equal(0, doc.CurrentActivities.Count, "player party membership interrupts guild ownership");
        True(events.Count > 0 && events[0].Type == GuildActivityEventType.ActivityInterrupted, "interruption event emitted");
    }

    private static void TestLivingGuildDoesNotRunAwayAfterLongGap()
    {
        DateTime now = new DateTime(638900400000000000L, DateTimeKind.Utc);
        GuildLifeDocument doc = new GuildLifeDocument();
        GuildSnapshot snap = LivingGuild("Test", 5, Member(1, "A"), Member(2, "B"), Member(3, "C"), Member(4, "D"), Member(5, "E"));
        List<GuildActivityEvent> events;
        GuildActivityEngine.Tick(doc, snap, now, "char", 30, 2, out events);
        GuildActivityEngine.Tick(doc, snap, now.AddDays(10), "char", 30, 2, out events);
        True(doc.CurrentActivities.Count <= 1, "one tick never catches up a backlog of starts");
        True(events.Count <= 1, "long gap emits bounded work on one tick");
    }

    private static void TestLivingGuildEventIdsStayMonotonicAcrossGuildChange()
    {
        DateTime now = new DateTime(638900450000000000L, DateTimeKind.Utc);
        GuildLifeDocument doc = new GuildLifeDocument();
        List<GuildActivityEvent> events;
        GuildSnapshot first = LivingGuild("First", 100, Member(1, "A"));
        GuildActivityEngine.Tick(doc, first, now, "char", 60, 1, out events);
        GuildActivityEngine.Tick(doc, first, now.AddSeconds(9), "char", 60, 1, out events);
        long firstSequence = doc.ActivityEventCounter;
        GuildSnapshot second = LivingGuild("Second", 200, Member(2, "B"));
        GuildActivityEngine.Tick(doc, second, now.AddSeconds(10), "char", 60, 1, out events);
        GuildActivityEngine.Tick(doc, second, now.AddSeconds(19), "char", 60, 1, out events);
        True(doc.ActivityEventCounter > firstSequence, "event counter stays monotonic across guild identity reset");
        True(doc.ActivityEvents.Count > 0 && doc.ActivityEvents[doc.ActivityEvents.Count - 1].EventId == "guildlife-" + doc.ActivityEventCounter.ToString(), "event id uses monotonic per-character counter");
    }

    private static void TestLivingGuildTechnicalReleaseIsSilent()
    {
        GuildLifeDocument doc = new GuildLifeDocument();
        GuildSnapshot snapshot = Guild("Knights", 77, "Player", "A");
        snapshot.PlayerName = "Player";
        snapshot.Members[1].StableId = 1;
        List<GuildActivityEvent> emitted;
        DateTime now = new DateTime(638900000000000000L, DateTimeKind.Utc);
        GuildActivityEngine.Tick(doc, snapshot, now, "player", 30, 1, out emitted);
        GuildActivityEngine.Tick(doc, snapshot, now.AddSeconds(13), "player", 30, 1, out emitted);
        True(doc.CurrentActivities.Count == 1, "technical release test has runtime ownership");
        long beforeEvents = doc.ActivityEventCounter;
        True(GuildActivityEngine.ReleaseRuntimeOwnership(doc), "technical release reports cleared ownership");
        Equal(0, doc.CurrentActivities.Count, "technical release clears current activities");
        Equal(beforeEvents, doc.ActivityEventCounter, "technical release emits no fake in-world event");
        True(doc.NextActivityUtc == default(DateTime), "technical release clears runtime schedule");
    }

    private static void TestOptionalJournalAbsentSafe()
    {
        GuildActivityEvent evt = new GuildActivityEvent();
        evt.EventId = "test-journal-absent"; evt.Detail = "Meaningful local test event."; evt.Meaningful = true;
        True(!OptionalJournalBridge.TryPost(evt), "absent optional Journal fails closed without throwing");
    }

    private static void TestLivingGuildStoreRoundTrip()
    {
        string root = Path.Combine(Path.GetTempPath(), "GuildLifeTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            string path = Path.Combine(root, "bulletin.dat");
            GuildLifeDocument doc = new GuildLifeDocument();
            doc.ActiveGuildKey = "id:9"; doc.ActivityCounter = 7; doc.ActivityEventCounter = 4; doc.GuildAccomplishments = 2;
            GuildMemberLifeState member = new GuildMemberLifeState(); member.StableId = 44; member.LastKnownName = "Fiora"; member.ActivityExperience = 12; member.CompletedActivities = 4; member.Setbacks = 1; member.LastSeenUtc = DateTime.UtcNow; doc.MemberStates.Add(member);
            GuildActivityEvent evt = new GuildActivityEvent(); evt.Sequence = 4; evt.EventId = "guildlife-4"; evt.Utc = DateTime.UtcNow; evt.Type = GuildActivityEventType.ActivityCompleted; evt.ActivityId = "a"; evt.GuildKey = "id:9"; evt.Detail = "Fiora completed training."; evt.ParticipantIds.Add(44); evt.ParticipantNames.Add("Fiora"); doc.ActivityEvents.Add(evt);
            GuildActivityRecord runtime = new GuildActivityRecord(); runtime.ActivityId = "runtime"; runtime.ParticipantIds.Add(44); doc.CurrentActivities.Add(runtime);
            GuildStore store = new GuildStore(path); store.Save(doc);
            string warning; GuildLifeDocument loaded = store.Load(out warning);
            Equal(string.Empty, warning, "living store warning");
            Equal("id:9", loaded.ActiveGuildKey, "guild identity round trips");
            Equal(1, loaded.MemberStates.Count, "member progression round trips");
            Equal(12, loaded.MemberStates[0].ActivityExperience, "activity experience round trips");
            Equal(1, loaded.ActivityEvents.Count, "activity feed round trips");
            Equal(0, loaded.CurrentActivities.Count, "ephemeral runtime activity ownership is not persisted");
        }
        finally { TryDelete(root); }
    }

    private static GuildMemberSnapshot Member(int id, string name)
    {
        GuildMemberSnapshot member = new GuildMemberSnapshot();
        member.StableId = id; member.TrackingResolved = id >= 0; member.Name = name;
        return member;
    }

    private static GuildSnapshot LivingGuild(string name, int id, params GuildMemberSnapshot[] members)
    {
        GuildSnapshot value = new GuildSnapshot();
        value.RuntimeAvailable = true; value.InGuild = true; value.GuildName = name; value.GuildId = id; value.PlayerName = "Player";
        for (int i = 0; i < members.Length; i++) value.Members.Add(members[i]);
        return value;
    }

    private static GuildSnapshot Guild(string name, int id, params string[] members)
    {
        GuildSnapshot value = new GuildSnapshot();
        value.RuntimeAvailable = true;
        value.InGuild = true;
        value.GuildName = name;
        value.GuildId = id;
        for (int i = 0; i < members.Length; i++)
        {
            GuildMemberSnapshot member = new GuildMemberSnapshot();
            member.Name = members[i];
            value.Members.Add(member);
        }
        return value;
    }

    private static void TryDelete(string root)
    {
        try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
    }

    private static void True(bool value, string label)
    {
        _assertions++;
        if (!value) throw new Exception(label);
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        _assertions++;
        if (!object.Equals(expected, actual))
            throw new Exception(label + " expected=" + expected + " actual=" + actual);
    }
}
