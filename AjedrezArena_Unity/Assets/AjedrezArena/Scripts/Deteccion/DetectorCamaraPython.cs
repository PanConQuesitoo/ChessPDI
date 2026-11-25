// =====================================================================
//  DetectorCamaraPython.cs  -  Webcam + detector_ajedrez.py  =  "jugada por cámara".
//
//  Cómo se usa (con el tablero físico frente a la cámara, visto desde arriba, blancas abajo):
//    B      -> "foto base": guarda cómo está el tablero ahora (hazlo al empezar)
//    G      -> haces tu jugada en el tablero físico y pulsas G: toma la foto de "después",
//              llama al detector con (antes, después) y la jugada se ejecuta en el juego.
//              Si la jugada fue válida, la foto "después" pasa a ser la nueva base.
//
//  Si prefieres que el cuadernillo/Python haga todo (captura + detección), no necesitas este
//  componente: basta con que envíe el JSON por UDP a EntradaCamaraAjedrez (puerto 5066).
//
//  El detector corre en un hilo aparte (tarda unos segundos): el juego no se congela.
// =====================================================================
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AjedrezArena
{
    public class DetectorCamaraPython : MonoBehaviour
    {
        public EntradaCamaraAjedrez entrada;

        [Header("Detector (carpeta entrega_unity)")]
        [Tooltip("python.exe del entorno virtual donde se instalaron torch, opencv, etc.")]
        public string rutaPython = "C:/Proyecto_Ajedrez_PDI/venv/Scripts/python.exe";
        [Tooltip("detector_ajedrez.py (junto a modelo_ajedrez.pth y config_modelo.json)")]
        public string rutaScript = "C:/Proyecto_Ajedrez_PDI/entrega_unity/detector_ajedrez.py";
        public int segundosMaximos = 90;

        [Header("Webcam")]
        public bool usarWebcam = true;
        [Tooltip("Nombre (o parte del nombre) de la cámara; vacío = la primera.")]
        public string nombreCamara = "";
        public int ancho = 1280, alto = 720;
        public bool mostrarVistaPrevia = true;

        [Header("Fotos de prueba (sin webcam): clic derecho en el componente -> Probar con fotos")]
        public string fotoPruebaAntes = "C:/fotos/antes.jpg";
        public string fotoPruebaDespues = "C:/fotos/despues.jpg";

        WebCamTexture camara;
        string carpeta, fotoBase, fotoNueva;
        bool ocupado;
        string estado = "";
        readonly ConcurrentQueue<string> resultados = new ConcurrentQueue<string>();
        string fotoEnEvaluacion;

        void OnEnable()
        {
            if (entrada == null) entrada = FindFirstObjectByType<EntradaCamaraAjedrez>();
            if (entrada != null) entrada.DeteccionProcesada += AlProcesar;
            carpeta = Path.Combine(Application.persistentDataPath, "fotos_ajedrez");
            Directory.CreateDirectory(carpeta);
            fotoBase = Path.Combine(carpeta, "base.jpg");
            fotoNueva = Path.Combine(carpeta, "despues.jpg");
            if (usarWebcam) AbrirCamara();
        }

        void OnDisable()
        {
            if (entrada != null) entrada.DeteccionProcesada -= AlProcesar;
            if (camara != null) { camara.Stop(); Destroy(camara); camara = null; }
        }

        void AbrirCamara()
        {
            var dispositivos = WebCamTexture.devices;
            if (dispositivos.Length == 0) { estado = "No se encontró ninguna cámara"; Debug.LogWarning("[Ajedrez] " + estado); return; }
            string elegido = dispositivos[0].name;
            if (!string.IsNullOrEmpty(nombreCamara))
                foreach (var d in dispositivos)
                    if (d.name.IndexOf(nombreCamara, StringComparison.OrdinalIgnoreCase) >= 0) { elegido = d.name; break; }
            camara = new WebCamTexture(elegido, ancho, alto, 30);
            camara.Play();
            estado = "Cámara: " + elegido;
        }

        // ------------------------------------------------------------------ teclas
        void Update()
        {
            string linea;
            while (resultados.TryDequeue(out linea))
            {
                ocupado = false;
                if (entrada != null) entrada.EnviarTexto(linea);
            }
            if (ocupado) return;
            if (EntradaUtil.Pulsada(TeclaJuego.FotoBase)) GuardarBase();
            else if (EntradaUtil.Pulsada(TeclaJuego.DetectarJugada)) DetectarConWebcam();
        }

        bool GuardarFotoActual(string ruta)
        {
            if (camara == null || !camara.isPlaying || camara.width < 32) { estado = "La cámara no está lista"; return false; }
            var tex = new Texture2D(camara.width, camara.height, TextureFormat.RGB24, false);
            tex.SetPixels32(camara.GetPixels32());
            tex.Apply();
            File.WriteAllBytes(ruta, tex.EncodeToJPG(95));
            Destroy(tex);
            return true;
        }

        public void GuardarBase()
        {
            if (!GuardarFotoActual(fotoBase)) return;
            estado = "Foto base guardada. Haz tu jugada y pulsa G.";
            Debug.Log("[Ajedrez] " + estado + " (" + fotoBase + ")");
        }

        public void DetectarConWebcam()
        {
            if (!File.Exists(fotoBase)) { estado = "Primero toma la foto base (B) con el tablero en su posición actual"; return; }
            if (!GuardarFotoActual(fotoNueva)) return;
            Detectar(fotoBase, fotoNueva);
        }

        [ContextMenu("Probar con fotos")]
        void ProbarConFotos() { Detectar(fotoPruebaAntes, fotoPruebaDespues); }

        // ------------------------------------------------------------------ llamar al detector
        public void Detectar(string antes, string despues)
        {
            if (ocupado) return;
            ocupado = true;
            fotoEnEvaluacion = despues;
            estado = "Analizando el tablero… (unos segundos)";
            string py = rutaPython, script = rutaScript;
            int limite = segundosMaximos * 1000;
            Task.Run(() => resultados.Enqueue(EjecutarDetector(py, script, antes, despues, limite)));
        }

        /// <summary>Corre detector_ajedrez.py --completo y devuelve su línea JSON (o un JSON de error).</summary>
        static string EjecutarDetector(string python, string script, string antes, string despues, int limiteMs)
        {
            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = python,
                    Arguments = "\"" + script + "\" \"" + antes + "\" \"" + despues + "\" --completo",
                    WorkingDirectory = Path.GetDirectoryName(script),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                using (var p = Process.Start(info))
                {
                    var err = new StringBuilder();
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) err.AppendLine(e.Data); };
                    p.BeginErrorReadLine();
                    string salida = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(limiteMs)) { try { p.Kill(); } catch (Exception) { } return Error("El detector tardó demasiado"); }
                    salida = salida.Trim();
                    if (salida.Length == 0) return Error("El detector no devolvió nada: " + err);
                    // el JSON es la última línea de la salida estándar
                    int i = salida.LastIndexOf('\n');
                    return i >= 0 ? salida.Substring(i + 1).Trim() : salida;
                }
            }
            catch (Exception e)
            {
                return Error("No se pudo ejecutar Python (revisa rutaPython y rutaScript): " + e.Message);
            }
        }

        static string Error(string msg)
        {
            return "{\"pieza\": null, \"origen\": null, \"destino\": null, \"confianza\": 0.0, \"tipo\": \"error\", \"observacion\": \"" +
                   msg.Replace("\\", "/").Replace("\"", "'").Replace("\r", " ").Replace("\n", " ") + "\"}";
        }

        // ------------------------------------------------------------------ la jugada fue válida: la foto nueva pasa a ser la base
        void AlProcesar(DeteccionCamara d, ResultadoInterpretacion r, bool aplicada)
        {
            if (aplicada && !string.IsNullOrEmpty(fotoEnEvaluacion) && File.Exists(fotoEnEvaluacion))
            {
                try { File.Copy(fotoEnEvaluacion, fotoBase, true); estado = "Jugada aplicada. Foto base actualizada."; }
                catch (IOException e) { Debug.LogWarning("[Ajedrez] No se pudo actualizar la foto base: " + e.Message); }
            }
            else if (!aplicada) estado = "No se aplicó la jugada; la foto base sigue igual. Vuelve a pulsar G.";
        }

        // ------------------------------------------------------------------ vista previa
        void OnGUI()
        {
            if (!mostrarVistaPrevia) return;
            float w = Mathf.Min(260f, Screen.width * 0.2f);
            float x = 16f, y = 56f;
            if (camara != null && camara.isPlaying && camara.width > 32)
            {
                float h = w * camara.height / camara.width;
                GUI.DrawTexture(new Rect(x, y, w, h), camara, ScaleMode.ScaleToFit);
                y += h + 4f;
            }
            var estilo = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(11, Screen.height / 62), wordWrap = true };
            ControladorTablero.Sombra(new Rect(x, y, w + 120f, 60f), (ocupado ? "[...] " : "") + estado + "\nB: foto base · G: detectar jugada", estilo);
        }
    }
}
