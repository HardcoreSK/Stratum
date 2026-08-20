using RimWorld;
using UnityEngine;
using Verse;

using SolarWeb.Stratum.Stats;
using SolarWeb.Stratum.MapComponents;

namespace SolarWeb.Stratum.Graphics;

[StaticConstructorOnStartup]
public class CustomRoofsRenderer : SectionLayer
{
  public CustomRoofsRenderer(Section section) : base(section)
  {
    relevantChangeTypes = (ulong)MapMeshFlagDefOf.Roofs | (ulong)MapMeshFlagDefOf.FogOfWar;
  }

  public override bool Visible => true;

  private RoofIntegrityGrid? integrityGrid;
  private bool integrityGridResolved;

  public override void DrawLayer()
  {
    if (Find.PlaySettings.showRoofOverlay && base.Map == Find.CurrentMap)
    {
      base.DrawLayer();
    }
  }

  public override void Regenerate()
  {
    ClearSubMeshes(MeshParts.All);

    if (Find.PlaySettings == null || !Find.PlaySettings.showRoofOverlay) return;

    Map map = base.Map;
    if (map == null || map.roofGrid == null || map.fogGrid == null) return;

    if (!integrityGridResolved)
    {
      integrityGrid = map.GetComponent<RoofIntegrityGrid>();
      integrityGridResolved = true;
    }

    if (integrityGrid != null && !integrityGrid.hasScanned)
    {
      integrityGrid.ExecuteScan();
    }

    CellRect cellRect = new(section.botLeft.x, section.botLeft.z, 17, 17);
    cellRect.ClipInsideMap(map);

    bool isCutscene = false;
    CellRect captureBounds = GravshipCapturer.GravshipCaptureBounds;
    if (!GravshipCapturer.IsGravshipRenderInProgress)
    {
      isCutscene = WorldComponent_GravshipController.CutsceneInProgress && map == Find.CurrentMap;
    }

    // Use MapDataOverlay to ensure we draw above the lighting overlay,
    // but leave MetaOverlays available for ghost placement so we don't z-fight.
    float altitude = AltitudeLayer.MapDataOverlay.AltitudeFor();

    var fogGrid = map.fogGrid;
    var roofGrid = map.roofGrid;
    var cellIndices = map.cellIndices;

    for (int z = cellRect.minZ; z <= cellRect.maxZ; z++)
    {
      int index = cellIndices.CellToIndex(cellRect.minX, z);
      for (int x = cellRect.minX; x <= cellRect.maxX; x++, index++)
      {
        if (fogGrid.IsFogged(index)) continue;

        RoofDef roof = roofGrid.RoofAt(index);
        if (roof == null || !RoofStatCache.IsCustomRoof(roof)) continue;

        IntVec3 c = new(x, 0, z);
        if (isCutscene && captureBounds.Contains(c)) continue;

        // Default options reproduce this renderer's original behaviour: Stratum's own render
        // queues, MetaOverlay for natural roofs, damage scratches on.
        RoofCellPainter.PrintRoofCell(this, map, c, roof, integrityGrid, altitude);
      }
    }

    FinalizeMesh(MeshParts.All);
  }
}
