// =====================================================================
//  ReglasAjedrez.cs  -  Lógica pura de ajedrez (sin dependencias de Unity)
//  Incluye: movimientos legales, jaque, jaque mate, ahogado, enroque,
//  captura al paso, coronación, regla de 50 jugadas y carga de FEN.
//  Se puede probar fuera de Unity (ver Pruebas/PerftPrueba.cs).
// =====================================================================
using System;
using System.Collections.Generic;
using System.Text;

namespace AjedrezArena
{
    public enum TipoPieza { Ninguna = 0, Peon, Caballo, Alfil, Torre, Dama, Rey }
    public enum Bando { Blancas = 0, Negras = 1 }

    public enum EstadoPartida { EnJuego, Jaque, JaqueMate, Ahogado, TablasCincuenta, TablasMaterial }

    [Serializable]
    public struct Pieza
    {
        public TipoPieza Tipo;
        public Bando Bando;
        public Pieza(TipoPieza tipo, Bando bando) { Tipo = tipo; Bando = bando; }
        public bool Vacia { get { return Tipo == TipoPieza.Ninguna; } }
        public static readonly Pieza Nada = new Pieza(TipoPieza.Ninguna, Bando.Blancas);
        public override string ToString() { return Vacia ? "." : (Bando == Bando.Blancas ? Letra(Tipo).ToUpperInvariant() : Letra(Tipo)); }
        public static string Letra(TipoPieza t)
        {
            switch (t)
            {
                case TipoPieza.Peon: return "p";
                case TipoPieza.Caballo: return "n";
                case TipoPieza.Alfil: return "b";
                case TipoPieza.Torre: return "r";
                case TipoPieza.Dama: return "q";
                case TipoPieza.Rey: return "k";
                default: return ".";
            }
        }
    }

    /// <summary>Casilla del tablero: Columna 0..7 = a..h, Fila 0..7 = 1..8.</summary>
    [Serializable]
    public struct Casilla : IEquatable<Casilla>
    {
        public int Columna;
        public int Fila;
        public Casilla(int columna, int fila) { Columna = columna; Fila = fila; }
        public bool Valida { get { return Columna >= 0 && Columna < 8 && Fila >= 0 && Fila < 8; } }
        public bool Equals(Casilla o) { return Columna == o.Columna && Fila == o.Fila; }
        public override bool Equals(object obj) { return obj is Casilla && Equals((Casilla)obj); }
        public override int GetHashCode() { return Columna * 8 + Fila; }
        public static bool operator ==(Casilla a, Casilla b) { return a.Equals(b); }
        public static bool operator !=(Casilla a, Casilla b) { return !a.Equals(b); }
        public override string ToString() { return Valida ? ((char)('a' + Columna)).ToString() + (Fila + 1) : "--"; }
        public static readonly Casilla Ninguna = new Casilla(-1, -1);

        public static Casilla Desde(string texto)
        {
            if (string.IsNullOrEmpty(texto) || texto.Length < 2) return Ninguna;
            return new Casilla(texto[0] - 'a', texto[1] - '1');
        }
    }

    [Serializable]
    public struct Jugada
    {
        public Casilla Desde;
        public Casilla Hasta;
        public TipoPieza Coronacion;     // Ninguna si no corona
        public bool EsEnroque;
        public bool EsAlPaso;
        public bool EsCaptura;
        public Casilla CasillaCapturada; // distinta de Hasta en la captura al paso
        public Casilla TorreDesde;       // solo enroque
        public Casilla TorreHasta;       // solo enroque

        public override string ToString()
        {
            string s = Desde.ToString() + Hasta.ToString();
            if (Coronacion != TipoPieza.Ninguna) s += Pieza.Letra(Coronacion);
            return s;
        }
    }

    /// <summary>Resultado de aplicar una jugada (para la parte visual).</summary>
    public struct ResultadoJugada
    {
        public Jugada Jugada;
        public Pieza PiezaMovida;
        public Pieza PiezaCapturada;
        public EstadoPartida EstadoDespues;
        public Bando TurnoDespues;
    }

    public class PartidaAjedrez
    {
        public const string FenInicial = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

