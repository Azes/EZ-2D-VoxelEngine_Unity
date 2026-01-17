
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class RaycastSpotlight : MonoBehaviour
{
    [Header("Spotlight Settings")]
    public float Distance = 5f;
    [Range(0, 360)] public float angle = 60f;
    [Tooltip("The total number of raycasts used to define the mesh shape. Higher values increase detail but require more performance.")]
    public int rayCount = 40; 
    public float startWidth = 0.5f;
    [Header("LightSystem Settings")]
    [Tooltip("If true, the spotlight calculates its shape and lighting only once upon activation (no runtime lighting updates).")]
    public bool Baked;
    [Tooltip("The spotlight material will automatically adopt this color.")]
    public Color color;
    public float intensity;


    private Color bc;
    private float bi;
    private float bd;
    private float ba;
    private int br;
    private Mesh mesh;
    private Vector3[] vertices;
    private Vector2[] uvs;
    private int[] triangles;
    private int _cachedRayCount = 0;
    private int ID = -1;
    private List<Vector2Int> blocks;
    private Vector3 temppos, temprote;
    private Renderer ren;
    private bool instanzeBake;

    private Quaternion rot;
    private Vector3 originWorld;
    private Vector3 forwardDir;
    private Vector3 rightDir;

    private void OnEnable()
    {
        if(ID < 0)
            ID = Mathf.RoundToInt(Random.Range(0, 10000000));

        if (!Baked)
        {
            if (temppos == Vector3.zero || temppos == null)
            {
                temppos = transform.position;
                temprote = transform.eulerAngles;
            }

            bc = color;
            bi = intensity;
            bd = Distance;
            br = rayCount;
            ba = angle;
        }
        
        if(Baked)
            instanzeBake = true;

        ren = GetComponent<Renderer>();
        ren.material.SetFloat("_Blend", Distance);
        ren.material.SetColor("_Color", color);

        if (mesh == null)
        {
            mesh = new Mesh();
            mesh.name = "SpotlightMesh";
            GetComponent<MeshFilter>().mesh = mesh;

            LightSystem.AddSpotLight(color, intensity, ID);
            blocks = new List<Vector2Int>();

        }

        CheckAndResizeArrays();

        StartCoroutine(Baking());

    }

    private void OnDestroy()
    {
        mesh = null;
        LightSystem.RemoveSpotlight(ID);

    }
    private void OnDisable()
    {
        mesh = null;
        LightSystem.RemoveSpotlight(ID);
    }


    private void LateUpdate()
    {

        if (!Baked)
        {
            var spos = Vector3Int.FloorToInt(transform.position);

            if (!bc.Equals(color))
            {
                bc = color;

                LightSystem.GetSpotLight(ID).setColor(color.r, color.g, color.b);

                ren.material.SetColor("_Color", color);
                LightSystem.UpdateDynamicLight();
            }
            if (bi != intensity)
            {
                bi = intensity;
                LightSystem.GetSpotLight(ID).setIntensity(intensity);
                LightSystem.UpdateDynamicLight();
            }

            if (transform.position != temppos || transform.eulerAngles != temprote || bd != Distance || br != rayCount || ba != angle || LightSystem.GetRegionDirty(spos.x, spos.y))
            {
                bd = Distance;
                br = rayCount;
                ba = angle;

                temppos = transform.position;
                temprote = transform.eulerAngles;
                
                ren.material.SetFloat("_Blend", Distance);

                if (br != rayCount)
                {
                    br = rayCount;
                    CheckAndResizeArrays();
                }

                UpdateMesh();
                LightSystem.UpdateDynamicLight();
            }
        }

        if (instanzeBake)
        {
            instanzeBake = false;
            StartCoroutine(Baking());
        }
    }

    private IEnumerator Baking()
    {
        yield return new WaitForEndOfFrame();
        ren.material.SetFloat("_Blend", Distance);
        ren.material.SetColor("_Color", color);

        UpdateMesh();
        LightSystem.UpdateDynamicLight();
        yield return null;
    }

    private bool CheckAndResizeArrays()
    {
        if (rayCount < 2)
        {
            if (mesh != null) mesh.Clear();
            return false;
        }

        int requiredVertexCount = rayCount * 2;
        int requiredTriangleCount = (rayCount - 1) * 6;

        if (vertices == null || vertices.Length != requiredVertexCount)
        {
            vertices = new Vector3[requiredVertexCount];
            uvs = new Vector2[requiredVertexCount]; 
            triangles = new int[requiredTriangleCount];

            _cachedRayCount = rayCount;
            GenerateTriangles(); 
            return true;
        }

        return false;
    }
    private void GenerateTriangles()
    {
        for (int i = 0; i < _cachedRayCount - 1; i++)
        {
            int innerCurrent = i;
            int innerNext = i + 1;
            int outerCurrent = i + _cachedRayCount;
            int outerNext = i + 1 + _cachedRayCount;

            // Erstes Dreieck (oben links)
            triangles[i * 6 + 0] = innerCurrent;
            triangles[i * 6 + 1] = outerCurrent;
            triangles[i * 6 + 2] = outerNext;

            // Zweites Dreieck (unten rechts)
            triangles[i * 6 + 3] = innerCurrent;
            triangles[i * 6 + 4] = outerNext;
            triangles[i * 6 + 5] = innerNext;
        }
    }
    private void UpdateMesh()
    {
        if (mesh == null || rayCount < 2)
            return;

        float angleStep = angle / (rayCount - 1);
        float startAngle = -angle / 2f;

        rot = transform.rotation;
        originWorld = transform.position;
        forwardDir = DirFromAngle(0f).normalized;
        rightDir = new Vector3(-forwardDir.y, forwardDir.x, 0f).normalized;

        blocks.Clear(); 
        LightSystem.GetSpotLight(ID).BlockInfo.Clear();

        for (int i = 0; i < rayCount; i++)
        {
            float t = (float)i / (rayCount - 1);
            float widthOffset = startWidth * (t - 0.5f);

            vertices[i] = rightDir * widthOffset;

            Vector3 rayOrigin = originWorld + transform.TransformVector(rightDir * widthOffset);
            Vector3 currentA = rot * DirFromAngle(startAngle + i * angleStep);
            float distance = Distance;

            if (WorldGen.RaycastBlock(rayOrigin, currentA, Distance, out RaycastBlockData data, true))
            {
                distance = data.distance;

                blocks.AddRange(data.passBlocks);
            }

            vertices[i + rayCount] = transform.InverseTransformPoint(rayOrigin + currentA * distance);

            uvs[i] = new Vector2(t, 0f);
            uvs[i + rayCount] = new Vector2(t, 1f);
        }

        LightSystem.GetSpotLight(ID).BlockInfo.AddRange(blocks);
        mesh.Clear(); 
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;
        mesh.RecalculateBounds();
    }

    private Vector2 DirFromAngle(float angleInDegrees)
    {
        angleInDegrees += transform.eulerAngles.z;
        float rad = angleInDegrees * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
    }

    private void OnDrawGizmos()
    {
        if (Application.isPlaying || rayCount < 2) return;

        float angleStep = angle / (rayCount - 1);
        float startAngle = -angle / 2f;

        Vector3 forwardDir = DirFromAngle(0f); 
        Vector3 rightDir = new Vector3(-forwardDir.y, forwardDir.x, 0f).normalized;
        Vector3 originPosition = transform.position;

        for (int i = 0; i < rayCount; i++)
        {
            float currentAngle = startAngle + i * angleStep;

            Vector3 dir = DirFromAngle(currentAngle);
            dir = transform.TransformDirection(dir);

            float t = (float)i / (rayCount - 1);
            float widthOffset = startWidth * (t - 0.5f);

            Vector3 rightOffset = rightDir * widthOffset;
            Vector3 innerPoint = originPosition + rightOffset;

            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(innerPoint, innerPoint + dir * Distance);
        }

        Gizmos.color = Color.red;
        Vector3 startLeft = originPosition + rightDir * (-startWidth / 2f);
        Vector3 startRight = originPosition + rightDir * (startWidth / 2f);
        Gizmos.DrawLine(startLeft, startRight);
    }
}