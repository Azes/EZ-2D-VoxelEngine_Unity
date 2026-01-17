
using System.Collections.Generic;
using UnityEngine;

public static class Explosion
{

    // BlockEntity ID´s
    //15 = ligthdirt
    //10 = dirtdark
    //16 = normalstone
    //17 = darstone
    //23 = sandstone

    private static List<GameObject> listeb = new List<GameObject>();

    public static void SetExplosionAt(int worldX, int worldY, float radius, float power)
    {
        radius = Mathf.Clamp(radius, 1, 100);
        GameObject preEBE = Resources.Load("ExpoEntityBlock") as GameObject;
      
        Expo(worldX, worldY, radius, power);

        listeb.Clear();

       var b = getBlockAt(worldX, worldY, radius);

        foreach (var block in b)
        {
            var g = UnityEngine.Object.Instantiate(preEBE) as GameObject;
            listeb.Add(g);
            g.transform.position = new Vector2(block.pos_x, block.pos_y);
            var m = g.GetComponent<Renderer>().material;

            switch (block.type)
            {
                case BlockType.Dirt:
                    m.SetFloat("_Index", 15);
                    break;
                case BlockType.Grass:
                    m.SetFloat("_Index", 15);
                    break;
                case BlockType.DarkDirt:
                    m.SetFloat("_Index", 10);
                    break;
                case BlockType.Stone:
                    m.SetFloat("_Index", 16);
                    break;
                case BlockType.DarkStone:
                    m.SetFloat("_Index", 17);
                    break;
                case BlockType.SandStone:
                    m.SetFloat("_Index", 23);
                    break;
                default:
                    m.SetFloat("_Index", 16);
                    break;
            }
        }

        for (int i = 0; i < b.Count; i++)
        {
            Rigidbody2D ri = listeb[i].GetComponent<Rigidbody2D>();

            var dir = (listeb[i].transform.position - new Vector3(worldX, worldY)).normalized;
            dir.y = Mathf.Abs(dir.y);
            dir.y = Mathf.Clamp(dir.y, 0.5f, 1f);
            ri.AddForce(dir * power, ForceMode2D.Impulse);
            ri.AddTorque(Random.Range(-5f, 5f), ForceMode2D.Impulse);
        }

        foreach (var item in listeb)
            UnityEngine.Object.Destroy(item.gameObject, Random.Range(2, 6));
    }

    public static void ExplosionAtPos(int worldX, int worldY, float radius, float power)
    {
        radius = Mathf.Clamp(radius, 1, 100);
        Expo(worldX, worldY, radius, power);

    }

    private static void Expo(int wx, int wy, float r, float p)
    {
        int rd = Mathf.RoundToInt(r);

        for (int x = wx - rd; x < wx + rd; x++)
        {
            for (int y = wy - rd; y < wy + rd; y++)
            {
                if (x < 0 || y < 0 || x >= WorldGen.GLMsize.x || y >= WorldGen.GLMsize.y)
                    continue;

                if(((x - wx) * (x - wx)) + ((y - wy) * (y - wy)) <= rd * rd)
                {
                    Block bf = WorldGen.GetBlockFromChunkFrontLayer(x, y);
                    Block bb = WorldGen.GetBlockFromChunkBacktLayer(x, y);

                    if (bf.GetHarvestLevel() <= (p * p) && bf.GetHarvestLevel() != -1)
                        WorldGen.SetBlockInChunkFrontLayerDirect(x, y, BlockType.Air);

                    if (bb.GetHarvestLevel() <= (p * p) && bb.GetHarvestLevel() != -1)
                        WorldGen.SetBlockInChunkBackLayerDirect(x, y, BlockType.Air);

                    if (WorldGen.waterActive && WorldGen.GetFluidLevelFromChunkAtWorldPosition(x, y) > 0.1f)
                        WorldGen.SetFluidLevelInChunk(x, y, 0);
                }
            }
        }
    }

    private static List<Block> getBlockAt(int wx, int wy, float r)
    {
        List<Block> list = new List<Block>();
        int rd = Mathf.RoundToInt(r);
        int v = Random.Range(1, 6);

        for (int x = wx - rd; x < wx + rd; x += v)
        {
            for (int y = wy + 1; y > 0; y--)
            {
                var b = WorldGen.GetBlockFromChunkFrontLayer(x, y);
                if (b.IsSolid())
                {
                    list.Add(b);
                    break;
                }

                var ba = WorldGen.GetBlockFromChunkBacktLayer(x, y);
                if (ba.IsSolid())
                {
                    list.Add(ba);
                    break;
                }
            }

            v = Random.Range(1, 4);
        }

        return list;    
    }
}
