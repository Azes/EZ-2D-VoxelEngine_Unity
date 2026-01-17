
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
/// <summary>
/// Defines a single block layer for flat or uniform world generation.
/// </summary>
public struct BlockLayer
{
    /// <summary>The <see cref="BlockType"/> used for this layer.</summary>
    public BlockType Block;
    /// <summary>The depth (in blocks) of this specific layer.</summary>
    public int LayerDepth;
}


[System.Serializable]
/// <summary>
/// Defines the parameters for a specific mineral/ore generation pattern.
/// The public values are human-readable (1-100) and internally converted to noise parameters.
/// </summary>
public class OreCloud
{
    /// <summary>The type of ore block to be generated.</summary>
    public BlockType oreType;

    // Menschlich lesbare Werte
    /// <summary>
    /// Controls the size and frequency of the large ore regions (Clouds).
    /// Low value (1) = small, rare; High value (100) = large, frequent.
    /// </summary>
    public float cluster = 50;
    /// <summary>
    /// Controls the smoothness and thickness of the veins within the clouds.
    /// Low value (1) = smooth, thick; High value (100) = chaotic, thin.
    /// </summary>
    public float vein = 50;
    /// <summary>
    /// Controls the fine detail and holes within the veins.
    /// Low value (1) = simple; High value (100) = complex/holey.
    /// </summary>
    public float detail = 50;
    /// <summary>
    /// The overall rarity of the ore, affecting both base region limit and final spawn threshold.
    /// Low value (1) = rare; High value (100) = common.
    /// </summary>
    public float rarity = 50;

    /// <summary>
    /// Converts the human-readable 1-100 parameters into the internal floating-point noise scale and limit values.
    /// </summary>
    public void GetInternalValues(
        out float clusterScale,
        out float veinScale,
        out float detailScale,
        out float baseLimit,
        out float spawnLimit)
    {
        float c = Mathf.Clamp(cluster, 1, 100) / 100f;
        float v = Mathf.Clamp(vein, 1, 100) / 100f;
        float d = Mathf.Clamp(detail, 1, 100) / 100f;
        float r = Mathf.Clamp(rarity, 1, 100) / 100f;

        // Cluster Skalierung
        // Grosse Cluster brauchen sehr kleine Noise Skalen
        clusterScale = Mathf.Lerp(0.001f, 0.0003f, c);

        // Vein Skalierung
        veinScale = Mathf.Lerp(0.02f, 0.25f, v);

        // Detail Skalierung
        detailScale = Mathf.Lerp(0.015f, 0.3f, d);

        // Grundlimit je nach Rarity
        baseLimit = Mathf.Lerp(0.95f, 0.5f, r);

        // Spawnthreshold um Variation zu erzeugen
        spawnLimit = Mathf.Lerp(0.001f, 0.8f, r);
    }
}
public class WorldGenerator
{

    private static int seed = -1;




    public static int StringToSeed(string seedString)
    {
        if (string.IsNullOrEmpty(seedString)) return 0;

        int h = 0;
        foreach (char c in seedString)
        {
            h = 31 * h + c;
        }
        // Math.Abs stellt sicher, dass der Seed positiv bleibt
        return Mathf.Abs(h);
    }

