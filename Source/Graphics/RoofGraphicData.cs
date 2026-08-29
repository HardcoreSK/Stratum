using UnityEngine;
using Verse;

namespace SolarWeb.Stratum.Graphics;

public class RoofGraphicData
{
  public string texPath = null!;
  public Color color = Color.white;
  public DamageGraphicData? damageData;
  public bool isSeamless;
  public float skylightFrameWidth;
  public RoofEdgeGraphicData? edgeData;
  public RoofEdgeGraphicData? skylightEdgeData;

  private RoofAtlasManager.AtlasEntry? cachedAtlasEntry;

  /// <summary>
  /// The atlas entry for <see cref="texPath"/>, resolved once. Roof painting needs this per cell, and
  /// <see cref="RoofAtlasManager.GetEntry"/> hashes the texture path on every call.
  /// </summary>
  public RoofAtlasManager.AtlasEntry AtlasEntry =>
    cachedAtlasEntry ??= RoofAtlasManager.GetOrCreateEntry(texPath);
}
