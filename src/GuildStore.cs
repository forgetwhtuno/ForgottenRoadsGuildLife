using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ErenshorGuildLife
{
    internal sealed class GuildStore
    {
        private const string HeaderV1 = "ERENSHOR_GUILD_LIFE_V1";
        private const string HeaderV2 = "ERENSHOR_GUILD_LIFE_V2";
        private const long MaximumFileBytes = 4L * 1024L * 1024L;
        private readonly string _path;

        internal GuildStore(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A Guild Life data path is required.", "path");
            _path = path;
        }

        internal string PathOnDisk { get { return _path; } }

        internal GuildLifeDocument Load(out string warning)
        {
            warning = string.Empty;
            GuildLifeDocument document = new GuildLifeDocument();
            if (!File.Exists(_path)) return document;

            try
            {
                FileInfo info = new FileInfo(_path);
                if (info.Length > MaximumFileBytes) throw new InvalidDataException("Guild Life data file is unexpectedly large.");

                string[] lines = File.ReadAllLines(_path, Encoding.UTF8);
                if (lines.Length == 0 ||
                    (!string.Equals(lines[0], HeaderV1, StringComparison.Ordinal) && !string.Equals(lines[0], HeaderV2, StringComparison.Ordinal)))
                    throw new InvalidDataException("Unknown Guild Life data format.");

                bool v2 = string.Equals(lines[0], HeaderV2, StringComparison.Ordinal);
                int skippedRecords = 0;
                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    string[] parts = lines[i].Split('|');
                    if (parts.Length == 0) continue;
                    bool parsed = false;
                    if (string.Equals(parts[0], "E", StringComparison.Ordinal)) parsed = TryLoadBulletin(document, parts);
                    else if (v2 && string.Equals(parts[0], "V", StringComparison.Ordinal)) parsed = TryLoadVersionState(document, parts);
                    else if (v2 && string.Equals(parts[0], "M", StringComparison.Ordinal)) parsed = TryLoadMember(document, parts);
                    else if (v2 && string.Equals(parts[0], "R", StringComparison.Ordinal)) parsed = TryLoadRelationship(document, parts);
                    else if (v2 && string.Equals(parts[0], "O", StringComparison.Ordinal)) parsed = TryLoadOpportunity(document, parts);
                    else if (v2 && string.Equals(parts[0], "L", StringComparison.Ordinal)) parsed = TryLoadActivityEvent(document, parts);
                    if (!parsed) skippedRecords++;
                }

                // Runtime ownership and scheduling are reconstructed, never persisted.
                document.CurrentActivities.Clear();
                document.NextActivityUtc = default(DateTime);
                TrimLoaded(document);
                if (skippedRecords > 0)
                    warning = "Some malformed local Guild Life records were ignored; readable entries were preserved.";
                return document;
            }
            catch (Exception ex)
            {
                warning = "The local Guild Life data could not be read and was preserved as a .corrupt backup (" + ex.GetType().Name + ").";
                TryBackupUnreadable();
                return new GuildLifeDocument();
            }
        }

        internal void Save(GuildLifeDocument document)
        {
            if (document == null) throw new ArgumentNullException("document");
            string directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            string temp = _path + ".tmp";
            string backup = _path + ".bak";
            using (StreamWriter writer = new StreamWriter(temp, false, new UTF8Encoding(false)))
            {
                writer.WriteLine(HeaderV2);
                WriteVersionState(writer, document);
                WriteBulletin(writer, document);
                WriteMembers(writer, document);
                WriteRelationships(writer, document);
                WriteOpportunities(writer, document);
                WriteActivityEvents(writer, document);
            }

            if (!File.Exists(_path))
            {
                File.Move(temp, _path);
                return;
            }

            try { File.Replace(temp, _path, backup, true); }
            catch
            {
                File.Copy(_path, backup, true);
                File.Copy(temp, _path, true);
                File.Delete(temp);
            }
        }

        private static bool TryLoadBulletin(GuildLifeDocument document, string[] parts)
        {
            if (parts.Length < 6) return false;
            long ticks;
            string source, category, actor, text;
            if (!TryTicks(parts[1], out ticks) || !TryDecode(parts[2], out source) || !TryDecode(parts[3], out category) ||
                !TryDecode(parts[4], out actor) || !TryDecode(parts[5], out text)) return false;
            GuildLifeCore.AppendBulletin(document, new DateTime(ticks, DateTimeKind.Utc), source, category, actor, text);
            return true;
        }

        private static bool TryLoadVersionState(GuildLifeDocument document, string[] parts)
        {
            if (parts.Length < 6) return false;
            string guildKey;
            long activities, events;
            int accomplishments;
            if (!TryDecode(parts[1], out guildKey) || !long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out activities) || activities < 0 ||
                !long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out events) || events < 0 ||
                !int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out accomplishments) || accomplishments < 0) return false;
            // parts[5] is reserved for future migration flags and is intentionally ignored.
            document.ActiveGuildKey = GuildLifeCore.Clean(guildKey, 96);
            document.ActivityCounter = activities;
            document.ActivityEventCounter = events;
            document.GuildAccomplishments = accomplishments;
            return true;
        }

        private static bool TryLoadMember(GuildLifeDocument document, string[] parts)
        {
            if (parts.Length < 7) return false;
            int id, xp, completed, setbacks;
            long lastSeen;
            string name;
            if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out id) || id < 0 || !TryDecode(parts[2], out name) ||
                !int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out xp) || xp < 0 ||
                !int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out completed) || completed < 0 ||
                !int.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out setbacks) || setbacks < 0 ||
                !TryTicksOrZero(parts[6], out lastSeen)) return false;
            GuildMemberLifeState state = new GuildMemberLifeState();
            state.StableId = id;
            state.LastKnownName = GuildLifeCore.Clean(name, 96);
            state.ActivityExperience = xp;
            state.CompletedActivities = completed;
            state.Setbacks = setbacks;
            state.LastSeenUtc = lastSeen > 0 ? new DateTime(lastSeen, DateTimeKind.Utc) : default(DateTime);
            document.MemberStates.Add(state);
            return true;
        }

        private static bool TryLoadRelationship(GuildLifeDocument document, string[] parts)
        {
            if (parts.Length < 6) return false;
            int first, second, rapport, shared, disputes;
            if (!int.TryParse(parts[1], out first) || first < 0 || !int.TryParse(parts[2], out second) || second < 0 ||
                !int.TryParse(parts[3], out rapport) || !int.TryParse(parts[4], out shared) || shared < 0 ||
                !int.TryParse(parts[5], out disputes) || disputes < 0) return false;
            GuildRelationshipState value = new GuildRelationshipState();
            value.FirstStableId = Math.Min(first, second);
            value.SecondStableId = Math.Max(first, second);
            value.Rapport = Math.Max(-5, Math.Min(5, rapport));
            value.SharedActivities = shared;
            value.Disputes = disputes;
            document.Relationships.Add(value);
            return true;
        }

        private static bool TryLoadOpportunity(GuildLifeDocument document, string[] parts)
        {
            if (parts.Length < 9) return false;
            string id, title, detail, names;
            long created, expires;
            bool resolved;
            if (!TryDecode(parts[1], out id) || !TryTicks(parts[2], out created) || !TryTicks(parts[3], out expires) ||
                !TryDecode(parts[4], out title) || !TryDecode(parts[5], out detail) || !bool.TryParse(parts[6], out resolved) ||
                !TryDecode(parts[8], out names)) return false;
            GuildOpportunity value = new GuildOpportunity();
            value.OpportunityId = GuildLifeCore.Clean(id, 80);
            value.CreatedUtc = new DateTime(created, DateTimeKind.Utc);
            value.ExpiresUtc = new DateTime(expires, DateTimeKind.Utc);
            value.Title = GuildLifeCore.Clean(title, 96);
            value.Detail = GuildLifeCore.Clean(detail, 220);
            value.Resolved = resolved;
            ParseIds(parts[7], value.ParticipantIds);
            ParseNames(names, value.ParticipantNames);
            document.Opportunities.Add(value);
            return true;
        }

        private static bool TryLoadActivityEvent(GuildLifeDocument document, string[] parts)
        {
            if (parts.Length < 11) return false;
            long sequence, ticks;
            int type;
            bool meaningful;
            string eventId, activityId, guildKey, detail, names;
            if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out sequence) || sequence <= 0 ||
                !TryDecode(parts[2], out eventId) || !TryTicks(parts[3], out ticks) || !int.TryParse(parts[4], out type) ||
                type < 0 || type > (int)GuildActivityEventType.Milestone || !TryDecode(parts[5], out activityId) ||
                !TryDecode(parts[6], out guildKey) || !TryDecode(parts[7], out detail) || !bool.TryParse(parts[8], out meaningful) ||
                !TryDecode(parts[10], out names)) return false;
            GuildActivityEvent value = new GuildActivityEvent();
            value.Sequence = sequence;
            value.EventId = GuildLifeCore.Clean(eventId, 80);
            value.Utc = new DateTime(ticks, DateTimeKind.Utc);
            value.Type = (GuildActivityEventType)type;
            value.ActivityId = GuildLifeCore.Clean(activityId, 80);
            value.GuildKey = GuildLifeCore.Clean(guildKey, 96);
            value.Detail = GuildLifeCore.Clean(detail, 320);
            value.Meaningful = meaningful;
            ParseIds(parts[9], value.ParticipantIds);
            ParseNames(names, value.ParticipantNames);
            document.ActivityEvents.Add(value);
            if (sequence > document.ActivityEventCounter) document.ActivityEventCounter = sequence;
            return true;
        }

        private static void WriteVersionState(StreamWriter writer, GuildLifeDocument document)
        {
            writer.WriteLine(string.Join("|", new string[]
            {
                "V", Encode(GuildLifeCore.Clean(document.ActiveGuildKey, 96)),
                Math.Max(0L, document.ActivityCounter).ToString(CultureInfo.InvariantCulture),
                Math.Max(0L, document.ActivityEventCounter).ToString(CultureInfo.InvariantCulture),
                Math.Max(0, document.GuildAccomplishments).ToString(CultureInfo.InvariantCulture), "0"
            }));
        }

        private static void WriteBulletin(StreamWriter writer, GuildLifeDocument document)
        {
            int start = Math.Max(0, document.Bulletin.Count - GuildLifeCore.MaxBulletinEntries);
            for (int i = start; i < document.Bulletin.Count; i++)
            {
                GuildBulletinEntry value = document.Bulletin[i];
                if (value == null || string.IsNullOrWhiteSpace(value.Text)) continue;
                writer.WriteLine(string.Join("|", new string[]
                {
                    "E", NormalizeUtc(value.TimestampUtc).Ticks.ToString(CultureInfo.InvariantCulture),
                    Encode(GuildLifeCore.Clean(value.Source, 64)), Encode(GuildLifeCore.Clean(value.Category, 64)),
                    Encode(GuildLifeCore.Clean(value.Actor, 96)), Encode(GuildLifeCore.Clean(value.Text, 320))
                }));
            }
        }

        private static void WriteMembers(StreamWriter writer, GuildLifeDocument document)
        {
            int count = Math.Min(document.MemberStates.Count, GuildActivityEngine.MaxMemberStates);
            for (int i = Math.Max(0, document.MemberStates.Count - count); i < document.MemberStates.Count; i++)
            {
                GuildMemberLifeState value = document.MemberStates[i];
                if (value == null || value.StableId < 0) continue;
                writer.WriteLine(string.Join("|", new string[]
                {
                    "M", value.StableId.ToString(CultureInfo.InvariantCulture), Encode(GuildLifeCore.Clean(value.LastKnownName, 96)),
                    Math.Max(0, value.ActivityExperience).ToString(CultureInfo.InvariantCulture),
                    Math.Max(0, value.CompletedActivities).ToString(CultureInfo.InvariantCulture),
                    Math.Max(0, value.Setbacks).ToString(CultureInfo.InvariantCulture),
                    (value.LastSeenUtc == default(DateTime) ? 0L : NormalizeUtc(value.LastSeenUtc).Ticks).ToString(CultureInfo.InvariantCulture)
                }));
            }
        }

        private static void WriteRelationships(StreamWriter writer, GuildLifeDocument document)
        {
            int start = Math.Max(0, document.Relationships.Count - GuildActivityEngine.MaxRelationships);
            for (int i = start; i < document.Relationships.Count; i++)
            {
                GuildRelationshipState value = document.Relationships[i];
                if (value == null || value.FirstStableId < 0 || value.SecondStableId < 0) continue;
                writer.WriteLine(string.Join("|", new string[]
                {
                    "R", value.FirstStableId.ToString(CultureInfo.InvariantCulture), value.SecondStableId.ToString(CultureInfo.InvariantCulture),
                    Math.Max(-5, Math.Min(5, value.Rapport)).ToString(CultureInfo.InvariantCulture),
                    Math.Max(0, value.SharedActivities).ToString(CultureInfo.InvariantCulture), Math.Max(0, value.Disputes).ToString(CultureInfo.InvariantCulture)
                }));
            }
        }

        private static void WriteOpportunities(StreamWriter writer, GuildLifeDocument document)
        {
            int start = Math.Max(0, document.Opportunities.Count - GuildActivityEngine.MaxOpportunities);
            for (int i = start; i < document.Opportunities.Count; i++)
            {
                GuildOpportunity value = document.Opportunities[i];
                if (value == null || string.IsNullOrWhiteSpace(value.OpportunityId)) continue;
                writer.WriteLine(string.Join("|", new string[]
                {
                    "O", Encode(value.OpportunityId), NormalizeUtc(value.CreatedUtc).Ticks.ToString(CultureInfo.InvariantCulture),
                    NormalizeUtc(value.ExpiresUtc).Ticks.ToString(CultureInfo.InvariantCulture), Encode(GuildLifeCore.Clean(value.Title, 96)),
                    Encode(GuildLifeCore.Clean(value.Detail, 220)), value.Resolved ? "True" : "False",
                    JoinIds(value.ParticipantIds), Encode(JoinNames(value.ParticipantNames))
                }));
            }
        }

        private static void WriteActivityEvents(StreamWriter writer, GuildLifeDocument document)
        {
            int start = Math.Max(0, document.ActivityEvents.Count - GuildActivityEngine.MaxActivityEvents);
            for (int i = start; i < document.ActivityEvents.Count; i++)
            {
                GuildActivityEvent value = document.ActivityEvents[i];
                if (value == null || value.Sequence <= 0) continue;
                writer.WriteLine(string.Join("|", new string[]
                {
                    "L", value.Sequence.ToString(CultureInfo.InvariantCulture), Encode(GuildLifeCore.Clean(value.EventId, 80)),
                    NormalizeUtc(value.Utc).Ticks.ToString(CultureInfo.InvariantCulture), ((int)value.Type).ToString(CultureInfo.InvariantCulture),
                    Encode(GuildLifeCore.Clean(value.ActivityId, 80)), Encode(GuildLifeCore.Clean(value.GuildKey, 96)),
                    Encode(GuildLifeCore.Clean(value.Detail, 320)), value.Meaningful ? "True" : "False",
                    JoinIds(value.ParticipantIds), Encode(JoinNames(value.ParticipantNames))
                }));
            }
        }

        private static void TrimLoaded(GuildLifeDocument document)
        {
            while (document.ActivityEvents.Count > GuildActivityEngine.MaxActivityEvents) document.ActivityEvents.RemoveAt(0);
            while (document.Opportunities.Count > GuildActivityEngine.MaxOpportunities) document.Opportunities.RemoveAt(0);
            while (document.Relationships.Count > GuildActivityEngine.MaxRelationships) document.Relationships.RemoveAt(0);
            while (document.MemberStates.Count > GuildActivityEngine.MaxMemberStates) document.MemberStates.RemoveAt(0);
        }

        private void TryBackupUnreadable()
        {
            try
            {
                if (!File.Exists(_path)) return;
                string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                string corrupt = _path + ".corrupt-" + stamp;
                int suffix = 2;
                while (File.Exists(corrupt))
                {
                    corrupt = _path + ".corrupt-" + stamp + "-" + suffix.ToString(CultureInfo.InvariantCulture);
                    suffix++;
                }
                File.Copy(_path, corrupt, true);
            }
            catch { }
        }

        private static DateTime NormalizeUtc(DateTime value)
        {
            if (value == default(DateTime)) return DateTime.UtcNow;
            if (value.Kind == DateTimeKind.Utc) return value;
            try { return value.ToUniversalTime(); }
            catch { return DateTime.UtcNow; }
        }

        private static bool TryTicks(string raw, out long ticks)
        {
            ticks = 0;
            return long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks) &&
                   ticks > DateTime.MinValue.Ticks && ticks <= DateTime.MaxValue.Ticks;
        }

        private static bool TryTicksOrZero(string raw, out long ticks)
        {
            ticks = 0;
            if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks)) return false;
            return ticks == 0 || (ticks > DateTime.MinValue.Ticks && ticks <= DateTime.MaxValue.Ticks);
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        private static bool TryDecode(string value, out string decoded)
        {
            decoded = string.Empty;
            if (string.IsNullOrEmpty(value)) return true;
            try { decoded = Encoding.UTF8.GetString(Convert.FromBase64String(value)); return true; }
            catch { decoded = string.Empty; return false; }
        }

        private static string JoinIds(List<int> values)
        {
            if (values == null || values.Count == 0) return string.Empty;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(values[i].ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private static void ParseIds(string raw, List<int> target)
        {
            if (target == null || string.IsNullOrWhiteSpace(raw)) return;
            string[] values = raw.Split(',');
            for (int i = 0; i < values.Length; i++)
            {
                int id;
                if (int.TryParse(values[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out id) && id >= 0) target.Add(id);
            }
        }

        private static string JoinNames(List<string> values)
        {
            if (values == null || values.Count == 0) return string.Empty;
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) sb.Append('\u001f');
                sb.Append(GuildLifeCore.Clean(values[i], 96));
            }
            return sb.ToString();
        }

        private static void ParseNames(string raw, List<string> target)
        {
            if (target == null || string.IsNullOrEmpty(raw)) return;
            string[] values = raw.Split('\u001f');
            for (int i = 0; i < values.Length; i++)
            {
                string clean = GuildLifeCore.Clean(values[i], 96);
                if (clean.Length > 0) target.Add(clean);
            }
        }
    }

    // V1 legacy bulletin claim remains byte-for-byte safe: the first character may import it,
    // GuildStore will then read V1 and upgrade to V2 on the next normal save.
    internal static class LegacyBulletinClaim
    {
        internal static bool TryClaim(string legacyPath, string claimMarkerPath, string targetPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(legacyPath) || string.IsNullOrWhiteSpace(claimMarkerPath) || string.IsNullOrWhiteSpace(targetPath)) return false;
                if (!File.Exists(legacyPath) || File.Exists(claimMarkerPath) || File.Exists(targetPath)) return false;
                string directory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                File.Copy(legacyPath, targetPath, false);
                string markerDirectory = Path.GetDirectoryName(claimMarkerPath);
                if (!string.IsNullOrWhiteSpace(markerDirectory)) Directory.CreateDirectory(markerDirectory);
                File.WriteAllText(claimMarkerPath, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
                return true;
            }
            catch { return false; }
        }
    }
}
