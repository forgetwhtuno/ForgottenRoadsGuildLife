using System;
using System.Collections.Generic;

namespace ErenshorGuildLife
{
    /// <summary>
    /// Optional reflection-friendly Guild Life surface. ContractVersion 1 preserves the
    /// original verified-bulletin ingress. ActivityContractVersion 1 is additive and exposes
    /// deterministic Guild Life events without introducing a Deep Sims or Journal dependency.
    /// </summary>
    public static class GuildLifeApi
    {
        public const int ContractVersion = 1;
        public const int ActivityContractVersion = 1;
        public static bool IsAvailable { get { return ErenshorGuildLifePlugin.Instance != null; } }
        public static long LatestActivityEventSequence
        {
            get
            {
                ErenshorGuildLifePlugin plugin = ErenshorGuildLifePlugin.Instance;
                GuildLifeDocument doc = plugin == null ? null : plugin.ControlDocument;
                return doc == null ? 0L : doc.ActivityEventCounter;
            }
        }

        public static long OldestActivityEventSequence
        {
            get
            {
                ErenshorGuildLifePlugin plugin = ErenshorGuildLifePlugin.Instance;
                GuildLifeDocument doc = plugin == null ? null : plugin.ControlDocument;
                return doc == null || doc.ActivityEvents.Count == 0 || doc.ActivityEvents[0] == null
                    ? 0L : doc.ActivityEvents[0].Sequence;
            }
        }

        private const int MaximumPendingEvents = 256;
        private static readonly Queue<PendingGuildEvent> Pending = new Queue<PendingGuildEvent>();

        public static bool PostVerifiedEvent(string source, string category, string actor, string text)
        {
            if (!IsAvailable) return false;
            ErenshorGuildLifePlugin plugin = ErenshorGuildLifePlugin.Instance;
            string characterKey = plugin == null ? string.Empty : plugin.ControlCharacterKey;
            if (string.IsNullOrWhiteSpace(characterKey)) return false;

            string cleanText = GuildLifeCore.Clean(text, 320);
            if (cleanText.Length == 0) return false;

            PendingGuildEvent value = new PendingGuildEvent();
            value.TimestampUtc = DateTime.UtcNow;
            value.CharacterKey = characterKey;
            value.Source = GuildLifeCore.Clean(source, 64);
            value.Category = GuildLifeCore.Clean(category, 64);
            value.Actor = GuildLifeCore.Clean(actor, 96);
            value.Text = cleanText;

            lock (Pending)
            {
                if (Pending.Count >= MaximumPendingEvents) return false;
                Pending.Enqueue(value);
            }
            return true;
        }

        public static List<Dictionary<string, string>> GetCurrentActivities()
        {
            List<Dictionary<string, string>> result = new List<Dictionary<string, string>>();
            ErenshorGuildLifePlugin plugin = ErenshorGuildLifePlugin.Instance;
            GuildLifeDocument doc = plugin == null ? null : plugin.ControlDocument;
            if (doc == null) return result;
            for (int i = 0; i < doc.CurrentActivities.Count; i++)
            {
                GuildActivityRecord activity = doc.CurrentActivities[i];
                if (activity == null) continue;
                Dictionary<string, string> row = new Dictionary<string, string>(StringComparer.Ordinal);
                row["contractVersion"] = ActivityContractVersion.ToString();
                row["source"] = "ErenshorGuildLife";
                row["activityId"] = activity.ActivityId ?? string.Empty;
                row["type"] = GuildActivityEngine.DescribeType(activity.Type);
                row["status"] = activity.Status.ToString();
                row["startedUtc"] = activity.StartedUtc.ToString("o");
                row["endsUtc"] = activity.EndsUtc.ToString("o");
                if (activity.ParticipantIds.Count > 0) row["participantIds"] = JoinIds(activity.ParticipantIds);
                if (activity.ParticipantNames.Count > 0) row["participantNames"] = string.Join(", ", activity.ParticipantNames.ToArray());
                result.Add(row);
            }
            return result;
        }

