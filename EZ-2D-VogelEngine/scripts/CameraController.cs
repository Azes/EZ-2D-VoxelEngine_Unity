using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    public float moveSpeed = 10, speed = 2;

    public float HarvestSpeed = 10f;
    private float hTime;
    private Vector2 MousePos, velocity;
    public GameObject blockHover;
    public SelectionCursor cursor;
    public GameObject destroyPrefabs;

   
    public SpriteRenderer bH;
    [HideInInspector] public Camera cam;
    private Mouse m;
    private Keyboard kb;

    public BlockType setType;
    public bool DestroyOrPlace;
   
    private Dictionary<(Vector2Int pos, bool isBack), DestroyBlockFrame> activeDestructions = new Dictionary<(Vector2Int, bool), DestroyBlockFrame>();
    private Stack<DestroyBlockFrame> framePool = new Stack<DestroyBlockFrame>();
    private HashSet<(Vector2Int, bool)> blocksBeingHitRightNow = new HashSet<(Vector2Int, bool)>();
    private List<(Vector2Int, bool)> toRemove = new List<(Vector2Int, bool)>();


    void Start()
    {
        cam = Camera.main;
        m = Mouse.current;
        kb = Keyboard.current;
        bH = blockHover.GetComponent<SpriteRenderer>();

        for (int i = 0; i < 10; i++) 
        {
            var d = Instantiate(destroyPrefabs).GetComponent<DestroyBlockFrame>();
            d.CancelProcess();
            framePool.Push(d);
        }

    }
        // Update is called once per frame
    void Update()
    {
        if(kb.eKey.wasPressedThisFrame)
            DestroyOrPlace = !DestroyOrPlace;

        if (!DestroyOrPlace)
            DestroyBlock();
        else
            PlaceBlock(setType);

            int ipy = 0;
        int ipx = 0;

        if (kb.dKey.isPressed)
            ipx = 1;
        else if (kb.aKey.isPressed)
            ipx = -1;

        if (kb.wKey.isPressed)
            ipy = 1;
        else if (kb.sKey.isPressed)
            ipy = -1;

        bool speedUp = kb.leftShiftKey.isPressed;

        if (ipx != 0)
            velocity.x = Mathf.MoveTowards(velocity.x, ipx * (speedUp ? moveSpeed * speed : moveSpeed), Time.deltaTime * moveSpeed);
        else
            velocity.x = Mathf.MoveTowards(velocity.x, 0, Time.deltaTime * moveSpeed * 2f);

        if (ipy != 0)
            velocity.y = Mathf.MoveTowards(velocity.y, ipy * (speedUp ? moveSpeed * speed : moveSpeed), Time.deltaTime * moveSpeed * 2f);
        else 
            velocity.y = Mathf.MoveTowards(velocity.y, 0, Time.deltaTime * moveSpeed);

        transform.position += (Vector3)velocity * Time.deltaTime;

    }

    private void PlaceBlock(BlockType type)
    {
        MousePos = cam.ScreenToWorldPoint(m.position.ReadValue());

        Vector2Int mp = Vector2Int.FloorToInt(MousePos);

        cursor.SetCursor(new Vector2(mp.x, mp.y));

        var sf = cursor.AABB(false, false);
        var sb = cursor.AABB(true, false);

        bool canfrontsolid = false;
        bool canbacksolid = false;
        bool canfrontcon = false;
        bool canbackcon = false;


        for (int i2 = 0; i2 < sf.Count; i2++)
        {
            var fposc = new Vector2Int(sf[i2].pos_x, sf[i2].pos_y);

            if (sf[i2].IsSolid())
                canfrontsolid = true;

            if (WorldGen.isBlockConnectFrontLayer(fposc.x, fposc.y))
                canfrontcon = true;
        }

        for (int i = 0; i < sb.Count; i++)
        {
            var bposc = new Vector2Int(sb[i].pos_x, sb[i].pos_y);

            if (sb[i].IsSolid())
                canbacksolid = true;

            if (WorldGen.isBlockConnectBackLayer(bposc.x, bposc.y))
                canbackcon = true;        
        }

        if(canfrontcon || canbackcon)
            cursor.isVisible(true);
        else cursor.isVisible(false);


        if ((canfrontcon || canbacksolid) && m.leftButton.wasPressedThisFrame)
        {
            var p = cursor.PAABB(false);

            foreach (var p2 in p)
                WorldGen.SetBlockInChunkFrontLayerDirect(p2.x, p2.y, type, true);
        }
        else if (!canfrontsolid && canbackcon && m.rightButton.wasPressedThisFrame)
        {
            var p = cursor.PAABB(true);

            foreach (var p2 in p)
                WorldGen.SetBlockInChunkBackLayerDirect(p2.x, p2.y, type, true);
        }


    }
    private void DestroyBlock()
    {
        MousePos = cam.ScreenToWorldPoint(m.position.ReadValue());
        cursor.SetCursor(MousePos);

        blocksBeingHitRightNow.Clear();
        toRemove.Clear();

        var bf = cursor.AABB(false);

        if (bf.Count > 0)
        {
            cursor.isVisible(true);

            if (m.leftButton.isPressed)
            {
                ProcessLayer(bf, false, false, blocksBeingHitRightNow);
            }
        }

        var bb = cursor.AABB(true);

        if (bb.Count > 0)
        {
            cursor.isVisible(true);

            if (m.rightButton.isPressed)
            {
                ProcessLayer(bb, true, true, blocksBeingHitRightNow);
            }
        }

        foreach (var key in activeDestructions.Keys)
        {
            if (!blocksBeingHitRightNow.Contains(key))
                toRemove.Add(key);
        }

        foreach (var key in toRemove)
        {
            CleanupFrame(key);
        }
    }

   
    private void ProcessLayer(List<Block> hitBlocks, bool isBack, bool isNotFrontBlock, HashSet<(Vector2Int, bool)> hitSet)
    {
        foreach (var block in hitBlocks)
        {
            if (block.type == BlockType.Air || block.type == BlockType.None || !block.CanHarvest) continue;

            Vector2Int pos = Vector2Int.FloorToInt(new Vector2(block.pos_x, block.pos_y));
            var key = (pos, isBack);
            hitSet.Add(key);


            if (!activeDestructions.ContainsKey(key))
                if (framePool.Count > 0)
                    activeDestructions.Add(key, framePool.Pop());


            // Fortschritt verarbeiten
            if (activeDestructions.ContainsKey(key))
            {

                if (isNotFrontBlock && isBack)
                {
                    if (WorldGen.isSolidBlock(block.pos_x, block.pos_y))
                        continue;
                        
                }

                if (activeDestructions[key].setDestroyProcess(new Vector2(block.pos_x, block.pos_y), HarvestSpeed, block.GetHarvestLevel()))
                {
                    if (!isBack)
                        WorldGen.SetBlockInChunkFrontLayerDirect(pos.x, pos.y, BlockType.Air, true);
                    else
                        WorldGen.SetBlockInChunkBackLayerDirect(pos.x, pos.y, BlockType.Air, true);

                    CleanupFrame(key);
                }
            }
        }
    }

    private void CleanupFrame((Vector2Int pos, bool isBack) key)
    {
        if (activeDestructions.ContainsKey(key))
        {
            activeDestructions[key].CancelProcess();
            framePool.Push(activeDestructions[key]);
            activeDestructions.Remove(key);
        }
    }

}
