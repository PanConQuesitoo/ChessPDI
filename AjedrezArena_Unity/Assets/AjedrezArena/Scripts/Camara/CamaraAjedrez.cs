// =====================================================================
//  CamaraAjedrez.cs  -  Cámara orbital alrededor del tablero + temblor.
//    Clic derecho + arrastrar : orbitar        Rueda : zoom
//    Q / E                    : girar          C     : cambiar vista
//    V : vista cenital ortográfica (para proyectar sobre la arena)
// =====================================================================
using UnityEngine;

namespace AjedrezArena
{
    [RequireComponent(typeof(Camera))]
    public class CamaraAjedrez : MonoBehaviour
    {
        public static CamaraAjedrez Instancia { get; private set; }

        public Transform objetivo;
        public float distancia = 11.5f;
        public float yaw = 0f;          // 0 = detrás de las blancas mirando hacia las negras
        public float pitch = 50f;
        public float distanciaMin = 5f, distanciaMax = 25f;
        public float sensibilidad = 0.25f;
        [Tooltip("Tamaño ortográfico de la vista cenital (4.6 encuadra el tablero con marco).")]
        public float tamCenital = 4.8f;

        Camera cam;
        float yawSuave, pitchSuave, distSuave;
        Vector2 mouseAnterior;
        bool arrastrando, cenital;
        int vista;
        float temblorFuerza, temblorTiempo, temblorDuracion;
        Vector3 temblorOffset;

        static readonly Vector3[] Vistas = { new Vector3(0f, 50f, 11.5f), new Vector3(180f, 50f, 11.5f), new Vector3(90f, 35f, 13f), new Vector3(30f, 22f, 14f) };

        void Awake()
        {
            Instancia = this;
            cam = GetComponent<Camera>();
            yawSuave = yaw; pitchSuave = pitch; distSuave = distancia;
        }

        public static void Temblar(float fuerza, float duracion)
        {
            if (Instancia == null) return;
            Instancia.temblorFuerza = Mathf.Max(Instancia.temblorFuerza * (1f - Instancia.Progreso()), fuerza);
            Instancia.temblorDuracion = Mathf.Max(0.05f, duracion);
            Instancia.temblorTiempo = 0f;
        }

        float Progreso() { return temblorDuracion <= 0f ? 1f : Mathf.Clamp01(temblorTiempo / temblorDuracion); }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            Vector3 centro = objetivo != null ? objetivo.position : Vector3.zero;

            // ---- controles
            Vector2 m = EntradaUtil.PosicionMouse;
            if (EntradaUtil.BotonDerecho)
            {
                if (arrastrando)
                {
                    Vector2 d = m - mouseAnterior;
                    yaw += d.x * sensibilidad;
                    pitch = Mathf.Clamp(pitch - d.y * sensibilidad, 8f, 89f);
                }
                arrastrando = true;
            }
            else arrastrando = false;
            mouseAnterior = m;
            if (EntradaUtil.Mantenida(TeclaJuego.OrbitarIzquierda)) yaw += 70f * dt;
            if (EntradaUtil.Mantenida(TeclaJuego.OrbitarDerecha)) yaw -= 70f * dt;
            float rueda = EntradaUtil.Rueda;
            if (Mathf.Abs(rueda) > 0.01f)
            {
                if (cenital) tamCenital = Mathf.Clamp(tamCenital * (1f - rueda * 0.08f), 2f, 12f);
                else distancia = Mathf.Clamp(distancia * (1f - rueda * 0.1f), distanciaMin, distanciaMax);
            }
            if (EntradaUtil.Pulsada(TeclaJuego.CambiarVista))
            {
                cenital = false;
                vista = (vista + 1) % Vistas.Length;
                yaw = Vistas[vista].x; pitch = Vistas[vista].y; distancia = Vistas[vista].z;
            }
            if (EntradaUtil.Pulsada(TeclaJuego.VistaCenital)) cenital = !cenital;

            // ---- posición suave
            float k = 1f - Mathf.Exp(-dt * 6f);
            yawSuave = Mathf.LerpAngle(yawSuave, yaw, k);
            pitchSuave = Mathf.Lerp(pitchSuave, cenital ? 90f : pitch, k);
            distSuave = Mathf.Lerp(distSuave, cenital ? 15f : distancia, k);
            cam.orthographic = cenital && pitchSuave > 85f;
            if (cam.orthographic) cam.orthographicSize = tamCenital;

            Quaternion rot = Quaternion.Euler(Mathf.Min(pitchSuave, 89.9f), yawSuave, 0f);
            Vector3 pos = centro - rot * Vector3.forward * distSuave + Vector3.up * 0.0f;

            // ---- temblor (ruido Perlin, decae con el tiempo)
            if (temblorFuerza > 0f)
            {
                temblorTiempo += dt;
                float p = Progreso();
                float f = temblorFuerza * (1f - p) * (1f - p);
                float t = Time.time * 25f;
                temblorOffset = new Vector3(Mathf.PerlinNoise(t, 0.1f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, Mathf.PerlinNoise(t, 3.3f) - 0.5f) * 2f * f;
                if (p >= 1f) temblorFuerza = 0f;
            }
            else temblorOffset = Vector3.zero;

            transform.position = pos + temblorOffset;
            transform.rotation = rot * Quaternion.Euler(temblorOffset.y * 4f, temblorOffset.x * 4f, temblorOffset.z * 6f);
        }
    }
}
