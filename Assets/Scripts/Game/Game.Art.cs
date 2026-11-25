using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SpaceDefender
{
    /// Describes a material; the cache turns specs into shared Material instances.
    public class MatSpec
    {
        public Color color = Color.white;
        public Texture2D tex;
        public Vector2 tiling = Vector2.one;
        public Color emission = Color.black;
        public Texture2D emisTex;
        public float smooth = 0.15f, metal;
        public bool transparent;

        public MatSpec Clone() => (MatSpec)MemberwiseClone();
        public bool Emissive => emisTex != null || emission.maxColorComponent > 0.001f;

        public string Key()
        {
            Color32 c = color;
            return $"{c.r},{c.g},{c.b},{c.a}|{(tex ? tex.name + tex.GetInstanceID() : "-")}|{tiling.x:0.##},{tiling.y:0.##}|" +
                   $"{emission.r:0.##},{emission.g:0.##},{emission.b:0.##}|{(emisTex ? emisTex.GetInstanceID().ToString() : "-")}|{smooth:0.##}|{metal:0.##}|{transparent}";
        }

        public static MatSpec Of(Color c, float smooth = 0.15f) => new MatSpec { color = c, smooth = smooth };
        public static MatSpec Tex(Texture2D t, Color tint, float tx = 1, float ty = 1, float smooth = 0.15f) =>
            new MatSpec { tex = t, color = tint, tiling = new Vector2(tx, ty), smooth = smooth };
        public static MatSpec Glow(Color c, float intensity) => new MatSpec { color = c, emission = c * intensity, smooth = 0.5f };
        public static MatSpec Glass(Color c, float alpha) { c.a = alpha; return new MatSpec { color = c, smooth = 0.9f, transparent = true }; }
    }

    public partial class Game
    {
        Material matOpaque, matEmissive, matTransparent, matTransparentEmissive;
        readonly Dictionary<string, Material> matCache = new Dictionary<string, Material>();

        void SetupMaterials()
        {
            matOpaque = Resources.Load<Material>("SD_BaseLit");
            matEmissive = Resources.Load<Material>("SD_BaseLitEmissive") ?? matOpaque;
            matTransparent = Resources.Load<Material>("SD_BaseLitTransparent") ?? matOpaque;
            matTransparentEmissive = Resources.Load<Material>("SD_BaseLitTransparentEmissive") ?? matTransparent;
            if (matOpaque == null)
            {
                var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                matOpaque = probe.GetComponent<Renderer>().sharedMaterial;
                Destroy(probe);
                matEmissive = matTransparent = matTransparentEmissive = matOpaque;
            }
        }

        public Material Mat(Color c) => Mat(MatSpec.Of(c));

        public Material Mat(MatSpec s)
        {
            var key = s.Key();
            if (matCache.TryGetValue(key, out var m) && m != null) return m;
            var baseM = s.transparent ? (s.Emissive ? matTransparentEmissive : matTransparent) : (s.Emissive ? matEmissive : matOpaque);
            m = new Material(baseM);
            m.SetColor("_BaseColor", s.color);
            m.color = s.color;
            m.SetTexture("_BaseMap", s.tex != null ? s.tex : Texture2D.whiteTexture);
            m.SetTextureScale("_BaseMap", s.tiling);
            m.SetFloat("_Smoothness", s.smooth);
            m.SetFloat("_Metallic", s.metal);
            if (s.Emissive)
            {
                m.SetColor("_EmissionColor", s.emisTex != null ? (s.emission.maxColorComponent > 0 ? s.emission : Color.white) : s.emission);
                m.SetTexture("_EmissionMap", s.emisTex != null ? s.emisTex : Texture2D.whiteTexture);
            }
            matCache[key] = m;
            return m;
        }

        /// Fog / x-ray version of a spec.
        MatSpec FogSpec(MatSpec s, FogState fog, bool ghost)
        {
            var r = s.Clone();
            if (fog == FogState.Explored)
            {
                float g = s.color.grayscale;
                var c = Color.Lerp(s.color, new Color(g, g, g), 0.75f) * 0.42f;
                c.a = s.color.a;
                r.color = c;
                r.emission = s.emission * 0.12f;
                if (s.emisTex != null && s.emission.maxColorComponent <= 0) r.emission = Color.white * 0.12f;
            }
            else if (fog == FogState.Unexplored)
            {
                r.color = new Color(0.05f, 0.055f, 0.07f, s.color.a);
                r.emission = Color.black;
                r.emisTex = null;
                r.tex = null;
            }
            if (ghost)
            {
                r.transparent = true;
                var c = r.color; c.a = 0.16f; r.color = c;
                r.emission *= 0.3f;
            }
            return r;
        }

        // ---------- primitives ----------

        /// A primitive part without collider.
        static readonly Dictionary<PrimitiveType, Mesh> primMeshes = new Dictionary<PrimitiveType, Mesh>();

        /// Built-in primitive meshes, fetched once (avoids adding a collider to every part).
        static Mesh PrimMesh(PrimitiveType type)
        {
            if (primMeshes.TryGetValue(type, out var m) && m != null) return m;
            var tmp = GameObject.CreatePrimitive(type);
            m = tmp.GetComponent<MeshFilter>().sharedMesh;
            DestroyImmediate(tmp);
            primMeshes[type] = m;
            return m;
        }

        public GameObject Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, MatSpec spec, Vector3 euler = default, bool shadows = true)
        {
            var g = new GameObject(type.ToString());
            g.AddComponent<MeshFilter>().sharedMesh = PrimMesh(type);
            g.AddComponent<MeshRenderer>();
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = Quaternion.Euler(euler);
            g.transform.localScale = scale;
            var r = g.GetComponent<Renderer>();
            r.sharedMaterial = Mat(spec);
            if (!shadows) r.shadowCastingMode = ShadowCastingMode.Off;
            return g;
        }

        /// A tile part: registered for fog tinting. Optionally keeps a box collider for picking.
        GameObject TPart(Tile t, Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, MatSpec spec, bool collider = false, Vector3 euler = default)
        {
            var g = Part(parent, type, pos, scale, spec, euler);
            if (collider) g.AddComponent<BoxCollider>();
            t.renderers.Add(g.GetComponent<Renderer>());
            t.specs.Add(spec);
            return g;
        }

        // ---------- lighting & post ----------

        void SetupLighting()
        {
            if (!Application.isMobilePlatform && QualitySettings.names.Length > 1) QualitySettings.SetQualityLevel(1, true);

            var sun = sunLight;
            if (sun != null && sun.type == LightType.Directional)
            {
                sun.transform.rotation = Quaternion.Euler(36, -38, 0);
                sun.color = new Color(1f, 0.84f, 0.7f);
                sun.intensity = 1.6f;
                sun.shadows = LightShadows.Soft;
                sun.shadowStrength = 0.85f;
            }
            RenderSettings.ambientMode = AmbientMode.Flat;
            var sh = new SphericalHarmonicsL2();
            sh.AddAmbientLight(new Color(0.48f, 0.52f, 0.64f));
            RenderSettings.ambientProbe = sh;
            RenderSettings.ambientLight = new Color(0.48f, 0.52f, 0.64f);

            if (cam != null)
            {
                cam.backgroundColor = new Color(0.06f, 0.08f, 0.13f);
                var data = cam.GetUniversalAdditionalCameraData();
                if (data != null)
                {
                    data.renderPostProcessing = true;
                    data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
                    data.renderShadows = true;
                }
            }

            var vol = FindAnyObjectByType<Volume>();
            if (vol != null && vol.profile != null)
            {
                var p = vol.profile;
                if (!p.TryGet(out Bloom bloom)) bloom = p.Add<Bloom>(true);
                bloom.active = true;
                bloom.threshold.Override(0.95f);
                bloom.intensity.Override(0.9f);
                bloom.scatter.Override(0.65f);
                if (!p.TryGet(out ColorAdjustments ca)) ca = p.Add<ColorAdjustments>(true);
                ca.active = true;
                ca.postExposure.Override(0.25f);
                ca.contrast.Override(12f);
                ca.saturation.Override(8f);
                if (p.TryGet(out Vignette vig)) { vig.intensity.Override(0.28f); }
                if (p.TryGet(out Tonemapping tm)) { tm.active = true; tm.mode.Override(TonemappingMode.ACES); }
            }
        }
    }

    /// Small idle animation (breathing / pulsing).
    public class Idle : MonoBehaviour
    {
        public float bob = 0.012f, speed = 2.2f, pulse;
        float phase; Vector3 basePos, baseScale;
        void Start() { phase = Random.value * 10f; basePos = transform.localPosition; baseScale = transform.localScale; }
        void Update()
        {
            float s = Mathf.Sin(Time.time * speed + phase);
            transform.localPosition = basePos + Vector3.up * s * bob;
            if (pulse > 0) transform.localScale = baseScale * (1f + s * pulse);
        }
    }

    public class Spin : MonoBehaviour
    {
        public Vector3 speed = new Vector3(0, 90, 0);
        void Update() => transform.Rotate(speed * Time.deltaTime, Space.Self);
    }
}
