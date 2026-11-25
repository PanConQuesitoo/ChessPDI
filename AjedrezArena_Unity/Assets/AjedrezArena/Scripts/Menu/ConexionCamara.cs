// =====================================================================
//  ConexionCamara.cs  -  Enciende la cámara (ajedrez_vivo.py) y habla con ella.
//
//  Unity  --(5070: modo menu / calibrar / juego, recalibrar, ping, salir)-->  Python
//  Python --(5067: estado JSON cada cuadro: mano, tablero, calibración)-->    Unity
//  Python --(5068: video JPEG de la cámara con la cuadrícula dibujada)-->     Unity
//  (la mano del juego sigue llegando por 5065 y las jugadas por 5066)
//
//  Si Python ya está corriendo (iniciar_camara.bat), no se abre otro.
//  Al salir del juego se apaga el Python que encendió Unity.
// =====================================================================
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AjedrezArena
{
    /// <summary>Lo que cuenta Python en cada cuadro (mismos nombres que EstadoVivo en ajedrez_vivo.py).</summary>
    [Serializable]
    public class EstadoCamara
    {
        public string modo = "";
        public int cam;
        public float fps;
        public string error = "";
        public bool cal;                 // tablero encontrado
        public float calidad;            // qué tan clara se ve la cuadrícula (0..1)
        public bool mano;                // hay una mano en la imagen
        public float ix, iy;             // mano en la imagen (0..1, origen arriba-izquierda)
        public float bx, by;             // mano en el tablero (0..1, origen a1)
        public bool agarre;              // pinza (pulgar + índice juntos)
        public bool encima;              // la mano está sobre el tablero
        public bool quieto;              // el tablero lleva un rato quieto
        public bool leido;               // la red ya leyó el tablero
        public bool ocupacionOk;         // 32 piezas donde van y 32 casillas vacías
        public string[] malas = new string[0];
        public int dudosas;
        public int tiposOk;
        public string orientacion = "";
        public bool listo;               // visto bueno para jugar
        public float listoSeg;
        public string aviso = "";
        public int jugadas;
        public string ultimaJugada = "";
    }

    public class ConexionCamara : MonoBehaviour
    {
        public static ConexionCamara Instancia { get; private set; }

        [Header("Python (ajedrez_vivo.py)")]
        public bool encenderAlIniciar = true;
        [Tooltip("Carpeta con ajedrez_vivo.py, entrega_unity y venv. Vacío = <proyecto>/Detector")]
        public string carpetaDetector = "";
        [Tooltip("python.exe del entorno virtual. Vacío = <carpetaDetector>/venv/Scripts/python.exe")]
        public string rutaPython = "";
        public int indiceCamara = 0;
        public string argumentosExtra = "";

        [Header("Puertos")]
        public int puertoEstado = 5067;
        public int puertoVideo = 5068;
        public int puertoControl = 5070;

        public EstadoCamara Estado { get; private set; }
        public Texture2D Video { get; private set; }
        public bool HayVideo { get; private set; }
        public bool Conectada { get { return Time.unscaledTime - tUltimoEstado < 1.5f; } }
        public bool AlgunaVezConectada { get; private set; }
        public bool Encendiendo { get { return ProcesoVivo && !AlgunaVezConectada; } }
        public bool ProcesoVivo { get { try { return proceso != null && !proceso.HasExited; } catch (Exception) { return false; } } }
        public bool FaltaInstalar { get; private set; }
        public string Problema { get; private set; }
        public float SegundosEncendiendo { get { return Time.unscaledTime - tEncendido; } }
        public string CarpetaDetector { get { return Carpeta(); } }

        Process proceso;
        UdpClient udpEstado, udpVideo, udpEnvio;
        Thread hiloEstado, hiloVideo;
        volatile bool corriendo;
        volatile string jsonPendiente;
        byte[] videoPendiente;
        readonly object cerrojoVideo = new object();
        float tUltimoEstado = -100f, tEncendido = -100f, tPing;
        readonly ConcurrentQueue<string> lineas = new ConcurrentQueue<string>();
        StreamWriter registro;
        string modoActual = "menu";
        bool nuevaActual = true;
        float tModo;

        // ------------------------------------------------------------------ ciclo de vida
        void Awake()
        {
            Instancia = this;
            Estado = new EstadoCamara();
            Problema = "";
            udpEnvio = new UdpClient();
            corriendo = true;
            udpEstado = Escuchar(puertoEstado, false, out hiloEstado);
            udpVideo = Escuchar(puertoVideo, true, out hiloVideo);
        }

        IEnumerator Start()
        {
            // si la cámara ya estaba encendida (iniciar_camara.bat), no abrir otra
            float fin = Time.unscaledTime + 1.2f;
            while (Time.unscaledTime < fin && !Conectada) yield return null;
            if (!Conectada && encenderAlIniciar) Encender();
        }

        UdpClient Escuchar(int puerto, bool esVideo, out Thread hilo)
        {
            hilo = null;
            try
            {
                var u = new UdpClient(new IPEndPoint(IPAddress.Loopback, puerto));
                u.Client.ReceiveBufferSize = 1 << 20;
                hilo = new Thread(() => Recibir(u, esVideo)) { IsBackground = true, Name = "Camara" + puerto };
                hilo.Start();
                return u;
            }
            catch (Exception e)
            {
                Problema = "No se pudo abrir el puerto " + puerto + ": " + e.Message;
                Debug.LogWarning("[Ajedrez] " + Problema);
                return null;
            }
        }

        void Recibir(UdpClient u, bool esVideo)
        {
            var remoto = new IPEndPoint(IPAddress.Any, 0);
            while (corriendo)
            {
                try
                {
                    byte[] datos = u.Receive(ref remoto);
                    if (esVideo) { lock (cerrojoVideo) videoPendiente = datos; }
                    else jsonPendiente = Encoding.UTF8.GetString(datos);
                }
                catch (SocketException) { if (!corriendo) break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception) { }
            }
        }

        void Update()
        {
            string j = jsonPendiente;
            if (j != null)
            {
                jsonPendiente = null;
                try
                {
                    var e = JsonUtility.FromJson<EstadoCamara>(j);
                    if (e != null)
                    {
                        if (e.malas == null) e.malas = new string[0];
                        Estado = e;
                        indiceCamara = e.cam;      // la cámara que de verdad funciona (si se reinicia, abre esa)
                        tUltimoEstado = Time.unscaledTime;
                        AlgunaVezConectada = true;
                        Problema = "";
                    }
                }
                catch (Exception) { }
            }
            byte[] v = null;
            lock (cerrojoVideo) { v = videoPendiente; videoPendiente = null; }
            if (v != null)
            {
                if (Video == null) { Video = new Texture2D(4, 4, TextureFormat.RGB24, false); Video.wrapMode = TextureWrapMode.Clamp; }
                if (Video.LoadImage(v)) HayVideo = true;
            }
            string l;
            while (lineas.TryDequeue(out l)) Debug.Log("[Cámara] " + l);

            if (Time.unscaledTime - tPing > 1f)
            {
                tPing = Time.unscaledTime;
                Enviar("{\"cmd\":\"ping\"}");
                // se repite el modo por si Python se reinició (Python ignora el repetido)
                if (Time.unscaledTime - tModo > 2f) EnviarModo();
            }
            if (proceso != null && !ProcesoVivo && !Conectada && string.IsNullOrEmpty(Problema))
                Problema = "La cámara se cerró: " + (string.IsNullOrEmpty(ultimaLinea) ? "sin mensaje" : ultimaLinea) +
                           "  (detalle en Detector/ajedrez_vivo.log)";
        }

        void OnDestroy() { Apagar(); }
        void OnApplicationQuit() { Apagar(); }

        // ------------------------------------------------------------------ encender / apagar Python
        string Carpeta()
        {
            if (!string.IsNullOrEmpty(carpetaDetector)) return carpetaDetector;
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Detector"));
        }

        public string RutaPython()
        {
            if (!string.IsNullOrEmpty(rutaPython) && File.Exists(rutaPython)) return rutaPython;
            return Path.Combine(Carpeta(), "venv", "Scripts", "python.exe");
        }

        public void Encender()
        {
            if (ProcesoVivo) return;
            Problema = "";
            FaltaInstalar = false;
            string carpeta = Carpeta();
            string script = Path.Combine(carpeta, "ajedrez_vivo.py");
            string python = RutaPython();
            if (!File.Exists(script)) { Problema = "No encuentro " + script; return; }
            if (!File.Exists(python))
            {
                FaltaInstalar = true;
                Problema = "Falta instalar las librerías de la cámara (instalar_dependencias.bat).";
                return;
            }
            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = python,
                    Arguments = "\"" + script + "\" --unity --cam " + indiceCamara + " " + argumentosExtra,
                    WorkingDirectory = carpeta,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                info.EnvironmentVariables["PYTHONUNBUFFERED"] = "1";
                info.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                try { registro = new StreamWriter(Path.Combine(carpeta, "ajedrez_vivo.log"), false, new UTF8Encoding(false)) { AutoFlush = true }; }
                catch (Exception) { registro = null; }
                proceso = new Process { StartInfo = info, EnableRaisingEvents = true };
                proceso.OutputDataReceived += (s, e) => Linea(e.Data);
                proceso.ErrorDataReceived += (s, e) => Linea(e.Data);
                proceso.Start();
                proceso.BeginOutputReadLine();
                proceso.BeginErrorReadLine();
                tEncendido = Time.unscaledTime;
                Debug.Log("[Ajedrez] Cámara encendida: " + python + " " + info.Arguments);
            }
            catch (Exception e)
            {
                Problema = "No se pudo encender Python: " + e.Message;
                proceso = null;
            }
        }

        volatile string ultimaLinea = "";

        void Linea(string s)
        {
            if (s == null) return;
            if (s.Trim().Length > 0) ultimaLinea = s.Trim();
            lineas.Enqueue(s);
            var r = registro;
            if (r != null) { try { lock (r) r.WriteLine(s); } catch (Exception) { } }
        }

        /// <summary>Abre el instalador de librerías en una ventana (la descarga es de ~1 GB, una sola vez).</summary>
        public void AbrirInstalador()
        {
            string bat = Path.Combine(Carpeta(), "instalar_dependencias.bat");
            if (!File.Exists(bat)) { Problema = "No encuentro " + bat; return; }
            try { Process.Start(new ProcessStartInfo { FileName = bat, WorkingDirectory = Carpeta(), UseShellExecute = true }); }
            catch (Exception e) { Problema = "No se pudo abrir el instalador: " + e.Message; }
        }

        public void Reintentar()
        {
            ApagarProceso();
            // los puertos de escucha se quedan abiertos (cerrarlos y reabrirlos al instante falla en Windows)
            if (udpEstado == null) udpEstado = Escuchar(puertoEstado, false, out hiloEstado);
            if (udpVideo == null) udpVideo = Escuchar(puertoVideo, true, out hiloVideo);
            AlgunaVezConectada = false;
            Encender();
        }

        /// <summary>Cierra solo el programa de Python.</summary>
        void ApagarProceso()
        {
            if (proceso != null)
            {
                try
                {
                    Enviar("{\"cmd\":\"salir\"}");
                    if (!proceso.WaitForExit(800)) proceso.Kill();
                }
                catch (Exception) { }
                proceso = null;
            }
            if (registro != null) { try { registro.Dispose(); } catch (Exception) { } registro = null; }
        }

        /// <summary>Apaga todo: Python y los puertos (al salir de Play).</summary>
        public void Apagar()
        {
            ApagarProceso();
            corriendo = false;
            try { if (udpEstado != null) udpEstado.Close(); } catch (Exception) { }
            try { if (udpVideo != null) udpVideo.Close(); } catch (Exception) { }
            udpEstado = udpVideo = null;
        }

        bool saliendo;
        void OnDisable() { saliendo = true; }

        // ------------------------------------------------------------------ órdenes a Python
        void Enviar(string json)
        {
            try
            {
                byte[] b = Encoding.UTF8.GetBytes(json);
                udpEnvio.Send(b, b.Length, "127.0.0.1", puertoControl);
            }
            catch (Exception) { }
        }

        /// <summary>"menu" (solo mano, sin jugadas), "calibrar" (revisa el tablero) o "juego".</summary>
        public void Modo(string modo, bool nuevaPartida = true)
        {
            modoActual = modo;
            nuevaActual = nuevaPartida;
            EnviarModo();
        }

        void EnviarModo()
        {
            tModo = Time.unscaledTime;
            Enviar("{\"cmd\":\"modo\",\"valor\":\"" + modoActual + "\",\"nueva\":" + (nuevaActual ? "true" : "false") + "}");
        }

        public void Recalibrar() { Enviar("{\"cmd\":\"recalibrar\"}"); }
        public void Girar() { Enviar("{\"cmd\":\"girar\",\"grados\":90}"); }
        public void Espejo() { Enviar("{\"cmd\":\"espejo\"}"); }

        int camaraPedida = -1;

        /// <summary>Prueba la siguiente cámara (0..3). Si no existe, Python sigue con la que funcionaba
        /// y el siguiente toque prueba otra.</summary>
        public void CambiarCamara()
        {
            int actual = Conectada ? Estado.cam : indiceCamara;
            int desde = camaraPedida >= 0 ? camaraPedida : actual;
            int siguiente = (desde + 1) % 4;
            if (siguiente == actual) siguiente = (siguiente + 1) % 4;
            camaraPedida = siguiente;
            if (!ProcesoVivo) { indiceCamara = siguiente; Reintentar(); return; }
            Enviar("{\"cmd\":\"camara\",\"indice\":" + siguiente + "}");
        }
    }
}
