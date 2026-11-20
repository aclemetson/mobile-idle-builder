using System;
using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>Android channel / notification group. One per kind of reminder so players can mute a kind in OS settings.</summary>
    public enum NotificationChannel
    {
        Production,
        Boosts,
        Rewards,
        Reminders,
    }

    /// <summary>The local notifications the game can schedule. Order is priority: earlier wins the per-day cap.</summary>
    public enum NotificationKind
    {
        StorageFull,
        ResearchDone,
        CraftDone,
        CyclotronReady,
        BoostEnded,
        DailyReward,
        RewardsExpiring,
        Comeback24h,
        Comeback72h,
        ProductionCombined,   // several production alerts merged into one
        DevTest,              // dev console "notif test": never planned, scheduled by hand
    }

    /// <summary>One notification ready to hand to the OS.</summary>
    public readonly struct PlannedNotification
    {
        public readonly NotificationKind    Kind;
        public readonly NotificationChannel Channel;
        public readonly string              Title;
        public readonly string              Body;
        public readonly DateTime            FireUtc;

        public PlannedNotification(NotificationKind kind, NotificationChannel channel, string title, string body,
                                   DateTime fireUtc)
        {
            Kind = kind; Channel = channel; Title = title; Body = body; FireUtc = fireUtc;
        }

        /// <summary>Stable numeric id (Android notification id / iOS identifier suffix).</summary>
        public int Id => 1000 + (int)Kind;

        /// <summary>
        /// How late the OS may deliver it (see <see cref="NotificationPlanner.MaxLatenessFor"/>). Every platform
        /// honours it: Android narrows its inexact alarm window to this, iOS triggers already fire on time.
        /// </summary>
        public TimeSpan MaxLateness => NotificationPlanner.MaxLatenessFor(Kind);

        public override string ToString() => $"{Kind} @ {FireUtc:yyyy-MM-dd HH:mm:ss}Z \"{Title}\"";
    }

    /// <summary>
    /// What the game knows about pending timers when the player leaves. Every delay is measured from
    /// <see cref="NowUtc"/>; null means "nothing to remind about". Built from the save by
    /// <see cref="NotificationInputs.FromSave"/>; kept separate so the planner rules are unit-tested without services.
    /// </summary>
    public class NotificationInputs
    {
        /// <summary>Real wall-clock time (the OS fires on it, never on GameClock).</summary>
        public DateTime NowUtc;
        /// <summary>Device local offset from UTC, used for quiet hours.</summary>
        public TimeSpan LocalOffset;

        public TimeSpan? StorageFullIn;
        public TimeSpan? ResearchDoneIn;
        public TimeSpan? CraftDoneIn;
        public TimeSpan? CyclotronReadyIn;
        public TimeSpan? BoostEndedIn;
        public TimeSpan? DailyRewardIn;
        public TimeSpan? RewardsExpiringIn;
        /// <summary>Comeback reminders only make sense once the player knows the game (tutorial done).</summary>
        public bool IncludeComeback;

        /// <summary>
        /// Reads the pending timers from the save. <paramref name="gameNow"/> is <see cref="GameClock.UtcNow"/>
        /// (what the timers are measured against); <paramref name="idleCapSeconds"/> is
        /// <see cref="OfflineCollectionService.GetEffectiveIdleCap"/>; <paramref name="dailyRewardEnabled"/> and
        /// <paramref name="hasClaimable"/> come from the live services, which are not pure.
        /// </summary>
        public static NotificationInputs FromSave(SaveData save, DateTime wallNowUtc, DateTime gameNow,
                                                  TimeSpan localOffset, float idleCapSeconds,
                                                  bool dailyRewardEnabled,
                                                  Func<AchievementCategory, bool> hasClaimable)
        {
            var inputs = new NotificationInputs { NowUtc = wallNowUtc, LocalOffset = localOffset };
            if (save == null) return inputs;

            bool tutorialDone = save.tutorial != null && !save.tutorial.isActive;
            inputs.IncludeComeback = tutorialDone;

            // Offline earnings stop growing once the idle cap is reached. Only useful once the factory runs
            // on its own, so not during the tutorial.
            if (tutorialDone && idleCapSeconds > 0)
                inputs.StorageFullIn = TimeSpan.FromSeconds(idleCapSeconds);

            inputs.ResearchDoneIn = Until(save.activeResearchCompleteUtc, gameNow);

            double craftLeft = CraftQueue.SecondsRemaining(save.currentRun?.craftQueue, gameNow);
            if (craftLeft > 0) inputs.CraftDoneIn = TimeSpan.FromSeconds(craftLeft);

            if (CyclotronProgress.IsUnlocked(save))
            {
                var left = CyclotronProgress.CooldownLeft(save, gameNow);
                if (left > TimeSpan.Zero) inputs.CyclotronReadyIn = left;
            }

            var boostLeft = BoostLock.Remaining(save, gameNow);
            if (boostLeft > TimeSpan.Zero) inputs.BoostEndedIn = boostLeft;

            if (dailyRewardEnabled && tutorialDone)
                inputs.DailyRewardIn = DailyRewardDelay(save.lastLoginRewardUtc, wallNowUtc);

            if (hasClaimable != null)
            {
                TimeSpan? best = null;
                Consider(AchievementCategory.Daily,   save.dailyResetUtc);
                Consider(AchievementCategory.Weekly,  save.weeklyResetUtc);
                Consider(AchievementCategory.Monthly, save.monthlyResetUtc);
                inputs.RewardsExpiringIn = best;

                void Consider(AchievementCategory c, string resetUtc)
                {
                    if (!hasClaimable(c)) return;
                    var untilReset = Until(resetUtc, wallNowUtc);   // reset deadlines are server (wall) time
                    if (untilReset == null) return;
                    var fire = untilReset.Value - NotificationPlanner.ExpiryWarning;
                    if (fire <= TimeSpan.Zero) return;
                    if (best == null || fire < best.Value) best = fire;
                }
            }

            return inputs;
        }

        /// <summary>
        /// When to remind about the login calendar: at the next UTC midnight if today's reward was claimed,
        /// otherwise a few hours from now (the reward is waiting, and is lost to the day if never claimed).
        /// </summary>
        internal static TimeSpan DailyRewardDelay(string lastClaimUtc, DateTime nowUtc)
        {
            var nextReset = nowUtc.Date.AddDays(1);
            bool claimedToday = TryParseUtc(lastClaimUtc, out var last) && last.Date >= nowUtc.Date;
            if (claimedToday) return nextReset - nowUtc;
            var soon = NotificationPlanner.UnclaimedDailyDelay;
            return nowUtc + soon < nextReset ? soon : nextReset - nowUtc;
        }

        static TimeSpan? Until(string isoUtc, DateTime now)
        {
            if (!TryParseUtc(isoUtc, out var t)) return null;
            var left = t - now;
            return left > TimeSpan.Zero ? left : (TimeSpan?)null;
        }

        static bool TryParseUtc(string iso, out DateTime utc)
        {
            utc = default;
            if (string.IsNullOrEmpty(iso)) return false;
            if (!DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var t)) return false;
            utc = t.Kind == DateTimeKind.Local ? t.ToUniversalTime()
                : DateTime.SpecifyKind(t, DateTimeKind.Utc);
            return true;
        }
    }

    /// <summary>
    /// Pure rules that turn <see cref="NotificationInputs"/> into the list of OS notifications to schedule:
    /// skip near/past times, move anything in quiet hours to the morning, merge production alerts that land
    /// close together, and cap how many fire in any 24 h so the game never spams.
    /// </summary>
    public static class NotificationPlanner
    {
        public static readonly TimeSpan MinDelay            = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan MergeWindow         = TimeSpan.FromMinutes(15);
        public static readonly TimeSpan CapWindow           = TimeSpan.FromHours(24);
        public const int                MaxPerWindow        = 3;
        public const int                QuietStartHour      = 22;   // local
        public const int                QuietEndHour        = 8;    // local
        public static readonly TimeSpan ExpiryWarning       = TimeSpan.FromHours(2);
        public static readonly TimeSpan UnclaimedDailyDelay = TimeSpan.FromHours(4);
        public static readonly TimeSpan Comeback1           = TimeSpan.FromHours(24);
        public static readonly TimeSpan Comeback2           = TimeSpan.FromHours(72);

        /// <summary>Delivery tolerance for anything tied to a game event. 10 min is the tightest Android 12+ allows without an exact-alarm permission.</summary>
        public static readonly TimeSpan PreciseWindow       = TimeSpan.FromMinutes(10);
        /// <summary>Delivery tolerance for reminders nobody waits on: the OS may batch them to save battery.</summary>
        public static readonly TimeSpan RelaxedWindow       = TimeSpan.FromHours(1);

        /// <summary>How late a notification of this kind may arrive. One rule for every platform.</summary>
        public static TimeSpan MaxLatenessFor(NotificationKind kind) => kind switch
        {
            NotificationKind.Comeback24h => RelaxedWindow,
            NotificationKind.Comeback72h => RelaxedWindow,
            _                            => PreciseWindow,
        };

        public static List<PlannedNotification> Plan(NotificationInputs inputs)
        {
            var result = new List<PlannedNotification>();
            if (inputs == null) return result;

            var candidates = new List<PlannedNotification>();
            Add(candidates, inputs, NotificationKind.StorageFull,     inputs.StorageFullIn);
            Add(candidates, inputs, NotificationKind.ResearchDone,    inputs.ResearchDoneIn);
            Add(candidates, inputs, NotificationKind.CraftDone,       inputs.CraftDoneIn);
            Add(candidates, inputs, NotificationKind.CyclotronReady,  inputs.CyclotronReadyIn);
            Add(candidates, inputs, NotificationKind.BoostEnded,      inputs.BoostEndedIn);
            Add(candidates, inputs, NotificationKind.DailyReward,     inputs.DailyRewardIn);
            Add(candidates, inputs, NotificationKind.RewardsExpiring, inputs.RewardsExpiringIn);
            if (inputs.IncludeComeback)
            {
                Add(candidates, inputs, NotificationKind.Comeback24h, Comeback1);
                Add(candidates, inputs, NotificationKind.Comeback72h, Comeback2);
            }

            candidates = MergeProduction(candidates);

            // Highest priority first, then earliest: each one is kept unless the 24 h window around it is full.
            candidates.Sort((a, b) =>
            {
                int p = Priority(a.Kind).CompareTo(Priority(b.Kind));
                return p != 0 ? p : a.FireUtc.CompareTo(b.FireUtc);
            });
            foreach (var c in candidates)
            {
                int near = 0;
                foreach (var kept in result)
                    if ((kept.FireUtc - c.FireUtc).Duration() < CapWindow) near++;
                if (near < MaxPerWindow) result.Add(c);
            }

            result.Sort((a, b) => a.FireUtc.CompareTo(b.FireUtc));
            return result;
        }

        /// <summary>Moves a time inside local quiet hours (22:00-08:00) to the next 08:00 local.</summary>
        public static DateTime ApplyQuietHours(DateTime fireUtc, TimeSpan localOffset)
        {
            var local = fireUtc + localOffset;
            int h = local.Hour;
            if (h >= QuietStartHour)
                return local.Date.AddDays(1).AddHours(QuietEndHour) - localOffset;
            if (h < QuietEndHour)
                return local.Date.AddHours(QuietEndHour) - localOffset;
            return fireUtc;
        }

        static void Add(List<PlannedNotification> list, NotificationInputs inputs, NotificationKind kind,
                        TimeSpan? delay)
        {
            if (delay == null || delay.Value < MinDelay) return;
            var fire = ApplyQuietHours(inputs.NowUtc + delay.Value, inputs.LocalOffset);
            var (title, body) = NotificationTexts.For(kind);
            list.Add(new PlannedNotification(kind, ChannelOf(kind), title, body, fire));
        }

        /// <summary>Production alerts within <see cref="MergeWindow"/> of each other become one, at the latest time.</summary>
        static List<PlannedNotification> MergeProduction(List<PlannedNotification> candidates)
        {
            var production = new List<PlannedNotification>();
            var others     = new List<PlannedNotification>();
            foreach (var c in candidates)
                (c.Channel == NotificationChannel.Production ? production : others).Add(c);
            if (production.Count < 2) return candidates;

            production.Sort((a, b) => a.FireUtc.CompareTo(b.FireUtc));
            var groups = new List<List<PlannedNotification>> { new() { production[0] } };
            for (int i = 1; i < production.Count; i++)
            {
                var group = groups[groups.Count - 1];
                if (production[i].FireUtc - group[0].FireUtc <= MergeWindow) group.Add(production[i]);
                else groups.Add(new List<PlannedNotification> { production[i] });
            }

            foreach (var g in groups)
            {
                if (g.Count == 1) { others.Add(g[0]); continue; }
                var best = g[0];
                foreach (var n in g) if (Priority(n.Kind) < Priority(best.Kind)) best = n;
                var (title, body) = NotificationTexts.For(NotificationKind.ProductionCombined);
                others.Add(new PlannedNotification(best.Kind == NotificationKind.StorageFull
                                                       ? NotificationKind.StorageFull
                                                       : NotificationKind.ProductionCombined,
                                                   NotificationChannel.Production, title, body,
                                                   g[g.Count - 1].FireUtc));
            }
            return others;
        }

        public static NotificationChannel ChannelOf(NotificationKind kind) => kind switch
        {
            NotificationKind.BoostEnded      => NotificationChannel.Boosts,
            NotificationKind.DailyReward     => NotificationChannel.Rewards,
            NotificationKind.RewardsExpiring => NotificationChannel.Rewards,
            NotificationKind.Comeback24h     => NotificationChannel.Reminders,
            NotificationKind.Comeback72h     => NotificationChannel.Reminders,
            _                                => NotificationChannel.Production,
        };

        static int Priority(NotificationKind kind) => kind switch
        {
            NotificationKind.StorageFull        => 0,
            NotificationKind.ProductionCombined => 1,
            NotificationKind.ResearchDone       => 2,
            NotificationKind.CraftDone          => 3,
            NotificationKind.CyclotronReady     => 4,
            NotificationKind.BoostEnded         => 5,
            NotificationKind.RewardsExpiring    => 6,
            NotificationKind.DailyReward        => 7,
            NotificationKind.Comeback24h        => 8,
            _                                   => 9,
        };
    }
}
