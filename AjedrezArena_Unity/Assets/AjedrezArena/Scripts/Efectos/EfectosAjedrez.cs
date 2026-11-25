// =====================================================================
//  EfectosAjedrez.cs  -  Biblioteca de efectos visuales y sonoros.
//  Todo se construye por código (ParticleSystem, LineRenderer, Light),
//  así que no necesitas prefabs de partículas.
//
//  Tipos de golpe (los decide ControladorMano según velocidad/aceleración):
//    Luz       -> movimiento suave: destello dorado y chispitas
//    Fuego     -> movimiento rápido: llamarada, brasas y anillo de fuego
//    Rayo      -> sacudida brusca (aceleración alta): rayos desde el cielo
//    Supernova -> pieza cargada (quieta un rato): onda de choque gigante
// =====================================================================
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace AjedrezArena
{
    public enum TipoEfecto { Luz, Fuego, Rayo, Supernova }

    /// <summary>Resumen del movimiento de la mano al soltar la pieza.</summary>
    public struct Impulso
    {
        public TipoEfecto Tipo;
        public float Energia;       // 0..1 (intensidad del efecto)
        public float Rapidez;       // unidades/s al soltar
        public float Aceleracion;   // pico de aceleración reciente (u/s²)
        public float TiempoAgarre;  // s que se sostuvo la pieza
        public float Carga;         // 0..1 (energía acumulada estando quieto)
        public Vector3 Direccion;   // dirección del movimiento (plano del tablero)

        public static Impulso Suave(float energia = 0.2f)
        {
            return new Impulso { Tipo = TipoEfecto.Luz, Energia = energia, Direccion = Vector3.forward };
        }

        public override string ToString()
        {
            return string.Format("{0}  energía {1:0.00} | v {2:0.0} u/s | a {3:0} u/s² | carga {4:0.00} | {5:0.0}s",
                Tipo, Energia, Rapidez, Aceleracion, Carga, TiempoAgarre);
        }
    }

    public enum TipoAmbiente { Ninguno, Neblina, PetalosSakura, Arena, NieveAurora, BrillosDorados, Luciernagas, MotasPolvo }

    public class EfectosAjedrez : MonoBehaviour
    {
        public static EfectosAjedrez Instancia { get; private set; }

        [Header("Materiales (opcional: se crean solos si están vacíos)")]
        public Material materialAditivo;
        public Material materialAlfa;
        public Material materialHaz;

        [Header("Sonidos (opcional: se sintetizan si están vacíos)")]
        public AudioClip sonidoGolpe;
        public AudioClip sonidoFuego;
        public AudioClip sonidoRayo;
        public AudioClip sonidoLuz;
        public AudioClip sonidoSupernova;
        public AudioClip sonidoAgarrar;
        public AudioClip sonidoSoltar;
        public AudioClip sonidoError;
        public AudioClip sonidoCarga;
        [Range(0f, 1f)] public float volumen = 0.8f;

        [Header("Escala general de los efectos")]
        public float escala = 1f;

        AudioSource fuenteAudio;

        void Awake()
        {
            if (Instancia != null && Instancia != this) { Destroy(this); return; }
            Instancia = this;
            if (materialAditivo == null) materialAditivo = FabricaMateriales.Particula(FabricaMateriales.TexturaSuave, true);
            if (materialAlfa == null) materialAlfa = FabricaMateriales.Particula(FabricaMateriales.TexturaSuave, false);
            if (materialHaz == null) materialHaz = FabricaMateriales.Particula(FabricaMateriales.TexturaHaz, true);
            if (sonidoGolpe == null) sonidoGolpe = SonidosProcedurales.Golpe();
            if (sonidoFuego == null) sonidoFuego = SonidosProcedurales.Fuego();
            if (sonidoRayo == null) sonidoRayo = SonidosProcedurales.Rayo();
            if (sonidoLuz == null) sonidoLuz = SonidosProcedurales.Luz();
            if (sonidoSupernova == null) sonidoSupernova = SonidosProcedurales.Supernova();
            if (sonidoAgarrar == null) sonidoAgarrar = SonidosProcedurales.Pop(1.2f);
            if (sonidoSoltar == null) sonidoSoltar = SonidosProcedurales.Pop(0.8f);
            if (sonidoError == null) sonidoError = SonidosProcedurales.Error();
            if (sonidoCarga == null) sonidoCarga = SonidosProcedurales.Carga();
            fuenteAudio = gameObject.AddComponent<AudioSource>();
            fuenteAudio.playOnAwake = false;
            fuenteAudio.spatialBlend = 0f;
        }

        public void Sonar(AudioClip clip, float vol = 1f, float tono = 1f)
        {
            if (clip == null || fuenteAudio == null) return;
            fuenteAudio.pitch = tono;
            fuenteAudio.PlayOneShot(clip, vol * volumen);
        }

        // =================================================================
        //  IMPACTO PRINCIPAL (cuando la pieza "aterriza" con fuerza)
        // =================================================================
        public void Impacto(Vector3 pos, Impulso imp, bool esCaptura)
        {
            float e = Mathf.Clamp01(imp.Energia) * (esCaptura ? 1.25f : 1f);
            float s = escala;
            Polvo(pos, 0.5f + e);
            OndaChoque(pos, ColorDe(imp.Tipo), (1.2f + e * 2.5f) * s, 0.45f + e * 0.2f, 0.12f + e * 0.15f);
            float temblor = 0.05f + e * 0.2f;
            switch (imp.Tipo)
            {
                case TipoEfecto.Luz:
                    Luz(pos, e);
                    Sonar(sonidoLuz, 0.5f + e * 0.5f, 1f + Random.Range(-0.05f, 0.05f));
                    break;
                case TipoEfecto.Fuego:
                    Fuego(pos, e);
                    Sonar(sonidoFuego, 0.7f + e * 0.3f);
                    temblor += 0.1f;
                    break;
                case TipoEfecto.Rayo:
                    Rayos(pos, e);
                    Sonar(sonidoRayo, 0.8f + e * 0.2f, 1f + Random.Range(-0.1f, 0.1f));
                    temblor += 0.25f;
                    break;
                case TipoEfecto.Supernova:
                    Supernova(pos, Mathf.Max(e, 0.8f));
                    Sonar(sonidoSupernova, 1f);
                    temblor += 0.45f;
                    break;
            }
            Sonar(sonidoGolpe, 0.4f + e * 0.6f, 1.1f - e * 0.3f);
            CamaraAjedrez.Temblar(temblor * s, 0.25f + e * 0.35f);
        }

        public static Color ColorDe(TipoEfecto t)
        {
            switch (t)
            {
                case TipoEfecto.Fuego: return new Color(1f, 0.45f, 0.1f);
                case TipoEfecto.Rayo: return new Color(0.45f, 0.75f, 1f);
                case TipoEfecto.Supernova: return new Color(0.85f, 0.5f, 1f);
                default: return new Color(1f, 0.9f, 0.6f);
            }
        }

        // =================================================================
        //  CONSTRUCCIÓN DE PARTÍCULAS
        // =================================================================
        public ParticleSystem NuevoSistema(string nombre, Vector3 pos, Material mat, float duracion, bool bucle,
                                            Transform padre = null)
        {
            var go = new GameObject(nombre);
            if (padre != null) go.transform.SetParent(padre, false);
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = duracion;
            main.loop = bucle;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = 3000;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            if (!bucle) main.stopAction = ParticleSystemStopAction.Destroy;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var sh = ps.shape;
            sh.enabled = true;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.1f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.material = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.maxParticleSize = 3f;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        static void Rafaga(ParticleSystem ps, int cantidad, float tiempo = 0f)
        {
            var em = ps.emission;
            em.SetBursts(new[] { new ParticleSystem.Burst(tiempo, (short)Mathf.Clamp(cantidad, 1, 30000)) });
        }

        static void ColorVida(ParticleSystem ps, Color[] colores, float[] alfas)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(FabricaMateriales.Degradado(colores, alfas));
        }

        static void TamanoVida(ParticleSystem ps, float inicio, float fin)
        {
            var sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, inicio, 1f, fin));
        }

        static void Ruido(ParticleSystem ps, float fuerza, float frecuencia)
        {
            var n = ps.noise;
            n.enabled = true;
            n.strength = fuerza;
            n.frequency = frecuencia;
            n.scrollSpeed = 0.5f;
            n.damping = true;
        }

        static void Arrastre(ParticleSystem ps, float arrastre)
        {
            var lv = ps.limitVelocityOverLifetime;
            lv.enabled = true;
            lv.limit = 1000f;
            lv.drag = arrastre;
            lv.multiplyDragByParticleSize = false;
            lv.multiplyDragByParticleVelocity = true;
        }

        static void Estirar(ParticleSystem ps, float escalaVelocidad, float escalaLargo)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = escalaVelocidad;
            r.lengthScale = escalaLargo;
        }

        // =================================================================
        //  EFECTOS BÁSICOS
        // =================================================================
        public void Polvo(Vector3 pos, float fuerza)
        {
            var ps = NuevoSistema("Polvo", pos + Vector3.up * 0.05f, materialAlfa, 0.2f, false);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f * fuerza, 3.5f * fuerza);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.78f, 0.65f, 0.55f), new Color(0.7f, 0.65f, 0.55f, 0.4f));
            main.gravityModifier = -0.05f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Circle;
            sh.radius = 0.3f;
            sh.radiusThickness = 0f;
            sh.rotation = new Vector3(90f, 0f, 0f);
            Rafaga(ps, Mathf.RoundToInt(14 + 30 * fuerza));
            Arrastre(ps, 3f);
            TamanoVida(ps, 0.6f, 1.6f);
            ColorVida(ps, new[] { Color.white, Color.white }, new[] { 1f, 0f });
            ps.Play();
        }

        /// <summary>Anillo que se expande sobre el tablero.</summary>
        public void OndaChoque(Vector3 pos, Color color, float radioFinal, float duracion, float ancho)
        {
            StartCoroutine(CorOnda(pos + Vector3.up * 0.03f, color, radioFinal, duracion, ancho));
        }

        IEnumerator CorOnda(Vector3 pos, Color color, float radioFinal, float duracion, float ancho)
        {
            var go = new GameObject("OndaChoque");
            var lr = go.AddComponent<LineRenderer>();
            ConfigurarLinea(lr, ancho);
            const int seg = 48;
            lr.positionCount = seg;
            lr.loop = true;
            var pts = new Vector3[seg];
            float t = 0f;
            while (t < duracion)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duracion);
                float radio = Mathf.Lerp(0.2f, radioFinal, 1f - (1f - k) * (1f - k));
                for (int i = 0; i < seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2f;
                    pts[i] = pos + new Vector3(Mathf.Cos(a) * radio, 0f, Mathf.Sin(a) * radio);
                }
                lr.SetPositions(pts);
                Color c = color; c.a = 1f - k;
                lr.startColor = c; lr.endColor = c;
                lr.widthMultiplier = ancho * (1f - k * 0.6f);
                yield return null;
            }
            Destroy(go);
        }

        void ConfigurarLinea(LineRenderer lr, float ancho)
        {
            lr.material = materialHaz;
            lr.useWorldSpace = true;
            lr.widthMultiplier = ancho;
            lr.numCapVertices = 2;
            lr.numCornerVertices = 2;
            lr.textureMode = LineTextureMode.Stretch;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.alignment = LineAlignment.View;
        }

        /// <summary>Luz puntual que se apaga (destello).</summary>
        public void Destello(Vector3 pos, Color color, float intensidad, float rango, float duracion, bool parpadeo = false)
        {
            StartCoroutine(CorDestello(pos, color, intensidad, rango, duracion, parpadeo));
        }

        IEnumerator CorDestello(Vector3 pos, Color color, float intensidad, float rango, float duracion, bool parpadeo)
        {
            var go = new GameObject("Destello");
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.range = rango;
            l.shadows = LightShadows.None;
            float t = 0f;
            while (t < duracion)
            {
                t += Time.deltaTime;
                float k = 1f - Mathf.Clamp01(t / duracion);
                float f = parpadeo ? Mathf.Lerp(0.6f, 1.2f, Mathf.PerlinNoise(t * 18f, 0.3f)) : 1f;
                l.intensity = intensidad * k * k * f;
                yield return null;
            }
            Destroy(go);
        }

        // =================================================================
        //  LUZ
        // =================================================================
        public void Luz(Vector3 pos, float e)
        {
            Color oro = new Color(1f, 0.88f, 0.55f);
            // halo central
            var halo = NuevoSistema("Luz_Halo", pos + Vector3.up * 0.4f, materialAditivo, 0.1f, false);
            var m = halo.main;
            m.startLifetime = 0.6f;
            m.startSpeed = 0f;
            m.startSize = 1.5f + e * 2.5f;
            m.startColor = oro;
            Rafaga(halo, 2);
            TamanoVida(halo, 0.4f, 1.3f);
            ColorVida(halo, new[] { Color.white, oro }, new[] { 1f, 0f });
            halo.Play();
            // chispitas que suben girando
            var ch = NuevoSistema("Luz_Chispas", pos + Vector3.up * 0.2f, materialAditivo, 0.3f, false);
            m = ch.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2f + e * 2f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
            m.startColor = new ParticleSystem.MinMaxGradient(Color.white, oro);
            m.gravityModifier = -0.15f;
            var sh = ch.shape; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 0.4f;
            sh.rotation = new Vector3(-90f, 0f, 0f);
            Rafaga(ch, Mathf.RoundToInt(25 + 60 * e));
            Ruido(ch, 0.6f, 1.2f);
            Arrastre(ch, 1.2f);
            ColorVida(ch, new[] { Color.white, oro, oro }, new[] { 1f, 1f, 0f });
            ch.Play();
            // columna de luz suave
            StartCoroutine(CorColumna(pos, oro, 0.4f + e * 0.5f, 2.5f + e * 3f, 0.5f));
            Destello(pos + Vector3.up * 0.8f, oro, 2.5f + e * 3f, 4f + e * 3f, 0.6f);
        }

        IEnumerator CorColumna(Vector3 pos, Color color, float ancho, float alto, float duracion)
        {
            var go = new GameObject("ColumnaLuz");
            var lr = go.AddComponent<LineRenderer>();
            ConfigurarLinea(lr, ancho);
            lr.positionCount = 2;
            float t = 0f;
            while (t < duracion)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duracion);
                lr.SetPosition(0, pos);
                lr.SetPosition(1, pos + Vector3.up * alto * Mathf.Sqrt(k));
                Color a = color; a.a = (1f - k);
                Color b = color; b.a = 0f;
                lr.startColor = a; lr.endColor = b;
                lr.widthMultiplier = ancho * (1f - k * 0.5f);
                yield return null;
            }
            Destroy(go);
        }

        // =================================================================
        //  FUEGO
        // =================================================================
        public void Fuego(Vector3 pos, float e)
        {
            Color amarillo = new Color(1f, 0.9f, 0.5f), naranja = new Color(1f, 0.45f, 0.05f),
                  rojo = new Color(0.8f, 0.1f, 0.02f), humo = new Color(0.15f, 0.12f, 0.1f);
            // llamarada central
            var ll = NuevoSistema("Fuego_Llamas", pos + Vector3.up * 0.15f, materialAditivo, 0.35f, false);
            var m = ll.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.0f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f + e * 3f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.4f + e * 0.3f, 0.9f + e * 0.6f);
            m.startColor = Color.white;
            m.gravityModifier = -0.6f;
            var sh = ll.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 30f; sh.radius = 0.35f;
            sh.rotation = new Vector3(-90f, 0f, 0f);
            Rafaga(ll, Mathf.RoundToInt(40 + 110 * e));
            Ruido(ll, 0.8f, 1.5f);
            TamanoVida(ll, 1f, 0.15f);
            ColorVida(ll, new[] { amarillo, naranja, rojo, humo }, new[] { 1f, 0.9f, 0.5f, 0f });
            ll.Play();
            // anillo de fuego a ras del tablero
            var an = NuevoSistema("Fuego_Anillo", pos + Vector3.up * 0.1f, materialAditivo, 0.15f, false);
            m = an.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(3f + e * 3f, 5f + e * 5f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            m.gravityModifier = -0.3f;
            sh = an.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.3f; sh.radiusThickness = 0f;
            sh.rotation = new Vector3(90f, 0f, 0f);
            Rafaga(an, Mathf.RoundToInt(40 + 80 * e));
            Arrastre(an, 2.5f);
            TamanoVida(an, 1f, 0.2f);
            ColorVida(an, new[] { amarillo, naranja, rojo }, new[] { 1f, 0.8f, 0f });
            an.Play();
            // brasas (chispas estiradas que caen)
            var br = NuevoSistema("Fuego_Brasas", pos + Vector3.up * 0.4f, materialAditivo, 0.2f, false);
            m = br.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(3f, 7f + e * 5f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            m.startColor = new ParticleSystem.MinMaxGradient(amarillo, naranja);
            m.gravityModifier = 0.6f;
            sh = br.shape; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 0.2f; sh.rotation = new Vector3(-90f, 0f, 0f);
            Rafaga(br, Mathf.RoundToInt(30 + 70 * e));
            Estirar(br, 0.06f, 1.5f);
            ColorVida(br, new[] { amarillo, naranja, rojo }, new[] { 1f, 1f, 0f });
            br.Play();
            // humo
            var hu = NuevoSistema("Fuego_Humo", pos + Vector3.up * 0.8f, materialAlfa, 0.4f, false);
            m = hu.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.2f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
            m.startColor = new Color(0.2f, 0.18f, 0.16f, 0.5f);
            m.gravityModifier = -0.15f;
            Rafaga(hu, Mathf.RoundToInt(6 + 14 * e), 0.1f);
            Ruido(hu, 0.4f, 0.5f);
            TamanoVida(hu, 0.6f, 2f);
            ColorVida(hu, new[] { Color.white, Color.white }, new[] { 0f, 0.6f, 0f });
            hu.Play();
            Destello(pos + Vector3.up * 0.6f, naranja, 4f + e * 5f, 5f + e * 4f, 0.9f, true);
        }

        // =================================================================
        //  RAYOS
        // =================================================================
        public void Rayos(Vector3 pos, float e)
        {
            Color celeste = new Color(0.55f, 0.8f, 1f);
            int n = 2 + Mathf.RoundToInt(e * 3f);
            for (int i = 0; i < n; i++)
            {
                Vector3 cielo = pos + new Vector3(Random.Range(-2.5f, 2.5f), Random.Range(8f, 12f), Random.Range(-2.5f, 2.5f));
                Vector3 suelo = pos + new Vector3(Random.Range(-0.15f, 0.15f), 0.05f, Random.Range(-0.15f, 0.15f));
                StartCoroutine(CorRayo(cielo, suelo, celeste, 0.12f + e * 0.15f, 0.35f + i * 0.05f, i * 0.04f, true));
            }
            // arcos eléctricos que se arrastran alrededor de la pieza
            StartCoroutine(CorArcos(pos, celeste, 0.6f + e * 0.8f, 0.9f + e));
            // chispas eléctricas
            var ch = NuevoSistema("Rayo_Chispas", pos + Vector3.up * 0.2f, materialAditivo, 0.1f, false);
            var m = ch.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(5f, 12f + e * 8f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            m.startColor = new ParticleSystem.MinMaxGradient(Color.white, celeste);
            m.gravityModifier = 1f;
            var sh = ch.shape; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 0.2f; sh.rotation = new Vector3(-90f, 0f, 0f);
            Rafaga(ch, Mathf.RoundToInt(50 + 120 * e));
            Estirar(ch, 0.04f, 2f);
            Arrastre(ch, 1.5f);
            ch.Play();
            // fogonazo
            var fg = NuevoSistema("Rayo_Fogonazo", pos + Vector3.up * 0.5f, materialAditivo, 0.05f, false);
            m = fg.main;
            m.startLifetime = 0.25f; m.startSpeed = 0f; m.startSize = 3f + e * 3f; m.startColor = Color.white;
            Rafaga(fg, 1);
            TamanoVida(fg, 0.5f, 1.5f);
            ColorVida(fg, new[] { Color.white, celeste }, new[] { 1f, 0f });
            fg.Play();
            Destello(pos + Vector3.up * 2f, celeste, 10f + e * 10f, 14f + e * 8f, 0.4f, true);
        }

        /// <summary>Rayo zigzag con desplazamiento del punto medio (fractal), que parpadea.</summary>
        IEnumerator CorRayo(Vector3 a, Vector3 b, Color color, float ancho, float duracion, float retardo, bool ramas)
        {
            if (retardo > 0f) yield return new WaitForSeconds(retardo);
            var go = new GameObject("Rayo");
            var lr = go.AddComponent<LineRenderer>();
            ConfigurarLinea(lr, ancho);
            float t = 0f, siguiente = 0f;
            bool ramaCreada = false;
            while (t < duracion)
            {
                t += Time.deltaTime;
                if (t >= siguiente)
                {
                    siguiente = t + 0.045f;
                    var pts = Zigzag(a, b, 5, (a - b).magnitude * 0.12f);
                    lr.positionCount = pts.Count;
                    lr.SetPositions(pts.ToArray());
                    if (ramas && !ramaCreada && pts.Count > 8)
                    {
                        ramaCreada = true;
                        Vector3 inicio = pts[pts.Count / 3];
                        Vector3 fin = inicio + new Vector3(Random.Range(-2f, 2f), -Random.Range(1.5f, 3f), Random.Range(-2f, 2f));
                        StartCoroutine(CorRayo(inicio, fin, color, ancho * 0.5f, duracion * 0.7f, 0f, false));
                    }
                }
                float k = Mathf.Clamp01(t / duracion);
                float brillo = (Random.value > 0.25f ? 1f : 0.3f) * (1f - k * k);
                Color c = color; c.a = brillo;
                Color w = Color.white; w.a = brillo;
                lr.startColor = c; lr.endColor = w;
                yield return null;
            }
            Destroy(go);
        }

        static List<Vector3> Zigzag(Vector3 a, Vector3 b, int niveles, float desplazamiento)
        {
            var pts = new List<Vector3> { a, b };
            for (int n = 0; n < niveles; n++)
            {
                var nuevos = new List<Vector3>(pts.Count * 2);
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    nuevos.Add(pts[i]);
                    Vector3 medio = (pts[i] + pts[i + 1]) * 0.5f;
                    medio += Random.insideUnitSphere * desplazamiento;
                    nuevos.Add(medio);
                }
                nuevos.Add(pts[pts.Count - 1]);
                pts = nuevos;
                desplazamiento *= 0.5f;
            }
            return pts;
        }

        IEnumerator CorArcos(Vector3 centro, Color color, float radio, float duracion)
        {
            var lineas = new LineRenderer[3];
            for (int i = 0; i < lineas.Length; i++)
            {
                var go = new GameObject("ArcoElectrico");
                lineas[i] = go.AddComponent<LineRenderer>();
                ConfigurarLinea(lineas[i], 0.05f);
            }
            float t = 0f, siguiente = 0f;
            while (t < duracion)
            {
                t += Time.deltaTime;
                if (t >= siguiente)
                {
                    siguiente = t + 0.06f;
                    foreach (var lr in lineas)
                    {
                        Vector3 p1 = centro + Vector3.up * Random.Range(0.1f, 1.2f) + Random.insideUnitSphere * radio * 0.3f;
                        Vector2 d = Random.insideUnitCircle.normalized * radio;
                        Vector3 p2 = centro + new Vector3(d.x, 0.03f, d.y);
                        var pts = Zigzag(p1, p2, 3, 0.15f);
                        lr.positionCount = pts.Count;
                        lr.SetPositions(pts.ToArray());
                    }
                }
                float k = 1f - Mathf.Clamp01(t / duracion);
                foreach (var lr in lineas)
                {
                    Color c = color; c.a = k * (Random.value > 0.3f ? 1f : 0f);
                    lr.startColor = c; lr.endColor = c;
                }
                yield return null;
            }
            foreach (var lr in lineas) Destroy(lr.gameObject);
        }

        // =================================================================
        //  SUPERNOVA (pieza cargada)
        // =================================================================
        public void Supernova(Vector3 pos, float e)
        {
            Color violeta = new Color(0.8f, 0.45f, 1f), cian = new Color(0.4f, 0.95f, 1f), blanco = Color.white;
            OndaChoque(pos, violeta, 5f + e * 4f, 0.7f, 0.35f);
            StartCoroutine(Retrasar(0.08f, () => OndaChoque(pos, cian, 4f + e * 3f, 0.6f, 0.25f)));
            StartCoroutine(Retrasar(0.16f, () => OndaChoque(pos, blanco, 7f + e * 3f, 0.9f, 0.15f)));
            StartCoroutine(CorColumna(pos, violeta, 1.2f + e, 18f, 0.9f));
            // estallido radial de chispas
            var ex = NuevoSistema("Supernova_Estallido", pos + Vector3.up * 0.3f, materialAditivo, 0.1f, false);
            var m = ex.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(8f, 18f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
            m.startColor = new ParticleSystem.MinMaxGradient(violeta, cian);
            m.gravityModifier = 0.3f;
            var sh = ex.shape; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 0.3f; sh.rotation = new Vector3(-90f, 0f, 0f);
            Rafaga(ex, 250);
            Estirar(ex, 0.05f, 2.5f);
            Arrastre(ex, 1.2f);
            ColorVida(ex, new[] { blanco, violeta, cian }, new[] { 1f, 1f, 0f });
            ex.Play();
            // esfera de energía que crece y se desvanece
            var es = NuevoSistema("Supernova_Esfera", pos + Vector3.up * 0.5f, materialAditivo, 0.1f, false);
            m = es.main;
            m.startLifetime = 0.7f; m.startSpeed = 0f; m.startSize = 6f; m.startColor = violeta;
            Rafaga(es, 3);
            TamanoVida(es, 0.1f, 1.6f);
            ColorVida(es, new[] { blanco, violeta }, new[] { 1f, 0f });
            es.Play();
            // polvo de estrellas que queda flotando
            var po = NuevoSistema("Supernova_Polvo", pos + Vector3.up * 1f, materialAditivo, 0.2f, false);
            m = po.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(1f, 4f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
            m.startColor = new ParticleSystem.MinMaxGradient(cian, violeta);
            m.gravityModifier = -0.05f;
            sh = po.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 1f;
            Rafaga(po, 120);
            Ruido(po, 0.8f, 0.8f);
            Arrastre(po, 1.5f);
            ColorVida(po, new[] { blanco, cian, violeta }, new[] { 0f, 1f, 1f, 0f });
            po.Play();
            Rayos(pos, 0.3f);
            Destello(pos + Vector3.up * 1.5f, violeta, 18f, 22f, 1.2f);
        }

        IEnumerator Retrasar(float s, System.Action accion)
        {
            yield return new WaitForSeconds(s);
            accion();
        }

        // =================================================================
        //  CAPTURA: la pieza capturada se rompe en fragmentos
        // =================================================================
        public void Fragmentar(PiezaVisual pieza, Vector3 desde, Impulso imp)
        {
            if (pieza == null) return;
            Material mat = pieza.MaterialPrincipal;
            Bounds b = pieza.Limites();
            float e = Mathf.Clamp01(imp.Energia);
            int n = 14 + Mathf.RoundToInt(e * 22f);
            Vector3 empuje = (pieza.transform.position - desde);
            empuje.y = 0f;
            if (empuje.sqrMagnitude < 0.01f) empuje = imp.Direccion;
            empuje = empuje.normalized;
            for (int i = 0; i < n; i++)
            {
                var f = GameObject.CreatePrimitive(i % 3 == 0 ? PrimitiveType.Sphere : PrimitiveType.Cube);
                f.name = "Fragmento";
                f.transform.position = new Vector3(Random.Range(b.min.x, b.max.x), Random.Range(b.min.y, b.max.y), Random.Range(b.min.z, b.max.z));
                f.transform.rotation = Random.rotation;
                f.transform.localScale = Vector3.one * Random.Range(0.06f, 0.16f);
                if (mat != null) f.GetComponent<Renderer>().sharedMaterial = mat;
                var rb = f.AddComponent<Rigidbody>();
                rb.mass = 0.05f;
                Vector3 v = (Random.insideUnitSphere + Vector3.up * 1.2f + empuje * 0.8f).normalized * Random.Range(2f, 5f + e * 7f);
                rb.AddForce(v, ForceMode.VelocityChange);
                rb.AddTorque(Random.insideUnitSphere * 20f, ForceMode.VelocityChange);
                f.AddComponent<FragmentoTemporal>().vida = Random.Range(1.6f, 2.6f);
            }
            // brillo del color del tipo de golpe
            var ch = NuevoSistema("Captura_Brillo", b.center, materialAditivo, 0.1f, false);
            var m = ch.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.3f);
            m.startColor = ColorDe(imp.Tipo);
            Rafaga(ch, 40);
            Arrastre(ch, 2f);
            ColorVida(ch, new[] { Color.white, ColorDe(imp.Tipo) }, new[] { 1f, 0f });
            ch.Play();
        }

        // =================================================================
        //  EFECTOS QUE ACOMPAÑAN A LA PIEZA MIENTRAS SE MUEVE
        // =================================================================
        public EfectosEnMovimiento AdjuntarEfectosMovimiento(PiezaVisual pieza)
        {
            var em = pieza.gameObject.GetComponent<EfectosEnMovimiento>();
            if (em == null) em = pieza.gameObject.AddComponent<EfectosEnMovimiento>();
            em.Iniciar(this, pieza);
            return em;
        }

        // =================================================================
        //  AMBIENTES DE LOS ESCENARIOS
        // =================================================================
        public GameObject CrearAmbiente(TipoAmbiente tipo, Transform padre)
        {
            var raiz = new GameObject("Ambiente_" + tipo);
            raiz.transform.SetParent(padre, false);
            switch (tipo)
            {
                case TipoAmbiente.Neblina:
                    {
                        var ps = Ambiente(raiz.transform, "Neblina", materialAlfa, new Vector3(0, 3, 6), new Vector3(60, 5, 60), 6f);
                        var m = ps.main;
                        m.startLifetime = new ParticleSystem.MinMaxCurve(10f, 16f);
                        m.startSpeed = new ParticleSystem.MinMaxCurve(0.1f, 0.4f);
                        m.startSize = new ParticleSystem.MinMaxCurve(6f, 14f);
                        m.startColor = new Color(1f, 1f, 1f, 0.16f);
                        m.maxParticles = 120;
                        ColorVida(ps, new[] { Color.white, Color.white }, new[] { 0f, 1f, 0f });
                        Ruido(ps, 0.3f, 0.1f);
                        Precalentar(ps);
                        break;
                    }
                case TipoAmbiente.PetalosSakura:
                    {
                        var ps = Ambiente(raiz.transform, "Petalos", materialAlfa, new Vector3(0, 9, 0), new Vector3(30, 1, 30), 35f);
                        var m = ps.main;
                        m.startLifetime = 12f;
                        m.startSpeed = 0f;
                        m.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.14f);
                        m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.85f), new Color(1f, 0.9f, 0.95f));
                        m.gravityModifier = 0.025f;
                        var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
                        Ruido(ps, 0.7f, 0.4f);
                        ColorVida(ps, new[] { Color.white, Color.white }, new[] { 0f, 1f, 1f, 0f });
                        Precalentar(ps);
                        break;
                    }
                case TipoAmbiente.Arena:
                    {
                        var ps = Ambiente(raiz.transform, "Arena", materialAlfa, new Vector3(-20f, 0.5f, 0f), new Vector3(2, 3, 40), 90f);
                        var m = ps.main;
                        m.startLifetime = 9f;
                        m.startSpeed = 0f;
                        m.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
                        m.startColor = new Color(0.9f, 0.75f, 0.5f, 0.6f);
                        var vel = ps.velocityOverLifetime; vel.enabled = true;
                        vel.x = new ParticleSystem.MinMaxCurve(3f, 6f);
                        vel.y = new ParticleSystem.MinMaxCurve(-0.2f, 0.4f);
                        vel.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
                        Ruido(ps, 0.6f, 0.6f);
                        Precalentar(ps);
                        var nubes = Ambiente(raiz.transform, "ArenaNubes", materialAlfa, new Vector3(-20, 1, 0), new Vector3(2, 2, 40), 3f);
                        m = nubes.main;
                        m.startLifetime = 10f; m.startSpeed = 0f;
                        m.startSize = new ParticleSystem.MinMaxCurve(2f, 4f);
                        m.startColor = new Color(0.85f, 0.7f, 0.45f, 0.12f);
                        vel = nubes.velocityOverLifetime; vel.enabled = true;
                        vel.x = new ParticleSystem.MinMaxCurve(3f, 5f);
                        vel.y = new ParticleSystem.MinMaxCurve(0f, 0.2f);
                        vel.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
                        ColorVida(nubes, new[] { Color.white, Color.white }, new[] { 0f, 1f, 0f });
                        Precalentar(nubes);
                        break;
                    }
                case TipoAmbiente.NieveAurora:
                    {
                        var ps = Ambiente(raiz.transform, "Nieve", materialAditivo, new Vector3(0, 12, 0), new Vector3(40, 1, 40), 160f);
                        var m = ps.main;
                        m.startLifetime = 9f;
                        m.startSpeed = 0f;
                        m.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
                        m.startColor = new Color(0.9f, 0.95f, 1f, 0.8f);
                        m.gravityModifier = 0.08f;
                        Ruido(ps, 0.5f, 0.5f);
                        Precalentar(ps);
                        var aurora = new GameObject("Aurora");
                        aurora.transform.SetParent(raiz.transform, false);
                        aurora.AddComponent<AuroraBoreal>().Construir(materialAditivo);
                        break;
                    }
                case TipoAmbiente.BrillosDorados:
                    {
                        var ps = Ambiente(raiz.transform, "Brillos", materialAditivo, new Vector3(0f, 2.5f, 0f), new Vector3(24, 4, 24), 25f);
                        var m = ps.main;
                        m.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
                        m.startSpeed = 0.1f;
                        m.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
                        m.startColor = new Color(1f, 0.85f, 0.5f, 0.9f);
                        Ruido(ps, 0.3f, 0.3f);
                        ColorVida(ps, new[] { Color.white, Color.white }, new[] { 0f, 1f, 0f });
                        Precalentar(ps);
                        break;
                    }
                case TipoAmbiente.MotasPolvo:
                    {
                        // polvo suspendido que brilla dentro de los haces de luz (museos)
                        var ps = Ambiente(raiz.transform, "Motas", materialAditivo, new Vector3(0f, 3f, 0f), new Vector3(16f, 6f, 16f), 30f);
                        var m = ps.main;
                        m.startLifetime = new ParticleSystem.MinMaxCurve(8f, 14f);
                        m.startSpeed = 0.02f;
                        m.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
                        m.startColor = new Color(1f, 0.92f, 0.75f, 0.5f);
                        Ruido(ps, 0.15f, 0.2f);
                        ColorVida(ps, new[] { Color.white, Color.white }, new[] { 0f, 1f, 0f });
                        Precalentar(ps);
                        break;
                    }
                case TipoAmbiente.Luciernagas:
                    {
                        var ps = Ambiente(raiz.transform, "Luciernagas", materialAditivo, new Vector3(0f, 1.2f, 0f), new Vector3(26, 2, 26), 18f);
                        var m = ps.main;
                        m.startLifetime = new ParticleSystem.MinMaxCurve(4f, 8f);
                        m.startSpeed = 0.2f;
                        m.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.12f);
                        m.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.4f), new Color(0.7f, 1f, 0.4f));
                        Ruido(ps, 0.8f, 0.4f);
                        var sol = ps.sizeOverLifetime; sol.enabled = true;
                        var curva = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.15f, 1f), new Keyframe(0.3f, 0.2f),
                                                       new Keyframe(0.5f, 1f), new Keyframe(0.7f, 0.1f), new Keyframe(0.85f, 1f), new Keyframe(1f, 0f));
                        sol.size = new ParticleSystem.MinMaxCurve(1f, curva);
                        Precalentar(ps);
                        break;
                    }
            }
            return raiz;
        }

        ParticleSystem Ambiente(Transform padre, string nombre, Material mat, Vector3 centro, Vector3 caja, float tasa)
        {
            var ps = NuevoSistema(nombre, Vector3.zero, mat, 10f, true, padre);
            ps.transform.localPosition = centro;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = caja;
            var em = ps.emission;
            em.rateOverTime = tasa;
            return ps;
        }

        static void Precalentar(ParticleSystem ps)
        {
            var m = ps.main;
            m.prewarm = true;
            ps.Play();
        }
    }
}
