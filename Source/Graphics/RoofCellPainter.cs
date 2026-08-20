using System.Collections.Generic;
using UnityEngine;
using Verse;

using SolarWeb.Stratum.MapComponents;
using SolarWeb.Stratum.Stats;

namespace SolarWeb.Stratum.Graphics;

/// <summary>
/// Options controlling how <see cref="RoofCellPainter"/> paints a roof cell.
/// </summary>
/// <remarks>
/// <c>default</c> must reproduce <see cref="CustomRoofsRenderer"/>'s behaviour exactly, so every
/// field is phrased such that its zero value is the current-map default.
/// </remarks>
public struct RoofPaintOptions
{
  /// <summary>
  /// Base render queue for painted materials, or <c>null</c> to keep Stratum's own queues
  /// (2900 cutout / 2901 transparent / 4500 meta overlay). Glass is drawn at base + 1.
  /// </summary>
  public int? RenderQueueOverride;

  /// <summary>When <c>true</c>, skip the damage scratch overlay.</summary>
  public bool SuppressDamageScratches;

  /// <summary>
  /// When <c>true</c>, natural roofs draw as a single flat quad instead of the 3x3 grid whose
  /// outer ring fades into neighbouring rock.
  /// </summary>
  /// <remarks>
  /// That fade is built from a per-vertex alpha ramp on a <c>Map/MetaOverlay</c> material, which
  /// only composites correctly at its native render queue (4500) -- late, after the scene is drawn.
  /// Anywhere the roof has to be re-queued to sit among ordinary geometry, the ramp blends against
  /// a half-drawn framebuffer and produces a shifting bright band along the cell edges.
  ///
  /// Flattening trades a subtle edge blend for order-independent rendering. The blend is there to
  /// soften rock-to-rock transitions when you are standing on that level; a floor down it is not
  /// really perceptible anyway.
  /// </remarks>
  public bool FlattenNaturalRoofEdges;
}

/// <summary>
/// Paints a single Stratum roof cell into a <see cref="MapDrawLayer"/>.
/// </summary>
/// <remarks>
/// Extracted from <see cref="CustomRoofsRenderer"/> so the same rendering can be driven for a map
/// other than the one owning the layer -- specifically, so the MultiFloors compatibility module can
/// paint a lower level's roofs into the upper level's section mesh. Every method here takes the
/// target layer and the source map explicitly rather than relying on <c>this</c> and
/// <c>MapDrawLayer.Map</c>.
///
/// <see cref="MapDrawLayer.GetSubMesh(Material)"/> is public, so painting into a foreign layer is
/// legitimate -- but it must happen between that layer's <c>ClearSubMeshes</c> and
/// <c>FinalizeMesh</c>, i.e. from inside its own <c>Regenerate</c>.
/// </remarks>
public static class RoofCellPainter
{
  private static Material[]? defaultScratchMats;
  private static Material[] DefaultScratchMats =>
    defaultScratchMats ??= [
      MaterialPool.MatFrom(RimWorldTextures.Damage.Scratch1, ShaderDatabase.Cutout),
      MaterialPool.MatFrom(RimWorldTextures.Damage.Scratch2, ShaderDatabase.Cutout),
      MaterialPool.MatFrom(RimWorldTextures.Damage.Scratch3, ShaderDatabase.Cutout)
    ];

  private static Material? fallbackMat;
  public static Material FallbackMat =>
    fallbackMat ??= MaterialPool.MatFrom(RimWorldTextures.Terrain.Surfaces.Concrete, ShaderDatabase.Cutout, new Color(0.5f, 0.5f, 0.5f));

  private static readonly Dictionary<(Material, int), Material> requeuedMaterials = [];

