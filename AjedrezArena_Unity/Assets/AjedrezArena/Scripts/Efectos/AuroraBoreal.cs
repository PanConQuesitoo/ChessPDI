// =====================================================================
//  AuroraBoreal.cs  -  Cortinas de aurora animadas (malla generada con
//  colores por vértice). Se usa en el escenario de Islandia.
// =====================================================================
using UnityEngine;

namespace AjedrezArena
{
    public class AuroraBoreal : MonoBehaviour
    {
        public int cortinas = 3;
        public int segmentos = 90;
        public float radio = 38f;
        public float alturaBase = 9f;
        public float alto = 16f;
        public float arcoGrados = 140f;

        Mesh[] mallas;
        Vector3[][] baseVerts;
        Vector3[][] verts;
        Color[][] colores;
        float[] fases;

        static readonly Color Verde = new Color(0.2f, 1f, 0.55f);
        static readonly Color Turquesa = new Color(0.2f, 0.9f, 0.9f);
        static readonly Color Violeta = new Color(0.65f, 0.25f, 1f);

        public void Construir(Material materialAditivo)
        {
            mallas = new Mesh[cortinas];
            baseVerts = new Vector3[cortinas][];
            verts = new Vector3[cortinas][];
            colores = new Color[cortinas][];
            fases = new float[cortinas];
            for (int c = 0; c < cortinas; c++)
            {
                fases[c] = c * 1.7f;
                var go = new GameObject("Cortina" + c);
                go.transform.SetParent(transform, false);
                var mf = go.AddComponent<MeshFilter>();
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = materialAditivo;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                var m = new Mesh { name = "Aurora" };
                m.MarkDynamic();
                int n = (segmentos + 1) * 2;
                var v = new Vector3[n];
                var uv = new Vector2[n];
                float r = radio + c * 5f;
                float a0 = -arcoGrados * 0.5f + c * 12f;
                for (int i = 0; i <= segmentos; i++)
                {
                    float t = i / (float)segmentos;
                    float a = (a0 + arcoGrados * t) * Mathf.Deg2Rad;
                    // centrado en +Z (al fondo, frente al jugador de blancas)
                    Vector3 p = new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
                    v[i * 2] = p + Vector3.up * (alturaBase + c * 2f);
                    v[i * 2 + 1] = p + Vector3.up * (alturaBase + c * 2f + alto);
                    uv[i * 2] = new Vector2(t, 0f);
                    uv[i * 2 + 1] = new Vector2(t, 1f);
                }
                // doble cara: triángulos de ida y de vuelta
                var tri = new int[segmentos * 12];
                int k = 0;
                for (int i = 0; i < segmentos; i++)
                {
                    int a = i * 2, b = i * 2 + 1, cc = i * 2 + 2, d = i * 2 + 3;
                    tri[k++] = a; tri[k++] = b; tri[k++] = cc;
                    tri[k++] = cc; tri[k++] = b; tri[k++] = d;
                    tri[k++] = a; tri[k++] = cc; tri[k++] = b;
                    tri[k++] = cc; tri[k++] = d; tri[k++] = b;
                }
                m.vertices = v;
                m.uv = uv;
                m.triangles = tri;
                m.colors = new Color[n];
                m.bounds = new Bounds(Vector3.up * (alturaBase + alto * 0.5f), Vector3.one * (radio * 3f));
                mf.sharedMesh = m;
                mallas[c] = m;
                baseVerts[c] = v;
                verts[c] = (Vector3[])v.Clone();
                colores[c] = new Color[n];
            }
        }

        void Update()
        {
            if (mallas == null) return;
            float t = Time.time;
            for (int c = 0; c < mallas.Length; c++)
            {
                var bv = baseVerts[c];
                var v = verts[c];
                var col = colores[c];
                for (int i = 0; i <= segmentos; i++)
                {
                    float u = i / (float)segmentos;
                    float ondaX = Mathf.Sin(u * 9f + t * 0.35f + fases[c]) * 2.2f + Mathf.Sin(u * 23f - t * 0.6f) * 0.8f;
                    float ondaZ = Mathf.Cos(u * 7f + t * 0.25f + fases[c]) * 2.5f;
                    Vector3 off = new Vector3(ondaX, 0f, ondaZ);
                    float pliegue = Mathf.Sin(u * 15f + t * 0.8f + fases[c] * 2f) * 0.5f + 0.5f;
                    v[i * 2] = bv[i * 2] + off;
                    v[i * 2 + 1] = bv[i * 2 + 1] + off * 1.4f + Vector3.up * pliegue * 2f;
                    float brillo = Mathf.Clamp01(0.25f + 0.75f * Mathf.PerlinNoise(u * 4f + fases[c], t * 0.15f));
                    float bordes = Mathf.Sin(u * Mathf.PI);
                    Color abajo = Color.Lerp(Verde, Turquesa, pliegue) * brillo;
                    abajo.a = 0.75f * bordes * brillo;
                    Color arriba = Color.Lerp(Verde, Violeta, 0.4f + 0.6f * pliegue) * brillo;
                    arriba.a = 0f;
                    col[i * 2] = abajo;
                    col[i * 2 + 1] = arriba;
                }
                mallas[c].vertices = v;
                mallas[c].colors = col;
            }
        }
    }
}
