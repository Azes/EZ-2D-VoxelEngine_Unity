using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "TextureAtlas", menuName = "Voxel/Texture Atlas")]
public class TextureAtlas : ScriptableObject
{
    [Header("Ziehe hier deine Atlas-Textur rein")]
    public Texture2D texture;

    [Header("Größe einer Kachel (z. B. 256x256)")]
    public int tileSize = 256;

    public static TextureAtlas Instance;

    public static Dictionary<int, Vector2[]> Texture = new Dictionary<int, Vector2[]>();

    private static bool initTextures;

    public int AtlasSizeInBlocks => texture != null ? texture.width / tileSize : 1;

    private void OnEnable()
    {
        Instance = this;
    }

    public static void Init()
    {

        if (initTextures)
            return;

        int maxIdSize = (Instance.texture.width / Instance.tileSize) * (Instance.texture.height / Instance.tileSize);

        for (int i = 0; i < maxIdSize; i++)
        {
            if(!Texture.ContainsKey(i))Texture.Add(i, GetUVs(i));
        }

        initTextures = true;
    }
    private static Vector2[] GetUVs(int index)
    {
        int atlasSize = Instance.AtlasSizeInBlocks;
        int x = index % atlasSize;
        int y = index / atlasSize;
        float size = 1f / atlasSize;

        Vector2 bottomLeft = new Vector2(x * size, y * size);

        return new Vector2[]
        {
        bottomLeft,                                // 0 - unten links
        bottomLeft + new Vector2(0, size),         // 1 - oben links
        bottomLeft + new Vector2(size, 0),         // 2 - unten rechts
        bottomLeft + new Vector2(size, size)       // 3 - oben rechts
        };
    }
}