  /// <summary>
  /// Returns <paramref name="src"/> re-queued to <paramref name="queue"/>, or unchanged when no
  /// override is requested. Clones are cached so material identity stays stable and
  /// <see cref="MapDrawLayer.GetSubMesh(Material)"/> keeps deduplicating correctly.
  /// </summary>
  public static Material AtQueue(Material src, int? queue)
  {
    // Pass-through: null in means null out, and every caller sources its material from
    // RoofAtlasManager, which never returns null.
    if (src == null || queue == null || src.renderQueue == queue.Value) return src!;

    var key = (src, queue.Value);
    if (!requeuedMaterials.TryGetValue(key, out var requeued))
    {
      requeued = new Material(src) { renderQueue = queue.Value, name = src.name + "_rq" + queue.Value };
      requeuedMaterials[key] = requeued;
    }
    return requeued;
  }

  private static int? Offset(int? queue, int by) => queue.HasValue ? queue.Value + by : null;

  /// <summary>
  /// Picks the atlas UVs for a cell: the seamless grid slot when the entry tiles, otherwise a
  /// hash-stable flat variant. Returns <c>null</c> when the entry has nothing usable, in which
  /// case the caller should draw nothing.
  /// </summary>
  public static Vector2[]? ResolveUvs(RoofAtlasManager.AtlasEntry entry, IntVec3 c)
  {
    if (entry == null) return null;

    if (entry.IsSeamless && entry.SeamlessGrid != null)
    {
      int col = c.x % entry.GridWidth;
      if (col < 0) col += entry.GridWidth;

      int row = c.z % entry.GridHeight;
      if (row < 0) row += entry.GridHeight;

      // Flattened grid where available -- this resolves once per painted cell, and the tuple-keyed
      // dictionary is the slower path.
      var flat = entry.SeamlessFlat;
      if (flat != null)
      {
        int i = row * entry.GridWidth + col;
        return (uint)i < (uint)flat.Length ? flat[i] : null;
      }

      return entry.SeamlessGrid.TryGetValue((col, row), out var uvs) ? uvs : null;
    }

    return entry.FlatVariants.Count > 0
      ? entry.FlatVariants[Mathf.Abs(c.GetHashCode()) % entry.FlatVariants.Count]
      : null;
  }

  /// <summary>
  /// Paints one roof cell: texture, colour, framed skylight or natural-roof edge blending as
  /// appropriate, plus damage scratches.
  /// </summary>
  public static void PrintRoofCell(MapDrawLayer layer, Map map, IntVec3 c, RoofDef roof,
                                   RoofIntegrityGrid? integrityGrid, float altitude,
                                   in RoofPaintOptions opts = default)
  {
    if (layer == null || map == null || roof == null) return;

    RoofGraphicData? graphicData = null;
    float alpha = 1f;

    if (RoofStatCache.IsCustomRoof(roof))
    {
      graphicData = RoofStatCache.GetGraphicData(roof);
      ThingDef? stuff = integrityGrid?.GetStuff(c);
      Color roofColor = RoofStatCache.GetColor(roof, stuff);

      bool skylight = RoofStatCache.IsSkylight(roof);
      if (skylight) alpha = 1f - RoofStatCache.GetTransparency(roof);
      roofColor.a *= alpha;

      Vector3 center = new(c.x + 0.5f, altitude, c.z + 0.5f);

      if (graphicData != null)
      {
        // Resolved once and reused for UVs and materials both; RoofAtlasManager's string-keyed
        // lookups hash the texture path on every call.
        var entry = graphicData.AtlasEntry;
        var uvs = ResolveUvs(entry, c);

        // A seamless entry with no slot for this cell draws nothing, matching the original.
        if (uvs != null)
        {
          if (roof.isNatural && !opts.FlattenNaturalRoofEdges)
          {
            // MetaOverlay is the only shader that honours both the per-vertex alpha ramp and the
            // vertex colours PrintNaturalRoof relies on -- but only at its native queue 4500,
            // where it composites after the scene. Callers that must re-queue it should set
            // FlattenNaturalRoofEdges instead of swapping the shader.
            PrintNaturalRoof(layer, map, c, RoofAtlasManager.GetMetaOverlay(entry),
                             roofColor, altitude, integrityGrid, uvs);
          }
          else if (roof.isNatural)
          {
            // Flat variant: colour baked into the material, no alpha ramp, so nothing depends on
            // draw order.
            PrintQuad(layer, center, Vector2.one,
                      AtQueue(RoofAtlasManager.GetMaterials(entry, roofColor).cutout, opts.RenderQueueOverride),
                      Color.white, 0f, uvs);
          }
          else if (skylight && graphicData.skylightFrameWidth > 0f)
          {
            PrintFramedSkylight(layer, map, c, roof, graphicData, altitude, stuff, uvs, entry, opts);
          }
          else
          {
            var (cutout, transparent) = RoofAtlasManager.GetMaterials(entry, roofColor);
            Material mat = skylight
              ? AtQueue(transparent, Offset(opts.RenderQueueOverride, 1))
              : AtQueue(cutout, opts.RenderQueueOverride);

            PrintQuad(layer, center, Vector2.one, mat, Color.white, 0f, uvs);
          }
        }
      }
      else
      {
        PrintQuad(layer, center, Vector2.one, AtQueue(FallbackMat, opts.RenderQueueOverride), roofColor, 0f);
      }
    }

    // IsDamaged is a single lookup against the repair set; GetMaxHitPoints below is not (roof grid read,
    // stuff read, lock, hook dispatch), so undamaged cells -- nearly all of them -- must not reach it.
    if (!opts.SuppressDamageScratches && integrityGrid != null && Stratum.Settings.enableRoofDamageScratches
        && integrityGrid.IsDamaged(c))
    {
      short hp = integrityGrid.GetHitPoints(c);
      short maxHp = integrityGrid.GetMaxHitPoints(c);

      if (hp > 0 && hp < maxHp)
      {
        // Scratches sit slightly above the roof surface.
        PrintDamageScratches(layer, c, graphicData, altitude + 0.05f, hp, maxHp, alpha, opts);
      }
    }
  }

