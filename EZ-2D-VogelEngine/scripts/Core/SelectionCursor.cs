using System.Collections.Generic;
using UnityEngine;

public class SelectionCursor : MonoBehaviour
{
    public enum areas
    {
        b1x1, b2x2, b3x3
    }

    public areas CursorSize;


    private Vector3[] sizes = { new Vector3(1f, 1f, 1f), new Vector3(2f, 2f, 2f), new Vector3(3f, 3f, 3f) };


    public List<Block> BlocksList = new List<Block>();
    public List<Vector2Int> BlockPoses = new List<Vector2Int>();
    private SpriteRenderer rend;

    private void Start()
    {
        rend = GetComponent<SpriteRenderer>();

        switch (CursorSize)
        {
            case areas.b1x1:
                transform.localScale = sizes[0];
                break;
            case areas.b2x2:
                transform.localScale = sizes[1];
                break;
            case areas.b3x3:
                transform.localScale = sizes[2];
                break;
        }
    }

    public void setCursorSize(areas area)
    {
        switch (area)
        {
            case areas.b1x1:
                transform.localScale = sizes[0];
                break;
            case areas.b2x2:
                transform.localScale = sizes[1];
                break;
            case areas.b3x3:
                transform.localScale = sizes[2];
                break;
        }
    }

    public void isVisible(bool visible)
    {
        rend.enabled = visible;
    }

    public void SetCursor(Vector2 pos)
    {
        
        switch (CursorSize)
        {
            case areas.b1x1: transform.position = new Vector2(Mathf.Floor(pos.x), Mathf.Floor(pos.y)); break;
            case areas.b2x2: transform.position = new Vector2(Mathf.Floor(pos.x - .5f), Mathf.Floor(pos.y - .5f)); break;
            case areas.b3x3: transform.position = new Vector2(Mathf.Floor(pos.x - .75f), Mathf.Floor(pos.y - .75f)); break;
        }
    }

    public List<Block> AABB(bool back,bool haveToSolid = true)
    {
        BlocksList.Clear();

        int radius = 0;
        Vector2Int center = Vector2Int.FloorToInt(transform.position);

        switch (CursorSize)
        {
            case areas.b1x1:
                radius = 0;
                break;
            case areas.b2x2:
                radius = 1;
                center = Vector2Int.FloorToInt(transform.position + new Vector3(.5f, .5f));
                break; 
            case areas.b3x3:
                radius = 2;
                center = Vector2Int.FloorToInt(transform.position + new Vector3(.75f, .75f)); 
                break; 
        }

       

        // Iteriere durch den Bereich
        for (int x = 0; x <= radius; x++)
        {
            for (int y = 0; y <= radius; y++)
            {
                Block b = WorldGen.GetBlock(center.x + x, center.y + y, back);

                if (b.IsSolid() && haveToSolid)
                    BlocksList.Add(b);
                else if(!haveToSolid)
                    BlocksList.Add(b);
            }
        }

            return BlocksList;
    }
    public List<Vector2Int> PAABB(bool back, bool haveToSolid = false)
    {
        BlockPoses.Clear();
        int radius = 0;

        Vector2Int center = Vector2Int.FloorToInt(transform.position);

        switch (CursorSize)
        {
            case areas.b1x1:
                radius = 0;
                break;
            case areas.b2x2:
                radius = 1;
                center = Vector2Int.FloorToInt(transform.position + new Vector3(.5f, .5f));
                break;
            case areas.b3x3:
                radius = 2;
                center = Vector2Int.FloorToInt(transform.position + new Vector3(.75f, .75f));
                break;
        }


        for (int x = 0; x <= radius; x++)
        {
            for (int y = 0; y <= radius; y++)
            {
                Block b = WorldGen.GetBlock(center.x + x, center.y + y, back);

                bool s = false;

                if(haveToSolid)
                    s = b.IsSolid();
                else s = !b.IsSolid();

                if (s)
                    BlockPoses.Add(new Vector2Int(b.pos_x, b.pos_y));

            }
        }

        return BlockPoses;
    }
}
