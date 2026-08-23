using Verse;

using SolarWeb.Stratum.WorldComponents;

namespace SolarWeb.Stratum.GameComponents;

public class SaveMigrationDriver : GameComponent
{
  public SaveMigrationDriver(Game game)
  {
  }

  public override void LoadedGame()
  {
    Find.World?.GetComponent<SaveMigrationManager>()?.RunPendingMigrations();
  }

  public override void StartedNewGame()
  {
    Find.World?.GetComponent<SaveMigrationManager>()?.MarkCurrent();
  }
}
