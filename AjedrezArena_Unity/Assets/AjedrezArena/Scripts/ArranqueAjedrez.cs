// =====================================================================
//  ArranqueAjedrez.cs  -  EL ÚNICO componente que necesitas en la escena.
//  Ponlo en un GameObject vacío y dale Play: crea cámara, luz, tablero,
//  piezas, mano, efectos y escenarios. Si los modelos FBX no están
//  asignados, usa versiones con primitivas para que puedas probar ya.
//  (El menú "Ajedrez Arena > Configurar escena" asigna todo solo.)
// =====================================================================
using System.Collections.Generic;
using UnityEngine;

namespace AjedrezArena
{
    [DefaultExecutionOrder(-100)]
    public class ArranqueAjedrez : MonoBehaviour
    {
        [Header("Modelos de piezas (Assets/AjedrezArena/Modelos/Piezas)")]
        public GameObject peon;
        public GameObject torre;
        public GameObject caballo;
        public GameObject alfil;
        public GameObject dama;
        public GameObject rey;

        [Header("Otros modelos")]
        public GameObject tablero;
        public GameObject mano;

        [Header("Escenarios (en orden: teclas 1..9, 0)")]
        public List<GameObject> escenarios = new List<GameObject>();
        [Header("Juegos temáticos (tablero + piezas de cada lugar)")]
        public List<ModelosTema> modelosTemas = new List<ModelosTema>();
        [Tooltip("Cambiar piezas y tablero al cambiar de escenario")]
        public bool piezasTematicas = true;
        public int escenarioInicial = 0;

        [Header("Materiales (opcional)")]
        public Material materialBlancas;
        public Material materialNegras;
        public Material materialAditivo;
        public Material materialAlfa;
        public Material materialHaz;

        [Header("Entrada de la mano")]
        [Tooltip("Escuchar por UDP la posición de la mano que envía Python (cámara + red neuronal).")]
        public bool usarUDP = false;
        public int puertoUDP = 5065;
        [Tooltip("Si no llegan datos por UDP, usar el mouse.")]
        public bool mouseDeRespaldo = true;

                [Header("Entrada por camara (detector de jugadas del tablero fisico)")]
        [Tooltip("Aceptar jugadas detectadas por la camara/red neuronal (UDP, archivo o webcam).")]
        public bool usarDeteccionCamara = false;
        public int puertoDeteccion = 5066;
        [Tooltip("Opcional: .json donde Python escribe la ultima deteccion.")]
        public string archivoDetecciones = "";
        [Tooltip("Tomar las fotos con la webcam y llamar a detector_ajedrez.py desde Unity (B = foto base, G = detectar).")]
        public bool usarWebcamDetector = false;
        public string rutaPython = "C:/Proyecto_Ajedrez_PDI/venv/Scripts/python.exe";
        public string rutaScript = "C:/Proyecto_Ajedrez_PDI/entrega_unity/detector_ajedrez.py";

        [Header("Menu con las manos")]
        [Tooltip("Al dar Play: menu con fondo 360 que se maneja con la mano; enciende la camara (ajedrez_vivo.py) sola.")]
        public bool usarMenu = true;
        public MenuAjedrez Menu { get; private set; }
        public ConexionCamara Conexion { get; private set; }

        [Header("Ajustes de juego")]
        public float rapidezFuego = 5f;
        public float rapidezRayo = 12f;
        public float aceleracionRayo = 80f;
        public float tiempoParaCargar = 2.2f;
        public float rotacionCaballo = 0f;

        public ControladorTablero Tablero { get; private set; }
        public ControladorMano Mano { get; private set; }
        public ControladorEscenarios Escenarios { get; private set; }
        public EntradaCamaraAjedrez EntradaCamara { get; private set; }

        bool ayuda = true;
        float tiempoAyuda = 12f;