        readonly Pieza[,] tablero = new Pieza[8, 8]; // [columna, fila]
        public Bando Turno { get; private set; }
        // derechos de enroque: 0 = blancas corto, 1 = blancas largo, 2 = negras corto, 3 = negras largo
        readonly bool[] enroque = new bool[4];
        public Casilla AlPaso { get; private set; } // casilla destino de captura al paso o Ninguna
        public int MediasJugadasSinCaptura { get; private set; }
        public int NumeroJugada { get; private set; }
        public EstadoPartida Estado { get; private set; }
        public readonly List<string> Historial = new List<string>();

        static readonly int[,] DirCaballo = { { 1, 2 }, { 2, 1 }, { 2, -1 }, { 1, -2 }, { -1, -2 }, { -2, -1 }, { -2, 1 }, { -1, 2 } };
        static readonly int[,] DirRey = { { 1, 0 }, { 1, 1 }, { 0, 1 }, { -1, 1 }, { -1, 0 }, { -1, -1 }, { 0, -1 }, { 1, -1 } };
        static readonly int[,] DirAlfil = { { 1, 1 }, { -1, 1 }, { -1, -1 }, { 1, -1 } };
        static readonly int[,] DirTorre = { { 1, 0 }, { 0, 1 }, { -1, 0 }, { 0, -1 } };

        public PartidaAjedrez() { CargarFen(FenInicial); }
        public PartidaAjedrez(string fen) { CargarFen(fen); }

        public Pieza En(Casilla c) { return c.Valida ? tablero[c.Columna, c.Fila] : Pieza.Nada; }
        public Pieza En(int col, int fila) { return tablero[col, fila]; }

        // --------------------------------------------------------- FEN
        public void CargarFen(string fen)
        {
            Array.Clear(tablero, 0, tablero.Length);
            for (int c = 0; c < 8; c++) for (int f = 0; f < 8; f++) tablero[c, f] = Pieza.Nada;
            string[] partes = fen.Trim().Split(' ');
            int fila = 7, col = 0;
            foreach (char ch in partes[0])
            {
                if (ch == '/') { fila--; col = 0; continue; }
                if (char.IsDigit(ch)) { col += ch - '0'; continue; }
                Bando b = char.IsUpper(ch) ? Bando.Blancas : Bando.Negras;
                TipoPieza t;
                switch (char.ToLowerInvariant(ch))
                {
                    case 'p': t = TipoPieza.Peon; break;
                    case 'n': t = TipoPieza.Caballo; break;
                    case 'b': t = TipoPieza.Alfil; break;
                    case 'r': t = TipoPieza.Torre; break;
                    case 'q': t = TipoPieza.Dama; break;
                    default: t = TipoPieza.Rey; break;
                }
                tablero[col, fila] = new Pieza(t, b);
                col++;
            }
            Turno = (partes.Length > 1 && partes[1] == "b") ? Bando.Negras : Bando.Blancas;
            string e = partes.Length > 2 ? partes[2] : "-";
            enroque[0] = e.Contains("K"); enroque[1] = e.Contains("Q");
            enroque[2] = e.Contains("k"); enroque[3] = e.Contains("q");
            AlPaso = (partes.Length > 3 && partes[3] != "-") ? Casilla.Desde(partes[3]) : Casilla.Ninguna;
            int n;
            MediasJugadasSinCaptura = (partes.Length > 4 && int.TryParse(partes[4], out n)) ? n : 0;
            NumeroJugada = (partes.Length > 5 && int.TryParse(partes[5], out n)) ? n : 1;
            Historial.Clear();
            Estado = CalcularEstado();
        }

        public string Fen()
        {
            var sb = new StringBuilder();
            for (int f = 7; f >= 0; f--)
            {
                int vacias = 0;
                for (int c = 0; c < 8; c++)
                {
                    Pieza p = tablero[c, f];
                    if (p.Vacia) { vacias++; continue; }
                    if (vacias > 0) { sb.Append(vacias); vacias = 0; }
                    sb.Append(p.ToString());
                }
                if (vacias > 0) sb.Append(vacias);
                if (f > 0) sb.Append('/');
            }
            sb.Append(Turno == Bando.Blancas ? " w " : " b ");
            string e = (enroque[0] ? "K" : "") + (enroque[1] ? "Q" : "") + (enroque[2] ? "k" : "") + (enroque[3] ? "q" : "");
            sb.Append(e.Length == 0 ? "-" : e);
            sb.Append(' ').Append(AlPaso.Valida ? AlPaso.ToString() : "-");
            sb.Append(' ').Append(MediasJugadasSinCaptura).Append(' ').Append(NumeroJugada);
            return sb.ToString();
        }