    public static int[] GenerateWorldFromSeed(string seedString, int chunkOriginX, int chunkSize, int baseHeight, int maxTerrainHeight)
    {
        seed = StringToSeed(seedString);

        System.Random rng = new System.Random(seed);
        int[] result = new int[chunkSize];

        // Wir erstellen drei verschiedene Skalen für die Abwechslung
        // 1. Kontinental: Bestimmt, ob Berg oder Tal (sehr langsam)
        float continentScale = 0.002f + (float)rng.NextDouble() * 0.002f;
        // 2. Roughness: Bestimmt die Zackigkeit (mittel)
        float roughScale = 0.01f + (float)rng.NextDouble() * 0.01f;
        // 3. Detail: Kleine Unebenheiten (schnell)
        float detailScale = 0.05f;

        for (int i = 0; i < chunkSize; i++)
        {
            float x = chunkOriginX + i;

            // "Biome Noise": Wo sind wir? (0 = Tal, 1 = Gebirge)
            float biomeValue = Mathf.PerlinNoise((x + seed) * continentScale, seed * 0.1f);

            // "Erosion": Macht Täler flacher als Berge
            float intensity = Mathf.Pow(biomeValue, 5.0f);

            // Die eigentlichen Berge
            float mountainNoise = Mathf.PerlinNoise((x + seed) * roughScale, seed * 0.5f);
            float detailNoise = Mathf.PerlinNoise((x + seed) * detailScale, seed * 0.8f);

            // Kombination: Im Tal (intensity niedrig) wirken Berge kaum. 
            // Im Gebirge (intensity hoch) wird der Noise voll addiert.
            float heightOffset = (mountainNoise * 600 + detailNoise * 10f) * intensity;

            // Zusätzliche Grundhöhe durch das Biom (Täler sind tiefer)
            float baseVariation = biomeValue * 60f;



            int finalHeight = Mathf.FloorToInt(baseHeight + baseVariation + heightOffset);
            result[i] = Mathf.Clamp(finalHeight, baseHeight, maxTerrainHeight);
        }

        return result;
    }
    private static void GenerateWater(Chunk c, int waterLevelOffset)
    {
        int water = WorldGen.Instance.GroundHeight + waterLevelOffset;
        for (int x = c.chunkOrigin.x; x < c.chunkOrigin.x + c.chunkSize; x++)
        {
            int h = WorldGen.GetSurfaceHeight(x);
            for (int y = water; y > WorldGen.Instance.GroundHeight; y--)
            {
                if (y < h) continue;

                if (!WorldGen.GetSolidBlock(x, y, false).IsSolid())
                {
                    WorldGen.SetBlockInChunkBackLayerDirect(x, y, BlockType.Dirt, false, true);
                    WorldGen.SetFluidLevelInChunkDirect(x, y, 100);
                }
            }
        }

        for (int x = c.chunkOrigin.x; x < c.chunkOrigin.x + c.chunkSize; x++)
        {
            int h = WorldGen.GetSurfaceHeight(x);
            for (int y = water; y > WorldGen.Instance.GroundHeight; y--)
            {
                if (y < h) continue;

                if (WorldGen.GetSolidBlock(x, y, false).IsSolid())
                {
                    bool nextToWater =
                        WorldGen.GetFluidLevelFromChunkAtWorldPosition(x + 1, y) > 0.1f ||
                        WorldGen.GetFluidLevelFromChunkAtWorldPosition(x - 1, y) > 0.1f ||
                        WorldGen.GetFluidLevelFromChunkAtWorldPosition(x, y + 1) > 0.1f ||
                        WorldGen.GetFluidLevelFromChunkAtWorldPosition(x, y - 1) > 0.1f;

                    if (nextToWater)
                    {
                        WorldGen.SetBlockInChunkFrontLayerDirect(x, y, BlockType.SandStone, false, true);
                    }
                }
            }
        }
    }


    private static void GenerateBlockForChunk(Chunk c)
    {
        int cw = c.chunkSize;
        System.Random rng = new System.Random(seed);
        int p = c.chunkOrigin.x;

        for (int lx = p; lx < p + cw; lx++)
        {
            int index = lx - p;
            int columnHeight = c.heightMap[index];

            int dd = Mathf.RoundToInt(Mathf.Lerp(1, 4, Mathf.PerlinNoise1D((seed + lx) * 1.5f)));

            for (int ly = WorldGen.MaxTerrainHeight; ly >= 0; ly--)
            {

                BlockType front = BlockType.Air;
                BlockType back = BlockType.Air;

                if (ly <= 0)
                {
                    front = BlockType.BlackStone;
                    back = BlockType.BlackStone;
                }
                else if (ly == columnHeight)
                {
                    front = BlockType.Grass;
                    back = BlockType.Dirt;
                }
                else if (ly >= columnHeight - dd && ly < columnHeight)
                {
                    front = BlockType.Dirt;
                    back = BlockType.Dirt;
                }
                else if (ly < columnHeight - dd)
                {
                    front = BlockType.Stone;
                    back = BlockType.Stone;
                }

                // benutze Weltkoordinaten
                WorldGen.SetBlockInChunkFrontLayer(lx, ly, front);
                WorldGen.SetBlockInChunkBackLayer(lx, ly, back);
            }
        }

        // extra variation layers: ebenfalls world coords, iteriere aber chunk-local
        BlockType[] groundStones = new BlockType[]
        {
            BlockType.DarkStone,
            BlockType.DarkDirt,
            BlockType.DarkStone,
            BlockType.DarkDirt,
            BlockType.Stone,
            BlockType.DarkStone,
            BlockType.DarkDirt,
            BlockType.Stone,
            BlockType.DarkStone,
            BlockType.DarkDirt,
            BlockType.Stone,
        };

        int groundCount = groundStones.Length;

        for (int i = 0; i < groundStones.Length; i++)
        {
            float seedOffset = (seed * 1001 * i) * 0.5f;
            float maxt = Mathf.PerlinNoise(seedOffset, seedOffset);

            for (int lx2 = p; lx2 < p + cw; lx2++)
            {
                int index2 = lx2 - p;
                int columnHeight = c.heightMap[index2] - 4;

                for (int ly2 = columnHeight; ly2 > 0; ly2--)
                {
                    float d = i % 2 == 0 ? 1f : -1f;
                    float s = 0.2f;
                    float nx = ((lx2 + seed * 100) * d) * s;
                    float ny = ((ly2 + seed * 100) * d) * s;
                    float tick = Mathf.PerlinNoise(nx, ny);

                    if (tick >= maxt)
                    {
                        float wx = lx2 + seed * 13;
                        float wy = ly2 + seed * 17;

                        float n1 = Mathf.PerlinNoise(wx * 0.05f, wy * 0.05f);
                        float n2 = Mathf.PerlinNoise(wx * 0.11f + 99, wy * 0.11f + 99);
                        float n3 = Mathf.PerlinNoise(wx * 0.21f + 1234, wy * 0.21f + 1234);

                        float mix = (n1 * 0.5f) + (n2 * 0.35f) + (n3 * 0.15f);

                        int index = Mathf.FloorToInt(mix * groundCount);
                        if (index >= groundCount) index = groundCount - 1;

                        WorldGen.SetBlockInChunkFrontLayer(lx2, ly2, groundStones[index]);
                        WorldGen.SetBlockInChunkBackLayer(lx2, ly2, groundStones[index]);
                    }
                }
            }
        }
    }

