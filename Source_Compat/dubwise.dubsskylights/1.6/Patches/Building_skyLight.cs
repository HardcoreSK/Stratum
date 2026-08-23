using System.Collections.Generic;
using HarmonyLib;
using Dubs_Skylight;
using Verse;
using UnityEngine;

using SolarWeb.Stratum.MapComponents;
using SolarWeb.Stratum.Stats;

namespace SolarWeb.Stratum.DubsSkylights.Patches;

[HarmonyPatch(typeof(Building_skyLight))]
public static class Building_skyLight_Patch
{
  public readonly struct CellRescale(IntVec3 cell, int oldHP, int oldMaxHP)
  {
    public readonly IntVec3 cell = cell;
    public readonly int oldHP = oldHP;
    public readonly int oldMaxHP = oldMaxHP;
  }

  public sealed class RescaleState(Map map, List<CellRescale> cells)
  {
    public readonly Map map = map;
    public readonly List<CellRescale> cells = cells;
  }

  private static RescaleState? Capture(Building_skyLight building, Map? map)
  {
    if (map == null) return null;
    var integrity = map.GetComponent<RoofIntegrityGrid>();
    if (integrity == null) return null;

    List<CellRescale>? cells = null;
    foreach (IntVec3 cell in building.OccupiedRect())
    {
      if (!cell.InBounds(map)) continue;
      var roof = map.roofGrid.RoofAt(cell);
      if (roof == null || !RoofStatCache.IsSkylight(roof)) continue;

      cells ??= [];
      cells.Add(new CellRescale(cell, integrity.GetHitPoints(cell), integrity.GetMaxHitPoints(cell)));
    }

    return cells == null ? null : new RescaleState(map, cells);
  }

  private static void Apply(RescaleState? state)
  {
    if (state == null) return;
    var integrity = state.map.GetComponent<RoofIntegrityGrid>();
    if (integrity == null) return;

    var solar = state.map.GetComponent<SolarRoofMapComponent>();

    foreach (var rescale in state.cells)
    {
      var roof = state.map.roofGrid.RoofAt(rescale.cell);
      if (roof == null || !RoofStatCache.IsSkylight(roof)) continue;

      int newMaxHP = integrity.GetMaxHitPoints(rescale.cell);
      if (rescale.oldMaxHP <= 0 || newMaxHP <= 0 || newMaxHP == rescale.oldMaxHP) continue;

      solar?.Notify_RoofChanged(rescale.cell, roof);

      integrity.SetHitPoints(rescale.cell,
        (short)Mathf.RoundToInt((float)rescale.oldHP * newMaxHP / rescale.oldMaxHP));
    }
  }

  private static Building_skyLight? smashingFrame;
  private static Map? smashingMap;
  private static CellRect smashingRect;

  public readonly struct SmashGuard(Building_skyLight? frame, Map? map, CellRect rect)
  {
    public readonly Building_skyLight? frame = frame;
    public readonly Map? map = map;
    public readonly CellRect rect = rect;
  }

  internal static bool IsSmashClearing(Map map, IntVec3 cell)
    => smashingFrame is { Spawned: false } && map == smashingMap && smashingRect.Contains(cell);

  [HarmonyPatch(nameof(Building_skyLight.Destroy))]
  [HarmonyPrefix]
  public static void Destroy_Prefix(Building_skyLight __instance, DestroyMode mode, out SmashGuard __state)
  {
    __state = new SmashGuard(smashingFrame, smashingMap, smashingRect);
    if (mode != DestroyMode.KillFinalize) return;

    var map = __instance.Map;
    if (map == null) return;

    smashingFrame = __instance;
    smashingMap = map;
    smashingRect = __instance.OccupiedRect().ExpandedBy(1);
  }

  [HarmonyPatch(nameof(Building_skyLight.Destroy))]
  [HarmonyFinalizer]
  public static void Destroy_Finalizer(SmashGuard __state)
  {
    smashingFrame = __state.frame;
    smashingMap = __state.map;
    smashingRect = __state.rect;
  }

  [HarmonyPatch(nameof(Building_skyLight.SpawnSetup))]
  [HarmonyPrefix]
  public static void SpawnSetup_Prefix(Building_skyLight __instance, Map map, bool rsal, out RescaleState? __state)
  {
    __state = rsal ? null : Capture(__instance, map);
  }

  [HarmonyPatch(nameof(Building_skyLight.SpawnSetup))]
  [HarmonyPostfix]
  public static void SpawnSetup_Postfix(RescaleState? __state)
  {
    Apply(__state);
  }

  [HarmonyPatch(nameof(Building_skyLight.DeSpawn))]
  [HarmonyPrefix]
  public static void DeSpawn_Prefix(Building_skyLight __instance, out RescaleState? __state)
  {
    __state = Capture(__instance, __instance.Map);
  }

  [HarmonyPatch(nameof(Building_skyLight.DeSpawn))]
  [HarmonyPostfix]
  public static void DeSpawn_Postfix(RescaleState? __state)
  {
    Apply(__state);
  }
}
