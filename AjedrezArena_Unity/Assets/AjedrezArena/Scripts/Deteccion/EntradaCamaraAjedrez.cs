// =====================================================================
//  EntradaCamaraAjedrez.cs  -  La cámara como jugador.
//
//  Recibe jugadas detectadas (por la CNN en Python) y las ejecuta en el juego
//  con la misma animación y efectos de siempre. Las valida con las reglas del
//  ajedrez: una detección que no sea una jugada legal NUNCA toca el tablero.
//
//  Por dónde pueden llegar las detecciones (todas a la vez, usa la que quieras):
//    1) UDP        -> el cuadernillo / script de Python envía el JSON al puerto 5066
//    2) Archivo    -> Python escribe un .json y Unity lo lee cuando cambia
//    3) Código     -> otro script llama a  Enviar(...)  o  EnviarTexto(...)
//    4) Webcam     -> DetectorCamaraPython.cs (toma las fotos y llama al detector)
//
//  Con ajedrez_vivo.py la mano virtual sigue tu mano real (UDP 5065) y hace la
//  jugada con sus efectos; la detección que llega después la CONFIRMA o la CORRIGE.
//
//  Formatos de texto aceptados: ver DeteccionCamara.Parsear.
//
//  Teclas:  Enter = aceptar una jugada dudosa    Retroceso = descartarla
// =====================================================================
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace AjedrezArena
{
    public class EntradaCamaraAjedrez : MonoBehaviour
    {
        [Header("Conexión")]
        public ControladorTablero tablero;
        [Tooltip("Escuchar detecciones por UDP (JSON del detector).")]
        public bool escucharUDP = true;
        public int puertoUDP = 5066;
        [Tooltip("Ruta de un .json donde Python escribe la última detección (vacío = no usar).")]
        public string archivoDetecciones = "";
        public float segundosEntreLecturas = 0.25f;

        [Header("Validación")]
        [Range(0f, 1f)]
        [Tooltip("Por debajo de esta confianza la jugada espera confirmación (Enter) o se descarta (Retroceso).")]
        public float umbralConfianza = 0.7f;
        [Tooltip("Si la CNN nombra una pieza distinta de la que hay en el origen, rechazar. Apagado: se confía en las casillas.")]
        public bool exigirPieza = false;
        [Tooltip("Segundos que espera una jugada dudosa antes de descartarse sola.")]
        public float esperaConfirmacion = 15f;

        [Header("Mano en tiempo real + cámara")]
        [Tooltip("Si la mano virtual (seguida por la cámara) ya hizo la jugada, la detección la CONFIRMA. " +
                 "Si la cámara ve otra cosa, se deshace lo de la mano y se aplica lo que vio la cámara.")]
        public bool conciliarConMano = true;
        [Tooltip("Si la camara ve otra cosa que la mano, deshacer la jugada de la mano? Apagado: la jugada de la mano se queda.")]
        public bool permitirDeshacer = false;

        [Header("Animación")]
        public float duracionMovimiento = 0.7f;
        public float alturaArco = 1.3f;

        /// <summary>(detección, interpretación, ¿se aplicó al tablero?)</summary>
        public event Action<DeteccionCamara, ResultadoInterpretacion, bool> DeteccionProcesada;

        public string UltimoMensaje { get; private set; }
        public int JugadasAplicadas { get; private set; }

        readonly ConcurrentQueue<string> entrantes = new ConcurrentQueue<string>();
        readonly ConcurrentQueue<DeteccionCamara> directas = new ConcurrentQueue<DeteccionCamara>();
        UdpClient cliente;
        Thread hilo;
        volatile bool corriendo;
        int paquetes;
        DateTime ultimaModificacion = DateTime.MinValue;
        float proximaLectura;

        bool moviendo;
        DeteccionCamara pendiente;
        ResultadoInterpretacion pendienteResultado;
        bool pendienteRevertir;
        float tiempoPendiente;
        string mensajePanel = "";
        float tiempoPanel;
        bool panelOk;

        // conciliación mano <-> cámara
        string fenConfirmado;                                   // última posición que la cámara dio por buena
        readonly List<Jugada> jugadasMano = new List<Jugada>(); // jugadas hechas con la mano desde entonces
        bool aplicandoCamara;
        ControladorMano mano;
        public int JugadasConfirmadas { get; private set; }
        public int JugadasCorregidas { get; private set; }

        // ------------------------------------------------------------------ ciclo de vida
        void OnEnable()
        {
            if (tablero == null) tablero = FindFirstObjectByType<ControladorTablero>();
            if (tablero != null)
            {
                tablero.PartidaReiniciada += AlReiniciar;
                tablero.JugadaRealizada += AlJugar;
            }
            if (!escucharUDP) return;
            try
            {
                cliente = new UdpClient(puertoUDP);
                corriendo = true;
                hilo = new Thread(Escuchar) { IsBackground = true, Name = "AjedrezDeteccionUDP" };
                hilo.Start();
                Debug.Log("[Ajedrez] Esperando jugadas de la cámara por UDP en el puerto " + puertoUDP);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Ajedrez] No se pudo abrir el puerto UDP " + puertoUDP + " para la cámara: " + e.Message);
            }
        }

        void OnDisable()
        {
            if (tablero != null)
            {
                tablero.PartidaReiniciada -= AlReiniciar;
                tablero.JugadaRealizada -= AlJugar;
            }
            corriendo = false;
            try { if (cliente != null) cliente.Close(); } catch (Exception) { }
            cliente = null;
            if (hilo != null && hilo.IsAlive) hilo.Join(200);
            hilo = null;
        }

        void AlReiniciar()
        {
            StopAllCoroutines();
            moviendo = false;
            pendiente = null;
            aplicandoCamara = false;
            string t; DeteccionCamara d;
            while (entrantes.TryDequeue(out t)) { }
            while (directas.TryDequeue(out d)) { }
            jugadasMano.Clear();
            fenConfirmado = tablero != null && tablero.Partida != null ? tablero.Partida.Fen() : null;
        }

        /// <summary>Cada jugada que NO aplicó la cámara (la hizo la mano o el mouse) queda pendiente de confirmar.</summary>
        void AlJugar(ResultadoJugada res, Impulso imp)
        {
            if (aplicandoCamara) return;
            jugadasMano.Add(res.Jugada);
        }

        void PosicionConfirmada()
        {
            fenConfirmado = tablero.Partida.Fen();
            jugadasMano.Clear();
        }

        /// <summary>Deshace lo que hizo la mano: el tablero vuelve a la última posición confirmada por la cámara.</summary>
        void Revertir()
        {
            tablero.CargarPosicion(fenConfirmado);
            jugadasMano.Clear();
            JugadasCorregidas++;
        }

        void Escuchar()
        {
            var remoto = new IPEndPoint(IPAddress.Any, 0);
            while (corriendo)
            {
                try
                {
                    byte[] datos = cliente.Receive(ref remoto);
                    entrantes.Enqueue(System.Text.Encoding.UTF8.GetString(datos));
                    paquetes++;
                }
                catch (SocketException) { if (!corriendo) break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception e) { Debug.LogWarning("[Ajedrez] UDP cámara: " + e.Message); }
            }
        }

        // ------------------------------------------------------------------ API pública
        /// <summary>Envía una detección ya construida (se procesa en el siguiente cuadro).</summary>
        public void Enviar(DeteccionCamara d) { if (d != null) directas.Enqueue(d); }

        /// <summary>Envía el texto del detector (JSON, "M;pieza;origen;destino;conf" o "e2e4").</summary>
        public void EnviarTexto(string texto) { if (!string.IsNullOrWhiteSpace(texto)) entrantes.Enqueue(texto); }

        // ------------------------------------------------------------------ bucle principal
        void Update()
        {
            if (tablero == null || tablero.Partida == null) return;
            if (tiempoPanel > 0f) tiempoPanel -= Time.deltaTime;

            // archivo observado
            if (!string.IsNullOrEmpty(archivoDetecciones) && Time.unscaledTime >= proximaLectura)
            {
                proximaLectura = Time.unscaledTime + Mathf.Max(0.05f, segundosEntreLecturas);
                try
                {
                    if (File.Exists(archivoDetecciones))
                    {
                        var t = File.GetLastWriteTimeUtc(archivoDetecciones);
                        if (t != ultimaModificacion)
                        {
                            ultimaModificacion = t;
                            string texto = File.ReadAllText(archivoDetecciones);
                            if (!string.IsNullOrWhiteSpace(texto)) entrantes.Enqueue(texto);
                        }
                    }
                }
                catch (IOException) { /* Python todavía lo está escribiendo: se reintenta */ }
                catch (UnauthorizedAccessException) { }
            }

            // jugada dudosa en espera
            if (pendiente != null)
            {
                tiempoPendiente -= Time.deltaTime;
                if (EntradaUtil.Pulsada(TeclaJuego.AceptarDeteccion)) AceptarPendiente();
                else if (EntradaUtil.Pulsada(TeclaJuego.DescartarDeteccion) || tiempoPendiente <= 0f) DescartarPendiente();
                return;   // mientras hay una dudosa, las nuevas esperan
            }

            if (moviendo) return;
            if (fenConfirmado == null) fenConfirmado = tablero.Partida.Fen();
            if (mano == null) mano = FindFirstObjectByType<ControladorMano>();
            if (mano != null && !mano.Libre) return;   // la mano virtual todavía está animando su jugada

            DeteccionCamara d = null;
            if (!directas.TryDequeue(out d))
            {
                string texto;
                if (entrantes.TryDequeue(out texto))
                {
                    d = DeteccionCamara.Parsear(texto);
                    if (d == null) { Avisar("Mensaje de la cámara no reconocido: " + Recortar(texto, 60), false); return; }
                }
            }
            if (d != null) Procesar(d);
        }

        void Procesar(DeteccionCamara d)
        {
            if (conciliarConMano && jugadasMano.Count > 0)
            {
                ConciliarConMano(d);
                return;
            }
            var r = InterpreteDeteccion.Interpretar(tablero.Partida, d, umbralConfianza, exigirPieza);
            Resolver(d, r, false);
        }

        /// <summary>
        /// La mano virtual ya movió algo. La cámara compara el tablero REAL antes/después, así que
        /// la detección se interpreta desde la última posición confirmada:
        ///   misma jugada        -> confirmada (no se anima nada otra vez)
        ///   la cámara no vio nada -> la mano se equivocó (soltó sin mover la pieza real): se deshace
        ///   otra jugada legal   -> se deshace lo de la mano y se aplica lo que vio la cámara
        ///   no se entiende      -> se queda lo de la mano y se avisa
        /// </summary>
        void ConciliarConMano(DeteccionCamara d)
        {
            ResultadoInterpretacion r;
            var decision = InterpreteDeteccion.Conciliar(fenConfirmado, jugadasMano, d, umbralConfianza, exigirPieza, out r);
            if (!permitirDeshacer && (decision == DecisionConciliacion.Deshacer || decision == DecisionConciliacion.Corregir))
                decision = DecisionConciliacion.MantenerMano;   // no se devuelven jugadas: manda la mano
            if (decision == DecisionConciliacion.Confirmar)
            {
                PosicionConfirmada();
                JugadasConfirmadas++;
                Avisar("OK  La cámara confirma: " + r.Mensaje, true);
                Notificar(d, r, true);
                return;
            }
            if (decision == DecisionConciliacion.Deshacer)
            {
                Revertir();
                Avisar("La cámara no vio ninguna jugada en el tablero real: se deshizo la de la mano", false);
                tablero.Mensaje("Corregido por la cámara");
                Notificar(d, r, false);
                return;
            }
            if (decision == DecisionConciliacion.MantenerMano)
            {
                PosicionConfirmada();   // la cámara no lo entiende: se confía en la mano
                Avisar("La cámara no pudo confirmar (" + r.Mensaje + "). Se mantiene la jugada de la mano", false);
                Notificar(d, r, false);
                return;
            }
            Resolver(d, r, true);
        }

        /// <param name="revertirAntes">true si antes de aplicar hay que deshacer lo que hizo la mano.</param>
        void Resolver(DeteccionCamara d, ResultadoInterpretacion r, bool revertirAntes)
        {
            switch (r.Estado)
            {
                case EstadoInterpretacion.Aceptada:
                    if (revertirAntes)
                    {
                        Revertir();
                        Avisar("La cámara corrige la jugada de la mano: " + r.Mensaje, true);
                    }
                    else Avisar("Cámara: " + r.Mensaje, true);
                    Aplicar(d, r);
                    break;
                case EstadoInterpretacion.NecesitaConfirmacion:
                    pendiente = d; pendienteResultado = r; pendienteRevertir = revertirAntes; tiempoPendiente = esperaConfirmacion;
                    Avisar("¿" + r.Mensaje + "? (" + Mathf.RoundToInt(d.confianza * 100f) + "%)" +
                           (revertirAntes ? "  [la mano hizo otra]" : "") + "  Enter: aceptar · Retroceso: descartar", true, esperaConfirmacion);
                    break;
                case EstadoInterpretacion.SinCambios:
                    Avisar(r.Mensaje, false);
                    Notificar(d, r, false);
                    break;
                default:
                    Avisar("Cámara: " + r.Mensaje, false);
                    var fx = EfectosAjedrez.Instancia;
                    if (fx != null) fx.Sonar(fx.sonidoError, 0.45f);
                    tablero.Mensaje("Jugada no válida");
                    Notificar(d, r, false);
                    break;
            }
        }

        void AceptarPendiente()
        {
            var d = pendiente; var r = pendienteResultado;
            pendiente = null;
            if (pendienteRevertir) Revertir();
            Avisar("Cámara (confirmada): " + r.Mensaje, true);
            Aplicar(d, r);
        }

        void DescartarPendiente()
        {
            var d = pendiente; var r = pendienteResultado;
            pendiente = null;
            if (pendienteRevertir) PosicionConfirmada();   // se descarta lo de la cámara: queda lo de la mano
            Avisar("Jugada descartada", false);
            Notificar(d, r, false);
        }

        void Notificar(DeteccionCamara d, ResultadoInterpretacion r, bool aplicada)
        {
            if (DeteccionProcesada != null) DeteccionProcesada(d, r, aplicada);
        }

        // ------------------------------------------------------------------ ejecutar la jugada
        void Aplicar(DeteccionCamara d, ResultadoInterpretacion r)
        {
            var pieza = tablero.PiezaEn(r.Jugada.Desde);
            if (pieza == null || pieza.Ocupada)
            {
                Avisar("La pieza en " + r.Jugada.Desde + " no está disponible", false);
                Notificar(d, r, false);
                return;
            }
            StartCoroutine(Mover(d, r, pieza));
        }

        IEnumerator Mover(DeteccionCamara d, ResultadoInterpretacion r, PiezaVisual pieza)
        {
            moviendo = true;
            var j = r.Jugada;
            var fx = EfectosAjedrez.Instancia;
            Vector3 destino = tablero.CasillaAMundo(j.Hasta);
            Vector3 dir = destino - tablero.CasillaAMundo(j.Desde);
            dir.y = 0f;
            var imp = Impulso.Suave(j.EsCaptura ? 0.55f : 0.35f);
            if (dir.sqrMagnitude > 1e-4f) imp.Direccion = dir.normalized;

            pieza.Resaltar(new Color(0.4f, 0.9f, 1f, 1f));
            if (fx != null) fx.Sonar(fx.sonidoAgarrar, 0.5f);
            yield return new WaitForSeconds(0.25f);

            bool llego = false;
            pieza.MoverEnArco(destino, Mathf.Max(0.2f, duracionMovimiento), alturaArco, () => llego = true);
            while (!llego) yield return null;

            pieza.QuitarResaltado();
            if (fx != null) fx.Sonar(fx.sonidoSoltar, 0.5f);
            aplicandoCamara = true;
            try { tablero.Confirmar(pieza, j, imp); }
            finally { aplicandoCamara = false; }
            PosicionConfirmada();
            JugadasAplicadas++;
            Notificar(d, r, true);
            yield return new WaitForSeconds(0.1f);
            moviendo = false;
        }

        // ------------------------------------------------------------------ mensajes
        void Avisar(string texto, bool ok, float segundos = 6f)
        {
            UltimoMensaje = texto;
            mensajePanel = texto;
            panelOk = ok;
            tiempoPanel = segundos;
            Debug.Log("[Ajedrez] " + texto);
        }

        static string Recortar(string s, int n) { return s.Length <= n ? s : s.Substring(0, n) + "…"; }

        void OnGUI()
        {
            if (tablero == null || MenuAjedrez.Activo || MenuAjedrez.SinTablero) return;
            var estilo = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(12, Screen.height / 55), alignment = TextAnchor.LowerRight };
            string estadoUdp = escucharUDP ? "UDP " + puertoUDP + " (" + paquetes + ")" : "sin UDP";
            string pie = "Cámara · " + estadoUdp + " · aplicadas " + JugadasAplicadas + " · confirmadas " + JugadasConfirmadas +
                         " · corregidas " + JugadasCorregidas + (jugadasMano.Count > 0 && paquetes > 0 ? " · esperando confirmación…" : "");
            ControladorTablero.Sombra(new Rect(Screen.width - 816, Screen.height - 34, 800, 26), pie, estilo);
            if (tiempoPanel > 0f && !string.IsNullOrEmpty(mensajePanel))
            {
                var g = new GUIStyle(estilo) { fontSize = Mathf.Max(14, Screen.height / 38), fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperRight, wordWrap = true };
                Color c = GUI.color;
                GUI.color = panelOk ? new Color(0.6f, 1f, 0.7f, Mathf.Clamp01(tiempoPanel)) : new Color(1f, 0.55f, 0.45f, Mathf.Clamp01(tiempoPanel));
                ControladorTablero.Sombra(new Rect(Screen.width - 716, 16, 700, 90), mensajePanel, g);
                GUI.color = c;
            }
        }
    }
}
