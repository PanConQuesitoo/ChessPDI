// =====================================================================
//  TemaPiezas.cs  -  Un "juego temático": modelos del tablero y de las 6
//  piezas + colores de cada bando. Cada escenario tiene el suyo
//  (p. ej. Giza: Anubis, obeliscos, ankh; Museo del Oro: tunjos, jaguar).
// =====================================================================
using System;
using UnityEngine;

namespace AjedrezArena
{
    [Serializable]
    public class ModelosTema
    {
        public string nombre;
        public GameObject tablero, peon, torre, caballo, alfil, dama, rey;
        public GameObject mano;
    }

    [Serializable]
    public class TemaPiezas
    {
        public string nombre;
        public ModelosTema modelos = new ModelosTema();
        public Color blancas = new Color(0.93f, 0.9f, 0.82f);
        public Color negras = new Color(0.09f, 0.09f, 0.12f);
        public Color acentoBlancas = new Color(0.83f, 0.69f, 0.22f);
        public Color acentoNegras = new Color(0.83f, 0.69f, 0.22f);
        [Range(0f, 1f)] public float metalBlancas = 0f;
        [Range(0f, 1f)] public float metalNegras = 0.2f;
        [Range(0f, 1f)] public float suavidad = 0.7f;
        [Tooltip("El acento brilla (neón, hielo)")]
        public bool acentoEmisivo = false;

        public TemaPiezas() { }
        public TemaPiezas(string nombre, string b, string n, string ab, string an, float mb, float mn, float suav = 0.7f, bool emisivo = false)
        {
            this.nombre = nombre;
            modelos.nombre = nombre;
            blancas = Hex(b); negras = Hex(n); acentoBlancas = Hex(ab); acentoNegras = Hex(an);
            metalBlancas = mb; metalNegras = mn; suavidad = suav; acentoEmisivo = emisivo;
        }

        public static Color Hex(string h)
        {
            Color c;
            return ColorUtility.TryParseHtmlString(h.StartsWith("#") ? h : "#" + h, out c) ? c : Color.magenta;
        }
    }
}
