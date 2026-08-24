using System;
using System.Collections.Generic;

namespace ErenshorGuildLife
{
    // Bounded deterministic living-guild layer. It never mutates native Erenshor guild,
    // Sim, inventory, combat, XP, or movement state. Its progression is explicitly
    // Guild-Life-owned and exists to make guild membership visibly active between UI opens.
    internal static class GuildActivityEngine
    {
        internal const int MaxActivityEvents = 128;
        internal const int MaxOpportunities = 8;
        internal const int MaxRelationships = 96;
        internal const int MaxMemberStates = 128;

        internal static bool Tick(GuildLifeDocument document, GuildSnapshot snapshot, DateTime nowUtc,
            string characterKey, int intervalSeconds, int maxConcurrent, out List<GuildActivityEvent> emitted)
        {
            emitted = new List<GuildActivityEvent>();
            if (document == null || snapshot == null || !snapshot.RuntimeAvailable || !snapshot.InGuild) return false;

            nowUtc = NormalizeUtc(nowUtc);
            intervalSeconds = Clamp(intervalSeconds, 30, 600);
            maxConcurrent = Clamp(maxConcurrent, 1, 4);
            string guildKey = ResolveGuildKey(snapshot);
            if (guildKey.Length == 0) return false;

            bool changed = false;
            if (!string.Equals(document.ActiveGuildKey ?? string.Empty, guildKey, StringComparison.Ordinal))
            {
                ResetForGuild(document, guildKey, nowUtc);
                changed = true;
            }

            changed |= ReconcileMembers(document, snapshot, nowUtc);
            changed |= ExpireOpportunities(document, nowUtc, emitted);
            changed |= ReconcileActiveActivities(document, snapshot, nowUtc, guildKey, emitted);
            changed |= CompleteDueActivities(document, snapshot, nowUtc, guildKey, characterKey, emitted);

            if (document.NextActivityUtc == default(DateTime))
            {
                // Make the first visible action quick enough for a live test without creating
                // a rapid background simulator.
                document.NextActivityUtc = nowUtc.AddSeconds(Math.Min(12, intervalSeconds));
                changed = true;
            }

            if (nowUtc >= document.NextActivityUtc && document.CurrentActivities.Count < maxConcurrent)
            {
                GuildActivityRecord started = TryStartActivity(document, snapshot, nowUtc, guildKey, characterKey);
                // Always move the next schedule forward from NOW. This intentionally avoids
                // catch-up loops after a long pause/load.
                int jitter = PositiveHash(characterKey + "|" + guildKey + "|next|" + document.ActivityCounter.ToString()) % Math.Max(1, intervalSeconds / 3);
                document.NextActivityUtc = nowUtc.AddSeconds(intervalSeconds + jitter);
                changed = true;
                if (started != null)
                {
                    GuildActivityEvent evt = AddEvent(document, nowUtc, GuildActivityEventType.ActivityStarted,
                        started.ActivityId, guildKey, DescribeStart(started), false, started.ParticipantIds, started.ParticipantNames);
                    emitted.Add(evt);
                }
            }

            Trim(document);
            return changed;
        }

        internal static bool StopAll(GuildLifeDocument document, DateTime nowUtc, string reason, out List<GuildActivityEvent> emitted)
        {
            emitted = new List<GuildActivityEvent>();
            if (document == null || document.CurrentActivities.Count == 0) return false;
            nowUtc = NormalizeUtc(nowUtc);
            string guildKey = document.ActiveGuildKey ?? string.Empty;
            while (document.CurrentActivities.Count > 0)
            {
                GuildActivityRecord activity = document.CurrentActivities[0];
                document.CurrentActivities.RemoveAt(0);
                activity.Status = GuildActivityStatus.Interrupted;
                activity.Outcome = GuildLifeCore.Clean(reason, 180);
                GuildActivityEvent evt = AddEvent(document, nowUtc, GuildActivityEventType.ActivityInterrupted,
                    activity.ActivityId, guildKey, DescribeParticipants(activity.ParticipantNames) + " stopped " + DescribeType(activity.Type).ToLowerInvariant() +
                    ": " + (activity.Outcome.Length == 0 ? "runtime ownership ended" : activity.Outcome) + ".",
                    false, activity.ParticipantIds, activity.ParticipantNames);
                emitted.Add(evt);
            }
            document.NextActivityUtc = default(DateTime);
            Trim(document);
            return true;
        }