    private static void GenerateCavesForChunk(Chunk c)
    {
        int cw = c.chunkSize;
        int ox = c.chunkOrigin.x;
        int oy = c.chunkOrigin.y;

        System.Random rng = new System.Random(seed);

        BlockType[] groundStones = new BlockType[]
        {
        BlockType.DarkStone,
        BlockType.DarkDirt,
        BlockType.Stone,
        BlockType.DarkStone,
        BlockType.DarkDirt,
        };

        for (int lx = 0; lx < cw; lx++)
        {
            for (int ly = 0; ly < cw; ly++)
            {
                int wx = ox + lx;
                int wy = oy + ly;

                float caveScale = 0.05f; // Probier Werte zwischen 0.02 und 0.08
                float nx = (wx + seed * 12.3f) * caveScale;
                float ny = (wy + seed * 31.7f) * caveScale;

                float noiseSum = Mathf.PerlinNoise(nx, ny);


                if (noiseSum < .4f)
                {
                    var b = c.GetFrontBlock(wx, wy);


                    if (b.type == BlockType.Grass ||
                        b.type == BlockType.Dirt ||
                        b.type == BlockType.Air ||
                        b.type == BlockType.BlackStone)
                        continue;

                    // zufällige Mischung für BackLayer
                    float mixX = (wx + seed * 37.12f * 91.1f) * 0.15f;
                    float mixY = (wy + seed * 19.44f * 13.8f) * 0.15f;
                    float mix = Mathf.PerlinNoise(mixX, mixY);
                    int index = Mathf.FloorToInt(mix * groundStones.Length);
                    if (index < 0) index = 0;
                    if (index >= groundStones.Length) index = groundStones.Length - 1;

                    c.SetFrontBlock(wx, wy, BlockType.Air);
                    c.SetBackBlock(wx, wy, groundStones[index]);

                }
            }

        }
    }

    private static void GenerateOreForChunk(Chunk c, OreCloud[] ores)
    {
        int cw = c.chunkSize;
        int cx = c.chunkOrigin.x;
        int cy = c.chunkOrigin.y;

        foreach (var ore in ores)
        {
            ore.GetInternalValues(
            out float clusterNoiseScale,
            out float veinNoiseScale,
            out float detailNoiseScale,
            out float baseNoiseLimit,
            out float spawnThreshold
        );

            float baseScale = clusterNoiseScale;       // grobe Verteilung
            float veinScale = veinNoiseScale;          // Form der Ader
            float detailScale = detailNoiseScale;      // Mikrostruktur
            float threshold = spawnThreshold;

            for (int lx = 0; lx < cw; lx++)
            {
                for (int ly = 0; ly < cw; ly++)
                {
                    int wx = cx + lx;
                    int wy = cy + ly;

                    float n1 = Mathf.PerlinNoise((wx + seed) * baseScale, (wy + seed) * baseScale);
                    if (n1 > baseNoiseLimit) continue;

                    float n2 = Mathf.PerlinNoise((wx + seed) * veinScale, (wy + seed) * veinScale);
                    float n3 = Mathf.PerlinNoise((wx + seed) * detailScale, (wy + seed) * detailScale);

                    float final = (n2 * 0.7f) + (n3 * 0.3f);

                    if (final < threshold)
                    {
                        Block b = c.GetFrontBlock(wx, wy);
                        if (b.type == BlockType.Stone || b.type == BlockType.DarkStone || b.type == BlockType.Dirt)
                        {
                            c.SetFrontBlock(wx, wy, ore.oreType);
                        }
                    }
                }
            }
        }
    }


