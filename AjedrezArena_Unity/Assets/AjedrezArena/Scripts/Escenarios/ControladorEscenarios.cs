// =====================================================================
//  ControladorEscenarios.cs  -  Cambia entre los escenarios del mundo.
//  Cada escenario define: modelo (FBX), juego de piezas y tablero temático,
//  cielo, sol, luz ambiente, niebla, partículas, luces locales (leídas del
//  FBX), foco sobre el tablero, post-proceso y una sonda de reflejos.
//    Teclas 1..9 y 0 : elegir escenario       N : siguiente
// =====================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AjedrezArena
{
    [Serializable]
    public class DatosEscenario
    {
        public string nombre = "Escenario";
        public string lugar = "";
        public GameObject prefab;
        public TemaPiezas tema = new TemaPiezas();
        public bool interior = false;
        [Header("Cielo")]
        public Color tinteCielo = new Color(0.5f, 0.5f, 0.5f);
        public Color colorSueloCielo = new Color(0.37f, 0.35f, 0.34f);
        public float exposicion = 1.3f;
        public float atmosfera = 1f;
        [Header("Sol")]
        public Color colorSol = Color.white;
        public float intensidadSol = 1.2f;
        public Vector3 rotacionSol = new Vector3(50f, -30f, 0f);
        public bool sombrasSol = true;
        [Header("Ambiente")]
        public Color ambienteCielo = new Color(0.6f, 0.7f, 0.85f);
        public Color ambienteHorizonte = new Color(0.5f, 0.5f, 0.5f);
        public Color ambienteSuelo = new Color(0.3f, 0.28f, 0.25f);
        public Color colorNiebla = new Color(0.7f, 0.75f, 0.8f);
        public float densidadNiebla = 0.012f;
        public TipoAmbiente particulas = TipoAmbiente.Ninguno;
        [Header("Iluminación del tablero")]
        public Color colorFoco = new Color(1f, 0.95f, 0.88f);
        public float intensidadFoco = 1.2f;
        public Color colorContraluz = new Color(0.6f, 0.75f, 1f);
        public float intensidadContraluz = 0.6f;
        [Tooltip("Multiplica las luces que vienen dentro del FBX")]
        public float multiplicadorLuces = 1f;
        public ParametrosPost post = new ParametrosPost();

        public DatosEscenario() { }
        public DatosEscenario(string nombre, string lugar) { this.nombre = nombre; this.lugar = lugar; }
    }

    public class ControladorEscenarios : MonoBehaviour
    {
        public List<DatosEscenario> escenarios = new List<DatosEscenario>();
        public int inicial = 0;
        public Transform ancla;                 // normalmente el tablero
        public ControladorTablero tablero;
        public ControladorMano mano;
        public Camera camara;
        public Light sol;
        public float duracionTransicion = 0.6f;
        public bool temasDePiezas = true;

        public int Actual { get; private set; }
        public DatosEscenario Datos { get { return escenarios.Count > 0 ? escenarios[Actual] : null; } }

        GameObject instancia, ambiente;
        Light foco, contraluz;
        ReflectionProbe sonda;
        float fundido;            // 0 = transparente, 1 = negro
        bool cambiando;
        float tiempoTitulo;
        bool inicializado;
        int lucesCreadas;

        void Reset() { escenarios = Predeterminados(); }

        void Start() { Inicializar(); }

        public void Inicializar()
        {
            if (inicializado) return;
            inicializado = true;
            if (escenarios == null || escenarios.Count == 0) escenarios = Predeterminados();
            if (camara == null) camara = Camera.main;
            if (sol == null) sol = RenderSettings.sun;
            if (sol == null)
            {
                foreach (var l in LucesEscena()) if (l.type == LightType.Directional) { sol = l; break; }
            }
            if (sol == null)
            {
                var go = new GameObject("Sol");
                sol = go.AddComponent<Light>();
                sol.type = LightType.Directional;
            }
            CrearLucesTablero();
            Aplicar(Mathf.Clamp(inicial, 0, escenarios.Count - 1));
        }

        static Light[] LucesEscena()
        {
#if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
#else
            return UnityEngine.Object.FindObjectsOfType<Light>();
#endif
        }

        void CrearLucesTablero()
        {
            Transform a = ancla != null ? ancla : transform;
            // foco principal (luz "de estudio" que hace leer bien las piezas)
            var gf = new GameObject("FocoTablero");
            gf.transform.SetParent(transform, false);
            gf.transform.position = a.position + new Vector3(-3.5f, 9f, -5.5f);
            gf.transform.rotation = Quaternion.LookRotation(a.position - gf.transform.position);
            foco = gf.AddComponent<Light>();
            foco.type = LightType.Spot;
            foco.spotAngle = 70f;
            foco.innerSpotAngle = 40f;
            foco.range = 25f;
            foco.shadows = LightShadows.Soft;
            // contraluz desde atrás: separa las piezas del fondo
            var gc = new GameObject("Contraluz");
            gc.transform.SetParent(transform, false);
            gc.transform.position = a.position + new Vector3(3f, 6f, 8f);
            gc.transform.rotation = Quaternion.LookRotation(a.position - gc.transform.position);
            contraluz = gc.AddComponent<Light>();
            contraluz.type = LightType.Spot;
            contraluz.spotAngle = 80f;
            contraluz.range = 20f;
            contraluz.shadows = LightShadows.None;
            // sonda de reflejos: las piezas brillantes reflejan el escenario
            var gs = new GameObject("SondaReflejos");
            gs.transform.SetParent(transform, false);
            gs.transform.position = a.position + Vector3.up * 2.5f;
            sonda = gs.AddComponent<ReflectionProbe>();
            sonda.mode = ReflectionProbeMode.Realtime;
            sonda.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            sonda.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            sonda.size = new Vector3(80f, 40f, 80f);
            sonda.resolution = 256;
            sonda.hdr = true;
        }

        /// <summary>Los 10 escenarios con su iluminación y su juego de piezas. Los modelos los asigna el menú "Ajedrez Arena".</summary>
        public static List<DatosEscenario> Predeterminados()
        {
            var l = new List<DatosEscenario>();
            l.Add(new DatosEscenario("MachuPicchu", "Machu Picchu, Perú — amanecer entre nubes")
            {
                tema = new TemaPiezas("MachuPicchu", "#cfc6b4", "#4a4440", "#d4af37", "#d4af37", 0f, 0f, 0.35f),
                tinteCielo = new Color(0.45f, 0.6f, 0.85f), colorSueloCielo = new Color(0.3f, 0.38f, 0.28f), exposicion = 1.25f, atmosfera = 0.9f,
                colorSol = new Color(1f, 0.88f, 0.72f), intensidadSol = 1.35f, rotacionSol = new Vector3(28f, -40f, 0f),
                ambienteCielo = new Color(0.62f, 0.72f, 0.85f), ambienteHorizonte = new Color(0.55f, 0.6f, 0.58f), ambienteSuelo = new Color(0.25f, 0.3f, 0.2f),
                colorNiebla = new Color(0.78f, 0.82f, 0.86f), densidadNiebla = 0.018f, particulas = TipoAmbiente.Neblina,
                colorFoco = new Color(1f, 0.93f, 0.82f), intensidadFoco = 1.2f, colorContraluz = new Color(0.7f, 0.8f, 1f), intensidadContraluz = 0.8f,
                post = new ParametrosPost(0.4f, 0.1f, 12f, 10f, 0.2f, Color.white)
            });
            l.Add(new DatosEscenario("Kioto", "Kioto, Japón — primavera de cerezos")
            {
                tema = new TemaPiezas("Kioto", "#efe3c8", "#1a1416", "#d4af37", "#d4af37", 0f, 0.1f, 0.8f),
                tinteCielo = new Color(0.65f, 0.55f, 0.65f), colorSueloCielo = new Color(0.35f, 0.33f, 0.3f), exposicion = 1.3f, atmosfera = 1.1f,
                colorSol = new Color(1f, 0.9f, 0.85f), intensidadSol = 1.2f, rotacionSol = new Vector3(42f, 35f, 0f),
                ambienteCielo = new Color(0.85f, 0.72f, 0.8f), ambienteHorizonte = new Color(0.65f, 0.6f, 0.62f), ambienteSuelo = new Color(0.3f, 0.32f, 0.22f),
                colorNiebla = new Color(0.9f, 0.82f, 0.86f), densidadNiebla = 0.012f, particulas = TipoAmbiente.PetalosSakura,
                colorFoco = new Color(1f, 0.92f, 0.9f), intensidadFoco = 1.1f, colorContraluz = new Color(1f, 0.75f, 0.85f), intensidadContraluz = 0.9f,
                post = new ParametrosPost(0.5f, 0.05f, 10f, 8f, 0.22f, new Color(1f, 0.97f, 0.98f))
            });
            l.Add(new DatosEscenario("Giza", "Giza, Egipto — atardecer en el desierto")
            {
                tema = new TemaPiezas("Giza", "#e8d3a0", "#1f3b73", "#e8c547", "#e8c547", 0f, 0.2f, 0.6f),
                tinteCielo = new Color(0.75f, 0.5f, 0.35f), colorSueloCielo = new Color(0.55f, 0.42f, 0.3f), exposicion = 1.35f, atmosfera = 1.6f,
                colorSol = new Color(1f, 0.72f, 0.45f), intensidadSol = 1.5f, rotacionSol = new Vector3(14f, 150f, 0f),
                ambienteCielo = new Color(0.9f, 0.68f, 0.5f), ambienteHorizonte = new Color(0.8f, 0.6f, 0.45f), ambienteSuelo = new Color(0.45f, 0.35f, 0.25f),
                colorNiebla = new Color(0.92f, 0.74f, 0.55f), densidadNiebla = 0.01f, particulas = TipoAmbiente.Arena,
                colorFoco = new Color(1f, 0.85f, 0.7f), intensidadFoco = 0.9f, colorContraluz = new Color(1f, 0.6f, 0.35f), intensidadContraluz = 1.2f,
                post = new ParametrosPost(0.6f, 0.1f, 15f, 15f, 0.28f, new Color(1f, 0.93f, 0.85f))
            });
            l.Add(new DatosEscenario("Islandia", "Islandia — noche de aurora boreal")
            {
                tema = new TemaPiezas("Islandia", "#bfe6f7", "#15181d", "#7fd6ff", "#7fd6ff", 0f, 0.3f, 0.95f, true),
                tinteCielo = new Color(0.15f, 0.2f, 0.4f), colorSueloCielo = new Color(0.05f, 0.06f, 0.08f), exposicion = 0.25f, atmosfera = 0.6f,
                colorSol = new Color(0.55f, 0.7f, 1f), intensidadSol = 0.4f, rotacionSol = new Vector3(35f, -150f, 0f),
                ambienteCielo = new Color(0.12f, 0.25f, 0.3f), ambienteHorizonte = new Color(0.1f, 0.16f, 0.2f), ambienteSuelo = new Color(0.04f, 0.05f, 0.06f),
                colorNiebla = new Color(0.05f, 0.09f, 0.13f), densidadNiebla = 0.014f, particulas = TipoAmbiente.NieveAurora,
                colorFoco = new Color(0.7f, 0.85f, 1f), intensidadFoco = 2.4f, colorContraluz = new Color(0.3f, 1f, 0.6f), intensidadContraluz = 1.6f,
                post = new ParametrosPost(1.2f, 0.3f, 18f, 15f, 0.35f, new Color(0.85f, 0.95f, 1f))
            });
            l.Add(new DatosEscenario("Santorini", "Santorini, Grecia — hora dorada sobre el Egeo")
            {
                tema = new TemaPiezas("Santorini", "#f7f5f0", "#1d4fa3", "#1d4fa3", "#f7f5f0", 0f, 0f, 0.6f),
                tinteCielo = new Color(0.55f, 0.6f, 0.8f), colorSueloCielo = new Color(0.3f, 0.4f, 0.55f), exposicion = 1.4f, atmosfera = 1.2f,
                colorSol = new Color(1f, 0.8f, 0.6f), intensidadSol = 1.4f, rotacionSol = new Vector3(18f, 200f, 0f),
                ambienteCielo = new Color(0.7f, 0.75f, 0.9f), ambienteHorizonte = new Color(0.85f, 0.75f, 0.65f), ambienteSuelo = new Color(0.5f, 0.45f, 0.4f),
                colorNiebla = new Color(0.85f, 0.8f, 0.78f), densidadNiebla = 0.006f, particulas = TipoAmbiente.BrillosDorados,
                colorFoco = new Color(1f, 0.9f, 0.8f), intensidadFoco = 0.9f, colorContraluz = new Color(1f, 0.7f, 0.45f), intensidadContraluz = 1.1f,
                post = new ParametrosPost(0.5f, 0.15f, 10f, 12f, 0.2f, Color.white)
            });
            l.Add(new DatosEscenario("Cartagena", "Cartagena, Colombia — noche en la ciudad amurallada")
            {
                tema = new TemaPiezas("Cartagena", "#f2d6a2", "#5a2a14", "#e76f51", "#2a9d8f", 0f, 0f, 0.65f),
                tinteCielo = new Color(0.35f, 0.3f, 0.55f), colorSueloCielo = new Color(0.2f, 0.15f, 0.2f), exposicion = 0.7f, atmosfera = 1.4f,
                colorSol = new Color(1f, 0.6f, 0.45f), intensidadSol = 0.7f, rotacionSol = new Vector3(10f, 120f, 0f),
                ambienteCielo = new Color(0.4f, 0.35f, 0.6f), ambienteHorizonte = new Color(0.6f, 0.4f, 0.4f), ambienteSuelo = new Color(0.25f, 0.2f, 0.18f),
                colorNiebla = new Color(0.35f, 0.28f, 0.4f), densidadNiebla = 0.01f, particulas = TipoAmbiente.Luciernagas,
                colorFoco = new Color(1f, 0.8f, 0.6f), intensidadFoco = 1.8f, colorContraluz = new Color(0.6f, 0.5f, 1f), intensidadContraluz = 1.0f,
                post = new ParametrosPost(1.0f, 0.2f, 15f, 18f, 0.3f, new Color(1f, 0.92f, 0.87f))
            });
            l.Add(new DatosEscenario("Louvre", "Museo del Louvre, París — noche junto a la pirámide de cristal")
            {
                tema = new TemaPiezas("Louvre", "#f2efe9", "#3b3d42", "#d4af37", "#d4af37", 0f, 0.6f, 0.75f),
                tinteCielo = new Color(0.1f, 0.15f, 0.35f), colorSueloCielo = new Color(0.04f, 0.04f, 0.06f), exposicion = 0.2f, atmosfera = 0.8f,
                colorSol = new Color(0.55f, 0.65f, 1f), intensidadSol = 0.25f, rotacionSol = new Vector3(40f, 60f, 0f),
                ambienteCielo = new Color(0.12f, 0.15f, 0.28f), ambienteHorizonte = new Color(0.2f, 0.17f, 0.15f), ambienteSuelo = new Color(0.08f, 0.07f, 0.06f),
                colorNiebla = new Color(0.08f, 0.09f, 0.14f), densidadNiebla = 0.007f, particulas = TipoAmbiente.BrillosDorados,
                colorFoco = new Color(1f, 0.9f, 0.75f), intensidadFoco = 2.2f, colorContraluz = new Color(1f, 0.8f, 0.5f), intensidadContraluz = 1.4f,
                multiplicadorLuces = 1.2f, post = new ParametrosPost(1.3f, 0.35f, 18f, 10f, 0.3f, new Color(1f, 0.95f, 0.9f))
            });
            l.Add(new DatosEscenario("MuseoDelOro", "Museo del Oro, Bogotá — bóveda de orfebrería precolombina")
            {
                tema = new TemaPiezas("MuseoDelOro", "#e3b341", "#2b211a", "#8a5a12", "#e3b341", 1f, 0.7f, 0.85f),
                interior = true,
                tinteCielo = new Color(0.1f, 0.08f, 0.06f), colorSueloCielo = Color.black, exposicion = 0.1f, atmosfera = 0.5f,
                colorSol = new Color(1f, 0.8f, 0.55f), intensidadSol = 0.15f, rotacionSol = new Vector3(70f, 0f, 0f), sombrasSol = false,
                ambienteCielo = new Color(0.1f, 0.07f, 0.04f), ambienteHorizonte = new Color(0.08f, 0.06f, 0.04f), ambienteSuelo = new Color(0.03f, 0.02f, 0.02f),
                colorNiebla = new Color(0.05f, 0.035f, 0.02f), densidadNiebla = 0.02f, particulas = TipoAmbiente.MotasPolvo,
                colorFoco = new Color(1f, 0.85f, 0.6f), intensidadFoco = 2.8f, colorContraluz = new Color(1f, 0.7f, 0.3f), intensidadContraluz = 1.2f,
                multiplicadorLuces = 1.3f, post = new ParametrosPost(1.5f, 0.35f, 20f, 10f, 0.4f, new Color(1f, 0.93f, 0.82f))
            });
            l.Add(new DatosEscenario("HistoriaNatural", "Museo de Historia Natural — el gran salón de los dinosaurios")
            {
                tema = new TemaPiezas("HistoriaNatural", "#e8dcc0", "#3a2e24", "#c7832a", "#e8dcc0", 0f, 0.1f, 0.45f),
                interior = true,
                tinteCielo = new Color(0.5f, 0.55f, 0.6f), colorSueloCielo = new Color(0.3f, 0.28f, 0.25f), exposicion = 0.9f, atmosfera = 1f,
                colorSol = new Color(0.95f, 0.97f, 1f), intensidadSol = 0.45f, rotacionSol = new Vector3(75f, 20f, 0f), sombrasSol = false,
                ambienteCielo = new Color(0.55f, 0.55f, 0.56f), ambienteHorizonte = new Color(0.45f, 0.42f, 0.38f), ambienteSuelo = new Color(0.25f, 0.22f, 0.18f),
                colorNiebla = new Color(0.55f, 0.53f, 0.5f), densidadNiebla = 0.008f, particulas = TipoAmbiente.MotasPolvo,
                colorFoco = new Color(1f, 0.96f, 0.88f), intensidadFoco = 1.8f, colorContraluz = new Color(0.8f, 0.9f, 1f), intensidadContraluz = 0.8f,
                post = new ParametrosPost(0.7f, 0.15f, 14f, 6f, 0.28f, new Color(1f, 0.97f, 0.92f))
            });
            l.Add(new DatosEscenario("ArteModerno", "Museo de Arte Moderno — galería de cubo blanco")
            {
                tema = new TemaPiezas("ArteModerno", "#f5f5f5", "#111111", "#ff2d75", "#25e2ff", 0f, 0f, 0.9f, true),
                interior = true,
                tinteCielo = new Color(0.6f, 0.6f, 0.62f), colorSueloCielo = new Color(0.5f, 0.5f, 0.5f), exposicion = 1f, atmosfera = 1f,
                colorSol = Color.white, intensidadSol = 0.55f, rotacionSol = new Vector3(70f, 0f, 0f), sombrasSol = false,
                ambienteCielo = new Color(0.78f, 0.78f, 0.8f), ambienteHorizonte = new Color(0.7f, 0.7f, 0.72f), ambienteSuelo = new Color(0.45f, 0.45f, 0.45f),
                colorNiebla = new Color(0.8f, 0.8f, 0.82f), densidadNiebla = 0.004f, particulas = TipoAmbiente.Ninguno,
                colorFoco = Color.white, intensidadFoco = 1.5f, colorContraluz = new Color(1f, 0.3f, 0.6f), intensidadContraluz = 1.0f,
                post = new ParametrosPost(0.9f, 0f, 12f, 5f, 0.15f, Color.white)
            });
            return l;
        }

        void Update()
        {
            if (!inicializado || escenarios.Count == 0) return;
            TeclaJuego[] teclas = { TeclaJuego.Escenario1, TeclaJuego.Escenario2, TeclaJuego.Escenario3, TeclaJuego.Escenario4,
                                    TeclaJuego.Escenario5, TeclaJuego.Escenario6, TeclaJuego.Escenario7, TeclaJuego.Escenario8,
                                    TeclaJuego.Escenario9, TeclaJuego.Escenario10 };
            for (int i = 0; i < teclas.Length && i < escenarios.Count; i++)
                if (EntradaUtil.Pulsada(teclas[i])) Cambiar(i);
            if (EntradaUtil.Pulsada(TeclaJuego.SiguienteEscenario)) Cambiar((Actual + 1) % escenarios.Count);
            if (tiempoTitulo > 0f) tiempoTitulo -= Time.deltaTime;
        }

        public void Cambiar(int indice)
        {
            if (cambiando || indice == Actual && instancia != null) return;
            StartCoroutine(CorCambiar(indice));
        }

        IEnumerator CorCambiar(int indice)
        {
            cambiando = true;
            for (float t = 0f; t < duracionTransicion * 0.5f; t += Time.deltaTime) { fundido = t / (duracionTransicion * 0.5f); yield return null; }
            fundido = 1f;
            Aplicar(indice);
            yield return null;
            for (float t = 0f; t < duracionTransicion * 0.5f; t += Time.deltaTime) { fundido = 1f - t / (duracionTransicion * 0.5f); yield return null; }
            fundido = 0f;
            cambiando = false;
        }

        void Aplicar(int indice)
        {
            Actual = indice;
            var d = escenarios[indice];
            if (instancia != null) Destroy(instancia);
            if (ambiente != null) Destroy(ambiente);
            Transform a = ancla != null ? ancla : transform;
            lucesCreadas = 0;
            if (d.prefab != null)
            {
                instancia = Instantiate(d.prefab, a.position, a.rotation, transform);
                instancia.name = "Escenario_" + d.nombre;
                foreach (var r in instancia.GetComponentsInChildren<Renderer>())
                {
                    r.shadowCastingMode = ShadowCastingMode.On;
                    r.receiveShadows = true;
                }
                foreach (var c in instancia.GetComponentsInChildren<Collider>()) Destroy(c);
                IluminacionEscena.PrepararMateriales(instancia);
                lucesCreadas = IluminacionEscena.CrearLuces(instancia, a.position, d.multiplicadorLuces);
            }
            // juego de piezas y tablero del lugar
            if (temasDePiezas && tablero != null && d.tema != null) tablero.AplicarTema(d.tema);
            if (temasDePiezas && mano != null && d.tema != null) mano.AplicarTema(d.tema);

            ambiente = EfectosAjedrez.Instancia != null ? EfectosAjedrez.Instancia.CrearAmbiente(d.particulas, transform) : null;
            if (ambiente != null) { ambiente.transform.position = a.position; ambiente.transform.rotation = a.rotation; }

            var cielo = FabricaMateriales.Cielo(d.tinteCielo, d.colorSueloCielo, d.exposicion, d.atmosfera, 0.04f);
            if (cielo != null) RenderSettings.skybox = cielo;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = d.ambienteCielo;
            RenderSettings.ambientEquatorColor = d.ambienteHorizonte;
            RenderSettings.ambientGroundColor = d.ambienteSuelo;
            RenderSettings.fog = d.densidadNiebla > 0f;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = d.colorNiebla;
            RenderSettings.fogDensity = d.densidadNiebla;
            if (sol != null)
            {
                sol.color = d.colorSol;
                sol.intensity = d.intensidadSol;
                sol.transform.rotation = Quaternion.Euler(d.rotacionSol);
                sol.shadows = d.sombrasSol ? LightShadows.Soft : LightShadows.None;
                sol.shadowStrength = 0.85f;
                RenderSettings.sun = sol;
            }
            if (foco != null) { foco.color = d.colorFoco; foco.intensity = d.intensidadFoco * 2f; }
            if (contraluz != null) { contraluz.color = d.colorContraluz; contraluz.intensity = d.intensidadContraluz * 2f; }
            DynamicGI.UpdateEnvironment();
            PostProcesoURP.Aplicar(camara != null ? camara : Camera.main, d.post, transform);
            StartCoroutine(ActualizarSonda());
            tiempoTitulo = 3.5f;
        }

        IEnumerator ActualizarSonda()
        {
            // se espera un cuadro para que el escenario nuevo ya esté dibujado
            yield return null;
            yield return new WaitForEndOfFrame();
            if (sonda != null) sonda.RenderProbe();
        }

        void OnGUI()
        {
            if (fundido > 0.001f)
            {
                Color c = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, fundido);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = c;
            }
            var d = Datos;
            if (d != null && tiempoTitulo > 0f && !MenuAjedrez.Activo)
            {
                var estilo = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.Max(16, Screen.height / 30), alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Italic
                };
                Color c = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(tiempoTitulo));
                ControladorTablero.Sombra(new Rect(0, Screen.height * 0.045f, Screen.width, 50),
                    d.lugar, estilo);
                GUI.color = c;
            }
        }
    }
}
