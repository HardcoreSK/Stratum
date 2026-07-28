using System;
using System.Reflection;
using CombatExtended;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

using SolarWeb.Stratum.MapComponents;
using SolarWeb.Stratum.Stats;

namespace SolarWeb.Stratum.Patches.CE;

/// <summary>
/// Records damage when CE's own geometry reports a collision with a Stratum
/// roof. The postfix is intentional: CE remains the single authority for
/// trajectory intersection and projectile impact behavior.
/// </summary>
[HarmonyPatch]
internal static class ProjectileCE_TryCollideWithRoof_Patch
{
  private readonly struct RoofImpactState
  {
    private RoofImpactState(bool beganBelowRoofImpact)
    {
      Integrity = null;
      Map = null;
      Cell = default;
      Roof = null;
      Damage = 0f;
      Penetration = 0f;
      DamageDef = null;
      Instigator = null;
      BeganBelowRoofImpact = beganBelowRoofImpact;
    }

    public RoofImpactState(
      RoofIntegrityGrid integrity,
      Map map,
      IntVec3 cell,
      RoofDef roof,
      float damage,
      float penetration,
      DamageDef? damageDef,
      Thing instigator)
    {
      Integrity = integrity;
      Map = map;
      Cell = cell;
      Roof = roof;
      Damage = damage;
      Penetration = penetration;
      DamageDef = damageDef;
      Instigator = instigator;
      BeganBelowRoofImpact = false;
    }

    public RoofIntegrityGrid? Integrity { get; }
    public Map? Map { get; }
    public IntVec3 Cell { get; }
    public RoofDef? Roof { get; }
    public float Damage { get; }
    public float Penetration { get; }
    public DamageDef? DamageDef { get; }
    public Thing? Instigator { get; }
    public bool BeganBelowRoofImpact { get; }

    public static RoofImpactState BelowRoofExplosion =>
      new(beganBelowRoofImpact: true);

    public bool IsValid =>
      Integrity != null
      && Map != null
      && Roof != null
      && Instigator != null;
  }

  private static MethodBase TargetMethod()
  {
    return AccessTools.Method(
      typeof(ProjectileCE),
      "TryCollideWithRoof",
      [typeof(IntVec3)]);
  }

  [HarmonyPrefix]
  private static void Prefix(
    ProjectileCE __instance,
    IntVec3 cell,
    out RoofImpactState __state)
  {
    __state = default;

    var map = __instance.Map;
    if (map == null || !cell.InBounds(map))
    {
      return;
    }

    var roof = map.roofGrid.RoofAt(cell);
    if (roof == null || !RoofStatCache.IsCustomRoof(roof))
    {
      return;
    }

    // ExplosionCE applies explosive damage per affected cell. Recording the
    // projectile collision as a second direct hit would double-charge the roof.
    if (CECompatibility.HasExplosion(__instance))
    {
      var bounds = CE_Utility.GetBoundsFor(cell, roof);
      bool enteredRoofFromBelow =
        __instance.ExactPosition.y > __instance.LastPos.y
        && __instance.LastPos.y < bounds.min.y;

      if (enteredRoofFromBelow)
      {
        CECompatibility.BeginBelowRoofImpact();
        __state = RoofImpactState.BelowRoofExplosion;
      }
      return;
    }

    var integrity = map.GetComponent<RoofIntegrityGrid>();
    if (integrity == null)
    {
      return;
    }

    __state = new RoofImpactState(
      integrity,
      map,
      cell,
      roof,
      __instance.DamageAmount,
      __instance.PenetrationAmount,
      __instance.def.projectile.damageDef,
      __instance.launcher ?? __instance);
  }

  [HarmonyPostfix]
  private static void Postfix(bool __result, RoofImpactState __state)
  {
    if (!__result || !__state.IsValid)
    {
      return;
    }

    CECompatibility.ApplyCEDamage(
      __state.Integrity!,
      __state.Map!,
      __state.Cell,
      __state.Damage,
      __state.Penetration,
      __state.DamageDef,
      __state.Instigator!);
  }

  [HarmonyFinalizer]
  private static Exception? Finalizer(
    Exception? __exception,
    RoofImpactState __state)
  {
    if (__state.BeganBelowRoofImpact)
    {
      CECompatibility.EndBelowRoofImpact();
    }

    return __exception;
  }
}

/// <summary>
/// CE spawns projectile fragments at the projectile's impact height. Fragments
/// created on an intact roof surface can immediately travel downward and damage
/// things below even though the explosion itself was stopped by the roof.
/// </summary>
[HarmonyPatch(typeof(CompFragments), nameof(CompFragments.Throw))]
internal static class CompFragments_Throw_Patch
{
  private const float SurfaceTolerance = 0.01f;

  [HarmonyPrefix]
  private static bool Prefix(CompFragments __instance, Vector3 pos, Map map)
  {
    if (__instance.parent is not ProjectileCE
      || map == null
      || !pos.ToIntVec3().InBounds(map))
    {
      return true;
    }

    var cell = pos.ToIntVec3();
    var roof = map.roofGrid.RoofAt(cell);
    if (roof == null || !RoofStatCache.IsCustomRoof(roof))
    {
      return true;
    }

    var bounds = CE_Utility.GetBoundsFor(cell, roof);
    bool isRoofSurfaceImpact =
      pos.y >= bounds.min.y - SurfaceTolerance
      && pos.y <= bounds.max.y + SurfaceTolerance;

    return !isRoofSurfaceImpact;
  }
}
