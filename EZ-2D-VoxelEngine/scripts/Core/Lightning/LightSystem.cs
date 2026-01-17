using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

[Serializable]
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct LightMapData
{
    public float r;
    public float g;
    public float b;
    public float a;
}

public struct BlockData
{
    public byte fsolid, bsoild;
    public float fluid;
}

public struct PointData
{
    public float x, y;
    public float r, g, b, intensivit;
    public float range;
    public bool od;
}

public class LightRegion
{
    public JobHandle lightJobHandle;
    public bool isJobRunning = false;

    public bool isDirty;
    public int Width;
    public int Height;
    public int OffsetX;
    public int OffsetY;
    public int chunksSize;
    public int totalPixels;

    public List<Chunk> chunks;

    public NativeArray<BlockData> blockdata;
    public NativeArray<PointData> pointdata;
    public NativeArray<LightMapData> temp;
    public NativeArray<LightMapData> BLMD;
    public NativeArray<LightMapData> buffLight;
    public NativeArray<LightMapData> buffer;
    public NativeArray<LightMapData> LightData;
    public NativeArray<LightMapData> ClearLightData;
    public NativeArray<BlockData> Clearblockdata;

    public void DisPose()
    {
        if (BLMD.IsCreated) BLMD.Dispose();
        if (temp.IsCreated) temp.Dispose();
        if (buffer.IsCreated) buffer.Dispose();
        if (blockdata.IsCreated) blockdata.Dispose();
        if (pointdata.IsCreated) pointdata.Dispose();
        if (buffLight.IsCreated) buffLight.Dispose();
        if (LightData.IsCreated) LightData.Dispose();
        if (ClearLightData.IsCreated) ClearLightData.Dispose();
        if (Clearblockdata.IsCreated) Clearblockdata.Dispose();
    }
    public void LightCompositeBuffer()
    {
        if (!ClearLightData.IsCreated) 
            ClearLightData = new NativeArray<LightMapData>(totalPixels, Allocator.Persistent);

        if (!Clearblockdata.IsCreated) 
            Clearblockdata = new NativeArray<BlockData>(totalPixels, Allocator.Persistent);


        if (!BLMD.IsCreated)
            BLMD = new NativeArray<LightMapData>(totalPixels, Allocator.Persistent);
        else
            NativeArray<LightMapData>.Copy(ClearLightData, BLMD, totalPixels);

        if (!temp.IsCreated)
            temp = new NativeArray<LightMapData>(totalPixels, Allocator.Persistent);
        else
            NativeArray<LightMapData>.Copy(ClearLightData, temp, totalPixels);

        if (!buffLight.IsCreated)
            buffLight = new NativeArray<LightMapData>(totalPixels, Allocator.Persistent);
        else
            NativeArray<LightMapData>.Copy(ClearLightData, buffLight, totalPixels);

        if (!buffer.IsCreated)
            buffer = new NativeArray<LightMapData>(totalPixels, Allocator.Persistent);
        else
            NativeArray<LightMapData>.Copy(ClearLightData, buffer, totalPixels);

        if (!blockdata.IsCreated)
            blockdata = new NativeArray<BlockData>(totalPixels, Allocator.Persistent);
        else
            NativeArray<BlockData>.Copy(Clearblockdata, blockdata, totalPixels);

    }


    public int GetBufferIndex(int worldX, int worldY)
    {
        int localX = worldX - OffsetX;
        int localY = worldY - OffsetY;

        if (localX < 0 || localX >= Width || localY < 0 || localY >= Height) return -1;

        return localX + localY * Width;
    }
    public void ApplyToChunks()
    {
        foreach (var c in chunks)
        {
            for (int lx = 0; lx < c.chunkSize; lx++)
            {
                int worldX = c.chunkOrigin.x + lx;

                for (int ly = 0; ly < c.chunkSize; ly++)
                {
                    int worldY = c.chunkOrigin.y + ly;

                    int bufferIndex = GetBufferIndex(worldX, worldY);
                    if (bufferIndex == -1)
                        continue;

                    c.SetLightBlockAt(worldX, worldY, BLMD[bufferIndex]);
                }
            }
        }
    }
}

public static class LightSystem
{

    public class SpotLight
    {
        public int ID;
        public float r, g, b;
        public float intemsity;
        public List<Vector2Int> BlockInfo;

        public void setColor(float r, float g, float b)
        {
            this.r = r; this.g = g; this.b = b;
        }

        public void setIntensity(float intensity)
        {
            this.intemsity = intensity;
        }
    }

    public class PointLight
    {
        public int ID;
        public int x;
        public int y;
        public float r;
        public float g;
        public float b;
        public float intensity;
        public int range;
        public bool OverLight;

        public void MoveLight(int worldX, int worldY)
        {
            if (worldX < 0 || worldY < 0) return;

            x = worldX;
            y = worldY;
        }

