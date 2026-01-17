using UnityEngine;

public class DestroyBlockFrame : MonoBehaviour
{
    public SpriteRenderer rend;

    public Sprite[] DestroyFrames;

    private float hTime;


    private void Start()
    {
        CancelProcess();     
    }

    public bool setDestroyProcess(Vector2 pos, float HarvestSpeed, float HarvestLevel)
    {
        transform.position = pos;
        hTime += Time.deltaTime * HarvestSpeed;
        float ht = HarvestLevel;
        int frameCount = DestroyFrames.Length;
        float progress = hTime / ht;
        int frameIndex = Mathf.FloorToInt(progress * frameCount);
        int safeIndex = Mathf.Clamp(frameIndex, 0, frameCount - 1);
        rend.sprite = DestroyFrames[safeIndex];

        if (hTime > ht) return true;
        return false;
    }

    public void CancelProcess()
    {
        rend.sprite = DestroyFrames[0];
        hTime = 0;

        transform.position = new Vector3(int.MaxValue - 5, int.MaxValue - 5);
    }

}
