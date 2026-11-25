// =====================================================================
//  MenuAjedrez.cs  -  Menú que se maneja con la MANO (sin mouse ni teclado).
//
//  Fondo: la cámara del juego gira 360° alrededor del tablero del escenario.
//  Cursor: tu mano vista por la cámara (ajedrez_vivo.py). Para elegir un botón:
//     - junta pulgar e índice (pinza), o
//     - deja la mano quieta encima ~1 s (se llena una barra).
//
//  Flujo:  Encendiendo cámara -> Principal -> Jugar -> Elegir tablero -> Calibración
//          -> (visto bueno: tablero visto, quieto, 32 piezas en su sitio, bien orientado)
//          -> cuenta 3, 2, 1 -> la cámara vuela al tablero -> JUEGO
//  En el juego: saca la mano del tablero hacia la derecha y deja el cursor sobre "Menú"
//  para pausar (Continuar, escenario, nueva partida, menú principal, salir).
//
//  Si la cámara no se puede encender, el mouse queda de respaldo solo para estas pantallas.
// =====================================================================
using System.Collections.Generic;
using UnityEngine;

namespace AjedrezArena
{
    public class MenuAjedrez : MonoBehaviour
    {
        /// <summary>true mientras hay un menú en pantalla (los textos del juego se esconden).</summary>
        public static bool Activo { get; private set; }
        /// <summary>Se juega sin tablero físico: la mano es un puntero en la pantalla.</summary>
        public static bool SinTablero { get; private set; }

        public ArranqueAjedrez arranque;
        public ConexionCamara camara;

        [Header("Fondo 360°")]
        public float velocidadGiro = 7f;
        public float radio = 13f;
        public float altura = 6.2f;

        [Header("Selección con la mano")]
        [Tooltip("Segundos con la mano quieta sobre un botón para elegirlo.")]
        public float tiempoMantener = 1.1f;
        [Tooltip("Cuánto más allá del tablero llega el cursor (fracción del tablero).")]
        public float margenTablero = 0.35f;
        [Tooltip("Cuando aún no hay tablero, el cursor sigue la imagen como un espejo: mano a la derecha = cursor a la derecha.")]
        public bool espejarImagen = true;
        [Tooltip("Invierte izquierda/derecha cuando el cursor sigue al tablero (actívalo si juegas del lado de las negras).")]
        public bool espejarTablero = false;
        public float suavizadoCursor = 14f;
        [Tooltip("Si la cámara no se puede encender, permitir el mouse solo en el menú.")]
        public bool mouseSiNoHayCamara = true;

        [Header("Inicio del juego")]
        public float cuentaRegresiva = 3f;
        public float duracionVuelo = 1.6f;

        enum Pantalla { Encendiendo, Principal, Tableros, Calibracion, PrepararMano, Vuelo, Juego, Pausa }
        Pantalla pantalla = Pantalla.Encendiendo;
        bool calibrarParaJugar, camaraValidada, sinCamara, pinzaProbada, agarrePrepPrev;
        bool sinTablero { get { return SinTablero; } set { SinTablero = value; } }
        /// <summary>Con tablero real manda solo la cámara (los cambios en el tablero): la mano virtual no mueve piezas.
        /// Sin tablero, o sin cámara, la mano es la que juega.</summary>
        bool ManoJuega { get { return sinTablero || sinCamara; } }
        float tCuenta, tListo, tPantalla;

        // cámara de fondo
        Camera cam;
        CamaraAjedrez orbita;
        float yaw360;
        Vector3 posIni, posFin;
        Quaternion rotIni, rotFin;
        float tVuelo;

        // cursor
        Vector2 cursor;
        bool hayCursor, usandoMouse, agarrePrev, mousePrev, clic;
        Vector2 mousePos; float tMouse = -99f;
        string hoverId, bloqueado;
        float tHover, tBloqueo, progresoCursor;
        bool hoverVisto;

        // dibujo
        Texture2D texRedondo, texCirculo, texDegradado;
        GUIStyle estiloRedondo, estiloRedondoChico;
        readonly Dictionary<string, Texture2D> miniaturas = new Dictionary<string, Texture2D>();
        float s;   // escala: alto de pantalla / 1080

        static readonly Color Oro = new Color(1f, 0.8f, 0.32f, 1f);
        static readonly Color Verde = new Color(0.35f, 0.9f, 0.5f, 1f);
        static readonly Color Rojo = new Color(1f, 0.4f, 0.35f, 1f);
        static readonly Color Ambar = new Color(1f, 0.72f, 0.25f, 1f);
        static readonly Color Panel = new Color(0.05f, 0.07f, 0.11f, 0.78f);

        // ------------------------------------------------------------------ inicio
        void Start()
        {
            cam = Camera.main;
            orbita = cam != null ? cam.GetComponent<CamaraAjedrez>() : null;
            if (orbita != null) orbita.enabled = false;
            if (arranque != null && arranque.Mano != null) arranque.Mano.Pausar(true);
            if (camara == null) camara = ConexionCamara.Instancia;
            if (cam != null) yaw360 = cam.transform.eulerAngles.y;
            CrearTexturas();
            Activo = true;
            Ir(Pantalla.Encendiendo);
        }

        void OnDestroy() { Activo = false; SinTablero = false; }

        void Ir(Pantalla p)
        {
            pantalla = p;
            tPantalla = Time.unscaledTime;
            tCuenta = 0f;
            tListo = 0f;
            pinzaProbada = false;
            agarrePrepPrev = true;   // la pinza tiene que hacerse estando ya en la pantalla
            Activo = p != Pantalla.Juego && p != Pantalla.Vuelo;
            if (camara == null) return;
            if (p == Pantalla.Calibracion) camara.Modo("calibrar");
            else if (p != Pantalla.Juego && p != Pantalla.Vuelo) camara.Modo("menu");
        }

        // ------------------------------------------------------------------ cada cuadro
        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            ActualizarCursor(dt);