        internal static bool ReleaseRuntimeOwnership(GuildLifeDocument document)
        {
            if (document == null) return false;
            bool changed = document.CurrentActivities.Count > 0 || document.NextActivityUtc != default(DateTime);
            document.CurrentActivities.Clear();
            document.NextActivityUtc = default(DateTime);
            return changed;
        }

        internal static GuildMemberLifeState FindMemberState(GuildLifeDocument document, int stableId)
        {
            if (document == null || stableId < 0) return null;
            for (int i = 0; i < document.MemberStates.Count; i++)
            {
                GuildMemberLifeState state = document.MemberStates[i];
                if (state != null && state.StableId == stableId) return state;
            }
            return null;
        }

        internal static string CurrentActivityFor(GuildLifeDocument document, int stableId)
        {
            if (document == null || stableId < 0) return string.Empty;
            for (int i = 0; i < document.CurrentActivities.Count; i++)
            {
                GuildActivityRecord activity = document.CurrentActivities[i];
                if (activity == null) continue;
                for (int p = 0; p < activity.ParticipantIds.Count; p++)
                    if (activity.ParticipantIds[p] == stableId) return DescribeType(activity.Type);
            }
            return string.Empty;
        }

        internal static string NameForStableId(GuildLifeDocument document, int stableId)
        {
            GuildMemberLifeState state = FindMemberState(document, stableId);
            if (state == null || string.IsNullOrWhiteSpace(state.LastKnownName)) return "Sim #" + stableId.ToString();
            return state.LastKnownName;
        }

        private static bool ReconcileMembers(GuildLifeDocument document, GuildSnapshot snapshot, DateTime nowUtc)
        {
            bool changed = false;
            for (int i = 0; i < snapshot.Members.Count; i++)
            {
                GuildMemberSnapshot member = snapshot.Members[i];
                if (member == null || member.StableId < 0 || string.IsNullOrWhiteSpace(member.Name)) continue;
                GuildMemberLifeState state = FindMemberState(document, member.StableId);
                if (state == null)
                {
                    state = new GuildMemberLifeState();
                    state.StableId = member.StableId;
                    state.LastKnownName = GuildLifeCore.Clean(member.Name, 96);
                    state.LastSeenUtc = nowUtc;
                    document.MemberStates.Add(state);
                    changed = true;
                }
                else
                {
                    string cleanName = GuildLifeCore.Clean(member.Name, 96);
                    if (!string.Equals(state.LastKnownName ?? string.Empty, cleanName, StringComparison.Ordinal))
                    {
                        state.LastKnownName = cleanName;
                        changed = true;
                    }
                    state.LastSeenUtc = nowUtc;
                }
            }
            while (document.MemberStates.Count > MaxMemberStates)
            {
                int oldest = 0;
                for (int i = 1; i < document.MemberStates.Count; i++)
                    if (document.MemberStates[i].LastSeenUtc < document.MemberStates[oldest].LastSeenUtc) oldest = i;
                document.MemberStates.RemoveAt(oldest);
                changed = true;
            }
            return changed;
        }

        private static bool ExpireOpportunities(GuildLifeDocument document, DateTime nowUtc, List<GuildActivityEvent> emitted)
        {
            bool changed = false;
            for (int i = document.Opportunities.Count - 1; i >= 0; i--)
            {
                GuildOpportunity value = document.Opportunities[i];
                if (value == null || value.Resolved || value.ExpiresUtc > nowUtc) continue;
                document.Opportunities.RemoveAt(i);
                GuildActivityEvent evt = AddEvent(document, nowUtc, GuildActivityEventType.OpportunityExpired,
                    value.OpportunityId, document.ActiveGuildKey, "Opportunity expired: " + (value.Title ?? "guild request") + ".",
                    false, value.ParticipantIds, value.ParticipantNames);
                emitted.Add(evt);
                changed = true;
            }
            return changed;
        }

