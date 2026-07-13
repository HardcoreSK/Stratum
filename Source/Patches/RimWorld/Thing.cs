using HarmonyLib;
using Verse;

using SolarWeb.Stratum.MapComponents;
using SolarWeb.Stratum.Stats;

namespace SolarWeb.Stratum.Patches.RimWorld;

[HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
public static class Thing_TakeDamage_Patch
{
  public static void Prefix(Thing __instance, ref DamageInfo dinfo)
  {
    if (dinfo.Category != DamageInfo.SourceCategory.Collapse || __instance.Map == null) return;

    var roof = __instance.Map.roofGrid.RoofAt(__instance.Position);
    if (roof == null || !RoofStatCache.IsCustomRoof(roof)) return;

    var integrity = __instance.Map.GetComponent<RoofIntegrityGrid>();
    var stuff = integrity?.GetStuff(__instance.Position);
    float factor = RoofStatCache.GetCollapseDamageFactor(roof, stuff);

    if (factor != 1f)
    {
      dinfo.SetAmount(dinfo.Amount * factor);
    }
  }
}
