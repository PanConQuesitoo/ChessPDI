// =====================================================================
//  EntradaManoMouse.cs  -  La mano sigue al mouse (clic izquierdo = agarrar).
// =====================================================================
using UnityEngine;

namespace AjedrezArena
{
    // ---------------------------------------------------------------- MOUSE
    public class EntradaManoMouse : FuenteEntradaMano
    {
        [Tooltip("Oculta la flecha del sistema: la mano 3D hace de cursor.")]
        public bool ocultarCursor = true;
        [Tooltip("Con la cámara: el mouse solo toma el control si lo mueves o haces clic (así la mano " +
                 "virtual no salta al mouse cuando retiras tu mano real del tablero).")]
        public bool soloSiSeUsa = false;
        public float segundosDeUso = 2f;

        Vector2 ultimaPos;
        float ultimoUso = -100f;

        void Update()
        {
            if (ocultarCursor) Cursor.visible = EntradaUtil.BotonDerecho;
            Vector2 p = EntradaUtil.PosicionMouse;
            if ((p - ultimaPos).sqrMagnitude > 4f || EntradaUtil.BotonIzquierdo) ultimoUso = Time.unscaledTime;
            ultimaPos = p;
        }
        void OnDisable() { Cursor.visible = true; }

        public override bool Activa
        {
            get { return EntradaUtil.HayMouse && (!soloSiSeUsa || Time.unscaledTime - ultimoUso < segundosDeUso); }
        }
        public override EspacioCoordenadas Espacio { get { return EspacioCoordenadas.Pantalla; } }
        public override Vector2 Posicion
        {
            get
            {
                Vector2 p = EntradaUtil.PosicionMouse;
                return new Vector2(p.x / Mathf.Max(1, Screen.width), p.y / Mathf.Max(1, Screen.height));
            }
        }
        public override bool Agarrando { get { return EntradaUtil.BotonIzquierdo || EntradaUtil.Mantenida(TeclaJuego.Agarrar); } }
        public override string Nombre { get { return "Mouse"; } }
    }
}
