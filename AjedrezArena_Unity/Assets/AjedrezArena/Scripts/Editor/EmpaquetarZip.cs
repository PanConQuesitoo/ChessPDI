#if UNITY_EDITOR
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;

namespace AjedrezArena.Herramientas
{
    /// <summary>Comprime la carpeta del .exe (Build/AjedrezArena) en un .zip listo para mandar.</summary>
    public static class EmpaquetarZip
    {
        [MenuItem("Ajedrez Arena/6. Comprimir el .exe en .zip", false, 21)]
        public static void Comprimir()
        {
            string carpeta = CrearEjecutable.CarpetaSalida;
            if (!Directory.Exists(carpeta)) { Debug.LogWarning("[Ajedrez Arena] Primero crea el .exe."); return; }
            string zip = Path.Combine(Path.GetDirectoryName(carpeta), "AjedrezArena.zip");
            if (File.Exists(zip)) File.Delete(zip);
            ZipFile.CreateFromDirectory(carpeta, zip, System.IO.Compression.CompressionLevel.Optimal, true);
            Debug.Log("[Ajedrez Arena] Zip listo: " + zip + " (" + (new FileInfo(zip).Length / 1048576f).ToString("0") + " MB)");
        }
    }
}
#endif
