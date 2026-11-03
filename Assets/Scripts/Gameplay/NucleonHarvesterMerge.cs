using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Rules for merging two neighbouring Harvesters (one collecting Up Quarks, one Down Quarks) into a
    /// Nucleon Harvester (#313). Pure so the price and geometry are testable; the inspector does the ECS work.
    /// </summary>
    public static class NucleonHarvesterMerge
    {
        public const string ResearchId          = "nucleon_harvesting";
        public const string HarvesterId         = "harvester";
        public const string NucleonHarvesterId  = "nucleon_harvester";
        public const string UpQuarkRecipeId     = "collect_up_quark";
        public const string DownQuarkRecipeId   = "collect_down_quark";
        public const string DefaultRecipeId     = "harvest_proton";

        /// <summary>
        /// The merge fee: a placed Nucleon Harvester's price minus the two Harvesters already paid for, all
        /// after the prestige cost reduction. Two Harvesters plus the fee therefore always cost exactly one
        /// placed Nucleon Harvester, so neither path is cheaper. Never negative.
        /// </summary>
        public static long Fee(BuildingSO nucleonHarvester, BuildingSO harvester, float costReduction)
        {
            if (nucleonHarvester == null || harvester == null) return 0;
            long whole = EntropyWallet.ReducedCost(nucleonHarvester.entropyCost, costReduction);
            long pair  = 2 * EntropyWallet.ReducedCost(harvester.entropyCost, costReduction);
            return System.Math.Max(0L, whole - pair);
        }

        /// <summary>True when one recipe collects Up Quarks and the other Down Quarks (opposing polarity).</summary>
        public static bool OpposingPolarity(string recipeA, string recipeB) =>
            (recipeA == UpQuarkRecipeId && recipeB == DownQuarkRecipeId) ||
            (recipeA == DownQuarkRecipeId && recipeB == UpQuarkRecipeId);

        /// <summary>
        /// Where a 2x1 Nucleon Harvester goes to cover both cells: side by side → anchor at the left cell,
        /// rotation 0; stacked → anchor at the lower cell, rotation 1 (footprint 1x2). False when the cells
        /// are not orthogonal neighbours.
        /// </summary>
        public static bool TryPlacement(Vector2Int a, Vector2Int b, out Vector2Int anchor, out int rotation)
        {
            anchor   = default;
            rotation = 0;
            var d = b - a;
            if (Mathf.Abs(d.x) + Mathf.Abs(d.y) != 1) return false;

            anchor   = new Vector2Int(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y));
            rotation = d.y != 0 ? 1 : 0;
            return true;
        }

        /// <summary>
        /// Of the two orientations that cover the same pair of cells (rotation r and r + 2), the one whose
        /// output lands on a belt, so the merged building keeps feeding a line one of the Harvesters already
        /// fed. Falls back to <paramref name="rotation"/> when neither does.
        /// </summary>
        public static int PreferBeltedRotation(BuildingSO nucleonHarvester, Vector2Int anchor, int rotation,
                                               System.Func<Vector2Int, bool> isBelt)
        {
            if (nucleonHarvester?.ports == null || isBelt == null) return rotation;
            var fp = new Vector2Int(Mathf.Max(1, nucleonHarvester.footprint.x), Mathf.Max(1, nucleonHarvester.footprint.y));
            foreach (int r in new[] { rotation, (rotation + 2) % 4 })
                foreach (var port in nucleonHarvester.ports)
                {
                    if (port.portType != PortType.Output) continue;
                    var (cell, facing) = PortUtils.TransformPort(port, fp, false, r);
                    if (isBelt(TutorialRouteGuide.OutputFront(anchor + cell, facing))) return r;
                }
            return rotation;
        }

        /// <summary>
        /// Upgrade levels carry over at the lower of the two Harvesters' levels (a Nucleon Harvester's rate
        /// ladder matches a Harvester's), so merging upgraded Harvesters doesn't throw the upgrades away.
        /// </summary>
        public static int CarriedLevel(int levelA, int levelB) => Mathf.Max(1, Mathf.Min(levelA, levelB));
    }
}
