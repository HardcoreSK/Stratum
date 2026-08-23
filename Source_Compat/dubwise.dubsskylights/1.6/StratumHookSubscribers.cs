using Verse;
using Dubs_Skylight;
using SolarWeb.Stratum.Hooks;

namespace SolarWeb.Stratum.DubsSkylights;

internal static class StratumHookSubscribers
{
  private static void VetoSmashRoofRemoval(Map map, IntVec3 cell, RoofDef? oldRoof, ref RoofDef? newRoof, ref bool allow)
  {
    if (!allow || newRoof != null || oldRoof == null || map == null) return;
    if (!Stats.RoofStatCache.IsCustomRoof(oldRoof)) return;
    if (!Patches.Building_skyLight_Patch.IsSmashClearing(map, cell)) return;

    allow = false;
  }

  public static void Register()
  {
    MapHookRegistry.RegisterGlobal<MapHookRegistry.BeforeSetRoofHandler>(
      MapHookRegistry.HookId.BeforeSetRoof,
      VetoSmashRoofRemoval
    );

    MapHookRegistry.RegisterGlobal<MapHookRegistry.RoofChangedHandler>(
      MapHookRegistry.HookId.RoofChanged,
      (map, cell, oldRoof, newRoof) =>
      {
        if (map == null) return;
        bool oldIsSkylight = oldRoof != null && Stats.RoofStatCache.IsSkylight(oldRoof);
        bool newIsSkylight = newRoof != null && Stats.RoofStatCache.IsSkylight(newRoof);
        if (oldIsSkylight || newIsSkylight)
        {
          map.GetComponent<MapComp_Skylights>()?.RegenGrid();
        }
      }
    );

    MapHookRegistry.RegisterGlobal<MapHookRegistry.ThermalConductivityHandler>(
      MapHookRegistry.HookId.ThermalConductivity,
      (map, cell, baseVal) =>
      {
        if (map == null || !cell.InBounds(map)) return baseVal;
        var roof = map.roofGrid.RoofAt(cell);
        if (roof == null || !Stats.RoofStatCache.IsSkylight(roof)) return baseVal;

        var thingList = map.thingGrid.ThingsListAt(cell);
        for (int i = 0; i < thingList.Count; i++)
        {
          var thing = thingList[i];
          if (thing is Dubs_Skylight.Building_skyLight)
          {
            return baseVal * 0.75f;
          }
        }
        return baseVal;
      }
    );

    MapHookRegistry.RegisterGlobal<MapHookRegistry.RoofMaxHitPointsHandler>(
      MapHookRegistry.HookId.RoofMaxHitPoints,
      (map, cell, baseVal) =>
      {
        if (map == null || !cell.InBounds(map)) return baseVal;
        var roof = map.roofGrid.RoofAt(cell);
        if (roof == null || !Stats.RoofStatCache.IsSkylight(roof)) return baseVal;

        var thingList = map.thingGrid.ThingsListAt(cell);
        for (int i = 0; i < thingList.Count; i++)
        {
          var thing = thingList[i];
          if (thing is Dubs_Skylight.Building_skyLight)
          {
            return baseVal + 50;
          }
        }
        return baseVal;
      }
    );

    MapHookRegistry.RegisterGlobal<MapHookRegistry.RoofDamageThresholdHandler>(
      MapHookRegistry.HookId.RoofDamageThreshold,
      (map, cell, baseVal) =>
      {
        if (map == null || !cell.InBounds(map)) return baseVal;
        var roof = map.roofGrid.RoofAt(cell);
        if (roof == null || !Stats.RoofStatCache.IsSkylight(roof)) return baseVal;

        var thingList = map.thingGrid.ThingsListAt(cell);
        for (int i = 0; i < thingList.Count; i++)
        {
          var thing = thingList[i];
          if (thing is Dubs_Skylight.Building_skyLight)
          {
            return baseVal + 2.5f;
          }
        }
        return baseVal;
      }
    );

    MapHookRegistry.RegisterGlobal<MapHookRegistry.RoofArmorRatingHandler>(
      MapHookRegistry.HookId.RoofArmorRating,
      (map, cell, baseVal) =>
      {
        if (map == null || !cell.InBounds(map)) return baseVal;
        var roof = map.roofGrid.RoofAt(cell);
        if (roof == null || !Stats.RoofStatCache.IsSkylight(roof)) return baseVal;

        var thingList = map.thingGrid.ThingsListAt(cell);
        for (int i = 0; i < thingList.Count; i++)
        {
          var thing = thingList[i];
          if (thing is Dubs_Skylight.Building_skyLight)
          {
            return baseVal + 0.1f;
          }
        }
        return baseVal;
      }
    );
  }
}
