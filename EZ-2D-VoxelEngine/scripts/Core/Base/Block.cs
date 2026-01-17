
using System;
using UnityEngine;
public enum BlockType { Air, Grass, Dirt, Stone, DarkStone, BlackStone, DarkDirt, SandStone,
    CoalOre, IronOre, GoldOre, DiamondOre, None}

[Serializable]
public struct Block
{
    public BlockType type;
    public int pos_x, pos_y;

    public Block(BlockType t, int x, int y) 
    {
        type = t;
        pos_x = x;
        pos_y = y;
    }


    public bool isNone() => type == BlockType.None;

    public bool IsSolid() => type != BlockType.Air;

    public void setBlockType(BlockType t) { type = t; }
    
    public void setBlockPosition(Vector2 Pos) { pos_x = Mathf.FloorToInt(Pos.x); pos_y = Mathf.FloorToInt(Pos.y); }

    public bool CanHarvest => GetHarvestLevel() > 0;

    public float GetHarvestLevel()
    {
        
        switch (type)
        {
            case BlockType.Air:
                return 0;
            case BlockType.Grass:
                return 1;
            case BlockType.Dirt:
                return 2;
            case BlockType.Stone:
                return 4;
            case BlockType.DarkStone:
                return 6;
            case BlockType.DarkDirt:
                return 2;
            case BlockType.CoalOre:
                return 3;
            case BlockType.GoldOre:
                return 5;
            case BlockType.IronOre:
                return 8;
            case BlockType.DiamondOre:
                return 11;
            case BlockType.BlackStone:
                return -1;
            case BlockType.SandStone:
                return 4;

            default:
                return -1;
        }
    }

    
    public int GetTextureID(int dirType)
    {
        int id = type switch
        {
            BlockType.Grass => dirType switch
            {
                0 => 1,
                1 => 20,// Left Corner
                2 => 22,// Right Corner
                3 => 24,// left and Right
                _ => 1
            },
            BlockType.Dirt => dirType switch
            {
                0 => 5,
                1 => 21,// Left
                2 => 23,// Right
                3 => 19,// right innen
                4 => 18,// left innen
                _ => 5
            },
            BlockType.Stone => 6,
            BlockType.DarkStone => 7,
            BlockType.BlackStone => 8,
            BlockType.DarkDirt => 10,
            BlockType.CoalOre => 13,
            BlockType.GoldOre => 15,
            BlockType.IronOre => 16,
            BlockType.DiamondOre => 17,
            BlockType.SandStone => 3,
            BlockType.Air => 9,// Air Block Have tobe A Block Size Transparent Area in TextureAtlas
            _ => -1


        };

        return id;
    }

    
}
