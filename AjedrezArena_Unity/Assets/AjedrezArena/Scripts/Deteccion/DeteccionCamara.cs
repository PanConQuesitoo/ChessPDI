// =====================================================================
//  DeteccionCamara.cs  -  Traduce lo que "ve" la cámara a una jugada legal.
//
//  Este archivo NO usa UnityEngine: es lógica pura (así se puede probar fuera de Unity).
//
//  Qué entra:   {"pieza":"caballo","origen":"B1","destino":"C3","confianza":0.97}
//               (lo que devuelve detector_ajedrez.py; con --completo trae además
//                "tipo" y "observacion", que también se entienden)
//  Qué sale:    una Jugada legal para la partida en curso, o el motivo por el que no lo es.
//
//  Idea clave: la red neuronal NO distingue colores y a veces confunde piezas parecidas
//  (alfil/peón, rey/reina/torre), pero casi nunca falla en "qué casilla se vació y cuál
//  se llenó". Por eso la jugada se decide por las CASILLAS y se valida contra las reglas
//  del ajedrez; el nombre de la pieza solo se usa como verificación extra.
// =====================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AjedrezArena
{
    [Serializable]
    public class DeteccionCamara
    {
        public string pieza;          // rey, reina, torre, alfil, caballo, peon (sin color)
        public string origen;         // "B1"
        public string destino;        // "C3" (puede faltar en capturas de la misma clase)
        public float confianza;       // 0..1
        public string tipo;           // normal, captura, enroque, ... (solo con --completo)
        public string observacion;
        public string coronacion;     // opcional: en qué se convierte el peón ("reina" por defecto)

        public override string ToString()
        {
            return string.Format("{0} {1}->{2} ({3:0}%)", pieza ?? "?", origen ?? "?", destino ?? "?", confianza * 100f);
        }

        // ------------------------------------------------------------------ lectura de texto
        static readonly Regex ReCampoTexto = new Regex("\"(?<k>[A-Za-z_]+)\"\\s*:\\s*(?:null|\"(?<v>(?:[^\"\\\\]|\\\\.)*)\")", RegexOptions.Compiled);
        static readonly Regex ReConfianza = new Regex("\"confianza\"\\s*:\\s*(?<v>-?[0-9]+(?:\\.[0-9]+)?(?:[eE][+-]?[0-9]+)?)", RegexOptions.Compiled);
        static readonly Regex ReUci = new Regex("^\\s*([a-hA-H][1-8])\\s*[-: ]?\\s*([a-hA-H][1-8])\\s*([qrbnQRBN])?\\s*$", RegexOptions.Compiled);

        /// <summary>
        /// Acepta tres formatos:
        ///   1) JSON del detector:   {"pieza":"caballo","origen":"B1","destino":"C3","confianza":0.97}
        ///   2) Texto separado por ';':  M;caballo;B1;C3;0.97    (o sin la "M")
        ///   3) Jugada simple:        e2e4  /  e2-e4  /  e7e8q   (confianza 1; sirve para probar sin cámara)
        /// Devuelve null si el texto no se entiende.
        /// </summary>
        public static DeteccionCamara Parsear(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return null;
            texto = texto.Trim();
            var d = new DeteccionCamara { confianza = 0f };

            if (texto.StartsWith("{"))
            {
                foreach (Match m in ReCampoTexto.Matches(texto))
                {
                    string k = m.Groups["k"].Value;
                    string v = m.Groups["v"].Success ? m.Groups["v"].Value : null;
                    switch (k)
                    {
                        case "pieza": d.pieza = v; break;
                        case "origen": d.origen = v; break;
                        case "destino": d.destino = v; break;
                        case "tipo": d.tipo = v; break;
                        case "observacion": d.observacion = v; break;
                        case "coronacion": d.coronacion = v; break;
                    }
                }
                var mc = ReConfianza.Match(texto);
                if (mc.Success) float.TryParse(mc.Groups["v"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out d.confianza);
                return d;
            }

            var mu = ReUci.Match(texto);
            if (mu.Success)
            {
                d.origen = mu.Groups[1].Value.ToUpperInvariant();
                d.destino = mu.Groups[2].Value.ToUpperInvariant();
                d.confianza = 1f;
                if (mu.Groups[3].Success)
                    d.coronacion = NombreDePieza(mu.Groups[3].Value.ToLowerInvariant()[0]);
                return d;
            }

            var p = texto.Split(';');
            int i = (p.Length > 0 && (p[0].Trim() == "M" || p[0].Trim() == "m")) ? 1 : 0;
            if (p.Length - i >= 3)
            {
                d.pieza = p[i].Trim();
                d.origen = p[i + 1].Trim();
                d.destino = p[i + 2].Trim();
                if (p.Length - i >= 4)
                    float.TryParse(p[i + 3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d.confianza);
                else d.confianza = 1f;
                if (d.destino.Length == 0 || d.destino.ToLowerInvariant() == "null") d.destino = null;
                return d;
            }
            return null;
        }

        static string NombreDePieza(char letra)
        {
            switch (letra)
            {
                case 'q': return "reina";
                case 'r': return "torre";
                case 'b': return "alfil";
                case 'n': return "caballo";
                default: return null;
            }
        }
    }

    public enum EstadoInterpretacion
    {
        Aceptada,              // jugada legal y confianza suficiente: se puede aplicar
        NecesitaConfirmacion,  // jugada legal pero poco segura: pedir confirmación al jugador
        SinCambios,            // las dos fotos son iguales: nadie movió
        Rechazada              // no corresponde a una jugada legal (o no se pudo leer)
    }

    /// <summary>Que hacer cuando la mano virtual ya movio y llega lo que vio la camara.</summary>
    public enum DecisionConciliacion
    {
        Confirmar,      // la camara vio la misma jugada que hizo la mano
        Deshacer,       // el tablero real no cambio: la mano solto sin mover la pieza real
        Corregir,       // la camara vio OTRA jugada legal: se deshace la de la mano y se aplica la de la camara
        MantenerMano    // la camara no entendio el tablero: se confia en la mano
    }

    public class ResultadoInterpretacion
    {
        public EstadoInterpretacion Estado;
        public Jugada Jugada;
        public string Mensaje = "";
        public bool PiezaDiferente;   // la CNN dijo otra pieza distinta de la que hay en el origen
        public List<Jugada> Opciones = new List<Jugada>();   // si hay ambigüedad (captura de la misma clase)
        public bool TieneJugada { get { return Estado == EstadoInterpretacion.Aceptada || Estado == EstadoInterpretacion.NecesitaConfirmacion; } }
    }

    public static class InterpreteDeteccion
    {
        // ------------------------------------------------------------------ nombres
        static string Normalizar(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string d = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char c in d)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString();
        }

        public static TipoPieza ParsearPieza(string nombre)
        {
            switch (Normalizar(nombre))
            {
                case "peon": case "pawn": case "p": return TipoPieza.Peon;
                case "caballo": case "knight": case "n": case "c": return TipoPieza.Caballo;
                case "alfil": case "bishop": case "b": case "a": return TipoPieza.Alfil;
                case "torre": case "rook": case "r": case "t": return TipoPieza.Torre;
                case "reina": case "dama": case "queen": case "q": case "d": return TipoPieza.Dama;
                case "rey": case "king": case "k": return TipoPieza.Rey;
                default: return TipoPieza.Ninguna;
            }
        }

        public static string NombrePieza(TipoPieza t)
        {
            switch (t)
            {
                case TipoPieza.Peon: return "peón";
                case TipoPieza.Caballo: return "caballo";
                case TipoPieza.Alfil: return "alfil";
                case TipoPieza.Torre: return "torre";
                case TipoPieza.Dama: return "reina";
                case TipoPieza.Rey: return "rey";
                default: return "nada";
            }
        }

        /// <summary>"B1" o "b1" -> Casilla; Casilla.Ninguna si no es válida.</summary>
        public static Casilla ParsearCasilla(string texto)
        {
            if (string.IsNullOrEmpty(texto)) return Casilla.Ninguna;
            texto = texto.Trim().ToLowerInvariant();
            if (texto.Length != 2) return Casilla.Ninguna;
            var c = new Casilla(texto[0] - 'a', texto[1] - '1');
            return c.Valida ? c : Casilla.Ninguna;
        }

        static string Nombre(Casilla c) { return c.ToString().ToUpperInvariant(); }

        // ------------------------------------------------------------------ interpretación
        /// <param name="umbralConfianza">por debajo de este valor la jugada se acepta solo tras confirmarla</param>
        /// <param name="exigirPieza">si es true, que la CNN nombre una pieza distinta de la del origen invalida la jugada</param>
        public static ResultadoInterpretacion Interpretar(PartidaAjedrez partida, DeteccionCamara d,
                                                          float umbralConfianza = 0.7f, bool exigirPieza = false)
        {
            var r = new ResultadoInterpretacion { Estado = EstadoInterpretacion.Rechazada };
            if (partida == null) { r.Mensaje = "No hay partida en curso"; return r; }
            if (d == null) { r.Mensaje = "Detección vacía"; return r; }
            if (partida.Terminada) { r.Mensaje = "La partida ya terminó (R para reiniciar)"; return r; }

            string tipo = Normalizar(d.tipo);
            if (tipo == "sin_cambios") { r.Estado = EstadoInterpretacion.SinCambios; r.Mensaje = "No se detectó ninguna jugada"; return r; }
            if (tipo == "error")
            {
                r.Mensaje = string.IsNullOrEmpty(d.observacion) ? "El detector no pudo leer el tablero" : d.observacion;
                return r;
            }

            Casilla desde = ParsearCasilla(d.origen);
            if (!desde.Valida)
            {
                r.Mensaje = string.IsNullOrEmpty(d.observacion) ? "No se pudo determinar la jugada: pide otra foto" : d.observacion;
                return r;
            }

            Pieza enOrigen = partida.En(desde);
            if (enOrigen.Vacia) { r.Mensaje = "En " + Nombre(desde) + " no hay ninguna pieza"; return r; }
            if (enOrigen.Bando != partida.Turno)
            {
                r.Mensaje = "Ahora mueven las " + (partida.Turno == Bando.Blancas ? "blancas" : "negras") +
                            " y en " + Nombre(desde) + " hay una pieza " + (enOrigen.Bando == Bando.Blancas ? "blanca" : "negra");
                return r;
            }

            TipoPieza vista = ParsearPieza(d.pieza);
            Casilla hasta = ParsearCasilla(d.destino);

            // ---- destino desconocido: captura de una pieza de la misma clase (sin color la CNN no ve el cambio)
            if (!hasta.Valida)
            {
                var opciones = new List<Jugada>();
                foreach (var j in partida.JugadasLegales(desde))
                {
                    if (!j.EsCaptura) continue;
                    if (vista != TipoPieza.Ninguna && partida.En(j.CasillaCapturada).Tipo != vista) continue;
                    if (j.Coronacion != TipoPieza.Ninguna && j.Coronacion != TipoPieza.Dama) continue;
                    opciones.Add(j);
                }
                r.Opciones = opciones;
                if (opciones.Count == 1)
                {
                    r.Jugada = opciones[0];
                    r.Mensaje = "Captura deducida: " + Nombre(desde) + " x " + Nombre(opciones[0].Hasta);
                    r.Estado = d.confianza >= umbralConfianza ? EstadoInterpretacion.Aceptada : EstadoInterpretacion.NecesitaConfirmacion;
                    r.PiezaDiferente = vista != TipoPieza.Ninguna && vista != enOrigen.Tipo;
                    return r;
                }
                if (opciones.Count == 0) r.Mensaje = "No hay ninguna captura posible desde " + Nombre(desde);
                else
                {
                    var sb = new StringBuilder("Captura ambigua desde " + Nombre(desde) + ": puede ser ");
                    for (int i = 0; i < opciones.Count; i++) sb.Append(i > 0 ? (i == opciones.Count - 1 ? " o " : ", ") : "").Append(Nombre(opciones[i].Hasta));
                    r.Mensaje = sb.ToString();
                }
                return r;
            }

            // ---- caso normal: origen y destino conocidos
            TipoPieza corona = TipoPieza.Dama;
            if (!string.IsNullOrEmpty(d.coronacion))
            {
                var t = ParsearPieza(d.coronacion);
                if (t != TipoPieza.Ninguna && t != TipoPieza.Rey && t != TipoPieza.Peon) corona = t;
            }
            Jugada jugada;
            if (!partida.BuscarJugada(desde, hasta, out jugada, corona))
            {
                r.Mensaje = "Jugada no legal: " + NombrePieza(enOrigen.Tipo) + " " + Nombre(desde) + " → " + Nombre(hasta);
                return r;
            }

            r.Jugada = jugada;
            r.PiezaDiferente = vista != TipoPieza.Ninguna && vista != enOrigen.Tipo;
            if (r.PiezaDiferente && exigirPieza)
            {
                r.Mensaje = "La cámara vio " + NombrePieza(vista) + " pero en " + Nombre(desde) + " hay " + NombrePieza(enOrigen.Tipo);
                return r;
            }
            r.Mensaje = NombrePieza(enOrigen.Tipo) + " " + Nombre(desde) + " → " + Nombre(hasta);
            if (r.PiezaDiferente) r.Mensaje += " (la red dijo " + NombrePieza(vista) + ")";
            r.Estado = d.confianza >= umbralConfianza ? EstadoInterpretacion.Aceptada : EstadoInterpretacion.NecesitaConfirmacion;
            return r;
        }
    
        /// <summary>
        /// La camara compara el tablero REAL antes/despues de la jugada, asi que su deteccion se interpreta
        /// desde la ultima posicion que ella confirmo (fenConfirmado), no desde lo que ya movio la mano.
        /// </summary>
        public static DecisionConciliacion Conciliar(string fenConfirmado, IList<Jugada> jugadasMano, DeteccionCamara d,
                                                     float umbralConfianza, bool exigirPieza, out ResultadoInterpretacion r)
        {
            r = Interpretar(new PartidaAjedrez(fenConfirmado), d, umbralConfianza, exigirPieza);
            if (r.Estado == EstadoInterpretacion.SinCambios) return DecisionConciliacion.Deshacer;
            if (!r.TieneJugada) return DecisionConciliacion.MantenerMano;
            if (jugadasMano != null && jugadasMano.Count == 1)
            {
                var jm = jugadasMano[0];
                if (r.Jugada.Desde == jm.Desde && r.Jugada.Hasta == jm.Hasta) return DecisionConciliacion.Confirmar;
            }
            return DecisionConciliacion.Corregir;
        }
    }
}
