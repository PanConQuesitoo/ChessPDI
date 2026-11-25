// =====================================================================
//  FuenteEntradaMano.cs  -  De dónde sale la posición de la mano.
//
//  La mano NO depende del mouse: depende de una "FuenteEntradaMano".
//  Hoy usas el mouse (EntradaManoMouse). Mañana tu red neuronal en
//  Python (cámara/celular) le envía por UDP la posición de la mano real
//  (EntradaManoUDP) y el juego no cambia en nada.
//
//  Formato UDP (texto, una línea por paquete):
//      V;x;y;g    -> x,y en coordenadas de pantalla 0..1 (origen arriba-izq), g = 0/1 agarre
//      B;x;y;g    -> x,y en coordenadas del TABLERO 0..1 (origen en a1), g = 0/1 agarre
//  Ejemplo:  "B;0.4375;0.0625;1"  (mano sobre e1, cerrada)
// =====================================================================
using UnityEngine;

namespace AjedrezArena
{
    public enum EspacioCoordenadas { Pantalla, Tablero }

    public abstract class FuenteEntradaMano : MonoBehaviour
    {
        public abstract bool Activa { get; }
        public abstract EspacioCoordenadas Espacio { get; }
        /// <summary>Posición 0..1 (pantalla con origen abajo-izq, o tablero con origen en a1).</summary>
        public abstract Vector2 Posicion { get; }
        public abstract bool Agarrando { get; }
        public virtual string Nombre { get { return GetType().Name; } }
    }
}
