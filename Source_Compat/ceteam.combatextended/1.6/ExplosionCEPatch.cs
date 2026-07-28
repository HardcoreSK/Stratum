using System.Collections.Generic;
using System.Linq;
using CombatExtended;
using HarmonyLib;
using Verse;

using SolarWeb.Stratum.MapComponents;
using SolarWeb.Stratum.Stats;

namespace SolarWeb.Stratum.Patches.CE;

/// <summary>
/// An explosive projectile that reaches a roof from below detonates on the
/// underside of that roof. Classifying it as below wall height makes CE retain
/// the room-side affected cells and lets AffectCell damage things below.
/// </summary>
[HarmonyPatch(typeof(GenExplosionCE), nameof(GenExplosionCE.DoExplosion))]
internal static class GenExplosionCE_DoExplosion_Patch
{
  [HarmonyPrefix]
  private static void Prefix(ref float height)
  {
    if (CECompatibility.ExplosionOriginatesBelowRoof)
    {
      height = CollisionVertical.WallCollisionHeight - 0.001f;
    }
  }
}

/// <summary>
/// Restores Stratum roof cells to CE's above-roof explosion surface. CE normally
/// removes every roofed cell from ExplosionCellsToHit when the blast is at roof
/// height, which would otherwise make custom roofs immune to the explosion. (due to they are excluded from being affected by explosion)
/// </summary>
[HarmonyPatch(typeof(ExplosionCE), nameof(ExplosionCE.ExplosionCellsToHit), MethodType.Getter)]
internal static class ExplosionCE_ExplosionCellsToHit_Patch
{
  [HarmonyPostfix]
  private static void Postfix(ExplosionCE __instance, ref IEnumerable<IntVec3> __result)
  {
    var map = __instance.Map;
    if (map == null || __instance.height < CollisionVertical.WallCollisionHeight)
    {
      return;
    }

    var cells = __result?.ToList() ?? new List<IntVec3>();
    var included = new HashSet<IntVec3>(cells);

    foreach (var cell in GenRadial.RadialCellsAround(__instance.Position, __instance.radius, true))
    {
      if (!cell.InBounds(map) || included.Contains(cell))
      {
        continue;
      }

      var roof = map.roofGrid.RoofAt(cell);
      if (roof != null && RoofStatCache.IsCustomRoof(roof))
      {
        included.Add(cell);
        cells.Add(cell);
      }
    }

    __result = cells;
  }
}

/// <summary>
/// Applies CE's own distance falloff and armor penetration to custom roofs.
/// An explosion at or above roof height stops at an intact roof cell instead
/// of also running RimWorld's ground-level AffectCell logic inside the room.
/// </summary>
[HarmonyPatch(typeof(Explosion), "AffectCell")]
internal static class ExplosionCE_AffectCell_Patch
{
  [HarmonyPrefix]
  private static bool Prefix(Explosion __instance, IntVec3 c)
  {
    if (__instance is not ExplosionCE explosionCE)
    {
      return true;
    }

    var map = explosionCE.Map;
    if (map == null || !c.InBounds(map))
    {
      return true;
    }

    var roof = map.roofGrid.RoofAt(c);
    if (roof == null || !RoofStatCache.IsCustomRoof(roof))
    {
      return true;
    }

    var integrity = map.GetComponent<RoofIntegrityGrid>();
    if (integrity == null)
    {
      return true;
    }
    // Damage roof
    CECompatibility.ApplyCEDamage(
      integrity,
      map,
      c,
      explosionCE.GetDamageAmountAtCE(c),
      explosionCE.GetArmorPenetrationAtCE(c),
      explosionCE.damType,
      explosionCE.instigator ?? explosionCE);

    // And if original height is higher than roof, stop iterating items below
    return explosionCE.height < CollisionVertical.WallCollisionHeight;
  }
}
