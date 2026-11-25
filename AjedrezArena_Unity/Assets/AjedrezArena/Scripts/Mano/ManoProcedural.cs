// =====================================================================
//  ManoProcedural.cs  -  Mano de respaldo hecha con primitivas, con la
//  MISMA jerarquía y nombres que Mano.fbx (Palma, Dedo_X_1, Dedo_X_2,
//  Punta_X, Pulgar_1, Pulgar_2, Centro_Agarre). Se usa si no asignaste
//  el modelo de Blender.  Orientación: palma hacia abajo, dedos hacia +Z.
// =====================================================================
using UnityEngine;

namespace AjedrezArena
{
    public static class ManoProcedural
    {
        public static GameObject Crear(Material material)
        {
            var raiz = new GameObject("ManoProcedural");
            var palma = Visual(raiz.transform, "Palma", PrimitiveType.Sphere, Vector3.zero, new Vector3(0.52f, 0.2f, 0.55f), material, true);
            var puno = Visual(palma, "Puno", PrimitiveType.Cylinder, new Vector3(0f, 0f, -0.32f), new Vector3(0.5f, 0.06f, 0.3f), material, false);
            puno.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var centro = new GameObject("Centro_Agarre").transform;
            centro.SetParent(palma, false);
            centro.localPosition = new Vector3(0f, -0.28f, 0.27f);

            string[] nombres = { "Indice", "Medio", "Anular", "Menique" };
            float[] xs = { -0.175f, -0.06f, 0.06f, 0.17f };
            float[] l1 = { 0.20f, 0.22f, 0.21f, 0.16f };
            float[] l2 = { 0.17f, 0.19f, 0.17f, 0.14f };
            float[] r = { 0.062f, 0.065f, 0.062f, 0.055f };
            for (int i = 0; i < 4; i++)
            {
                var f1 = Falange(palma, "Dedo_" + nombres[i] + "_1", new Vector3(xs[i], 0f, 0.2f), l1[i], r[i], material);
                f1.localRotation = Quaternion.Euler(6f, xs[i] * 25f, 0f);
                var f2 = Falange(f1, "Dedo_" + nombres[i] + "_2", new Vector3(0f, 0f, l1[i] - r[i] * 0.6f), l2[i], r[i] * 0.95f, material);
                var punta = new GameObject("Punta_" + nombres[i]).transform;
                punta.SetParent(f2, false);
                punta.localPosition = new Vector3(0f, 0f, l2[i]);
            }
            var p1 = Falange(palma, "Pulgar_1", new Vector3(-0.235f, -0.03f, -0.02f), 0.17f, 0.07f, material);
            p1.localRotation = Quaternion.Euler(10f, -55f, 0f);
            var p2 = Falange(p1, "Pulgar_2", new Vector3(0f, 0f, 0.13f), 0.15f, 0.066f, material);
            var pp = new GameObject("Punta_Pulgar").transform;
            pp.SetParent(p2, false);
            pp.localPosition = new Vector3(0f, 0f, 0.15f);
            return raiz;
        }

        static Transform Visual(Transform padre, string nombre, PrimitiveType tipo, Vector3 pos, Vector3 escala, Material m, bool contenedor)
        {
            // 'contenedor' = true crea un nodo vacío con la malla como hijo (para no escalar a los hijos)
            Transform nodo;
            if (contenedor)
            {
                nodo = new GameObject(nombre).transform;
                nodo.SetParent(padre, false);
                nodo.localPosition = pos;
                var malla = GameObject.CreatePrimitive(tipo);
                malla.name = nombre + "_Malla";
                Object.Destroy(malla.GetComponent<Collider>());
                malla.transform.SetParent(nodo, false);
                malla.transform.localScale = escala;
                if (m != null) malla.GetComponent<Renderer>().sharedMaterial = m;
            }
            else
            {
                var g = GameObject.CreatePrimitive(tipo);
                g.name = nombre;
                Object.Destroy(g.GetComponent<Collider>());
                nodo = g.transform;
                nodo.SetParent(padre, false);
                nodo.localPosition = pos;
                nodo.localScale = escala;
                if (m != null) g.GetComponent<Renderer>().sharedMaterial = m;
            }
            return nodo;
        }

        static Transform Falange(Transform padre, string nombre, Vector3 pos, float largo, float radio, Material m)
        {
            var pivote = new GameObject(nombre).transform;
            pivote.SetParent(padre, false);
            pivote.localPosition = pos;
            var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            cap.name = nombre + "_Malla";
            Object.Destroy(cap.GetComponent<Collider>());
            cap.transform.SetParent(pivote, false);
            cap.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // eje Y de la cápsula -> Z
            cap.transform.localPosition = new Vector3(0f, 0f, largo * 0.5f);
            cap.transform.localScale = new Vector3(radio * 2f, largo * 0.5f, radio * 2f);
            if (m != null) cap.GetComponent<Renderer>().sharedMaterial = m;
            return pivote;
        }
    }
}
