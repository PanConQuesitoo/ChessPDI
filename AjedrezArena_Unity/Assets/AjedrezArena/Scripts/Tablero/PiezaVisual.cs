// =====================================================================
//  PiezaVisual.cs  -  Representación 3D de una pieza.
//  Estructura:  PiezaVisual (raíz: posición en el tablero)
//                 └ Modelo (aquí se aplican rebotes, aplastamiento e inclinación)
//  Así las animaciones "de gelatina" nunca desordenan la posición lógica.
// =====================================================================
using System.Collections;
using UnityEngine;

namespace AjedrezArena
{
    public class PiezaVisual : MonoBehaviour
    {
        public TipoPieza Tipo;
        public Bando Bando;
        public Casilla Casilla;
        public Transform Modelo { get; private set; }

        Renderer[] renderers;
        bool[][] esAcento;          // por renderer y por slot: ¿era el material "Pieza_Acento"?
        MaterialPropertyBlock bloque;
        Quaternion rotacionBase;
        Vector3 escalaBase;

        // resorte de aplastamiento (squash & stretch)
        float aplastar, velAplastar;
        // salto vertical (onda del tablero)
        float alturaSalto, velSalto;
        bool enElAire;
        // inclinación cuando la mano la lleva
        Quaternion inclinacion = Quaternion.identity;
        // brillo
        Color brilloActual = Color.black, brilloObjetivo = Color.black;
        float pulso;

        public bool Ocupada { get; set; }   // true mientras la mano la lleva o se anima

        public void Configurar(TipoPieza tipo, Bando bando, Casilla c, GameObject modelo, Quaternion rotModelo)
        {
            Tipo = tipo; Bando = bando; Casilla = c;
            CambiarModelo(modelo, rotModelo);
        }

        public void CambiarModelo(GameObject modelo, Quaternion rotModelo)
        {
            if (Modelo != null) Destroy(Modelo.gameObject);
            var contenedor = new GameObject("Modelo").transform;
            contenedor.SetParent(transform, false);
            modelo.transform.SetParent(contenedor, false);
            Modelo = contenedor;
            rotacionBase = rotModelo;
            escalaBase = Vector3.one;
            Modelo.localRotation = rotacionBase;
            renderers = Modelo.GetComponentsInChildren<Renderer>();
            esAcento = new bool[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++)
            {
                var mats = renderers[i].sharedMaterials;
                esAcento[i] = new bool[mats.Length];
                for (int j = 0; j < mats.Length; j++)
                    esAcento[i][j] = mats[j] != null && mats[j].name.Contains("Acento");
            }
            bloque = new MaterialPropertyBlock();
            // las piezas no deben bloquear raycasts ni chocar con nada
            foreach (var col in Modelo.GetComponentsInChildren<Collider>()) Destroy(col);
        }

        /// <summary>Material del cuerpo (color del bando) y, opcional, del acento (oro, azul, neón...).</summary>
        public void AsignarMaterial(Material m, Material acento = null)
        {
            if (renderers == null) return;
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                var mats = r.sharedMaterials;
                for (int j = 0; j < mats.Length; j++)
                    mats[j] = (acento != null && esAcento[i] != null && j < esAcento[i].Length && esAcento[i][j]) ? acento : m;
                r.sharedMaterials = mats;
            }
        }

        public Material MaterialPrincipal
        {
            get { return renderers != null && renderers.Length > 0 && renderers[0] != null ? renderers[0].sharedMaterial : null; }
        }