        public static List<Dictionary<string, string>> GetActivityEventsAfter(long sequence)
        {
            List<Dictionary<string, string>> result = new List<Dictionary<string, string>>();
            ErenshorGuildLifePlugin plugin = ErenshorGuildLifePlugin.Instance;
            GuildLifeDocument doc = plugin == null ? null : plugin.ControlDocument;
            if (doc == null) return result;
            for (int i = 0; i < doc.ActivityEvents.Count; i++)
            {
                GuildActivityEvent evt = doc.ActivityEvents[i];
                if (evt == null || evt.Sequence <= sequence) continue;
                Dictionary<string, string> row = new Dictionary<string, string>(StringComparer.Ordinal);
                row["contractVersion"] = ActivityContractVersion.ToString();
                row["source"] = "ErenshorGuildLife";
                row["sequence"] = evt.Sequence.ToString();
                row["eventId"] = evt.EventId ?? string.Empty;
                row["utc"] = evt.Utc.ToString("o");
                row["type"] = ToWireType(evt.Type);
                row["meaningful"] = evt.Meaningful ? "true" : "false";
                Put(row, "activityId", evt.ActivityId);
                Put(row, "guildKey", evt.GuildKey);
                Put(row, "detail", evt.Detail);
                if (evt.ParticipantIds.Count > 0) row["participantIds"] = JoinIds(evt.ParticipantIds);
                if (evt.ParticipantNames.Count > 0) row["participantNames"] = string.Join(", ", evt.ParticipantNames.ToArray());
                result.Add(row);
            }
            return result;
        }

        public static List<Dictionary<string, string>> GetRelationships()
        {
            List<Dictionary<string, string>> result = new List<Dictionary<string, string>>();
            ErenshorGuildLifePlugin plugin = ErenshorGuildLifePlugin.Instance;
            GuildLifeDocument document = plugin == null ? null : plugin.ControlDocument;
            if (document == null) return result;
            for (int i = 0; i < document.Relationships.Count; i++)
            {
                GuildRelationshipState value = document.Relationships[i];
                if (value == null) continue;
                Dictionary<string, string> row = new Dictionary<string, string>(StringComparer.Ordinal);
                row["contractVersion"] = ActivityContractVersion.ToString();
                row["source"] = "ErenshorGuildLife";
                row["firstId"] = value.FirstStableId.ToString();
                row["secondId"] = value.SecondStableId.ToString();
                row["firstName"] = GuildActivityEngine.NameForStableId(document, value.FirstStableId);
                row["secondName"] = GuildActivityEngine.NameForStableId(document, value.SecondStableId);
                row["rapport"] = value.Rapport.ToString();
                row["sharedActivities"] = value.SharedActivities.ToString();
                row["disputes"] = value.Disputes.ToString();
                result.Add(row);
            }
            return result;
        }

        public static List<Dictionary<string, string>> GetOpenOpportunities()
        {
            List<Dictionary<string, string>> result = new List<Dictionary<string, string>>();
            ErenshorGuildLifePlugin plugin = ErenshorGuildLifePlugin.Instance;
            GuildLifeDocument doc = plugin == null ? null : plugin.ControlDocument;
            if (doc == null) return result;
            for (int i = 0; i < doc.Opportunities.Count; i++)
            {
                GuildOpportunity value = doc.Opportunities[i];
                if (value == null || value.Resolved) continue;
                Dictionary<string, string> row = new Dictionary<string, string>(StringComparer.Ordinal);
                row["contractVersion"] = ActivityContractVersion.ToString();
                row["source"] = "ErenshorGuildLife";
                row["opportunityId"] = value.OpportunityId ?? string.Empty;
                row["createdUtc"] = value.CreatedUtc.ToString("o");
                row["expiresUtc"] = value.ExpiresUtc.ToString("o");
                Put(row, "title", value.Title);
                Put(row, "detail", value.Detail);
                if (value.ParticipantIds.Count > 0) row["participantIds"] = JoinIds(value.ParticipantIds);
                if (value.ParticipantNames.Count > 0) row["participantNames"] = string.Join(", ", value.ParticipantNames.ToArray());
                result.Add(row);
            }
            return result;
        }

        private static string ToWireType(GuildActivityEventType type)
        {
            switch (type)
            {
                case GuildActivityEventType.ActivityStarted: return "guild_activity_started";
                case GuildActivityEventType.ActivityCompleted: return "guild_activity_completed";
                case GuildActivityEventType.ActivityInterrupted: return "guild_activity_interrupted";
                case GuildActivityEventType.OpportunityCreated: return "guild_opportunity_created";
                case GuildActivityEventType.OpportunityExpired: return "guild_opportunity_expired";
                case GuildActivityEventType.RelationshipChanged: return "guild_relationship_changed";
                case GuildActivityEventType.Milestone: return "guild_milestone";
                default: return "guild_activity_unknown";
            }
        }

        private static void Put(Dictionary<string, string> row, string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) row[key] = value;
        }

        private static string JoinIds(List<int> ids)
        {
            string result = string.Empty;
            for (int i = 0; i < ids.Count; i++)
            {
                if (i > 0) result += ",";
                result += ids[i].ToString();
            }
            return result;
        }

        internal static bool TryDequeue(out PendingGuildEvent value)
        {
            lock (Pending)
            {
                if (Pending.Count == 0) { value = null; return false; }
                value = Pending.Dequeue();
                return true;
            }
        }

        internal static void ClearPending()
        {
            lock (Pending) Pending.Clear();
        }
    }
}
