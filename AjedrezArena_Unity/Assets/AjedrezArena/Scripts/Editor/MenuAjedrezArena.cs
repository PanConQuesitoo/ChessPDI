// =====================================================================
//  MenuAjedrezArena.cs  (Editor)  -  Menú "Ajedrez Arena" en Unity:
//    1. Configurar escena  -> crea el objeto AjedrezArena y le asigna
//       todos los FBX (los busca por nombre en el proyecto).
//    2. Crear materiales   -> guarda los materiales/texturas como assets
//       (necesario para que funcionen en un BUILD, no solo en el editor).
//    3. Vista previa       -> muestra tablero y piezas en el editor (sin Play).
// =====================================================================
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AjedrezArena.Herramientas
{
    public static class MenuAjedrezArena
    {
        const string CarpetaMateriales = "Assets/AjedrezArena/Materiales";

        [MenuItem("Ajedrez Arena/1. Configurar escena", false, 1)]
        public static void ConfigurarEscena()
        {
#if UNITY_2023_1_OR_NEWER
            var arranque = Object.FindFirstObjectByType<ArranqueAjedrez>();
#else
            var arranque = Object.FindObjectOfType<ArranqueAjedrez>();
#endif
            if (arranque == null)
            {
                var go = new GameObject("AjedrezArena");
                arranque = go.AddComponent<ArranqueAjedrez>();
                Undo.RegisterCreatedObjectUndo(go, "Crear AjedrezArena");
            }
            Undo.RecordObject(arranque, "Configurar AjedrezArena");
            arranque.peon = Buscar("Peon");
            arranque.torre = Buscar("Torre");
            arranque.caballo = Buscar("Caballo");
            arranque.alfil = Buscar("Alfil");
            arranque.dama = Buscar("Dama");
            arranque.rey = Buscar("Rey");
            arranque.tablero = Buscar("Tablero");
            arranque.mano = Buscar("Mano");
            arranque.escenarios.Clear();
            arranque.modelosTemas.Clear();
            int piezasTema = 0;
            foreach (var e in ControladorEscenarios.Predeterminados())
            {
                arranque.escenarios.Add(Buscar("Escenario_" + e.nombre));
                var mt = new ModelosTema
                {
                    nombre = e.nombre,
                    tablero = Buscar(e.nombre + "_Tablero"),
                    peon = Buscar(e.nombre + "_Peon"), torre = Buscar(e.nombre + "_Torre"), caballo = Buscar(e.nombre + "_Caballo"),
                    alfil = Buscar(e.nombre + "_Alfil"), dama = Buscar(e.nombre + "_Dama"), rey = Buscar(e.nombre + "_Rey"),
                    mano = Buscar(e.nombre + "_Mano"),
                };
                foreach (var g in new[] { mt.tablero, mt.peon, mt.torre, mt.caballo, mt.alfil, mt.dama, mt.rey, mt.mano }) if (g != null) piezasTema++;
                arranque.modelosTemas.Add(mt);
            }

            int encontrados = 0;
            foreach (var g in new[] { arranque.peon, arranque.torre, arranque.caballo, arranque.alfil, arranque.dama, arranque.rey, arranque.tablero, arranque.mano })
                if (g != null) encontrados++;
            foreach (var g in arranque.escenarios) if (g != null) encontrados++;

            CrearMateriales(arranque);
            ConstruirVistaPrevia(arranque);
            EditorUtility.SetDirty(arranque);
            EditorSceneManager.MarkSceneDirty(arranque.gameObject.scene);
            Selection.activeObject = arranque.gameObject;
            Debug.Log("[Ajedrez Arena] Escena configurada: " + encontrados + "/18 modelos base y " + piezasTema + "/80 modelos temáticos. Dale Play ▶");
            if (encontrados < 18)
                Debug.LogWarning("[Ajedrez Arena] Faltan modelos. Copia la carpeta 'Modelos' dentro de Assets/AjedrezArena/ y vuelve a usar el menú. Mientras tanto se usan primitivas.");
        }

        [MenuItem("Ajedrez Arena/2. Crear materiales (para builds)", false, 2)]
        public static void SoloMateriales()
        {
#if UNITY_2023_1_OR_NEWER
            var arranque = Object.FindFirstObjectByType<ArranqueAjedrez>();
#else
            var arranque = Object.FindObjectOfType<ArranqueAjedrez>();
#endif
            CrearMateriales(arranque);
        }

        // -----------------------------------------------------------------
        //  VISTA PREVIA: el juego se crea al darle Play; esto pone una copia
        //  estática (tablero + 32 piezas + escenario inicial) para verlo en
        //  el editor. Se borra sola al dar Play y no entra en los builds.
        // -----------------------------------------------------------------
        public const string NombreVistaPrevia = "VistaPrevia (solo editor)";

        [MenuItem("Ajedrez Arena/3. Mostrar vista previa en el editor", false, 3)]
        public static void MenuVistaPrevia()
        {
#if UNITY_2023_1_OR_NEWER
            var arranque = Object.FindFirstObjectByType<ArranqueAjedrez>();
#else
            var arranque = Object.FindObjectOfType<ArranqueAjedrez>();
#endif
            if (arranque == null) { ConfigurarEscena(); return; }
            ConstruirVistaPrevia(arranque);
            EditorSceneManager.MarkSceneDirty(arranque.gameObject.scene);
        }

        [MenuItem("Ajedrez Arena/4. Quitar vista previa", false, 4)]
        public static void QuitarVistaPrevia()
        {
#if UNITY_2023_1_OR_NEWER
            var arranque = Object.FindFirstObjectByType<ArranqueAjedrez>();
#else
            var arranque = Object.FindObjectOfType<ArranqueAjedrez>();
#endif
            if (arranque == null) return;
            var vieja = arranque.transform.Find(NombreVistaPrevia);
            if (vieja != null) Undo.DestroyObjectImmediate(vieja.gameObject);
            EditorSceneManager.MarkSceneDirty(arranque.gameObject.scene);
        }

        public static void ConstruirVistaPrevia(ArranqueAjedrez a)
        {
            var vieja = a.transform.Find(NombreVistaPrevia);
            if (vieja != null) Object.DestroyImmediate(vieja.gameObject);

            var raiz = new GameObject(NombreVistaPrevia);
            raiz.tag = "EditorOnly";                       // no entra en los builds
            raiz.transform.SetParent(a.transform, false);
            Undo.RegisterCreatedObjectUndo(raiz, "Vista previa Ajedrez");

            // modelos del escenario inicial (si existen) o los base
            ModelosTema mt = null;
            if (a.piezasTematicas && a.escenarioInicial >= 0 && a.escenarioInicial < a.modelosTemas.Count) mt = a.modelosTemas[a.escenarioInicial];
            System.Func<GameObject, GameObject, GameObject> elegir = (tema, baseM) => tema != null ? tema : baseM;

            // escenario
            if (a.escenarioInicial >= 0 && a.escenarioInicial < a.escenarios.Count && a.escenarios[a.escenarioInicial] != null)
            {
                var esc = (GameObject)PrefabUtility.InstantiatePrefab(a.escenarios[a.escenarioInicial], raiz.transform);
                esc.name = "Escenario";
            }

            // tablero
            var pfTab = elegir(mt != null ? mt.tablero : null, a.tablero);
            if (pfTab != null)
            {
                var t = (GameObject)PrefabUtility.InstantiatePrefab(pfTab, raiz.transform);
                t.name = "Tablero";
            }
            else
            {
                var t = GameObject.CreatePrimitive(PrimitiveType.Cube);
                t.name = "Tablero";
                t.transform.SetParent(raiz.transform, false);
                t.transform.localPosition = new Vector3(0f, -0.06f, 0f);
                t.transform.localScale = new Vector3(8f, 0.12f, 8f);
            }

            // piezas en la posición inicial
            string[] fila = { "torre", "caballo", "alfil", "dama", "rey", "alfil", "caballo", "torre" };
            var piezas = new GameObject("Piezas").transform;
            piezas.SetParent(raiz.transform, false);
            for (int c = 0; c < 8; c++)
            {
                PonerPieza(a, mt, piezas, fila[c], c, 0, true);
                PonerPieza(a, mt, piezas, "peon", c, 1, true);
                PonerPieza(a, mt, piezas, "peon", c, 6, false);
                PonerPieza(a, mt, piezas, fila[c], c, 7, false);
            }

            // cámara del juego mirando al tablero (igual que al empezar el Play)
            var cam = Camera.main;
            if (cam != null)
            {
                Undo.RecordObject(cam.transform, "Cámara Ajedrez");
                Quaternion rot = Quaternion.Euler(50f, 0f, 0f);
                cam.transform.position = a.transform.position - rot * Vector3.forward * 11.5f;
                cam.transform.rotation = rot;
                cam.fieldOfView = 50f;
            }
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.Frame(new Bounds(a.transform.position, Vector3.one * 10f), false);
        }

        static void PonerPieza(ArranqueAjedrez a, ModelosTema mt, Transform padre, string tipo, int col, int fil, bool blancas)
        {
            GameObject pfTema = null, pfBase = null;
            switch (tipo)
            {
                case "peon": pfTema = mt != null ? mt.peon : null; pfBase = a.peon; break;
                case "torre": pfTema = mt != null ? mt.torre : null; pfBase = a.torre; break;
                case "caballo": pfTema = mt != null ? mt.caballo : null; pfBase = a.caballo; break;
                case "alfil": pfTema = mt != null ? mt.alfil : null; pfBase = a.alfil; break;
                case "dama": pfTema = mt != null ? mt.dama : null; pfBase = a.dama; break;
                default: pfTema = mt != null ? mt.rey : null; pfBase = a.rey; break;
            }
            var pf = pfTema != null ? pfTema : pfBase;
            GameObject g;
            if (pf != null) g = (GameObject)PrefabUtility.InstantiatePrefab(pf, padre);
            else
            {
                g = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                g.transform.SetParent(padre, false);
                g.transform.localScale = new Vector3(0.5f, 0.4f, 0.5f);
                Object.DestroyImmediate(g.GetComponent<Collider>());
            }
            g.name = (blancas ? "B_" : "N_") + tipo + "_" + (char)('a' + col) + (fil + 1);
            g.transform.localPosition = new Vector3(col - 3.5f, 0f, fil - 3.5f) + (pf == null ? Vector3.up * 0.4f : Vector3.zero);
            float giro = (blancas ? 0f : 180f) + (tipo == "caballo" ? a.rotacionCaballo : 0f);
            g.transform.localRotation = Quaternion.Euler(0f, giro, 0f) * g.transform.localRotation;
            var mat = blancas ? a.materialBlancas : a.materialNegras;
            if (mat != null)
                foreach (var r in g.GetComponentsInChildren<Renderer>())
                {
                    var ms = r.sharedMaterials;
                    // el primer material es el cuerpo; los demás (acentos) se respetan
                    if (ms.Length > 0) { ms[0] = mat; r.sharedMaterials = ms; }
                }
        }

        static GameObject Buscar(string nombre)
        {
            foreach (var guid in AssetDatabase.FindAssets(nombre + " t:Model"))
            {
                string ruta = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(ruta) == nombre)
                    return AssetDatabase.LoadAssetAtPath<GameObject>(ruta);
            }
            return null;
        }

        static void CrearMateriales(ArranqueAjedrez arranque)
        {
            if (!AssetDatabase.IsValidFolder("Assets/AjedrezArena")) AssetDatabase.CreateFolder("Assets", "AjedrezArena");
            if (!AssetDatabase.IsValidFolder(CarpetaMateriales)) AssetDatabase.CreateFolder("Assets/AjedrezArena", "Materiales");

            var texSuave = GuardarTextura(FabricaMateriales.TexturaSuave, "Tex_Suave");
            var texHaz = GuardarTextura(FabricaMateriales.TexturaHaz, "Tex_Haz");
            var texAnillo = GuardarTextura(FabricaMateriales.TexturaAnillo, "Tex_Anillo");

            var blancas = Guardar(FabricaMateriales.Lit(new Color(0.93f, 0.9f, 0.82f), 0.75f, 0f), "Piezas_Blancas");
            var negras = Guardar(FabricaMateriales.Lit(new Color(0.09f, 0.09f, 0.12f), 0.85f, 0.25f), "Piezas_Negras");
            var aditivo = Guardar(FabricaMateriales.Particula(texSuave, true), "Particula_Aditiva");
            var alfa = Guardar(FabricaMateriales.Particula(texSuave, false), "Particula_Alfa");
            var haz = Guardar(FabricaMateriales.Particula(texHaz, true), "Haz_Aditivo");
            Guardar(FabricaMateriales.Particula(texAnillo, true), "Marcador_Casilla");
            AssetDatabase.SaveAssets();

            if (arranque != null)
            {
                Undo.RecordObject(arranque, "Asignar materiales");
                arranque.materialBlancas = blancas;
                arranque.materialNegras = negras;
                arranque.materialAditivo = aditivo;
                arranque.materialAlfa = alfa;
                arranque.materialHaz = haz;
                EditorUtility.SetDirty(arranque);
            }
            Debug.Log("[Ajedrez Arena] Materiales guardados en " + CarpetaMateriales);
        }

        static Texture2D GuardarTextura(Texture2D t, string nombre)
        {
            string ruta = CarpetaMateriales + "/" + nombre + ".asset";
            var existente = AssetDatabase.LoadAssetAtPath<Texture2D>(ruta);
            if (existente != null) return existente;
            var copia = Object.Instantiate(t);
            copia.name = nombre;
            AssetDatabase.CreateAsset(copia, ruta);
            return copia;
        }

        static Material Guardar(Material m, string nombre)
        {
            string ruta = CarpetaMateriales + "/" + nombre + ".mat";
            var existente = AssetDatabase.LoadAssetAtPath<Material>(ruta);
            if (existente != null) return existente;
            m.name = nombre;
            AssetDatabase.CreateAsset(m, ruta);
            return m;
        }
    }
}