        // --------------------------------------------------------- Ataques
        public bool CasillaAtacada(Casilla c, Bando atacante)
        {
            int col = c.Columna, fila = c.Fila;
            // peones
            int dirPeon = atacante == Bando.Blancas ? -1 : 1; // desde la casilla, mirar hacia donde viene el peón
            for (int dc = -1; dc <= 1; dc += 2)
            {
                int cc = col + dc, ff = fila + dirPeon;
                if (Dentro(cc, ff)) { Pieza p = tablero[cc, ff]; if (p.Tipo == TipoPieza.Peon && p.Bando == atacante) return true; }
            }
            for (int i = 0; i < 8; i++)
            {
                int cc = col + DirCaballo[i, 0], ff = fila + DirCaballo[i, 1];
                if (Dentro(cc, ff)) { Pieza p = tablero[cc, ff]; if (p.Tipo == TipoPieza.Caballo && p.Bando == atacante) return true; }
                cc = col + DirRey[i, 0]; ff = fila + DirRey[i, 1];
                if (Dentro(cc, ff)) { Pieza p = tablero[cc, ff]; if (p.Tipo == TipoPieza.Rey && p.Bando == atacante) return true; }
            }
            if (Deslizante(col, fila, DirAlfil, atacante, TipoPieza.Alfil)) return true;
            if (Deslizante(col, fila, DirTorre, atacante, TipoPieza.Torre)) return true;
            return false;
        }

        bool Deslizante(int col, int fila, int[,] dirs, Bando atacante, TipoPieza tipo)
        {
            for (int d = 0; d < 4; d++)
            {
                int cc = col + dirs[d, 0], ff = fila + dirs[d, 1];
                while (Dentro(cc, ff))
                {
                    Pieza p = tablero[cc, ff];
                    if (!p.Vacia)
                    {
                        if (p.Bando == atacante && (p.Tipo == tipo || p.Tipo == TipoPieza.Dama)) return true;
                        break;
                    }
                    cc += dirs[d, 0]; ff += dirs[d, 1];
                }
            }
            return false;
        }

        public Casilla PosicionRey(Bando b)
        {
            for (int c = 0; c < 8; c++)
                for (int f = 0; f < 8; f++)
                    if (tablero[c, f].Tipo == TipoPieza.Rey && tablero[c, f].Bando == b) return new Casilla(c, f);
            return Casilla.Ninguna;
        }

        public bool EnJaque(Bando b)
        {
            Casilla r = PosicionRey(b);
            return r.Valida && CasillaAtacada(r, Contrario(b));
        }

        public static Bando Contrario(Bando b) { return b == Bando.Blancas ? Bando.Negras : Bando.Blancas; }
        static bool Dentro(int c, int f) { return c >= 0 && c < 8 && f >= 0 && f < 8; }

        // --------------------------------------------------------- Generación
        void Agregar(List<Jugada> lista, Casilla desde, Casilla hasta, bool captura)
        {
            lista.Add(new Jugada { Desde = desde, Hasta = hasta, EsCaptura = captura, CasillaCapturada = captura ? hasta : Casilla.Ninguna, Coronacion = TipoPieza.Ninguna });
        }

        void AgregarPeon(List<Jugada> lista, Casilla desde, Casilla hasta, bool captura, int filaCoronacion)
        {
            if (hasta.Fila == filaCoronacion)
            {
                TipoPieza[] opciones = { TipoPieza.Dama, TipoPieza.Torre, TipoPieza.Alfil, TipoPieza.Caballo };
                foreach (var t in opciones)
                    lista.Add(new Jugada { Desde = desde, Hasta = hasta, EsCaptura = captura, CasillaCapturada = captura ? hasta : Casilla.Ninguna, Coronacion = t });
            }
            else Agregar(lista, desde, hasta, captura);
        }