        public void setColor(float r, float g, float b)
        {
            this.r = r;
            this.g = g;
            this.b = b;
        }

        public void setIntensity(float intensity)
        {
            this.intensity = intensity;
        }

        public void setRange(float range)
        {
            this.r += range;
        }
    }

    private static float SunLightIntensity = 1f;
    private static Color SunLightColor = Color.white;

    private static List<PointLight> pointLights = new List<PointLight>();
    private static List<SpotLight> spotLights = new List<SpotLight>();
    private static NativeArray<LightMapData> LightData;

    public static int RegionChunkSize = 5; 
    public static Dictionary<Vector2Int, LightRegion> regions = new Dictionary<Vector2Int, LightRegion>();

    private static int chunkSize;



    //** LightSystem USER FUNKTIONS **

    /// <summary>
    /// **LightSystem: Set The Scene Sun Light Intensity**
    /// </summary>
    /// <param name="value">The intensity value for the global sun light (default is 1).</param>
    public static void SetSunLightIntensity(float value)
    {
        if (Mathf.Approximately(value, SunLightIntensity)) return;
        SunLightIntensity = value;
        setAllRegionsDirty();
    }

    /// <summary>
    /// **LightSystem: Set The Scene Sun Light Color**
    /// </summary>
    /// <param name="c">The new Color value (in RGB format) for the global sun light.</param>
    public static void SetSunLightColor(Color c)
    {
        SunLightColor = c;
        setAllRegionsDirty();
    }

    /// <summary>
    /// **LightSystem: Returns Point Light by ID**
    /// </summary>
    /// <param name="id">The unique ID of the Point Light.</param>
    /// <returns>The PointLight object at the specified index/ID.</returns>
    public static PointLight GetPointLight(int id)
    {
        if (pointLights.Count <= 0) return null;
        return pointLights[id];
    }

    /// <summary>
    /// **LightySystem: Adds a Point Light to World Space.**
    /// <para>This function creates a new point light in the scene. You can control.</para>
    /// <para>the point light either upon instantiation or later by directly referencing its ID.
    /// Available control functions for point lights include:</para>
    /// <list type="bullet">
    ///     <item>MoveLight</item>
    ///     <item>SetColor</item>
    ///     <item>SetIntensity</item>
    ///     <item>SetRange</item>
    /// </list>
    /// <param name="worldX">The X-coordinate (position) in World Space.</param>
    /// <param name="worldY">The Y-coordinate (position) in World Space.</param>
    /// <param name="color">The Color of the point light.</param>
    /// <param name="intensity">The brightness/strength of the point light.</param>
    /// <param name="range">The radius or "glow range" of the point light.</param>
    /// <param name="ovl"><para>If set to <c>true</c>, the light can over-light (ignore/pass through)</para> 
    /// <para>solid blocks in the foreground layer. (Optional: defaults to <c>false</c>)</para></param>
    /// <param name="ID">
    /// <para>A unique ID used to easily find and edit this point light later.</para>
    /// <para>If an ID less than 0 is provided (the default), the ID is automatically</para>
    /// <para>set to the current number of point lights in the scene.</para>
    /// </param>
    /// </summary>
    public static void AddPointLight(int worldX, int worldY, Color color, float intensity, int range, bool ovl = false, int ID = -1)
    {

        PointLight pl = new PointLight();
        pl.x = worldX;
        pl.y = worldY;
        pl.r = color.r;
        pl.g = color.g;
        pl.b = color.b;
        pl.intensity = intensity;
        pl.range = range;
        pl.OverLight = ovl;

        if (ID < 0)
            pl.ID = pointLights.Count;
        else pl.ID = ID;

        if (!pointLights.Contains(pointLights.FirstOrDefault(c => c.ID == ID)))
            pointLights.Add(pl);
    }

    /// <summary>
    /// **LightSystem: Get Spot Light by ID**
    /// <para>!! WARNING !!</para>
    /// <para>Do not use this method directly; it is an internal helper function for the automatic system!</para>
    /// </summary>
    /// <param name="ID">The unique identifier of the SpotLight.</param>
    /// <returns>The SpotLight object matching the ID, or null if not found.</returns>
    public static SpotLight GetSpotLight(int ID)
    {
        return spotLights.FirstOrDefault(c => c.ID == ID);
    }

    /// <summary>
    /// **LightSystem: Add Spot Light by ID**
    /// <para>!! WARNING !!</para>
    /// <para>Do not call this method yourself! Only the **SpotLight2D** component should use this method to register itself.</para>
    /// <para>Do not use this method like you would use <c>AddPointLight</c>.</para>
    /// </summary>
    /// <param name="c">The color of the spot light.</param>
    /// <param name="intensity">The intensity level of the light.</param>
    /// <param name="ID">The unique identifier to assign to the SpotLight.</param>
    public static void AddSpotLight(Color c, float intensity, int ID)
    {
        SpotLight pl = new SpotLight();
        pl.r = c.r; pl.g = c.g; pl.b = c.b;
        pl.intemsity = intensity;
        pl.BlockInfo = new List<Vector2Int>();
        pl.ID = ID;

        if (!spotLights.Contains(spotLights.FirstOrDefault(i => i.ID == pl.ID)))
        {
            spotLights.Add(pl);
        }

    }

