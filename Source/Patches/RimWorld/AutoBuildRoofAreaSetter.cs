using HarmonyLib;
using RimWorld;
using Verse;

namespace SolarWeb.Stratum.Patches;

/// <summary>
/// Vanilla AutoBuildRoofAreaSetter paints BuildRoof / NoRoof when a room closes. Custom roofs already own those cells;
/// the vanilla write then MarkForDraw on those areas, which flashes the build-roof and remove-roof overlays while a colonist is constructing or deconstructing a custom roof.
/// TryGenerateAreaFor is not the only entry: TryGenerateAreaNow still runs the scan. Both prefixes skip the write. Postfix cannot un-paint in the same frame.
/// </summary>
[HarmonyPatch]
public static class AutoBuildRoofAreaSetter_Patch
{
  [HarmonyPatch(typeof(AutoBuildRoofAreaSetter), nameof(AutoBuildRoofAreaSetter.TryGenerateAreaFor))]
  [HarmonyPrefix]
  public static bool TryGenerateAreaFor_Prefix()
  {
    return false;
  }

  [HarmonyPatch(typeof(AutoBuildRoofAreaSetter), "TryGenerateAreaNow")]
  [HarmonyPrefix]
  public static bool TryGenerateAreaNow_Prefix()
  {
    return false;
  }
}