        void Awake()
        {
            if (usarMenu) { usarUDP = true; usarDeteccionCamara = true; }

            // ---------- con camara, Unity debe seguir corriendo aunque la ventana activa sea la de Python
            if (usarUDP || usarDeteccionCamara || usarWebcamDetector) Application.runInBackground = true;

            // ---------- la vista previa del editor sobra al jugar
            var vista = transform.Find("VistaPrevia (solo editor)");
            if (vista != null) { vista.gameObject.SetActive(false); Destroy(vista.gameObject); }

            // ---------- efectos
            var fx = GetComponentInChildren<EfectosAjedrez>();
            if (fx == null)
            {
                var go = new GameObject("Efectos");
                go.transform.SetParent(transform, false);
                go.SetActive(false);                 // para asignar materiales antes de su Awake
                fx = go.AddComponent<EfectosAjedrez>();
                fx.materialAditivo = materialAditivo;
                fx.materialAlfa = materialAlfa;
                fx.materialHaz = materialHaz;
                go.SetActive(true);
            }

            // ---------- tablero
            var gt = new GameObject("Tablero");
            gt.transform.SetParent(transform, false);
            gt.SetActive(false);
            Tablero = gt.AddComponent<ControladorTablero>();
            Tablero.prefabTablero = tablero;
            Tablero.prefabPeon = peon; Tablero.prefabTorre = torre; Tablero.prefabCaballo = caballo;
            Tablero.prefabAlfil = alfil; Tablero.prefabDama = dama; Tablero.prefabRey = rey;
            Tablero.materialBlancas = materialBlancas; Tablero.materialNegras = materialNegras;
            Tablero.rotacionCaballo = rotacionCaballo;
            gt.SetActive(true);

            // ---------- cámara
            var cam = Camera.main;
            if (cam == null)
            {
                var gc = new GameObject("Main Camera");
                gc.tag = "MainCamera";
                cam = gc.AddComponent<Camera>();
                gc.AddComponent<AudioListener>();
            }
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 400f;
            cam.fieldOfView = 50f;
            var orb = cam.GetComponent<CamaraAjedrez>();
            if (orb == null) orb = cam.gameObject.AddComponent<CamaraAjedrez>();
            orb.objetivo = gt.transform;

            // ---------- escenarios
            var ge = new GameObject("Escenarios");
            ge.transform.SetParent(transform, false);
            ge.SetActive(false);
            Escenarios = ge.AddComponent<ControladorEscenarios>();
            Escenarios.escenarios = ControladorEscenarios.Predeterminados();
            for (int i = 0; i < Escenarios.escenarios.Count && i < escenarios.Count; i++)
                Escenarios.escenarios[i].prefab = escenarios[i];
            foreach (var d in Escenarios.escenarios)
                foreach (var mt in modelosTemas)
                    if (mt != null && mt.nombre == d.nombre) d.tema.modelos = mt;
            Escenarios.inicial = escenarioInicial;
            Escenarios.ancla = gt.transform;
            Escenarios.tablero = Tablero;
            Escenarios.camara = cam;
            Escenarios.temasDePiezas = piezasTematicas;
            ge.SetActive(true);

            // ---------- mano
            var gm = new GameObject("Mano");
            gm.transform.SetParent(transform, false);
            gm.SetActive(false);
            Mano = gm.AddComponent<ControladorMano>();
            Mano.tablero = Tablero;
            Mano.camara = cam;
            Mano.prefabMano = mano;
            Mano.rapidezFuego = rapidezFuego;
            Mano.rapidezRayo = rapidezRayo;
            Mano.aceleracionRayo = aceleracionRayo;
            Mano.tiempoParaCargar = tiempoParaCargar;
            if (usarUDP)
            {
                var udp = gm.AddComponent<EntradaManoUDP>();
                udp.puerto = puertoUDP;
                Mano.fuentes.Add(udp);
            }
            if (!usarUDP || mouseDeRespaldo)
            {
                var mouse = gm.AddComponent<EntradaManoMouse>();
                mouse.soloSiSeUsa = usarUDP;   // con camara, el mouse solo manda si lo tocas
                Mano.fuentes.Add(mouse);
            }
            gm.SetActive(true);
            Escenarios.mano = Mano;

            // ---------- jugadas detectadas por la camara
            if (usarDeteccionCamara || usarWebcamDetector)
            {
                var gd = new GameObject("EntradaCamara");
                gd.transform.SetParent(transform, false);
                gd.SetActive(false);
                EntradaCamara = gd.AddComponent<EntradaCamaraAjedrez>();
                EntradaCamara.tablero = Tablero;
                EntradaCamara.puertoUDP = puertoDeteccion;
                EntradaCamara.archivoDetecciones = archivoDetecciones;
                if (usarWebcamDetector)
                {
                    var det = gd.AddComponent<DetectorCamaraPython>();
                    det.entrada = EntradaCamara;
                    det.rutaPython = rutaPython;
                    det.rutaScript = rutaScript;
                }
                gd.SetActive(true);
            }

            // ---------- menu que se maneja con la mano (y enciende la camara)
            if (usarMenu)
            {
                var gmenu = new GameObject("MenuConLaMano");
                gmenu.transform.SetParent(transform, false);
                Conexion = gmenu.AddComponent<ConexionCamara>();
                if (!string.IsNullOrEmpty(rutaPython) && System.IO.File.Exists(rutaPython)) Conexion.rutaPython = rutaPython;
                Menu = gmenu.AddComponent<MenuAjedrez>();
                Menu.arranque = this;
                Menu.camara = Conexion;
            }
        }

        void Start()
        {
            Tablero.Inicializar();
            Escenarios.Inicializar();
        }

        void Update()
        {
            if (EntradaUtil.Pulsada(TeclaJuego.Ayuda)) { ayuda = !ayuda; tiempoAyuda = ayuda ? 9999f : 0f; }
            if (tiempoAyuda > 0f) tiempoAyuda -= Time.deltaTime; else ayuda = false;
        }

        void OnGUI()
        {
            if (MenuAjedrez.Activo) return;
            var estilo = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(12, Screen.height / 60) };
            if (!ayuda)
            {
                ControladorTablero.Sombra(new Rect(16, Screen.height - 30, 600, 24), "H: ayuda", estilo);
                return;
            }
            string t =
                "CONTROLES\n" +
                "Clic izquierdo (o Espacio): agarrar / soltar pieza\n" +
                "  · suave → LUZ   · rápido → FUEGO   · sacudida brusca → RAYO\n" +
                "  · mantén la pieza quieta ~2 s → SUPERNOVA\n" +
                "Clic derecho + arrastrar: girar cámara · Rueda: zoom · C: vistas · V: cenital\n" +
                "1-9, 0: escenario (cada uno con su tablero y piezas) · N: siguiente · R: reiniciar · F1: medidores · H: ocultar";
                        if (EntradaCamara != null)
                t += "\nCAMARA: las jugadas del tablero fisico llegan solas" + (usarWebcamDetector ? " - B: foto base - G: detectar jugada" : "") +
                     " - Enter: aceptar jugada dudosa - Retroceso: descartarla";
            ControladorTablero.Sombra(new Rect(16, Screen.height - 150, 900, 150), t, estilo);
        }
    }
}