    /// <summary>
    /// **LightSystem: Remove Spot Light by ID**
    /// <para>!! WARNING !!</para> 
    /// <para>SpotLights generate and save a random ID upon instantiation in Play Mode.</para>
    /// <para>The **SpotLight2D** system automatically removes the correct spotlight when it is disabled or destroyed.</para>
    /// <para>Therefore, do not use this method like you would use <c>RemovePointLight</c>.</para>
    /// </summary>
    /// <param name="ID">The ID of the SpotLight to remove.</param>
    public static void RemoveSpotlight(int ID)
    {
        spotLights.RemoveAll(p => p.ID == ID);
    }

    /// <summary>
    /// **LightSystem: Remove Point Light by ID**
    /// </summary>
    /// <param name="ID">The unique ID of the Point Light.</param>
    public static void RemovePointLight(int ID)
    {
        if (pointLights.Count <= 0) return;
        var v = pointLights.FirstOrDefault(p => p.ID == ID);
        if (v != null)
        {
            SetRegionDirty(v.x, v.y);
            pointLights.Remove(v);
        }
    }

    /// <summary>
    /// **LightSystem: Remove ALL Point Lights in Scene**
    /// </summary>
    public static void ClearPointLights()
    {
        pointLights.Clear();
        setAllRegionsDirty();
    }

    /// <summary>
    /// **LightSystem: Returns the Lightmap Color at a specific world position.**
    /// <para>This method is called by the **EntityLight** script to enable shader interaction with LightMap data.</para>
    /// </summary>
    /// <param name="worldX">The X coordinate in World Space.</param>
    /// <param name="worldY">The Y coordinate in World Space.</param>
    /// <returns>A <c>float[4]</c> array containing the current LightMap color data: <c>[r, g, b, a]</c>.</returns>
    public static float[] GetLightAtPosition(int worldX, int worldY)
    {
        Chunk c = WorldGen.GetChunkAtWorldPos(worldX, worldY);
        if (c == null)
            return new float[] { 0, 0, 0, 0 };

        var cc = c.GetLightBlockAt(worldX, worldY);

        return new float[]{
            cc.r,
            cc.g,
            cc.b,
            cc.a
        };

    }


    //** LIGHTSYSTEM CORE **

    public static void DrawRegionGizmos()
    {
        Gizmos.color = Color.cyan;

        foreach (var r in regions.Values)
        {
            Vector3 center = new Vector3(
                r.OffsetX + r.Width * 0.5f,
                r.OffsetY + r.Height * 0.5f,
                0
            );

            Vector3 size = new Vector3(r.Width, r.Height, 0);

            Gizmos.DrawWireCube(center, size);
        }
    }

    public static void setAllRegionsDirty()
    {
        foreach (var region in regions.Values)
            region.isDirty = true;
    }

    public static void DisPosRegions()
    {
        if(LightData.IsCreated)LightData.Dispose();
        foreach (var region in regions.Values)
            region.DisPose();
    }

    public static Vector2Int GetRegionCoordByWorldPos(int worldX, int worldY)
    {
        int regionWorldSize = WorldGen.chunkSize * RegionChunkSize;

        int rx = Mathf.FloorToInt((float)worldX / regionWorldSize);
        int ry = Mathf.FloorToInt((float)worldY / regionWorldSize);

        return new Vector2Int(rx, ry);
    }

    public static LightRegion GetOrCreateRegion(Vector2Int regionID)
    {
        if (regions.TryGetValue(regionID, out var r))
            return r;

        int pad = WorldGen.chunkSize;

        var newRegion = new LightRegion();

        int regionWorldSize = WorldGen.chunkSize * RegionChunkSize;
        var wh = regionWorldSize + pad * 2;
        newRegion.OffsetX = regionID.x * regionWorldSize - pad;
        newRegion.OffsetY = regionID.y * regionWorldSize - pad;
        newRegion.Width = wh;
        newRegion.Height = wh;
        newRegion.totalPixels = wh * wh; 
        newRegion.chunksSize = WorldGen.chunkSize;
        newRegion.isDirty = true;
        newRegion.chunks = new List<Chunk>();

        regions.Add(regionID, newRegion);

        return newRegion;
    }
    public static void RegisterChunkToRegion(Chunk c)
    {
        var rc = GetRegionCoordByWorldPos(c.chunkOrigin.x, c.chunkOrigin.y);
        var region = GetOrCreateRegion(rc);

        if (!region.chunks.Contains(c))
            region.chunks.Add(c);


        region.isDirty = true;
    }

