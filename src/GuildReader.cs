using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ErenshorGuildLife
{
    internal static class GuildReader
    {
        private static Type _gameDataType;
        private static Type _guildManagerType;
        private static bool _typesResolved;

        internal static GuildSnapshot Read(string verifiedPlayerName)
        {
            ResolveTypes();
            GuildSnapshot result = new GuildSnapshot();
            if (_gameDataType == null)
            {
                result.Diagnostic = "Assembly-CSharp GameData type was not available.";
                return result;
            }

            result.PlayerName = string.IsNullOrWhiteSpace(verifiedPlayerName) ? string.Empty : verifiedPlayerName.Trim();
            if (result.PlayerName.Length == 0)
            {
                result.Diagnostic = "The active character identity was not available.";
                return result;
            }

            object guildManager = ReadStaticMember(_gameDataType, new string[] { "GuildManager", "GuildMngr" });
            if (guildManager == null && _guildManagerType != null)
            {
                try { guildManager = UnityEngine.Object.FindObjectOfType(_guildManagerType); }
                catch { }
            }

            IEnumerable guilds = ReadMember(guildManager, new string[] { "Guilds" }) as IEnumerable;
            if (guilds == null)
            {
                result.Diagnostic = "Native Guilds collection was not available.";
                return result;
            }

            result.RuntimeAvailable = true;
            Dictionary<string, TrackingInfo> tracking = ReadTracking();
            bool unreadableRosterSeen = false;

            foreach (object guild in guilds)
            {
                if (guild == null) continue;
                bool rosterResolved;
                List<string> members = ReadMemberNames(guild, out rosterResolved);
                if (!rosterResolved)
                {
                    unreadableRosterSeen = true;
                    continue;
                }
                if (members.Count == 0) continue;

                bool playerMember = false;
                for (int i = 0; i < members.Count; i++)
                {
                    if (!string.IsNullOrWhiteSpace(result.PlayerName) &&
                        string.Equals(members[i], result.PlayerName, StringComparison.OrdinalIgnoreCase))
                    {
                        playerMember = true;
                        break;
                    }
                }
                if (!playerMember) continue;

                result.InGuild = true;
                result.GuildId = ReadInt(guild, new string[] { "Id", "ID", "GuildID", "GuildId" }, 0);
                result.GuildName = ReadString(guild, new string[] { "GuildName", "Name" }, string.Empty);

                for (int i = 0; i < members.Count; i++)
                {
                    string memberName = members[i];
                    GuildMemberSnapshot member = new GuildMemberSnapshot();
                    member.Name = memberName;
                    TrackingInfo info;
                    if (tracking.TryGetValue(memberName, out info))
                    {
                        member.Zone = info.Zone;
                        member.Level = info.Level;
                        member.StableId = info.StableId;
                        member.TrackingResolved = info.StableId >= 0;
                        member.GroupedWithPlayer = info.GroupedWithPlayer;
                        member.KnownUnavailable = info.KnownUnavailable;
                    }
                    else
                    {
                        member.Zone = string.Empty;
                        member.Level = 0;
                        member.StableId = -1;
                        member.TrackingResolved = false;
                    }
                    result.Members.Add(member);
                }
                break;
            }

            if (!result.InGuild)
            {
                if (unreadableRosterSeen)
                {
                    result.RuntimeAvailable = false;
                    result.Diagnostic = "One or more native guild rosters could not be read.";
                    return result;
                }
                result.Diagnostic = "The active character was not found in a native guild roster.";
                return result;
            }

            result.Members.Sort(delegate(GuildMemberSnapshot a, GuildMemberSnapshot b)
            {
                return string.Compare(a == null ? string.Empty : a.Name,
                                      b == null ? string.Empty : b.Name,
                                      StringComparison.OrdinalIgnoreCase);
            });
            result.Diagnostic = "Native guild roster resolved read-only.";
            return result;
        }

        private static void ResolveTypes()
        {
            if (_typesResolved && _gameDataType != null) return;
            _typesResolved = true;
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                Type gameData = null;
                try { gameData = assembly.GetType("GameData", false); } catch { }
                if (gameData == null) continue;
                _gameDataType = gameData;
                try { _guildManagerType = assembly.GetType("GuildManager", false); } catch { }
                return;
            }
        }

        private static Dictionary<string, TrackingInfo> ReadTracking()
        {
            Dictionary<string, TrackingInfo> result =
                new Dictionary<string, TrackingInfo>(StringComparer.OrdinalIgnoreCase);
            // Native guild rosters currently expose member names while SimPlayerTracking exposes
            // the stable simIndex. If two tracking records ever share a display name, fail closed
            // instead of arbitrarily binding permanent Guild Life state to one of them.
            HashSet<string> ambiguousNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<int> groupedIds = new HashSet<int>();
            HashSet<string> groupedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ReadCurrentGroupIdentity(groupedIds, groupedNames);

            object simManager = ReadStaticMember(_gameDataType, new string[] { "SimMngr", "SimManager" });
            IEnumerable sims = ReadMember(simManager, new string[] { "Sims" }) as IEnumerable;
            if (sims == null) return result;

            foreach (object tracking in sims)
            {
                if (tracking == null) continue;
                string name = ReadString(tracking, new string[] { "SimName", "Name" }, string.Empty);
                if (string.IsNullOrWhiteSpace(name)) continue;

                TrackingInfo value = new TrackingInfo();
                value.Zone = ReadString(tracking, new string[] { "CurScene", "CurrentScene", "Scene", "Zone" }, string.Empty);
                value.Level = ReadInt(tracking, new string[] { "Level", "CurrentLevel", "SimLevel" }, 0);
                // Current installed-source evidence (Follow assembly findings) establishes
                // SimPlayerTracking.simIndex as the persistent Sim identity across zoning.
                value.StableId = ReadInt(tracking, new string[] { "simIndex", "SimIndex" }, -1);
                value.GroupedWithPlayer = (value.StableId >= 0 && groupedIds.Contains(value.StableId)) || groupedNames.Contains(name);
                value.KnownUnavailable = ReadLoadedDeadState(tracking);

                if (ambiguousNames.Contains(name)) continue;
                TrackingInfo existing;
                if (result.TryGetValue(name, out existing) && existing != null && existing.StableId != value.StableId)
                {
                    result.Remove(name);
                    ambiguousNames.Add(name);
                    continue;
                }
                result[name] = value;
            }
            return result;
        }

        private static void ReadCurrentGroupIdentity(HashSet<int> ids, HashSet<string> names)
        {
            object raw = ReadStaticMember(_gameDataType, new string[] { "GroupMembers" });
            IEnumerable members = raw as IEnumerable;
            if (members == null) return;
            foreach (object tracking in members)
            {
                if (tracking == null) continue;
                int id = ReadInt(tracking, new string[] { "simIndex", "SimIndex" }, -1);
                if (id >= 0) ids.Add(id);
                string name = ReadString(tracking, new string[] { "SimName", "Name" }, string.Empty);
                if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
            }
        }

        private static bool ReadLoadedDeadState(object tracking)
        {
            object avatar = ReadMember(tracking, new string[] { "MyAvatar" });
            if (avatar == null) return false;
            object stats = ReadMember(avatar, new string[] { "MyStats" });
            object character = ReadMember(stats, new string[] { "Myself" });
            object rawAlive;
            if (!TryReadMember(character, new string[] { "Alive" }, out rawAlive) || rawAlive == null) return false;
            try { return !Convert.ToBoolean(rawAlive); }
            catch { return false; }
        }

        private static List<string> ReadMemberNames(object guild, out bool resolved)
        {
            List<string> result = new List<string>();
            object rawMembers;
            bool memberShapeFound = TryReadMember(guild, new string[] { "GuildMembers", "Members", "MemberNames" }, out rawMembers);
            IEnumerable members = rawMembers as IEnumerable;
            resolved = memberShapeFound && members != null;
            if (!resolved) return result;

            foreach (object raw in members)
            {
                string name = MemberName(raw);
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!Contains(result, name)) result.Add(name);
            }
            return result;
        }

        private static string MemberName(object value)
        {
            if (value == null) return string.Empty;
            string direct = value as string;
            if (!string.IsNullOrWhiteSpace(direct)) return direct.Trim();

            string reflected = ReadString(value,
                new string[] { "SimName", "MemberName", "CharacterName", "Name" },
                string.Empty);
            return string.IsNullOrWhiteSpace(reflected) ? string.Empty : reflected.Trim();
        }

        private static bool Contains(List<string> values, string value)
        {
            for (int i = 0; i < values.Count; i++)
                if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static object ReadStaticMember(Type type, string[] names)
        {
            if (type == null || names == null) return null;
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    FieldInfo field = type.GetField(names[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (field != null) return field.GetValue(null);
                    PropertyInfo property = type.GetProperty(names[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (property != null && property.GetIndexParameters().Length == 0) return property.GetValue(null, null);
                }
                catch { }
            }
            return null;
        }

        private static object ReadMember(object target, string[] names)
        {
            object value;
            return TryReadMember(target, names, out value) ? value : null;
        }

        private static bool TryReadMember(object target, string[] names, out object value)
        {
            value = null;
            if (target == null || names == null) return false;
            Type type = target.GetType();
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    FieldInfo field = type.GetField(names[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (field != null)
                    {
                        value = field.GetValue(target);
                        return true;
                    }
                    PropertyInfo property = type.GetProperty(names[i], BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (property != null && property.GetIndexParameters().Length == 0)
                    {
                        value = property.GetValue(target, null);
                        return true;
                    }
                }
                catch { }
            }
            return false;
        }

        private static string ReadString(object target, string[] names, string fallback)
        {
            object value = ReadMember(target, names);
            if (value == null) return fallback;
            try
            {
                string text = Convert.ToString(value);
                return string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();
            }
            catch { return fallback; }
        }

        private static int ReadInt(object target, string[] names, int fallback)
        {
            object value = ReadMember(target, names);
            if (value == null) return fallback;
            try { return Convert.ToInt32(value); }
            catch { return fallback; }
        }

        private sealed class TrackingInfo
        {
            internal string Zone;
            internal int Level;
            internal int StableId = -1;
            internal bool GroupedWithPlayer;
            internal bool KnownUnavailable;
        }
    }
}
