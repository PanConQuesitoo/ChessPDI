// =====================================================================
//  IluminacionEscena.cs  -  Lee las "marcas de luz" que vienen dentro de los
//  FBX y prepara los materiales especiales.
//
//  En Blender cada escenario lleva vacíos con nombres como:
//     Luz_ffb45e_25_60_03     -> luz puntual, color #ffb45e, intensidad 2.5, rango 6.0
//     Foco_ffdca0_60_60_35_07 -> foco que apunta hacia abajo, ángulo 35°
//     FocoT_fff3e0_60_140_45_09 -> foco que apunta al centro del tablero
//  Aquí se convierten en componentes Light reales. Así las luces se diseñan
//  en Blender (junto al farol o la vitrina) y llegan solas a Unity.
//
//  Materiales:  *_Brillo -> emisivos (ventanas, neones, lava, faroles)
//               *Vidrio* -> transparentes (vitrinas, pirámide del Louvre)
// =====================================================================
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace AjedrezArena
{
    public static class IluminacionEscena
    {
        static readonly Dictionary<Material, Material> cache = new Dictionary<Material, Material>();

        /// <summary>Convierte los vacíos Luz_/Foco_/FocoT_ en luces. Devuelve cuántas creó.</summary>
        public static int CrearLuces(GameObject raiz, Vector3 centroTablero, float multiplicador = 1f)
        {
            int n = 0;
            if (raiz == null) return 0;
            foreach (var t in raiz.GetComponentsInChildren<Transform>(true))
            {
                string nombre = t.name;
                int punto = nombre.IndexOf('.');
                if (punto >= 0) nombre = nombre.Substring(0, punto);
                var p = nombre.Split('_');
                if (p.Length < 4 || (p[0] != "Luz" && p[0] != "Foco" && p[0] != "FocoT")) continue;
                Color color;
                if (!ColorUtility.TryParseHtmlString("#" + p[1], out color)) continue;
                float intensidad, rango, angulo = 60f;
                if (!float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out intensidad)) continue;
                if (!float.TryParse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture, out rango)) continue;
                if (p[0] != "Luz" && p.Length > 5) float.TryParse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture, out angulo);

                var go = new GameObject("Luz");
                go.transform.SetParent(t, false);
                go.transform.position = t.position;
                var l = go.AddComponent<Light>();
                l.color = color;
                l.intensity = intensidad / 10f * multiplicador;
                l.range = rango / 10f;
                l.shadows = LightShadows.None;
                if (p[0] == "Luz")
                {
                    l.type = LightType.Point;
                }
                else
                {
                    l.type = LightType.Spot;
                    l.spotAngle = Mathf.Clamp(angulo * 1.4f, 10f, 160f);
                    l.innerSpotAngle = l.spotAngle * 0.55f;
                    Vector3 objetivo = p[0] == "FocoT" ? centroTablero + Vector3.up * 0.4f : t.position + Vector3.down;
                    Vector3 dir = (objetivo - t.position).normalized;
                    go.transform.rotation = Quaternion.LookRotation(dir, Mathf.Abs(dir.y) > 0.95f ? Vector3.forward : Vector3.up);
                    if (p[0] == "FocoT") l.shadows = LightShadows.Soft;
                }
                n++;
            }
            return n;
        }

        /// <summary>Hace emisivos los materiales *_Brillo y transparentes los *Vidrio*.</summary>
        public static void PrepararMateriales(GameObject raiz, float fuerzaBrillo = 2.5f)
        {
            if (raiz == null) return;
            foreach (var r in raiz.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool cambio = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    bool brillo = m.name.Contains("Brillo"), vidrio = m.name.Contains("Vidrio");
                    if (!brillo && !vidrio) continue;
                    Material copia;
                    if (!cache.TryGetValue(m, out copia) || copia == null)
                    {
                        copia = new Material(m) { name = m.name + " (Ajedrez)" };
                        Color c = ColorBase(m);
                        if (brillo) FabricaMateriales.Emision(copia, c * fuerzaBrillo);
                        if (vidrio) HacerTransparente(copia, new Color(c.r, c.g, c.b, 0.28f));
                        cache[m] = copia;
                    }
                    mats[i] = copia;
                    cambio = true;
                }
                if (cambio) r.sharedMaterials = mats;
            }
        }

        public static Color ColorBase(Material m)
        {
            if (m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            if (m.HasProperty("_Color")) return m.GetColor("_Color");
            return Color.white;
        }

        public static void HacerTransparente(Material m, Color c)
        {
            if (m.HasProperty("_Surface"))
            {
                // URP Lit
                m.SetFloat("_Surface", 1f);
                if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.95f);
            }
            else if (m.HasProperty("_Mode"))
            {
                // Standard (Built-in)
                m.SetFloat("_Mode", 3f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
                m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.95f);
            }
            FabricaMateriales.ColorBase(m, c);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
    }
}
