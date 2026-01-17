using UnityEngine;
using TMPro;
using System.Collections.Generic;
using System.Linq;
public class FPSViewer : MonoBehaviour
{
    public TextMeshProUGUI tmp;
    [SerializeField] private float updateInterval = 0.5f;

    [SerializeField] private int maxSamples = 10;
    private float timer;
    private int frames;

    private List<int> totalFrames = new List<int>();    
    int dfps;
    void Update()
    {
        frames++;
        timer += Time.unscaledDeltaTime;

        if (timer >= updateInterval)
        {
            float fps = frames / timer;

            totalFrames.Add(Mathf.RoundToInt(fps));

            if (totalFrames.Count >= maxSamples)
            {
                dfps = totalFrames.Min();
                totalFrames.Clear();
            }


            if (tmp != null)
                tmp.text = $"FPS: {Mathf.RoundToInt(fps)}\nDFPS: {dfps}";

            frames = 0;
            timer = 0f;
        }
    }
}