// Ejemplo para Unity: ejecuta detector_ajedrez.py y lee el resultado en JSON.
// Poner este script en un GameObject y ajustar las rutas en el Inspector.
using System.Diagnostics;
using UnityEngine;

[System.Serializable]
public class ResultadoMovimiento
{
    public string pieza;      // rey, reina, torre, alfil, caballo, peon (sin color)
    public string origen;     // casilla de salida, por ejemplo "B1"
    public string destino;    // casilla de llegada, por ejemplo "C3"
    public float confianza;   // 0 a 1
}

public class ChessDetector : MonoBehaviour
{
    [Tooltip("python.exe del entorno donde se instalaron las librerías, por ejemplo C:/Proyecto_Ajedrez_PDI/venv/Scripts/python.exe")]
    public string rutaPython = "C:/Proyecto_Ajedrez_PDI/venv/Scripts/python.exe";

    [Tooltip("Ruta de detector_ajedrez.py (en la misma carpeta deben estar modelo_ajedrez.pth y config_modelo.json)")]
    public string rutaScript = "C:/Proyecto_Ajedrez_PDI/entrega_unity/detector_ajedrez.py";

    public ResultadoMovimiento Detectar(string fotoAntes, string fotoDespues)
    {
        var info = new ProcessStartInfo
        {
            FileName = rutaPython,
            Arguments = $"\"{rutaScript}\" \"{fotoAntes}\" \"{fotoDespues}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8
        };

        using (var proceso = Process.Start(info))
        {
            string salida = proceso.StandardOutput.ReadToEnd();   // una línea con el JSON
            proceso.WaitForExit();
            if (string.IsNullOrWhiteSpace(salida))
            {
                UnityEngine.Debug.LogError("El detector no devolvió nada: " + proceso.StandardError.ReadToEnd());
                return null;
            }
            var r = JsonUtility.FromJson<ResultadoMovimiento>(salida.Trim());
            if (string.IsNullOrEmpty(r.origen))
                UnityEngine.Debug.LogWarning("No se pudo determinar el movimiento: " + salida);
            return r;
        }
    }

    // Prueba rápida: clic derecho sobre el componente -> "Probar detector"
    public string fotoPruebaAntes = "C:/fotos/antes.jpg";
    public string fotoPruebaDespues = "C:/fotos/despues.jpg";

    [ContextMenu("Probar detector")]
    void Probar()
    {
        var r = Detectar(fotoPruebaAntes, fotoPruebaDespues);
        if (r != null)
            UnityEngine.Debug.Log($"{r.pieza}: {r.origen} -> {r.destino} (confianza {r.confianza})");
    }
}
