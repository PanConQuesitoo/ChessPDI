// =====================================================================
//  ManoAnimador.cs  -  Abre y cierra los dedos de la mano.
//
//  Busca en el modelo las falanges por nombre (Dedo_Indice_1, Dedo_Indice_2,
//  Pulgar_1, ...). Cada falange tiene el pivote en su articulación, así que
//  cerrar la mano = rotar cada falange sobre un eje.
//
//  Calibración automática: para cada falange prueba varios ejes/sentidos y
//  se queda con el que acerca la punta del dedo al "Centro_Agarre". Así no
//  importa cómo convirtió los ejes el importador FBX.
// =====================================================================
using System.Collections.Generic;
using UnityEngine;

namespace AjedrezArena
{
    public class ManoAnimador : MonoBehaviour
    {
        [Range(0f, 1f)] public float cierre = 0.15f;     // 0 = abierta, 1 = puño
        public float velocidad = 14f;
        public float anguloProximal = 72f;
        public float anguloDistal = 88f;
        public float anguloPunta = 62f;
        public float anguloPulgar = 45f;

        class Falange
        {
            public Transform t;
            public Quaternion baseRot;
            public Vector3 eje;
            public float maxAngulo;
            public bool distal;
            public bool punta;
        }

        readonly List<Falange> falanges = new List<Falange>();
        public Transform CentroAgarre { get; private set; }
        float cierreActual;
        float temblor;

        void Awake() { Preparar(); }

        public void Preparar()
        {
            falanges.Clear();
            Transform[] todos = GetComponentsInChildren<Transform>(true);
            foreach (var t in todos)
                if (t.name.StartsWith("Centro_Agarre")) CentroAgarre = t;
            if (CentroAgarre == null)
            {
                CentroAgarre = new GameObject("Centro_Agarre").transform;
                CentroAgarre.SetParent(transform, false);
                CentroAgarre.localPosition = new Vector3(0f, -0.28f, 0.27f);
            }
            foreach (var t in todos)
            {
                string n = t.name;
                int punto = n.IndexOf('.');
                if (punto >= 0) n = n.Substring(0, punto);          // "Dedo_Indice_1.001" -> "Dedo_Indice_1"
                bool dedo = n.StartsWith("Dedo_"), pulgar = n.StartsWith("Pulgar_");
                if (!dedo && !pulgar) continue;
                bool distal = n.EndsWith("_2"), punta = n.EndsWith("_3");
                if (!distal && !punta && !n.EndsWith("_1")) continue;
                var f = new Falange
                {
                    t = t,
                    baseRot = t.localRotation,
                    distal = distal || punta,
                    punta = punta,
                    maxAngulo = pulgar ? anguloPulgar : (punta ? anguloPunta : (distal ? anguloDistal : anguloProximal))
                };
                f.eje = Calibrar(f);
                falanges.Add(f);
            }
        }

        Transform PuntaDe(Transform t)
        {
            foreach (var c in t.GetComponentsInChildren<Transform>(true))
                if (c.name.StartsWith("Punta")) return c;
            // sin punta: usa el hijo más lejano
            Transform lejos = t;
            float d = 0f;
            foreach (var c in t.GetComponentsInChildren<Transform>(true))
            {
                float dd = (c.position - t.position).sqrMagnitude;
                if (dd > d) { d = dd; lejos = c; }
            }
            return lejos;
        }

        Vector3 Calibrar(Falange f)
        {
            Transform punta = PuntaDe(f.t);
            if (punta == f.t) return Vector3.right;
            Vector3[] candidatos = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            Vector3 mejor = Vector3.right;
            float mejorDist = float.MaxValue;
            foreach (var eje in candidatos)
            {
                f.t.localRotation = f.baseRot * Quaternion.AngleAxis(40f, eje);
                float d = (punta.position - CentroAgarre.position).sqrMagnitude;
                if (d < mejorDist) { mejorDist = d; mejor = eje; }
            }
            f.t.localRotation = f.baseRot;
            return mejor;
        }

        /// <summary>Temblor de los dedos (cuando la pieza se carga de energía).</summary>
        public void Temblar(float cantidad) { temblor = cantidad; }

        void LateUpdate()
        {
            cierreActual = Mathf.MoveTowards(cierreActual, cierre, velocidad * Time.deltaTime * 0.25f);
            cierreActual = Mathf.Lerp(cierreActual, cierre, 1f - Mathf.Exp(-velocidad * Time.deltaTime));
            float c = cierreActual;
            float cDistal = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.15f, 1f, c));
            float ruido = temblor > 0f ? (Mathf.PerlinNoise(Time.time * 30f, 0f) - 0.5f) * temblor * 20f : 0f;
            foreach (var f in falanges)
            {
                if (f.t == null) continue;
                float k = f.punta ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 1f, c)) : (f.distal ? cDistal : c);
                f.t.localRotation = f.baseRot * Quaternion.AngleAxis(f.maxAngulo * k + ruido, f.eje);
            }
        }
    }
}