  public static void PrintFramedSkylight(MapDrawLayer layer, Map map, IntVec3 c, RoofDef roof, RoofGraphicData gd,
                                         float y, ThingDef? stuff, Vector2[] uv, RoofAtlasManager.AtlasEntry entry,
                                         in RoofPaintOptions opts = default)
  {
    float f = gd.skylightFrameWidth;
    float glassAlpha = 1f - RoofStatCache.GetTransparency(roof);

    Color frameColor = RoofStatCache.GetColor(roof, stuff);
    frameColor.a = 1f;

    Color glassColor = RoofStatCache.GetGlassTint(roof, map, c);
    glassColor.a = glassAlpha;

    Material frameMat = AtQueue(RoofAtlasManager.GetMaterials(entry, frameColor).cutout, opts.RenderQueueOverride);
    Material glassMat = AtQueue(RoofAtlasManager.GetMaterials(entry, glassColor).transparent, Offset(opts.RenderQueueOverride, 1));

    // Only two materials across the nine slices, so resolve their submeshes once instead of letting
    // PrintQuad scan the layer's submesh list nine times.
    LayerSubMesh frameSub = layer.GetSubMesh(frameMat);
    LayerSubMesh glassSub = layer.GetSubMesh(glassMat);

    Vector3 basePos = new(c.x, y, c.z);

    // 9-slice positions (0.0 to 1.0)
    System.ReadOnlySpan<float> p = stackalloc float[] { 0f, f, 1f - f, 1f };

    Vector2 bl = uv[0], tl = uv[1], tr = uv[2], br = uv[3];

    Vector2 GetUv(float px, float pz)
    {
      Vector2 bottom = Vector2.Lerp(bl, br, px);
      Vector2 top = Vector2.Lerp(tl, tr, px);
      return Vector2.Lerp(bottom, top, pz);
    }

    System.Span<Vector2> qUv = stackalloc Vector2[4];

    for (int x = 0; x < 3; x++)
    {
      for (int z = 0; z < 3; z++)
      {
        bool isCenter = (x == 1 && z == 1);
        LayerSubMesh quadSub = isCenter ? glassSub : frameSub;
        if (quadSub == null) continue;

        Vector3 qCenter = basePos + new Vector3((p[x] + p[x + 1]) / 2f, 0, (p[z] + p[z + 1]) / 2f);
        Vector2 qSize = new(p[x + 1] - p[x], p[z + 1] - p[z]);

        qUv[0] = GetUv(p[x], p[z]);         // BL
        qUv[1] = GetUv(p[x], p[z + 1]);     // TL
        qUv[2] = GetUv(p[x + 1], p[z + 1]); // TR
        qUv[3] = GetUv(p[x + 1], p[z]);     // BR

        // Vertex color is white because color is baked into the material
        PrintQuad(quadSub, qCenter, qSize, Color.white, 0f, qUv);
      }
    }
  }

