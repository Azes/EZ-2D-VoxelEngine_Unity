using System.Collections.Generic;
using UnityEngine;

public static class FluidSystem
{
    private static Queue<long> _current = new Queue<long>();
    private static Queue<long> _next = new Queue<long>();
    private static HashSet<long> _queued = new HashSet<long>();

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
    private static HashSet<Chunk> LoadedChunks = new HashSet<Chunk>();
    public static int FlowSpeed = 25;
    public static int FluidSimulationsSteps = 100;
    public static bool FluidDirty;
    public static bool updateChange;
    private static int LightUpdate;

    public static int GetFluidAt(int x, int y)
    {
        return WorldGen.GetFluidLevelFromChunkAtWorldPosition(x, y);
    }

    // Fügt einen Block zur Liste für den NÄCHSTEN Durchlauf hinzu
    public static void AddActiveNext(int x, int y)
    {
            long k = Encode(x, y);
            if (_queued.Add(k))
                _next.Enqueue(k);
        

    }

    public static void MarkNeighbors(int x, int y)
    {
        AddActiveNext(x - 1, y);
        AddActiveNext(x + 1, y);
        AddActiveNext(x, y - 1);
        AddActiveNext(x, y + 1);
    }
    public static void UpdateSystem(List<Chunk> chunks)
    {
        bool newl = false;

        if (!LoadedChunks.SetEquals(chunks))
        {
            LoadedChunks.Clear();
            LoadedChunks.UnionWith(chunks);
            newl = true;
        }
    
        if (updateChange || (!updateChange && newl))
        {
            updateChange = false;

            foreach (var cl in LoadedChunks)
            {
                for (int i = 0; i < cl.WaterBlocks.Length; i++)
                {
                    var cw = cl.WaterBlocks[i];
                    if (cw.Active)
                    {
                        AddActiveNext(cw.worldX, cw.worldY);
                    }
                }
            }
        }

        if (_current.Count == 0 && _next.Count > 0)
        {
            var temp = _current;
            _current = _next;
            _next = temp;
        }

        int steps = FluidSimulationsSteps;
        int X = 0;
        int Y = 0;

        while (_current.Count > 0 && steps-- > 0)
        {
            long k = _current.Dequeue();
            _queued.Remove(k);

            var p = Decode(k);
            bool changed = SimulateFluidCell(p.x, p.y);

            if (changed)
            {
                AddActiveNext(p.x, p.y);
                MarkNeighbors(p.x, p.y);
            }

            LightUpdate++;
            X = p.x;
            Y = p.y;
        }

        if(LightUpdate > FluidSimulationsSteps * 1000)
        {
            LightUpdate = 0;
            LightSystem.SetRegionDirty(X, Y);
        }

        foreach (var c in chunks)
             c.UpdateFluidBufferRegion = true;
         
        FluidDirty = _current.Count + _next.Count > 0;

    }


    private static bool SimulateFluidCell(int x, int y)
    {
        int level = GetFluidAt(x, y);
        bool changed = false;

        if (level < 1)
        {
            WorldGen.SetFluidLevelInChunksBuffer(x, y, 0);
            AddActiveNext(x, y);
            return false;
        }


        if (!WorldGen.isSolidBlock(x, y - 1))
        {
            int below = GetFluidAt(x, y - 1);
            int spaceBelow = 100 - below; 

            if (spaceBelow > 0)
            {
                int amountToMove = Mathf.Min(level, spaceBelow);


                if (amountToMove > 0)
                {
                    level -= amountToMove + FlowSpeed;
                    below += amountToMove + FlowSpeed;

                    WorldGen.SetFluidLevelInChunksBuffer(x, y, level);
                    WorldGen.SetFluidLevelInChunksBuffer(x, y - 1, below);

                    AddActiveNext(x, y);
                    AddActiveNext(x, y - 1);
                    MarkNeighbors(x, y);

                    if (level <= 0)
                    {
                        return false;
                    }
                }
            }
        }


        if (!WorldGen.isSolidBlockBack(x, y))
        {
            int oldLevel = level;
            level = Mathf.Max(0, level - FlowSpeed);

            if (level != oldLevel)
            {
                WorldGen.SetFluidLevelInChunksBuffer(x, y, level);

                if (level > 0)
                {
                    AddActiveNext(x, y);
                }

                MarkNeighbors(x, y);

                return false;
            }
        }

        if (Random.value > 0.5f)
        {
            changed |= TrySpread(x, y, x + 1, y, ref level);
            changed |= TrySpread(x, y, x - 1, y, ref level);
        }
        else
        {
            changed |= TrySpread(x, y, x - 1, y, ref level);
            changed |= TrySpread(x, y, x + 1, y, ref level);
        }


        if (changed)
            WorldGen.SetFluidLevelInChunksBuffer(x, y, level);

        return changed;
    }

    private static bool TrySpread(int sx, int sy, int tx, int ty, ref int sourceLevel)
    {
        if (sourceLevel <= 1) return false;
        if (WorldGen.isSolidBlock(tx, ty)) return false;

        int targetLevel = GetFluidAt(tx, ty);
        if (targetLevel >= sourceLevel) return false;

        int desiredFlow = (sourceLevel - targetLevel) / 2;

        if (desiredFlow <= 0) return false;

        int actualFlow = Mathf.Min(desiredFlow, FlowSpeed);

        sourceLevel -= actualFlow;
        targetLevel += actualFlow;

        if (sourceLevel <= 1) sourceLevel = 0;


        AddActiveNext(sx, sy);
        AddActiveNext(tx, ty);

        WorldGen.SetFluidLevelInChunksBuffer(sx, sy, sourceLevel);
        WorldGen.SetFluidLevelInChunksBuffer(tx, ty, targetLevel);

        return true;
    }
}