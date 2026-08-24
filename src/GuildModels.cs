using System;
using System.Collections.Generic;

namespace ErenshorGuildLife
{
    internal sealed class GuildMemberSnapshot
    {
        internal string Name;
        internal string Zone;
        internal int Level;
        // SimPlayerTracking.simIndex is the current verified persistent Sim key.
        // -1 means the tracking identity was not available on this read.
        internal int StableId = -1;
        internal bool TrackingResolved;
        internal bool GroupedWithPlayer;
        // Only true when a loaded avatar was positively read as not alive.
        // A null/missing avatar is temporary absence, not proof of death.
        internal bool KnownUnavailable;
    }

    internal sealed class GuildSnapshot
    {
        internal bool RuntimeAvailable;
        internal bool InGuild;
        internal string PlayerName;
        internal string GuildName;
        internal int GuildId;
        internal string Diagnostic;
        internal readonly List<GuildMemberSnapshot> Members = new List<GuildMemberSnapshot>();
    }

    internal sealed class GuildBulletinEntry
    {
        internal DateTime TimestampUtc;
        internal string Source;
        internal string Category;
        internal string Actor;
        internal string Text;
    }

    internal enum GuildActivityType
    {
        Training = 0,
        Exploration = 1,
        Patrol = 2,
        EquipmentPractice = 3,
        SupplySurvey = 4,
        Study = 5
    }

    internal enum GuildActivityStatus
    {
        Active = 0,
        Completed = 1,
        Interrupted = 2
    }

    internal enum GuildActivityEventType
    {
        ActivityStarted = 0,
        ActivityCompleted = 1,
        ActivityInterrupted = 2,
        OpportunityCreated = 3,
        OpportunityExpired = 4,
        RelationshipChanged = 5,
        Milestone = 6
    }

    internal sealed class GuildActivityRecord
    {
        internal string ActivityId;
        internal GuildActivityType Type;
        internal DateTime StartedUtc;
        internal DateTime EndsUtc;
        internal GuildActivityStatus Status;
        internal string Outcome;
        internal readonly List<int> ParticipantIds = new List<int>();
        internal readonly List<string> ParticipantNames = new List<string>();
    }

    internal sealed class GuildMemberLifeState
    {
        internal int StableId = -1;
        internal string LastKnownName;
        internal int ActivityExperience;
        internal int CompletedActivities;
        internal int Setbacks;
        internal DateTime LastSeenUtc;
    }

    internal sealed class GuildRelationshipState
    {
        internal int FirstStableId;
        internal int SecondStableId;
        internal int Rapport;
        internal int SharedActivities;
        internal int Disputes;
    }

    internal sealed class GuildOpportunity
    {
        internal string OpportunityId;
        internal DateTime CreatedUtc;
        internal DateTime ExpiresUtc;
        internal string Title;
        internal string Detail;
        internal bool Resolved;
        internal readonly List<int> ParticipantIds = new List<int>();
        internal readonly List<string> ParticipantNames = new List<string>();
    }

    internal sealed class GuildActivityEvent
    {
        internal long Sequence;
        internal string EventId;
        internal DateTime Utc;
        internal GuildActivityEventType Type;
        internal string ActivityId;
        internal string GuildKey;
        internal string Detail;
        internal bool Meaningful;
        internal readonly List<int> ParticipantIds = new List<int>();
        internal readonly List<string> ParticipantNames = new List<string>();
    }

    internal sealed class GuildLifeDocument
    {
        internal readonly List<GuildBulletinEntry> Bulletin = new List<GuildBulletinEntry>();

        // Long-lived Guild Life state. This is mod-owned progression only; it never writes
        // Erenshor level/XP/items/resources.
        internal string ActiveGuildKey;
        internal long ActivityCounter;
        internal long ActivityEventCounter;
        internal int GuildAccomplishments;
        internal readonly List<GuildMemberLifeState> MemberStates = new List<GuildMemberLifeState>();
        internal readonly List<GuildRelationshipState> Relationships = new List<GuildRelationshipState>();
        internal readonly List<GuildOpportunity> Opportunities = new List<GuildOpportunity>();
        internal readonly List<GuildActivityEvent> ActivityEvents = new List<GuildActivityEvent>();

        // Runtime-only scheduling/ownership. GuildStore deliberately does not serialize these.
        internal DateTime NextActivityUtc;
        internal readonly List<GuildActivityRecord> CurrentActivities = new List<GuildActivityRecord>();
    }

    internal sealed class PendingGuildEvent
    {
        internal DateTime TimestampUtc;
        internal string CharacterKey;
        internal string Source;
        internal string Category;
        internal string Actor;
        internal string Text;
    }

    internal sealed class GuildRosterDelta
    {
        internal readonly List<string> Joined = new List<string>();
        internal readonly List<string> Left = new List<string>();
    }
}
