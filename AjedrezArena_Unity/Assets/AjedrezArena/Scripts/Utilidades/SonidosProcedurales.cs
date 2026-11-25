// =====================================================================
//  SonidosProcedurales.cs  -  Genera los efectos de sonido por código
//  (síntesis simple: senos + ruido + envolventes). Así el proyecto suena
//  sin necesidad de importar audios. Puedes reemplazarlos asignando tus
//  propios AudioClip en EfectosAjedrez.
// =====================================================================
using UnityEngine;

namespace AjedrezArena
{
    public static class SonidosProcedurales
    {
        const int Frecuencia = 44100;
        static System.Random rnd = new System.Random(1234);

        static float Ruido() { return (float)(rnd.NextDouble() * 2.0 - 1.0); }

        static AudioClip Crear(string nombre, float segundos, System.Func<float, float> muestra)
        {
            int n = Mathf.CeilToInt(segundos * Frecuencia);
            var datos = new float[n];
            float pico = 0.0001f;
            for (int i = 0; i < n; i++)
            {
                datos[i] = muestra(i / (float)Frecuencia);
                pico = Mathf.Max(pico, Mathf.Abs(datos[i]));
            }
            for (int i = 0; i < n; i++) datos[i] = datos[i] / pico * 0.9f;
            var clip = AudioClip.Create(nombre, n, 1, Frecuencia, false);
            clip.SetData(datos, 0);
            return clip;
        }

        /// <summary>Golpe grave en el tablero (seno que cae de 140 a 45 Hz + chasquido).</summary>
        public static AudioClip Golpe()
        {
            float fase = 0f;
            return Crear("Golpe", 0.6f, t =>
            {
                float f = Mathf.Lerp(140f, 45f, Mathf.Clamp01(t * 4f));
                fase += 2f * Mathf.PI * f / Frecuencia;
                float cuerpo = Mathf.Sin(fase) * Mathf.Exp(-t * 9f);
                float clic = Ruido() * Mathf.Exp(-t * 80f) * 0.6f;
                return cuerpo + clic;
            });
        }

        /// <summary>Llamarada: ruido filtrado con envolvente.</summary>
        public static AudioClip Fuego()
        {
            float filtro = 0f;
            return Crear("Fuego", 1.1f, t =>
            {
                filtro = Mathf.Lerp(filtro, Ruido(), 0.08f);
                float chispa = (rnd.NextDouble() < 0.0015) ? Ruido() * 0.8f : 0f;
                float env = Mathf.Clamp01(t * 20f) * Mathf.Exp(-t * 2.8f);
                return (filtro * 3f + chispa) * env;
            });
        }

        /// <summary>Rayo: chasquido brillante + retumbo.</summary>
        public static AudioClip Rayo()
        {
            float grave = 0f, fase = 0f;
            return Crear("Rayo", 1.6f, t =>
            {
                grave = Mathf.Lerp(grave, Ruido(), 0.02f);
                fase += 2f * Mathf.PI * 55f / Frecuencia;
                float crack = Ruido() * Mathf.Exp(-t * 18f);
                float zumbido = Mathf.Sign(Mathf.Sin(fase * 2f)) * 0.15f * Mathf.Exp(-t * 10f);
                float retumbo = grave * 6f * Mathf.Exp(-t * 2.2f) * Mathf.Clamp01(t * 8f);
                return crack + zumbido + retumbo;
            });
        }

        /// <summary>Destello de luz: campanita (acorde de senos).</summary>
        public static AudioClip Luz()
        {
            return Crear("Luz", 1.4f, t =>
            {
                float s = Mathf.Sin(2f * Mathf.PI * 880f * t) + 0.6f * Mathf.Sin(2f * Mathf.PI * 1318.5f * t) +
                          0.4f * Mathf.Sin(2f * Mathf.PI * 1760f * t) + 0.25f * Mathf.Sin(2f * Mathf.PI * 2637f * t);
                return s * Mathf.Exp(-t * 3.5f) * Mathf.Clamp01(t * 200f);
            });
        }

        /// <summary>Supernova: barrido ascendente + explosión grave.</summary>
        public static AudioClip Supernova()
        {
            float fase = 0f, filtro = 0f;
            return Crear("Supernova", 2.2f, t =>
            {
                float f = Mathf.Lerp(60f, 30f, Mathf.Clamp01(t));
                fase += 2f * Mathf.PI * f / Frecuencia;
                filtro = Mathf.Lerp(filtro, Ruido(), 0.05f);
                float boom = Mathf.Sin(fase) * Mathf.Exp(-t * 1.8f);
                float aire = filtro * 2.5f * Mathf.Exp(-t * 1.2f);
                float brillo = Mathf.Sin(2f * Mathf.PI * (900f + 600f * t) * t) * 0.2f * Mathf.Exp(-t * 2f);
                return boom + aire + brillo;
            });
        }

        /// <summary>Agarrar / soltar: "pop" corto.</summary>
        public static AudioClip Pop(float tono = 1f)
        {
            float fase = 0f;
            return Crear("Pop", 0.12f, t =>
            {
                float f = Mathf.Lerp(900f, 300f, t / 0.12f) * tono;
                fase += 2f * Mathf.PI * f / Frecuencia;
                return Mathf.Sin(fase) * Mathf.Exp(-t * 40f);
            });
        }

        /// <summary>Carga de energía: zumbido que sube (se reproduce en bucle con pitch variable).</summary>
        public static AudioClip Carga()
        {
            float fase = 0f;
            return Crear("Carga", 1.0f, t =>
            {
                fase += 2f * Mathf.PI * 220f / Frecuencia;
                // bucle perfecto: 220 Hz durante 1 s = ciclos enteros
                return (Mathf.Sin(fase) + 0.5f * Mathf.Sin(fase * 2.01f) + 0.3f * Mathf.Sin(fase * 3f)) * 0.5f;
            });
        }

        /// <summary>Error (jugada ilegal): dos tonos descendentes.</summary>
        public static AudioClip Error()
        {
            return Crear("Error", 0.35f, t =>
            {
                float f = t < 0.15f ? 330f : 220f;
                return Mathf.Sign(Mathf.Sin(2f * Mathf.PI * f * t)) * 0.4f * Mathf.Exp(-(t % 0.17f) * 12f);
            });
        }
    }
}