        void PseudoLegales(Casilla desde, List<Jugada> lista)
        {
            Pieza p = En(desde);
            if (p.Vacia) return;
            int c = desde.Columna, f = desde.Fila;
            Bando yo = p.Bando;
            switch (p.Tipo)
            {
                case TipoPieza.Peon:
                    {
                        int dir = yo == Bando.Blancas ? 1 : -1;
                        int filaInicio = yo == Bando.Blancas ? 1 : 6;
                        int filaCor = yo == Bando.Blancas ? 7 : 0;
                        if (Dentro(c, f + dir) && tablero[c, f + dir].Vacia)
                        {
                            AgregarPeon(lista, desde, new Casilla(c, f + dir), false, filaCor);
                            if (f == filaInicio && tablero[c, f + 2 * dir].Vacia)
                                Agregar(lista, desde, new Casilla(c, f + 2 * dir), false);
                        }
                        for (int dc = -1; dc <= 1; dc += 2)
                        {
                            int cc = c + dc, ff = f + dir;
                            if (!Dentro(cc, ff)) continue;
                            Pieza o = tablero[cc, ff];
                            if (!o.Vacia && o.Bando != yo) AgregarPeon(lista, desde, new Casilla(cc, ff), true, filaCor);
                            else if (AlPaso.Valida && AlPaso.Columna == cc && AlPaso.Fila == ff)
                            {
                                lista.Add(new Jugada
                                {
                                    Desde = desde, Hasta = new Casilla(cc, ff), EsCaptura = true, EsAlPaso = true,
                                    CasillaCapturada = new Casilla(cc, f), Coronacion = TipoPieza.Ninguna
                                });
                            }
                        }
                        break;
                    }
                case TipoPieza.Caballo:
                    for (int i = 0; i < 8; i++) Salto(lista, desde, c + DirCaballo[i, 0], f + DirCaballo[i, 1], yo);
                    break;
                case TipoPieza.Rey:
                    for (int i = 0; i < 8; i++) Salto(lista, desde, c + DirRey[i, 0], f + DirRey[i, 1], yo);
                    Enroques(lista, desde, yo);
                    break;
                case TipoPieza.Alfil: Deslizar(lista, desde, DirAlfil, yo); break;
                case TipoPieza.Torre: Deslizar(lista, desde, DirTorre, yo); break;
                case TipoPieza.Dama: Deslizar(lista, desde, DirAlfil, yo); Deslizar(lista, desde, DirTorre, yo); break;
            }
        }

        void Salto(List<Jugada> lista, Casilla desde, int c, int f, Bando yo)
        {
            if (!Dentro(c, f)) return;
            Pieza o = tablero[c, f];
            if (o.Vacia) Agregar(lista, desde, new Casilla(c, f), false);
            else if (o.Bando != yo) Agregar(lista, desde, new Casilla(c, f), true);
        }

        void Deslizar(List<Jugada> lista, Casilla desde, int[,] dirs, Bando yo)
        {
            for (int d = 0; d < 4; d++)
            {
                int c = desde.Columna + dirs[d, 0], f = desde.Fila + dirs[d, 1];
                while (Dentro(c, f))
                {
                    Pieza o = tablero[c, f];
                    if (o.Vacia) Agregar(lista, desde, new Casilla(c, f), false);
                    else { if (o.Bando != yo) Agregar(lista, desde, new Casilla(c, f), true); break; }
                    c += dirs[d, 0]; f += dirs[d, 1];
                }
            }
        }

        void Enroques(List<Jugada> lista, Casilla desde, Bando yo)
        {
            int fila = yo == Bando.Blancas ? 0 : 7;
            if (desde.Columna != 4 || desde.Fila != fila) return;
            Bando rival = Contrario(yo);
            int idxCorto = yo == Bando.Blancas ? 0 : 2, idxLargo = yo == Bando.Blancas ? 1 : 3;
            if (CasillaAtacada(desde, rival)) return;
            Pieza torreCorta = tablero[7, fila], torreLarga = tablero[0, fila];
            if (enroque[idxCorto] && torreCorta.Tipo == TipoPieza.Torre && torreCorta.Bando == yo &&
                tablero[5, fila].Vacia && tablero[6, fila].Vacia &&
                !CasillaAtacada(new Casilla(5, fila), rival) && !CasillaAtacada(new Casilla(6, fila), rival))
            {
                lista.Add(new Jugada
                {
                    Desde = desde, Hasta = new Casilla(6, fila), EsEnroque = true, Coronacion = TipoPieza.Ninguna,
                    CasillaCapturada = Casilla.Ninguna, TorreDesde = new Casilla(7, fila), TorreHasta = new Casilla(5, fila)
                });
            }
            if (enroque[idxLargo] && torreLarga.Tipo == TipoPieza.Torre && torreLarga.Bando == yo &&
                tablero[1, fila].Vacia && tablero[2, fila].Vacia && tablero[3, fila].Vacia &&
                !CasillaAtacada(new Casilla(3, fila), rival) && !CasillaAtacada(new Casilla(2, fila), rival))
            {
                lista.Add(new Jugada
                {
                    Desde = desde, Hasta = new Casilla(2, fila), EsEnroque = true, Coronacion = TipoPieza.Ninguna,
                    CasillaCapturada = Casilla.Ninguna, TorreDesde = new Casilla(0, fila), TorreHasta = new Casilla(3, fila)
                });
            }
        }