    /// <summary>
    /// **World Generator: CreateChunkBlocks**
    /// <para>Generates the basic block structure (Grass, Dirt, Stone) for the provided chunks based on the generated height map.</para>
    /// <para>This does NOT include caves or ores yet. Used after a <c>Generate</c> or <c>GenerateSimpleNoiseMap</c> call.</para>
    /// </summary>
    /// <param name="chunks">A list of <see cref="Chunk"/> objects to be processed.</param>
    public static void CreateChunkBlocks(List<Chunk> chunks)
    {
        foreach (var chunk in chunks)
            GenerateBlockForChunk(chunk);
    }
    /// <summary>
    /// **World Generator: CreateChunks**
    /// <para>Performs the full block generation pipeline for the provided chunks, including base terrain, caves, ores, and lakes.</para>
    /// <para>Uses default ore settings.</para>
    /// </summary>
    /// <param name="chunks">A list of <see cref="Chunk"/> objects to be processed.</param>
    public static void CreateChunks(List<Chunk> chunks)
    {
        foreach (var chunk in chunks) 
            GenerateBlockForChunk(chunk);


        foreach (var chunk in chunks)
            GenerateCavesForChunk(chunk);

        foreach (var chunk in chunks)
        {
            GenerateOreForChunk(chunk, new OreCloud[]
            {
                new OreCloud { oreType = BlockType.CoalOre,    cluster = 70, vein = 50, detail = 30, rarity =  40},
                new OreCloud { oreType = BlockType.IronOre,    cluster = 50, vein = 40, detail = 35, rarity = 20},
                new OreCloud { oreType = BlockType.GoldOre,    cluster = 30, vein = 20, detail = 20, rarity = 10},
                new OreCloud { oreType = BlockType.DiamondOre, cluster = 15, vein = 10, detail = 15, rarity = 5}
            });

        }

        if (WorldGen.waterActive)
            foreach (var chunk in chunks)
                GenerateWater(chunk, 20);
    }

    public static void CreateSingleChunk(Chunk chunk)
    {

        GenerateBlockForChunk(chunk);
        GenerateCavesForChunk(chunk);

        GenerateOreForChunk(chunk, new OreCloud[]
        {
                new OreCloud { oreType = BlockType.CoalOre,    cluster = 70, vein = 50, detail = 30, rarity =  40},
                new OreCloud { oreType = BlockType.IronOre,    cluster = 50, vein = 40, detail = 35, rarity = 20},
                new OreCloud { oreType = BlockType.GoldOre,    cluster = 30, vein = 20, detail = 20, rarity = 10},
                new OreCloud { oreType = BlockType.DiamondOre, cluster = 15, vein = 10, detail = 15, rarity = 5}
        });

        if (WorldGen.waterActive)
            GenerateWater(chunk, 20);
    }
    public static void CreateSingleChunk(Chunk chunk, OreCloud[] ores)
    {

        GenerateBlockForChunk(chunk);
        GenerateCavesForChunk(chunk);

        GenerateOreForChunk(chunk, ores);

        if (WorldGen.waterActive)
            GenerateWater(chunk, 20);
    }

    /// <summary>
    /// **World Generator: CreateChunks**
    /// <para>Performs the full block generation pipeline for the provided chunks, including base terrain, caves, ores, and lakes.</para>
    /// <para>Allows specifying custom ore distribution using <see cref="OreCloud"/> definitions.</para>
    /// </summary>
    /// <param name="chunks">A list of <see cref="Chunk"/> objects to be processed.</param>
    /// <param name="ores">An array of custom <see cref="OreCloud"/> settings.</param>
    public static void CreateChunks(List<Chunk> chunks, OreCloud[] ores)
    {
        foreach (var chunk in chunks)
            GenerateBlockForChunk(chunk);


        foreach (var chunk in chunks)
            GenerateCavesForChunk(chunk);

        foreach (var chunk in chunks)
            GenerateOreForChunk(chunk, ores);

        if (WorldGen.waterActive)
            foreach (var chunk in chunks)
                GenerateWater(chunk, 20);
    }
}
