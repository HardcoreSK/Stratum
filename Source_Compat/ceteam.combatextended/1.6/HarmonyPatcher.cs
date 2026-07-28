using HarmonyLib;
using Verse;

namespace SolarWeb.Stratum.Patches.CE;

[StaticConstructorOnStartup]
internal static class HarmonyPatcher
{
  static HarmonyPatcher()
  {
    var harmony = new Harmony("com.solarweb.Stratum.CombatExtended");
    harmony.PatchAll();
  }
}
