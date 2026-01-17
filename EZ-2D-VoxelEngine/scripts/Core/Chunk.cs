
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;


[Serializable]
public struct FluidCell
{
    public bool Active;
    public int Level;
    public int worldX, worldY; 

    public FluidCell(bool a, int x, int y, int l)
    {
        Active = a; Level = l;
        worldX = x; worldY = y;
    }

}
[Serializable]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class Chunk : MonoBehaviour
{

    [Serializable]
    public class ChunkData
    {

        public Block[] chunkFB;
        public Block[] chunkBB;
        public int[] wBlocks;
        public float[] wNext;
        public int[] sheight;
    }


    public class MeshDataRegion
    {
        public NativeList<MyVertex> vertices;
        public NativeList<ushort> trianglesF;
        public NativeList<ushort> trianglesB;

        public MeshDataRegion()
        {
            vertices = new NativeList<MyVertex>(256, Allocator.Persistent);
            trianglesF = new NativeList<ushort>(384, Allocator.Persistent);
            trianglesB = new NativeList<ushort>(384, Allocator.Persistent);
        }

        public void ClearUp()
        {
            if (vertices.IsCreated) vertices.Clear();
            if (trianglesF.IsCreated) trianglesF.Clear();
            if (trianglesB.IsCreated) trianglesB.Clear();
        }

        public void Dispose()
        {
            if (vertices.IsCreated) vertices.Dispose();
            if (trianglesF.IsCreated) trianglesF.Dispose();
            if (trianglesB.IsCreated) trianglesB.Dispose();
        }
    }

    public int chunkSize = 128;
    public Vector2Int chunkOrigin;

    private int regionSize = 8;
    private int regionCount;

    private bool[] regionDirty;
    private MeshDataRegion[] regionData;

    public TextureAtlas atlas;

    public Material frontM, backM;
    public Material water;


    public GameObject FluidObject;

    public MeshRenderer fluiddRen;

    public RenderTexture lightMapTexture, dynamicLightMapTexture;

    private bool[] frDirty;
    private MeshDataRegion[] frData;


    public int chunkIndexX;
    public int chunkIndexY;

    public Block[] chunkFrontBlocks;
    public Block[] chunkBackBlocks;

    public FluidCell[] WaterBlocks; 

    public LightMapData[] LMD;


    public ComputeBuffer lightBuffer;

    public MeshFilter[] reginObjecte;
    private Mesh[] regionMeshes;

    public MeshFilter[] FluidRegionObjecte;
    private Mesh[] FluidRegionMeshes;

    public Chunk neighborLeft;
    public Chunk neighborRight;
    public Chunk neighborBottom;
    public Chunk neighborTop;

    public int[] heightMap;
    public bool noGen, Generadet;

    private HashSet<Vector2Int> bufferFuildRegions = new HashSet<Vector2Int>();
    public bool UpdateFluidBufferRegion = false;


    private void OnDrawGizmos()
    {
        Gizmos.color = Color.orange;
        Vector2 center = (Vector2)transform.position + new Vector2(WorldGen.chunkSize, WorldGen.chunkSize) * .5f;
        Vector2 size = new Vector2(WorldGen.chunkSize, WorldGen.chunkSize);
        Gizmos.DrawWireCube(center, size);
    }

    public void Generate(Vector2Int worldOrigin, int iX, int iY, int regionSize, lightmodes type)
    {
        this.regionSize = regionSize;

        int pw = WorldGen.chunkSize;
        chunkSize = pw;

        TextureAtlas.Init();

        frontM.mainTexture = atlas.texture;
        backM.mainTexture = atlas.texture;
        heightMap = new int[chunkSize];
        chunkOrigin = worldOrigin;

        chunkIndexX = iX;
        chunkIndexY = iY;


        lightBuffer = new ComputeBuffer(((pw + 4) * (pw + 4)), sizeof(float) * 4);


        if (lightMapTexture == null)
        {
            lightMapTexture = new RenderTexture((pw + 4), (pw + 4), 1, RenderTextureFormat.ARGBFloat);
            lightMapTexture.name = chunkIndexX + "_" + chunkIndexY;

            dynamicLightMapTexture = new RenderTexture(pw, pw, 1, RenderTextureFormat.ARGBFloat);

            if (type == lightmodes.PixelLight)
            {
                lightMapTexture.filterMode = FilterMode.Point;
                dynamicLightMapTexture.filterMode = FilterMode.Point;
            }
            else if (type == lightmodes.SmoothLight)
            {
                lightMapTexture.filterMode = FilterMode.Bilinear;
                dynamicLightMapTexture.filterMode = FilterMode.Bilinear;
            }

            lightMapTexture.wrapMode = TextureWrapMode.Clamp;
            dynamicLightMapTexture.wrapMode = TextureWrapMode.Clamp;

            lightMapTexture.enableRandomWrite = true;
            dynamicLightMapTexture.enableRandomWrite = true;

            lightMapTexture.Create();
            dynamicLightMapTexture.Create();
        }



        regionCount = Mathf.CeilToInt(chunkSize / (float)regionSize);
        regionDirty = new bool[regionCount * regionCount];
        regionData = new MeshDataRegion[regionCount * regionCount];
        reginObjecte = new MeshFilter[regionCount * regionCount];
        regionMeshes = new Mesh[regionCount * regionCount];

        for (int rx = 0; rx < regionCount; rx++)
        {
            for (int ry = 0; ry < regionCount; ry++)
            {
                GameObject go = new GameObject("Region_" + rx + "_" + ry);
                go.transform.parent = transform;
                go.transform.localPosition = Vector3.zero;

                var mf = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = new Material[] { frontM, backM };

                Material[] ms = mr.materials;
                ms[0].SetTexture("_LightMap", lightMapTexture);
                ms[0].SetTexture("_LightMapD", dynamicLightMapTexture);
                ms[0].SetFloat("_ColorM", 1f);
                ms[0].renderQueue = 4500;

                ms[1].SetTexture("_LightMap", lightMapTexture);
                ms[1].SetTexture("_LightMapD", dynamicLightMapTexture);
                ms[1].SetFloat("_ColorM", 0.05f);
                ms[1].renderQueue = 2000;

                int index = ry * regionCount + rx;

                regionMeshes[index] = new Mesh();
                regionMeshes[index].indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

                reginObjecte[index] = mf;

                regionData[index] = new MeshDataRegion();
                regionDirty[index] = true;
            }
        }




        if (WorldGen.waterActive)
        {

            WaterBlocks = new FluidCell[chunkSize * chunkSize];

            for (int i = 0; i < WaterBlocks.Length; i++)
            {
                int x = i % chunkSize;
                int y = i / chunkSize;

                WaterBlocks[i] = new FluidCell(false, chunkOrigin.x + x, chunkOrigin.y + y, 0);
            }

            frData = new MeshDataRegion[regionCount * regionCount];

            FluidRegionObjecte = new MeshFilter[regionCount * regionCount];
            FluidRegionMeshes = new Mesh[regionCount * regionCount];

            frDirty = new bool[regionCount * regionCount];

            for (int rx = 0; rx < regionCount; rx++)
            {
                for (int ry = 0; ry < regionCount; ry++)
                {
                    GameObject go = new GameObject("FluidRegion_" + rx + "_" + ry);
                    go.transform.parent = transform;
                    go.transform.localPosition = Vector3.zero;

                    var mf = go.AddComponent<MeshFilter>();
                    var mr = go.AddComponent<MeshRenderer>();

                    mr.sharedMaterials = new Material[] { water };
                    Material[] mm = mr.materials;

                    int index = ry * regionCount + rx;

                    FluidRegionMeshes[index] = new Mesh();
                    FluidRegionMeshes[index].indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

                    mm[0].SetTexture("_Lightmap", lightMapTexture);
                    mm[0].renderQueue = 4000;

                    FluidRegionObjecte[index] = mf;

                    frData[index] = new MeshDataRegion();
                    frDirty[index] = true;
                }
            }
        }

        LMD = new LightMapData[pw * pw];

       
            chunkFrontBlocks = new Block[chunkSize * chunkSize];
            chunkBackBlocks = new Block[chunkSize * chunkSize];

            for (int i = 0; i < chunkSize; i++)
            {
                for (int j = 0; j < chunkSize; j++)
                {
                    int worldX = chunkOrigin.x + i;
                    int worldY = chunkOrigin.y + j;

                    int id = i + j * chunkSize;
                    chunkFrontBlocks[id] = new Block(BlockType.Air, worldX, worldY);
                    chunkBackBlocks[id] = new Block(BlockType.Air, worldX, worldY);

                }
            }
        

        Generadet = true;
    }


    
    private void SaveData(int idx, int idy)
    {

        string root = Path.Combine(Application.persistentDataPath, "ChunkBuffer");

        if (!Directory.Exists(root))
            Directory.CreateDirectory(root);

        string data = "Cx" + idx + "_y" + idy + ".data";

        var fb = Path.Combine(Application.persistentDataPath, "ChunkBuffer", data);

        using (FileStream file = new FileStream(fb, FileMode.Create))
        {
            BinaryFormatter bf = new BinaryFormatter();
            bf.Serialize(file, new ChunkData()
            {
                chunkFB = chunkFrontBlocks,
                chunkBB = chunkBackBlocks,
                sheight = heightMap
            });
        }

    }

    private bool Load(int idx, int idy)
    {
        string data = "Cx" + idx + "_y" + idy + ".data";
        var fb = Path.Combine(Application.persistentDataPath, "ChunkBuffer", data);

        if (File.Exists(fb))
        {
            noGen = true;
            using (FileStream file = File.Open(fb, FileMode.Open))
            {
                BinaryFormatter bf = new BinaryFormatter();
                ChunkData cd = (ChunkData)bf.Deserialize(file);
                chunkFrontBlocks = cd.chunkFB;
                chunkBackBlocks = cd.chunkBB;
                heightMap = cd.sheight;
            }

            return true;
        }

        return false;
    }
    private void OnDestroy()
    {
        ClearBufferChunkData();

        if(regionData != null)
        foreach (var i in regionData)
            i.Dispose();

        if (WorldGen.waterActive)
            if(frData != null)
            foreach (var i in frData)
                i.Dispose();
    

        
        if (lightBuffer != null)
            lightBuffer.Dispose();

        if (lightMapTexture != null)
            lightMapTexture.Release();

        if (dynamicLightMapTexture != null)
            dynamicLightMapTexture.Release();
    }

    private void OnDisable()
    {
        SaveData(chunkIndexX, chunkIndexY);
    }

    private void OnEnable()
    {
        if(Generadet)
            Load(chunkIndexX, chunkIndexY);
    }

    public void ClearBufferChunkData()
    {
        int idx = chunkIndexX; int idy= chunkIndexY;
        string data = "Cx" + idx + "_y" + idy + ".data";
        var fb = Path.Combine(Application.persistentDataPath, "ChunkBuffer", data);

        if (File.Exists(fb))
        { 
            File.Delete(fb);
        }
    }
   

    public void GenerateHeight(string seed)
    {
        if (noGen)
            return;

        int[] h = WorldGenerator.GenerateWorldFromSeed(seed, chunkOrigin.x, WorldGen.chunkSize, WorldGen.Instance.GroundHeight, WorldGen.MaxTerrainHeight);

        for (int x = 0; x < chunkSize; x++)
            heightMap[x] = h[x];
    }

    public int GetSurfacePosition(int worldx, int worldy)
    {
        int x = worldx - chunkOrigin.x;
        int y = worldy - chunkOrigin.y;

        if (x < 0 || y < 0 || x >= chunkSize || y >= chunkSize)
            return chunkOrigin.y + chunkSize;

        return heightMap[x];
    }

    public void GetNeighbors()
    {
        neighborLeft = WorldGen.GetChunkAtWorldPos(chunkOrigin.x - 1, chunkOrigin.y);
        neighborRight = WorldGen.GetChunkAtWorldPos(chunkOrigin.x + chunkSize + 1, chunkOrigin.y);
        neighborBottom = WorldGen.GetChunkAtWorldPos(chunkOrigin.x, chunkOrigin.y - 1);
        neighborTop = WorldGen.GetChunkAtWorldPos(chunkOrigin.x, chunkOrigin.y + chunkSize + 1);
    }

    public void SetLightBlockAt(int worldX, int worldY, LightMapData data)
    {
        int posx = worldX - chunkOrigin.x;
        int posy = worldY - chunkOrigin.y;

        if (posx < 0 || posx >= chunkSize || posy < 0 || posy >= chunkSize)
        {
            return ;
        }

        int id = posx + posy * chunkSize;

        LMD[id].r = data.r;
        LMD[id].g = data.g;
        LMD[id].b = data.b;
        LMD[id].a = data.a;

    }
    public LightMapData GetLightDataAt(int worldX, int worldY)
    {
        int posx = worldX - chunkOrigin.x;
        int posy = worldY - chunkOrigin.y;

        if (posx < 0 || posx >= chunkSize || posy < 0 || posy >= chunkSize)
        {
            return LMD[0];
        }

        int id = posx + posy * chunkSize;
        return LMD[id];
    }

    public Color GetLightBlockAt(int worldX, int worldY)
    {
        int posx = worldX - chunkOrigin.x;
        int posy = worldY - chunkOrigin.y;

        if (posx < 0 || posx >= chunkSize || posy < 0 || posy >= chunkSize)
        {
            return new Color(0,0,0,0);
        }

        int id = posx + posy * chunkSize;
        return new Color(LMD[id].r, LMD[id].g, LMD[id].b, LMD[id].a);
    }
    /// <summary>
    /// **Core: UpdateChunkLightmap**
    /// <para>Updates the lighting texture/map for a specific chunk using a Compute Shader.</para>
    /// <para>This is an internal core function and should **not** be called directly by users.</para>
    /// </summary>
    /// <param name="chunk">The chunk whose lightmap needs to be recalculated.</param>
    public void UpdateChunkLightmap(NativeArray<LightMapData> data)
    {
        int bufferLength = (WorldGen.chunkSize + 4) * (WorldGen.chunkSize + 4);

        if (lightBuffer == null || !lightBuffer.IsValid())
        {
            if (lightBuffer != null)
                lightBuffer.Release();

            lightBuffer = new ComputeBuffer(bufferLength, sizeof(float) * 4);
        }

        if (data.IsCreated)
            lightBuffer.SetData(data);
        else return;

        int size = chunkSize + 4;
        var w = WorldGen.Instance;
        var k = WorldGen.kernel;
        w.compute.SetInt("width", size);
        w.compute.SetInt("height", size);

        w.compute.SetBuffer(k, "LightData", lightBuffer);
        w.compute.SetTexture(k, "Result", lightMapTexture);
        int groups = Mathf.CeilToInt((size) / 8f);

        w.compute.Dispatch(k, groups, groups, 1);
    }
    public void UpdateDynamicLightmap(NativeArray<LightMapData> data)
    {
        int bufferLength = (WorldGen.chunkSize + 4) * (WorldGen.chunkSize + 4);
        
        if (lightBuffer == null || !lightBuffer.IsValid())
        {
            if (lightBuffer != null)
                lightBuffer.Release();

            lightBuffer = new ComputeBuffer(bufferLength, sizeof(float) * 4);
        }

        if (data.IsCreated)
            lightBuffer.SetData(data);
        else return;
        
        int size = chunkSize;
        var w = WorldGen.Instance;
        var k = WorldGen.kernel;
        w.compute.SetInt("width", size);
        w.compute.SetInt("height", size);

        w.compute.SetBuffer(k, "LightData", lightBuffer);
        w.compute.SetTexture(k, "Result", dynamicLightMapTexture);
        int groups = Mathf.CeilToInt((size) / 8f);

        w.compute.Dispatch(k, groups, groups, 1);
    }
    private void BuildFluidRegionMesh(int rx, int ry)
    {
        BuildFluidRegion(rx, ry);
        int index = rx + ry * regionCount;
        var region = frData[index];
        var mesh = FluidRegionMeshes[index];

        mesh.Clear();

        var layout = new[] {
         new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
         new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
         new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2)
        };

        mesh.SetVertexBufferParams(region.vertices.Length, layout);
        mesh.SetIndexBufferParams(region.trianglesF.Length, IndexFormat.UInt16);

        mesh.SetVertexBufferData(region.vertices.AsArray(), 0, 0, region.vertices.Length);

        int frontCount = region.trianglesF.Length;

        mesh.SetIndexBufferData(region.trianglesF.AsArray(), 0, 0, frontCount);
        
        mesh.SetSubMesh(0, new SubMeshDescriptor(0, frontCount));
        
        mesh.RecalculateBounds(); 

        FluidRegionObjecte[index].sharedMesh = mesh;

    }
    private void BuildFluidRegion(int rx, int ry)
    {
        int index = rx + ry * regionCount;
        MeshDataRegion region = frData[index];
        region.ClearUp();   

        int startX = rx * regionSize;
        int startY = ry * regionSize;
        int endX = Mathf.Min(startX + regionSize, chunkSize);
        int endY = Mathf.Min(startY + regionSize, chunkSize);


        for (int x = startX; x < endX; x++)
        {
            for (int y = startY; y < endY; y++)
            {
                    float level = Mathf.Clamp01(WaterBlocks[x + y * chunkSize].Level / 100f);

                    if(level > 0.1f)
                        AddWaterMesh(level, x, y,
                                     region.vertices, region.trianglesF);

                        if (level > 0.5f)
                            MarkRegionDirtyLocal(x, y);

                    
                
            }
        }

    }

   
    public void MarkFluidRegionDirty(int worldX, int worldY)
    {
        int posx = worldX - chunkOrigin.x;
        int posy = worldY - chunkOrigin.y;

        int regionX = Mathf.FloorToInt(posx / (float)regionSize);
        int regionY = Mathf.FloorToInt(posy / (float)regionSize);

        int index = regionY * regionCount + regionX;
        if (regionX >= 0 && regionX < regionCount && regionY >= 0 && regionY < regionCount)
           frDirty[index] = true;

    }
    public void MarkFluidRegionDirtyLocal(int localX, int localY)
    {
        
        int regionX = Mathf.FloorToInt(localX / (float)regionSize);
        int regionY = Mathf.FloorToInt(localY / (float)regionSize);

        int index = regionY * regionCount + regionX;
        if (regionX >= 0 && regionX < regionCount && regionY >= 0 && regionY < regionCount)
            frDirty[index] = true;

    }

   
    private void UpdateFluid()
    {
     
            for (int rx = 0; rx < regionCount; rx++)
            {
                for (int ry = 0; ry < regionCount; ry++)
                {
                    int index = ry * regionCount + rx;
                    if (frDirty[index])
                    {
                        BuildFluidRegionMesh(rx, ry);
                        frDirty[index] = false;
                    }
                }
            }
        
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct MyVertex
    {
        public Vector3 position;
        public Vector2 uv0; 
        public Vector2 uv1;
    }

    private void BuildRegionMesh(int rx, int ry)
    {
        BuildRegion(rx, ry);
        int index = ry * regionCount + rx;
        var region = regionData[index];
        var mesh = regionMeshes[index];

        mesh.Clear();

        var layout = new[] {
         new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
         new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
         new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2)
        };

        mesh.SetVertexBufferParams(region.vertices.Length, layout);
        mesh.SetIndexBufferParams(region.trianglesF.Length + region.trianglesB.Length, IndexFormat.UInt16);

        mesh.SetVertexBufferData(region.vertices.AsArray(), 0, 0, region.vertices.Length);

        int frontCount = region.trianglesF.Length;
        int backCount = region.trianglesB.Length;

        mesh.SetIndexBufferData(region.trianglesF.AsArray(), 0, 0, frontCount);
        mesh.SetIndexBufferData(region.trianglesB.AsArray(), 0, frontCount, backCount);

        mesh.subMeshCount = 2;
        mesh.SetSubMesh(0, new SubMeshDescriptor(0, frontCount));
        mesh.SetSubMesh(1, new SubMeshDescriptor(frontCount, backCount));

        mesh.RecalculateBounds(); 


        reginObjecte[index].sharedMesh = mesh;

    }

    private void BuildRegion(int rx, int ry)
    {
        int index = ry * regionCount + rx;
        regionData[index].ClearUp();


        int startX = rx * regionSize;
        int startY = ry * regionSize;
        int endX = Mathf.Min(startX + regionSize, chunkSize);
        int endY = Mathf.Min(startY + regionSize, chunkSize);


        for (int x = startX; x < endX; x++)
        {
            for (int y = startY; y < endY; y++)
            {

                int id = x + y * chunkSize;
                Block block = chunkFrontBlocks[id];

                if (block.IsSolid())
                {

                    AddBlockMesh(block, x, y,
                        regionData[index].vertices, regionData[index].trianglesF,
                        RuleTileCached(x, y), 0);
                }
                else
                {

                    Block backBlock = chunkBackBlocks[id];

                    if (backBlock.IsSolid())
                    {

                        AddBlockMesh(backBlock, x, y,
                            regionData[index].vertices, regionData[index].trianglesB,
                           0, -0.1f);
                    }
                }
            }
        }

    }

    private void AddBlockMesh(
    Block block, int posX, int posY,
    NativeList<MyVertex> verts,
    NativeList<ushort> tris,
    int DirType, float zOffset = 0)
    {
        ushort vIndex = (ushort)verts.Length;

        // UVs holen
        int texID = block.GetTextureID(DirType);
        Vector2[] uvs = TextureAtlas.Texture[texID];

        // Licht UVs berechnen
        float paddedSize = (float)chunkSize + 4f;
        float paddingOffset = 2f;
        float u = (posX + paddingOffset) / paddedSize;
        float v = (posY + paddingOffset) / paddedSize;
        float du = 1f / paddedSize;
        float dv = 1f / paddedSize;

        // 4 Vertices direkt in die Struktur schreiben
        // Unten Links
        verts.Add(new MyVertex { position = new Vector3(posX, posY, zOffset), uv0 = uvs[0], uv1 = new Vector2(u, v) });
        // Oben Links
        verts.Add(new MyVertex { position = new Vector3(posX, posY + 1, zOffset), uv0 = uvs[1], uv1 = new Vector2(u, v + dv) });
        // Unten Rechts
        verts.Add(new MyVertex { position = new Vector3(posX + 1, posY, zOffset), uv0 = uvs[2], uv1 = new Vector2(u + du, v) });
        // Oben Rechts
        verts.Add(new MyVertex { position = new Vector3(posX + 1, posY + 1, zOffset), uv0 = uvs[3], uv1 = new Vector2(u + du, v + dv) });

        // Triangles hinzufügen
        tris.Add(vIndex); tris.Add((ushort)(vIndex + 1)); tris.Add((ushort)(vIndex + 2));
        tris.Add((ushort)(vIndex + 2)); tris.Add((ushort)(vIndex + 1)); tris.Add((ushort)(vIndex + 3));
    }

    private void AddWaterMesh(float level, int posX, int posY, NativeList<MyVertex> verts, NativeList<ushort> tris)
    {
        if (level < 0.05f) return;

        ushort vIndex = (ushort)verts.Length;
        
        float topY = posY + Mathf.Clamp01(level);

        float paddedSize = (float)chunkSize + 4f;
        float paddingOffset = 2f;
        float u = (posX + paddingOffset) / paddedSize;
        float v = (posY + paddingOffset) / paddedSize;
        float du = 1f / paddedSize;
        float dv = 1f / paddedSize;

        // Unten Links
        verts.Add(new MyVertex { position = new Vector3(posX, posY, 0), uv0 = Vector2.zero, uv1 = new Vector2(u, v) });
        // Oben Links
        verts.Add(new MyVertex { position = new Vector3(posX, topY, 0), uv0 = Vector2.zero, uv1 = new Vector2(u, v + dv) });
        // Unten Rechts
        verts.Add(new MyVertex { position = new Vector3(posX + 1, posY, 0), uv0 = Vector2.zero, uv1 = new Vector2(u + du, v) });
        // Oben Rechts
        verts.Add(new MyVertex { position = new Vector3(posX + 1, topY, 0), uv0 = Vector2.zero, uv1 = new Vector2(u + du, v + dv) });

        tris.Add(vIndex); tris.Add((ushort)(vIndex + 1)); tris.Add((ushort)(vIndex + 2));
        tris.Add((ushort)(vIndex + 2)); tris.Add((ushort)(vIndex + 1)); tris.Add((ushort)(vIndex + 3));
    }
    private void updateDirtyRegion()
    {

        for (int rx = 0; rx < regionCount; rx++)
        {
            for (int ry = 0; ry < regionCount; ry++)
            {
                int index = ry * regionCount + rx;
                if (regionDirty[index])
                {
                    BuildRegionMesh(rx, ry);
                    regionDirty[index] = false;
                }
            }
        }
    }

    public void MarkRegionDirty(int worldX, int worldY)
    {
        int posx = worldX - chunkOrigin.x;
        int posy = worldY - chunkOrigin.y;

        int regionX = Mathf.FloorToInt(posx / (float)regionSize);
        int regionY = Mathf.FloorToInt(posy / (float)regionSize);
        int index = regionY * regionCount + regionX;
        if (regionX >= 0 && regionX < regionCount && regionY >= 0 && regionY < regionCount)
            regionDirty[index] = true;

    }

    public void MarkRegionDirtyLocal(int localX, int localY)
    {
        int regionX = Mathf.FloorToInt(localX / (float)regionSize);
        int regionY = Mathf.FloorToInt(localY / (float)regionSize);

        int index = regionY * regionCount + regionX;
        if (regionX >= 0 && regionX < regionCount && regionY >= 0 && regionY < regionCount)
            regionDirty[index] = true;

    }


    private int RuleTileCached(int x, int y)
    {

        Block Get(int ox, int oy) => WorldGen.GetBlockFromChunkFrontLayer(chunkOrigin.x + x + ox, chunkOrigin.y + y + oy);

        bool left = Get(-1, 0).IsSolid();
        bool up = Get(0, 1).IsSolid();
        bool right = Get(1, 0).IsSolid();
        bool down = Get(0,  -1).IsSolid();
        bool leftUp = Get(-1, 1).IsSolid();
        bool rightUp = Get(1, 1).IsSolid();

        int dirt = 0;
        BlockType type = Get(0, 0).type;


        switch (type)
        {
            case BlockType.Grass:

                if (!up && left && down && right)
                    dirt = 0;
                else if (!up && down && !left && !right)
                    dirt = 3;
                else if (!up && !left && down && right)
                    dirt = 2;
                else if (!up && left && down && !right)
                    dirt = 1;

                if (WorldGen.waterActive)
                {
                    bool lw = WorldGen.GetFluidLevelFromChunkAtWorldPosition(chunkOrigin.x + x - 1, chunkOrigin.y + y) > 0.5f;
                    bool rw = WorldGen.GetFluidLevelFromChunkAtWorldPosition(chunkOrigin.x + x + 1, chunkOrigin.y + y) > 0.5f;

                    if (lw || rw)
                        dirt = 0;
                }

                break;

            case BlockType.Dirt:

                BlockType typeUp = Get(0, 1).type;
                if (up && down && left && right && rightUp && leftUp)
                    dirt = 0;
                if (up && down && left && right && !leftUp && typeUp == BlockType.Grass)
                    dirt = 3;
                if (up && down && left && right && !rightUp && typeUp == BlockType.Grass)
                    dirt = 4;
                if (up && down && !left && right && !leftUp && typeUp == BlockType.Grass)
                    dirt = 2;
                if (up && down && left && !right && !rightUp && typeUp == BlockType.Grass)
                    dirt = 1;
                
                if (WorldGen.waterActive)
                {
                    bool lw = WorldGen.GetFluidLevelFromChunkAtWorldPosition(chunkOrigin.x + x - 1, chunkOrigin.y + y) > 0.5f;
                    bool rw = WorldGen.GetFluidLevelFromChunkAtWorldPosition(chunkOrigin.x + x + 1, chunkOrigin.y + y) > 0.5f;

                    if (lw || rw)
                        dirt = 0;
                }

                break;

            default:
                dirt = 0;
                break;
        }

        return dirt;
    }
    public bool SetFrontBlock(int worldX, int worldY, BlockType blockType)
    {

        int posx = worldX - chunkOrigin.x;
        int posy = worldY - chunkOrigin.y;


        if (posx < 0 || posx >= chunkSize || posy < 0 || posy >= chunkSize)
        {
            Debug.LogWarning("Error in Set front  Pos out of Bounds");
            return false;
        }

        chunkFrontBlocks[posx + posy * chunkSize].setBlockType(blockType);
        MarkRegionDirtyLocal(posx, posy);
       
       return true;
    }
    public bool SetFrontBlockDirect(int worldX, int worldY, BlockType blockType)
    {

        int posx = worldX - chunkOrigin.x;
        int posy = worldY - chunkOrigin.y;


        if (posx < 0 || posx >= chunkSize || posy < 0 || posy >= chunkSize)
        {
            Debug.LogWarning("Error in Set front  Pos out of Bounds");
            return false;
        }

        chunkFrontBlocks[posx + posy * chunkSize].setBlockType(blockType);

        int regionX = Mathf.FloorToInt(posx / (float)regionSize);
        int regionY = Mathf.FloorToInt(posy / (float)regionSize);

        if (regionX >= 0 && regionX < regionCount && regionY >= 0 && regionY < regionCount)
            BuildRegionMesh(regionX, regionY);

        return true;
    }

    public bool SetBackBlock(int worldX, int worldY, BlockType blockType)
    {
        int posx = worldX - chunkOrigin.x;
        int posy = worldY - chunkOrigin.y;


        if (posx < 0 || posx >= chunkSize || posy < 0 || posy >= chunkSize)
        {
            Debug.LogWarning("Error in Set BACK  Pos out of Bounds");
            return false;
        }

        chunkBackBlocks[posx + posy * chunkSize].setBlockType(blockType);

        MarkRegionDirtyLocal(posx, posy);

        return true;
      
    }

    public bool SetBackBlockDirect(int worldX, int worldY, BlockType blockType)
    {
        int posx = worldX - chunkOrigin.x;
        int posy = worldY - chunkOrigin.y;


        if (posx < 0 || posx >= chunkSize || posy < 0 || posy >= chunkSize)
        {
            Debug.LogWarning("Error in Set BACK  Pos out of Bounds");
            return false;
        }

        chunkBackBlocks[posx + posy * chunkSize].setBlockType(blockType);

        int regionX = Mathf.FloorToInt(posx / (float)regionSize);
        int regionY = Mathf.FloorToInt(posy / (float)regionSize);

        if (regionX >= 0 && regionX < regionCount && regionY >= 0 && regionY < regionCount)
            BuildRegionMesh(regionX, regionY);

        return true;

    }


    public bool ActivateFluidAt(int worldX, int worldY, bool active = false)
    {
        int localX = worldX - chunkOrigin.x;
        int localY = worldY - chunkOrigin.y;

        // Chunk-Grenzen überprüfen und an Nachbarn weiterleiten
        if (localX < 0 && neighborLeft != null)
        {
            return neighborLeft.ActivateFluidAt(worldX, worldY, active);
        }
        else if (localX >= chunkSize && neighborRight != null)
        {
            return neighborRight.ActivateFluidAt(worldX, worldY, active);
        }
        else if (localY < 0 && neighborBottom != null)
        {
            return neighborBottom.ActivateFluidAt(worldX, worldY, active);
        }
        else if (localY >= chunkSize && neighborTop != null)
        {
            return neighborTop.ActivateFluidAt(worldX, worldY, active);
        }

        // Außerhalb aller Chunks
        if (localX < 0 || localX >= chunkSize || localY < 0 || localY >= chunkSize)
            return false;

        int index = localX + localY * chunkSize;
        WaterBlocks[index].Active = active;



        return true;
    }
    public bool SetFluidAt(int worldX, int worldY, int level)
    {
        int localX = worldX - chunkOrigin.x;
        int localY = worldY - chunkOrigin.y;


        if (localX < 0 && neighborLeft != null) return neighborLeft.SetFluidAt(worldX, worldY, level);
        if (localX >= chunkSize&& neighborRight != null)
        {
            return neighborRight.SetFluidAt(worldX, worldY, level);
        }
        if (neighborBottom != null && localY < 0) return neighborBottom.SetFluidAt(worldX, worldY, level);
        if (neighborTop != null && localY >= chunkSize) return neighborTop.SetFluidAt(worldX, worldY, level);

        if (localX < 0 || localX >= chunkSize || localY < 0 || localY >= chunkSize)
            return false;


        int index = localX + localY * chunkSize;

        WaterBlocks[index].Level = level;

        MarkFluidRegionDirtyLocal(localX, localY);

        bool empty = level <= 1;

        if (empty)
        {
            WaterBlocks[index].Active = false;
            WaterBlocks[index].Level = 0;
        }
        else
            WaterBlocks[index].Active = true;



        return true;
    }


    public bool SetFluidAtinBuffer(int worldX, int worldY, int level)
    {
        int localX = worldX - chunkOrigin.x;
        int localY = worldY - chunkOrigin.y;


        if (localX < 0 && neighborLeft != null) return neighborLeft.SetFluidAtinBuffer(worldX, worldY, level);
        if (localX >= chunkSize && neighborRight != null)
        {
            return neighborRight.SetFluidAtinBuffer(worldX, worldY, level);
        }
        if (neighborBottom != null && localY < 0) return neighborBottom.SetFluidAtinBuffer(worldX, worldY, level);
        if (neighborTop != null && localY >= chunkSize) return neighborTop.SetFluidAtinBuffer(worldX, worldY, level);

        if (localX < 0 || localX >= chunkSize || localY < 0 || localY >= chunkSize)
            return false;


        int index = localX + localY * chunkSize;

        WaterBlocks[index].Level = level;
        WaterBlocks[index].Active = level > 1;

        bufferFuildRegions.Add(new Vector2Int(localX, localY));
        
        return true;
    }
    public bool SetFluidAtDirect(int worldX, int worldY, int level,bool a = false)
    {

        int posx = worldX - chunkOrigin.x;
        int posy = worldY - chunkOrigin.y;


        if (posx < 0 || posx >= chunkSize || posy < 0 || posy >= chunkSize)
        {
            Debug.LogWarning("Error in Set Fluid  Pos out of Bounds");
            return false;
        }


        int index = posx + posy * chunkSize;
        WaterBlocks[index].Active = a;
        WaterBlocks[index].Level = level;

        int regionX = Mathf.FloorToInt(posx / (float)regionSize);
        int regionY = Mathf.FloorToInt(posy / (float)regionSize);

        if (regionX >= 0 && regionX < regionCount && regionY >= 0 && regionY < regionCount)
        {
            BuildFluidRegionMesh(regionX, regionY);
            MarkRegionDirtyLocal(posx, posy);
        }

        return true;
    }

    public Block GetFrontBlock(int worldX, int worldY)
    {

        int posx = worldX - chunkOrigin.x;
        int posy = worldY - chunkOrigin.y;

        if (posx < 0 || posx >= chunkSize || posy < 0 || posy >= chunkSize)
            return new Block(BlockType.Air, 0, 0);

        return chunkFrontBlocks[posx + posy * chunkSize];
    }

    public Block GetFrontBlockLocal(int localX, int localY)
    {

        if (localX < 0 || localX >= chunkSize || localY < 0 || localY >= chunkSize)
            return WorldGen.GetBlockFromChunkFrontLayer(chunkOrigin.x + localX, chunkOrigin.y + localY);

        return chunkFrontBlocks[localX + localY * chunkSize];
    }


    public Block GetBackBlock(int worldX, int worldY)
    {
        int posx = worldX - chunkOrigin.x;
        int posy = worldY - chunkOrigin.y;

        if (posx < 0 || posx >= chunkSize || posy < 0 || posy >= chunkSize)
            return new Block(BlockType.Air, 0,0);
        

        return chunkBackBlocks[posx + posy * chunkSize];
    }
    public Block GetBackBlockLocal(int localX, int localY)
    {

        if (localX < 0 || localX >= chunkSize || localY < 0 || localY >= chunkSize)
            return WorldGen.GetBlockFromChunkBacktLayer(chunkOrigin.x + localX, chunkOrigin.y + localY);

        return chunkBackBlocks[localX + localY * chunkSize];
    }

    public int GetFluidAt(int worldX, int worldY)
    {

        int posx = worldX - chunkOrigin.x;
        int posy = worldY - chunkOrigin.y;

        if(posx < 0  && neighborLeft != null)
        {
            return neighborLeft.GetFluidAt(worldX, worldY);
        }
        else if (posx >= chunkSize && neighborRight != null)
        {
            return neighborRight.GetFluidAt(worldX, worldY);
        }

        if (posy < 0 && neighborBottom != null)
        {
            return neighborBottom.GetFluidAt(worldX, worldY);
        }
        else if (posy >= chunkSize && neighborTop != null)
        {
            return neighborTop.GetFluidAt(worldX, worldY);
        }


        if (posx < 0 || posx >= chunkSize || posy < 0 || posy >= chunkSize)
            return 0;

        return WaterBlocks[posx + posy * chunkSize].Level;
    }
    public  FluidCell GetFluidCellAt(int worldX, int worldY)
    {
        int posx = worldX - chunkOrigin.x;
        int posy = worldY - chunkOrigin.y;

        if (posx < 0 && neighborLeft != null)
        {
            return  neighborLeft.GetFluidCellAt(worldX, worldY);
        }
        else if (posx >= chunkSize && neighborRight != null)
        {
            return  neighborRight.GetFluidCellAt(worldX, worldY);
        }

        if (posy < 0 && neighborBottom != null)
        {
            return  neighborBottom.GetFluidCellAt(worldX, worldY);
        }
        else if (posy >= chunkSize && neighborTop != null)
        {
            return  neighborTop.GetFluidCellAt(worldX, worldY);
        }


        if (posx < 0 || posx >= chunkSize || posy < 0 || posy >= chunkSize)
            return  new FluidCell();

        return  WaterBlocks[posx + posy * chunkSize];
    }

    public int GetFluidLocalAt(int localX, int localY)
    {
        if (localX < 0 || localX >= chunkSize || localY < 0 || localY >= chunkSize)
            return 0;

        return WaterBlocks[localX + localY * chunkSize].Level;
    }

    public bool isSolidBlockFront(int worldx, int worldy)
    {
        int lpx = worldx - chunkOrigin.x;
        int lpy = worldy - chunkOrigin.y;

        if(lpx < 0 && neighborLeft != null)
        {
            return neighborLeft.isSolidBlockFront(worldx, worldy);
        }
        else if (lpx >= chunkSize && neighborRight != null)
        {
            return neighborRight.isSolidBlockFront(worldx, worldy);
        }

        if (lpy < 0 && neighborBottom != null)
        {
            return neighborBottom.isSolidBlockFront(worldx, worldy);
        }
        else if (lpy >= chunkSize && neighborTop != null)
        {
            return neighborTop.isSolidBlockFront(worldx, worldy);
        }

        if (lpx < 0 || lpx >= chunkSize || lpy < 0 || lpy >= chunkSize)
            return false;

        return chunkFrontBlocks[lpx + lpy * chunkSize].IsSolid();
    }
    public bool isSolidBlockBack(int worldx, int worldy)
    {
        int lpx = worldx - chunkOrigin.x;
        int lpy = worldy - chunkOrigin.y;

        if (lpx < 0 && neighborLeft != null)
        {
            return neighborLeft.isSolidBlockBack(worldx, worldy);
        }
        else if (lpx >= chunkSize && neighborRight != null)
        {
            Debug.Log("Solid right call");
            return neighborRight.isSolidBlockBack(worldx, worldy);
        }

        if (lpy < 0 && neighborBottom != null)
        {
            return neighborBottom.isSolidBlockBack(worldx, worldy);
        }
        else if (lpy >= chunkSize && neighborTop != null)
        {
            return neighborTop.isSolidBlockBack(worldx, worldy);
        }

        if (lpx < 0 || lpx >= chunkSize || lpy < 0 || lpy >= chunkSize)
            return false;

        return chunkBackBlocks[lpx + lpy * chunkSize].IsSolid();
    }


    public bool AABB(Vector2 checkPos, Vector2 checkSize)
    {
        return (checkPos.x + checkSize.x > chunkOrigin.x &&
            checkPos.x - checkSize.x < chunkOrigin.x + chunkSize &&
            checkPos.y + checkSize.y > chunkOrigin.y  &&
            checkPos.y - checkSize.y < chunkOrigin.y + chunkSize);
    }


    private void Update()
    {
        updateDirtyRegion();

        if (WorldGen.waterActive)
        {
            if (UpdateFluidBufferRegion)
            {
                UpdateFluidBufferRegion = false;

                foreach (var i in bufferFuildRegions)
                    MarkFluidRegionDirtyLocal(i.x, i.y);

                bufferFuildRegions.Clear();

            }

            UpdateFluid();
        }
    }

}