        /// <summary>Caja que envuelve la pieza en coordenadas de mundo.</summary>
        public Bounds Limites()
        {
            if (renderers == null || renderers.Length == 0) return new Bounds(transform.position + Vector3.up * 0.4f, Vector3.one * 0.6f);
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        public float Altura
        {
            get
            {
                switch (Tipo)
                {
                    case TipoPieza.Peon: return 0.75f;
                    case TipoPieza.Torre: return 0.86f;
                    case TipoPieza.Caballo: return 1.1f;
                    case TipoPieza.Alfil: return 1.15f;
                    case TipoPieza.Dama: return 1.36f;
                    default: return 1.55f;
                }
            }
        }

        // ------------------------------------------------------------ Brillo
        public void Resaltar(Color c) { brilloObjetivo = c; }
        public void QuitarResaltado() { brilloObjetivo = Color.black; }

        // ------------------------------------------------------- Animaciones
        /// <summary>Golpe al aterrizar: se aplasta y rebota como gelatina.</summary>
        public void Aplastar(float fuerza)
        {
            aplastar = Mathf.Clamp(fuerza, 0f, 0.6f);
            velAplastar = 0f;
        }

        /// <summary>Salto provocado por la onda expansiva de un golpe cercano.</summary>
        public void Saltar(float altura, float retardo)
        {
            if (altura < 0.01f) return;
            StartCoroutine(CorSaltar(altura, retardo));
        }

        IEnumerator CorSaltar(float altura, float retardo)
        {
            if (retardo > 0f) yield return new WaitForSeconds(retardo);
            Aplastar(Mathf.Min(0.35f, altura * 0.8f));      // se encoge antes de saltar
            yield return new WaitForSeconds(0.05f);
            velSalto = Mathf.Max(velSalto, Mathf.Sqrt(2f * 25f * altura));
            enElAire = true;
        }

        /// <summary>Inclina el modelo según la velocidad (cuando va colgando de la mano).</summary>
        public void Inclinar(Vector3 velocidad, float grados)
        {
            Vector3 eje = Vector3.Cross(Vector3.up, velocidad);
            float ang = Mathf.Clamp(velocidad.magnitude * grados, 0f, 50f);
            Quaternion objetivo = eje.sqrMagnitude > 1e-6f ? Quaternion.AngleAxis(ang, eje.normalized) : Quaternion.identity;
            inclinacion = Quaternion.Slerp(inclinacion, objetivo, 1f - Mathf.Exp(-Time.deltaTime * 10f));
        }

        public void Enderezar() { inclinacion = Quaternion.identity; GiroExtra = 0f; }

        /// <summary>Giro adicional sobre el eje vertical (para el "giro" antes del golpe).</summary>
        public float GiroExtra { get; set; }

        /// <summary>Movimiento en arco hasta 'destino' (lo usa la torre al enrocar y las piezas que vuelven).</summary>
        public Coroutine MoverEnArco(Vector3 destino, float duracion, float alturaArco, System.Action alTerminar = null)
        {
            return StartCoroutine(CorArco(destino, duracion, alturaArco, alTerminar));
        }

        IEnumerator CorArco(Vector3 destino, float duracion, float alturaArco, System.Action alTerminar)
        {
            Ocupada = true;
            Vector3 inicio = transform.position;
            float t = 0f;
            while (t < duracion)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duracion);
                float s = k * k * (3f - 2f * k);
                transform.position = Vector3.Lerp(inicio, destino, s) + Vector3.up * Mathf.Sin(k * Mathf.PI) * alturaArco;
                yield return null;
            }
            transform.position = destino;
            Aplastar(0.25f);
            Ocupada = false;
            if (alTerminar != null) alTerminar();
        }

        void LateUpdate()
        {
            if (Modelo == null) return;
            float dt = Time.deltaTime;
            // resorte amortiguado: x'' = -k x - c x'
            const float k = 260f, c = 11f;
            velAplastar += (-k * aplastar - c * velAplastar) * dt;
            aplastar += velAplastar * dt;
            // salto con gravedad
            if (enElAire)
            {
                velSalto -= 25f * dt;
                alturaSalto += velSalto * dt;
                if (alturaSalto <= 0f)
                {
                    alturaSalto = 0f;
                    enElAire = false;
                    Aplastar(Mathf.Min(0.3f, Mathf.Abs(velSalto) * 0.04f));
                    velSalto = 0f;
                }
            }
            float sy = 1f - aplastar;
            float sxz = 1f + aplastar * 0.5f;
            Modelo.localScale = new Vector3(escalaBase.x * sxz, escalaBase.y * sy, escalaBase.z * sxz);
            Modelo.localPosition = Vector3.up * alturaSalto;
            Modelo.localRotation = inclinacion * Quaternion.AngleAxis(GiroExtra, Vector3.up) * rotacionBase;

            // brillo (emisión) con pulso suave
            brilloActual = Color.Lerp(brilloActual, brilloObjetivo, 1f - Mathf.Exp(-dt * 12f));
            pulso += dt * 5f;
            Color e = brilloActual * (0.75f + 0.25f * Mathf.Sin(pulso));
            if (renderers != null)
            {
                foreach (var r in renderers)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(bloque);
                    bloque.SetColor("_EmissionColor", e);
                    r.SetPropertyBlock(bloque);
                }
            }
        }
    }
}