            switch (pantalla)
            {
                case Pantalla.Encendiendo:
                    if (camara != null && camara.Conectada && camara.Estado.fps > 0f && Time.unscaledTime - tPantalla > 0.8f)
                        Ir(Pantalla.Principal);
                    break;

                case Pantalla.Calibracion:
                    bool listo = camara != null && camara.Conectada && camara.Estado.listo;
                    tListo = listo ? tListo + dt : 0f;
                    if (calibrarParaJugar)
                    {
                        tCuenta = listo ? tCuenta + dt : 0f;
                        if (tCuenta >= cuentaRegresiva) EmpezarJuego(true);
                    }
                    else if (listo) camaraValidada = true;
                    break;

                case Pantalla.PrepararMano:
                {
                    // sin tablero: basta con ver la mano y que haga una pinza (agarrar) una vez
                    var e = camara != null && camara.Conectada ? camara.Estado : null;
                    bool agarra = e != null && e.mano && e.agarre;
                    if (agarra && !agarrePrepPrev && Time.unscaledTime - tPantalla > 0.6f) pinzaProbada = true;
                    agarrePrepPrev = agarra;
                    if (e == null) pinzaProbada = false;
                    tCuenta = pinzaProbada ? tCuenta + dt : 0f;
                    if (tCuenta >= cuentaRegresiva) EmpezarJuego(true);
                    break;
                }

                case Pantalla.Juego:
                    if (EntradaUtil.Pulsada(TeclaJuego.Pausa)) Pausar();
                    break;
            }

            if (pantalla == Pantalla.Vuelo) Volar(dt);
            else if (pantalla != Pantalla.Juego && pantalla != Pantalla.Pausa) Orbitar(dt);
        }

        void Orbitar(float dt)
        {
            if (cam == null || arranque == null || arranque.Tablero == null) return;
            yaw360 += velocidadGiro * dt;
            Vector3 c = arranque.Tablero.transform.position;
            Vector3 pos = c + Quaternion.Euler(0f, yaw360, 0f) * new Vector3(0f, altura, -radio)
                          + Vector3.up * Mathf.Sin(Time.unscaledTime * 0.35f) * 0.6f;
            Quaternion rot = Quaternion.LookRotation(c + Vector3.up * 0.3f - pos);
            float k = 1f - Mathf.Exp(-dt * 1.8f);
            cam.orthographic = false;
            cam.transform.position = Vector3.Lerp(cam.transform.position, pos, k);
            cam.transform.rotation = Quaternion.Slerp(cam.transform.rotation, rot, k);
        }

