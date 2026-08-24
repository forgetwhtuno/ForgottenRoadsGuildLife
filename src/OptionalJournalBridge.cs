using System;
using System.Reflection;

namespace ErenshorGuildLife
{
    // Optional reflection-only Chronicle integration. Unknown/absent Journal versions are ignored.
    internal static class OptionalJournalBridge
    {
        private static MethodInfo _addEvent;
        private static DateTime _nextResolveUtc;

        internal static bool TryPost(GuildActivityEvent evt)
        {
            if (evt == null || !evt.Meaningful || string.IsNullOrWhiteSpace(evt.EventId) || string.IsNullOrWhiteSpace(evt.Detail)) return false;
            MethodInfo method = Resolve();
            if (method == null) return false;
            try
            {
                object value = method.Invoke(null, new object[]
                {
                    evt.EventId, "Guild Life", "Guild", "Guild activity", evt.Detail
                });
                return value is bool && (bool)value;
            }
            catch
            {
                _addEvent = null;
                _nextResolveUtc = DateTime.UtcNow.AddSeconds(10);
                return false;
            }
        }

        private static MethodInfo Resolve()
        {
            if (_addEvent != null) return _addEvent;
            if (DateTime.UtcNow < _nextResolveUtc) return null;
            _nextResolveUtc = DateTime.UtcNow.AddSeconds(10);
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = null;
                try { type = assemblies[i].GetType("ErenshorJournal.JournalApi", false); } catch { }
                if (type == null) continue;
                try
                {
                    MethodInfo method = type.GetMethod("AddChronicleEvent", BindingFlags.Public | BindingFlags.Static, null,
                        new Type[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(string) }, null);
                    if (method != null && method.ReturnType == typeof(bool)) { _addEvent = method; return _addEvent; }
                }
                catch { }
            }
            return null;
        }
    }
}
