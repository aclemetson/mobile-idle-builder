using System;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Generated from game_data.json (daily_rewards + daily_challenges) by GameDataImporter.
    /// Holds the 28-day login reward calendar and the rotating daily-challenge pool.
    /// Lives in Resources so DailyEventService can load it at runtime.
    /// </summary>
    [CreateAssetMenu(fileName = "DailyContent", menuName = "MobileIdleBuilder/Daily Content")]
    public class DailyContentSO : ScriptableObject
    {
        [Tooltip("28-day login calendar, ordered by day (1-based). Index with the 0-based loginStreakIndex.")]
        public DailyRewardEntry[] loginRewards = Array.Empty<DailyRewardEntry>();

        [Tooltip("Pool of daily challenges; 3 are drawn deterministically per UTC day.")]
        public DailyChallengeEntry[] challengePool = Array.Empty<DailyChallengeEntry>();
    }

    /// <summary>One day of the login reward calendar.</summary>
    [Serializable]
    public class DailyRewardEntry
    {
        public int  day;               // 1-based calendar day
        public int  crystals;          // paid currency (◆)
        public long entropy;           // base currency (e)
        public int  prestigeCurrency;  // ascension shards (✦)
    }

    /// <summary>One challenge in the daily-challenge pool.</summary>
    [Serializable]
    public class DailyChallengeEntry
    {
        public string id;
        public string description;
        public string trigger;  // matches an AchievementTrigger enum value
        public int    target;   // amount needed to complete
        public int    crystals; // reward on completion
    }
}