    public static void SetRegionDirty(int worldx, int worldy)
    {
        Vector2Int center = GetRegionCoordByWorldPos(worldx, worldy);

        for (int ox = -1; ox <= 1; ox++)
        {
            for (int oy = -1; oy <= 1; oy++)
            {
                Vector2Int r = new Vector2Int(center.x + ox, center.y + oy);

                if (regions.TryGetValue(r, out var region))
                    region.isDirty = true;
            }
        }
    }
    public static void SetSingleRegionDirty(int worldx, int worldy)
    {
        var p = GetRegionCoordByWorldPos(worldx, worldy);

        if (regions.TryGetValue(p, out var region))
            region.isDirty = true;

    }
    public static bool GetRegionDirty(int worldx, int worldy)
    {
        var p = GetRegionCoordByWorldPos(worldx, worldy);
        if (regions.TryGetValue(new Vector2Int(p[0], p[1]), out var region))
        {
            return region.isDirty;
        }

        return false;
    }

    public static void CalculateVisibleRegions(bool noLight)
    {
        foreach (var region in regions.Values)
        {
            if (region.isDirty)
                CalculateRegion(region, noLight);

            if (region.isJobRunning)
                CheckIfJobFinished(region);
        }
    }

    public static void CalculateRegion(LightRegion region, bool noLights)
    {
        
        if (region.isJobRunning)
        {
            if (region.lightJobHandle.IsCompleted)
            {
                region.lightJobHandle.Complete();
                region.isJobRunning = false;

                region.ApplyToChunks();
                ApplyChunkToTexture(region);
            }
            else return; 
        }

        if(!region.isDirty) return;

        chunkSize = WorldGen.chunkSize;

        WorldGen.DebugLog("REGION CALC " + region.OffsetX + " / " + region.OffsetY + "   chunks: " + region.chunks.Count);

        region.LightCompositeBuffer();

        int baseHeight = WorldGen.Instance.GroundHeight;

        if (noLights)
        {
            region.BLMD.FillArray(new LightMapData() { a = 1,r = 1, g = 1, b = 1});

            region.ApplyToChunks();
            ApplyChunkToTexture(region);

            region.lightJobHandle.Complete();
            region.isJobRunning = false;
            region.isDirty = false;
            return;
        }

       
        for (int lx = 0; lx < region.Width; lx++)
        {
            int worldx = lx + region.OffsetX;
            float sunInt = SunLightIntensity;
            bool hitSolidFrontBlock = false;

            for (int wy = region.Height - 1; wy >= 0; wy--)
            {
                int worldy = wy + region.OffsetY;
                bool front = WorldGen.isSolidBlock(worldx, worldy);
                bool back = WorldGen.isSolidBlockBack(worldx, worldy);
                
                int idx = region.GetBufferIndex(worldx, worldy);
                if (idx == -1) continue;

                var l = region.BLMD[idx];

                region.blockdata[idx] = new BlockData
                { 
                    fsolid = (byte)(front ? 1 : 0), 
                    bsoild = (byte)(back ? 1 : 0),
                    fluid = WorldGen.GetFluidLevelFromChunkAtWorldPosition(worldx, worldy)
                };
                

                if (!front && !back && !hitSolidFrontBlock)
                {
                    l.r = SunLightColor.r;
                    l.g = SunLightColor.g;
                    l.b = SunLightColor.b;
                    sunInt = SunLightIntensity;
                    l.a = sunInt;
                }
                else if (front)
                {
                    hitSolidFrontBlock = true;
                    l.a = 0.01f;
                    continue;
                }

                
                if (back && !front && worldy >= baseHeight && !hitSolidFrontBlock)
                {
                    sunInt *= .98f;
                    l.a = sunInt;
                }

                if (WorldGen.waterActive && WorldGen.GetFluidLevelFromChunkAtWorldPosition(worldx, worldy) > 0.1f)
                {
                    l.r = .2f;
                    l.g = .6f;
                    l.b = .8f;
                    sunInt *= .98f;
                    l.a = sunInt;
                    continue;
                }

                if (worldy < baseHeight)
                {
                    l.a = 0.01f;
                }

                if (!back && !front && worldy >= baseHeight && hitSolidFrontBlock)
                {
                    l.r = SunLightColor.r;
                    l.g = SunLightColor.g;
                    l.b = SunLightColor.b;
                    l.a = SunLightIntensity;
                }


                region.BLMD[idx] = l;
            }
        }

        FloodJobLight flood = new FloodJobLight()
        {
            data = region.blockdata,
            wt = WorldGen.waterActive,
            light = region.BLMD,
            width = region.Width,
            height = region.Height,
            offsetx = 0,
            offsety = 0
        };

        var currentHandle = flood.Schedule();

        currentHandle = ScheduleBlur(region, 3, currentHandle);


        if (pointLights.Count > 0)
        {
             var pointdata = new NativeArray<PointData>(pointLights.Count, Allocator.TempJob);

            for (int p = 0; p < pointLights.Count; p++)
            {
                var l = pointLights[p];
                pointdata[p] = new PointData()
                {
                    x = l.x,
                    y = l.y,
                    r = l.r,
                    g = l.g,
                    b = l.b,
                    intensivit = l.intensity,
                    range = l.range,
                    od = l.OverLight
                };
            }

            var copyToBuff = new CopyBufferJob { Source = region.BLMD, Destination = region.buffLight };
            var copyHandle = copyToBuff.Schedule(region.Width * region.Height, 64, currentHandle);

            FloodJobPointLight pointjob = new FloodJobPointLight()
            {
                bock = region.blockdata,
                data = pointdata,
                light = region.buffLight,
                buffer = region.buffer,
                wt = WorldGen.waterActive,
                height = region.Height,
                width = region.Width,
                offsetx = region.OffsetX,
                offsety = region.OffsetY,
                inters = 4,
            };


            var pointHandle = pointjob.Schedule(copyHandle);

            pointdata.Dispose(pointHandle);

            var copyBack = new CopyBufferJob { Source = region.buffLight, Destination = region.BLMD };

            region.lightJobHandle = copyBack.Schedule(region.Width * region.Height, 64, pointHandle);
        }
        else
            region.lightJobHandle = currentHandle;


        region.isJobRunning = true;
        region.isDirty = false;
    }

