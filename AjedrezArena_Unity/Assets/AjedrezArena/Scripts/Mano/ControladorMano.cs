// =====================================================================
//  ControladorMano.cs  -  La mano que agarra las piezas.
//
//  1. Lee la posición desde una FuenteEntradaMano (mouse hoy, cámara mañana).
//  2. Proyecta esa posición sobre el plano del tablero.
//  3. Mide VELOCIDAD y ACELERACIÓN de la mano (con suavizado y picos
//     recientes) mientras lleva una pieza.
//  4. Al soltar decide el tipo de golpe:
//        carga completa (quieto ~2 s)          -> SUPERNOVA
//        aceleración muy alta o velocidad alta  -> RAYO
//        velocidad media/alta                   -> FUEGO
//        movimiento tranquilo                   -> LUZ
//     y la "energía" (0..1) que escala partículas, sacudida y onda.
//  5. Anima el golpe estilo "ground pound": sube, gira, se queda en el
//     aire un instante y cae en picada sobre la casilla.
//  6. Hay DOS manos: una del color de las blancas y otra del de las negras.
//     Juega la del bando que tiene el turno; la otra espera en su lado del
//     tablero, relajada y con la luz apagada. Al cambiar el turno se relevan.
// =====================================================================
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AjedrezArena
{
    public class ControladorMano : MonoBehaviour
    {
        [Header("Referencias")]
        public ControladorTablero tablero;
        public Camera camara;
        [Tooltip("Se usa la primera fuente activa de la lista (p. ej. UDP y, si no hay datos, el mouse).")]
        public List<FuenteEntradaMano> fuentes = new List<FuenteEntradaMano>();
        public GameObject prefabMano;
        public Color colorMano = new Color(0.95f, 0.95f, 1f);
        [Header("Dos manos (una por bando)")]
        public Color colorManoBlancas = new Color(0.95f, 0.93f, 0.9f);
        public Color colorManoNegras = new Color(0.12f, 0.12f, 0.15f);
        [Tooltip("La mano negra es una mano izquierda (espejo): se lee como 'el otro jugador'.")]
        public bool espejarNegras = true;
        [Tooltip("Cada mano apunta desde su lado del tablero hacia el rival.")]
        public bool orientarSegunBando = true;
        [Tooltip("Dónde espera la mano que no tiene el turno (coordenadas del tablero; las negras usan el opuesto).")]
        public Vector3 reposoBlancas = new Vector3(3.2f, 1.4f, -5.6f);

        [Header("Posición de la mano")]
        public float escalaMano = 1.6f;
        public float alturaLibre = 1.6f;
        [Tooltip("Altura de la base de la pieza mientras se lleva.")]
        public float alturaPiezaLlevada = 0.45f;
        public float suavizado = 22f;

        [Header("Umbrales de energía (casillas/s y casillas/s²)")]
        public float rapidezFuego = 5f;
        public float rapidezRayo = 12f;
        public float aceleracionRayo = 80f;
        [Tooltip("Por debajo de esta rapidez la pieza se considera quieta y empieza a cargarse.")]
        public float rapidezQuieto = 0.6f;
        public float esperaAntesDeCargar = 0.6f;
        public float tiempoParaCargar = 2.2f;
        [Tooltip("Ventana (s) para medir los picos de velocidad/aceleración antes de soltar.")]
        public float ventanaPicos = 0.45f;

        [Header("Golpe")]
        public float velocidadCaida = 14f;
        public bool depuracion = false;

        enum Estado { Libre, Llevando, Golpeando, Devolviendo }
        Estado estado = Estado.Libre;

        Transform mano;          // mano ACTIVA (la del turno)
        ManoAnimador anim;

        class ManoDeBando
        {
            public Bando bando;
            public Transform raiz;
            public GameObject modelo;
            public ManoAnimador anim;
            public Light luz;
            public Material piel;
            public Quaternion rot = Quaternion.identity;
        }
        readonly ManoDeBando[] manos = new ManoDeBando[2];
        ManoDeBando activa;
        float transicion;
        GameObject prefabTema;
        FuenteEntradaMano fuente;
        Vector3 puntoTablero, puntoSuave, puntoAnterior;
        Vector3 velocidad, velocidadPrev;
        float rapidez, aceleracion;
        bool hayPunto, agarrePrev, primerCuadro = true;

        struct Muestra { public float t, v, a; }
        readonly List<Muestra> historial = new List<Muestra>(128);

        PiezaVisual llevada, hover;
        List<Jugada> destinos = new List<Jugada>();
        EfectosEnMovimiento fxMov;
        Vector3 posPieza, velPieza;
        float tiempoAgarre, tiempoQuieto, carga, recorrido;
        bool cargaCompleta;
        Vector3? posManoForzada;
        Quaternion rotMano = Quaternion.identity;
        Impulso ultimoImpulso;
        bool hayUltimoImpulso;

        public float Rapidez { get { return rapidez; } }
        public float Aceleracion { get { return aceleracion; } }
        public float Carga { get { return carga; } }
        /// <summary>true cuando la mano no lleva ni anima ninguna pieza.</summary>
        public bool Libre { get { return estado == Estado.Libre; } }
        bool pausada;
        /// <summary>Menu abierto: las manos se esconden y no agarran nada.</summary>
        public void Pausar(bool p)
        {
            pausada = p;
            if (p && estado != Estado.Libre) AlReiniciar();
            MostrarManos(!p);
        }

        void MostrarManos(bool v)
        {
            foreach (var m in manos)
                if (m != null && m.raiz != null && m.raiz.gameObject.activeSelf != v) m.raiz.gameObject.SetActive(v);
        }

        // =================================================================
        void Start()
        {
            if (tablero == null) tablero = FindAnyObjectByTypeCompat<ControladorTablero>();
            if (camara == null) camara = Camera.main;
            if (fuentes.Count == 0)
            {
                var m = GetComponent<EntradaManoMouse>();
                if (m == null) m = gameObject.AddComponent<EntradaManoMouse>();
                fuentes.Add(m);
            }
            CrearMano();
            if (tablero != null)
            {
                tablero.PartidaReiniciada += AlReiniciar;
                tablero.PosicionCargada += AlReiniciar;
            }
        }

        void OnDestroy()
        {
            if (tablero == null) return;
            tablero.PartidaReiniciada -= AlReiniciar;
            tablero.PosicionCargada -= AlReiniciar;
        }

        void AlReiniciar()
        {
            StopAllCoroutines();
            if (fxMov != null) fxMov.Detener();
            fxMov = null;
            llevada = null;
            hover = null;
            posManoForzada = null;
            estado = Estado.Libre;
        }

        static T FindAnyObjectByTypeCompat<T>() where T : Object
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindFirstObjectByType<T>();
#else
            return Object.FindObjectOfType<T>();
#endif
        }

        void CrearMano()
        {
            for (int i = 0; i < 2; i++)
            {
                var m = new ManoDeBando { bando = i == 0 ? Bando.Blancas : Bando.Negras };
                var raiz = new GameObject(i == 0 ? "ManoBlanca" : "ManoNegra");
                raiz.transform.SetParent(transform, false);
                m.raiz = raiz.transform;
                var gl = new GameObject("LuzTurno");
                gl.transform.SetParent(m.raiz, false);
                gl.transform.localPosition = new Vector3(0f, 0.6f, 0f);
                m.luz = gl.AddComponent<Light>();
                m.luz.type = LightType.Point;
                m.luz.range = 3.5f;
                m.luz.intensity = 0f;
                m.luz.shadows = LightShadows.None;
                m.luz.color = i == 0 ? new Color(1f, 0.93f, 0.8f) : new Color(0.6f, 0.72f, 1f);
                manos[i] = m;
                ConstruirModelo(m, prefabMano, i == 0 ? colorManoBlancas : colorManoNegras, 0f);
                m.raiz.position = PosicionReposo(m.bando);
            }
            Activar(manos[tablero != null && tablero.Partida != null && tablero.Partida.Turno == Bando.Negras ? 1 : 0], false);
        }

        /// <summary>(Re)crea el modelo de una mano con el color de su bando.</summary>
        void ConstruirModelo(ManoDeBando m, GameObject prefab, Color color, float metal)
        {
            if (m.modelo != null) Destroy(m.modelo);
            if (m.piel != null) Destroy(m.piel);
            m.piel = FabricaMateriales.Lit(color, m.bando == Bando.Negras ? 0.8f : 0.55f, metal);
            GameObject modelo;
            if (prefab != null)
            {
                modelo = Instantiate(prefab);
                bool algunaPiel = false;
                foreach (var r in modelo.GetComponentsInChildren<Renderer>())
                    foreach (var mt in r.sharedMaterials)
                        if (mt != null && (mt.name.Contains("Piel") || mt.name.Contains("Guante"))) algunaPiel = true;
                foreach (var r in modelo.GetComponentsInChildren<Renderer>())
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        string n = mats[i] != null ? mats[i].name : "";
                        bool esPiel = algunaPiel ? ((n.Contains("Piel") || n.Contains("Guante")) && !n.Contains("Costura")) : true;
                        if (esPiel) mats[i] = m.piel;
                    }
                    r.sharedMaterials = mats;
                }
                IluminacionEscena.PrepararMateriales(modelo);
            }
            else modelo = ManoProcedural.Crear(m.piel);
            modelo.transform.SetParent(m.raiz, false);
            Vector3 esc = modelo.transform.localScale * escalaMano;
            if (m.bando == Bando.Negras && espejarNegras) esc.x = -esc.x;
            modelo.transform.localScale = esc;
            foreach (var c in modelo.GetComponentsInChildren<Collider>()) Destroy(c);
            foreach (var r in modelo.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            m.modelo = modelo;
            m.anim = modelo.AddComponent<ManoAnimador>();
            m.anim.cierre = 0.3f;
            if (activa == m) anim = m.anim;
        }

        /// <summary>Usa la mano del tema (o la base) y los colores de los bandos del escenario.</summary>
        public void AplicarTema(TemaPiezas tema)
        {
            if (manos[0] == null) return;
            prefabTema = tema != null && tema.modelos != null && tema.modelos.mano != null ? tema.modelos.mano : prefabMano;
            Color b = tema != null ? tema.blancas : colorManoBlancas;
            Color n = tema != null ? Color.Lerp(tema.negras, Color.white, 0.06f) : colorManoNegras;
            ConstruirModelo(manos[0], prefabTema, b, tema != null ? tema.metalBlancas : 0f);
            ConstruirModelo(manos[1], prefabTema, n, tema != null ? tema.metalNegras : 0.2f);
        }

        void Activar(ManoDeBando m, bool conTransicion)
        {
            if (activa == m) return;
            activa = m;
            mano = m.raiz;
            anim = m.anim;
            transicion = conTransicion ? 0.8f : 0f;
            if (conTransicion && EfectosAjedrez.Instancia != null)
                EfectosAjedrez.Instancia.Sonar(EfectosAjedrez.Instancia.sonidoAgarrar, 0.25f, 0.7f);
        }

        Vector3 PosicionReposo(Bando b)
        {
            Vector3 l = b == Bando.Blancas ? reposoBlancas : new Vector3(-reposoBlancas.x, reposoBlancas.y, -reposoBlancas.z);
            return tablero != null ? tablero.transform.TransformPoint(l * tablero.tamCasilla) : l;
        }

        /// <summary>La mano que no tiene el turno espera en su lado, relajada.</summary>
        void MoverReposo(ManoDeBando m, float dt)
        {
            if (m == null || m.raiz == null) return;
            Vector3 up = tablero.transform.up;
            Vector3 objetivo = PosicionReposo(m.bando) + up * Mathf.Sin(Time.time * 1.3f + (int)m.bando * 2f) * 0.06f;
            m.raiz.position = Vector3.Lerp(m.raiz.position, objetivo, 1f - Mathf.Exp(-dt * 4f));
            Vector3 haciaCentro = Vector3.ProjectOnPlane(tablero.transform.position - m.raiz.position, up);
            if (haciaCentro.sqrMagnitude < 1e-4f) haciaCentro = tablero.transform.forward;
            Quaternion r = Quaternion.LookRotation(haciaCentro.normalized, up);
            r = Quaternion.AngleAxis(18f, r * Vector3.right) * r;
            m.rot = Quaternion.Slerp(m.rot, r, 1f - Mathf.Exp(-dt * 5f));
            m.raiz.rotation = m.rot;
            m.anim.cierre = 0.35f + Mathf.Sin(Time.time * 0.9f + (int)m.bando) * 0.05f;
            m.anim.Temblar(0f);
            m.luz.intensity = Mathf.MoveTowards(m.luz.intensity, 0f, dt * 3f);
        }

        // =================================================================
        void Update()
        {
            if (pausada) { MostrarManos(false); return; }
            if (tablero == null || mano == null || tablero.Partida == null) return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            if (EntradaUtil.Pulsada(TeclaJuego.Depuracion)) depuracion = !depuracion;

            // relevo: cuando ya no hay animación en curso, juega la mano del bando con el turno
            if (estado == Estado.Libre && manos[0] != null)
            {
                var deTurno = manos[tablero.Partida.Turno == Bando.Blancas ? 0 : 1];
                if (deTurno != activa)
                {
                    if (hover != null) { hover.QuitarResaltado(); hover = null; }
                    Activar(deTurno, true);
                    agarrePrev = true;   // evita que un clic sostenido agarre con la mano nueva
                }
            }
            LeerEntrada();
            MedirMovimiento(dt);

            bool agarre = fuente != null && fuente.Activa && fuente.Agarrando;
            bool presionado = agarre && !agarrePrev;
            bool soltado = !agarre && agarrePrev;
            agarrePrev = agarre;

            switch (estado)
            {
                case Estado.Libre: ActualizarLibre(presionado, agarre); break;
                case Estado.Llevando: ActualizarLlevando(dt, soltado); break;
            }
            MoverMano(dt);
            if (manos[0] != null)
            {
                MoverReposo(manos[0] == activa ? manos[1] : manos[0], dt);
                activa.luz.intensity = Mathf.MoveTowards(activa.luz.intensity, 1.4f, dt * 3f);
                if (transicion > 0f) transicion -= dt;
            }
        }

        void LeerEntrada()
        {
            fuente = null;
            foreach (var f in fuentes) if (f != null && f.isActiveAndEnabled && f.Activa) { fuente = f; break; }
            if (fuente == null) { hayPunto = false; return; }
            Vector3 p;
            if (fuente.Espacio == EspacioCoordenadas.Tablero)
            {
                p = tablero.NormalizadoAMundo(fuente.Posicion);
            }
            else
            {
                if (camara == null) camara = Camera.main;
                if (camara == null) return;
                Ray r = camara.ViewportPointToRay(new Vector3(fuente.Posicion.x, fuente.Posicion.y, 0f));
                float d;
                // plano a la altura de la mano para que el cursor "toque" donde apunta
                Plane plano = tablero.Plano;
                if (!plano.Raycast(r, out d)) return;
                p = r.GetPoint(d);
            }
            // limitar a la zona del tablero (+ un margen)
            Vector3 l = tablero.transform.InverseTransformPoint(p);
            float lim = 5.2f * tablero.tamCasilla;
            l.x = Mathf.Clamp(l.x, -lim, lim);
            l.z = Mathf.Clamp(l.z, -lim, lim);
            l.y = 0f;
            puntoTablero = tablero.transform.TransformPoint(l);
            hayPunto = true;
        }

        void MedirMovimiento(float dt)
        {
            if (!hayPunto) return;
            if (primerCuadro) { puntoSuave = puntoAnterior = puntoTablero; primerCuadro = false; }
            puntoSuave = Vector3.Lerp(puntoSuave, puntoTablero, 1f - Mathf.Exp(-dt * suavizado));
            // velocidad en "casillas por segundo"
            Vector3 v = (puntoSuave - puntoAnterior) / dt / tablero.tamCasilla;
            puntoAnterior = puntoSuave;
            velocidad = Vector3.Lerp(velocidad, v, 1f - Mathf.Exp(-dt * 15f));
            Vector3 a = (velocidad - velocidadPrev) / dt;
            velocidadPrev = velocidad;
            rapidez = velocidad.magnitude;
            aceleracion = Mathf.Lerp(aceleracion, a.magnitude, 1f - Mathf.Exp(-dt * 12f));
            historial.Add(new Muestra { t = Time.time, v = rapidez, a = aceleracion });
            while (historial.Count > 0 && Time.time - historial[0].t > ventanaPicos) historial.RemoveAt(0);
        }

        float PicoRapidez() { float m = 0f; foreach (var s in historial) m = Mathf.Max(m, s.v); return m; }
        float PicoAceleracion() { float m = 0f; foreach (var s in historial) m = Mathf.Max(m, s.a); return m; }

        // =================================================================
        //  ESTADO: LIBRE (mano abierta buscando pieza)
        // =================================================================
        void ActualizarLibre(bool presionado, bool agarre)
        {
            Casilla c = Casilla.Ninguna;
            PiezaVisual nueva = null;
            bool sobreTablero = hayPunto && tablero.MundoACasilla(puntoSuave, out c);
            if (sobreTablero)
            {
                var p = tablero.PiezaEn(c);
                if (p != null && !p.Ocupada) nueva = p;
            }
            if (nueva != hover)
            {
                if (hover != null) hover.QuitarResaltado();
                hover = nueva;
            }
            bool propia = hover != null && tablero.EsTurnoDe(hover);
            if (hover != null) hover.Resaltar(propia ? new Color(1f, 0.75f, 0.25f) * 0.9f : new Color(0.25f, 0.25f, 0.3f));
            tablero.MostrarCursor(sobreTablero && !agarre, c, propia ? ControladorTablero.ColorEspecial : new Color(1f, 1f, 1f, 0.35f));

            anim.cierre = agarre ? 1f : (propia ? 0f : 0.2f);   // se abre sobre una pieza propia
            anim.Temblar(0f);

            if (presionado && hover != null)
            {
                if (propia) Tomar(hover);
                else
                {
                    // pieza del rival o partida terminada: "no"
                    hover.Aplastar(0.15f);
                    hover.Resaltar(new Color(1f, 0.1f, 0.1f));
                    if (EfectosAjedrez.Instancia != null) EfectosAjedrez.Instancia.Sonar(EfectosAjedrez.Instancia.sonidoError, 0.5f);
                    tablero.Mensaje(tablero.Partida.Terminada ? "La partida terminó (R para reiniciar)" :
                                    "Es el turno de las " + (tablero.Partida.Turno == Bando.Blancas ? "blancas" : "negras"));
                }
            }
        }

        void Tomar(PiezaVisual p)
        {
            llevada = p;
            hover = null;
            p.Ocupada = true;
            p.Resaltar(new Color(1f, 0.85f, 0.4f) * 0.6f);
            destinos = tablero.JugadasDe(p);
            tablero.MostrarDestinos(destinos);
            tablero.MostrarCursor(false, Casilla.Ninguna, Color.white);
            posPieza = p.transform.position;
            velPieza = Vector3.zero;
            tiempoAgarre = tiempoQuieto = carga = recorrido = 0f;
            cargaCompleta = false;
            historial.Clear();
            p.Aplastar(-0.25f);          // se estira al levantarla
            anim.cierre = 0.85f;
            estado = Estado.Llevando;
            var fx = EfectosAjedrez.Instancia;
            if (fx != null)
            {
                fx.Sonar(fx.sonidoAgarrar, 0.6f);
                fxMov = fx.AdjuntarEfectosMovimiento(p);
            }
        }

        // =================================================================
        //  ESTADO: LLEVANDO UNA PIEZA
        // =================================================================
        void ActualizarLlevando(float dt, bool soltado)
        {
            if (llevada == null) { estado = Estado.Libre; return; }
            tiempoAgarre += dt;
            recorrido += rapidez * dt;
            if (rapidez < rapidezQuieto) tiempoQuieto += dt;
            else tiempoQuieto = Mathf.Max(0f, tiempoQuieto - dt * 4f);
            float cargaObjetivo = Mathf.Clamp01((tiempoQuieto - esperaAntesDeCargar) / tiempoParaCargar);
            // la carga sube al estar quieto y baja lentamente si te mueves (puedes cargar y luego lanzar)
            carga = cargaObjetivo > carga ? cargaObjetivo : Mathf.Max(cargaObjetivo, carga - dt * 0.15f);
            if (carga >= 0.999f) cargaCompleta = true;     // una vez cargada, se queda cargada hasta soltar
            if (cargaCompleta) carga = 1f;
            anim.cierre = 0.85f;
            anim.Temblar(carga);

            // la pieza cuelga de la mano con un resorte (se balancea)
            Vector3 objetivo = puntoSuave + tablero.transform.up * (alturaPiezaLlevada + Mathf.Sin(Time.time * 3f) * 0.03f);
            const float k = 320f, amort = 24f;
            velPieza += ((objetivo - posPieza) * k - velPieza * amort) * dt;
            posPieza += velPieza * dt;
            llevada.transform.position = posPieza;
            llevada.Inclinar(velPieza / tablero.tamCasilla, 3.5f);

            if (fxMov != null) fxMov.Actualizar(Mathf.Max(rapidez, PicoRapidez() * 0.7f), aceleracion, carga, rapidezFuego, rapidezRayo, aceleracionRayo);

            // casilla bajo la pieza
            Casilla c;
            bool dentro = tablero.MundoACasilla(puntoSuave, out c);
            bool legal = false;
            if (dentro) foreach (var j in destinos) if (j.Hasta == c) { legal = true; break; }
            tablero.MostrarCursor(dentro, c, legal ? Color.white : new Color(1f, 0.3f, 0.3f, 0.5f));

            if (soltado) Soltar();
        }

        Impulso CalcularImpulso()
        {
            float v = Mathf.Max(rapidez, PicoRapidez() * 0.85f);
            float a = PicoAceleracion();
            var imp = new Impulso
            {
                Rapidez = v,
                Aceleracion = a,
                TiempoAgarre = tiempoAgarre,
                Carga = carga,
                Direccion = velocidad.sqrMagnitude > 0.01f ? velocidad.normalized : tablero.transform.forward
            };
            if (carga >= 0.999f)
            {
                imp.Tipo = TipoEfecto.Supernova;
                imp.Energia = 1f;
            }
            else if (a >= aceleracionRayo || v >= rapidezRayo)
            {
                imp.Tipo = TipoEfecto.Rayo;
                imp.Energia = 0.4f + 0.6f * Mathf.Max(Mathf.InverseLerp(rapidezRayo, rapidezRayo * 1.8f, v),
                                                      Mathf.InverseLerp(aceleracionRayo, aceleracionRayo * 2f, a));
            }
            else if (v >= rapidezFuego || (recorrido > 14f && v > rapidezFuego * 0.6f))
            {
                imp.Tipo = TipoEfecto.Fuego;
                imp.Energia = 0.35f + 0.65f * Mathf.InverseLerp(rapidezFuego, rapidezRayo, v);
            }
            else
            {
                imp.Tipo = TipoEfecto.Luz;
                imp.Energia = 0.15f + 0.6f * Mathf.InverseLerp(0f, rapidezFuego, v) + 0.25f * carga;
            }
            imp.Energia = Mathf.Clamp01(imp.Energia);
            return imp;
        }

        void Soltar()
        {
            var p = llevada;
            tablero.OcultarDestinos();
            tablero.MostrarCursor(false, Casilla.Ninguna, Color.white);
            Casilla destino;
            bool dentro = tablero.MundoACasilla(puntoSuave, out destino);
            Jugada j;
            var imp = CalcularImpulso();
            ultimoImpulso = imp;
            hayUltimoImpulso = true;
            if (dentro && destino != p.Casilla && tablero.Validar(p, destino, out j))
                StartCoroutine(Golpear(p, j, imp));
            else
                StartCoroutine(Devolver(p, dentro && destino == p.Casilla));
        }

        // =================================================================
        //  ANIMACIÓN DE GOLPE ("ground pound")
        // =================================================================
        IEnumerator Golpear(PiezaVisual p, Jugada j, Impulso imp)
        {
            estado = Estado.Golpeando;
            llevada = null;
            var fx = EfectosAjedrez.Instancia;
            Vector3 up = tablero.transform.up;
            Vector3 destino = tablero.CasillaAMundo(j.Hasta);
            float e = imp.Energia;
            bool grande = imp.Tipo == TipoEfecto.Supernova;

            // 1) sube sobre la casilla destino girando
            float alturaPrep = 0.5f + e * 1.4f + (grande ? 1.2f : 0f);
            float tPrep = 0.12f + 0.1f * e + (grande ? 0.15f : 0f);
            float vueltas = e > 0.55f ? 360f : (e > 0.3f ? 180f : 0f);
            if (grande) vueltas = 720f;
            Vector3 inicio = p.transform.position;
            Vector3 arriba = destino + up * alturaPrep;
            float t = 0f;
            while (t < tPrep)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / tPrep);
                float s = 1f - (1f - k) * (1f - k);
                p.transform.position = Vector3.Lerp(inicio, arriba, s);
                p.GiroExtra = vueltas * s;
                p.Inclinar(Vector3.zero, 0f);
                posManoForzada = p.transform.position + up * (p.Altura * 0.8f + 0.5f);
                yield return null;
            }
            // 2) suspendida un instante: la mano se abre
            anim.cierre = 0f;
            if (fx != null) fx.Sonar(fx.sonidoSoltar, 0.5f);
            if (fxMov != null) fxMov.Detener();
            p.Aplastar(-0.2f - e * 0.2f);         // se estira antes de caer
            float pausa = 0.05f + 0.07f * e + (grande ? 0.12f : 0f);
            yield return new WaitForSeconds(pausa);
            posManoForzada = null;

            // 3) caída en picada (acelera)
            float vel = velocidadCaida * (1f + e * 1.5f);
            while (true)
            {
                vel += 60f * Time.deltaTime;
                Vector3 pos = p.transform.position - up * vel * Time.deltaTime;
                if (Vector3.Dot(pos - destino, up) <= 0f) break;
                p.transform.position = pos;
                yield return null;
            }
            p.transform.position = destino;
            p.Enderezar();

            // 4) impacto
            p.QuitarResaltado();
            tablero.Confirmar(p, j, imp);
            p.Ocupada = false;
            fxMov = null;
            yield return new WaitForSeconds(0.08f);
            estado = Estado.Libre;
        }

        IEnumerator Devolver(PiezaVisual p, bool mismaCasilla)
        {
            estado = Estado.Devolviendo;
            llevada = null;
            anim.cierre = 0.2f;
            var fx = EfectosAjedrez.Instancia;
            if (fxMov != null) fxMov.Detener();
            fxMov = null;
            if (!mismaCasilla)
            {
                if (fx != null)
                {
                    fx.Sonar(fx.sonidoError, 0.45f);
                    fx.Polvo(p.transform.position, 0.3f);
                }
                tablero.Mensaje("Jugada no válida");
            }
            p.Enderezar();
            p.QuitarResaltado();
            bool listo = false;
            p.MoverEnArco(tablero.CasillaAMundo(p.Casilla), mismaCasilla ? 0.15f : 0.4f, mismaCasilla ? 0.1f : 0.8f, () => listo = true);
            p.Ocupada = true;
            while (!listo) yield return null;
            p.Ocupada = false;
            if (fx != null) fx.Sonar(fx.sonidoSoltar, 0.4f);
            estado = Estado.Libre;
        }

        // =================================================================
        //  MOVER Y ORIENTAR LA MANO
        // =================================================================
        void MoverMano(float dt)
        {
            bool visible = hayPunto || posManoForzada.HasValue || estado != Estado.Libre;
            if (mano.gameObject.activeSelf != visible) mano.gameObject.SetActive(visible);
            if (!visible) return;
            Vector3 up = tablero.transform.up;
            Vector3 offsetAgarre = anim.CentroAgarre != null ? anim.CentroAgarre.position - mano.position : Vector3.down * 0.45f;
            Vector3 objetivo;
            if (posManoForzada.HasValue) objetivo = posManoForzada.Value;
            else if (estado == Estado.Llevando && llevada != null)
            {
                // la mano queda justo encima de la pieza que lleva
                objetivo = posPieza + up * (llevada.Altura * 0.8f) - offsetAgarre;
                objetivo += Random.insideUnitSphere * carga * 0.04f;   // tiembla al cargarse
            }
            else objetivo = puntoSuave + up * alturaLibre - Vector3.ProjectOnPlane(offsetAgarre, up);  // el punto de agarre sobre el cursor
            float k = posManoForzada.HasValue ? 1f - Mathf.Exp(-dt * 30f) : 1f;
            if (transicion > 0f) k = 1f - Mathf.Exp(-dt * 7f);      // la mano nueva llega volando desde su lado
            mano.position = Vector3.Lerp(mano.position, objetivo, k);

            // orientación: desde su lado del tablero (o hacia donde mira la cámara), inclinada según la velocidad
            Vector3 adelante = camara != null ? Vector3.ProjectOnPlane(camara.transform.forward, up) : tablero.transform.forward;
            if (orientarSegunBando && activa != null)
                adelante = activa.bando == Bando.Blancas ? tablero.transform.forward : -tablero.transform.forward;
            if (adelante.sqrMagnitude < 1e-4f) adelante = camara != null ? Vector3.ProjectOnPlane(camara.transform.up, up) : Vector3.forward;
            Quaternion baseRot = Quaternion.LookRotation(adelante.normalized, up);
            Vector3 v = velocidad;
            Vector3 eje = Vector3.Cross(up, v);
            float ang = Mathf.Clamp(v.magnitude * 2.2f, 0f, 35f);
            Quaternion inclinacion = eje.sqrMagnitude > 1e-6f ? Quaternion.AngleAxis(ang, eje.normalized) : Quaternion.identity;
            Quaternion cabeceo = Quaternion.AngleAxis(estado == Estado.Libre ? 12f : 22f, baseRot * Vector3.right);
            if (activa != null) rotMano = activa.rot;
            rotMano = Quaternion.Slerp(rotMano, inclinacion * cabeceo * baseRot, 1f - Mathf.Exp(-dt * 12f));
            mano.rotation = rotMano;
            if (activa != null) activa.rot = rotMano;
        }

        // =================================================================
        //  PANEL DE DEPURACIÓN (F1): útil para ajustar umbrales con la cámara
        // =================================================================
        void OnGUI()
        {
            if (!depuracion) return;
            var estilo = new GUIStyle(GUI.skin.label) { fontSize = 14 };
            float x = Screen.width - 360, y = 60;
            GUI.Box(new Rect(x - 10, y - 10, 350, 230), "");
            GUI.Label(new Rect(x, y, 340, 20), "Fuente: " + (fuente != null ? fuente.Nombre : "ninguna"), estilo); y += 22;
            GUI.Label(new Rect(x, y, 340, 20), "Estado: " + estado, estilo); y += 22;
            Barra(ref y, x, "Rapidez", rapidez, rapidezRayo * 1.5f, rapidezFuego, rapidezRayo, estilo);
            Barra(ref y, x, "Pico rapidez", PicoRapidez(), rapidezRayo * 1.5f, rapidezFuego, rapidezRayo, estilo);
            Barra(ref y, x, "Aceleración", aceleracion, aceleracionRayo * 1.5f, aceleracionRayo, aceleracionRayo, estilo);
            Barra(ref y, x, "Carga", carga, 1f, 1f, 1f, estilo);
            if (estado == Estado.Llevando)
                GUI.Label(new Rect(x, y, 340, 20), "Al soltar: " + CalcularImpulso().Tipo, estilo);
            else if (hayUltimoImpulso)
                GUI.Label(new Rect(x, y, 340, 40), "Último: " + ultimoImpulso, estilo);
        }

        static void Barra(ref float y, float x, string nombre, float valor, float maximo, float u1, float u2, GUIStyle estilo)
        {
            GUI.Label(new Rect(x, y, 120, 20), nombre, estilo);
            float w = 200f;
            GUI.Box(new Rect(x + 120, y + 4, w, 12), "");
            Color c = GUI.color;
            GUI.color = valor >= u2 ? new Color(0.5f, 0.8f, 1f) : valor >= u1 ? new Color(1f, 0.55f, 0.15f) : new Color(1f, 0.95f, 0.7f);
            GUI.DrawTexture(new Rect(x + 120, y + 4, w * Mathf.Clamp01(valor / Mathf.Max(0.001f, maximo)), 12), Texture2D.whiteTexture);
            GUI.color = c;
            y += 22;
        }
    }
}
