// =====================================================================
//  PiezaPrimitiva.cs  -  Piezas de respaldo hechas con primitivas de
//  Unity. Solo se usan si todavía no importaste los FBX de Blender,
//  para que el juego se pueda probar desde el primer minuto.
// =====================================================================
using UnityEngine;

namespace AjedrezArena
{
    public static class PiezaPrimitiva
    {
        static GameObject Parte(Transform padre, PrimitiveType tipo, Vector3 pos, Vector3 escala, Vector3 rot = default(Vector3))
        {
            var g = GameObject.CreatePrimitive(tipo);
            Object.Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(padre, false);
            g.transform.localPosition = pos;
            g.transform.localScale = escala;
            g.transform.localRotation = Quaternion.Euler(rot);
            return g;
        }

        public static GameObject Crear(TipoPieza tipo)
        {
            var raiz = new GameObject(tipo.ToString());
            var t = raiz.transform;
            // base común (los cilindros de Unity miden 2 de alto)
            Parte(t, PrimitiveType.Cylinder, new Vector3(0, 0.06f, 0), new Vector3(0.72f, 0.06f, 0.72f));
            Parte(t, PrimitiveType.Cylinder, new Vector3(0, 0.16f, 0), new Vector3(0.5f, 0.05f, 0.5f));
            switch (tipo)
            {
                case TipoPieza.Peon:
                    Parte(t, PrimitiveType.Cylinder, new Vector3(0, 0.32f, 0), new Vector3(0.24f, 0.14f, 0.24f));
                    Parte(t, PrimitiveType.Sphere, new Vector3(0, 0.58f, 0), Vector3.one * 0.3f);
                    break;
                case TipoPieza.Torre:
                    Parte(t, PrimitiveType.Cylinder, new Vector3(0, 0.45f, 0), new Vector3(0.42f, 0.25f, 0.42f));
                    Parte(t, PrimitiveType.Cylinder, new Vector3(0, 0.76f, 0), new Vector3(0.55f, 0.08f, 0.55f));
                    for (int i = 0; i < 4; i++)
                        Parte(t, PrimitiveType.Cube, new Vector3(Mathf.Cos(i * Mathf.PI / 2) * 0.2f, 0.88f, Mathf.Sin(i * Mathf.PI / 2) * 0.2f),
                              new Vector3(0.14f, 0.12f, 0.14f), new Vector3(0, -i * 90f, 0));
                    break;
                case TipoPieza.Caballo:
                    Parte(t, PrimitiveType.Cube, new Vector3(0, 0.5f, -0.02f), new Vector3(0.22f, 0.55f, 0.3f), new Vector3(-12f, 0, 0));
                    Parte(t, PrimitiveType.Cube, new Vector3(0, 0.82f, 0.12f), new Vector3(0.2f, 0.22f, 0.42f), new Vector3(20f, 0, 0));
                    Parte(t, PrimitiveType.Cube, new Vector3(0, 0.98f, -0.06f), new Vector3(0.08f, 0.12f, 0.06f));
                    break;
                case TipoPieza.Alfil:
                    Parte(t, PrimitiveType.Cylinder, new Vector3(0, 0.42f, 0), new Vector3(0.2f, 0.22f, 0.2f));
                    Parte(t, PrimitiveType.Sphere, new Vector3(0, 0.85f, 0), new Vector3(0.3f, 0.42f, 0.3f));
                    Parte(t, PrimitiveType.Sphere, new Vector3(0, 1.1f, 0), Vector3.one * 0.09f);
                    break;
                case TipoPieza.Dama:
                    Parte(t, PrimitiveType.Cylinder, new Vector3(0, 0.5f, 0), new Vector3(0.24f, 0.32f, 0.24f));
                    Parte(t, PrimitiveType.Cylinder, new Vector3(0, 1.0f, 0), new Vector3(0.42f, 0.12f, 0.42f));
                    for (int i = 0; i < 8; i++)
                        Parte(t, PrimitiveType.Sphere, new Vector3(Mathf.Cos(i * Mathf.PI / 4) * 0.2f, 1.14f, Mathf.Sin(i * Mathf.PI / 4) * 0.2f), Vector3.one * 0.08f);
                    Parte(t, PrimitiveType.Sphere, new Vector3(0, 1.2f, 0), Vector3.one * 0.16f);
                    break;
                default: // Rey
                    Parte(t, PrimitiveType.Cylinder, new Vector3(0, 0.55f, 0), new Vector3(0.26f, 0.36f, 0.26f));
                    Parte(t, PrimitiveType.Cylinder, new Vector3(0, 1.1f, 0), new Vector3(0.42f, 0.12f, 0.42f));
                    Parte(t, PrimitiveType.Cube, new Vector3(0, 1.4f, 0), new Vector3(0.08f, 0.32f, 0.08f));
                    Parte(t, PrimitiveType.Cube, new Vector3(0, 1.44f, 0), new Vector3(0.24f, 0.08f, 0.08f));
                    break;
            }
            return raiz;
        }
    }
}
