// =====================================================================
//  CrearEjecutable.cs  (Editor)  -  Menú "Ajedrez Arena/5. Crear .exe para compartir".
//  Arma una carpeta lista para mandar (zip) a otra persona:
//
//    Build/AjedrezArena/
//      AjedrezArena.exe  + AjedrezArena_Data/ ...   el juego
//      Detector/                                   la cámara (Python)
//        ajedrez_vivo.py, entrega_unity/ (red neuronal), LEEME_CAMARA.txt
//        instalar_dependencias.bat                 instala Python + librerías (una vez)
//      LEEME_PRIMERO.txt                           instrucciones para tu amigo
//
//  No copia el venv (pesa ~1,5 GB y solo sirve en este PC): cada PC lo instala
//  con instalar_dependencias.bat. El juego también funciona sin cámara (mouse).
// =====================================================================
using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AjedrezArena.Herramientas
{
    public static class CrearEjecutable
    {
        const string NombreJuego = "AjedrezArena";

        static string Raiz { get { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); } }
        public static string CarpetaSalida { get { return Path.Combine(Raiz, "Build", NombreJuego); } }

        [MenuItem("Ajedrez Arena/5. Crear .exe para compartir", false, 20)]
        public static void Crear()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Ajedrez Arena", "Primero detén el modo Play.", "OK"); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            // 1. materiales como assets (si no, los shaders no entran en el build)
            try { MenuAjedrezArena.SoloMateriales(); } catch (Exception e) { Debug.LogWarning("[Ajedrez Arena] Materiales: " + e.Message); }
            EditorSceneManager.SaveOpenScenes();

            // 2. escenas: las del Build Settings, o la escena abierta
            string[] escenas = EditorBuildSettings.scenes.Where(e => e.enabled).Select(e => e.path).ToArray();
            if (escenas.Length == 0)
            {
                string actual = EditorSceneManager.GetActiveScene().path;
                if (string.IsNullOrEmpty(actual)) { EditorUtility.DisplayDialog("Ajedrez Arena", "Guarda la escena primero.", "OK"); return; }
                escenas = new[] { actual };
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(actual, true) };
            }

            // 3. ajustes del reproductor
            PlayerSettings.productName = "Ajedrez sobre Arena";
            PlayerSettings.runInBackground = true;          // seguir aunque la ventana de la cámara tome el foco
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.resizableWindow = true;

            string salida = CarpetaSalida;
            if (Directory.Exists(salida))
            {
                try { Directory.Delete(salida, true); }
                catch (Exception e) { EditorUtility.DisplayDialog("Ajedrez Arena", "No pude borrar el build anterior (¿está abierto el juego?):\n" + e.Message, "OK"); return; }
            }
            Directory.CreateDirectory(salida);

            var opciones = new BuildPlayerOptions
            {
                scenes = escenas,
                locationPathName = Path.Combine(salida, NombreJuego + ".exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            BuildReport reporte = BuildPipeline.BuildPlayer(opciones);
            if (reporte.summary.result != BuildResult.Succeeded)
            {
                EditorUtility.DisplayDialog("Ajedrez Arena", "El build falló (" + reporte.summary.result + "). Mira la consola.", "OK");
                return;
            }

            // 4. la cámara (Python) al lado del .exe
            string detectorOrigen = Path.Combine(Raiz, "Detector");
            string detectorDestino = Path.Combine(salida, "Detector");
            Directory.CreateDirectory(detectorDestino);
            foreach (string f in new[] { "ajedrez_vivo.py", "LEEME_CAMARA.txt", "enviar_deteccion_udp.py" })
            {
                string o = Path.Combine(detectorOrigen, f);
                if (File.Exists(o)) File.Copy(o, Path.Combine(detectorDestino, f), true);
            }
            string entrega = Path.Combine(detectorOrigen, "entrega_unity");
            if (Directory.Exists(entrega)) CopiarCarpeta(entrega, Path.Combine(detectorDestino, "entrega_unity"));
            else Debug.LogWarning("[Ajedrez Arena] No encontré Detector/entrega_unity: la red neuronal no irá en el build.");
            File.WriteAllText(Path.Combine(detectorDestino, "instalar_dependencias.bat"), Instalador().Replace("\n", "\r\n"), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(salida, "LEEME_PRIMERO.txt"), LeemeAmigo().Replace("\n", "\r\n"), new UTF8Encoding(true));

            // la carpeta de depuración de Burst no hace falta para jugar
            foreach (string d in Directory.GetDirectories(salida, "*DoNotShip*"))
                try { Directory.Delete(d, true); } catch (Exception) { }

            long bytes = new DirectoryInfo(salida).GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            Debug.Log("[Ajedrez Arena] .exe listo en " + salida + "  (" + (bytes / 1048576f).ToString("0") + " MB). Comprime la carpeta en .zip y mándala.");
            EditorUtility.RevealInFinder(Path.Combine(salida, NombreJuego + ".exe"));
        }

        static void CopiarCarpeta(string origen, string destino)
        {
            Directory.CreateDirectory(destino);
            foreach (string f in Directory.GetFiles(origen))
            {
                string n = Path.GetFileName(f);
                if (n.EndsWith(".pyc") || n.EndsWith(".log")) continue;
                File.Copy(f, Path.Combine(destino, n), true);
            }
            foreach (string d in Directory.GetDirectories(origen))
            {
                string n = Path.GetFileName(d);
                if (n == "__pycache__" || n == "venv" || n.StartsWith(".")) continue;
                CopiarCarpeta(d, Path.Combine(destino, n));
            }
        }

        // ------------------------------------------------------------------
        static string Instalador()
        {
            return
@"@echo off
chcp 65001 >nul
cd /d ""%~dp0""
title Ajedrez sobre Arena - instalar la camara
echo.
echo  ==========================================================
echo   Ajedrez sobre Arena: instalando la camara (solo una vez)
echo   Descarga ~1 GB. Puede tardar 5-15 minutos.
echo  ==========================================================
echo.

rem ---- 1) buscar Python 3.9 - 3.12 (MediaPipe todavia no funciona con 3.13)
set ""PY=""
for %%V in (3.12 3.11 3.10) do (
  if not defined PY (
    py -%%V -c ""import sys"" >nul 2>&1 && set ""PY=py -%%V""
  )
)
if not defined PY (
  python -c ""import sys; sys.exit(0 if (3,9)<=sys.version_info[:2]<=(3,12) else 1)"" >nul 2>&1 && set ""PY=python""
)
if not defined PY if exist ""%LOCALAPPDATA%\Programs\Python\Python312\python.exe"" set PY=""%LOCALAPPDATA%\Programs\Python\Python312\python.exe""

if not defined PY (
  echo No encontre Python 3.12. Lo instalo con winget...
  winget install -e --id Python.Python.3.12 --scope user --accept-package-agreements --accept-source-agreements
  if exist ""%LOCALAPPDATA%\Programs\Python\Python312\python.exe"" set PY=""%LOCALAPPDATA%\Programs\Python\Python312\python.exe""
)
if not defined PY (
  echo.
  echo  [ERROR] No pude instalar Python.
  echo  Instala Python 3.12 desde https://www.python.org/downloads/release/python-3128/
  echo  marcando ""Add python.exe to PATH"", y vuelve a abrir este archivo.
  pause
  exit /b 1
)
echo Usando: %PY%

rem ---- 2) entorno propio de la camara (carpeta venv, al lado de este archivo)
if not exist ""venv\Scripts\python.exe"" (
  %PY% -m venv venv
  if errorlevel 1 ( echo [ERROR] No pude crear el entorno venv. & pause & exit /b 1 )
)

