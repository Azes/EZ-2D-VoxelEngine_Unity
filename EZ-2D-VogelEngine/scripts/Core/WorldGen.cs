using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

public enum lightmodes
{
    PixelLight,
    SmoothLight
}
public struct BlockCastHit
{
    public bool Hit;
    public Block Block;
    public Block beforBlock;
    public Vector2 Position;
    public float Distance;
}


public struct RaycastBlockData
{
    public float distance;
    public Vector2Int hitPoint;
    public Vector2 hitPointRaw;
    public Vector2 startPointraw;
    public Vector2Int startpoint;

    public List<Vector2Int> passBlocks;

}
public class WorldGen : MonoBehaviour
{
    [Tooltip("If checked, enables detailed debug logging output.")]
    public bool Debuglog;

    public static WorldGen Instance { get; private set; }

    [Header("Chunk Settings")]
    [Tooltip("Prefab used for instantiating new chunks.")]
    public GameObject Chunk;
    [Tooltip("The number of chunks generated along the world's X-axis.")]
    public int ChunkXCount;
    [Tooltip("The number of chunks generated along the world's Y-axis.")]
    public int ChunkYCount = 1;
    [Tooltip("Defines the size (width and height) of a single chunk in blocks. Default is 32x32 blocks.")]
    public int cSize = 32;

    [Header("World Generator")]
    [Tooltip("The average Y-coordinate for the generated surface/ground level.")]
    public int GroundHeight;
    [Tooltip("The seed used for world generation (ensures reproducibility).")]
    public string WorldSeed;
   


    [Header("Water Simulation")]
    [Tooltip("If checked, the fluid (water) simulation system is enabled.")]
    public bool ActivateWater = true;

    [Header("Player and Camera")]
    [Tooltip("Reference to the player controller object in the scene.")]
    public PlayerController Player;


    [Header("Light Settings")]
    [Tooltip("If checked, all lighting calculations will be disabled.")]
    public bool NoLight;
    [Tooltip("Specifies the type of lighting calculation to use (e.g., Smooth Light).")]
    public lightmodes LightType = lightmodes.SmoothLight;

    [Header("Performance Settings")]
    [Tooltip("Extra radius (in chunks) around the camera view where simulations (Water/Light/etc.) are processed. Total Simulation Area = Visible Chunks + SimulationSize.")]
    public int SimulatioansSize = 2;
    [Tooltip("Defines the size (in chunks) of a world region, primarily used for grouping and culling. Low size creates more regions.")]
    public int RegionSize = 8;
    private Vector2 OldCamPos;

    public ComputeShader compute;
    public static int kernel;
    public static bool DB;
    public static int chunkSize;
    public static int MaxTerrainHeight;
    public static bool waterActive;
    public static Vector2Int GLMsize;
    private static Dictionary<long, Chunk> ChunkList = new Dictionary<long, Chunk>();
    private static List<Chunk> LoadetChunkList = new List<Chunk>(); 
    private Camera cam;
    private static bool StartGenerate;


    private static List<Action> ChunkUpdates = new List<Action>();
    private static List<Action> SimulationUpdates = new List<Action>();
    private static List<Action> LightUpdates = new List<Action>();

    public static void AddChunkUpdate(Action U) => ChunkUpdates.Add(U);
    public static void AddSimluationUpdate(Action U) => SimulationUpdates.Add(U);
    public static void AddLightUpdate(Action U) => LightUpdates.Add(U);


    private static IEnumerator ExecuteUpdates()
    {
        while (true)
        {
            ChunkUpdates.ForEach(c => c.Invoke());
            ChunkUpdates.Clear();

            yield return new WaitForEndOfFrame();

            SimulationUpdates.ForEach(c => c.Invoke());
            SimulationUpdates.Clear();

            yield return new WaitForEndOfFrame();

            LightUpdates.ForEach(c => c.Invoke());
            LightUpdates.Clear();

            yield return null;

        }
    }

    private void OnDrawGizmos()
    {
        if (!Debuglog)
            return;

        if (Application.isPlaying)
        {
            LightSystem.DrawRegionGizmos();
            return;
        }

        if (ChunkXCount <= 0 || ChunkYCount <= 0)
            return;

        Gizmos.color = Color.yellow;

        for (int x = 0; x < ChunkXCount; x++)
        {
            for (int y = 0; y < ChunkYCount; y++)
            {
                float cx = x * cSize + cSize * 0.5f;
                float cy = y * cSize + cSize * 0.5f;

                Vector3 center = new Vector3(cx, cy, 0);
                Vector3 size = new Vector3(cSize, cSize, 0);

                Gizmos.DrawWireCube(center, size);
            }
        }


    }

