using HarmonyLib;
using RimWorld;
using SolarWeb.Stratum.Hooks;
using SolarWeb.Stratum.Utilities;
using Verse;
using Verse.Sound;

namespace SolarWeb.Stratum.Patches;

[HarmonyPatch(typeof(PlaySettings))]
public static class PlaySettings_Patch
{
  [HarmonyPatch(nameof(PlaySettings.ExposeData))]
  [HarmonyPostfix]
  public static void ExposeData_Postfix()
  {
    Scribe_Values.Look(ref RoofBuildings.showRoofBuildings, "showRoofBuildings", false);
  }

  [HarmonyPatch("DoMapControls")]
  [HarmonyPostfix]
  public static void DoMapControls_Postfix(WidgetRow row)
  {
    var handlers = MapHookRegistry.GetGlobalHandlers<MapHookRegistry.PlaySettingsDoMapControlsHandler>(MapHookRegistry.HookId.PlaySettingsDoMapControls);
    if (handlers != null)
    {
      for (int i = 0; i < handlers.Count; i++)
      {
        try
        {
          handlers[i](row);
        }
        catch (System.Exception ex)
        {
          StratumLog.Error($"Error in PlaySettingsDoMapControls subscriber: {ex}");
        }
      }
    }

    try
    {
      RoofBuildings.DoMapControls(row);
    }
    catch (System.Exception ex)
    {
      StratumLog.Error($"Error in built-in DoMapControls: {ex}");
    }

    RoofBuildings.DoMapControls(row);

    bool before = Find.PlaySettings.showRoofOverlay;

    if (!SelectedThingUsesFlickKey())
    {
      CheckKeyBindingToggle(
          KeyBindingDefOf.ToggleRoofVisibility,
          ref Find.PlaySettings.showRoofOverlay);
    }

    if (Find.CurrentMap != null && before != RoofBuildings.showRoofBuildings)
    {
      RoofBuildings.DirtyAllRoofBuildingCells(Find.CurrentMap);
    }

  }

  private static void CheckKeyBindingToggle(KeyBindingDef keyBinding, ref bool value)
  {
      if (!keyBinding.KeyDownEvent)
        return;
      value = !value;
      if (value)
        SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
      else
        SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
  }

  private static bool SelectedThingUsesFlickKey()
  {
      Thing thing = Find.Selector.SingleSelectedThing;
      if (thing == null)
          return false;

      return thing.TryGetComp<CompFlickable>() != null;
  }

}