rem ---- 3) librerias
""venv\Scripts\python.exe"" -m pip install --upgrade pip
""venv\Scripts\python.exe"" -m pip install ""numpy<2"" ""mediapipe==0.10.21"" pillow torch torchvision
if errorlevel 1 ( echo. & echo [ERROR] Fallo la instalacion. Revisa tu internet y vuelve a intentar. & pause & exit /b 1 )

""venv\Scripts\python.exe"" -c ""import cv2, mediapipe, torch; print('OK: camara lista')""
echo.
echo  Listo. Ya puedes abrir AjedrezArena.exe
echo  (si mueves la carpeta del juego a otro lugar, vuelve a ejecutar este archivo)
pause
";
        }

        static string LeemeAmigo()
        {
            return
@"AJEDREZ SOBRE ARENA
Proyecto final de Procesamiento Digital de Imágenes - Ingeniería Multimedia (UAO)
=================================================================================

ANTES DE JUGAR (una sola vez, ~10 minutos, necesita internet)
  1. Descomprime la carpeta completa (no lo abras desde dentro del .zip).
  2. Entra a la carpeta Detector y haz doble clic en  instalar_dependencias.bat
     (instala Python 3.12 si no lo tienes, y las librerías de la cámara).

JUGAR
  - Doble clic en AjedrezArena.exe
  - La cámara se enciende sola. Todo se maneja con la mano:
      * el cursor es el punto entre tu pulgar y tu índice
      * para elegir un botón: junta los dedos (pinza) o deja la mano quieta 1 segundo
  - En el menú elige  ""Jugar solo con la mano""  (no necesitas tablero físico):
      1. elige el escenario
      2. muestra la mano a la cámara y junta los dedos una vez
      3. apunta a una pieza, junta los dedos para agarrarla, llévala y sepáralos para soltar
      Rápido = fuego · muy rápido = rayo · pieza quieta 2 s en la mano = supernova
  - Lleva la mano al borde derecho de la pantalla para abrir el Menú (o pulsa ESC).
  - ""Jugar con tablero"" es para un tablero de ajedrez real visto desde arriba por la cámara.

SI ALGO FALLA
  - ""Falta instalar las librerías"": ejecuta Detector\instalar_dependencias.bat
    (el botón ""Instalar librerías"" del juego lo abre; ahí sí funciona el mouse).
  - La cámara no prende: cierra Zoom, Teams, OBS... que la estén usando, y pulsa Reintentar.
  - Imagen negra: destapa la cámara o prende la luz.
  - Lo que pasó queda escrito en Detector\ajedrez_vivo.log
  - Sin cámara también se puede jugar con el mouse (""Jugar sin cámara"").
";
        }
    }
}
