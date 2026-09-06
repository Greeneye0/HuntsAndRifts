using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ProjectM;
using ProjectM.Shared.WarEvents;
using ProjectM.Shared.WorldEvents;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace HuntsAndRifts;

internal readonly struct RiftTrack
{
    internal WarEventType Type { get; init; }
    internal bool IsActive { get; init; }
    internal double RemainingSeconds { get; init; }
    internal double NextInSeconds { get; init; }
    internal int Level { get; init; }
    internal long StartTicks { get; init; }
    internal long EndTicks { get; init; }
    internal long NextTicks { get; init; }
    internal long DecayTicks { get; init; }
    internal bool IsTest { get; init; }
}

internal static class RiftReader
{
    internal const string ServerJsonPath = @"C:\VRisingServer\BepInEx\Log\HuntsAndRifts-rifts.json";
    internal const double TestMinorSeconds = 12 * 60;
    internal const double TestMajorSeconds = 19 * 60 + 41;

    private static float _nextRefresh;
    private static readonly List<RiftTrack> Tracks = new(4);
    private static bool _loggedQuery;
    private static bool _loggedJson;
    private static bool _loggedClock;
    private static int _lastNetworkedCount = -1;
    private static int _lastWarEventCount = -1;
    private static long _testEpochMinor;
    private static long _testEpochMajor;
    private static float _widgetFill = -1f;
    private static double _widgetTimer = -1;
    private static float _widgetTimerAt = -1f;
    private static bool _loggedWidget;
    private static readonly RiftTimerSample WidgetSample = new();
    private static long _worldSequence = -1;
    private static string _loggedEventKey;
    private static bool _hasMapperTime;
    private static ServerTime _mapperTime;

    internal static IReadOnlyList<RiftTrack> GetTracks()
    {
        RefreshIfNeeded();
        return Tracks;
    }

    internal static void SetMapperTime(ServerTime time)
    {
        _mapperTime = time;
        _hasMapperTime = true;
    }

    internal static void SetWidgetFill(float fill)
    {
        if (float.IsNaN(fill) || float.IsInfinity(fill))
            _widgetFill = -1f;
        else
            _widgetFill = Math.Clamp(fill, 0f, 1f);
    }