        void Volar(float dt)
        {
            tVuelo += dt;
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(tVuelo / duracionVuelo));
            if (cam != null)
            {
                cam.transform.position = Vector3.Lerp(posIni, posFin, u) + Vector3.up * Mathf.Sin(u * Mathf.PI) * 2f;
                cam.transform.rotation = Quaternion.Slerp(rotIni, rotFin, u);
            }
            if (tVuelo >= duracionVuelo)
            {
                if (orbita != null) orbita.enabled = true;
                Ir(Pantalla.Juego);
            }
        }

        // ------------------------------------------------------------------ acciones
        void EmpezarJuego(bool nueva)
        {
            if (nueva && arranque.Tablero != null) arranque.Tablero.NuevaPartida();
            if (camara != null && !sinCamara) camara.Modo(sinTablero ? "libre" : "juego", nueva);
            if (arranque.Mano != null) arranque.Mano.Pausar(!ManoJuega);
            // vuelo desde el giro 360° hasta la vista de juego
            posIni = cam != null ? cam.transform.position : Vector3.zero;
            rotIni = cam != null ? cam.transform.rotation : Quaternion.identity;
            Vector3 c = arranque.Tablero != null ? arranque.Tablero.transform.position : Vector3.zero;
            float pitch = orbita != null ? orbita.pitch : 50f, yaw = orbita != null ? orbita.yaw : 0f, dist = orbita != null ? orbita.distancia : 11.5f;
            if (orbita != null && orbita.objetivo != null) c = orbita.objetivo.position;
            rotFin = Quaternion.Euler(pitch, yaw, 0f);
            posFin = c - rotFin * Vector3.forward * dist;
            tVuelo = 0f;
            pantalla = Pantalla.Vuelo;
            Activo = false;
        }

        void Pausar()
        {
            if (camara != null) camara.Modo("menu");
            if (arranque.Mano != null) arranque.Mano.Pausar(true);
            Ir(Pantalla.Pausa);
        }

        void Continuar()
        {
            if (camara != null && !sinCamara) camara.Modo(sinTablero ? "libre" : "juego", false);
            if (arranque.Mano != null) arranque.Mano.Pausar(!ManoJuega);
            Ir(Pantalla.Juego);
        }

        void AlMenuPrincipal()
        {
            if (orbita != null) orbita.enabled = false;
            if (arranque.Mano != null) arranque.Mano.Pausar(true);
            if (cam != null) yaw360 = cam.transform.eulerAngles.y;
            Ir(Pantalla.Principal);
        }

        static void Salir()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------------ cursor de la mano
        void ActualizarCursor(float dt)
        {
            float W = Screen.width, H = Screen.height;
            bool hayCamara = camara != null && camara.Conectada;
            bool camaraFunciona = hayCamara && string.IsNullOrEmpty(camara.Estado.error) && camara.Estado.fps > 0f;
            // el mouse funciona siempre en el menú: mientras se mueve o se hace clic manda sobre la mano
            if (EntradaUtil.HayMouse)
            {
                Vector2 mp = EntradaUtil.PosicionMouse;
                if ((mp - mousePos).sqrMagnitude > 4f || EntradaUtil.BotonIzquierdo) tMouse = Time.unscaledTime;
                mousePos = mp;
            }
            bool mouseActivo = EntradaUtil.HayMouse && pantalla != Pantalla.Juego && Time.unscaledTime - tMouse < 4f;
            usandoMouse = mouseActivo || (!camaraFunciona && mouseSiNoHayCamara && (sinCamara || Time.unscaledTime > 4f) && EntradaUtil.HayMouse);
            Vector2 objetivo = cursor;
            bool agarre = false;
            if (!usandoMouse && hayCamara && camara.Estado.mano)
            {
                var e = camara.Estado;
                if (e.cal && !sinTablero)
                {
                    float m = margenTablero;
                    float bx = espejarTablero ? 1f - e.bx : e.bx;
                    objetivo = new Vector2((bx + m) / (1f + 2f * m) * W, (1f - (e.by + m) / (1f + 2f * m)) * H);
                }
                else
                {
                    // la webcam ve a la persona "de frente": se invierte para que se sienta como un espejo
                    float ix = espejarImagen ? 1f - e.ix : e.ix;
                    objetivo = new Vector2((ix - 0.1f) / 0.8f * W, (e.iy - 0.1f) / 0.8f * H);
                }
                objetivo.x = Mathf.Clamp(objetivo.x, 0f, W);
                objetivo.y = Mathf.Clamp(objetivo.y, 0f, H);
                agarre = e.agarre;
                if (!hayCursor) cursor = objetivo;
                hayCursor = true;
            }
            else if (usandoMouse)
            {
                Vector2 p = EntradaUtil.PosicionMouse;
                objetivo = new Vector2(p.x, H - p.y);
                cursor = objetivo;
                agarre = EntradaUtil.BotonIzquierdo;
                hayCursor = true;
            }
            else hayCursor = false;

            if (hayCursor && !usandoMouse) cursor = Vector2.Lerp(cursor, objetivo, 1f - Mathf.Exp(-dt * suavizadoCursor));
            clic = clic || (agarre && !agarrePrev);   // pinza (o clic) recién hecha
            agarrePrev = agarre;
        }

        /// <summary>En el juego el cursor solo aparece cuando la mano sale del tablero.</summary>
        bool CursorEnJuego()
        {
            if (usandoMouse) return true;
            if (camara == null || !camara.Conectada || !camara.Estado.mano) return false;
            if (sinTablero) return cursor.x > Screen.width * 0.86f;   // mano al borde derecho de la pantalla
            var e = camara.Estado;
            return !e.encima && (e.bx < -0.06f || e.bx > 1.06f || e.by < -0.06f || e.by > 1.06f);
        }

        // ------------------------------------------------------------------ dibujo
        void OnGUI()
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            GUI.depth = -100;
            s = Screen.height / 1080f;
            hoverVisto = false;
            progresoCursor = 0f;
            float W = Screen.width, H = Screen.height;

            switch (pantalla)
            {
                case Pantalla.Encendiendo: DibujarEncendiendo(W, H); break;
                case Pantalla.Principal: DibujarPrincipal(W, H); break;
                case Pantalla.Tableros: DibujarTableros(W, H); break;
                case Pantalla.Calibracion: DibujarCalibracion(W, H); break;
                case Pantalla.PrepararMano: DibujarPrepararMano(W, H); break;
                case Pantalla.Vuelo: DibujarVuelo(W, H); break;
                case Pantalla.Juego: DibujarJuego(W, H); break;
                case Pantalla.Pausa: DibujarPausa(W, H); break;
            }

            if (!hoverVisto) { hoverId = null; bloqueado = null; }
            bool mostrarCursor = hayCursor && (pantalla != Pantalla.Juego || CursorEnJuego()) && pantalla != Pantalla.Vuelo;
            if (mostrarCursor) DibujarCursor();
            clic = false;
        }

        void Fondo(float W, float H, float fuerza)
        {
            Color c = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, fuerza);
            GUI.DrawTexture(new Rect(0, 0, W, H), texDegradado);
            GUI.color = c;
        }

        void Titulo(float W, string titulo, string subtitulo)
        {
            Texto(new Rect(0, 48 * s, W, 110 * s), titulo, 78, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            if (!string.IsNullOrEmpty(subtitulo))
                Texto(new Rect(0, 150 * s, W, 44 * s), subtitulo, 28, new Color(1f, 1f, 1f, 0.8f), TextAnchor.MiddleCenter, FontStyle.Normal);
        }

        void Pista(float W, float H, string t)
        {
            Texto(new Rect(0, H - 64 * s, W, 44 * s), t, 26, new Color(1f, 1f, 1f, 0.85f), TextAnchor.MiddleCenter, FontStyle.Italic);
        }

        string PistaMano()
        {
            if (usandoMouse) return "Haz clic en un botón con el mouse  ·  o muestra la mano a la cámara";
            if (camara == null || !camara.Conectada) return "Esperando la cámara...";
            if (!camara.Estado.mano) return sinTablero ? "Muestra tu mano a la cámara" : "Muestra tu mano a la cámara, sobre el tablero";
            return "Mueve la mano para mover el cursor  ·  junta pulgar e índice, o deja la mano quieta, para elegir";
        }

        // ---- pantallas ---------------------------------------------------
        void DibujarEncendiendo(float W, float H)
        {
            Fondo(W, H, 0.55f);
            Titulo(W, "AJEDREZ SOBRE ARENA", "Procesamiento Digital de Imágenes  ·  Ingeniería Multimedia");
            Rect r = new Rect(W / 2 - 520 * s, H * 0.36f, 1040 * s, 330 * s);
            Caja(r, Panel);
            string problema = camara != null ? camara.Problema : "No hay conexión con la cámara";
            bool errorCamara = camara != null && camara.Conectada && !string.IsNullOrEmpty(camara.Estado.error);
            if (errorCamara) problema = camara.Estado.error + ". Revisa que la cámara esté conectada y que ninguna otra aplicación la use.";
            if (string.IsNullOrEmpty(problema))
            {
                Texto(new Rect(r.x, r.y + 40 * s, r.width, 60 * s), "Encendiendo la cámara...", 44, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
                Girador(new Vector2(W / 2, r.y + 170 * s), 34 * s);
                string aviso = camara != null && camara.Estado != null ? camara.Estado.aviso : "";
                Texto(new Rect(r.x, r.y + 230 * s, r.width, 40 * s), string.IsNullOrEmpty(aviso) ? "Cargando la red neuronal y el seguimiento de la mano" : aviso,
                      26, new Color(1, 1, 1, 0.8f), TextAnchor.MiddleCenter, FontStyle.Normal);
                Texto(new Rect(r.x, r.y + 270 * s, r.width, 40 * s), "La primera vez tarda unos segundos", 22, new Color(1, 1, 1, 0.55f), TextAnchor.MiddleCenter, FontStyle.Italic);
                if (camara != null && camara.SegundosEncendiendo > 45f && !camara.Conectada)
                    Texto(new Rect(r.x, r.y + r.height + 10 * s, r.width, 40 * s), "Está tardando: revisa Detector/ajedrez_vivo.log", 24, Ambar, TextAnchor.MiddleCenter, FontStyle.Normal);
                return;
            }
            Texto(new Rect(r.x + 40 * s, r.y + 30 * s, r.width - 80 * s, 60 * s), "No se pudo encender la cámara", 40, Rojo, TextAnchor.MiddleCenter, FontStyle.Bold);
            Texto(new Rect(r.x + 40 * s, r.y + 100 * s, r.width - 80 * s, 110 * s), problema, 26, Color.white, TextAnchor.MiddleCenter, FontStyle.Normal, true);
            float y = r.y + r.height + 30 * s, bw = 330 * s, bh = 96 * s, sep = 25 * s;
            float x = W / 2 - (bw * 3 + sep * 2) / 2;
            if (camara != null && camara.FaltaInstalar)
            {
                if (Boton(new Rect(x, y, bw, bh), "Instalar librerías", "instalar")) camara.AbrirInstalador();
            }
            else if (errorCamara)
            {
                if (Boton(new Rect(x, y, bw, bh), "Otra cámara", "otracam0")) camara.CambiarCamara();
            }
            else if (Boton(new Rect(x, y, bw, bh), "Ver ayuda", "ayuda")) Application.OpenURL("file://" + System.IO.Path.Combine(camara.CarpetaDetector, "LEEME_CAMARA.txt"));
            if (Boton(new Rect(x + bw + sep, y, bw, bh), "Reintentar", "reintentar") && camara != null) camara.Reintentar();
            if (Boton(new Rect(x + 2 * (bw + sep), y, bw, bh), "Jugar sin cámara", "sincamara"))
            {
                sinCamara = true;
                sinTablero = false;
                calibrarParaJugar = true;
                Ir(Pantalla.Tableros);
            }
            Pista(W, H, PistaMano());
        }

        void DibujarPrincipal(float W, float H)
        {
            Fondo(W, H, 0.35f);
            Titulo(W, "AJEDREZ SOBRE ARENA", "Procesamiento Digital de Imágenes  ·  Ingeniería Multimedia  ·  UAO");
            float bw = 680 * s, bh = 108 * s, x = W / 2 - bw / 2, y = H * 0.27f, sep = 24 * s;
            bool con = camara != null && camara.Conectada;
            if (Boton(new Rect(x, y, bw, bh), "Jugar con tablero", "jugar", null, true, true, 48))
            {
                calibrarParaJugar = true;
                sinCamara = !con;
                sinTablero = false;
                Ir(Pantalla.Tableros);
            }
            if (Boton(new Rect(x, y + (bh + sep), bw, bh), "Jugar solo con la mano", "jugarmano", null, true, con, 44))
            {
                calibrarParaJugar = true;
                sinCamara = false;
                sinTablero = true;
                Ir(Pantalla.Tableros);
            }
            if (Boton(new Rect(x, y + 2 * (bh + sep), bw, bh), "Calibrar cámara", "calibrar", null, false, con, 40))
            {
                calibrarParaJugar = false;
                sinTablero = false;
                Ir(Pantalla.Calibracion);
            }
            if (Boton(new Rect(x, y + 3 * (bh + sep), bw, bh), "Salir", "salir", null, false, true, 40)) Salir();
            TarjetaCamara(new Rect(40 * s, H - 330 * s, 600 * s, 250 * s));
            Pista(W, H, PistaMano());
        }

        void TarjetaCamara(Rect r)
        {
            Caja(r, Panel);
            Rect v = new Rect(r.x + 18 * s, r.y + 18 * s, 380 * s * 0.75f, 214 * s * 0.75f);
            if (camara != null && camara.HayVideo) GUI.DrawTexture(v, camara.Video, ScaleMode.ScaleToFit);
            else { Caja(v, new Color(0, 0, 0, 0.6f)); Texto(v, "sin imagen", 20, new Color(1, 1, 1, 0.5f), TextAnchor.MiddleCenter, FontStyle.Italic); }
            float x = v.xMax + 20 * s, y = r.y + 22 * s, w = r.xMax - x - 10 * s;
            var e = camara != null ? camara.Estado : null;
            bool con = camara != null && camara.Conectada;
            Linea(x, ref y, w, con, con ? "Cámara " + Mathf.RoundToInt(e.fps) + " fps" : "Cámara apagada");
            Linea(x, ref y, w, con && e.cal, con && e.cal ? "Tablero visto" : "Tablero no visto");
            Linea(x, ref y, w, con && e.mano, con && e.mano ? "Mano detectada" : "Sin mano");
            Linea(x, ref y, w, camaraValidada, camaraValidada ? "Calibración lista" : "Sin calibrar", !camaraValidada);
            Texto(new Rect(r.x + 18 * s, r.yMax - 52 * s, r.width - 36 * s, 40 * s), "Ajedrez Interactivo sobre Arena", 20, new Color(1, 1, 1, 0.5f), TextAnchor.MiddleLeft, FontStyle.Italic);
        }

        void Linea(float x, ref float y, float w, bool ok, string t, bool advertencia = false)
        {
            Punto(new Vector2(x + 10 * s, y + 18 * s), 9 * s, ok ? Verde : (advertencia ? Ambar : Rojo));
            Texto(new Rect(x + 30 * s, y, w - 30 * s, 36 * s), t, 24, Color.white, TextAnchor.MiddleLeft, FontStyle.Normal);
            y += 42 * s;
        }

        void DibujarTableros(float W, float H)
        {
            Fondo(W, H, 0.45f);
            var esc = arranque != null ? arranque.Escenarios : null;
            Titulo(W, "Elige el tablero", esc != null && esc.Datos != null ? esc.Datos.lugar : "");
            if (esc == null || esc.escenarios.Count == 0) return;
            int n = esc.escenarios.Count, cols = 5;
            float cw = 300 * s, ch = cw * 9f / 16f + 58 * s, gap = 26 * s;
            float x0 = W / 2 - (cols * cw + (cols - 1) * gap) / 2, y0 = 225 * s;
            for (int i = 0; i < n; i++)
            {
                var d = esc.escenarios[i];
                Rect r = new Rect(x0 + (i % cols) * (cw + gap), y0 + (i / cols) * (ch + gap), cw, ch);
                if (Tarjeta(r, d.nombre, Bonito(d.nombre), Miniatura(d.nombre), esc.Actual == i) && esc.Actual != i) esc.Cambiar(i);
            }
            float by = y0 + 2 * (ch + gap) + 20 * s, bw = 420 * s, bh = 104 * s;
            if (Boton(new Rect(x0, by, bw, bh), "Volver", "volver")) Ir(sinCamara && (camara == null || !camara.Conectada) ? Pantalla.Encendiendo : Pantalla.Principal);
            string siguiente = sinCamara ? "Jugar  →" : (sinTablero ? "Preparar la mano  →" : "Calibrar y jugar  →");
            if (Boton(new Rect(x0 + cols * cw + (cols - 1) * gap - bw * 1.3f, by, bw * 1.3f, bh), siguiente, "seguir", null, true))
            {
                if (sinCamara) EmpezarJuego(true);
                else if (sinTablero) Ir(Pantalla.PrepararMano);
                else Ir(Pantalla.Calibracion);
            }
            Pista(W, H, PistaMano());
        }

        void DibujarCalibracion(float W, float H)
        {
            Fondo(W, H, 0.6f);
            var esc = arranque != null ? arranque.Escenarios : null;
            Titulo(W, "Calibrar la cámara", calibrarParaJugar && esc != null && esc.Datos != null ? "Para jugar en " + Bonito(esc.Datos.nombre) : "Pon el tablero a la vista y retira las manos");
            var e = camara != null ? camara.Estado : new EstadoCamara();
            bool con = camara != null && camara.Conectada;

            // ---- video
            Rect v = new Rect(60 * s, 215 * s, 1060 * s, 596 * s);
            Caja(new Rect(v.x - 8 * s, v.y - 8 * s, v.width + 16 * s, v.height + 16 * s), Panel);
            if (camara != null && camara.HayVideo) GUI.DrawTexture(v, camara.Video, ScaleMode.ScaleToFit);
            else Texto(v, "Sin imagen de la cámara", 30, new Color(1, 1, 1, 0.6f), TextAnchor.MiddleCenter, FontStyle.Italic);

            // ---- lista de chequeo
            Rect l = new Rect(1150 * s, 207 * s, W - 1190 * s, 612 * s);
            Caja(l, Panel);
            float x = l.x + 26 * s, y = l.y + 22 * s, w = l.width - 52 * s;
            Chequeo(x, ref y, w, con && string.IsNullOrEmpty(e.error), "Cámara encendida", con ? (string.IsNullOrEmpty(e.error) ? Mathf.RoundToInt(e.fps) + " cuadros por segundo" : e.error) : "Esperando a la cámara...");
            Chequeo(x, ref y, w, con && e.cal && e.calidad >= 0.35f, "Tablero detectado", con && e.cal ? "Calidad de la cuadrícula " + e.calidad.ToString("0.00") : "Que se vea el tablero completo, desde arriba");
            Chequeo(x, ref y, w, con && e.quieto && !e.encima, "Sin manos y quieto", e.encima ? "Retira la mano del tablero" : (e.quieto ? "Todo quieto" : "Esperando que no se mueva nada..."));
            Chequeo(x, ref y, w, con && e.leido && e.dudosas == 0, "Red neuronal", e.leido ? e.tiposOk + "/64 casillas reconocidas · " + e.dudosas + " dudosas" : "-", true);

            // ---- visto bueno
            Rect b = new Rect(60 * s, 840 * s, W - 120 * s, 96 * s);
            bool listo = con && e.listo;
            if (listo)
            {
                Caja(b, new Color(0.12f, 0.55f, 0.3f, 0.92f));
                string t = calibrarParaJugar
                    ? "¡Cámara lista!   El juego empieza en " + Mathf.Max(1, Mathf.CeilToInt(cuentaRegresiva - tCuenta))
                    : "¡Cámara lista! Ya puedes jugar";
                Texto(b, t, 46, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            }
            else
            {
                Caja(b, new Color(0.35f, 0.25f, 0.05f, 0.85f));
                Texto(b, Siguiente(e, con), 32, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            }

            float bw = 300 * s, bh = 84 * s, by = H - 120 * s, sep = 22 * s, bx = W / 2 - (bw * 4 + sep * 3) / 2;
            if (Boton(new Rect(bx, by, bw, bh), "Volver", "volver", null, false, true, 32)) Ir(calibrarParaJugar ? Pantalla.Tableros : Pantalla.Principal);
            if (Boton(new Rect(bx + (bw + sep), by, bw, bh), "Recalibrar", "recal", null, false, con, 32)) camara.Recalibrar();
            if (Boton(new Rect(bx + 2 * (bw + sep), by, bw, bh), "Girar 90°", "girar", null, false, con && e.cal, 32)) camara.Girar();
            if (!calibrarParaJugar && camaraValidada)
            {
                if (Boton(new Rect(bx + 3 * (bw + sep), by, bw, bh), "Listo", "listo", null, true, true, 32)) Ir(Pantalla.Principal);
            }
            else if (Boton(new Rect(bx + 3 * (bw + sep), by, bw, bh), "Otra cámara", "otracam", null, false, con, 32)) camara.CambiarCamara();
        }

        static string[] Primeros(string[] a, int n) { var r = new string[n]; System.Array.Copy(a, r, n); return r; }
        static string Primera(string t) { return t.Length > 0 ? char.ToUpper(t[0]) + t.Substring(1) : t; }

        static string Siguiente(EstadoCamara e, bool con)
        {
            if (!con) return "Esperando a la cámara...";
            if (!e.cal) return "Muestra el tablero completo a la cámara (sin manos encima)";
            if (e.encima) return "Retira las manos del tablero";
            if (!e.quieto) return "Espera a que todo quede quieto";
            if (!e.leido) return "La red está leyendo el tablero...";
            return "Comprobando... (no muevas nada)";
        }

        void Chequeo(float x, ref float y, float w, bool ok, string titulo, string detalle, bool soloAviso = false)
        {
            Punto(new Vector2(x + 16 * s, y + 26 * s), 15 * s, ok ? Verde : (soloAviso ? Ambar : Rojo));
            Texto(new Rect(x + 46 * s, y, w - 46 * s, 40 * s), titulo, 30, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            Texto(new Rect(x + 46 * s, y + 38 * s, w - 46 * s, 36 * s), detalle, 22, new Color(1, 1, 1, 0.75f), TextAnchor.MiddleLeft, FontStyle.Normal);
            y += 98 * s;
        }

        void DibujarPrepararMano(float W, float H)
        {
            Fondo(W, H, 0.6f);
            var esc = arranque != null ? arranque.Escenarios : null;
            Titulo(W, "Jugar solo con la mano", "Sin tablero físico" + (esc != null && esc.Datos != null ? "  ·  " + Bonito(esc.Datos.nombre) : ""));
            var e = camara != null ? camara.Estado : new EstadoCamara();
            bool con = camara != null && camara.Conectada;

            // ---- video en espejo, con el recuadro que cubre toda la pantalla
            Rect area = new Rect(60 * s, 215 * s, 1060 * s, 596 * s);
            Caja(new Rect(area.x - 8 * s, area.y - 8 * s, area.width + 16 * s, area.height + 16 * s), Panel);
            if (camara != null && camara.HayVideo)
            {
                var tex = camara.Video;
                float k = Mathf.Min(area.width / tex.width, area.height / tex.height);
                Rect v = new Rect(0, 0, tex.width * k, tex.height * k);
                v.center = area.center;
                GUI.DrawTextureWithTexCoords(v, tex, new Rect(1f, 0f, -1f, 1f));   // espejo
                const float z = 0.1f;                                              // igual que --zona-mano
                Rect zona = new Rect(v.x + v.width * z, v.y + v.height * z, v.width * (1 - 2 * z), v.height * (1 - 2 * z));
                Borde(zona, new Color(Oro.r, Oro.g, Oro.b, 0.8f));
                Texto(new Rect(zona.x + 12 * s, zona.y + 6 * s, zona.width, 34 * s), "zona de juego = toda la pantalla", 20, Oro, TextAnchor.UpperLeft, FontStyle.Italic);
                if (e.mano)
                {
                    Vector2 m = new Vector2(v.x + (1f - e.ix) * v.width, v.y + e.iy * v.height);
                    Punto(m, (e.agarre ? 16 : 24) * s, e.agarre ? Verde : Oro);
                }
            }
            else Texto(area, "Sin imagen de la cámara", 30, new Color(1, 1, 1, 0.6f), TextAnchor.MiddleCenter, FontStyle.Italic);

            // ---- lista de chequeo e instrucciones
            Rect l = new Rect(1150 * s, 207 * s, W - 1190 * s, 612 * s);
            Caja(l, Panel);
            float x = l.x + 26 * s, y = l.y + 22 * s, w = l.width - 52 * s;
            Chequeo(x, ref y, w, con && string.IsNullOrEmpty(e.error), "Cámara encendida", con ? (string.IsNullOrEmpty(e.error) ? Mathf.RoundToInt(e.fps) + " cuadros por segundo" : e.error) : "Esperando a la cámara...");
            Chequeo(x, ref y, w, con && e.mano, "Mano detectada", e.mano ? "Mueve la mano: el punto la sigue" : "Levanta la mano frente a la cámara, con la palma a la vista");
            Chequeo(x, ref y, w, pinzaProbada, "Prueba la pinza", pinzaProbada ? "¡Bien!" : "Junta pulgar e índice una vez");
            float ty = y + 6 * s;
            Texto(new Rect(x, ty, w, 40 * s), "Cómo se juega", 28, Oro, TextAnchor.MiddleLeft, FontStyle.Bold);
            Texto(new Rect(x, ty + 44 * s, w, 220 * s),
                  "1. Apunta a una pieza con la mano.\n2. Junta pulgar e índice para agarrarla.\n3. Llévala a otra casilla y separa los dedos.\n" +
                  "Rápido = fuego · muy rápido = rayo · quieta 2 s = supernova.\nMano al borde derecho = Menú.",
                  22, new Color(1, 1, 1, 0.85f), TextAnchor.UpperLeft, FontStyle.Normal, true);

            // ---- visto bueno
            Rect b = new Rect(60 * s, 840 * s, W - 120 * s, 96 * s);
            if (pinzaProbada)
            {
                Caja(b, new Color(0.12f, 0.55f, 0.3f, 0.92f));
                Texto(b, "¡Listo!   El juego empieza en " + Mathf.Max(1, Mathf.CeilToInt(cuentaRegresiva - tCuenta)), 46, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            }
            else
            {
                Caja(b, new Color(0.35f, 0.25f, 0.05f, 0.85f));
                Texto(b, !con ? "Esperando a la cámara..." : (!e.mano ? "Muestra tu mano a la cámara" : "Junta pulgar e índice para empezar"),
                      32, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            }

            float bw = 300 * s, bh = 84 * s, by = H - 120 * s, sep = 22 * s, bx = W / 2 - (bw * 2 + sep) / 2;
            if (Boton(new Rect(bx, by, bw, bh), "Volver", "volver", null, false, true, 32)) Ir(Pantalla.Tableros);
            if (Boton(new Rect(bx + bw + sep, by, bw, bh), "Otra cámara", "otracam", null, false, con, 32)) camara.CambiarCamara();
        }

        void DibujarVuelo(float W, float H)
        {
            float a = Mathf.Clamp01(1f - tVuelo / duracionVuelo);
            Color c = GUI.color;
            GUI.color = new Color(1, 1, 1, a);
            Texto(new Rect(0, H * 0.38f, W, 160 * s), "¡A jugar!", 110, Oro, TextAnchor.MiddleCenter, FontStyle.Bold);
            GUI.color = c;
        }

        void DibujarJuego(float W, float H)
        {
            if (!CursorEnJuego()) return;
            // botón de pausa: saca la mano del tablero hacia la derecha
            Rect r = new Rect(W - 230 * s, H * 0.5f - 60 * s, 200 * s, 120 * s);
            if (Boton(r, "Menú", "pausa", null, false, true, 36)) Pausar();
        }

        void DibujarPausa(float W, float H)
        {
            Fondo(W, H, 0.7f);
            Titulo(W, "Pausa", arranque.Escenarios != null && arranque.Escenarios.Datos != null ? arranque.Escenarios.Datos.lugar : "");
            float bw = 600 * s, bh = 104 * s, x = W / 2 - bw / 2, y = H * 0.27f, sep = 26 * s;
            if (Boton(new Rect(x, y, bw, bh), "Continuar", "continuar", null, true, true, 44)) Continuar();
            if (Boton(new Rect(x, y + (bh + sep), bw, bh), "Siguiente escenario", "escenario", null, false, true, 40) && arranque.Escenarios != null)
                arranque.Escenarios.Cambiar((arranque.Escenarios.Actual + 1) % Mathf.Max(1, arranque.Escenarios.escenarios.Count));
            if (Boton(new Rect(x, y + 2 * (bh + sep), bw, bh), "Nueva partida", "nueva", null, false, true, 40))
            {
                calibrarParaJugar = true;
                AlMenuPrincipal();
                Ir(Pantalla.Tableros);
            }
            if (Boton(new Rect(x, y + 3 * (bh + sep), bw, bh), "Menú principal", "principal", null, false, true, 40)) AlMenuPrincipal();
            if (Boton(new Rect(x, y + 4 * (bh + sep), bw, bh), "Salir", "salir", null, false, true, 40)) Salir();
            Pista(W, H, PistaMano());
        }

        // ---- botones que se eligen con la mano --------------------------
        bool Elegir(Rect r, string id, bool habilitado, out bool hover, out float prog)
        {
            hover = habilitado && hayCursor && r.Contains(cursor);
            prog = 0f;
            if (!hover) return false;
            hoverVisto = true;
            if (hoverId != id)
            {
                hoverId = id;
                tHover = Time.unscaledTime;
                Sonar(false);
            }
            if (id == bloqueado || Time.unscaledTime < tBloqueo) return false;
            prog = usandoMouse ? 0f : Mathf.Clamp01((Time.unscaledTime - tHover) / tiempoMantener);
            progresoCursor = Mathf.Max(progresoCursor, prog);
            if (prog >= 1f || clic)
            {
                bloqueado = id;
                tBloqueo = Time.unscaledTime + 0.5f;
                clic = false;
                Sonar(true);
                return true;
            }
            return false;
        }

        bool Boton(Rect r, string texto, string id, Texture2D imagen = null, bool principal = false, bool habilitado = true, int letra = 38)
        {
            bool hover; float prog;
            bool elegido = Elegir(r, id, habilitado, out hover, out prog);
            float crece = hover ? 1.04f : 1f;
            Rect d = Escalar(r, crece);
            Color fondo = !habilitado ? new Color(0.2f, 0.2f, 0.22f, 0.6f)
                        : hover ? new Color(Oro.r, Oro.g, Oro.b, 0.96f)
                        : principal ? new Color(0.55f, 0.4f, 0.12f, 0.88f) : Panel;
            if (principal && !hover) Caja(new Rect(d.x - 3 * s, d.y - 3 * s, d.width + 6 * s, d.height + 6 * s), Oro);
            Caja(d, fondo);
            Color letraColor = !habilitado ? new Color(1, 1, 1, 0.35f) : hover ? new Color(0.1f, 0.08f, 0.04f) : Color.white;
            Texto(d, texto, letra, letraColor, TextAnchor.MiddleCenter, FontStyle.Bold, false, !hover);
            if (prog > 0f) Caja(new Rect(d.x + 12 * s, d.yMax - 14 * s, (d.width - 24 * s) * prog, 7 * s), new Color(0.1f, 0.08f, 0.04f, 0.9f));
            return elegido;
        }

        bool Tarjeta(Rect r, string id, string nombre, Texture2D imagen, bool elegida)
        {
            bool hover; float prog;
            bool elegido = Elegir(r, "tab_" + id, true, out hover, out prog);
            Rect d = Escalar(r, hover ? 1.05f : 1f);
            if (elegida && !hover) Caja(new Rect(d.x - 3 * s, d.y - 3 * s, d.width + 6 * s, d.height + 6 * s), Oro);
            Caja(d, hover ? new Color(Oro.r, Oro.g, Oro.b, 0.95f) : (elegida ? new Color(0.55f, 0.4f, 0.12f, 0.92f) : Panel));
            Rect img = new Rect(d.x + 8 * s, d.y + 8 * s, d.width - 16 * s, (d.width - 16 * s) * 9f / 16f);
            if (imagen != null) GUI.DrawTexture(img, imagen, ScaleMode.ScaleAndCrop);
            else Caja(img, new Color(0.15f, 0.15f, 0.18f, 1f));
            Texto(new Rect(d.x, img.yMax, d.width, d.yMax - img.yMax - 4 * s), nombre, 26, hover ? new Color(0.1f, 0.08f, 0.04f) : Color.white,
                  TextAnchor.MiddleCenter, FontStyle.Bold, false, !hover);
            if (prog > 0f) Caja(new Rect(d.x + 10 * s, d.yMax - 10 * s, (d.width - 20 * s) * prog, 6 * s), new Color(0.1f, 0.08f, 0.04f, 0.9f));
            return elegido;
        }

        void DibujarCursor()
        {
            float r = 30 * s;
            Color c = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(new Rect(cursor.x - r - 3 * s, cursor.y - r - 3 * s + 4 * s, 2 * r + 6 * s, 2 * r + 6 * s), texCirculo);
            bool pinza = camara != null && camara.Conectada && camara.Estado.agarre;
            GUI.color = pinza ? Oro : new Color(1f, 1f, 1f, 0.92f);
            GUI.DrawTexture(new Rect(cursor.x - r, cursor.y - r, 2 * r, 2 * r), texCirculo);
            GUI.color = pinza ? new Color(0.4f, 0.25f, 0f, 1f) : new Color(0.15f, 0.55f, 1f, 1f);
            float ri = r * 0.42f;
            GUI.DrawTexture(new Rect(cursor.x - ri, cursor.y - ri, 2 * ri, 2 * ri), texCirculo);
            GUI.color = c;
            if (progresoCursor > 0f)
            {
                int n = 28, hasta = Mathf.CeilToInt(n * progresoCursor);
                for (int i = 0; i < hasta; i++)
                {
                    float a = -Mathf.PI / 2 + i * Mathf.PI * 2f / n;
                    Punto(cursor + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (r + 12 * s), 5 * s, Oro);
                }
            }
        }

        // ---- utilidades de dibujo ---------------------------------------
        Rect Escalar(Rect r, float k)
        {
            return k == 1f ? r : new Rect(r.center.x - r.width * k / 2, r.center.y - r.height * k / 2, r.width * k, r.height * k);
        }

        void Caja(Rect r, Color c)
        {
            Color a = GUI.color;
            GUI.color = c;
            float m = Mathf.Min(r.width, r.height);
            if (m >= 48f) GUI.Box(r, GUIContent.none, estiloRedondo);
            else if (m >= 20f) GUI.Box(r, GUIContent.none, estiloRedondoChico);
            else GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = a;
        }

        void Borde(Rect r, Color c)
        {
            float g = 4 * s;
            Caja(new Rect(r.x, r.y, r.width, g), c);
            Caja(new Rect(r.x, r.yMax - g, r.width, g), c);
            Caja(new Rect(r.x, r.y, g, r.height), c);
            Caja(new Rect(r.xMax - g, r.y, g, r.height), c);
        }

        void Punto(Vector2 centro, float radio, Color c)
        {
            Color a = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(centro.x - radio, centro.y - radio, 2 * radio, 2 * radio), texCirculo);
            GUI.color = a;
        }

        void Girador(Vector2 centro, float radio)
        {
            int n = 12;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n + Time.unscaledTime * 4f;
                float alfa = (float)((i + (int)(Time.unscaledTime * 8f)) % n) / n;
                Punto(centro + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radio, 6 * s, new Color(Oro.r, Oro.g, Oro.b, 0.25f + 0.75f * alfa));
            }
        }

        void Texto(Rect r, string t, int tam, Color c, TextAnchor alin, FontStyle estilo, bool ajustar = false, bool sombra = true)
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(10, Mathf.RoundToInt(tam * s)), alignment = alin, fontStyle = estilo, wordWrap = ajustar, richText = false };
            Color a = GUI.color;
            if (sombra)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.65f * c.a * a.a);
                st.normal.textColor = Color.white;
                GUI.Label(new Rect(r.x + 2 * s, r.y + 2 * s, r.width, r.height), t, st);
            }
            GUI.color = new Color(a.r, a.g, a.b, a.a);
            st.normal.textColor = c;
            GUI.Label(r, t, st);
            GUI.color = a;
        }

        void Sonar(bool elegir)
        {
            var fx = EfectosAjedrez.Instancia;
            if (fx == null) return;
            fx.Sonar(elegir ? fx.sonidoSoltar : fx.sonidoAgarrar, elegir ? 0.5f : 0.18f);
        }

        Texture2D Miniatura(string nombre)
        {
            Texture2D t;
            if (!miniaturas.TryGetValue(nombre, out t))
            {
                t = Resources.Load<Texture2D>("Miniaturas/escenario_" + nombre);
                miniaturas[nombre] = t;
            }
            return t;
        }

        static string Bonito(string nombre)
        {
            switch (nombre)
            {
                case "MachuPicchu": return "Machu Picchu";
                case "MuseoDelOro": return "Museo del Oro";
                case "HistoriaNatural": return "Historia Natural";
                case "ArteModerno": return "Arte Moderno";
                default: return nombre;
            }
        }

        static GUIStyle EstiloRedondo(int rad, out Texture2D tex)
        {
            int tam = 2 * rad + 4;
            tex = new Texture2D(tam, tam, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < tam; y++)
                for (int x = 0; x < tam; x++)
                {
                    float dx = Mathf.Max(rad - x - 0.5f, 0f, x + 0.5f - (tam - rad));
                    float dy = Mathf.Max(rad - y - 0.5f, 0f, y + 0.5f - (tam - rad));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(rad - d + 0.5f)));
                }
            tex.Apply();
            var st = new GUIStyle { border = new RectOffset(rad, rad, rad, rad) };
            st.normal.background = tex;
            return st;
        }

        void CrearTexturas()
        {
            Texture2D chico;
            estiloRedondo = EstiloRedondo(22, out texRedondo);
            estiloRedondoChico = EstiloRedondo(9, out chico);

            int tc = 128;
            texCirculo = new Texture2D(tc, tc, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < tc; y++)
                for (int x = 0; x < tc; x++)
                {
                    float d = Mathf.Sqrt((x - 63.5f) * (x - 63.5f) + (y - 63.5f) * (y - 63.5f));
                    texCirculo.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(62f - d + 0.5f)));
                }
            texCirculo.Apply();

            texDegradado = new Texture2D(1, 256, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < 256; y++)
            {
                float t = y / 255f;                       // 0 abajo, 1 arriba
                float a = 0.55f + 0.45f * Mathf.Pow(Mathf.Abs(t - 0.5f) * 2f, 1.6f);
                texDegradado.SetPixel(0, y, new Color(1f, 1f, 1f, a));
            }
            texDegradado.Apply();
        }
    }
}