        /// <summary>Jugadas legales de la pieza en 'desde' (solo si es su turno).</summary>
        public List<Jugada> JugadasLegales(Casilla desde)
        {
            var resultado = new List<Jugada>();
            Pieza p = En(desde);
            if (p.Vacia || p.Bando != Turno) return resultado;
            var pseudo = new List<Jugada>();
            PseudoLegales(desde, pseudo);
            foreach (var j in pseudo)
                if (EsLegal(j)) resultado.Add(j);
            return resultado;
        }

        public List<Jugada> TodasLasJugadasLegales()
        {
            var lista = new List<Jugada>();
            for (int c = 0; c < 8; c++)
                for (int f = 0; f < 8; f++)
                    if (!tablero[c, f].Vacia && tablero[c, f].Bando == Turno)
                        lista.AddRange(JugadasLegales(new Casilla(c, f)));
            return lista;
        }

        bool EsLegal(Jugada j)
        {
            Bando yo = Turno;
            var copia = Guardar();
            AplicarSinValidar(j);
            bool legal = !EnJaque(yo);
            Restaurar(copia);
            return legal;
        }

        // --------------------------------------------------------- Aplicar jugada
        struct Copia
        {
            public Pieza[] Casillas; public Bando Turno; public bool[] Enroque; public Casilla AlPaso;
            public int Medias; public int Numero; public EstadoPartida Estado;
        }

        Copia Guardar()
        {
            var c = new Copia { Casillas = new Pieza[64], Turno = Turno, Enroque = (bool[])enroque.Clone(), AlPaso = AlPaso, Medias = MediasJugadasSinCaptura, Numero = NumeroJugada, Estado = Estado };
            for (int i = 0; i < 64; i++) c.Casillas[i] = tablero[i % 8, i / 8];
            return c;
        }

        void Restaurar(Copia c)
        {
            for (int i = 0; i < 64; i++) tablero[i % 8, i / 8] = c.Casillas[i];
            Turno = c.Turno; Array.Copy(c.Enroque, enroque, 4); AlPaso = c.AlPaso;
            MediasJugadasSinCaptura = c.Medias; NumeroJugada = c.Numero; Estado = c.Estado;
        }

        void AplicarSinValidar(Jugada j)
        {
            Pieza p = En(j.Desde);
            Pieza capturada = j.EsCaptura ? En(j.CasillaCapturada) : Pieza.Nada;
            if (j.EsCaptura) tablero[j.CasillaCapturada.Columna, j.CasillaCapturada.Fila] = Pieza.Nada;
            tablero[j.Desde.Columna, j.Desde.Fila] = Pieza.Nada;
            tablero[j.Hasta.Columna, j.Hasta.Fila] = j.Coronacion != TipoPieza.Ninguna ? new Pieza(j.Coronacion, p.Bando) : p;
            if (j.EsEnroque)
            {
                Pieza torre = En(j.TorreDesde);
                tablero[j.TorreDesde.Columna, j.TorreDesde.Fila] = Pieza.Nada;
                tablero[j.TorreHasta.Columna, j.TorreHasta.Fila] = torre;
            }
            // derechos de enroque
            if (p.Tipo == TipoPieza.Rey)
            {
                if (p.Bando == Bando.Blancas) { enroque[0] = false; enroque[1] = false; }
                else { enroque[2] = false; enroque[3] = false; }
            }
            QuitarEnroqueSiTorre(j.Desde);
            QuitarEnroqueSiTorre(j.Hasta);
            // captura al paso disponible para el rival
            AlPaso = Casilla.Ninguna;
            if (p.Tipo == TipoPieza.Peon && Math.Abs(j.Hasta.Fila - j.Desde.Fila) == 2)
                AlPaso = new Casilla(j.Desde.Columna, (j.Desde.Fila + j.Hasta.Fila) / 2);
            MediasJugadasSinCaptura = (p.Tipo == TipoPieza.Peon || !capturada.Vacia) ? 0 : MediasJugadasSinCaptura + 1;
            if (Turno == Bando.Negras) NumeroJugada++;
            Turno = Contrario(Turno);
        }