  // Neighbour slots, in the order they are probed below.
  private const int NbW = 0, NbE = 1, NbS = 2, NbN = 3, NbSW = 4, NbNW = 5, NbSE = 6, NbNE = 7;

  public static void PrintNaturalRoof(MapDrawLayer layer, Map map, IntVec3 c, Material mat, Color roofColor,
                                      float altitude, RoofIntegrityGrid? integrityGrid, Vector2[] uv)
  {
    LayerSubMesh subMesh = layer.GetSubMesh(mat);
    if (subMesh == null) return;

    System.Span<bool> hasRoof = stackalloc bool[8];
    System.Span<bool> blends = stackalloc bool[8];
    System.Span<Color> nColor = stackalloc Color[8];

    System.ReadOnlySpan<int> offX = stackalloc int[8] { -1, 1, 0, 0, -1, -1, 1, 1 };
    System.ReadOnlySpan<int> offZ = stackalloc int[8] { 0, 0, -1, 1, -1, 1, -1, 1 };

    var roofGrid = map.roofGrid;
    for (int i = 0; i < 8; i++)
    {
      IntVec3 cell = new(c.x + offX[i], 0, c.z + offZ[i]);
      if (!cell.InBounds(map)) continue;

      RoofDef rf = roofGrid.RoofAt(cell);
      if (rf == null) continue;

      hasRoof[i] = true;
      if (rf.isNatural && RoofStatCache.IsCustomRoof(rf))
      {
        blends[i] = true;
        nColor[i] = RoofStatCache.GetColor(rf, integrityGrid?.GetStuff(cell));
      }
    }

    // 4x4 vertex colour grid, flattened as [x * 4 + z].
    System.Span<Color> colors = stackalloc Color[16];
    for (int i = 0; i < 16; i++) colors[i] = roofColor;

    colors[1] = Blend(roofColor, blends, nColor, NbW, -1, -1, !hasRoof[NbW]);           // [0,1]
    colors[2] = colors[1];                                                              // [0,2]

    colors[13] = Blend(roofColor, blends, nColor, NbE, -1, -1, !hasRoof[NbE]);          // [3,1]
    colors[14] = colors[13];                                                            // [3,2]

    colors[4] = Blend(roofColor, blends, nColor, NbS, -1, -1, !hasRoof[NbS]);           // [1,0]
    colors[8] = colors[4];                                                              // [2,0]

    colors[7] = Blend(roofColor, blends, nColor, NbN, -1, -1, !hasRoof[NbN]);           // [1,3]
    colors[11] = colors[7];                                                             // [2,3]

    colors[0] = Blend(roofColor, blends, nColor, NbW, NbS, NbSW,                        // [0,0]
                      !(hasRoof[NbW] && hasRoof[NbS] && hasRoof[NbSW]));
    colors[3] = Blend(roofColor, blends, nColor, NbW, NbN, NbNW,                        // [0,3]
                      !(hasRoof[NbW] && hasRoof[NbN] && hasRoof[NbNW]));
    colors[12] = Blend(roofColor, blends, nColor, NbE, NbS, NbSE,                       // [3,0]
                       !(hasRoof[NbE] && hasRoof[NbS] && hasRoof[NbSE]));
    colors[15] = Blend(roofColor, blends, nColor, NbE, NbN, NbNE,                       // [3,3]
                       !(hasRoof[NbE] && hasRoof[NbN] && hasRoof[NbNE]));

    System.ReadOnlySpan<float> p = NaturalRamp;

    Vector3 basePos = new(c.x, altitude, c.z);
    Vector2 bl = uv[0], tl = uv[1], tr = uv[2], br = uv[3];

    Vector2 GetUv(float px, float pz)
    {
      Vector2 bottom = Vector2.Lerp(bl, br, px);
      Vector2 top = Vector2.Lerp(tl, tr, px);
      return Vector2.Lerp(bottom, top, pz);
    }

    System.Span<Vector2> qUv = stackalloc Vector2[4];
    System.Span<Color> qColors = stackalloc Color[4];

    for (int x = 0; x < 3; x++)
    {
      for (int z = 0; z < 3; z++)
      {
        Vector3 qCenter = basePos + new Vector3((p[x] + p[x + 1]) / 2f, 0, (p[z] + p[z + 1]) / 2f);
        Vector2 qSize = new(p[x + 1] - p[x], p[z + 1] - p[z]);

        qUv[0] = GetUv(p[x], p[z]);
        qUv[1] = GetUv(p[x], p[z + 1]);
        qUv[2] = GetUv(p[x + 1], p[z + 1]);
        qUv[3] = GetUv(p[x + 1], p[z]);

        qColors[0] = colors[x * 4 + z];
        qColors[1] = colors[x * 4 + z + 1];
        qColors[2] = colors[(x + 1) * 4 + z + 1];
        qColors[3] = colors[(x + 1) * 4 + z];

        PrintQuad(subMesh, qCenter, qSize, roofColor, 0f, qUv, qColors);
      }
    }
  }

