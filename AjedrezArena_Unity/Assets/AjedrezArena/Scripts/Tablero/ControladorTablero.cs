// =====================================================================
//  ControladorTablero.cs  -  Une la lógica (PartidaAjedrez) con lo visual:
//  crea el tablero y las piezas, convierte casillas <-> mundo, valida y
//  ejecuta jugadas, captura con efectos, enroque, coronación y la "onda
//  de choque" que hace saltar las piezas cercanas (golpe estilo Mario).
// =====================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AjedrezArena
{
    public class ControladorTablero : MonoBehaviour
    {
        [Header("Modelos (FBX generados en Blender). Si faltan se usan primitivas.")]
        public GameObject prefabTablero;
        public GameObject prefabPeon;
        public GameObject prefabTorre;
        public GameObject prefabCaballo;
        public GameObject prefabAlfil;
        public GameObject prefabDama;
        public GameObject prefabRey;

        [Header("Materiales (opcionales)")]
        public Material materialBlancas;
        public Material materialNegras;

        [Header("Ajustes")]
        [Tooltip("Tamaño de una casilla en unidades de Unity (los modelos están hechos para 1).")]
        public float tamCasilla = 1f;
        [Tooltip("Si los caballos miran hacia atrás, pon 180.")]
        public float rotacionCaballo = 0f;
        public bool mostrarAyudaVisual = true;
        [Tooltip("FEN inicial (déjalo vacío para la posición normal).")]
        public string fenInicial = "";

        public PartidaAjedrez Partida { get; private set; }
        public TemaPiezas TemaActual { get; private set; }
        Material acentoBlancas, acentoNegras;
        readonly List<Material> materialesTema = new List<Material>();
        public event Action<ResultadoJugada, Impulso> JugadaRealizada;
        public event Action PartidaReiniciada;

        readonly Dictionary<Casilla, PiezaVisual> piezas = new Dictionary<Casilla, PiezaVisual>();
        readonly List<GameObject> marcadores = new List<GameObject>();
        Transform raizPiezas, raizMarcadores, modeloTablero;
        GameObject marcadorJaque, marcadorCursor;
        Material matMarcador;
        float sacudidaTablero, velSacudida;
        bool inicializado;
        string ultimoMensaje = "";
        float tiempoMensaje;

        public static readonly Color ColorMover = new Color(0.35f, 1f, 0.55f, 0.85f);
        public static readonly Color ColorCapturar = new Color(1f, 0.3f, 0.25f, 0.95f);
        public static readonly Color ColorEspecial = new Color(1f, 0.8f, 0.25f, 0.95f);

        void Start() { Inicializar(); }

        public void Inicializar()
        {
            if (inicializado) return;
            inicializado = true;
            if (materialBlancas == null) materialBlancas = FabricaMateriales.Lit(new Color(0.93f, 0.9f, 0.82f), 0.75f, 0f);
            if (materialNegras == null) materialNegras = FabricaMateriales.Lit(new Color(0.09f, 0.09f, 0.12f), 0.85f, 0.25f);
            matMarcador = FabricaMateriales.Particula(FabricaMateriales.TexturaAnillo, true);
            CrearTablero();
            raizPiezas = new GameObject("Piezas").transform;
            raizPiezas.SetParent(transform, false);
            raizMarcadores = new GameObject("Marcadores").transform;
            raizMarcadores.SetParent(transform, false);
            marcadorJaque = CrearMarcador(new Color(1f, 0.1f, 0.1f, 1f), 1.15f);
            marcadorJaque.SetActive(false);
            marcadorCursor = CrearMarcador(new Color(1f, 1f, 1f, 0.6f), 1f);
            marcadorCursor.SetActive(false);
            NuevaPartida();
        }

        // =================================================================
        //  COORDENADAS
        // =================================================================
        public Vector3 CasillaAMundo(Casilla c)
        {
            return transform.TransformPoint(new Vector3((c.Columna - 3.5f) * tamCasilla, 0f, (c.Fila - 3.5f) * tamCasilla));
        }

        public bool MundoACasilla(Vector3 p, out Casilla c)
        {
            Vector3 l = transform.InverseTransformPoint(p);
            int col = Mathf.FloorToInt(l.x / tamCasilla + 4f);
            int fila = Mathf.FloorToInt(l.z / tamCasilla + 4f);
            c = new Casilla(col, fila);
            return c.Valida;
        }

        /// <summary>Convierte coordenadas normalizadas del tablero (0..1, origen en a1) a mundo.</summary>
        public Vector3 NormalizadoAMundo(Vector2 n)
        {
            return transform.TransformPoint(new Vector3((n.x - 0.5f) * 8f * tamCasilla, 0f, (n.y - 0.5f) * 8f * tamCasilla));
        }

        public Plane Plano { get { return new Plane(transform.up, transform.position); } }

        public PiezaVisual PiezaEn(Casilla c)
        {
            PiezaVisual p;
            return piezas.TryGetValue(c, out p) ? p : null;
        }

        public IEnumerable<PiezaVisual> TodasLasPiezas() { return piezas.Values; }

        // =================================================================
        //  CONSTRUCCIÓN
        // =================================================================
        void CrearTablero() { ConstruirModeloTablero(prefabTablero); }

        void ConstruirModeloTablero(GameObject prefab)
        {
            if (modeloTablero != null) Destroy(modeloTablero.gameObject);
            if (prefab != null)
            {
                var t = Instantiate(prefab, transform);
                t.name = "ModeloTablero";
                t.transform.localScale = Vector3.one * tamCasilla;
                modeloTablero = t.transform;
                foreach (var r in t.GetComponentsInChildren<Renderer>()) r.receiveShadows = true;
                IluminacionEscena.PrepararMateriales(t);
            }
            else
            {
                modeloTablero = new GameObject("TableroPrimitivo").transform;
                modeloTablero.SetParent(transform, false);
                var clara = FabricaMateriales.Lit(new Color(0.91f, 0.86f, 0.75f), 0.4f);
                var oscura = FabricaMateriales.Lit(new Color(0.36f, 0.23f, 0.14f), 0.4f);
                var marco = FabricaMateriales.Lit(new Color(0.18f, 0.11f, 0.07f), 0.5f);
                for (int c = 0; c < 8; c++)
                    for (int f = 0; f < 8; f++)
                    {
                        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        Destroy(g.GetComponent<Collider>());
                        g.name = new Casilla(c, f).ToString();
                        g.transform.SetParent(modeloTablero, false);
                        g.transform.localPosition = new Vector3((c - 3.5f) * tamCasilla, -0.06f, (f - 3.5f) * tamCasilla);
                        g.transform.localScale = new Vector3(tamCasilla, 0.12f, tamCasilla);
                        g.GetComponent<Renderer>().sharedMaterial = (c + f) % 2 == 0 ? oscura : clara;
                    }
                var m = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(m.GetComponent<Collider>());
                m.name = "Marco";
                m.transform.SetParent(modeloTablero, false);
                m.transform.localPosition = new Vector3(0f, -0.1f, 0f);
                m.transform.localScale = new Vector3(9.2f * tamCasilla, 0.18f, 9.2f * tamCasilla);
                m.GetComponent<Renderer>().sharedMaterial = marco;
            }
            // colisionador para que los fragmentos reboten sobre el tablero
            var col = gameObject.GetComponent<BoxCollider>();
            if (col == null) col = gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, -0.1f, 0f);
            col.size = new Vector3(9.4f * tamCasilla, 0.2f, 9.4f * tamCasilla);
        }

        GameObject PrefabDe(TipoPieza t)
        {
            var mt = TemaActual != null ? TemaActual.modelos : null;
            if (mt != null)
            {
                GameObject g = null;
                switch (t)
                {
                    case TipoPieza.Peon: g = mt.peon; break;
                    case TipoPieza.Torre: g = mt.torre; break;
                    case TipoPieza.Caballo: g = mt.caballo; break;
                    case TipoPieza.Alfil: g = mt.alfil; break;
                    case TipoPieza.Dama: g = mt.dama; break;
                    default: g = mt.rey; break;
                }
                if (g != null) return g;
            }
            switch (t)
            {
                case TipoPieza.Peon: return prefabPeon;
                case TipoPieza.Torre: return prefabTorre;
                case TipoPieza.Caballo: return prefabCaballo;
                case TipoPieza.Alfil: return prefabAlfil;
                case TipoPieza.Dama: return prefabDama;
                default: return prefabRey;
            }
        }

        GameObject InstanciarModelo(TipoPieza t)
        {
            var pf = PrefabDe(t);
            GameObject g = pf != null ? Instantiate(pf) : PiezaPrimitiva.Crear(t);
            g.name = t.ToString();
            g.transform.localScale *= tamCasilla;
            return g;
        }

        Quaternion RotacionModelo(TipoPieza t, Bando b)
        {
            // Las blancas miran hacia +Z (hacia las negras) y las negras hacia -Z.
            float y = (b == Bando.Negras ? 180f : 0f) + (t == TipoPieza.Caballo ? rotacionCaballo : 0f);
            return Quaternion.Euler(0f, y, 0f);
        }

        PiezaVisual CrearPieza(Pieza p, Casilla c)
        {
            var go = new GameObject(p.Bando + "_" + p.Tipo + "_" + c);
            go.transform.SetParent(raizPiezas, false);
            go.transform.position = CasillaAMundo(c);
            go.transform.rotation = transform.rotation;
            var pv = go.AddComponent<PiezaVisual>();
            pv.Configurar(p.Tipo, p.Bando, c, InstanciarModelo(p.Tipo), RotacionModelo(p.Tipo, p.Bando));
            PintarPieza(pv);
            return pv;
        }

        void PintarPieza(PiezaVisual p)
        {
            bool b = p.Bando == Bando.Blancas;
            p.AsignarMaterial(b ? materialBlancas : materialNegras, b ? acentoBlancas : acentoNegras);
        }

        /// <summary>Cambia el juego de piezas y el tablero al tema del escenario, sin perder la partida.</summary>
        public void AplicarTema(TemaPiezas tema)
        {
            if (!inicializado) Inicializar();
            TemaActual = tema;
            foreach (var m in materialesTema) if (m != null) Destroy(m);
            materialesTema.Clear();
            if (tema != null)
            {
                materialBlancas = Registrar(FabricaMateriales.Lit(tema.blancas, tema.suavidad, tema.metalBlancas));
                materialNegras = Registrar(FabricaMateriales.Lit(tema.negras, Mathf.Min(1f, tema.suavidad + 0.1f), tema.metalNegras));
                acentoBlancas = Registrar(FabricaMateriales.Lit(tema.acentoBlancas, 0.8f, tema.acentoEmisivo ? 0f : 0.9f));
                acentoNegras = Registrar(FabricaMateriales.Lit(tema.acentoNegras, 0.8f, tema.acentoEmisivo ? 0f : 0.9f));
                if (tema.acentoEmisivo)
                {
                    FabricaMateriales.Emision(acentoBlancas, tema.acentoBlancas * 2.2f);
                    FabricaMateriales.Emision(acentoNegras, tema.acentoNegras * 2.2f);
                }
            }
            GameObject pt = tema != null && tema.modelos.tablero != null ? tema.modelos.tablero : prefabTablero;
            ConstruirModeloTablero(pt);
            foreach (var p in piezas.Values)
            {
                if (p == null) continue;
                p.CambiarModelo(InstanciarModelo(p.Tipo), RotacionModelo(p.Tipo, p.Bando));
                PintarPieza(p);
                float d = Vector3.Distance(p.transform.position, transform.position);
                p.Saltar(0.45f, d * 0.05f);
            }
            var fx = EfectosAjedrez.Instancia;
            if (fx != null) fx.OndaChoque(transform.position, new Color(1f, 0.9f, 0.6f), 7f, 0.8f, 0.2f);
        }

        Material Registrar(Material m) { materialesTema.Add(m); return m; }

        public void NuevaPartida()
        {
            foreach (var p in piezas.Values) if (p != null) Destroy(p.gameObject);
            piezas.Clear();
            Partida = string.IsNullOrEmpty(fenInicial) ? new PartidaAjedrez() : new PartidaAjedrez(fenInicial);
            for (int c = 0; c < 8; c++)
                for (int f = 0; f < 8; f++)
                {
                    var pz = Partida.En(c, f);
                    if (pz.Vacia) continue;
                    var cas = new Casilla(c, f);
                    piezas[cas] = CrearPieza(pz, cas);
                }
            OcultarDestinos();
            ActualizarJaque();
            Mensaje("¡Nueva partida! Juegan las blancas");
            StartCoroutine(AnimacionEntrada());
            if (PartidaReiniciada != null) PartidaReiniciada();
        }

        /// <summary>Se dispara cuando el tablero se reconstruye desde una posicion (correccion de la camara).</summary>
        public event Action PosicionCargada;

        /// <summary>
        /// Pone el tablero en la posicion FEN dada sin reiniciar la partida (la usa la camara para
        /// deshacer una jugada de la mano que no ocurrio en el tablero real).
        /// </summary>
        public void CargarPosicion(string fen)
        {
            if (!inicializado) Inicializar();
            if (string.IsNullOrEmpty(fen)) return;
            foreach (var p in piezas.Values) if (p != null) Destroy(p.gameObject);
            piezas.Clear();
            Partida = new PartidaAjedrez(fen);
            for (int c = 0; c < 8; c++)
                for (int f = 0; f < 8; f++)
                {
                    var pz = Partida.En(c, f);
                    if (pz.Vacia) continue;
                    var cas = new Casilla(c, f);
                    var pv = CrearPieza(pz, cas);
                    piezas[cas] = pv;
                    pv.Saltar(0.2f, 0f);
                }
            OcultarDestinos();
            MostrarCursor(false, Casilla.Ninguna, Color.white);
            ActualizarJaque();
            var fx = EfectosAjedrez.Instancia;
            if (fx != null) fx.Sonar(fx.sonidoError, 0.3f);
            if (PosicionCargada != null) PosicionCargada();
        }

        IEnumerator AnimacionEntrada()
        {
            // las piezas caen desde arriba en ola
            foreach (var p in piezas.Values)
            {
                float d = (p.Casilla.Fila < 4 ? p.Casilla.Fila : 7 - p.Casilla.Fila) * 0.08f + p.Casilla.Columna * 0.02f;
                p.Saltar(0.6f, d);
            }
            yield break;
        }

        // =================================================================
        //  JUGADAS
        // =================================================================
        public bool EsTurnoDe(PiezaVisual p) { return p != null && !Partida.Terminada && p.Bando == Partida.Turno; }

        public List<Jugada> JugadasDe(PiezaVisual p)
        {
            if (p == null) return new List<Jugada>();
            return Partida.JugadasLegales(p.Casilla);
        }

        public bool Validar(PiezaVisual p, Casilla destino, out Jugada jugada)
        {
            jugada = default(Jugada);
            if (p == null || !destino.Valida) return false;
            return Partida.BuscarJugada(p.Casilla, destino, out jugada, TipoPieza.Dama);
        }

        /// <summary>Aplica la jugada en el momento del impacto: captura, enroque, coronación y onda.</summary>
        public ResultadoJugada Confirmar(PiezaVisual pieza, Jugada j, Impulso imp)
        {
            var fx = EfectosAjedrez.Instancia;
            var res = Partida.Aplicar(j);
            // captura
            if (j.EsCaptura)
            {
                var victima = PiezaEn(j.CasillaCapturada);
                if (victima != null && victima != pieza)
                {
                    piezas.Remove(j.CasillaCapturada);
                    if (fx != null) fx.Fragmentar(victima, CasillaAMundo(j.Desde), imp);
                    Destroy(victima.gameObject);
                }
            }
            // mover la pieza en el diccionario
            piezas.Remove(j.Desde);
            pieza.Casilla = j.Hasta;
            piezas[j.Hasta] = pieza;
            pieza.transform.position = CasillaAMundo(j.Hasta);
            pieza.name = pieza.Bando + "_" + pieza.Tipo + "_" + j.Hasta;
            // enroque: la torre salta a su lugar
            if (j.EsEnroque)
            {
                var torre = PiezaEn(j.TorreDesde);
                if (torre != null)
                {
                    piezas.Remove(j.TorreDesde);
                    torre.Casilla = j.TorreHasta;
                    piezas[j.TorreHasta] = torre;
                    torre.MoverEnArco(CasillaAMundo(j.TorreHasta), 0.45f, 1.2f);
                }
                Mensaje("¡Enroque!");
            }
            // coronación: el peón se convierte en dama con un destello
            if (j.Coronacion != TipoPieza.Ninguna)
            {
                pieza.Tipo = j.Coronacion;
                pieza.CambiarModelo(InstanciarModelo(j.Coronacion), RotacionModelo(j.Coronacion, pieza.Bando));
                PintarPieza(pieza);
                if (fx != null) fx.Luz(pieza.transform.position, 1f);
                Mensaje("¡Coronación!");
            }
            // impacto y onda de choque
            Vector3 punto = CasillaAMundo(j.Hasta);
            if (fx != null) fx.Impacto(punto, imp, j.EsCaptura);
            Onda(punto, imp, pieza);
            pieza.Aplastar(0.25f + imp.Energia * 0.35f);
            ActualizarJaque();
            switch (res.EstadoDespues)
            {
                case EstadoPartida.Jaque: Mensaje("¡Jaque!"); break;
                case EstadoPartida.JaqueMate: Mensaje("¡JAQUE MATE! Ganan las " + (res.TurnoDespues == Bando.Blancas ? "negras" : "blancas")); StartCoroutine(Celebracion(res.TurnoDespues)); break;
                case EstadoPartida.Ahogado: Mensaje("Tablas por ahogado"); break;
                case EstadoPartida.TablasCincuenta: Mensaje("Tablas (regla de 50 jugadas)"); break;
                case EstadoPartida.TablasMaterial: Mensaje("Tablas por material insuficiente"); break;
            }
            if (JugadaRealizada != null) JugadaRealizada(res, imp);
            return res;
        }

        /// <summary>Onda expansiva: las piezas cercanas saltan con retardo según la distancia.</summary>
        public void Onda(Vector3 centro, Impulso imp, PiezaVisual excluir)
        {
            float e = Mathf.Clamp01(imp.Energia);
            float fuerza = 0.08f + e * 0.55f + (imp.Tipo == TipoEfecto.Supernova ? 0.4f : 0f);
            float alcance = 2.5f + e * 6f;
            const float velocidadOnda = 10f;
            foreach (var p in piezas.Values)
            {
                if (p == null || p == excluir || p.Ocupada) continue;
                float d = Vector3.Distance(p.transform.position, centro);
                if (d > alcance) continue;
                float caida = 1f - d / alcance;
                p.Saltar(fuerza * caida * caida, d / velocidadOnda);
            }
            // el tablero entero se hunde un poquito y rebota
            velSacudida -= (0.6f + e * 2.5f);
        }

        IEnumerator Celebracion(Bando perdedor)
        {
            var fx = EfectosAjedrez.Instancia;
            for (int i = 0; i < 6; i++)
            {
                foreach (var p in piezas.Values)
                    if (p != null && p.Bando != perdedor) p.Saltar(0.5f, UnityEngine.Random.Range(0f, 0.2f));
                if (fx != null)
                {
                    var rey = Partida.PosicionRey(PartidaAjedrez.Contrario(perdedor));
                    if (rey.Valida) fx.Luz(CasillaAMundo(rey), 1f);
                }
                yield return new WaitForSeconds(0.6f);
            }
        }

        // =================================================================
        //  MARCADORES (destinos legales, jaque, cursor)
        // =================================================================
        GameObject CrearMarcador(Color color, float escala)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(g.GetComponent<Collider>());
            g.name = "Marcador";
            g.transform.SetParent(raizMarcadores, false);
            g.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            g.transform.localScale = Vector3.one * tamCasilla * escala;
            var r = g.GetComponent<Renderer>();
            r.material = new Material(matMarcador);
            FabricaMateriales.ColorParticula(r.material, color);
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return g;
        }

        public void MostrarDestinos(List<Jugada> jugadas)
        {
            OcultarDestinos();
            if (!mostrarAyudaVisual) return;
            for (int i = 0; i < jugadas.Count; i++)
            {
                var j = jugadas[i];
                if (j.Coronacion != TipoPieza.Ninguna && j.Coronacion != TipoPieza.Dama) continue;
                while (marcadores.Count <= i) marcadores.Add(CrearMarcador(ColorMover, 0.92f));
                var m = marcadores[i];
                Color c = j.EsCaptura ? ColorCapturar : (j.EsEnroque || j.Coronacion != TipoPieza.Ninguna ? ColorEspecial : ColorMover);
                FabricaMateriales.ColorParticula(m.GetComponent<Renderer>().material, c);
                m.transform.position = CasillaAMundo(j.Hasta) + transform.up * 0.015f;
                m.SetActive(true);
            }
        }

        public void OcultarDestinos()
        {
            foreach (var m in marcadores) if (m != null) m.SetActive(false);
        }

        public void MostrarCursor(bool visible, Casilla c, Color color)
        {
            if (marcadorCursor == null) return;
            marcadorCursor.SetActive(visible && c.Valida);
            if (!visible || !c.Valida) return;
            marcadorCursor.transform.position = CasillaAMundo(c) + transform.up * 0.02f;
            FabricaMateriales.ColorParticula(marcadorCursor.GetComponent<Renderer>().material, color);
        }

        void ActualizarJaque()
        {
            if (marcadorJaque == null || Partida == null) return;
            bool jaque = Partida.EnJaque(Partida.Turno);
            marcadorJaque.SetActive(jaque);
            if (jaque) marcadorJaque.transform.position = CasillaAMundo(Partida.PosicionRey(Partida.Turno)) + transform.up * 0.012f;
        }

        public void Mensaje(string texto)
        {
            ultimoMensaje = texto;
            tiempoMensaje = 3f;
        }

        // =================================================================
        //  CICLO
        // =================================================================
        void Update()
        {
            if (!inicializado) return;
            if (EntradaUtil.Pulsada(TeclaJuego.Reiniciar)) NuevaPartida();

            float dt = Time.deltaTime;
            // resorte del tablero
            velSacudida += (-180f * sacudidaTablero - 14f * velSacudida) * dt;
            sacudidaTablero += velSacudida * dt;
            if (modeloTablero != null) modeloTablero.localPosition = Vector3.up * sacudidaTablero * 0.05f;

            // pulso de los marcadores
            float s = 1f + Mathf.Sin(Time.time * 6f) * 0.04f;
            foreach (var m in marcadores) if (m != null && m.activeSelf) m.transform.localScale = Vector3.one * tamCasilla * 0.92f * s;
            if (marcadorJaque != null && marcadorJaque.activeSelf)
                marcadorJaque.transform.localScale = Vector3.one * tamCasilla * (1.05f + Mathf.Sin(Time.time * 9f) * 0.1f);
            if (tiempoMensaje > 0f) tiempoMensaje -= dt;
        }

        void OnGUI()
        {
            if (Partida == null || MenuAjedrez.Activo) return;
            var estilo = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(14, Screen.height / 40), fontStyle = FontStyle.Bold };
            string turno = Partida.Terminada ? "Partida terminada (R para reiniciar)" :
                           "Turno: " + (Partida.Turno == Bando.Blancas ? "BLANCAS" : "NEGRAS") + (Partida.Estado == EstadoPartida.Jaque ? "  —  ¡JAQUE!" : "");
            Sombra(new Rect(16, 12, 900, 40), turno, estilo);
            if (tiempoMensaje > 0f)
            {
                var grande = new GUIStyle(estilo) { fontSize = Mathf.Max(22, Screen.height / 16), alignment = TextAnchor.MiddleCenter };
                Color c = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(tiempoMensaje));
                Sombra(new Rect(0, Screen.height * 0.12f, Screen.width, 80), ultimoMensaje, grande);
                GUI.color = c;
            }
        }

        public static void Sombra(Rect r, string texto, GUIStyle estilo)
        {
            Color c = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, c.a * 0.75f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), texto, estilo);
            GUI.color = c;
            GUI.Label(r, texto, estilo);
        }
    }
}