    public static void CheckIfJobFinished(LightRegion region)
    {
        if (region.isJobRunning && region.lightJobHandle.IsCompleted)
        {
            region.lightJobHandle.Complete();
            region.isJobRunning = false;

            DyeOnWater(region, WorldGen.GLMsize.y);

            region.ApplyToChunks();
            ApplyChunkToTexture(region);
        }
    }

    [BurstCompile]
    public struct CopyBufferJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<LightMapData> Source;
        public NativeArray<LightMapData> Destination;
        public void Execute(int i) => Destination[i] = Source[i];
    }

    [BurstCompile]
    public struct FloodJobLight : IJob
    {

        public NativeArray<BlockData> data;
        public NativeArray<LightMapData> light;

        [ReadOnly] public bool wt;
        [ReadOnly] public int width;
        [ReadOnly] public int height;
        [ReadOnly] public int offsetx;
        [ReadOnly] public int offsety;
        
        public void Execute()
        {
          
                // Von links oben nach rechts unten
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int i = x + y * width;
                        Propagate(i, x - 1, y); // links
                        Propagate(i, x, y - 1); // unten
                    }
                }
                // Von rechts unten nach links oben
                for (int y = height - 1; y >= 0; y--)
                {
                    for (int x = width - 1; x >= 0; x--)
                    {
                        int i = x + y * width;
                        Propagate(i, x + 1, y); // rechts
                        Propagate(i, x, y + 1); // oben
                    }
                }
            
        }
        private void Propagate(int currentIdx, int nx, int ny)
        {
            if (nx < 0 || nx >= width || ny < 0 || ny >= height) return;

            int nIdx = nx + ny * width;
            float neighborLight = light[nIdx].a;
            if (neighborLight <= 0.05f) return;

            float loss = GetPropagationFactor(nIdx);

            float result = neighborLight * loss;
            result = math.clamp(result, 0, 1);

            if (result > light[currentIdx].a)
            {
                LightMapData l = light[currentIdx];
                l.a = result;
                l.r = light[nIdx].r * 0.95f;
                l.g = light[nIdx].g * 0.95f;
                l.b = light[nIdx].b * 0.95f;
                light[currentIdx] = l;
            }
        }
    

        private  float GetPropagationFactor(int ind)
        {
            float value = 1f;


            if (wt && data[ind].fluid > 50f)
                return 0.01f;

            if (data[ind].fsolid == 1) return .01f;
            if(data[ind].bsoild == 1) return .8f;


            return value;
        }
    }

    [BurstCompile]
    public struct FloodJobPointLight : IJob
    {
        public NativeArray<BlockData> bock;
        public NativeArray<PointData> data;
        public NativeArray<LightMapData> light;
        public NativeArray<LightMapData> buffer;

        [ReadOnly] public bool wt;
        [ReadOnly] public int width;
        [ReadOnly] public int height;
        [ReadOnly] public int offsetx;
        [ReadOnly] public int offsety;
        [ReadOnly] public int inters;

        
        public void Execute()
        {
            for (int p = 0; p < data.Length; p++)
            {
                PointData pl = data[p];
                int lx = (int)(pl.x - offsetx);
                int ly = (int)(pl.y - offsety);

                if (lx >= 0 && lx < width && ly >= 0 && ly < height)
                {
                    int i = lx + ly * width;

                    var l = buffer[i];
                    float mark = pl.od ? pl.range + 1000f : pl.range;
                    l.a = mark;
                    l.r = pl.r;
                    l.g = pl.g;
                    l.b = pl.b;
                    buffer[i] = l;

                    var outL = light[i];
                    outL.r = pl.r * pl.intensivit;
                    outL.g = pl.g * pl.intensivit;
                    outL.b = pl.b * pl.intensivit;
                    outL.a = math.saturate(pl.intensivit);
                    light[i] = outL;

                }
            }

            for (int ii = 0; ii < inters; ii++)
                {
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int i = x + y * width;
                            Propagate(i, x - 1, y); // von links
                            Propagate(i, x, y - 1); // von unten
                        }
                    }

                    // Rückwärts-Pass
                    for (int y = height - 1; y >= 0; y--)
                    {
                        for (int x = width - 1; x >= 0; x--)
                        {
                            int i = x + y * width;
                            Propagate(i, x + 1, y); // von rechts
                            Propagate(i, x, y + 1); // von oben
                        }
                    }

                }
            
        }
        private void Propagate(int i, int nx, int ny)
        {
            if (nx < 0 || nx >= width || ny < 0 || ny >= height) return;
            int nIdx = nx + ny * width;

            var neighborBuf = buffer[nIdx];

            bool isOverdraw = neighborBuf.a > 1000f;
            float nRange = isOverdraw ? neighborBuf.a - 1000f : neighborBuf.a;
            if (nRange <= 0.1f) return;

            float loss = (isOverdraw ? 0.1f : GetPropagationFactor(i));
            float newRange = nRange - loss;

            if (newRange <= 0.1f) return;

            var currentBuf = buffer[i];
            float cRange = currentBuf.a > 1000f ? currentBuf.a - 1000f : currentBuf.a;
         
            if (newRange > cRange)
            {
                currentBuf.a = isOverdraw ? newRange + 1000f : newRange;
                currentBuf.r = neighborBuf.r;
                currentBuf.g = neighborBuf.g;
                currentBuf.b = neighborBuf.b;
                buffer[i] = currentBuf;

                var outL = light[i];
                float falloff = math.saturate(newRange / 2.0f);
                outL.r = math.lerp(outL.r, currentBuf.r, falloff);
                outL.g = math.lerp(outL.g, currentBuf.g, falloff);
                outL.b = math.lerp(outL.b, currentBuf.b, falloff);
                outL.a = math.max(outL.a, falloff);
                light[i] = outL;

            }
        }

        private float GetPropagationFactor(int i)
        {
           
            float value = 0.1f; 

            if (bock[i].fsolid == 1) value += 0.8f;
            if (bock[i].bsoild == 1) value += 0.2f;

            if (wt && bock[i].fluid > 10f) value += 0.2f;

            return value;
        }
    }
    
    [BurstCompile]
    public struct BlurPassJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<LightMapData> ReadBuffer;
        public NativeArray<LightMapData> WriteBuffer;
        public int Width;
        public int Height;
        public bool Horizontal;

        public void Execute(int index)
        {
            int x = index % Width;
            int y = index / Width;

            float sR = 0, sG = 0, sB = 0, sA = 0;
            float kernelSum = 16f;
            // Kernel: 1, 4, 6, 4, 1

            for (int k = -2; k <= 2; k++)
            {
                int ix = Horizontal ? math.clamp(x + k, 0, Width - 1) : x;
                int iy = Horizontal ? y : math.clamp(y + k, 0, Height - 1);

                var sample = ReadBuffer[iy * Width + ix];
                float weight = GetWeight(k);

                sR += sample.r * weight;
                sG += sample.g * weight;
                sB += sample.b * weight;
                sA += sample.a * weight;
            }

            WriteBuffer[index] = new LightMapData
            {
                r = sR / kernelSum,
                g = sG / kernelSum,
                b = sB / kernelSum,
                a = sA / kernelSum
            };
        }

        private float GetWeight(int k)
        {
            switch (k)
            {
                case -2: return 1f;
                case -1: return 4f;
                case 0: return 6f;
                case 1: return 4f;
                case 2: return 1f;
                default: return 0f;
            }
        }
    }

    private static void DyeOnWater(LightRegion region, int worldHeight)
    {
        if (!WorldGen.waterActive)
            return;

        for (int lx = 0; lx < region.Width; lx++)
        {
            int worldX = lx + region.OffsetX;

            for (int wy = region.Height - 1; wy >= 0; wy--)
            {
                int worldY = wy + region.OffsetY;
                int idx = region.GetBufferIndex(worldX, worldY);
                if (idx == -1) continue;

                bool up = false;
                
                var l = region.BLMD[idx];

                if (WorldGen.GetFluidLevelFromChunkAtWorldPosition(worldX, worldY + 1) >= 0.1)
                {
                    l.r = 0.2f;
                    l.g = 0.6f;
                    l.b = .8f;
                    region.BLMD[idx] = l;
                    up = true;
                
                }

                if (!up)
                    continue;
                
                if (WorldGen.GetFluidLevelFromChunkAtWorldPosition(worldX + 1, worldY) >= .25f)
                {
                    
                    int idx2 = region.GetBufferIndex(worldX - 1, worldY);
                    if(idx2 == -1) continue;
                    var l2 = region.BLMD[idx2];
                    
                    l2.r = 0.2f;
                    l2.g = 0.6f;
                    l2.b = .8f;

                    region.BLMD[idx2]= l2;

                    idx2 = region.GetBufferIndex(worldX + 1, worldY);
                    if (idx2 == -1) continue;
                    var l3 = region.BLMD[idx2];

                    l3.r = 0.2f;
                    l3.g = 0.6f;
                    l3.b = .8f;
                    region.BLMD[idx2]= l3;
                }
                if (WorldGen.GetFluidLevelFromChunkAtWorldPosition(worldX - 1, worldY) >= .25f)
                {
                    
                    int idx2 = region.GetBufferIndex(worldX - 1, worldY);
                    if (idx2 == -1) continue;
                    var l2 = region.BLMD[idx2];

                    l2.r = 0.2f;
                    l2.g = 0.6f;
                    l2.b = .8f;

                    region.BLMD[idx2] = l2;

                    idx2 = region.GetBufferIndex(worldX + 1, worldY);
                    if (idx2 == -1) continue;
                    var l3 = region.BLMD[idx2];

                    l3.r = 0.2f;
                    l3.g = 0.6f;
                    l3.b = .8f;
                    region.BLMD[idx2] = l3;

                }
            }

        }
    }
    public static void UpdateDynamicLight()
    {
        Dictionary<Chunk, LightMapData[]> lightBuffers = new Dictionary<Chunk, LightMapData[]>();

        foreach (SpotLight s in spotLights)
        {
            foreach (var pos in s.BlockInfo)
            {
                Chunk ch = WorldGen.GetChunkAtWorldPos(pos.x, pos.y);
                if (ch == null) continue;

                if (!lightBuffers.TryGetValue(ch, out var buffer))
                {
                    int size = ch.chunkSize * ch.chunkSize;
                    buffer = new LightMapData[size];

                    for (int i = 0; i < size; i++)
                        buffer[i] = new LightMapData();

                    lightBuffers[ch] = buffer;
                }

                int localX = pos.x - ch.chunkOrigin.x;
                int localY = pos.y - ch.chunkOrigin.y;

                if (localX < 0 || localX >= ch.chunkSize) continue;
                if (localY < 0 || localY >= ch.chunkSize) continue;

                int index = localX + localY * ch.chunkSize;

                LightMapData l;
                l.r = s.r;
                l.g = s.g;
                l.b = s.b;
                l.a = s.intemsity;

                buffer[index] = l;
            }
        }

        foreach (var pair in lightBuffers)
        {
            Chunk chunk = pair.Key;
            var arr = pair.Value;

            var native = new NativeArray<LightMapData>(arr.Length, Allocator.Temp);
            NativeArray<LightMapData>.Copy(arr, native, arr.Length);
            
            ApplySmoothLightBlur(2, native);

            chunk.UpdateDynamicLightmap(native);

            native.Dispose();
        }
    }
    private static void ApplySmoothLightBlur(int iterations, NativeArray<LightMapData> a)
    {
        int w = chunkSize; int h = chunkSize;
        NativeArray<LightMapData> read = a;
        NativeArray<LightMapData> write = new NativeArray<LightMapData>(chunkSize * chunkSize, Allocator.Temp);
        float[] blurKernel = { 1f, 4f, 6f, 4f, 1f };
        float kernelSum = 16f;

        for (int it = 0; it < iterations; it++)
        {
            for (int pass = 0; pass < 2; pass++) // Horizontal & Vertical
            {
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        float sR = 0, sG = 0, sB = 0, sA = 0;
                        for (int k = -2; k <= 2; k++)
                        {
                            int ix = (pass == 0) ? math.clamp(x + k, 0, w - 1) : x;
                            int iy = (pass == 0) ? y : math.clamp(y + k, 0, h - 1);
                            var sample = read[iy * w + ix];
                            float weight = blurKernel[k + 2];
                            sR += sample.r * weight; sG += sample.g * weight;
                            sB += sample.b * weight; sA += sample.a * weight;
                        }
                        write[y * w + x] = new LightMapData
                        {
                            r = sR / kernelSum,
                            g = sG / kernelSum,
                            b = sB / kernelSum,
                            a = sA / kernelSum
                        };
                    }
                }
                var tmp = read; read = write; write = tmp;
            }
        }
        if (read != a) read.CopyTo(a);

        write.Dispose();
    }

    public static JobHandle ScheduleBlur(LightRegion region, int iterations, JobHandle dependency)
    {
        int total = region.Width * region.Height;
        JobHandle currentHandle = dependency;

        NativeArray<LightMapData> bufferA = region.BLMD;
        NativeArray<LightMapData> bufferB = region.temp;

        for (int i = 0; i < iterations; i++)
        {
            // Horizontaler Pass
            var horizJob = new BlurPassJob
            {
                ReadBuffer = bufferA,
                WriteBuffer = bufferB,
                Width = region.Width,
                Height = region.Height,
                Horizontal = true
            };
            currentHandle = horizJob.Schedule(total, 64, currentHandle);

            // Vertikaler Pass
            var vertJob = new BlurPassJob
            {
                ReadBuffer = bufferB,
                WriteBuffer = bufferA,
                Width = region.Width,
                Height = region.Height,
                Horizontal = false
            };
            currentHandle = vertJob.Schedule(total, 64, currentHandle);
        }

        return currentHandle;
    }

 
    private static void ApplySmoothLightBlur(LightRegion region, int iterations, NativeArray<LightMapData> a)
    {
        int w = region.Width; int h = region.Height;
        NativeArray<LightMapData> read = a;
        NativeArray<LightMapData> write = region.temp;
        float[] blurKernel = { 1f, 4f, 6f, 4f, 1f };
        float kernelSum = 16f;

        for (int it = 0; it < iterations; it++)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        float sR = 0, sG = 0, sB = 0, sA = 0;
                        for (int k = -2; k <= 2; k++)
                        {
                            int ix = (pass == 0) ? math.clamp(x + k, 0, w - 1) : x;
                            int iy = (pass == 0) ? y : math.clamp(y + k, 0, h - 1);
                            var sample = read[iy * w + ix];
                            float weight = blurKernel[k + 2];
                            sR += sample.r * weight; sG += sample.g * weight;
                            sB += sample.b * weight; sA += sample.a * weight;
                        }
                        write[y * w + x] = new LightMapData
                        {
                            r = sR / kernelSum,
                            g = sG / kernelSum,
                            b = sB / kernelSum,
                            a = sA / kernelSum
                        };
                    }
                }
                var tmp = read; read = write; write = tmp;
            }
        }
        if (read != a) read.CopyTo(a);
    }
  
    public static LightMapData GetLightColorGlobal(int worldX, int worldY)
    {
        Chunk chunk = WorldGen.GetChunkAtWorldPos(worldX, worldY);
        if (chunk == null) return new LightMapData { r = 0, g = 0, b = 0, a = 0 }; 

        int lx = worldX - chunk.chunkOrigin.x;
        int ly = worldY - chunk.chunkOrigin.y;

        lx = Mathf.Clamp(lx, 0, WorldGen.chunkSize - 1);
        ly = Mathf.Clamp(ly, 0, WorldGen.chunkSize - 1);

        return chunk.LMD[lx + ly * WorldGen.chunkSize];
    }

    private static void ApplyChunkToTexture(LightRegion region)
    {
        int padding = 2;
        int paddedSize = chunkSize + (padding * 2);

        if (!LightData.IsCreated)
            LightData = new NativeArray<LightMapData>(paddedSize * paddedSize, Allocator.Persistent);
        else
            NativeArray<LightMapData>.Copy(region.ClearLightData, LightData, LightData.Length);

        foreach (Chunk c in region.chunks)
        {
            int index = 0;

            for (int y = -padding; y < chunkSize + padding; y++)
            {
                int worldY = c.chunkOrigin.y + y;
                int chunkY = y >= 0 && y < chunkSize ? y : -1;

                for (int x = -padding; x < chunkSize + padding; x++)
                {
                    int worldX = c.chunkOrigin.x + x;
                    int chunkX = x >= 0 && x < chunkSize ? x : -1;

                    var lm = LightData[index];

                    if (chunkX != -1 && chunkY != -1)
                    {
                        // schneller direkter Index
                        int cid = chunkX + chunkY * chunkSize;
                        lm.r = c.LMD[cid].r;
                        lm.g = c.LMD[cid].g;
                        lm.b = c.LMD[cid].b;
                        lm.a = c.LMD[cid].a;
                    }
                    else
                    {
                        int idx = region.GetBufferIndex(worldX, worldY);
                        if (idx != -1)
                            lm = region.BLMD[idx];
                        else
                        {
                            var cg = GetLightColorGlobal(worldX, worldY);
                            lm.r = cg.r;
                            lm.g = cg.g;
                            lm.b = cg.b;
                            lm.a = cg.a;
                        }
                    }

                    LightData[index] = lm;
                    index++;
                }
            }

            c.UpdateChunkLightmap(LightData);
        }
    }



}