  /// <summary>The 3x3 slice ramp for natural roofs: a quarter-cell blend band on each side.</summary>
  private static readonly float[] NaturalRamp = [0f, 0.25f, 0.75f, 1f];

  /// <summary>
  /// Averages <paramref name="self"/> with whichever of the given neighbour slots blend, then zeroes
  /// alpha when the vertex sits on an exposed edge. Slot indices of -1 are skipped, so the same helper
  /// serves both the one-neighbour edge case and the three-neighbour corner case.
  /// </summary>
  private static Color Blend(Color self, System.ReadOnlySpan<bool> blends, System.ReadOnlySpan<Color> nColor,
                             int s1, int s2, int s3, bool fades)
  {
    float r = self.r;
    float g = self.g;
    float b = self.b;
    float a = self.a;
    int count = 1;

    if (s1 >= 0 && blends[s1]) { Color n = nColor[s1]; r += n.r; g += n.g; b += n.b; a += n.a; count++; }
    if (s2 >= 0 && blends[s2]) { Color n = nColor[s2]; r += n.r; g += n.g; b += n.b; a += n.a; count++; }
    if (s3 >= 0 && blends[s3]) { Color n = nColor[s3]; r += n.r; g += n.g; b += n.b; a += n.a; count++; }

    Color avgColor = new(r / count, g / count, b / count, a / count);
    if (fades)
    {
      avgColor.a = 0f;
    }
    return avgColor;
  }

  /// <summary>Appends a single quad to <paramref name="layer"/>'s submesh for <paramref name="mat"/>.</summary>
  /// <remarks>
  /// <see cref="MapDrawLayer.GetSubMesh(Material)"/> is a linear scan over every material the layer has
  /// ever seen, so callers printing several quads with the same material should resolve the submesh once
  /// and use the <see cref="LayerSubMesh"/> overload instead.
  /// </remarks>
  public static void PrintQuad(MapDrawLayer layer, Vector3 center, Vector2 size, Material mat, Color color,
                               float angle = 0f, System.ReadOnlySpan<Vector2> uvArray = default,
                               System.ReadOnlySpan<Color> vertexColors = default)
  {
    LayerSubMesh subMesh = layer.GetSubMesh(mat);
    if (subMesh == null) return;

    PrintQuad(subMesh, center, size, color, angle, uvArray, vertexColors);
  }