    private void Awake()
    {
        Instance = this;

        QualitySettings.vSyncCount = 0; // VSync deaktivieren
        Application.targetFrameRate = -1; // keine Framerate-Begrenzung

        kernel = compute.FindKernel("CSLight");

        cam = Camera.main;

        OldCamPos = cam.transform.position;
        chunkSize = cSize;
        waterActive = ActivateWater;

        GLMsize = new Vector2Int((chunkSize * ChunkXCount), (chunkSize * ChunkYCount));
        MaxTerrainHeight = chunkSize * ChunkYCount - 6;

        DB = Debuglog;

    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    public void Start()
    {
        StartCoroutine(ExecuteUpdates());

       //optional for Spawn Player
       if (Player != null)
       {
           Vector2 pos = GetSurfacePosition(Player.transform.position);
           Player.transform.position = new Vector2(pos.x, pos.y + (Player.transform.localScale.y * .5f + 1f));
      
       }

    }


    // _,.--**##   User Funktions   ##**--.,_ \\

   

    public static int GetSurfaceHeight(int worldX)
    {
        for (int y = GLMsize.y - 1; y >= 0; y-= chunkSize)
        {
            Chunk c = GetChunkAtWorldPos(worldX, y);
            if (c == null)
                continue;
            
                int i = worldX - c.chunkOrigin.x;

            if (i < 0 || i >= c.heightMap.Length)
                continue;
            
            int sy = c.heightMap[i];

            if (sy > 0)
                return sy;
            
        }

        return Instance.GroundHeight;
    }

    /// <summary>
    /// **WorldGen: GetSurfacePosition**
    /// <para>Retrieves the **surface height** for a given world space position.</para>
    /// <para>**Note:** This function relies solely on the height data generated by the **WorldGenerator**.</para>
    /// </summary>
    /// <param name="worldPos">The world space position (Vector2) to check.</param>
    /// <returns>The spawn height position (Vector2) from the **WorldGenerator** heights.</returns>
    public static Vector2 GetSurfacePosition(Vector2 worldPos)
    {
        var hit = BlockDirCast(new Vector2(worldPos.x, GLMsize.y), Vector2.down, 10000000);
        if (hit.Hit)
        {
            var pos = hit.Position;
            pos.y += 1f;
            return pos;
        }

        return worldPos;
    }

    /// <summary>
    /// **WorldGen: GetChunkAtWorldPos**
    /// <para>Retrieves the specific **Chunk** object located at the given world coordinates.</para>
    /// </summary>
    /// <param name="worldX">World position X coordinate.</param>
    /// <param name="worldY">World position Y coordinate.</param>
    /// <returns>The found **Chunk** object, or **null** if no chunk exists at that position or the position is out of bounds.</returns>
    public static Chunk GetChunkAtWorldPos(int worldX, int worldY)
    {
        // Schneller Bounds-Check
        if (worldX < 0 || worldY < 0 || worldX >= GLMsize.x || worldY >= GLMsize.y)
            return null;

        // Chunk-Koordinaten berechnen
        int cx = (worldX / chunkSize) * chunkSize;
        int cy = (worldY / chunkSize) * chunkSize;

        // Key generieren
        long key = ((long)cx << 32) | (uint)cy;

        // Schneller Zugriff: TryGetValue ist effizienter als ContainsKey + Indexer
        if (ChunkList.TryGetValue(key, out Chunk chunk))
        {
            return chunk;
        }

        return null;
    }
    /// <summary>
    /// **WorldGen: isSolidBlock**
    /// <para>Checks if the block in the **Front Layer** at the specified world position is **solid**.</para>
    /// <para>Automatically finds the correct chunk and block coordinates.</para>
    /// </summary>
    /// <param name="x">World Position X coordinate.</param>
    /// <param name="y">World Position Y coordinate.</param>
    /// <returns>**false** if the block is not solid or if the corresponding chunk is null.</returns>
    public static bool isSolidBlock(int x, int y)
    {
        Chunk c = GetChunkAtWorldPos(x, y);
        if (c == null) return false;


        return c.isSolidBlockFront(x, y);
    }
    /// <summary>
    /// **WorldGen: isSolidBlock** (Overload)
    /// <para>Checks if the block in the **Front Layer** within the specified **Chunk** is **solid**.</para>
    /// </summary>
    /// <param name="c">The Chunk object to perform the check on.</param>
    /// <param name="x">World position X coordinate.</param>
    /// <param name="y">World position Y coordinate.</param>
    /// <returns>**false** if the chunk is null or the block is not solid.</returns>
    public static bool isSolidBlock(Chunk c, int x, int y)
    {
        if (c == null) return false;

        return c.isSolidBlockFront(x, y);
    }

    /// <summary>
    /// **WorldGen: isSolidBlockBack**
    /// <para>Checks if the block in the **Back Layer** at the specified world position is **solid**.</para>
    /// <para>Automatically retrieves the correct chunk based on world coordinates.</para>
    /// </summary>
    /// <param name="x">World position X coordinate.</param>
    /// <param name="y">World position Y coordinate.</param>
    /// <returns>**false** if the block is not solid or if the corresponding chunk is null.</returns>
    public static bool isSolidBlockBack(int x, int y)
    {
        Chunk c = GetChunkAtWorldPos(x, y);
        if (c == null) return false;

        return c.isSolidBlockBack(x, y);
    }
    /// <summary>
    /// **WorldGen: isSolidBlockBack** (Overload)
    /// <para>Checks if the block in the **Back Layer** within the specified **Chunk** is **solid**.</para>
    /// </summary>
    /// <param name="c">The Chunk object to perform the check on.</param>
    /// <param name="x">World position X coordinate.</param>
    /// <param name="y">World position Y coordinate.</param>
    /// <returns>**false** if the chunk is null or the block is not solid.</returns>
    public static bool isSolidBlockBack(Chunk c, int x, int y)
    {
        if (c == null) return false;


        return c.GetBackBlock(x, y).IsSolid();
    }

    /// <summary>
    /// **WorldGen: SetBlockInChunkFrontLayer**
    /// <para>Sets a block in the chunk's **Front Layer** at the specified world position.</para>
    /// <para>The target chunk is automatically retrieved by the world position.</para>
    /// <para>**IMPORTANT:** This function only marks the chunk as dirty for an update in the next frame's main **Update** loop.</para>
    /// <para>**DO NOT** use this for immediate runtime changes (e.g., harvesting) unless you intend to set an extreme amount of blocks,</para>
    /// <para>as direct updates might be costly.</para>
    /// </summary>
    /// <param name="worldX">World position X coordinate.</param>
    /// <param name="worldY">World position Y coordinate.</param>
    /// <param name="blockType">The <see cref="BlockType"/> to set.</param>
    /// <param name="updateFluid">If <c>true</c> and Water Simulation is active, triggers a fluid update at the position.</param>
    public static void SetBlockInChunkFrontLayer(int worldX, int worldY, BlockType blockType, bool updateFluid = false, bool noLightUpdate = false)
    {
        Chunk c = GetChunkAtWorldPos(worldX, worldY);
        if (c == null)
            return;


        c.SetFrontBlock(worldX, worldY, blockType);

        if (waterActive && updateFluid)
        {
            c.SetFluidAt(worldX, worldY, 0);

            FluidSystem.FluidDirty = true;
            FluidSystem.updateChange = true;

            FluidSystem.AddActiveNext(worldX, worldY);
            FluidSystem.AddActiveNext(worldX + 1, worldY);
            FluidSystem.AddActiveNext(worldX - 1, worldY);
            FluidSystem.AddActiveNext(worldX, worldY + 1);
        }

 
        if (!noLightUpdate) AddLightUpdate(() => LightSystem.SetRegionDirty(worldX, worldY));

    }
    /// <summary>
    /// **WorldGen: SetBlockInChunkFrontLayerDirect**
    /// <para>Sets a block in the chunk's **Front Layer** and forces an **immediate** update of the chunk.</para>
    /// <para>The target chunk is automatically retrieved by the world position.</para>
    /// <para>**RECOMMENDED USE:** Use this function for hand-placed or runtime block changes,</para>
    /// <para>such as for harvesting or player-driven placement, where an immediate visual change is required.</para>
    /// </summary>
    /// <param name="worldX">World position X coordinate.</param> 
    /// <param name="worldY">World position Y coordinate.</param>
    /// <param name="blockType">The <see cref="BlockType"/> to set.</param>
    /// <param name="updateFluid">If <c>true</c> and Water Simulation is active, triggers a fluid update at the position.</param>
    public static void SetBlockInChunkFrontLayerDirect(int worldX, int worldY, BlockType blockType, bool updateFluid = false,bool noLightUpdate = false)
    {
        Chunk c = GetChunkAtWorldPos(worldX, worldY);
        if (c == null)
            return;
        
        c.SetFrontBlockDirect(worldX, worldY, blockType);

        if (waterActive && updateFluid)
        {
            c.SetFluidAtDirect(worldX, worldY, 0);

            FluidSystem.FluidDirty = true; 
            FluidSystem.updateChange = true;
            FluidSystem.AddActiveNext(worldX, worldY);
            FluidSystem.AddActiveNext(worldX + 1, worldY);
            FluidSystem.AddActiveNext(worldX - 1, worldY);
            FluidSystem.AddActiveNext(worldX, worldY + 1);
        }

        if (!noLightUpdate) AddLightUpdate(() => LightSystem.SetRegionDirty(worldX, worldY));

    }
    /// <summary>
    /// **WorldGen: SetBlockInChunkBackLayer**
    /// <para>Sets a block in the chunk's **Back Layer** at the specified world position.</para>
    /// <para>The target chunk is automatically retrieved by the world position.</para>
    /// <para>**IMPORTANT:** This function only marks the chunk as dirty for an update in the next frame's main **Update** loop.</para>
    /// <para>**DO NOT** use this for immediate runtime changes (e.g., harvesting) unless you intend to set an extreme amount of blocks,</para>
    /// <para>as direct updates might be costly.</para>
    /// </summary>
    /// <param name="worldX">World position X coordinate.</param>
    /// <param name="worldY">World position Y coordinate.</param>
    /// <param name="blockType">The <see cref="BlockType"/> to set.</param>
    /// <param name="updateFluid">If <c>true</c> and Water Simulation is active, triggers a fluid update at the position.</param>
    public static void SetBlockInChunkBackLayer(int worldX, int worldY, BlockType blockType, bool updateFluid = false, bool noLightUpdate = false)
    {
        Chunk c = GetChunkAtWorldPos(worldX, worldY);
        if (c == null) return;

        c.SetBackBlock(worldX, worldY, blockType);

        if (waterActive && updateFluid)
        {
            FluidSystem.FluidDirty = true;
            FluidSystem.updateChange = true;
           c.ActivateFluidAt(worldX, worldY, true);
            FluidSystem.AddActiveNext(worldX, worldY);
            FluidSystem.AddActiveNext(worldX + 1, worldY);
            FluidSystem.AddActiveNext(worldX - 1, worldY);
            FluidSystem.AddActiveNext(worldX, worldY + 1);
        }
        if (!noLightUpdate) AddLightUpdate(() => LightSystem.SetRegionDirty(worldX, worldY));

    }

    /// <summary>
    /// **WorldGen: SetBlockInChunkBackLayerDirect**
    /// <para>Sets a block in the chunk's **Back Layer** and forces an **immediate** update of the chunk.</para>
    /// <para>The target chunk is automatically retrieved by the world position.</para>
    /// <para>**RECOMMENDED USE:** Use this function for hand-placed or runtime block changes,</para>
    /// <para>such as for harvesting or player-driven placement, where an immediate visual change is required.</para>
    /// </summary>
    /// <param name="worldX">World position X coordinate.</param> 
    /// <param name="worldY">World position Y coordinate.</param>
    /// <param name="blockType">The <see cref="BlockType"/> to set.</param>
    /// <param name="updateFluid">If <c>true</c> and Water Simulation is active, triggers a fluid update at the position.</param>
    public static void SetBlockInChunkBackLayerDirect(int worldX, int worldY, BlockType blockType, bool updateFluid = false, bool noLightUpdate = false)
    {
        Chunk c = GetChunkAtWorldPos(worldX, worldY);
        if (c == null) return;

        c.SetBackBlockDirect(worldX, worldY, blockType);

        if (waterActive && updateFluid)
        {
            FluidSystem.FluidDirty = true;
            FluidSystem.updateChange = true;
            c.ActivateFluidAt(worldX, worldY, true);

            FluidSystem.AddActiveNext(worldX, worldY);
            FluidSystem.AddActiveNext(worldX + 1, worldY);
            FluidSystem.AddActiveNext(worldX - 1, worldY);
            FluidSystem.AddActiveNext(worldX, worldY + 1);
        }

        if (!noLightUpdate) AddLightUpdate(() => LightSystem.SetRegionDirty(worldX, worldY));

    }

    /// <summary>
    /// **WorldGen: SetFluidLevelInChunk**
    /// <para>Sets the water/fluid level (0.0 to 1.0) for the block at the specified world position.</para>
    /// </summary>
    /// <param name="worldX">World position X coordinate.</param>
    /// <param name="worldY">World position Y coordinate.</param>
    /// <param name="level">The water level (float) to set, ranging from 0.0 (empty) to 1.0 (full).</param>
    public static void SetFluidLevelInChunk(int worldX, int worldY, int level)
    {
        Chunk c = GetChunkAtWorldPos(worldX, worldY);
        if (c == null) return;

        level = Mathf.Clamp(level, 0, 100);
        c.SetFluidAt(worldX, worldY, level);


        AddLightUpdate(() => LightSystem.SetSingleRegionDirty(worldX, worldY));
    }

    public static void SetFluidLevelInChunksBuffer(int worldX, int worldY, int level)
    {
        Chunk c = GetChunkAtWorldPos(worldX, worldY);
        if (c == null) return;

        level = Mathf.Clamp(level, 0, 100);

        c.SetFluidAtinBuffer(worldX, worldY, level);

    }
    public static void SetFluidLevelInChunkDirect(int worldX, int worldY, int level, bool a = false)
    {
        Chunk c = GetChunkAtWorldPos(worldX, worldY);
        if (c == null) return;
        level = Mathf.Clamp(level, 0, 100);
        c.SetFluidAtDirect(worldX, worldY, level, a);

        AddLightUpdate(() => LightSystem.SetSingleRegionDirty(worldX, worldY));
    }
  
    /// <summary>
    /// **WorldGen: GetBlockFromChunkFrontLayer**
    /// <para>Retrieves the **Block** object from the chunk's **Front Layer** at the specified world position.</para>
    /// </summary>
    /// <param name="worldX">World position X coordinate.</param>
    /// <param name="worldY">World position Y coordinate.</param>
    /// <returns>The <see cref="Block"/> object, or an **Air Block at (0, 0)** if the corresponding chunk is null.</returns>
    public static Block GetBlockFromChunkFrontLayer(int worldX, int worldY)
    {
        Chunk c = GetChunkAtWorldPos(worldX, worldY);
        if (c == null) return new Block(BlockType.None, 0, 0);

        return c.GetFrontBlock(worldX, worldY);
    }
    /// <summary>
    /// **WorldGen: GetBlockFromChunkBacktLayer**
    /// <para>Retrieves the **Block** object from the chunk's **Back Layer** at the specified world position.</para>
    /// </summary>
    /// <param name="worldX">World position X coordinate.</param>
    /// <param name="worldY">World position Y coordinate.</param>
    /// <returns>The <see cref="Block"/> object, or an **Air Block at (0, 0)** if the corresponding chunk is null.</returns>
    public static Block GetBlockFromChunkBacktLayer(int worldX, int worldY)
    {
        Chunk c = GetChunkAtWorldPos(worldX, worldY);
        if (c == null) return new Block(BlockType.None, 0, 0);

        return c.GetBackBlock(worldX, worldY);
    }
    /// <summary>
    /// **WorldGen: CheckChunkAABB**
    /// <para>Performs an Axis-Aligned Bounding Box (AABB) collision check on the chunk containing the <c>checkpos</c>.</para>
    /// </summary>
    /// <param name="checkpos">The center world coordinates for the check (e.g., mouse position). The correct chunk is automatically retrieved.</param>
    /// <param name="checkSize">The size/extent of the check area. Use <c>Vector2Int.zero</c> or <c>Vector2Int.one</c> for a single block check.</param>
    /// <returns>**false** if the chunk at the check position is null, or if the AABB check fails.</returns>
    public static bool CheckChunkAABB(Vector2Int checkpos, Vector2Int checkSize)
    {
        Chunk c = GetChunkAtWorldPos(checkpos.x, checkpos.y);
        if (c == null) return false;

        return c.AABB(checkpos, checkSize);

    }
    /// <summary>
    /// **WorldGen: CheckChunkAABB** (Overload)
    /// <para>Performs an Axis-Aligned Bounding Box (AABB) collision check on a given **Chunk**.</para>
    /// </summary>
    /// <param name="c">The Chunk object to perform the check on.</param>
    /// <param name="checkpos">The center world coordinates for the check.</param>
    /// <param name="checkSize">The size/extent of the check area.</param>
    /// <returns>**false** if the provided chunk is null, or if the AABB check fails.</returns>
    public static bool CheckChunkAABB(Chunk c, Vector2Int checkpos, Vector2Int checkSize)
    {

        if (c == null) return false;

        return c.AABB(checkpos, checkSize);

    }

    /// <summary>
    /// **WorldGen: GetFluidLevelFromChunkAtWorldPosition**
    /// <para>Retrieves the current fluid level (0.0 to 1.0) for the block at the specified world position.</para>
    /// <para>**Note:** This function only works if the fluid simulation is active.</para>
    /// </summary>
    /// <param name="worldX">World position X coordinate.</param>
    /// <param name="worldY">World position Y coordinate.</param>
    /// <returns>The fluid level (float), or **0** if the chunk at this world position is null.</returns>
    public static int GetFluidLevelFromChunkAtWorldPosition(int worldX, int worldY)
    {
        Chunk c = GetChunkAtWorldPos(worldX, worldY);
        if (c == null) return 0;

        return c.GetFluidAt(worldX, worldY);

    }
    public static FluidCell GetFluidCellFromChunkAtWorldPosition(int worldX, int worldY)
    {
        Chunk c = GetChunkAtWorldPos(worldX, worldY);
        if (c == null) return new FluidCell();

        return c.GetFluidCellAt(worldX, worldY);

    }
    /// <summary>
    /// **WorldGen: GetSolidBlock**
    /// <para>Retrieves the **Block** object at the world position if it is **solid**.</para>
    /// <para>It checks either the front or the back layer based on the <c>back</c> parameter.</para>
    /// </summary>
    /// <param name="worldX">World position X coordinate.</param>
    /// <param name="worldY">World position Y coordinate.</param>
    /// <param name="back">If <c>false</c>, checks the front layer. If <c>true</c>, checks the back layer.</param>
    /// <returns>The solid <see cref="Block"/> object found, otherwise an **Air Block at (0, 0)**.</returns>
    public static Block GetSolidBlock(int worldX, int worldY, bool back)
    {
        if (!back)
        {
            Block b = GetBlockFromChunkFrontLayer(worldX, worldY);
            if (b.IsSolid())
                return b;

        }
        else
        {
            Block b = GetBlockFromChunkBacktLayer(worldX, worldY);
            if (b.IsSolid())
                return b;
        }

        return new Block(BlockType.Air, 0, 0);
    }

    /// <summary>
    /// **WorldGen: GetBlock**
    /// <para>Retrieves the **Block** object from the specified layer (front or back) at the world position.</para>
    /// </summary>
    /// <param name="worldX">World position X coordinate.</param>
    /// <param name="worldY">World position Y coordinate.</param>
    /// <param name="FrontORBack">If <c>false</c>, retrieves the block from the front layer. If <c>true</c>, retrieves the block from the back layer.</param>
    /// <returns>The <see cref="Block"/> object.</returns>
    public static Block GetBlock(int worldX, int worldY, bool FrontORBack)
    {
        if (!FrontORBack)
            return GetBlockFromChunkFrontLayer(worldX, worldY);
        else
            return GetBlockFromChunkBacktLayer(worldX, worldY);
    }

    /// <summary>
    /// **WorldGen: RaycastBlock**
    /// <para>Performs a **raycast** against the **Front Layer** of blocks in the world.</para>
    /// <para>This uses the Digital Differential Analyzer (DDA) algorithm for block-grid raycasting.</para>
    /// </summary>
    /// <param name="worldStart">The starting position in world space.</param>
    /// <param name="direction">The direction vector of the raycast.</param>
    /// <param name="maxDistance">The maximum length/distance of the raycast.</param>
    /// <param name="data">The resulting <see cref="RaycastBlockData"/> structure, similar to a standard Unity RaycastHit.</param>
    /// <param name="collectBlocks">If <c>true</c>, all passed block coordinates will be saved in <c>data.passBlocks</c>.</param>
    /// <returns><c>true</c> if the raycast hit a solid block within <c>maxDistance</c>, <c>false</c> otherwise.</returns>
    public static bool RaycastBlock(Vector2 worldStart, Vector2 direction, float maxDistance, out RaycastBlockData data, bool collectBlocks = false)
    {
        data = new RaycastBlockData();

        if (collectBlocks)
            data.passBlocks = new List<Vector2Int>();

        data.startpoint = Vector2Int.FloorToInt(worldStart);
        data.startPointraw = worldStart;

        float x = worldStart.x;
        float y = worldStart.y;

        int mapX = Mathf.FloorToInt(x + .5f);
        int mapY = Mathf.FloorToInt(y + .5f);

        float dx = direction.normalized.x;
        float dy = direction.normalized.y;

        int stepX = dx < 0 ? -1 : 1;
        int stepY = dy < 0 ? -1 : 1;

        float deltaX = dx != 0 ? Mathf.Abs(1f / dx) : float.MaxValue;
        float deltaY = dy != 0 ? Mathf.Abs(1f / dy) : float.MaxValue;

        float sideDistX = dx < 0 ? (x - mapX) * deltaX : (mapX + 1f - x) * deltaX;
        float sideDistY = dy < 0 ? (y - mapY) * deltaY : (mapY + 1f - y) * deltaY;

        while (data.distance < maxDistance)
        {
            if (sideDistX < sideDistY)
            {
                mapX += stepX;
                float dist = sideDistX;
                sideDistX += deltaX;

                if (dist > maxDistance)
                    break;

                if (isSolidBlock(mapX, mapY))
                {
                    data.distance = dist;
                    data.hitPointRaw = worldStart + direction.normalized * dist;
                    data.hitPoint = new Vector2Int(mapX, mapY);

                    if (collectBlocks)
                    {
                        data.passBlocks.Add(data.hitPoint);
                    }
                    return true;
                }
            }
            else
            {
                mapY += stepY;
                float dist = sideDistY;
                sideDistY += deltaY;

                if (dist > maxDistance)
                    break;

                if (isSolidBlock(mapX, mapY))
                {
                    data.distance = dist;
                    data.hitPointRaw = worldStart + direction.normalized * dist;
                    data.hitPoint = new Vector2Int(mapX, mapY);

                    if (collectBlocks)
                        data.passBlocks.Add(data.hitPoint);

                    return true;
                }
            }


            if (collectBlocks)
                data.passBlocks.Add(new Vector2Int(mapX, mapY));
        }

        data.distance = maxDistance;
        data.hitPointRaw = worldStart + direction.normalized * maxDistance;
        data.hitPoint = Vector2Int.FloorToInt(data.hitPointRaw);

        if (collectBlocks)
        {
            data.passBlocks.Add(data.hitPoint);
            return true;
        }

        return false;
    }
    /// <summary>
    /// **WorldGen: BlockCast**
    /// <para>Performs a stepped line cast (or BlockCast) from a starting point towards a target point to detect the first solid block hit in the Front Layer.</para>
    /// <para>This method uses an iterative, fixed-step approach (<c>stepSize</c> = 0.25f) to traverse the world space.</para>
    /// </summary>
    /// <param name="startPos">The starting world position of the cast.</param>
    /// <param name="targetPos">The intended destination world position of the cast.</param>
    /// <param name="maxDistance">The maximum distance the cast can travel.</param>
    /// <returns>A <see cref="BlockCastHit"/> structure containing hit information, or <c>Hit = false</c> if no block was found.</returns>
    public static BlockCastHit BlockCast(Vector2 startPos, Vector2 targetPos, float maxDistance)
    {

        Vector2 delta = targetPos - startPos;
        float distance = delta.magnitude;


        if (distance > maxDistance)
        {
            targetPos = startPos + delta.normalized * maxDistance;
            distance = maxDistance;
        }


        const float stepSize = 0.25f;
        int steps = Mathf.CeilToInt(distance / stepSize);

        Vector2 current = startPos;

        Block BeforBlock = new Block(BlockType.Air, 0, 0);

        for (int i = 0; i <= steps; i++)
        {

            int bx = Mathf.FloorToInt(current.x);
            int by = Mathf.FloorToInt(current.y);

            Block block = WorldGen.GetBlock(bx, by, false);

            if (block.type != BlockType.Air)
            {
                return new BlockCastHit()
                {
                    Hit = true,
                    Block = block,
                    beforBlock = BeforBlock,
                    Position = new Vector2(bx, by),
                    Distance = Vector2.Distance(startPos, new Vector2(bx + 0.5f, by + 0.5f))
                };
            }

            BeforBlock = GetBlock(bx, by, false);

            current += delta.normalized * stepSize;
        }

        return new BlockCastHit() { Hit = false };
    }
    public static BlockCastHit BlockDirCast(Vector2 startPos, Vector2 direction, float maxDistance)
    {
        Vector2 moveDir = direction.normalized;

        const float stepSize = 0.25f;

        int steps = Mathf.CeilToInt(maxDistance / stepSize);

        Vector2 current = startPos;

        Block BeforBlock = new Block(BlockType.Air, 0, 0);

        for (int i = 0; i <= steps; i++)
        {
            int bx = Mathf.FloorToInt(current.x);
            int by = Mathf.FloorToInt(current.y);

            Block block = GetBlock(bx, by, false);

            if (block.type != BlockType.Air)
            {
                return new BlockCastHit()
                {
                    Hit = true,
                    Block = block,
                    beforBlock = BeforBlock,
                    Position = new Vector2(bx, by),
                    Distance = Vector2.Distance(startPos, new Vector2(bx + 0.5f, by + 0.5f))
                };
            }

            BeforBlock = GetBlock(bx, by, false);

            current += moveDir * stepSize;
        }

        return new BlockCastHit() { Hit = false };
    }

    // _,.:--++**##    Core Functions    ##**++--:.,_

    public static Vector2Int Decode(long key)
    {
        int x = (int)(key >> 32);
        int y = (int)(key & 0xFFFFFFFFL);
        return new Vector2Int(x, y);
    }
    public static long Encode(int x, int y)
    {
        return ((long)x << 32) | (uint)y;
    }

    public static void DebugLog(string massage)
    {
        if(DB)
            Debug.Log(massage);
    }

    public async void UpdateDynamicWorld(Camera cam)
    {
        Vector3 camPos = cam.transform.position;
        float halfHeight = cam.orthographicSize;
        float halfWidth = halfHeight * cam.aspect;

        // Bereich berechnen (Sichtfeld + Puffer)
        int padding = SimulatioansSize * chunkSize;
        int minChunkX = Mathf.FloorToInt((camPos.x - halfWidth - padding) / chunkSize);
        int maxChunkX = Mathf.FloorToInt((camPos.x + halfWidth + padding) / chunkSize);
        int minChunkY = Mathf.FloorToInt((camPos.y - halfHeight - padding) / chunkSize);
        int maxChunkY = Mathf.FloorToInt((camPos.y + halfHeight + padding) / chunkSize);

        for (int x = minChunkX; x <= maxChunkX; x++)
        {
            for (int y = minChunkY; y <= maxChunkY; y++)
            {
                // Weltgrenzen beachten
                if (x < 0 || y < 0 || x >= ChunkXCount || y >= ChunkYCount) continue;

                Vector2Int pos = new Vector2Int(x * chunkSize, y * chunkSize);
                var e = Encode(pos.x, pos.y);
                if (!ChunkList.ContainsKey(e))
                {
                    Chunk ch = Instantiate(Chunk).GetComponent<Chunk>();
                    ch.transform.position = (Vector2)pos;
                    ch.name = "Chunk:" + (x * chunkSize) + "x:" + (y * chunkSize) + "y";
                 
                    ch.Generate(pos, x, y, RegionSize, LightType);
                     
                    ChunkList.Add(e, ch);

                    ch.GetNeighbors();
                    ch.GenerateHeight(WorldSeed);
                    WorldGenerator.CreateSingleChunk(ch);
                    LightSystem.RegisterChunkToRegion(ch);
                    
                }
                else
                {
                    Chunk c = ChunkList[e];
                    c.gameObject.SetActive(true);

                }
            }
        }


        foreach (var pair in ChunkList)
        {
            Chunk c = pair.Value;
            if (c.chunkIndexX < minChunkX - 1 || c.chunkIndexX > maxChunkX + 1 ||
                c.chunkIndexY < minChunkY - 1 || c.chunkIndexY > maxChunkY + 1)
            {
                c.gameObject.SetActive(false);

            }
        }

        await Task.CompletedTask;

    }
    /// <summary>
    /// **Core: SimulationChunksCameraArea**
    /// <para>Determines which chunks fall within the camera's view frustum, plus a specified simulation padding area.</para>
    /// <para>This is an internal core function used to manage chunk loading, unloading, and simulation updates.</para>
    /// </summary>
    /// <param name="cam">The active Camera used to define the view area.</param>
    /// <param name="loadAll">If <c>true</c>, all loaded chunks are returned, ignoring the camera area check.</param>
    /// <returns>A list of <see cref="Chunk"/> objects currently visible or within the simulation range.</returns>
    public void SimulationChunksCameraArea(Camera cam, bool loadAll = false)
    {
        if (ChunkList == null || ChunkList.Count == 0) return;

        LoadetChunkList.Clear();

        if (loadAll)
        {
            LoadetChunkList.AddRange(ChunkList.Values);
            return;
        }

        // 1. Kamera-Bereich in Weltkoordinaten ermitteln
        Vector3 camPos = cam.transform.position;
        float halfHeight = cam.orthographicSize;
        float halfWidth = halfHeight * cam.aspect;

        // 2. Bereich berechnen (inkl. SimulatioansSize als Puffer)
        int padding = SimulatioansSize * chunkSize;
        float minX = camPos.x - halfWidth - padding;
        float maxX = camPos.x + halfWidth + padding;
        float minY = camPos.y - halfHeight - padding;
        float maxY = camPos.y + halfHeight + padding;

        // 3. In Chunk-Indizes umrechnen
        int minChunkX = Mathf.FloorToInt(minX / chunkSize);
        int maxChunkX = Mathf.FloorToInt(maxX / chunkSize);
        int minChunkY = Mathf.FloorToInt(minY / chunkSize);
        int maxChunkY = Mathf.FloorToInt(maxY / chunkSize);

        // 4. NUR die Chunks abfragen, die im Bereich liegen (O(1) Lookup)
        for (int x = minChunkX; x <= maxChunkX; x++)
        {
            for (int y = minChunkY; y <= maxChunkY; y++)
            {
                var key = Encode(x * chunkSize, y * chunkSize);
                if (ChunkList.TryGetValue(key, out Chunk c))
                {
                    LoadetChunkList.Add(c);
                }
            }
        }
    }

    /// <summary>
    /// **Core: isBlockConnectFrontLayer**
    /// <para>Checks if the block at <c>(worldX, worldY)</c> in the **Front Layer** has an adjacent solid block (top, bottom, left, or right).</para>
    /// <para>Used internally for mesh generation and block connectivity checks.</para>
    /// </summary>
    /// <param name="worldX">World position X coordinate of the block to check.</param>
    /// <param name="worldY">World position Y coordinate of the block to check.</param>
    /// <returns><c>true</c> if any adjacent block in the Front Layer is solid, <c>false</c> otherwise.</returns>
    public static bool isBlockConnectFrontLayer(int worldX, int worldY)
    {
        bool isConnect = false;

        // Local function to simplify checking neighbors
        bool Get(int x, int y) => GetBlockFromChunkFrontLayer(worldX + x, worldY + y).IsSolid();

        if (Get(-1, 0)) // Left
            isConnect = true;

        if (Get(1, 0)) // Right
            isConnect = true;

        if (Get(0, 1)) // Up
            isConnect = true;

        if (Get(0, -1)) // Down
            isConnect = true;

        return isConnect;
    }

    /// <summary>
    /// **Core: isBlockConnectBackLayer**
    /// <para>Checks if the block at <c>(worldX, worldY)</c> in the **Back Layer** has an adjacent solid block (top, bottom, left, or right).</para>
    /// <para>Used internally for mesh generation and block connectivity checks.</para>
    /// </summary>
    /// <param name="worldX">World position X coordinate of the block to check.</param>
    /// <param name="worldY">World position Y coordinate of the block to check.</param>
    /// <returns><c>true</c> if any adjacent block in the Back Layer is solid, <c>false</c> otherwise.</returns>
    public static bool isBlockConnectBackLayer(int worldX, int worldY)
    {
        bool isConnect = false;

        // Local function to simplify checking neighbors
        bool Get(int x, int y) => GetBlockFromChunkBacktLayer(worldX + x, worldY + y).IsSolid();

        if (Get(-1, 0)) // Left
            isConnect = true;

        if (Get(1, 0)) // Right
            isConnect = true;

        if (Get(0, 1)) // Up
            isConnect = true;

        if (Get(0, -1)) // Down
            isConnect = true;

        return isConnect;
    }
   

    private void OnDisable()
    {
        LightSystem.DisPosRegions();
    }

    private void OnDestroy()
    {
        LightSystem.DisPosRegions();
    }

    // Input Methods

    /// <summary>
    /// **Input: GetMouseAtVoxel**
    /// <para>Converts the current mouse screen position to the corresponding **integer Voxel/Block world coordinate**.</para>
    /// <para>This function is useful for block selection and interaction.</para>
    /// </summary>
    /// <returns>The rounded world position (<see cref="Vector2Int"/>) of the Voxel/Block under the mouse cursor.</returns>
    public static Vector2Int GetMouseAtVoxel()
    {
        Vector2 worldPos = Camera.main.ScreenToWorldPoint(UnityEngine.InputSystem.Mouse.current.position.ReadValue());
        Vector2Int pos = Vector2Int.zero;
        pos.x = Mathf.RoundToInt(worldPos.x);
        pos.y = Mathf.RoundToInt(worldPos.y);

        return pos;
    }

    /// <summary>
    /// **Input: GetMousePosition**
    /// <para>Retrieves the raw world space position of the mouse cursor.</para>
    /// </summary>
    /// <returns>The unrounded world position (<see cref="Vector2"/>) of the mouse cursor.</returns>
    public static Vector2 GetMousePosition()
    {
        return Camera.main.ScreenToWorldPoint(UnityEngine.InputSystem.Mouse.current.position.ReadValue());
    }
    /// <summary>
    /// **Input: GetMousePosition**
    /// <para>Retrieves the raw world space position of the mouse cursor.</para>
    /// </summary>
    /// <returns>The unrounded world position (<see cref="Vector2"/>) of the mouse cursor.</returns>
    public static Vector2Int GetMousePositionInt()
    {
        return (Vector2Int.RoundToInt(Camera.main.ScreenToWorldPoint(UnityEngine.InputSystem.Mouse.current.position.ReadValue())));
    }
    //

    //Test Methods

    public void PlaceWaterTest()
    {
        if (UnityEngine.InputSystem.Mouse.current.middleButton.wasPressedThisFrame)
        {
            Vector2Int pos = GetMouseAtVoxel();
            WorldGen.SetFluidLevelInChunk( pos.x, pos.y, 1);  
        }
    }

    int pcount;
    public void PlacePointLightTest()
    {
        var m = UnityEngine.InputSystem.Mouse.current;

        if (m.middleButton.wasPressedThisFrame)
        {
            Color[] c = new Color[] { Color.red, Color.green, new Color(0, 0.6f, 1f)};
            pcount++;
            LightSystem.AddPointLight((int)GetMousePosition().x, (int)GetMousePosition().y, c[UnityEngine.Random.Range(0, c.Length)], 2, 2, false, pcount);
            LightSystem.SetRegionDirty((int)GetMousePosition().x, (int)GetMousePosition().y);
        }

        if (m.rightButton.wasPressedThisFrame)
        {
            
            LightSystem.RemovePointLight(pcount);
            pcount--;
            if(pcount < 0)pcount = 0;
        }

    }

    //




    private void Update()
    {
        //** Core Updates **\\
        if (waterActive && FluidSystem.FluidDirty)
        {

            List<Chunk> load = new List<Chunk>();
            foreach (var cc in ChunkList.Values)
            {
                if (cc.gameObject.activeSelf)
                    load.Add(cc);
            }

            AddSimluationUpdate(() => FluidSystem.UpdateSystem(load));
        }

        AddLightUpdate(() => LightSystem.CalculateVisibleRegions(NoLight));



        //****+++***\\



        //Test Funktions

        // if (UnityEngine.InputSystem.Mouse.current.middleButton.wasPressedThisFrame)
        // {
        //     Explosion.SetExplosionAt((int)GetMousePosition().x, (int)GetMousePosition().y, 6, 10);
        // }

        PlacePointLightTest();

        if (UnityEngine.InputSystem.Keyboard.current.rKey.wasPressedThisFrame)
        {
            Color[] c = { new Color(1f, 0.7f, 0.4f), new Color(.7f, 0.7f, 0.4f), new Color(.2f, 0.6f, 0.8f), new Color(.2f, 0.4f, 0.9f), new Color(.3f, 0.7f, 0.2f), new Color(.3f, 0.9f, 0.5f) };

            LightSystem.SetSunLightColor(c[UnityEngine.Random.Range(0, c.Length -1)]);
            LightSystem.setAllRegionsDirty();
        }
        if (UnityEngine.InputSystem.Keyboard.current.fKey.wasPressedThisFrame)
        {
            LightSystem.SetSunLightColor(Color.white);
            LightSystem.setAllRegionsDirty();
        }
        
        if (UnityEngine.InputSystem.Keyboard.current.qKey.wasPressedThisFrame)
        {
            LightSystem.SetSunLightIntensity(0.1f);
            LightSystem.setAllRegionsDirty();
        }
        if (UnityEngine.InputSystem.Keyboard.current.cKey.wasPressedThisFrame)
        {
            LightSystem.SetSunLightIntensity(1);
            LightSystem.setAllRegionsDirty();
        }
    }




    private void LateUpdate()
    {
        // ** Core Updates ** \\
        Vector2 offset = (Vector2)cam.transform.position - OldCamPos;
        float max = ((chunkSize * SimulatioansSize) * .25f) * ((chunkSize * SimulatioansSize) * .25f);
        if (offset.sqrMagnitude >= max  || !StartGenerate)
        {
            StartGenerate = true;
            if (DB) Debug.Log("Update Simulation Space By Distance");
            OldCamPos = cam.transform.position;
            UpdateDynamicWorld(cam);
        }

        if (ChunkList == null || ChunkList.Count <= 0)
            UpdateDynamicWorld(cam);

        //**++++++++++++++++++**\\
    }

}