        void QuitarEnroqueSiTorre(Casilla c)
        {
            if (c.Columna == 7 && c.Fila == 0) enroque[0] = false;
            if (c.Columna == 0 && c.Fila == 0) enroque[1] = false;
            if (c.Columna == 7 && c.Fila == 7) enroque[2] = false;
            if (c.Columna == 0 && c.Fila == 7) enroque[3] = false;
        }

        /// <summary>Busca una jugada legal desde->hasta. Si es coronación usa 'coronacion' (Dama por defecto).</summary>
        public bool BuscarJugada(Casilla desde, Casilla hasta, out Jugada jugada, TipoPieza coronacion = TipoPieza.Dama)
        {
            jugada = default(Jugada);
            if (Estado == EstadoPartida.JaqueMate || Estado == EstadoPartida.Ahogado ||
                Estado == EstadoPartida.TablasCincuenta || Estado == EstadoPartida.TablasMaterial) return false;
            foreach (var j in JugadasLegales(desde))
            {
                if (j.Hasta != hasta) continue;
                if (j.Coronacion != TipoPieza.Ninguna && j.Coronacion != coronacion) continue;
                jugada = j;
                return true;
            }
            return false;
        }

        /// <summary>Aplica una jugada legal (obtenida con BuscarJugada o JugadasLegales).</summary>
        public ResultadoJugada Aplicar(Jugada j)
        {
            var r = new ResultadoJugada
            {
                Jugada = j,
                PiezaMovida = En(j.Desde),
                PiezaCapturada = j.EsCaptura ? En(j.CasillaCapturada) : Pieza.Nada
            };
            AplicarSinValidar(j);
            Historial.Add(j.ToString());
            Estado = CalcularEstado();
            r.EstadoDespues = Estado;
            r.TurnoDespues = Turno;
            return r;
        }

        EstadoPartida CalcularEstado()
        {
            bool hayJugadas = false;
            for (int c = 0; c < 8 && !hayJugadas; c++)
                for (int f = 0; f < 8 && !hayJugadas; f++)
                    if (!tablero[c, f].Vacia && tablero[c, f].Bando == Turno && JugadasLegales(new Casilla(c, f)).Count > 0)
                        hayJugadas = true;
            bool jaque = EnJaque(Turno);
            if (!hayJugadas) return jaque ? EstadoPartida.JaqueMate : EstadoPartida.Ahogado;
            if (MediasJugadasSinCaptura >= 100) return EstadoPartida.TablasCincuenta;
            if (MaterialInsuficiente()) return EstadoPartida.TablasMaterial;
            return jaque ? EstadoPartida.Jaque : EstadoPartida.EnJuego;
        }

        bool MaterialInsuficiente()
        {
            int menores = 0;
            for (int c = 0; c < 8; c++)
                for (int f = 0; f < 8; f++)
                {
                    TipoPieza t = tablero[c, f].Tipo;
                    if (t == TipoPieza.Peon || t == TipoPieza.Torre || t == TipoPieza.Dama) return false;
                    if (t == TipoPieza.Alfil || t == TipoPieza.Caballo) menores++;
                }
            return menores <= 1;
        }

        public bool Terminada
        {
            get
            {
                return Estado == EstadoPartida.JaqueMate || Estado == EstadoPartida.Ahogado ||
                       Estado == EstadoPartida.TablasCincuenta || Estado == EstadoPartida.TablasMaterial;
            }
        }

        // --------------------------------------------------------- Perft (pruebas)
        /// <summary>Cuenta posiciones a 'profundidad'. Sirve para validar el generador de jugadas.</summary>
        public long Perft(int profundidad)
        {
            if (profundidad == 0) return 1;
            long total = 0;
            var jugadas = new List<Jugada>();
            for (int c = 0; c < 8; c++)
                for (int f = 0; f < 8; f++)
                    if (!tablero[c, f].Vacia && tablero[c, f].Bando == Turno)
                    {
                        var pseudo = new List<Jugada>();
                        PseudoLegales(new Casilla(c, f), pseudo);
                        jugadas.AddRange(pseudo);
                    }
            foreach (var j in jugadas)
            {
                Bando yo = Turno;
                var copia = Guardar();
                AplicarSinValidar(j);
                if (!EnJaque(yo)) total += Perft(profundidad - 1);
                Restaurar(copia);
            }
            return total;
        }
    }
}
