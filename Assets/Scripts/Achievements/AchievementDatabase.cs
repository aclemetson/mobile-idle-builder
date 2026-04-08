using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    [CreateAssetMenu(fileName = "AchievementDatabase", menuName = "MobileIdleBuilder/Config/AchievementDatabase")]
    public class AchievementDatabase : ScriptableObject
    {
        [SerializeField] AchievementSO[] achievements;

        Dictionary<string, AchievementSO> _lookup;

        void OnEnable() => BuildLookup();

        void BuildLookup()
        {
            _lookup = new Dictionary<string, AchievementSO>();
            foreach (var a in achievements)
            {
                if (a == null || string.IsNullOrEmpty(a.id)) continue;
                _lookup[a.id] = a;
            }
        }

        public IReadOnlyList<AchievementSO> All => achievements;

        public AchievementSO Get(string id) =>
            _lookup != null && _lookup.TryGetValue(id, out var a) ? a : null;
    }
}
