// =====================================================================
//  FabricaMateriales.cs  -  Crea materiales y texturas por código para
//  que el proyecto funcione sin assets extra, tanto en URP como en el
//  pipeline integrado (Built-in).
// =====================================================================
using UnityEngine;
using UnityEngine.Rendering;

namespace AjedrezArena
{
    public static class FabricaMateriales
    {
        public static bool EsURP { get { return GraphicsSettings.currentRenderPipeline != null; } }

        static Shader Buscar(params string[] nombres)
        {
            foreach (var n in nombres)
            {
                var s = Shader.Find(n);
                if (s != null) return s;
            }
            return Shader.Find("Sprites/Default");
        }

        // ------------------------------------------------------------ Lit
        public static Material Lit(Color color, float suavidad = 0.5f, float metal = 0f, bool conEmision = true)
        {
            var m = new Material(EsURP ? Buscar("Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit")
                                       : Buscar("Standard"));
            ColorBase(m, color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", suavidad);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", suavidad);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            if (conEmision) Emision(m, Color.black);
            return m;
        }

        public static void ColorBase(Material m, Color c)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        public static void Emision(Material m, Color c)
        {
            if (m == null || !m.HasProperty("_EmissionColor")) return;
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", c);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        // ------------------------------------------------------- Partículas
        /// <summary>Material sin iluminación para partículas, rayos y brillos.
        /// aditivo = true suma luz (fuego, rayos); false = mezcla alfa (humo, polvo, pétalos).</summary>
        public static Material Particula(Texture textura, bool aditivo)
        {
            Material m;
            if (EsURP)
            {
                m = new Material(Buscar("Universal Render Pipeline/Particles/Unlit", "Universal Render Pipeline/Unlit"));
                SetF(m, "_Surface", 1f);
                SetF(m, "_Blend", aditivo ? 2f : 0f);
                SetF(m, "_SrcBlend", (float)BlendMode.SrcAlpha);
                SetF(m, "_DstBlend", aditivo ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
                SetF(m, "_SrcBlendAlpha", (float)BlendMode.One);
                SetF(m, "_DstBlendAlpha", aditivo ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
                SetF(m, "_ZWrite", 0f);
                SetF(m, "_Cull", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                if (aditivo) m.EnableKeyword("_BLENDMODE_ADD");
                if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", textura);
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            }
            else
            {
                m = new Material(aditivo ? Buscar("Legacy Shaders/Particles/Additive", "Particles/Additive", "Particles/Standard Unlit")
                                         : Buscar("Legacy Shaders/Particles/Alpha Blended", "Particles/Alpha Blended", "Particles/Standard Unlit"));
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", textura);
                if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
            }
            m.renderQueue = (int)RenderQueue.Transparent;
            return m;
        }

        static void SetF(Material m, string p, float v) { if (m.HasProperty(p)) m.SetFloat(p, v); }

        /// <summary>Cambia el color/tinte de un material de partículas (para resaltados).</summary>
        public static void ColorParticula(Material m, Color c)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", c * 0.5f);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        public static Material Cielo(Color tinte, Color suelo, float exposicion, float atmosfera, float tamSol)
        {
            var s = Shader.Find("Skybox/Procedural");
            if (s == null) return null;
            var m = new Material(s);
            m.SetColor("_SkyTint", tinte);
            m.SetColor("_GroundColor", suelo);
            m.SetFloat("_Exposure", exposicion);
            m.SetFloat("_AtmosphereThickness", atmosfera);
            m.SetFloat("_SunSize", tamSol);
            if (m.HasProperty("_SunDisk")) m.SetFloat("_SunDisk", 2f);
            return m;
        }

        // ----------------------------------------------------------- Texturas
        static Texture2D suave, haz, anillo;

        /// <summary>Círculo suave (centro blanco que se desvanece). Base de casi todas las partículas.</summary>
        public static Texture2D TexturaSuave
        {
            get
            {
                if (suave != null) return suave;
                const int n = 64;
                suave = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "Ajedrez_Suave", wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(1f - d);
                        a = a * a * (3f - 2f * a);
                        suave.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                suave.Apply();
                return suave;
            }
        }

        /// <summary>Franja brillante en el centro (para LineRenderer: rayos y ondas).</summary>
        public static Texture2D TexturaHaz
        {
            get
            {
                if (haz != null) return haz;
                const int w = 4, h = 64;
                haz = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "Ajedrez_Haz", wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < h; y++)
                {
                    float v = Mathf.Abs((y + 0.5f) / h * 2f - 1f);
                    float nucleo = Mathf.Clamp01(1f - v * 3f);
                    float halo = Mathf.Pow(Mathf.Clamp01(1f - v), 2f) * 0.6f;
                    float a = Mathf.Clamp01(nucleo + halo);
                    for (int x = 0; x < w; x++) haz.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
                haz.Apply();
                return haz;
            }
        }

        /// <summary>Anillo (para resaltar casillas).</summary>
        public static Texture2D TexturaAnillo
        {
            get
            {
                if (anillo != null) return anillo;
                const int n = 128;
                anillo = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "Ajedrez_Anillo", wrapMode = TextureWrapMode.Clamp };
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                        // cuadrado redondeado: borde brillante + centro tenue
                        float d = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                        float borde = Mathf.Exp(-Mathf.Pow((d - 0.86f) / 0.06f, 2f));
                        float centro = Mathf.Clamp01(1f - d) * 0.35f;
                        anillo.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(borde + centro)));
                    }
                anillo.Apply();
                return anillo;
            }
        }

        // ------------------------------------------------------- Degradados
        public static Gradient Degradado(Color[] colores, float[] alfas)
        {
            var g = new Gradient();
            var ck = new GradientColorKey[colores.Length];
            for (int i = 0; i < colores.Length; i++)
                ck[i] = new GradientColorKey(colores[i], colores.Length == 1 ? 0f : i / (float)(colores.Length - 1));
            var ak = new GradientAlphaKey[alfas.Length];
            for (int i = 0; i < alfas.Length; i++)
                ak[i] = new GradientAlphaKey(alfas[i], alfas.Length == 1 ? 0f : i / (float)(alfas.Length - 1));
            g.SetKeys(ck, ak);
            return g;
        }
    }
}
