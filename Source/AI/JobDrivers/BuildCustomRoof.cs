using RimWorld;
using SolarWeb.Stratum.DefModExtensions;
using SolarWeb.Stratum.MapComponents;
using SolarWeb.Stratum.Things;
using SolarWeb.Stratum.Utilities;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace SolarWeb.Stratum.AI.JobDrivers;

public class BuildCustomRoof : JobDriver
{
  protected IntVec3 Cell => TargetA.Cell;
  private RoofConstructionTracker? cachedTracker;

  public override bool TryMakePreToilReservations(bool errorOnFailed)
  {
    return pawn.Reserve(TargetA, job, 1, -1, null, errorOnFailed);
  }

  protected override IEnumerable<Toil> MakeNewToils()
  {
    this.FailOn(() => !pawn.CanReach(TargetA, PathEndMode.Touch, Danger.Deadly));
    this.FailOn(() =>
    {
      var frame = Cell.GetFirstThing<RoofFrame>(pawn.Map);
      return frame == null || frame.Faction != pawn.Faction || frame.IsForbidden(pawn);
    });
    this.FailOn(() => !RoofCollapseUtility.WithinRangeOfRoofHolder(Cell, pawn.Map));
    this.FailOn(() => !RoofCollapseUtility.ConnectedToRoofHolder(Cell, pawn.Map, assumeRoofAtRoot: true));

    yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

    var build = ToilMaker.MakeToil("MakeNewToils");

    #region HSK

    cachedTracker = pawn.Map.GetComponent<RoofConstructionTracker>();
    if (cachedTracker != null && cachedTracker.TryGetRecord(Cell, out var rec))
    {
        RoofDef roofDef = rec.roofDef;
        var ext = rec.roofDef.GetModExtension<BuildableRoofExtension>();

        build.PlaySustainerOrSound(ext?.sustainerSound ?? DefOf.SoundDefOf.Interact_ConstructMetal);
        build.PlaySoundAtEnd(ext?.finishSound ?? SoundDefOf.Roof_Finish);

        if (ext?.workEffect != null)
            build.WithEffect(ext.workEffect, TargetIndex.A);
        else
            build.WithEffect(EffecterDefOf.RoofWork, TargetIndex.A);
    }
    #endregion

    build.initAction = () =>
      {
        if (cachedTracker == null || !cachedTracker.TryGetRecord(Cell, out _))
        {
          EndJobWith(JobCondition.Incompletable);
        }
      };
    build.tickAction = () =>
        {
          if (cachedTracker != null && cachedTracker.TryGetRecord(Cell, out var rec))
          {
            pawn.rotationTracker.FaceCell(Cell);
            float work = pawn.GetStatValue(StatDefOf.ConstructionSpeed) * 1.7f;
            rec.workDone += work;
            pawn.skills?.Learn(SkillDefOf.Construction, 0.25f);

            if (rec.workDone >= rec.workTotal)
            {
              if (RoofUtility.FirstBlockingThing(Cell, pawn.Map) != null)
              {
                EndJobWith(JobCondition.Incompletable);
                return;
              }

              RoofDef roofDef = rec.roofDef;
              var ext = roofDef.GetModExtension<BuildableRoofExtension>();

              RoofMaterialUtils.ConsumeMaterialsAt(Cell, pawn.Map, ext);

              cachedTracker.CompleteConstruction(Cell);

              ReadyForNextToil();
            }
          }
          else
          {
            EndJobWith(JobCondition.Incompletable);
          }
        };
    build.defaultCompleteMode = ToilCompleteMode.Never;
    build.activeSkill = () => SkillDefOf.Construction;
    build.handlingFacing = true;

	build.WithProgressBar(TargetIndex.A, () =>
    {
      if (cachedTracker != null && cachedTracker.TryGetRecord(Cell, out var rec))
        return rec.workDone / rec.workTotal;
      return 0f;
    });
    yield return build;
  }
}