        private static bool ReconcileActiveActivities(GuildLifeDocument document, GuildSnapshot snapshot, DateTime nowUtc,
            string guildKey, List<GuildActivityEvent> emitted)
        {
            bool changed = false;
            for (int i = document.CurrentActivities.Count - 1; i >= 0; i--)
            {
                GuildActivityRecord activity = document.CurrentActivities[i];
                if (activity == null) { document.CurrentActivities.RemoveAt(i); changed = true; continue; }
                string reason = ActivityInvalidReason(activity, document, snapshot);
                if (reason.Length == 0) continue;
                document.CurrentActivities.RemoveAt(i);
                activity.Status = GuildActivityStatus.Interrupted;
                activity.Outcome = reason;
                GuildActivityEvent evt = AddEvent(document, nowUtc, GuildActivityEventType.ActivityInterrupted,
                    activity.ActivityId, guildKey, DescribeParticipants(activity.ParticipantNames) + " paused guild activity: " + reason + ".",
                    false, activity.ParticipantIds, activity.ParticipantNames);
                emitted.Add(evt);
                changed = true;
            }
            return changed;
        }

        private static bool CompleteDueActivities(GuildLifeDocument document, GuildSnapshot snapshot, DateTime nowUtc,
            string guildKey, string characterKey, List<GuildActivityEvent> emitted)
        {
            bool changed = false;
            // At most four completions can exist because starts are bounded to four concurrent.
            for (int i = document.CurrentActivities.Count - 1; i >= 0; i--)
            {
                GuildActivityRecord activity = document.CurrentActivities[i];
                if (activity == null || activity.EndsUtc > nowUtc) continue;
                document.CurrentActivities.RemoveAt(i);
                CompleteActivity(document, activity, nowUtc, guildKey, characterKey, emitted);
                changed = true;
            }
            return changed;
        }

        private static GuildActivityRecord TryStartActivity(GuildLifeDocument document, GuildSnapshot snapshot, DateTime nowUtc,
            string guildKey, string characterKey)
        {
            List<GuildMemberSnapshot> eligible = EligibleMembers(document, snapshot);
            if (eligible.Count == 0) return null;

            document.ActivityCounter++;
            string seed = characterKey + "|" + guildKey + "|activity|" + document.ActivityCounter.ToString();
            int hash = PositiveHash(seed);
            GuildActivityType type = (GuildActivityType)(hash % 6);
            int desired = 1 + ((hash / 7) % 3);
            if (type == GuildActivityType.Training || type == GuildActivityType.Study || type == GuildActivityType.EquipmentPractice)
                desired = Math.Min(desired, 2);
            desired = Math.Min(desired, eligible.Count);

            GuildActivityRecord activity = new GuildActivityRecord();
            activity.ActivityId = "guild-activity-" + document.ActivityCounter.ToString();
            activity.Type = type;
            activity.StartedUtc = nowUtc;
            activity.EndsUtc = nowUtc.AddSeconds(45 + ((hash / 13) % 46));
            activity.Status = GuildActivityStatus.Active;

            int start = hash % eligible.Count;
            for (int i = 0; i < desired; i++)
            {
                GuildMemberSnapshot member = eligible[(start + i) % eligible.Count];
                activity.ParticipantIds.Add(member.StableId);
                activity.ParticipantNames.Add(member.Name);
            }
            document.CurrentActivities.Add(activity);
            return activity;
        }

