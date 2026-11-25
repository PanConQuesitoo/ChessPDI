// =====================================================================
//  EntradaManoUDP.cs  -  Recibe la mano detectada por Python (cámara/red neuronal) vía UDP.
//  Formato: "B;x;y;g" (tablero 0..1) o "V;x;y;g" (pantalla 0..1). Ver FuenteEntradaMano.cs.
// =====================================================================
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

namespace AjedrezArena
{
    // ------------------------------------------------------------------ UDP
    public class EntradaManoUDP : FuenteEntradaMano
    {
        [Tooltip("Puerto donde escucha Unity. El script de Python debe enviar a este puerto.")]
        public int puerto = 5065;
        [Tooltip("La imagen de la cámara tiene el origen arriba; Unity abajo.")]
        public bool invertirY = true;
        [Tooltip("Espejo horizontal (útil con la cámara frontal del celular).")]
        public bool espejoX = false;
        [Tooltip("Segundos sin datos para considerar que no hay mano.")]
        public float tiempoEspera = 0.5f;
        [Tooltip("Suavizado (0 = nada). Reduce el temblor de la detección.")]
        [Range(0f, 0.95f)] public float suavizado = 0.5f;

        UdpClient cliente;
        Thread hilo;
        volatile bool corriendo;
        readonly object cerrojo = new object();
        Vector2 ultimaPos, posSuave;
        bool ultimoAgarre;
        EspacioCoordenadas ultimoEspacio = EspacioCoordenadas.Pantalla;
        long ultimoTicks;
        int paquetes;

        public override bool Activa
        {
            get
            {
                long t;
                lock (cerrojo) t = ultimoTicks;
                return t != 0 && (DateTime.UtcNow.Ticks - t) / (double)TimeSpan.TicksPerSecond < tiempoEspera;
            }
        }
        public override EspacioCoordenadas Espacio { get { lock (cerrojo) return ultimoEspacio; } }
        public override Vector2 Posicion { get { return posSuave; } }
        public override bool Agarrando { get { lock (cerrojo) return ultimoAgarre; } }
        public override string Nombre { get { return "UDP:" + puerto + " (" + paquetes + " paquetes)"; } }

        void OnEnable()
        {
            try
            {
                cliente = new UdpClient(puerto);
                corriendo = true;
                hilo = new Thread(Escuchar) { IsBackground = true, Name = "AjedrezUDP" };
                hilo.Start();
                Debug.Log("[Ajedrez] Escuchando la mano por UDP en el puerto " + puerto);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Ajedrez] No se pudo abrir el puerto UDP " + puerto + ": " + e.Message);
            }
        }

        void OnDisable()
        {
            corriendo = false;
            try { if (cliente != null) cliente.Close(); } catch (Exception) { }
            cliente = null;
            if (hilo != null && hilo.IsAlive) hilo.Join(200);
            hilo = null;
        }

        void Escuchar()
        {
            var remoto = new IPEndPoint(IPAddress.Any, 0);
            while (corriendo)
            {
                try
                {
                    byte[] datos = cliente.Receive(ref remoto);
                    Interpretar(System.Text.Encoding.ASCII.GetString(datos));
                }
                catch (SocketException) { if (!corriendo) break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception e) { Debug.LogWarning("[Ajedrez] UDP: " + e.Message); }
            }
        }

        void Interpretar(string texto)
        {
            var partes = texto.Trim().Split(';');
            if (partes.Length < 3) return;
            int i = 0;
            var esp = EspacioCoordenadas.Pantalla;
            if (partes[0] == "B" || partes[0] == "V") { esp = partes[0] == "B" ? EspacioCoordenadas.Tablero : EspacioCoordenadas.Pantalla; i = 1; }
            float x, y;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            if (!float.TryParse(partes[i], System.Globalization.NumberStyles.Float, ci, out x)) return;
            if (!float.TryParse(partes[i + 1], System.Globalization.NumberStyles.Float, ci, out y)) return;
            bool g = partes.Length > i + 2 && (partes[i + 2].Trim() == "1" || partes[i + 2].Trim().ToLowerInvariant() == "true");
            lock (cerrojo)
            {
                ultimoEspacio = esp;
                ultimaPos = new Vector2(x, y);
                ultimoAgarre = g;
                ultimoTicks = DateTime.UtcNow.Ticks;
                paquetes++;
            }
        }

        void Update()
        {
            Vector2 p; EspacioCoordenadas esp;
            lock (cerrojo) { p = ultimaPos; esp = ultimoEspacio; }
            if (esp == EspacioCoordenadas.Pantalla && invertirY) p.y = 1f - p.y;
            if (espejoX) p.x = 1f - p.x;
            float k = 1f - Mathf.Pow(suavizado, Time.deltaTime * 60f);
            posSuave = Vector2.Lerp(posSuave, p, suavizado <= 0f ? 1f : k);
        }
    }
}
