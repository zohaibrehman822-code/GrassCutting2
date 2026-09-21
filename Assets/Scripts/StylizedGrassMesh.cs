using UnityEngine;

/// <summary>
/// One compact tuft shared by the instanced wild and captured grass fields.
/// Seven tapered blades use 21 vertices and 7 triangles per tuft.
/// </summary>
public static class StylizedGrassMesh
{
    private static Mesh shared;

    public static Mesh Shared
    {
        get
        {
            if (shared == null)
            {
                shared = Create();
            }

            return shared;
        }
    }

    private static Mesh Create()
    {
        const int bladeCount = 7;
        var vertices = new Vector3[bladeCount * 3];
        var uv = new Vector2[vertices.Length];
        var triangles = new int[bladeCount * 3];

        for (int blade = 0; blade < bladeCount; blade++)
        {
            float angle = (blade + 0.18f * Mathf.Sin(blade * 3.1f)) *
                          Mathf.PI * 2f / bladeCount;
            Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 sideways = new Vector3(-outward.z, 0f, outward.x);
            float height = 1.45f + 0.32f * Mathf.Sin(blade * 2.4f);
            Vector3 baseCenter = outward * 0.025f;
            Vector3 tip = outward * 0.14f + Vector3.up * height;
            int v = blade * 3;
            int t = blade * 3;

            vertices[v] = baseCenter - sideways * 0.05f;
            vertices[v + 1] = baseCenter + sideways * 0.05f;
            vertices[v + 2] = tip;

            // The source material expects bottom color at UV.y = 1.
            uv[v] = new Vector2(0f, 1f);
            uv[v + 1] = new Vector2(1f, 1f);
            uv[v + 2] = new Vector2(0.5f, 0f);

            triangles[t] = v;
            triangles[t + 1] = v + 2;
            triangles[t + 2] = v + 1;
        }

        var mesh = new Mesh { name = "Instanced Stylized Grass Tuft" };
        mesh.hideFlags = HideFlags.DontSave;
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        Bounds bounds = mesh.bounds;
        bounds.Expand(new Vector3(0.3f, 0f, 0.3f));
        mesh.bounds = bounds;
        mesh.UploadMeshData(true);
        return mesh;
    }
}
