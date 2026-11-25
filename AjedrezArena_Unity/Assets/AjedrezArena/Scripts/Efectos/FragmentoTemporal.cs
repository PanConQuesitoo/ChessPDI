// =====================================================================
//  FragmentoTemporal.cs  -  Fragmentos de una pieza capturada: se encogen y desaparecen.
// =====================================================================
using UnityEngine;

namespace AjedrezArena
{
    /// <summary>Encoge y destruye los fragmentos de una pieza capturada.</summary>
    public class FragmentoTemporal : MonoBehaviour
    {
        public float vida = 2f;
        float t;
        Vector3 escala;
        void Start() { escala = transform.localScale; }
        void Update()
        {
            t += Time.deltaTime;
            float resto = vida - t;
            if (resto < 0.5f) transform.localScale = escala * Mathf.Clamp01(resto / 0.5f);
            if (resto <= 0f) Destroy(gameObject);
        }
    }
}
