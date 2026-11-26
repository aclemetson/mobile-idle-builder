using System;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Generated from game_data.json (daily_rewards) by GameDataImporter. Holds the 28-day login reward
    /// calendar. (The rotating daily-challenge pool became Daily achievements on 2026-11-20.)
    /// Lives in Resources so DailyEventService can load it at runtime.
    /// </summary>
    [CreateAssetMenu(fileName = "DailyContent", menuName = "MobileIdleBuilder/Daily Content")]
    public class DailyContentSO : ScriptableObject
    {
        [Tooltip("28-day login calendar, ordered by day (1-based). Index with the 0-based loginStreakIndex.")]
        public DailyRewardEntry[] loginRewards = Array.Empty<DailyRewardEntry>();
    }

    /// <summary>One day of the login reward calendar.</summary>
    [Serializable]
    public class DailyRewardEntry
    {
        public int  day;               // 1-based calendar day
        public int  crystals;          // paid currency (◆)
        public long entropy;           // base currency (e): a flat amount, the floor when entropyMinutes is set
        public int  entropyMinutes;    // pays this many minutes of current production instead, when larger
        public int  prestigeCurrency;  // ascension shards (✦)
    }
}
