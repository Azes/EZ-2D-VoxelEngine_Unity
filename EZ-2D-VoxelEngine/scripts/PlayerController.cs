using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    public float moveSpeed = 10, gravity = 30, jumpForce = 15, jumpForceMin = 5, jumpForceMulitply, maxJumpHoldTime = 0.25f;
    public float speedUp = 50, speedDown = 30;
    public float maxCursorDistanze = 50;
    public float HarvestSpeed = 10f;
    public bool groundet;
    public bool AutoJump;
    [HideInInspector]public Vector2 velocity;
    public Vector2 PlayerSize;
    public Vector2 MousePos;
    public SelectionCursor cursor;
    public GameObject blockHover;
    public GameObject destroyPrefabs;

    
    [HideInInspector] public Camera cam;

    private Mouse m;
    private Keyboard kb;
    private int input = 0;
    private float jfTime;
    private bool isHoldingJump;

    private Dictionary<(Vector2Int pos, bool isBack), DestroyBlockFrame> activeDestructions = new Dictionary<(Vector2Int, bool), DestroyBlockFrame>();
    private Stack<DestroyBlockFrame> framePool = new Stack<DestroyBlockFrame>();

    private SpriteRenderer bH;
    private HashSet<(Vector2Int, bool)> blocksBeingHitRightNow = new HashSet<(Vector2Int, bool)>();
    private List<(Vector2Int, bool)> toRemove = new List<(Vector2Int, bool)>();


    //Test

    public bool swit;
    

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


    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        var pos = new Vector3(transform.position.x + PlayerSize.x * .5f, transform.position.y + PlayerSize.y * .5f, transform.position.z);
        Gizmos.DrawWireCube(pos,
                 PlayerSize);
    }

    // Der Rest von Update() dient nur noch der Input-Abfrage und Block-Interaktion
    void Update()
    {
        if (kb.eKey.wasPressedThisFrame)
            swit = !swit;

        if (!swit) DestoryBlockCast();
        else CreateBlockCast(BlockType.Stone);


        if (kb.dKey.isPressed)
            input = 1;
        else if (kb.aKey.isPressed)
            input = -1;
        else input = 0;

        if (kb.spaceKey.wasReleasedThisFrame)
        {
            isHoldingJump = false;
            jfTime = 0;
        }
    }


    private void FixedUpdate()
    {

        if (input != 0)
            velocity.x = Mathf.MoveTowards(velocity.x, input * moveSpeed, speedUp * Time.fixedDeltaTime);
        else
            velocity.x = Mathf.MoveTowards(velocity.x, 0, speedDown * Time.fixedDeltaTime);

        if (groundet)
        {
            velocity.y = Mathf.Max(velocity.y, -.01f); 
            jfTime = 0;
        }
        else
            velocity.y -= gravity * Time.fixedDeltaTime; 


        if (groundet && kb.spaceKey.isPressed)
        {
            velocity.y = jumpForceMin;
            isHoldingJump = true;
            groundet = false;
        }

        if (isHoldingJump && kb.spaceKey.isPressed && velocity.y > 0)
        {
            jfTime += Time.fixedDeltaTime;
            if (jfTime <= maxJumpHoldTime)
            {
                velocity.y += jumpForceMulitply * Time.fixedDeltaTime;
                velocity.y = Mathf.Min(velocity.y, jumpForce);
            }
            else
            {
                isHoldingJump = false;
            }
        }

        if (!groundet && velocity.y <= 0)
        {
            isHoldingJump = false;
            jfTime = 0;
        }

        
        CollisionChecks();

        transform.position += (Vector3)velocity * Time.fixedDeltaTime;
    }


    private void CollisionChecks()
    {
        float width = PlayerSize.x;
        float height = PlayerSize.y;
        float px = transform.position.x;
        float py = Mathf.RoundToInt(transform.position.y);
        float skinWidth = 0.02f;

        bool solid(int x, int y) => WorldGen.isSolidBlock(x, y);

        int footY = Mathf.FloorToInt(py - skinWidth);

        int Grounded = 0;


        for (int sx = 0; sx <= width; sx++)
           if(solid(Mathf.FloorToInt(px + sx), footY))
                Grounded++;
        
        if (Grounded > 0 && velocity.y <= 0)
        {
            velocity.y = 0;

            var t = transform.position;
            t.y = Mathf.RoundToInt(t.y); 
            transform.position = t;

            groundet = true;
        }
        else
        {
            groundet = false;
        }


        // LEFT collider
        int leftX = Mathf.FloorToInt(px - skinWidth);
        int lc = 0;
        
        for (int sy = 0; sy < height; sy++)
            if (solid(leftX, Mathf.FloorToInt(py + sy + skinWidth)))
                lc++;

        if (velocity.x < 0 && lc > 0)
        {
            var t = transform.position;
            t.x = (float)leftX + 1 + skinWidth;
            transform.position = t;
            velocity.x = 0;
        }


        // RIGHT collider
        int rightX = Mathf.FloorToInt(px + width + skinWidth);
        int rc = 0;


        for (int sy = 0; sy < height; sy++)
            if (solid(rightX, Mathf.FloorToInt(py + sy + skinWidth)))
                rc++;

        if (velocity.x > 0 && rc > 0)
        {
            var t = transform.position;

            t.x = (float)rightX - width - skinWidth;
            transform.position = t;
            velocity.x = 0;
        }

        // HEAD collider
        int headY = Mathf.FloorToInt(py + height);
        int hc = 0;

        for (int sx = 0; sx <= width; sx++)
            if (solid(Mathf.FloorToInt(px + sx), headY))
                hc++;

        if (velocity.y > 0 && hc > 0)
        {
            var t = transform.position;

            t.y = (float)headY - height - skinWidth;
            transform.position = t;
            velocity.y = 0;

            isHoldingJump = false;
        }

    }



    public void CreateBlockCast(BlockType type)
    {
        MousePos = cam.ScreenToWorldPoint(m.position.ReadValue());

        Vector2Int mp = Vector2Int.FloorToInt(MousePos);

        var sb = WorldGen.GetBlock(mp.x, mp.y, true);
        var sf = WorldGen.GetBlock(mp.x, mp.y, false);

        bool canfront = false;
        bool canback = false;

        blocksBeingHitRightNow.Clear();
        toRemove.Clear();

        if (sb.type == BlockType.Air && WorldGen.isBlockConnectBackLayer(mp.x, mp.y))
        {
            cursor.isVisible(true);
            cursor.SetCursor(new Vector2(mp.x, mp.y));
            canfront = true;
            
        }
        else if (sf.type == BlockType.Air  && WorldGen.isBlockConnectFrontLayer(mp.x, mp.y))
        {
            cursor.isVisible(true);
            cursor.SetCursor(new Vector2(mp.x, mp.y));
            canback = true;
                    }
        else cursor.isVisible(false);

        if ((canfront || canback) && m.leftButton.wasPressedThisFrame)
        {
            var p = cursor.PAABB(false);

            foreach (var p2 in p)
                WorldGen.SetBlockInChunkFrontLayerDirect(p2.x, p2.y, type, true);
        }
        else if (canback && m.rightButton.wasPressedThisFrame)
        {
            var p = cursor.PAABB(false);

            foreach (var p2 in p)
                WorldGen.SetBlockInChunkBackLayerDirect(p2.x, p2.y, type, true);
        }


    }

    private void DestoryBlockCast()
    {
        MousePos = cam.ScreenToWorldPoint(m.position.ReadValue());

        Vector2Int startpos = Vector2Int.RoundToInt((Vector2)(transform.position + ((Vector3)PlayerSize * .5f)));
        Vector2Int mp = Vector2Int.FloorToInt(MousePos);

        var hitBlock = WorldGen.BlockCast(startpos, MousePos, maxCursorDistanze);
        var bBlockPos = WorldGen.GetSolidBlock(mp.x, mp.y, true);

        blocksBeingHitRightNow.Clear();
        toRemove.Clear();

        if (hitBlock.Hit)
        {
            cursor.isVisible(true);
            cursor.SetCursor(hitBlock.Position);

            if (m.leftButton.isPressed)
            {
                ProcessLayer(cursor.AABB(false), false, false, blocksBeingHitRightNow);
            }
        }
        else if (m.rightButton.isPressed && bBlockPos.IsSolid())
        {
            cursor.isVisible(true);
            cursor.SetCursor(new Vector2(bBlockPos.pos_x, bBlockPos.pos_y));

            ProcessLayer(cursor.AABB(true), true, true, blocksBeingHitRightNow);
        }
        else cursor.isVisible(false);


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
                    if (WorldGen.GetBlockFromChunkFrontLayer(block.pos_x, block.pos_y).IsSolid())
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