    internal static void SetWidgetTimer(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0 || seconds > 14 * 24 * 3600)
            return;
        if (_widgetTimer < 0 || Math.Abs(_widgetTimer - seconds) > 0.4)
            _nextRefresh = 0;
        _widgetTimer = seconds;
        _widgetTimerAt = Time.unscaledTime;
    }

    // The vanilla timer text is only re-written by the game on state changes once HuntsAndRifts
    // overwrites it, so the parsed value must be aged by the time since it was read.
    private static double WidgetTimerNow()
    {
        if (_widgetTimer < 0)
            return -1;
        var age = _widgetTimerAt >= 0f ? Time.unscaledTime - _widgetTimerAt : 0f;
        var v = _widgetTimer - age;
        return v < 0 ? 0 : v;
    }

    internal static void RefreshIfNeeded()
    {
        var now = Time.unscaledTime;
        if (now < _nextRefresh)
            return;
        _nextRefresh = now + 0.5f;
        RefreshNow();
    }

    private static void RefreshNow()
    {
        Tracks.Clear();
        var utcNowTicks = DateTime.UtcNow.Ticks;
        var includeTest = Plugin.IncludeTestTiers != null && Plugin.IncludeTestTiers.Value;
        var byType = new Dictionary<WarEventType, RiftTrack>(4);
        ServerTime serverTime = default;
        var hasServerTime = false;
        if (_hasMapperTime)
        {
            serverTime = _mapperTime;
            hasServerTime = true;
        }

        if (HuntReader.TryGetClientWorld(out var world))
        {
            var sequence = (long)world.SequenceNumber;
            if (_worldSequence != sequence)
            {
                _worldSequence = sequence;
                WidgetSample.Reset();
                _loggedEventKey = null;
                Observed.Clear();
                _gates.Clear();
                _gatesAt = -10f;
                _scheduleAt = -1f;
                RiftCardExtension.ResetTiming();
            }
            try
            {
                if (!hasServerTime)
                    hasServerTime = TryReadServerTime(world.EntityManager, out serverTime);
                ApplyClientEcs(world.EntityManager, byType, utcNowTicks, hasServerTime, serverTime);
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogDebug("HuntsAndRifts rift ECS skipped: " + ex.Message);
            }
        }

        // Do not merge a machine-local server file: it cannot be tied to the connected world.

        if (includeTest)
        {
            if (!byType.ContainsKey(WarEventType.Minor))
                byType[WarEventType.Minor] = MakeTestTrack(WarEventType.Minor, 57, utcNowTicks);
            if (!byType.ContainsKey(WarEventType.Major))
                byType[WarEventType.Major] = MakeTestTrack(WarEventType.Major, 80, utcNowTicks);
        }

        // Retain each tier's own observed deadline; never infer an alternating schedule.
        if (HuntReader.TryGetClientWorld(out var worldForSettings))
        {
            try
            {
                TrySynthesizeMissing(byType, worldForSettings.EntityManager, ReadLevels(worldForSettings.EntityManager));
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogDebug("HuntsAndRifts derive other rift skipped: " + ex.Message);
            }
        }

        // Both tiers can run at once while only one is networked: open gates tell the truth.
        if (HuntReader.TryGetClientWorld(out var gateWorld))
        {
            try
            {
                var gates = CachedGates(gateWorld.EntityManager);
                foreach (var g in gates)
                {
                    if (byType.TryGetValue(g.Type, out var t) && t.IsActive)
                        continue;
                    var level = byType.TryGetValue(g.Type, out var known) ? known.Level : 0;
                    byType[g.Type] = new RiftTrack
                    {
                        Type = g.Type,
                        IsActive = true,
                        RemainingSeconds = -1, // end time not networked for this tier
                        NextInSeconds = 0,
                        Level = level,
                        IsTest = false
                    };
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger?.LogDebug("HuntsAndRifts gate override: " + ex.Message);
            }
        }

        AddIfWanted(byType, WarEventType.Minor, includeTest);
        AddIfWanted(byType, WarEventType.Major, includeTest);
        AddIfWanted(byType, WarEventType.Primal, includeTest);
    }

    // Only carry forward a countdown observed for this exact tier. Settings describe
    // durations, not the independent scheduler's next start (tiers may overlap).
    private static readonly Dictionary<WarEventType, (RiftTrack track, float at)> Observed = new();
    private static void TrySynthesizeMissing(Dictionary<WarEventType, RiftTrack> byType,
        EntityManager em, Levels levels)
    {
        var now = Time.unscaledTime;
        foreach (var pair in byType)
            if (!pair.Value.IsTest)
                Observed[pair.Key] = (pair.Value, now);
        foreach (var type in new[] { WarEventType.Minor, WarEventType.Major })
        {
            if (byType.ContainsKey(type)) continue;
            var active = false;
            double remaining = -1, next = -1;
            if (Observed.TryGetValue(type, out var seen))
            {
                remaining = RiftTiming.Age(seen.track.RemainingSeconds, seen.at, now);
                next = RiftTiming.Age(seen.track.NextInSeconds, seen.at, now);
                active = seen.track.IsActive && remaining > 0;
                // Reaching a deadline does not prove a new event has started.
                if (seen.track.IsActive || next <= 0) next = -1;
            }
            byType[type] = new RiftTrack { Type = type, Level = levels.For(type),
                IsActive = active, RemainingSeconds = active ? remaining : -1, NextInSeconds = next };
        }
    }
    private static float _gatesAt = -10f;
    private static List<RiftGates.OpenGate> _gates = new();

    private static List<RiftGates.OpenGate> CachedGates(EntityManager em)
    {
        var now = Time.unscaledTime;
        if (now - _gatesAt >= 2f)
        {
            _gatesAt = now;
            _gates = RiftGates.Scan(em);
        }
        return _gates;
    }

    private static float _scheduleAt = -1f;
    private static bool _scheduleOk;
    private static WarEventGameSettings.StructData _schedule;

    /// <summary>Event duration and gap (seconds) for a tier, from the server's war settings.</summary>
    internal static bool TryGetSchedule(WarEventType type, out double duration, out double gap)
    {
        duration = 0;
        gap = 0;
        try
        {
            var now = Time.unscaledTime;
            if (_scheduleAt < 0f || now - _scheduleAt > 5f)
            {
                _scheduleAt = now;
                _scheduleOk = HuntReader.TryGetClientWorld(out var world) && TryReadWarSettings(world.EntityManager, out _schedule);
            }
            if (!_scheduleOk)
                return false;
            var d = _schedule.GetTotalDuration(type);
            var g = _schedule.GetTotalTimeBetweenEvents(type);
            duration = d;
            gap = g;
            return IsSaneSchedule(d, allowZero: false) && IsSaneSchedule(g, allowZero: true);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadWarSettings(EntityManager em, out WarEventGameSettings.StructData war)
    {
        war = default;
        NativeArray<ServerGameBalanceSettings> arr = default;
        try
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<ServerGameBalanceSettings>());
            try
            {
                if (q.IsEmptyIgnoreFilter)
                    return false;
                arr = q.ToComponentDataArray<ServerGameBalanceSettings>(Allocator.Temp);
                if (arr.Length == 0)
                    return false;
                war = arr[0].WarEventSettings;
                return true;
            }
            finally
            {
                q.Dispose();
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            if (arr.IsCreated)
                arr.Dispose();
        }
    }

    private static bool IsSaneSchedule(float seconds, bool allowZero)
    {
        if (float.IsNaN(seconds) || float.IsInfinity(seconds))
            return false;
        if (seconds < 0)
            return false;
        if (seconds == 0)
            return allowZero;
        return seconds <= 14 * 24 * 3600;
    }

    private static void AddIfWanted(Dictionary<WarEventType, RiftTrack> byType, WarEventType type, bool includeTest)
    {
        if (!byType.TryGetValue(type, out var track))
            return;
        if (track.IsTest && !includeTest)
            return;
        Tracks.Add(track);
    }

    private static RiftTrack MakeTestTrack(WarEventType type, int level, long nowTicks)
    {
        double remaining;
        if (type == WarEventType.Major)
            remaining = TestRemaining(TestMajorSeconds, ref _testEpochMajor, nowTicks);
        else
            remaining = TestRemaining(TestMinorSeconds, ref _testEpochMinor, nowTicks);
        return new RiftTrack
        {
            Type = type,
            IsActive = true,
            RemainingSeconds = remaining,
            NextInSeconds = 0,
            Level = level,
            IsTest = true
        };
    }

    internal static double TestRemaining(double durationSeconds, ref long epochTicks, long nowTicks)
    {
        if (epochTicks <= 0)
            epochTicks = nowTicks;
        var elapsed = (nowTicks - epochTicks) / (double)TimeSpan.TicksPerSecond;
        if (elapsed < 0)
            elapsed = 0;
        var rem = durationSeconds - (elapsed % durationSeconds);
        if (rem <= 0.05)
            rem = durationSeconds;
        return rem;
    }

    private static bool TryApplyJson(
        Dictionary<WarEventType, RiftTrack> byType,
        long utcNowTicks,
        bool includeTest)
    {
        string text;
        try
        {
            if (!File.Exists(ServerJsonPath))
                return false;
            using var fs = new FileStream(ServerJsonPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            text = sr.ReadToEnd();
        }
        catch
        {
            return false;
        }

        if (string.IsNullOrEmpty(text))
            return false;

        if (!TryExtractLong(text, "utcTicks", out var utcTicks))
            return false;
        var ageSec = (utcNowTicks - utcTicks) / (double)TimeSpan.TicksPerSecond;
        if (ageSec > 10)
            return false;

        var applied = 0;
        applied += TryApplyJsonTier(text, "Minor", WarEventType.Minor, byType, includeTest) ? 1 : 0;
        applied += TryApplyJsonTier(text, "Major", WarEventType.Major, byType, includeTest) ? 1 : 0;
        if (applied == 0)
            return false;

        if (!_loggedJson)
        {
            _loggedJson = true;
            Plugin.Logger?.LogInfo("HuntsAndRifts reading server JSON " + ServerJsonPath);
        }
        return true;
    }

    private static bool TryApplyJsonTier(
        string json,
        string typeName,
        WarEventType type,
        Dictionary<WarEventType, RiftTrack> byType,
        bool includeTest)
    {
        var marker = "\"type\":\"" + typeName + "\"";
        var idx = json.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
            return false;
        var start = json.LastIndexOf('{', idx);
        var end = json.IndexOf('}', idx);
        if (start < 0 || end < 0 || end <= start)
            return false;
        var obj = json.Substring(start, end - start + 1);

        var test = ExtractBool(obj, "test", true);
        if (test && !includeTest)
            return false;
        if (byType.TryGetValue(type, out var existing) && !existing.IsTest)
            return false;

        TryExtractInt(obj, "level", out var level);
        if (level <= 0)
            level = type == WarEventType.Major ? 80 : 57;
        TryExtractDouble(obj, "remaining", out var remaining);
        TryExtractDouble(obj, "next", out var next);
        var active = ExtractBool(obj, "active", true);

        byType[type] = new RiftTrack
        {
            Type = type,
            IsActive = active,
            RemainingSeconds = remaining,
            NextInSeconds = next,
            Level = level,
            IsTest = test
        };
        return true;
    }

    private static bool ExtractBool(string obj, string key, bool fallback)
    {
        var needle = "\"" + key + "\":";
        var i = obj.IndexOf(needle, StringComparison.Ordinal);
        if (i < 0)
            return fallback;
        i += needle.Length;
        while (i < obj.Length && obj[i] == ' ')
            i++;
        if (i + 4 <= obj.Length && string.Compare(obj, i, "true", 0, 4, StringComparison.OrdinalIgnoreCase) == 0)
            return true;
        if (i + 5 <= obj.Length && string.Compare(obj, i, "false", 0, 5, StringComparison.OrdinalIgnoreCase) == 0)
            return false;
        return fallback;
    }

    private static bool TryExtractLong(string obj, string key, out long value)
    {
        value = 0;
        var needle = "\"" + key + "\":";
        var i = obj.IndexOf(needle, StringComparison.Ordinal);
        if (i < 0)
            return false;
        i += needle.Length;
        var j = i;
        while (j < obj.Length && (char.IsDigit(obj[j]) || obj[j] == '-'))
            j++;
        return long.TryParse(obj.Substring(i, j - i), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryExtractInt(string obj, string key, out int value)
    {
        value = 0;
        if (!TryExtractLong(obj, key, out var l))
            return false;
        value = (int)l;
        return true;
    }

    private static bool TryExtractDouble(string obj, string key, out double value)
    {
        value = 0;
        var needle = "\"" + key + "\":";
        var i = obj.IndexOf(needle, StringComparison.Ordinal);
        if (i < 0)
            return false;
        i += needle.Length;
        var j = i;
        while (j < obj.Length && (char.IsDigit(obj[j]) || obj[j] == '-' || obj[j] == '.' ))
            j++;
        return double.TryParse(obj.Substring(i, j - i), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static void ApplyClientEcs(
        EntityManager em,
        Dictionary<WarEventType, RiftTrack> byType,
        long utcNowTicks,
        bool hasServerTime,
        ServerTime serverTime)
    {
        var levels = ReadLevels(em);
        NativeArray<WarEvent> warEvents = default;
        NativeArray<WarEvent_NetworkedData> networked = default;
        try
        {
            var warQuery = em.CreateEntityQuery(ComponentType.ReadOnly<WarEvent>());
            if (!warQuery.IsEmptyIgnoreFilter)
                warEvents = warQuery.ToComponentDataArray<WarEvent>(Allocator.Temp);

            var netQuery = em.CreateEntityQuery(ComponentType.ReadOnly<WarEvent_NetworkedData>());
            if (!netQuery.IsEmptyIgnoreFilter)
                networked = netQuery.ToComponentDataArray<WarEvent_NetworkedData>(Allocator.Temp);

            LogCountsOnce(warEvents, networked);

            if (networked.IsCreated)
            {
                for (var i = 0; i < networked.Length; i++)
                {
                    var track = FromNetworked(networked[i], utcNowTicks, hasServerTime, serverTime, levels,
                        networked.Length == 1);
                    if (track.Type == WarEventType.Minor || track.Type == WarEventType.Major || track.Type == WarEventType.Primal)
                        byType[track.Type] = track;
                }
            }

            if (warEvents.IsCreated)
            {
                for (var i = 0; i < warEvents.Length; i++)
                {
                    var track = FromWarEvent(warEvents[i], utcNowTicks, hasServerTime, serverTime, levels);
                    if (track.Type != WarEventType.Minor && track.Type != WarEventType.Major && track.Type != WarEventType.Primal)
                        continue;
                    if (byType.TryGetValue(track.Type, out var existing) && !existing.IsTest)
                        continue;
                    byType[track.Type] = track;
                }
            }
        }
        finally
        {
            // A vanilla reading is a single observation, not a fresh sample on every refresh.
            _widgetTimer = -1;
            if (warEvents.IsCreated)
                warEvents.Dispose();
            if (networked.IsCreated)
                networked.Dispose();
        }
    }

    private static RiftTrack WithLevel(RiftTrack track, int level)
    {
        return new RiftTrack
        {
            Type = track.Type,
            IsActive = track.IsActive,
            RemainingSeconds = track.RemainingSeconds,
            NextInSeconds = track.NextInSeconds,
            Level = level,
            StartTicks = track.StartTicks,
            EndTicks = track.EndTicks,
            NextTicks = track.NextTicks,
            DecayTicks = track.DecayTicks,
            IsTest = track.IsTest
        };
    }

    private static void LogCountsOnce(NativeArray<WarEvent> warEvents, NativeArray<WarEvent_NetworkedData> networked)
    {
        var warCount = warEvents.IsCreated ? warEvents.Length : 0;
        var netCount = networked.IsCreated ? networked.Length : 0;
        if (warCount == _lastWarEventCount && netCount == _lastNetworkedCount && _loggedQuery)
            return;
        _lastWarEventCount = warCount;
        _lastNetworkedCount = netCount;
        _loggedQuery = true;

        var types = "";
        if (networked.IsCreated)
        {
            for (var i = 0; i < networked.Length; i++)
            {
                if (types.Length > 0)
                    types += ", ";
                var n = networked[i];
                types += n.ActiveType + (n.IsActive ? " active" : " upcoming");
            }
        }

        Plugin.Logger?.LogInfo(
            $"HuntsAndRifts rifts: {netCount} WarEvent_NetworkedData, {warCount} WarEvent on client" +
            (types.Length > 0 ? " [" + types + "]" : "") + " (live only; TEST rows off unless IncludeTestTiers).");
    }

    private static RiftTrack FromWarEvent(
        WarEvent ev,
        long utcNowTicks,
        bool hasServerTime,
        ServerTime serverTime,
        Levels levels)
    {
        var remaining = SecondsUntil(ev.EndTimeTicks, utcNowTicks, hasServerTime, serverTime, false);
        var untilStart = SecondsUntil(ev.StartTimeTicks, utcNowTicks, hasServerTime, serverTime, true);
        var active = remaining > 0 && untilStart <= 0;
        return new RiftTrack
        {
            Type = ev.EventType,
            IsActive = active,
            RemainingSeconds = remaining,
            NextInSeconds = untilStart,
            Level = levels.For(ev.EventType),
            StartTicks = ev.StartTimeTicks,
            EndTicks = ev.EndTimeTicks,
            NextTicks = ev.StartTimeTicks,
            DecayTicks = ev.DecayDurationTicks,
            IsTest = false
        };
    }

    private static RiftTrack FromNetworked(
        WarEvent_NetworkedData data,
        long utcNowTicks,
        bool hasServerTime,
        ServerTime serverTime,
        Levels levels,
        bool useWidget)
    {
        var remaining = SecondsUntil(data.ActiveEventEndTimeTicks, utcNowTicks, hasServerTime, serverTime, false);
        var duration = (data.ActiveEventEndTimeTicks - data.ActiveEventStartTimeTicks) / (double)TimeSpan.TicksPerSecond;
        var untilStart = SecondsUntil(data.ActiveEventStartTimeTicks, utcNowTicks, hasServerTime, serverTime, true);
        var nextIn = SecondsUntil(data.NextEventTimeTicks, utcNowTicks, hasServerTime, serverTime, true);
        var eventKey = data.ActiveType + ":" + data.IsActive + ":" + data.ActiveEventStartTimeTicks
            + ":" + data.ActiveEventEndTimeTicks + ":" + data.NextEventTimeTicks;
        // Vanilla fill can represent elapsed progress, and may still belong to the
        // previous event. It is not an independent clock.
        if (!double.IsFinite(remaining) || remaining < 0 || remaining > 86400) remaining = -1;
        if (!double.IsFinite(nextIn) || nextIn < 0 || nextIn > 14 * 86400) nextIn = -1;
        if (_loggedEventKey != eventKey)
        {
            _loggedEventKey = eventKey;
            Plugin.Logger?.LogInfo("HuntsAndRifts rift timing: " + data.ActiveType
                + (data.IsActive ? " active" : " upcoming")
                + " remaining=" + remaining.ToString("0.0", CultureInfo.InvariantCulture)
                + " next=" + nextIn.ToString("0.0", CultureInfo.InvariantCulture)
                + " source=" + "event ticks" + ".");
        }
        return new RiftTrack
        {
            Type = data.ActiveType,
            IsActive = data.IsActive,
            RemainingSeconds = remaining,
            NextInSeconds = data.IsActive ? Math.Max(0, untilStart) : nextIn,
            Level = levels.For(data.ActiveType),
            StartTicks = data.ActiveEventStartTimeTicks,
            EndTicks = data.ActiveEventEndTimeTicks,
            NextTicks = data.NextEventTimeTicks,
            DecayTicks = data.ActiveEventDecayTicks,
            IsTest = false
        };
    }

    private static bool TryReadServerTime(EntityManager em, out ServerTime time)
    {
        time = default;
        NativeArray<ServerTime> arr = default;
        try
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<ServerTime>());
            if (q.IsEmptyIgnoreFilter)
                return false;
            arr = q.ToComponentDataArray<ServerTime>(Allocator.Temp);
            if (arr.Length == 0)
                return false;
            time = arr[0];
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (arr.IsCreated)
                arr.Dispose();
        }
    }

    private static double SecondsUntil(
        long targetTicks,
        long utcNowTicks,
        bool hasServerTime,
        ServerTime serverTime,
        bool allowLong)
    {
        // Verified game timestamps are UTC ticks, not ECS elapsed/server time.
        // Reading them directly also avoids parsing translated or rounded UI text.
        return RiftTiming.SecondsUntil(targetTicks, utcNowTicks);
    }
    private static void LogWidgetOnce(string detail)
    {
        if (_loggedWidget)
            return;
        _loggedWidget = true;
        Plugin.Logger?.LogInfo("HuntsAndRifts rift remaining: " + detail);
    }

    private static void LogClockOnce(string clock)
    {
        if (_loggedClock)
            return;
        _loggedClock = true;
        Plugin.Logger?.LogInfo("HuntsAndRifts rift clock: " + clock);
    }

    private static bool TrySane(double seconds, double cap, out double value)
    {
        value = seconds;
        return !double.IsNaN(seconds) && !double.IsInfinity(seconds) && seconds > -5 && seconds <= cap;
    }

    private readonly struct Levels
    {
        public int Minor { get; init; }
        public int Major { get; init; }
        public int Primal { get; init; }

        public int For(WarEventType type) => type switch
        {
            WarEventType.Minor => Minor,
            WarEventType.Major => Major,
            WarEventType.Primal => Primal,
            _ => 0
        };
    }

    private static Levels ReadLevels(EntityManager em)
    {
        var result = new Levels { Minor = 57, Major = 80, Primal = 0 };
        NativeArray<WarEventSettingsComponent> arr = default;
        try
        {
            var q = em.CreateEntityQuery(ComponentType.ReadOnly<WarEventSettingsComponent>());
            if (q.IsEmptyIgnoreFilter)
                return result;
            arr = q.ToComponentDataArray<WarEventSettingsComponent>(Allocator.Temp);
            if (arr.Length == 0)
                return result;
            var s = arr[0];
            result = new Levels
            {
                Minor = FirstPositive(s.MinorWarEventSettings.RecommendedGearLevel, s.MinorWarEventSettings.MinGearLevel, 57),
                Major = FirstPositive(s.MajorWarEventSettings.RecommendedGearLevel, s.MajorWarEventSettings.MinGearLevel, 80),
                Primal = FirstPositive(s.PrimalWarEventSettings.RecommendedGearLevel, s.PrimalWarEventSettings.MinGearLevel, 0)
            };
        }
        catch
        {
        }
        finally
        {
            if (arr.IsCreated)
                arr.Dispose();
        }
        return result;
    }

    private static int FirstPositive(int a, int b, int fallback)
    {
        if (a > 0) return a;
        if (b > 0) return b;
        return fallback;
    }
}