  /// <summary>Appends a single quad to an already-resolved submesh.</summary>
  public static void PrintQuad(LayerSubMesh subMesh, Vector3 center, Vector2 size, Color color,
                               float angle = 0f, System.ReadOnlySpan<Vector2> uvArray = default,
                               System.ReadOnlySpan<Color> vertexColors = default)
  {
    if (subMesh == null) return;

    int vCount = subMesh.verts.Count;

    Vector3 v1 = new(-size.x / 2f, 0, -size.y / 2f);
    Vector3 v2 = new(-size.x / 2f, 0, size.y / 2f);
    Vector3 v3 = new(size.x / 2f, 0, size.y / 2f);
    Vector3 v4 = new(size.x / 2f, 0, -size.y / 2f);

    if (angle != 0f)
    {
      Quaternion q = Quaternion.AngleAxis(angle, Vector3.up);
      v1 = q * v1;
      v2 = q * v2;
      v3 = q * v3;
      v4 = q * v4;
    }

    subMesh.verts.Add(v1 + center);
    subMesh.verts.Add(v2 + center);
    subMesh.verts.Add(v3 + center);
    subMesh.verts.Add(v4 + center);

    if (vertexColors.Length >= 4)
    {
      for (int i = 0; i < 4; i++) subMesh.colors.Add(vertexColors[i]);
    }
    else
    {
      Color32 color32 = color;
      for (int i = 0; i < 4; i++) subMesh.colors.Add(color32);
    }

    if (uvArray.Length >= 4)
    {
      subMesh.uvs.Add(new(uvArray[0].x, uvArray[0].y, 0f));
      subMesh.uvs.Add(new(uvArray[1].x, uvArray[1].y, 0f));
      subMesh.uvs.Add(new(uvArray[2].x, uvArray[2].y, 0f));
      subMesh.uvs.Add(new(uvArray[3].x, uvArray[3].y, 0f));
    }
    else
    {
      subMesh.uvs.Add(new(0f, 0f, 0f));
      subMesh.uvs.Add(new(0f, 1f, 0f));
      subMesh.uvs.Add(new(1f, 1f, 0f));
      subMesh.uvs.Add(new(1f, 0f, 0f));
    }

    for (int i = 0; i < 4; i++) subMesh.normals.Add(Vector3.up);

    subMesh.tris.Add(vCount);
    subMesh.tris.Add(vCount + 1);
    subMesh.tris.Add(vCount + 2);
    subMesh.tris.Add(vCount);
    subMesh.tris.Add(vCount + 2);
    subMesh.tris.Add(vCount + 3);
  }

  public static void PrintDamageScratches(MapDrawLayer layer, IntVec3 c, RoofGraphicData? graphicData, float y,
                                          short hp, short maxHp, float alpha, in RoofPaintOptions opts = default)
  {
    float damagePct = 1f - ((float)hp / maxHp);
    int scratchesCount = 0;
    if (damagePct > 0.75f) scratchesCount = 3;
    else if (damagePct > 0.5f) scratchesCount = 2;
    else if (damagePct > 0.1f) scratchesCount = 1;

    if (scratchesCount <= 0) return;

    IList<Material>? scratchMats = graphicData?.damageData?.scratchMats;
    if (scratchMats == null || scratchMats.Count == 0)
    {
      scratchMats = DefaultScratchMats;
    }

    Vector3 center = new(c.x + 0.5f, y, c.z + 0.5f);
    Color scratchColor = new(1f, 1f, 1f, alpha);

    Rand.PushState(c.GetHashCode());
    for (int i = 0; i < scratchesCount; i++)
    {
      Material scratchMat = AtQueue(scratchMats.RandomElement(), Offset(opts.RenderQueueOverride, 2));

      float rot = Rand.Range(0f, 360f);
      float scale = Rand.Range(0.7f, 0.9f);

      PrintQuad(layer, center, new Vector2(scale, scale), scratchMat, scratchColor, rot);
    }
    Rand.PopState();
  }
}
