using System.Collections.Generic;
using HarmonyLib;
using Verse;
using SolarWeb.Stratum.AI.Designators;
using SolarWeb.Stratum.DefModExtensions;

namespace SolarWeb.Stratum.Patches;

[HarmonyPatch]
public static class DesignationCategoryDef_Patch
{
  [HarmonyPatch(typeof(DesignationCategoryDef), "ResolveDesignators")]
  [HarmonyPostfix]
  public static void ResolveDesignators_Postfix(DesignationCategoryDef __instance)
  {
    foreach (var kvp in BuildableRoofGenerator.RoofToDesignator)
    {
      var roofDef = kvp.Key;
      var designator = kvp.Value;
      var extension = roofDef.GetModExtension<BuildableRoofExtension>();

      if (extension != null && extension.designationCategory == __instance)
      {
        if (!__instance.AllResolvedDesignators.Contains(designator))
        {
          __instance.AllResolvedDesignators.Add(designator);
        }
      }
    }
  }

  // Architect menus draw this list. Delete roofs is omitted unless developer mode is on.
  // Priority.Last runs after other mods replace the enumerable, so the brush is not put back.
  [HarmonyPatch(typeof(DesignationCategoryDef), nameof(DesignationCategoryDef.ResolvedAllowedDesignators), MethodType.Getter)]
  [HarmonyPostfix]
  [HarmonyPriority(Priority.Last)]
  public static void ResolvedAllowedDesignators_Postfix(DesignationCategoryDef __instance, ref IEnumerable<Designator> __result)
  {
    if (Prefs.DevMode)
      return;

    List<Designator> resolved = __instance.AllResolvedDesignators;
    bool hasDelete = false;
    for (int i = 0; i < resolved.Count; i++)
    {
      if (resolved[i] is Designator_AreaDeleteRoof)
      {
        hasDelete = true;
        break;
      }
    }

    if (!hasDelete)
      return;

    List<Designator> kept = new List<Designator>();
    foreach (Designator designator in __result)
    {
      if (designator is Designator_AreaDeleteRoof)
        continue;

      kept.Add(designator);
    }

    __result = kept;
  }
}
