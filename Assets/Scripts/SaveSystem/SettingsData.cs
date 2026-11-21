using System;

namespace MobileIdleBuilder
{
    [Serializable]
    public class SettingsData
    {
        public float masterVolume        = 1f;
        public float sfxVolume           = 1f;
        public float musicVolume         = 1f;
        public int   graphicsQuality     = 2;    // 0=Low 1=Medium 2=High
        public bool  notificationsEnabled = true;
        // Notification opt-in card ("Stay in the loop?"), see NotificationAskPolicy:
        public bool   notificationPromptShown = false; // asked at least once (either answer)
        public string notificationNextAskUtc  = "";    // ISO UTC: ask again on the first startup after this
        public bool   notificationNeverAsk    = false; // "Don't ask again" was ticked
        public bool  showPowerConnections  = false; // map-wide power-connection overlay toggle

        // ── Camera ────────────────────────────────────────────────────────────
        public float panSensitivity = 1f;     // multiplier on map-drag speed (0.5 = slow .. 2 = fast)
        public bool  invertTilt      = false;  // flip the vertical (tilt/pitch) drag direction
    }
}
