using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class EntityLight : MonoBehaviour
{

    public int UpdateDelay = 50;

    private SpriteRenderer render;
    private Material nm;

    private int count;
    private float[] color = new float[4];
    private float[] color2 = new float[4];

    private void Start()
    {
        render = GetComponent<SpriteRenderer>();
        nm = new Material(render.sharedMaterial);
        render.sharedMaterial = nm;
        count = UpdateDelay;
    }


    private void LateUpdate()
    {
        if (count >= UpdateDelay)
        {
            int x = Mathf.RoundToInt(transform.position.x);
            int y = Mathf.RoundToInt(transform.position.y - (transform.localScale.y * .5f));
            int y2 = Mathf.RoundToInt(transform.position.y + (transform.localScale.y * .5f));

            color = LightSystem.GetLightAtPosition(x, y);
            color2 = LightSystem.GetLightAtPosition(x, y2);

            nm.SetColor("_LightColor", 
                new Color(
                    Mathf.Max(color[0], color2[0]), Mathf.Max(color[1], color2[1]),
                    Mathf.Max(color[2], color2[2]), Mathf.Max(color[3], color2[3])
                    )
                );
        
            count = 0;
        }
        else count++;
    }

}
