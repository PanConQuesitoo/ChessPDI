// =====================================================================
//  PostProcesoURP.cs  -  Bloom, tonemapping ACES, ajuste de color y viñeta
//  por escenario, creados por código.
//
//  Se usa reflexión para no exigir URP: si el proyecto es Built-in, este
//  script simplemente no hace nada (y compila igual).
//  Analogía: es como los filtros de una cámara de fotos; no cambian la
//  escena, cambian cómo la "revela" la cámara (el bloom hace que lo muy
//  brillante —fuego, rayos, neones— se derrame en un halo suave).
// =====================================================================
using System;
using System.Reflection;
using UnityEngine;

namespace AjedrezArena
{
    [Serializable]
    public class ParametrosPost
    {
        [Range(0f, 3f)] public float bloom = 0.6f;
        [Range(0f, 1.5f)] public float umbralBloom = 0.95f;
        [Range(-2f, 2f)] public float exposicion = 0f;
        [Range(-50f, 50f)] public float contraste = 10f;
        [Range(-50f, 50f)] public float saturacion = 8f;
        public Color filtro = Color.white;
        [Range(0f, 0.6f)] public float vineta = 0.22f;

        public ParametrosPost() { }
        public ParametrosPost(float bloom, float exposicion, float contraste, float saturacion, float vineta, Color filtro)
        {
            this.bloom = bloom; this.exposicion = exposicion; this.contraste = contraste;
            this.saturacion = saturacion; this.vineta = vineta; this.filtro = filtro;
        }
    }

    public static class PostProcesoURP
    {
        static bool buscado;
        static Type tVolume, tProfile, tBloom, tTonemapping, tColor, tVignette, tCamData;
        static Component volumen;
        static ScriptableObject perfil;
        static object cBloom, cTono, cColor, cVineta;

        static Type Tipo(string nombre)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType(nombre, false);
                if (t != null) return t;
            }
            return null;
        }

        public static bool Disponible
        {
            get
            {
                if (!buscado)
                {
                    buscado = true;
                    tVolume = Tipo("UnityEngine.Rendering.Volume");
                    tProfile = Tipo("UnityEngine.Rendering.VolumeProfile");
                    tBloom = Tipo("UnityEngine.Rendering.Universal.Bloom");
                    tTonemapping = Tipo("UnityEngine.Rendering.Universal.Tonemapping");
                    tColor = Tipo("UnityEngine.Rendering.Universal.ColorAdjustments");
                    tVignette = Tipo("UnityEngine.Rendering.Universal.Vignette");
                    tCamData = Tipo("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData");
                }
                return FabricaMateriales.EsURP && tVolume != null && tProfile != null && tBloom != null;
            }
        }

        static void Asignar(object obj, string miembro, object valor)
        {
            if (obj == null) return;
            var t = obj.GetType();
            var p = t.GetProperty(miembro, BindingFlags.Public | BindingFlags.Instance);
            if (p != null && p.CanWrite) { p.SetValue(obj, Convertir(valor, p.PropertyType), null); return; }
            var f = t.GetField(miembro, BindingFlags.Public | BindingFlags.Instance);
            if (f != null) f.SetValue(obj, Convertir(valor, f.FieldType));
        }

        static object Convertir(object v, Type destino)
        {
            if (v == null) return null;
            if (destino.IsEnum) return Enum.ToObject(destino, v);
            if (destino.IsInstanceOfType(v)) return v;
            try { return Convert.ChangeType(v, destino); } catch (Exception) { return v; }
        }

        /// <summary>Asigna un VolumeParameter (p. ej. bloom.intensity) y marca su override.</summary>
        static void Parametro(object componente, string campo, object valor)
        {
            if (componente == null) return;
            var f = componente.GetType().GetField(campo, BindingFlags.Public | BindingFlags.Instance);
            if (f == null) return;
            var par = f.GetValue(componente);
            if (par == null) return;
            Asignar(par, "overrideState", true);
            var pv = par.GetType().GetProperty("value", BindingFlags.Public | BindingFlags.Instance);
            if (pv != null) pv.SetValue(par, Convertir(valor, pv.PropertyType), null);
        }

        static object Componente(Type t)
        {
            if (t == null || perfil == null) return null;
            var add = tProfile.GetMethod("Add", new[] { typeof(Type), typeof(bool) });
            var tieneGen = tProfile.GetMethod("Has", new[] { typeof(Type) });
            if (tieneGen != null && (bool)tieneGen.Invoke(perfil, new object[] { t }))
            {
                var tryGet = tProfile.GetMethod("TryGet", new[] { typeof(Type), t.MakeByRefType() });
                if (tryGet != null)
                {
                    var args = new object[] { t, null };
                    tryGet.Invoke(perfil, args);
                    return args[1];
                }
            }
            return add != null ? add.Invoke(perfil, new object[] { t, true }) : null;
        }

        public static void Aplicar(Camera camara, ParametrosPost p, Transform padre)
        {
            if (p == null || !Disponible) return;
            try
            {
                if (volumen == null)
                {
                    var go = new GameObject("PostProceso");
                    if (padre != null) go.transform.SetParent(padre, false);
                    volumen = go.AddComponent(tVolume);
                    perfil = ScriptableObject.CreateInstance(tProfile);
                    perfil.name = "PerfilAjedrez";
                    Asignar(volumen, "isGlobal", true);
                    Asignar(volumen, "priority", 50f);
                    Asignar(volumen, "sharedProfile", perfil);
                    cBloom = Componente(tBloom);
                    cTono = Componente(tTonemapping);
                    cColor = Componente(tColor);
                    cVineta = Componente(tVignette);
                }
                Parametro(cBloom, "intensity", p.bloom);
                Parametro(cBloom, "threshold", p.umbralBloom);
                Parametro(cBloom, "scatter", 0.7f);
                Parametro(cTono, "mode", 2);                 // TonemappingMode.ACES
                Parametro(cColor, "postExposure", p.exposicion);
                Parametro(cColor, "contrast", p.contraste);
                Parametro(cColor, "saturation", p.saturacion);
                Parametro(cColor, "colorFilter", p.filtro);
                Parametro(cVineta, "intensity", p.vineta);
                Parametro(cVineta, "smoothness", 0.45f);
                if (camara != null && tCamData != null)
                {
                    var datos = camara.GetComponent(tCamData);
                    if (datos == null) datos = camara.gameObject.AddComponent(tCamData);
                    Asignar(datos, "renderPostProcessing", true);
                    Asignar(datos, "antialiasing", 1);       // FXAA
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Ajedrez] No se pudo configurar el post-proceso URP: " + e.Message);
            }
        }
    }
}
