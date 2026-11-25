// =====================================================================
//  EfectosEnMovimiento.cs  -  Lo que acompaña a la pieza mientras la
//  mano la lleva: estela (TrailRenderer) que cambia de color según la
//  velocidad, fuego cuando vas rápido, chispas eléctricas cuando sacudes
//  y un aura que se va cargando si mantienes la pieza quieta.
// =====================================================================
using UnityEngine;

namespace AjedrezArena
{
    public class EfectosEnMovimiento : MonoBehaviour
    {
        EfectosAjedrez efectos;
        PiezaVisual pieza;
        TrailRenderer estela;
        ParticleSystem fuego, chispas, aura;
        Light luzAura;
        AudioSource zumbido;
        bool activo;

        static readonly Color ColorCalma = new Color(1f, 0.95f, 0.8f);
        static readonly Color ColorFuego = new Color(1f, 0.45f, 0.08f);
        static readonly Color ColorRayo = new Color(0.45f, 0.8f, 1f);
        static readonly Color ColorCarga = new Color(0.85f, 0.45f, 1f);

        public void Iniciar(EfectosAjedrez fx, PiezaVisual p)
        {
            efectos = fx;
            pieza = p;
            activo = true;
            Vector3 centro = p.Limites().center;

            if (estela == null)
            {
                var go = new GameObject("Estela");
                go.transform.SetParent(transform, false);
                go.transform.position = centro;
                estela = go.AddComponent<TrailRenderer>();
                estela.material = fx.materialHaz;
                estela.time = 0.35f;
                estela.minVertexDistance = 0.05f;
                estela.widthMultiplier = 0.35f;
                estela.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
                estela.numCapVertices = 2;
                estela.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                estela.alignment = LineAlignment.View;
            }
            estela.Clear();
            estela.emitting = true;

            if (fuego == null)
            {
                fuego = fx.NuevoSistema("FuegoPieza", centro, fx.materialAditivo, 1f, true, transform);
                var m = fuego.main;
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 0.8f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
                m.gravityModifier = -0.8f;
                var sh = fuego.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.25f;
                var col = fuego.colorOverLifetime; col.enabled = true;
                col.color = FabricaMateriales.Degradado(
                    new[] { new Color(1f, 0.95f, 0.6f), ColorFuego, new Color(0.7f, 0.08f, 0.02f) }, new[] { 0.9f, 0.7f, 0f });
                var sol = fuego.sizeOverLifetime; sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.1f));
                fuego.Play();
            }
            if (chispas == null)
            {
                chispas = fx.NuevoSistema("ChispasPieza", centro, fx.materialAditivo, 1f, true, transform);
                var m = chispas.main;
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(3f, 7f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
                m.startColor = new ParticleSystem.MinMaxGradient(Color.white, ColorRayo);
                var sh = chispas.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.3f;
                var r = chispas.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.04f; r.lengthScale = 1.5f;
                chispas.Play();
            }
            if (aura == null)
            {
                aura = fx.NuevoSistema("AuraCarga", centro, fx.materialAditivo, 1f, true, transform);
                var m = aura.main;
                m.startLifetime = 0.45f;
                m.startSpeed = -2.2f;      // velocidad negativa: las partículas van HACIA el centro
                m.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
                m.startColor = new ParticleSystem.MinMaxGradient(ColorCarga, ColorRayo);
                m.simulationSpace = ParticleSystemSimulationSpace.Local;
                var sh = aura.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 1.0f; sh.radiusThickness = 0f;
                var col = aura.colorOverLifetime; col.enabled = true;
                col.color = FabricaMateriales.Degradado(new[] { Color.white, Color.white }, new[] { 0f, 1f });
                aura.Play();
                var lgo = new GameObject("LuzAura");
                lgo.transform.SetParent(aura.transform, false);
                luzAura = lgo.AddComponent<Light>();
                luzAura.type = LightType.Point;
                luzAura.range = 4f;
                luzAura.intensity = 0f;
                luzAura.shadows = LightShadows.None;
                zumbido = gameObject.AddComponent<AudioSource>();
                zumbido.clip = fx.sonidoCarga;
                zumbido.loop = true;
                zumbido.volume = 0f;
                zumbido.playOnAwake = false;
                zumbido.spatialBlend = 0f;
            }
            Emision(fuego, 0f); Emision(chispas, 0f); Emision(aura, 0f);
        }

        static void Emision(ParticleSystem ps, float tasa)
        {
            if (ps == null) return;
            var em = ps.emission;
            em.rateOverTime = tasa;
        }

        /// <summary>Llamar cada cuadro mientras se lleva la pieza.</summary>
        public void Actualizar(float rapidez, float aceleracion, float carga, float umbralFuego, float umbralRayo, float umbralAcel)
        {
            if (!activo) return;
            // ---- estela: color según energía
            float kFuego = Mathf.InverseLerp(umbralFuego * 0.6f, umbralFuego, rapidez);
            float kRayo = Mathf.Max(Mathf.InverseLerp(umbralRayo * 0.75f, umbralRayo, rapidez),
                                    Mathf.InverseLerp(umbralAcel * 0.6f, umbralAcel, aceleracion));
            Color c = Color.Lerp(ColorCalma, ColorFuego, kFuego);
            c = Color.Lerp(c, ColorRayo, kRayo);
            c = Color.Lerp(c, ColorCarga, carga);
            float alfa = Mathf.Clamp01(0.15f + rapidez / Mathf.Max(0.01f, umbralFuego) * 0.6f + carga);
            estela.startColor = new Color(c.r, c.g, c.b, alfa);
            estela.endColor = new Color(c.r, c.g, c.b, 0f);
            estela.widthMultiplier = 0.25f + 0.35f * Mathf.Max(kFuego, kRayo) + 0.3f * carga;

            // ---- fuego y chispas
            Emision(fuego, kFuego * (1f - kRayo * 0.5f) * 90f);
            Emision(chispas, kRayo * 120f);

            // ---- aura de carga
            Emision(aura, carga > 0.02f ? 30f + carga * 140f : 0f);
            if (aura != null)
            {
                float temblor = carga * 0.12f;
                aura.transform.localScale = Vector3.one * (1.2f - carga * 0.5f);
                aura.transform.position = pieza.Limites().center + Random.insideUnitSphere * temblor * 0.3f;
            }
            if (luzAura != null)
            {
                luzAura.color = Color.Lerp(ColorRayo, ColorCarga, carga);
                luzAura.intensity = carga * carga * 6f;
            }
            if (zumbido != null)
            {
                if (carga > 0.05f && !zumbido.isPlaying) zumbido.Play();
                zumbido.volume = carga * 0.35f * (efectos != null ? efectos.volumen : 1f);
                zumbido.pitch = 0.6f + carga * 1.4f;
                if (carga <= 0.05f && zumbido.isPlaying) zumbido.Stop();
            }
        }

        public void Detener()
        {
            activo = false;
            if (estela != null) estela.emitting = false;
            Emision(fuego, 0f); Emision(chispas, 0f); Emision(aura, 0f);
            if (luzAura != null) luzAura.intensity = 0f;
            if (zumbido != null) zumbido.Stop();
        }
    }
}
