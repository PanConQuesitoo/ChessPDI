// =====================================================================
//  EntradaUtil.cs  -  Teclado y mouse que funcionan tanto con el
//  "Input Manager" antiguo como con el nuevo "Input System" de Unity.
//  (Unity 6 trae por defecto el Input System nuevo.)
// =====================================================================
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace AjedrezArena
{
    public enum TeclaJuego
    {
        Reiniciar,          // R
        SiguienteEscenario, // N
        Ayuda,              // H
        CambiarVista,       // C
        VistaCenital,       // V
        Escenario1, Escenario2, Escenario3, Escenario4, Escenario5, Escenario6,
        Escenario7, Escenario8, Escenario9, Escenario10,  // 7, 8, 9, 0
        OrbitarIzquierda,   // Q
        OrbitarDerecha,     // E
        Agarrar,            // Espacio (alternativa al clic)
        Depuracion,         // F1
        FotoBase,           // B  (camara: guarda el estado actual del tablero fisico)
        DetectarJugada,     // G  (camara: detecta la jugada hecha en el tablero fisico)
        AceptarDeteccion,   // Enter (camara: aceptar una jugada dudosa)
        DescartarDeteccion, // Retroceso (camara: descartarla)
        Pausa,              // Esc (menu de pausa; normalmente se abre con la mano)
    }

    public static class EntradaUtil
    {
        public static Vector2 PosicionMouse
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
                return Input.mousePosition;
#endif
            }
        }

        public static bool HayMouse
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Mouse.current != null;
#else
                return Input.mousePresent;
#endif
            }
        }

        public static bool BotonIzquierdo
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
                return Input.GetMouseButton(0);
#endif
            }
        }

        public static bool BotonDerecho
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                return Mouse.current != null && Mouse.current.rightButton.isPressed;
#else
                return Input.GetMouseButton(1);
#endif
            }
        }

        /// <summary>Rueda del mouse normalizada (aprox. +1 / -1 por "clic" de rueda).</summary>
        public static float Rueda
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                if (Mouse.current == null) return 0f;
                float y = Mouse.current.scroll.ReadValue().y;
                return Mathf.Abs(y) > 10f ? y / 120f : y;
#else
                return Input.mouseScrollDelta.y;
#endif
            }
        }

#if ENABLE_INPUT_SYSTEM
        static Key AKey(TeclaJuego t)
        {
            switch (t)
            {
                case TeclaJuego.Reiniciar: return Key.R;
                case TeclaJuego.SiguienteEscenario: return Key.N;
                case TeclaJuego.Ayuda: return Key.H;
                case TeclaJuego.CambiarVista: return Key.C;
                case TeclaJuego.VistaCenital: return Key.V;
                case TeclaJuego.Escenario1: return Key.Digit1;
                case TeclaJuego.Escenario2: return Key.Digit2;
                case TeclaJuego.Escenario3: return Key.Digit3;
                case TeclaJuego.Escenario4: return Key.Digit4;
                case TeclaJuego.Escenario5: return Key.Digit5;
                case TeclaJuego.Escenario6: return Key.Digit6;
                case TeclaJuego.Escenario7: return Key.Digit7;
                case TeclaJuego.Escenario8: return Key.Digit8;
                case TeclaJuego.Escenario9: return Key.Digit9;
                case TeclaJuego.Escenario10: return Key.Digit0;
                case TeclaJuego.OrbitarIzquierda: return Key.Q;
                case TeclaJuego.OrbitarDerecha: return Key.E;
                case TeclaJuego.Agarrar: return Key.Space;
                case TeclaJuego.FotoBase: return Key.B;
                case TeclaJuego.DetectarJugada: return Key.G;
                case TeclaJuego.AceptarDeteccion: return Key.Enter;
                case TeclaJuego.DescartarDeteccion: return Key.Backspace;
                case TeclaJuego.Pausa: return Key.Escape;
                default: return Key.F1;
            }
        }
#else
        static KeyCode AKeyCode(TeclaJuego t)
        {
            switch (t)
            {
                case TeclaJuego.Reiniciar: return KeyCode.R;
                case TeclaJuego.SiguienteEscenario: return KeyCode.N;
                case TeclaJuego.Ayuda: return KeyCode.H;
                case TeclaJuego.CambiarVista: return KeyCode.C;
                case TeclaJuego.VistaCenital: return KeyCode.V;
                case TeclaJuego.Escenario1: return KeyCode.Alpha1;
                case TeclaJuego.Escenario2: return KeyCode.Alpha2;
                case TeclaJuego.Escenario3: return KeyCode.Alpha3;
                case TeclaJuego.Escenario4: return KeyCode.Alpha4;
                case TeclaJuego.Escenario5: return KeyCode.Alpha5;
                case TeclaJuego.Escenario6: return KeyCode.Alpha6;
                case TeclaJuego.Escenario7: return KeyCode.Alpha7;
                case TeclaJuego.Escenario8: return KeyCode.Alpha8;
                case TeclaJuego.Escenario9: return KeyCode.Alpha9;
                case TeclaJuego.Escenario10: return KeyCode.Alpha0;
                case TeclaJuego.OrbitarIzquierda: return KeyCode.Q;
                case TeclaJuego.OrbitarDerecha: return KeyCode.E;
                case TeclaJuego.Agarrar: return KeyCode.Space;
                case TeclaJuego.FotoBase: return KeyCode.B;
                case TeclaJuego.DetectarJugada: return KeyCode.G;
                case TeclaJuego.AceptarDeteccion: return KeyCode.Return;
                case TeclaJuego.DescartarDeteccion: return KeyCode.Backspace;
                case TeclaJuego.Pausa: return KeyCode.Escape;
                default: return KeyCode.F1;
            }
        }
#endif

        /// <summary>True solo en el cuadro en que se presionó la tecla.</summary>
        public static bool Pulsada(TeclaJuego t)
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current[AKey(t)].wasPressedThisFrame;
#else
            return Input.GetKeyDown(AKeyCode(t));
#endif
        }

        /// <summary>True mientras la tecla esté presionada.</summary>
        public static bool Mantenida(TeclaJuego t)
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current[AKey(t)].isPressed;
#else
            return Input.GetKey(AKeyCode(t));
#endif
        }
    }
}