        private static List<GuildMemberSnapshot> EligibleMembers(GuildLifeDocument document, GuildSnapshot snapshot)
        {
            List<GuildMemberSnapshot> result = new List<GuildMemberSnapshot>();
            for (int i = 0; i < snapshot.Members.Count; i++)
            {
                GuildMemberSnapshot member = snapshot.Members[i];
                if (member == null || string.IsNullOrWhiteSpace(member.Name)) continue;
                if (string.Equals(member.Name, snapshot.PlayerName, StringComparison.OrdinalIgnoreCase)) continue;

                // New activity ownership requires the stable native tracking identity on this
                // read. A display name may keep an already-owned activity alive through brief
                // zone/load churn (see FindSnapshotMember), but it never authorizes NEW ownership.
                int stableId = member.StableId;
                if (stableId < 0) continue;
                if (member.GroupedWithPlayer || member.KnownUnavailable) continue;
                if (IsAlreadyActive(document, stableId)) continue;

                GuildMemberSnapshot copy = new GuildMemberSnapshot();
                copy.Name = member.Name;
                copy.Zone = member.Zone;
                copy.Level = member.Level;
                copy.StableId = stableId;
                copy.TrackingResolved = member.TrackingResolved;
                copy.GroupedWithPlayer = member.GroupedWithPlayer;
                copy.KnownUnavailable = member.KnownUnavailable;
                result.Add(copy);
            }
            result.Sort(delegate(GuildMemberSnapshot a, GuildMemberSnapshot b)
            {
                int cmp = a.StableId.CompareTo(b.StableId);
                return cmp != 0 ? cmp : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        private static void CompleteActivity(GuildLifeDocument document, GuildActivityRecord activity, DateTime nowUtc,
            string guildKey, string characterKey, List<GuildActivityEvent> emitted)
        {
            activity.Status = GuildActivityStatus.Completed;
            int hash = PositiveHash(characterKey + "|" + guildKey + "|outcome|" + activity.ActivityId);
            int roll = hash % 100;
            bool success = roll < 72;
            bool dispute = roll >= 90 && activity.ParticipantIds.Count >= 2;
            bool news = roll >= 72 && roll < 82;
            bool request = roll >= 82 && roll < 90;

            int xp = success ? 3 : (dispute ? 1 : 2);
            for (int i = 0; i < activity.ParticipantIds.Count; i++)
            {
                GuildMemberLifeState state = FindMemberState(document, activity.ParticipantIds[i]);
                if (state == null) continue;
                state.ActivityExperience += xp;
                state.CompletedActivities++;
                if (!success) state.Setbacks++;
            }

            if (activity.ParticipantIds.Count >= 2)
                UpdateRelationships(document, activity, dispute ? -1 : (success ? 1 : 0), dispute);

            bool meaningful = false;
            string outcome;
            if (dispute)
            {
                outcome = DescribeParticipants(activity.ParticipantNames) + " returned from " + DescribeType(activity.Type).ToLowerInvariant() +
                    " after a minor disagreement. The activity still produced some Guild Life experience.";
                meaningful = true;
            }
            else if (request)
            {
                outcome = DescribeParticipants(activity.ParticipantNames) + " returned from " + DescribeType(activity.Type).ToLowerInvariant() +
                    " with a request the guild may want to follow up on.";
                CreateOpportunity(document, activity, nowUtc, guildKey, "Follow up on " + DescribeType(activity.Type).ToLowerInvariant(),
                    "A guild member returned with a small request/opportunity related to the activity.", emitted);
                meaningful = true;
            }
            else if (news)
            {
                outcome = DescribeParticipants(activity.ParticipantNames) + " returned from " + DescribeType(activity.Type).ToLowerInvariant() +
                    " with news to share.";
                meaningful = true;
            }
            else if (success)
            {
                outcome = DescribeParticipants(activity.ParticipantNames) + " completed " + DescribeType(activity.Type).ToLowerInvariant() + " successfully.";
                if ((hash % 7) == 0)
                {
                    document.GuildAccomplishments++;
                    outcome += " The guild recorded a small accomplishment.";
                    meaningful = true;
                }
            }
            else
            {
                outcome = DescribeParticipants(activity.ParticipantNames) + " returned from " + DescribeType(activity.Type).ToLowerInvariant() +
                    " after a minor setback.";
            }
            activity.Outcome = outcome;

            GuildActivityEvent evt = AddEvent(document, nowUtc, GuildActivityEventType.ActivityCompleted,
                activity.ActivityId, guildKey, outcome, meaningful, activity.ParticipantIds, activity.ParticipantNames);
            emitted.Add(evt);
        }

        private static void CreateOpportunity(GuildLifeDocument document, GuildActivityRecord activity, DateTime nowUtc,
            string guildKey, string title, string detail, List<GuildActivityEvent> emitted)
        {
            GuildOpportunity opportunity = new GuildOpportunity();
            opportunity.OpportunityId = "guild-opportunity-" + document.ActivityCounter.ToString();
            opportunity.CreatedUtc = nowUtc;
            opportunity.ExpiresUtc = nowUtc.AddMinutes(12);
            opportunity.Title = GuildLifeCore.Clean(title, 96);
            opportunity.Detail = GuildLifeCore.Clean(detail, 220);
            Copy(activity.ParticipantIds, opportunity.ParticipantIds);
            Copy(activity.ParticipantNames, opportunity.ParticipantNames);
            document.Opportunities.Add(opportunity);
            while (document.Opportunities.Count > MaxOpportunities) document.Opportunities.RemoveAt(0);

            GuildActivityEvent evt = AddEvent(document, nowUtc, GuildActivityEventType.OpportunityCreated,
                opportunity.OpportunityId, guildKey, opportunity.Title + ": " + opportunity.Detail, true,
                opportunity.ParticipantIds, opportunity.ParticipantNames);
            emitted.Add(evt);
        }

        private static void UpdateRelationships(GuildLifeDocument document, GuildActivityRecord activity, int delta, bool dispute)
        {
            for (int a = 0; a < activity.ParticipantIds.Count; a++)
            {
                for (int b = a + 1; b < activity.ParticipantIds.Count; b++)
                {
                    int first = Math.Min(activity.ParticipantIds[a], activity.ParticipantIds[b]);
                    int second = Math.Max(activity.ParticipantIds[a], activity.ParticipantIds[b]);
                    GuildRelationshipState state = FindRelationship(document, first, second);
                    if (state == null)
                    {
                        state = new GuildRelationshipState();
                        state.FirstStableId = first;
                        state.SecondStableId = second;
                        document.Relationships.Add(state);
                    }
                    state.SharedActivities++;
                    state.Rapport = Clamp(state.Rapport + delta, -5, 5);
                    if (dispute) state.Disputes++;
                }
            }
            while (document.Relationships.Count > MaxRelationships) document.Relationships.RemoveAt(0);
        }

        private static GuildRelationshipState FindRelationship(GuildLifeDocument document, int first, int second)
        {
            for (int i = 0; i < document.Relationships.Count; i++)
            {
                GuildRelationshipState value = document.Relationships[i];
                if (value != null && value.FirstStableId == first && value.SecondStableId == second) return value;
            }
            return null;
        }

        private static string ActivityInvalidReason(GuildActivityRecord activity, GuildLifeDocument document, GuildSnapshot snapshot)
        {
            for (int i = 0; i < activity.ParticipantIds.Count; i++)
            {
                int stableId = activity.ParticipantIds[i];
                GuildMemberSnapshot member = FindSnapshotMember(snapshot, stableId, NameAt(activity, i));
                if (member == null) return "a participant is no longer on the guild roster";
                if (member.GroupedWithPlayer) return (member.Name ?? "a participant") + " joined the player's party";
                if (member.KnownUnavailable) return (member.Name ?? "a participant") + " is currently unavailable";
            }
            return string.Empty;
        }

        private static GuildMemberSnapshot FindSnapshotMember(GuildSnapshot snapshot, int stableId, string fallbackName)
        {
            for (int i = 0; i < snapshot.Members.Count; i++)
            {
                GuildMemberSnapshot member = snapshot.Members[i];
                if (member == null) continue;
                if (member.StableId >= 0 && member.StableId == stableId) return member;
            }
            // During zone/load churn the tracking can temporarily be missing; roster membership by
            // name is allowed only to keep an already stable-id-owned activity alive, never to create
            // permanent identity.
            for (int i = 0; i < snapshot.Members.Count; i++)
            {
                GuildMemberSnapshot member = snapshot.Members[i];
                if (member != null && !string.IsNullOrEmpty(fallbackName) &&
                    string.Equals(member.Name, fallbackName, StringComparison.OrdinalIgnoreCase)) return member;
            }
            return null;
        }

        private static bool IsAlreadyActive(GuildLifeDocument document, int stableId)
        {
            for (int i = 0; i < document.CurrentActivities.Count; i++)
            {
                GuildActivityRecord activity = document.CurrentActivities[i];
                if (activity == null) continue;
                for (int p = 0; p < activity.ParticipantIds.Count; p++)
                    if (activity.ParticipantIds[p] == stableId) return true;
            }
            return false;
        }

        private static void ResetForGuild(GuildLifeDocument document, string guildKey, DateTime nowUtc)
        {
            document.ActiveGuildKey = guildKey;
            document.CurrentActivities.Clear();
            document.MemberStates.Clear();
            document.Relationships.Clear();
            document.Opportunities.Clear();
            document.ActivityEvents.Clear();
            document.ActivityCounter = 0;
            // Keep ActivityEventCounter monotonic for this character so persisted event IDs remain
            // unique even when the native guild changes and the per-guild activity history resets.
            document.GuildAccomplishments = 0;
            document.NextActivityUtc = nowUtc.AddSeconds(8);
        }

        private static GuildActivityEvent AddEvent(GuildLifeDocument document, DateTime utc, GuildActivityEventType type,
            string activityId, string guildKey, string detail, bool meaningful, List<int> ids, List<string> names)
        {
            document.ActivityEventCounter++;
            GuildActivityEvent evt = new GuildActivityEvent();
            evt.Sequence = document.ActivityEventCounter;
            evt.EventId = "guildlife-" + evt.Sequence.ToString();
            evt.Utc = NormalizeUtc(utc);
            evt.Type = type;
            evt.ActivityId = GuildLifeCore.Clean(activityId, 80);
            evt.GuildKey = GuildLifeCore.Clean(guildKey, 96);
            evt.Detail = GuildLifeCore.Clean(detail, 320);
            evt.Meaningful = meaningful;
            Copy(ids, evt.ParticipantIds);
            Copy(names, evt.ParticipantNames);
            document.ActivityEvents.Add(evt);
            while (document.ActivityEvents.Count > MaxActivityEvents) document.ActivityEvents.RemoveAt(0);
            return evt;
        }

        private static void Trim(GuildLifeDocument document)
        {
            while (document.ActivityEvents.Count > MaxActivityEvents) document.ActivityEvents.RemoveAt(0);
            while (document.Opportunities.Count > MaxOpportunities) document.Opportunities.RemoveAt(0);
            while (document.Relationships.Count > MaxRelationships) document.Relationships.RemoveAt(0);
            while (document.MemberStates.Count > MaxMemberStates) document.MemberStates.RemoveAt(0);
        }

        internal static string DescribeType(GuildActivityType type)
        {
            switch (type)
            {
                case GuildActivityType.Training: return "Training";
                case GuildActivityType.Exploration: return "Exploration";
                case GuildActivityType.Patrol: return "Patrol";
                case GuildActivityType.EquipmentPractice: return "Equipment practice";
                case GuildActivityType.SupplySurvey: return "Supply survey";
                case GuildActivityType.Study: return "Study";
                default: return "Guild activity";
            }
        }

        private static string DescribeStart(GuildActivityRecord activity)
        {
            return DescribeParticipants(activity.ParticipantNames) + " began " + DescribeType(activity.Type).ToLowerInvariant() + ".";
        }

        internal static string DescribeParticipants(List<string> names)
        {
            if (names == null || names.Count == 0) return "Guild members";
            if (names.Count == 1) return names[0];
            if (names.Count == 2) return names[0] + " and " + names[1];
            return names[0] + ", " + names[1] + ", and " + names[2];
        }

        private static string NameAt(GuildActivityRecord activity, int index)
        {
            return activity != null && index >= 0 && index < activity.ParticipantNames.Count ? activity.ParticipantNames[index] : string.Empty;
        }

        private static string ResolveGuildKey(GuildSnapshot snapshot)
        {
            if (snapshot.GuildId > 0) return "id:" + snapshot.GuildId.ToString();
            string name = GuildLifeCore.Clean(snapshot.GuildName, 80).ToLowerInvariant();
            return name.Length == 0 ? string.Empty : "name:" + name;
        }

        private static int PositiveHash(string text)
        {
            unchecked
            {
                int hash = 17;
                string value = text ?? string.Empty;
                for (int i = 0; i < value.Length; i++) hash = (hash * 31) + value[i];
                if (hash == int.MinValue) return int.MaxValue;
                return Math.Abs(hash);
            }
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            return value > max ? max : value;
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            if (value == default(DateTime)) return DateTime.UtcNow;
            if (value.Kind == DateTimeKind.Utc) return value;
            try { return value.ToUniversalTime(); }
            catch { return DateTime.UtcNow; }
        }

        private static void Copy(List<int> source, List<int> target)
        {
            if (source == null || target == null) return;
            for (int i = 0; i < source.Count; i++) target.Add(source[i]);
        }

        private static void Copy(List<string> source, List<string> target)
        {
            if (source == null || target == null) return;
            for (int i = 0; i < source.Count; i++) target.Add(GuildLifeCore.Clean(source[i], 96));
        }
    }
}
