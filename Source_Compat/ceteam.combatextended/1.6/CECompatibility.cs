using System;
using CombatExtended;
using CombatExtended.Compatibility;
using RimWorld;
using Verse;

using SolarWeb.Stratum.MapComponents;
using SolarWeb.Stratum.Utilities;
using SolarWeb.Stratum.Stats;
using SolarWeb.Stratum.Hooks;

namespace SolarWeb.Stratum.Patches.CE;

[StaticConstructorOnStartup]
public static class CECompatibility
{
  [ThreadStatic]
  private static int belowRoofImpactDepth;

  static CECompatibility()
  {
    BlockerRegistry.RegisterImpactSomethingCallback(ImpactSomethingCallback);
    MapHookRegistry.RegisterGlobal<MapHookRegistry.RoofDamageCalculationHandler>(
      MapHookRegistry.HookId.RoofDamageCalculation,
      CalculateCEDamage);
  }

  public static bool CalculateCEDamage(RoofDef roof, ThingDef? stuff, float amount, float penetration, DamageInfo? dinfo, ref float effectiveDamage)
  {
    float ar = RoofStatCache.GetArmorRating(roof, stuff);
    if (penetration > 0f && ar > 0f)
    {
      // We interpret AR as mm RHA equivalent if it's high, or scale it if it's low.
      float effectiveArmor = ar > 2f ? ar : ar * 10f;

      if (penetration > effectiveArmor)
      {
        effectiveDamage = amount * (1f - (effectiveArmor / (penetration * 2f)));
      }
      else
      {
        effectiveDamage = amount * 0.05f;
      }
      return true;
    }
    return false;
  }

  public static bool ImpactSomethingCallback(ProjectileCE projectile, Thing launcher)
  {
    if (projectile.def?.projectile?.flyOverhead ?? false)
    {
      var map = projectile.Map;
      if (map == null) return false;

      var pos = projectile.ExactPosition.ToIntVec3();
      if (!pos.InBounds(map)) return false;

      var roof = map.roofGrid.RoofAt(pos);

      if (roof != null && RoofStatCache.IsCustomRoof(roof))
      {
        var integrityGrid = map.GetComponent<RoofIntegrityGrid>();
        if (integrityGrid != null)
        {
          if (!HasExplosion(projectile))
          {
            ApplyCEDamage(
              integrityGrid,
              map,
              pos,
              projectile.DamageAmount,
              projectile.PenetrationAmount,
              projectile.def.projectile.damageDef,
              projectile.launcher ?? projectile);
          }

          // CE normally resolves overhead impacts at ground level. Move this impact
          // to the roof surface so CE's own explosion, fragments, effects and sound
          // still run. ExplosionCE_AffectCell_Patch applies CE's per-cell falloff
          // and penetration to the roof, and keeps an above-roof blast out of the
          // room below.
          var impactPosition = projectile.ExactPosition;
          impactPosition.y = CE_Utility.GetBoundsFor(pos, roof).max.y;
          projectile.InterceptProjectile(typeof(CECompatibility), impactPosition);

          return true; // Return true to tell CE we handled the impact
        }
      }
    }

    return false;
  }

  internal static bool ExplosionOriginatesBelowRoof => belowRoofImpactDepth > 0;

  internal static void BeginBelowRoofImpact()
  {
    belowRoofImpactDepth++;
  }

  internal static void EndBelowRoofImpact()
  {
    if (belowRoofImpactDepth > 0)
    {
      belowRoofImpactDepth--;
    }
  }

  internal static bool HasExplosion(ProjectileCE projectile)
  {
    if (projectile.traitExplosion is { radius: > 0f }
      || projectile.def?.projectile?.explosionRadius > 0f)
    {
      return true;
    }

    var explosive = projectile.TryGetComp<CompExplosiveCE>()?.props as CompProperties_ExplosiveCE;
    return explosive?.explosiveRadius > 0f;
  }

  internal static void ApplyCEDamage(
    RoofIntegrityGrid grid,
    Map map,
    IntVec3 cell,
    float damage,
    float penetration,
    DamageDef? damageDef,
    Thing instigator)
  {
    if (damageDef == null)
    {
      grid.TakeDamage(cell, damage, penetration);
      return;
    }

    grid.TakeDamage(cell, damage, penetration, new DamageInfo(damageDef, damage));
    if (damageDef.igniteCellChance > 0f || damageDef == DamageDefOf.Flame || damageDef == DamageDefOf.Burn)
    {
      RoofFireUtility.TryIgniteRoofAt(cell, map, instigator, damageDef);
    }
  }
}

