using System.Collections.Generic;
using UnityEngine;

namespace SpaceDefender
{
    /// Procedurally generated textures (no external assets needed).
    public static class Tex
    {
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        static Texture2D New(int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4
            };
            return t;
        }

        static Texture2D Get(string key, System.Func<Texture2D> make)
        {
            if (!cache.TryGetValue(key, out var t) || t == null) { t = make(); cache[key] = t; }
            return t;
        }

        static float Fbm(float x, float y, float scale, int oct = 3, float off = 0)
        {
            float v = 0, a = 0.5f, f = scale;
            for (int i = 0; i < oct; i++) { v += a * Mathf.PerlinNoise(x * f + off + i * 17.3f, y * f + off + i * 9.1f); a *= 0.5f; f *= 2f; }
            return v / 0.875f;
        }

        static float Hash(int x, int y, int s)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + s * 2147483647;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xffff) / 65535f;
            }
        }

        static Color Grey(float v) => new Color(v, v, v, 1);

        // ---------- ground ----------

        public static Texture2D Asphalt() => Get("asphalt", () =>
        {
            int n = 256; var t = New(n, n); var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float v = 0.17f + Fbm(x, y, 0.05f) * 0.07f;
                    float sp = Hash(x, y, 1);
                    if (sp > 0.93f) v += 0.06f; else if (sp < 0.05f) v -= 0.04f;
                    float crack = Mathf.Abs(Mathf.PerlinNoise(x * 0.02f + 40, y * 0.02f + 7) - 0.5f);
                    if (crack < 0.008f) v -= 0.05f;
                    px[y * n + x] = Grey(v) * new Color(1f, 1f, 1.04f, 1);
                }
            t.SetPixels(px); t.Apply(true); return t;
        });

        public static Texture2D Sidewalk() => Get("sidewalk", () =>
        {
            int n = 128; var t = New(n, n); var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float v = 0.52f + Fbm(x, y, 0.08f) * 0.08f + (Hash(x, y, 2) - 0.5f) * 0.04f;
                    if (x % 32 < 2 || y % 32 < 2) v -= 0.13f;
                    px[y * n + x] = Grey(v) * new Color(1f, 0.98f, 0.95f, 1);
                }
            t.SetPixels(px); t.Apply(true); return t;
        });

        public static Texture2D Grass() => Get("grass", () =>
        {
            int n = 128; var t = New(n, n); var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float f = Fbm(x, y, 0.06f);
                    float b = Hash(x, y, 3);
                    var c = Color.Lerp(new Color(0.20f, 0.36f, 0.14f), new Color(0.36f, 0.52f, 0.22f), f);
                    if (b > 0.85f) c *= 1.18f; else if (b < 0.1f) c *= 0.8f;
                    c.a = 1; px[y * n + x] = c;
                }
            t.SetPixels(px); t.Apply(true); return t;
        });

        public static Texture2D Water() => Get("water", () =>
        {
            int n = 128; var t = New(n, n); var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float r = Mathf.Sin((x + Fbm(x, y, 0.05f) * 40f) * 0.25f) * 0.5f + 0.5f;
                    var c = Color.Lerp(new Color(0.05f, 0.16f, 0.26f), new Color(0.10f, 0.28f, 0.40f), r * 0.6f + Fbm(x, y, 0.1f) * 0.4f);
                    c.a = 1; px[y * n + x] = c;
                }
            t.SetPixels(px); t.Apply(true); return t;
        });

        public static Texture2D Roof() => Get("roof", () =>
        {
            int n = 128; var t = New(n, n); var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float v = 0.30f + Fbm(x, y, 0.1f) * 0.1f + (Hash(x, y, 4) - 0.5f) * 0.12f;
                    px[y * n + x] = Grey(v);
                }
            t.SetPixels(px); t.Apply(true); return t;
        });

        public static Texture2D Rubble() => Get("rubble", () =>
        {
            int n = 128; var t = New(n, n); var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float f = Fbm(x, y, 0.12f, 4);
                    var c = Color.Lerp(new Color(0.25f, 0.22f, 0.2f), new Color(0.55f, 0.5f, 0.45f), f);
                    if (Hash(x / 6, y / 6, 5) > 0.8f) c *= 0.7f;
                    c.a = 1; px[y * n + x] = c;
                }
            t.SetPixels(px); t.Apply(true); return t;
        });

        public static Texture2D Wood() => Get("wood", () =>
        {
            int n = 128; var t = New(n, n); var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int plank = y / 21;
                    float grain = Mathf.Sin(x * 0.15f + Fbm(x, y, 0.08f, 2, plank * 3) * 12f) * 0.5f + 0.5f;
                    var c = Color.Lerp(new Color(0.42f, 0.28f, 0.15f), new Color(0.6f, 0.43f, 0.25f), grain * 0.6f + Hash(plank, 0, 6) * 0.4f);
                    if (y % 21 < 2) c *= 0.55f;
                    c.a = 1; px[y * n + x] = c;
                }
            t.SetPixels(px); t.Apply(true); return t;
        });

        public static Texture2D Metal() => Get("metal", () =>
        {
            int n = 64; var t = New(n, n); var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    px[y * n + x] = Grey(0.85f + Fbm(x, y, 0.2f) * 0.15f);
            t.SetPixels(px); t.Apply(true); return t;
        });

        // ---------- facades ----------
        // One texture = one floor of one tile-wide bay. Returns albedo; emission mask via FacadeLights.

        public enum Style { Brick = 0, Glass = 1, Industrial = 2, Shed = 3, Storefront = 4 }

        public static Texture2D Facade(Style s, int variant) => Get($"facade{s}{variant}", () => BuildFacade(s, variant, false));
        public static Texture2D FacadeLights(Style s, int variant) => Get($"facadeL{s}{variant}", () => BuildFacade(s, variant, true));

        static Texture2D BuildFacade(Style s, int variant, bool lights)
        {
            int n = 128; var t = New(n, n); var px = new Color[n * n];
            var rnd = new System.Random(1000 + (int)s * 31 + variant * 7);
            // per-window lit flags
            var lit = new bool[16];
            for (int i = 0; i < lit.Length; i++) lit[i] = rnd.NextDouble() < (s == Style.Glass ? 0.3 : 0.5);
            var warm = new Color(1f, 0.82f, 0.5f);
            var cool = new Color(0.75f, 0.88f, 1f);

            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    Color c = Color.black; Color e = Color.black;
                    switch (s)
                    {
                        case Style.Brick:
                            {
                                int row = y / 8; int off = (row % 2) * 8;
                                bool mortar = y % 8 == 0 || (x + off) % 16 == 0;
                                float h = Hash((x + off) / 16, row, 7);
                                c = mortar ? new Color(0.62f, 0.58f, 0.52f) : Color.Lerp(new Color(0.48f, 0.22f, 0.16f), new Color(0.64f, 0.34f, 0.24f), h);
                                c *= 0.92f + Fbm(x, y, 0.2f) * 0.12f;
                                // two windows per bay
                                for (int w = 0; w < 2; w++)
                                {
                                    int x0 = 14 + w * 58, x1 = x0 + 42, y0 = 34, y1 = 104;
                                    if (x >= x0 - 3 && x < x1 + 3 && y >= y0 - 3 && y < y1 + 3) c = new Color(0.82f, 0.8f, 0.75f); // frame
                                    if (x >= x0 - 5 && x < x1 + 5 && y >= y0 - 9 && y < y0 - 3) c = new Color(0.7f, 0.68f, 0.64f); // sill
                                    if (x >= x0 && x < x1 && y >= y0 && y < y1)
                                    {
                                        bool mull = x == (x0 + x1) / 2 || y == (y0 + y1) / 2 + 6;
                                        c = mull ? new Color(0.8f, 0.78f, 0.74f) : Color.Lerp(new Color(0.12f, 0.16f, 0.2f), new Color(0.3f, 0.36f, 0.44f), (y - y0) / 70f);
                                        if (!mull && lit[w + variant * 2 % 8]) { e = warm * (0.8f + 0.2f * Fbm(x, y, 0.1f)); c = Color.Lerp(c, warm * 0.6f, 0.5f); }
                                    }
                                }
                                break;
                            }
                        case Style.Glass:
                            {
                                int col = x / 32, rowp = y / 64;
                                bool mullion = x % 32 < 3 || y % 64 < 3;
                                bool spandrel = y % 64 < 14;
                                float grad = (y % 64) / 64f;
                                c = Color.Lerp(new Color(0.22f, 0.32f, 0.44f), new Color(0.5f, 0.62f, 0.75f), grad * 0.7f + Fbm(x, y, 0.03f) * 0.3f);
                                if (spandrel) c = new Color(0.22f, 0.25f, 0.3f);
                                if (mullion) c = new Color(0.65f, 0.68f, 0.72f);
                                if (!mullion && !spandrel && lit[(col + rowp * 4 + variant * 5) % 16]) { e = new Color(0.85f, 0.9f, 1f) * 0.7f; c = Color.Lerp(c, cool * 0.6f, 0.3f); }
                                break;
                            }
                        case Style.Industrial:
                        case Style.Shed:
                            {
                                float rib = Mathf.Abs(Mathf.Sin(x * Mathf.PI / 8f));
                                var baseC = s == Style.Industrial ? new Color(0.5f, 0.55f, 0.6f) : new Color(0.55f, 0.28f, 0.18f);
                                c = baseC * (0.75f + rib * 0.25f) * (0.9f + Fbm(x, y, 0.05f) * 0.15f);
                                if (s == Style.Shed && Fbm(x, y, 0.07f, 3, 50) > 0.62f) c = Color.Lerp(c, new Color(0.45f, 0.25f, 0.1f), 0.6f); // rust
                                // clerestory window strip
                                if (y > 88 && y < 108 && x % 32 > 4)
                                {
                                    c = new Color(0.15f, 0.2f, 0.25f);
                                    if (lit[(x / 32 + variant) % 16]) { e = warm * 0.7f; c = warm * 0.4f; }
                                }
                                if (y < 4) c *= 0.6f;
                                break;
                            }
                        case Style.Storefront:
                            {
                                // sign band, big shop window, kick plate
                                if (y > 100) { c = variant % 2 == 0 ? new Color(0.55f, 0.12f, 0.12f) : new Color(0.12f, 0.3f, 0.45f); if (y > 108 && y < 120 && x > 20 && x < 108) { c = Color.white * 0.9f; e = (variant % 2 == 0 ? new Color(1f, 0.6f, 0.4f) : new Color(0.5f, 0.85f, 1f)) * 1.2f; } }
                                else if (y < 10) c = new Color(0.2f, 0.2f, 0.22f);
                                else if (x < 6 || x > 121 || (x > 60 && x < 68)) c = new Color(0.15f, 0.15f, 0.17f);
                                else
                                {
                                    c = Color.Lerp(new Color(0.25f, 0.22f, 0.18f), new Color(0.6f, 0.5f, 0.35f), y / 100f);
                                    if (lit[variant % 16] || variant == 0) e = warm * 1.1f;
                                    if (y < 40 && Hash(x / 10, y / 10, variant) > 0.6f) c *= 0.6f; // goods silhouettes
                                }
                                break;
                            }
                    }
                    c.a = 1; e.a = 1;
                    px[y * n + x] = lights ? e : c;
                }
            t.SetPixels(px); t.Apply(true); return t;
        }

        // ---------- misc ----------

        public static Texture2D Ladder() => Get("ladder", () =>
        {
            int n = 64; var t = New(n, n); var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    bool rail = x < 8 || x > 55;
                    bool rung = y % 16 < 4;
                    var c = rail || rung ? new Color(0.95f, 0.75f, 0.12f) : new Color(0.1f, 0.1f, 0.1f);
                    px[y * n + x] = c;
                }
            t.SetPixels(px); t.Apply(true); return t;
        });
    }
}
