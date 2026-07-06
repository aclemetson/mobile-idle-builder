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
        public bool  showPowerConnections  = false; // map-wide power-connection overlay toggle
    }
}
