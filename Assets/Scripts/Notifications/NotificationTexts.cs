namespace MobileIdleBuilder
{
    /// <summary>
    /// Player-facing notification copy. English only: the game has no localization system yet, so this is the
    /// one table to translate when it does.
    /// </summary>
    public static class NotificationTexts
    {
        public static (string title, string body) For(NotificationKind kind) => kind switch
        {
            NotificationKind.StorageFull        => ("Idle storage is full",
                                                    "Your foundry has stopped banking entropy. Come back to collect and restart it."),
            NotificationKind.ResearchDone       => ("Research complete",
                                                    "A new discovery is ready. Pick your next research."),
            NotificationKind.CraftDone          => ("Crafting finished",
                                                    "Your craft queue is done. The items are in your inventory."),
            NotificationKind.CyclotronReady     => ("Cyclotron charged",
                                                    "The Cyclotron is ready for another kick."),
            NotificationKind.BoostEnded         => ("Boost ended",
                                                    "Your boost has run out. A new one is available."),
            NotificationKind.DailyReward        => ("Daily reward waiting",
                                                    "Your login reward is ready to claim."),
            NotificationKind.RewardsExpiring    => ("Rewards expiring soon",
                                                    "You have achievement rewards to claim before they reset."),
            NotificationKind.Comeback24h        => ("Your foundry misses you",
                                                    "The particles have been piling up. Come back and spend them."),
            NotificationKind.Comeback72h        => ("The lab is quiet",
                                                    "There is new physics waiting. Jump back in and keep building."),
            NotificationKind.ProductionCombined => ("Your foundry needs you",
                                                    "Research, crafting or storage is finished. Come back to keep it running."),
            NotificationKind.DevTest            => ("Test notification",
                                                    "Local notifications are working on this device."),
            _                                   => ("Quantum Foundry", "Something is ready."),
        };

        public static string ChannelName(NotificationChannel c) => c switch
        {
            NotificationChannel.Production => "Production",
            NotificationChannel.Boosts     => "Boosts",
            NotificationChannel.Rewards    => "Rewards",
            NotificationChannel.Reminders  => "Reminders",
            _                              => "General",
        };

        public static string ChannelDescription(NotificationChannel c) => c switch
        {
            NotificationChannel.Production => "Research, crafting, Cyclotron and idle storage",
            NotificationChannel.Boosts     => "When a timed boost ends",
            NotificationChannel.Rewards    => "Daily login and achievement rewards",
            NotificationChannel.Reminders  => "Occasional reminders when you have been away",
            _                              => "",
        };

        public const string PromptTitle   = "Stay in the loop?";
        public const string PromptMessage =
            "We can let you know when idle storage is full, research finishes or a reward is waiting. " +
            "No spam: at most a few a day, never at night.";
        public const string PromptConfirm = "Notify me";
        public const string PromptCancel  = "Not now";
    }
}
