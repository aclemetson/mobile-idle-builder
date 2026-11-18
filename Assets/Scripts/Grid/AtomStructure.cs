using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// The numbers behind an element's belt visual (<see cref="BeltItemVisuals"/>): which electron shells are
    /// filled, the nucleus colour, and where each shell's ring sits. Pure logic, no scene.
    ///
    /// Approved look (2026-11-18, Blender mockup scripts/belt_items/mockup_atoms.py): a nucleus (a cluster of
    /// proton / neutron beads up to neon, a beaded ball in the element tile's regime colour above), one faint
    /// ring per occupied shell, and the outer shell's electrons as teal beads orbiting on the outer ring.
    /// Noble gases glow, radioactive elements throw decay sparks, metals get a glossier nucleus.
    /// </summary>
    public static class AtomStructure
    {
        /// <summary>Elements up to this Z show their nucleus as individual proton / neutron beads.</summary>
        public const int MaxClusterZ = 10;

        // Aufbau filling order. Exceptions such as Cr / Cu change one electron between subshells, which is
        // invisible at belt size, so they are ignored.
        private static readonly (int n, int capacity)[] Subshells =
        {
            (1, 2), (2, 2), (2, 6), (3, 2), (3, 6), (4, 2), (3, 10), (4, 6), (5, 2), (4, 10), (5, 6), (6, 2),
            (4, 14), (5, 10), (6, 6), (7, 2), (5, 14), (6, 10), (7, 6),
        };

        /// <summary>Electrons in each occupied principal shell, innermost first (H = [1], C = [2, 4], Fe = [2, 8, 14, 2]).</summary>
        public static int[] ShellOccupancy(int z)
        {
            var perShell = new int[7];
            int left = Mathf.Max(0, z), highest = 0;
            foreach (var (n, capacity) in Subshells)
            {
                if (left <= 0) break;
                int take = Mathf.Min(left, capacity);
                perShell[n - 1] += take;
                highest = Mathf.Max(highest, n);
                left -= take;
            }
            var result = new int[highest];
            System.Array.Copy(perShell, result, highest);
            return result;
        }

        /// <summary>Electrons on the outermost shell (the ones drawn as beads).</summary>
        public static int OuterElectrons(int z)
        {
            var shells = ShellOccupancy(z);
            return shells.Length == 0 ? 0 : shells[shells.Length - 1];
        }

        public static bool IsNoble(int z) => z is 2 or 10 or 18 or 36 or 54 or 86 or 118;

        public static bool IsMetal(int z) =>
            z is >= 3 and <= 4 or >= 11 and <= 13 or >= 19 and <= 32 or >= 37 and <= 51 or >= 55 and <= 84
              or >= 87 and <= 118;

        /// <summary>
        /// The element tile's border colour: the nuclear regime that creates the element (matches
        /// <c>ElementIconGenerator</c> and docs/gameplay_loop_data.js).
        /// </summary>
        public static Color RegimeColor(int z)
        {
            if (z <= 1)   return Hex(0xD2A8FF);   // genesis
            if (z <= 26)  return Hex(0x3FB950);   // fusion
            if (z <= 80)  return Hex(0xF0883E);   // fission
            if (z <= 92)  return Hex(0x58A6FF);   // field
            if (z <= 100) return Hex(0xDB61A2);   // breeding
            return Hex(0xE3B341);                 // synthesis
        }

        /// <summary>Neutrons in the nucleus: mass number minus Z (never negative).</summary>
        public static int NeutronCount(int z, int massNumber) => Mathf.Max(0, massNumber - z);

        /// <summary>Radius of the nucleus (light: a bead cluster; heavy: a beaded ball growing a little with Z).</summary>
        public static float NucleusRadius(int z, int neutrons)
        {
            if (z <= MaxClusterZ)
            {
                int count = Mathf.Max(1, z + neutrons);
                return count == 1 ? BeadRadius : BeadRadius * 0.84f * Mathf.Pow(count, 1f / 3f) + BeadRadius;
            }
            return 0.055f + 0.03f * Mathf.Min(1f, z / 92f);
        }

        public const float BeadRadius = 0.029f;

        /// <summary>
        /// Ring radius of shell <paramref name="k"/> (0 = innermost) of <paramref name="shellCount"/>, spread
        /// from just outside the nucleus to the item's edge; more shells push the outer ring slightly wider.
        /// </summary>
        public static float ShellRadius(int k, int shellCount, float nucleusRadius)
        {
            float outer = BeltItemVisuals.ItemSize * (0.42f + 0.06f * Mathf.Min(shellCount, 7) / 7f);
            if (shellCount <= 1) return outer * 0.85f;
            float inner = nucleusRadius + 0.035f;
            return inner + (outer - inner) * (k + 1) / shellCount;
        }

        /// <summary>
        /// Normal of shell <paramref name="k"/>'s ring in the atom's local space. The atom faces the camera
        /// (local -Z points at it), so every ring faces the viewer, tipped up to 15 degrees per shell so the
        /// rings read as 3D orbits and never edge-on. Deterministic per element and shell.
        /// </summary>
        public static Vector3 ShellNormal(int z, int k)
        {
            float tiltX = (Hash01(z * 31 + k * 7 + 1) * 2f - 1f) * 15f;
            float tiltY = (Hash01(z * 17 + k * 13 + 5) * 2f - 1f) * 15f;
            return Quaternion.Euler(tiltX, tiltY, 0f) * Vector3.back;
        }

        /// <summary>A stable pseudo-random value in [0, 1) for an integer key.</summary>
        public static float Hash01(int key)
        {
            unchecked
            {
                uint h = (uint)key * 2654435761u;
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
                return (h & 0xFFFFFF) / (float)0x1000000;
            }
        }

        private static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
