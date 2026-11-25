using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceDefender
{
    public partial class Game
    {
        readonly List<GameObject> markers = new List<GameObject>();
        readonly List<GameObject> markerPool = new List<GameObject>();

        static readonly Tex.Style[] DistrictStyle = { Tex.Style.Brick, Tex.Style.Glass, Tex.Style.Industrial, Tex.Style.Brick, Tex.Style.Shed };
        static readonly Color[][] Palettes =
        {
            new[] { Color.white, new Color(1f, 0.88f, 0.8f), new Color(0.86f, 0.86f, 0.92f) },   // brick
            new[] { Color.white, new Color(0.85f, 0.95f, 1f), new Color(1f, 0.95f, 0.86f) },    // glass
            new[] { Color.white, new Color(0.9f, 0.96f, 0.85f), new Color(1f, 0.92f, 0.85f) },  // industrial
            new[] { Color.white, Color.white, Color.white },
            new[] { Color.white, new Color(0.55f, 0.7f, 1f), new Color(0.85f, 0.85f, 0.85f) },  // sheds
        };

        // ---------- generation ----------

        static bool IsRoad(int v) => v % 5 == 2;

        static float H01(int x, int z, int s)
        {
            unchecked
            {
                int h = x * 73856093 ^ z * 19349663 ^ s * 83492791;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xffff) / 65535f;
            }
        }

        void GenerateMap()
        {
            tiles = new Tile[K.W, K.H];
            for (int x = 0; x < K.W; x++)
                for (int z = 0; z < K.H; z++)
                    tiles[x, z] = new Tile { x = x, z = z, type = TileType.Street, fog = FogState.Unexplored };

            for (int bx = 0; bx < 5; bx++)
                for (int bz = 0; bz < 5; bz++)
                {
                    int district = DistrictOf(bx, bz);
                    var xs = BlockRange(bx);
                    var zs = BlockRange(bz);
                    foreach (var t in BlockTiles(xs, zs)) t.district = district;

                    if (district == 3) { MakePark(xs, zs); continue; }

                    for (int lx = xs.x; lx <= xs.y; lx += 2)
                        for (int lz = zs.x; lz <= zs.y; lz += 2)
                        {
                            var lot = new List<Tile>();
                            for (int x = lx; x <= Mathf.Min(lx + 1, xs.y); x++)
                                for (int z = lz; z <= Mathf.Min(lz + 1, zs.y); z++) lot.Add(tiles[x, z]);
                            float chance = district == 1 ? 0.9f : district == 4 ? 0.45f : 0.75f;
                            if (Rf() > chance)
                            {
                                if (Rf() < 0.5f) AddProp(lot[R(lot.Count)], district == 2 || district == 4 ? "Crates" : "Car");
                                continue;
                            }
                            int h = district switch
                            {
                                1 => 2 + R(3),
                                2 => 1 + R(2),
                                4 => 1,
                                _ => 1 + (Rf() < 0.35f ? 1 : 0)
                            };
                            int pal = R(3) * 10 + R(3);
                            foreach (var t in lot)
                            {
                                t.type = TileType.Building;
                                t.hp = t.maxHp = h * 2;
                                t.palette = pal;
                            }
                            lot[R(lot.Count)].ladder = true;
                        }
                }

            for (int x = 0; x < K.W; x++)
                if (!IsRoad(x)) { var t = tiles[x, 0]; t.type = TileType.Water; t.prop = null; t.hp = t.maxHp = 0; t.ladder = false; }

            for (int x = 0; x < K.W; x++)
                for (int z = 1; z < K.H; z++)
                {
                    var t = tiles[x, z];
                    if (t.type == TileType.Street && (IsRoad(x) || IsRoad(z)) && !(IsRoad(x) && IsRoad(z)) && t.prop == null && Rf() < 0.06f && z > 4)
                        AddProp(t, "Car");
                }

            portals.Add(new Vector2Int(2, 19));
            portals.Add(new Vector2Int(17, 19));
            portals.Add(new Vector2Int(19, 12));
            foreach (var p in portals) ClearToStreet(tiles[p.x, p.y], TileType.Portal);

            for (int x = 8; x <= 13; x++) ClearToStreet(tiles[x, 2], TileType.Street);

            // ladders face open ground where possible
            foreach (var t in tiles)
                if (t.ladder)
                {
                    foreach (var d in Dirs)
                    {
                        var n = t.P + d;
                        if (In(n) && T(n).type != TileType.Building) { t.ladderDir = d; break; }
                    }
                }
        }

        void ClearToStreet(Tile t, TileType type)
        {
            t.type = type; t.prop = null; t.hp = t.maxHp = 0; t.ladder = false;
        }

        int DistrictOf(int bx, int bz)
        {
            if (bz == 0) return 4;
            if ((bx == 2 && bz == 2) || (bx == 1 && bz == 2) || (bx == 2 && bz == 3)) return 1;
            if (bx == 1 && bz == 3) return 3;
            if (bx == 3 && (bz == 1 || bz == 2)) return 2;
            return 0;
        }

        static Vector2Int BlockRange(int b)
        {
            switch (b)
            {
                case 0: return new Vector2Int(0, 1);
                case 1: return new Vector2Int(3, 6);
                case 2: return new Vector2Int(8, 11);
                case 3: return new Vector2Int(13, 16);
                default: return new Vector2Int(18, 19);
            }
        }

        IEnumerable<Tile> BlockTiles(Vector2Int xs, Vector2Int zs)
        {
            for (int x = xs.x; x <= xs.y; x++)
                for (int z = zs.x; z <= zs.y; z++) yield return tiles[x, z];
        }

        void MakePark(Vector2Int xs, Vector2Int zs)
        {
            foreach (var t in BlockTiles(xs, zs))
            {
                t.type = TileType.Park;
                if (Rf() < 0.25f) AddProp(t, "Tree");
            }
            var pond = tiles[xs.x + 1, zs.x + 1];
            pond.type = TileType.Water; pond.prop = null;
        }

        void AddProp(Tile t, string kind)
        {
            switch (kind)
            {
                case "Car":
                    t.prop = new Prop { name = "Car", hp = 2, explosive = true, height = 0.5f, color = CarColor() }; break;
                case "Crates":
                    t.prop = new Prop { name = "Crates", hp = 2, explosive = false, height = 0.6f, color = new Color(0.6f, 0.45f, 0.25f) }; break;
                case "Tree":
                    t.prop = new Prop { name = "Tree", hp = 3, explosive = false, height = 0.9f, color = new Color(0.18f, 0.42f, 0.18f) }; break;
            }
        }

        Color CarColor()
        {
            Color[] c =
            {
                new Color(0.65f, 0.08f, 0.08f), new Color(0.85f, 0.85f, 0.88f), new Color(0.08f, 0.2f, 0.45f),
                new Color(0.1f, 0.1f, 0.11f), new Color(0.75f, 0.6f, 0.15f), new Color(0.35f, 0.38f, 0.4f)
            };
            return c[R(c.Length)];
        }

        // ---------- map visuals ----------

        void BuildMapVisuals()
        {
            var ground = Part(world, PrimitiveType.Cube, new Vector3(9.5f, -0.65f, 9.5f), new Vector3(K.W + 14, 1f, K.H + 14),
                MatSpec.Tex(Tex.Asphalt(), new Color(0.45f, 0.45f, 0.5f), K.W + 14, K.H + 14));
            ground.name = "Ground";
            // harbor water beyond the south edge
            Part(world, PrimitiveType.Cube, new Vector3(9.5f, -0.2f, -4.5f), new Vector3(K.W + 14, 0.1f, 8f),
                new MatSpec { tex = Tex.Water(), color = Color.white, tiling = new Vector2(16, 6), smooth = 0.92f });
            foreach (var t in tiles) RebuildTile(t);
        }

        public void RebuildTile(Tile t)
        {
            if (t.root != null) Destroy(t.root);
            t.renderers.Clear(); t.specs.Clear();
            var root = new GameObject($"Tile {t.x},{t.z}");
            root.transform.SetParent(world, false);
            root.transform.localPosition = new Vector3(t.x, 0, t.z);
            root.AddComponent<Pick>().p = t.P;
            t.root = root;
            var tr = root.transform;

            switch (t.type)
            {
                case TileType.Building: BuildBuilding(t, tr); break;
                case TileType.Water:
                    TPart(t, tr, PrimitiveType.Cube, new Vector3(0, -0.2f, 0), new Vector3(1f, 0.1f, 1f),
                        new MatSpec { tex = Tex.Water(), color = Color.white, smooth = 0.92f }, true);
                    break;
                case TileType.Park:
                    TPart(t, tr, PrimitiveType.Cube, new Vector3(0, -0.06f, 0), new Vector3(1f, 0.12f, 1f), MatSpec.Tex(Tex.Grass(), Color.white), true);
                    break;
                case TileType.Rubble:
                    TPart(t, tr, PrimitiveType.Cube, new Vector3(0, -0.05f, 0), new Vector3(1f, 0.1f, 1f), MatSpec.Tex(Tex.Sidewalk(), new Color(0.8f, 0.76f, 0.7f)), true);
                    for (int i = 0; i < 5; i++)
                    {
                        float a = H01(t.x, t.z, 20 + i), b = H01(t.x, t.z, 30 + i), c = H01(t.x, t.z, 40 + i);
                        TPart(t, tr, PrimitiveType.Cube, new Vector3(a * 0.6f - 0.3f, 0.06f + c * 0.08f, b * 0.6f - 0.3f),
                            new Vector3(0.2f + c * 0.25f, 0.1f + a * 0.15f, 0.18f + b * 0.2f), MatSpec.Tex(Tex.Rubble(), Color.white), false,
                            new Vector3(a * 40, b * 180, c * 30));
                    }
                    break;
                case TileType.Portal: BuildPortal(t, tr); break;
                default: BuildStreet(t, tr); break;
            }

            if (t.prop != null) BuildProp(t, tr);
            ApplyTileFog(t);
        }

        void BuildStreet(Tile t, Transform tr)
        {
            bool roadX = IsRoad(t.x), roadZ = IsRoad(t.z);
            bool road = roadX || roadZ;
            if (!road)
            {
                TPart(t, tr, PrimitiveType.Cube, new Vector3(0, -0.05f, 0), new Vector3(1f, 0.1f, 1f), MatSpec.Tex(Tex.Sidewalk(), Color.white), true);
                return;
            }
            TPart(t, tr, PrimitiveType.Cube, new Vector3(0, -0.09f, 0), new Vector3(1f, 0.1f, 1f), MatSpec.Tex(Tex.Asphalt(), Color.white), true);
            var paint = MatSpec.Of(new Color(0.85f, 0.82f, 0.62f), 0.3f);
            var white = MatSpec.Of(new Color(0.88f, 0.88f, 0.86f), 0.3f);
            if (roadX && roadZ) return; // intersection

            // centre dashes along the road
            if (roadX) TPart(t, tr, PrimitiveType.Cube, new Vector3(0, -0.035f, 0), new Vector3(0.05f, 0.01f, 0.4f), paint);
            else TPart(t, tr, PrimitiveType.Cube, new Vector3(0, -0.035f, 0), new Vector3(0.4f, 0.01f, 0.05f), paint);

            // crosswalk next to intersections
            bool nearX = roadX && (In(t.x, t.z + 1) && IsRoad(t.z + 1) || In(t.x, t.z - 1) && IsRoad(t.z - 1));
            bool nearZ = roadZ && (In(t.x + 1, t.z) && IsRoad(t.x + 1) || In(t.x - 1, t.z) && IsRoad(t.x - 1));
            if (nearX || nearZ)
                for (int i = 0; i < 5; i++)
                {
                    float o = -0.36f + i * 0.18f;
                    if (nearX) TPart(t, tr, PrimitiveType.Cube, new Vector3(o, -0.034f, 0), new Vector3(0.1f, 0.01f, 0.55f), white);
                    else TPart(t, tr, PrimitiveType.Cube, new Vector3(0, -0.034f, o), new Vector3(0.55f, 0.01f, 0.1f), white);
                }

            // street lights on the curb side
            if (H01(t.x, t.z, 9) > 0.72f && t.prop == null)
            {
                Vector2Int side = roadX ? new Vector2Int(1, 0) : new Vector2Int(0, 1);
                if (H01(t.x, t.z, 10) > 0.5f) side = -side;
                var pos = new Vector3(side.x * 0.44f, 0, side.y * 0.44f);
                var pole = MatSpec.Of(new Color(0.18f, 0.19f, 0.2f), 0.4f);
                TPart(t, tr, PrimitiveType.Cylinder, pos + Vector3.up * 0.6f, new Vector3(0.04f, 0.6f, 0.04f), pole);
                var arm = pos + Vector3.up * 1.18f - new Vector3(side.x, 0, side.y) * 0.12f;
                TPart(t, tr, PrimitiveType.Cube, arm, new Vector3(side.x != 0 ? 0.26f : 0.03f, 0.03f, side.y != 0 ? 0.26f : 0.03f), pole);
                TPart(t, tr, PrimitiveType.Cube, arm - new Vector3(side.x, 0, side.y) * 0.12f - Vector3.up * 0.03f, new Vector3(0.1f, 0.04f, 0.1f),
                    MatSpec.Glow(new Color(1f, 0.85f, 0.6f), 3f));
            }
        }

        void BuildBuilding(Tile t, Transform tr)
        {
            int h = t.Height;
            var style = DistrictStyle[Mathf.Clamp(t.district, 0, 4)];
            int pal = t.palette / 10, variant = t.palette % 10;
            var tint = Palettes[Mathf.Clamp(t.district, 0, 4)][pal % 3];
            float H = h * K.Level;

            bool store = (style == Tex.Style.Brick || style == Tex.Style.Glass) && h >= 2;
            float lightBoost = style == Tex.Style.Glass ? 0.75f : 1.5f;
            if (store)
            {
                TPart(t, tr, PrimitiveType.Cube, new Vector3(0, 0.5f, 0), new Vector3(1f, 1f, 1f),
                    new MatSpec { tex = Tex.Facade(Tex.Style.Storefront, (t.x + t.z) % 4), emisTex = Tex.FacadeLights(Tex.Style.Storefront, (t.x + t.z) % 4), emission = Color.white * 1.3f, color = Color.white, smooth = 0.35f }, true);
                TPart(t, tr, PrimitiveType.Cube, new Vector3(0, 1f + (H - 1f) / 2f, 0), new Vector3(1f, H - 1f, 1f),
                    new MatSpec { tex = Tex.Facade(style, variant), emisTex = Tex.FacadeLights(style, variant), emission = Color.white * lightBoost, color = tint, tiling = new Vector2(1, h - 1), smooth = style == Tex.Style.Glass ? 0.8f : 0.2f, metal = style == Tex.Style.Glass ? 0.3f : 0f }, true);
                // awning over the shop on street-facing sides
                foreach (var d in Dirs)
                {
                    var n = t.P + d;
                    if (!In(n) || T(n).type == TileType.Building) continue;
                    var awn = new Color(0.55f + H01(t.x, t.z, 3) * 0.3f, 0.2f + H01(t.x, t.z, 4) * 0.3f, 0.18f);
                    var pos = new Vector3(d.x * 0.56f, 0.82f, d.y * 0.56f);
                    var sc = d.x != 0 ? new Vector3(0.12f, 0.03f, 0.9f) : new Vector3(0.9f, 0.03f, 0.12f);
                    TPart(t, tr, PrimitiveType.Cube, pos, sc, MatSpec.Of(awn, 0.3f), false, new Vector3(d.y * -18f, 0, d.x * 18f));
                }
            }
            else
            {
                TPart(t, tr, PrimitiveType.Cube, new Vector3(0, H / 2f, 0), new Vector3(1f, H, 1f),
                    new MatSpec { tex = Tex.Facade(style, variant), emisTex = Tex.FacadeLights(style, variant), emission = Color.white * lightBoost, color = tint, tiling = new Vector2(1, h), smooth = style == Tex.Style.Glass ? 0.8f : 0.2f, metal = style == Tex.Style.Glass ? 0.3f : 0f }, true);
            }

            // roof + parapets where the neighbour is lower
            TPart(t, tr, PrimitiveType.Cube, new Vector3(0, H + 0.01f, 0), new Vector3(0.995f, 0.03f, 0.995f), MatSpec.Tex(Tex.Roof(), Color.white));
            var parapet = MatSpec.Of(style == Tex.Style.Glass ? new Color(0.55f, 0.58f, 0.62f) : new Color(0.5f, 0.46f, 0.42f) * (style == Tex.Style.Brick ? 1f : 1.1f), 0.2f);
            foreach (var d in Dirs)
            {
                var n = t.P + d;
                bool edge = !In(n) || T(n).type != TileType.Building || T(n).Height != h;
                if (!edge) continue;
                var pos = new Vector3(d.x * 0.475f, H + 0.09f, d.y * 0.475f);
                var sc = d.x != 0 ? new Vector3(0.05f, 0.16f, 1f) : new Vector3(1f, 0.16f, 0.05f);
                TPart(t, tr, PrimitiveType.Cube, pos, sc, parapet);
            }

            // rooftop details (deterministic per tile)
            float r1 = H01(t.x, t.z, 1), r2 = H01(t.x, t.z, 2);
            if (r1 < 0.3f)
            {
                var ac = MatSpec.Of(new Color(0.72f, 0.73f, 0.75f), 0.4f);
                var p = new Vector3(r2 * 0.4f - 0.2f, H + 0.12f, 0.15f);
                TPart(t, tr, PrimitiveType.Cube, p, new Vector3(0.28f, 0.18f, 0.22f), ac);
                TPart(t, tr, PrimitiveType.Cylinder, p + Vector3.up * 0.1f, new Vector3(0.16f, 0.01f, 0.16f), MatSpec.Of(new Color(0.15f, 0.15f, 0.16f)));
            }
            else if (r1 < 0.4f && style == Tex.Style.Brick && h >= 2)
            {
                var wood = MatSpec.Tex(Tex.Wood(), Color.white);
                for (int i = 0; i < 4; i++)
                    TPart(t, tr, PrimitiveType.Cylinder, new Vector3((i % 2) * 0.2f - 0.1f, H + 0.15f, (i / 2) * 0.2f - 0.1f), new Vector3(0.025f, 0.15f, 0.025f), MatSpec.Of(new Color(0.2f, 0.2f, 0.2f)));
                TPart(t, tr, PrimitiveType.Cylinder, new Vector3(0, H + 0.45f, 0), new Vector3(0.32f, 0.16f, 0.32f), wood);
                TPart(t, tr, PrimitiveType.Sphere, new Vector3(0, H + 0.62f, 0), new Vector3(0.34f, 0.1f, 0.34f), MatSpec.Of(new Color(0.3f, 0.3f, 0.32f)));
            }
            else if (r1 < 0.5f && style == Tex.Style.Glass)
            {
                TPart(t, tr, PrimitiveType.Cylinder, new Vector3(0.2f, H + 0.45f, -0.2f), new Vector3(0.025f, 0.45f, 0.025f), MatSpec.Of(new Color(0.6f, 0.6f, 0.62f), 0.5f));
                TPart(t, tr, PrimitiveType.Sphere, new Vector3(0.2f, H + 0.92f, -0.2f), Vector3.one * 0.06f, MatSpec.Glow(new Color(1f, 0.15f, 0.1f), 4f));
            }
            else if (r1 < 0.62f)
            {
                TPart(t, tr, PrimitiveType.Cube, new Vector3(-0.15f, H + 0.06f, -0.1f), new Vector3(0.3f, 0.08f, 0.3f), MatSpec.Glass(new Color(0.4f, 0.55f, 0.7f), 0.85f));
            }

            // ladder / fire escape
            if (t.ladder)
            {
                var d = t.ladderDir;
                var yellow = new MatSpec { color = new Color(0.95f, 0.72f, 0.1f), emission = new Color(0.95f, 0.72f, 0.1f) * 0.35f, smooth = 0.4f };
                float off = 0.53f;
                Vector3 side = d.x != 0 ? new Vector3(0, 0, 1) : new Vector3(1, 0, 0);
                Vector3 outward = new Vector3(d.x, 0, d.y) * off;
                for (int s = -1; s <= 1; s += 2)
                    TPart(t, tr, PrimitiveType.Cube, outward + side * (0.13f * s) + Vector3.up * (H / 2f + 0.1f), new Vector3(0.035f, H + 0.25f, 0.035f), yellow);
                int rungs = Mathf.Max(3, Mathf.RoundToInt(H * 3));
                for (int i = 0; i < rungs; i++)
                {
                    float y = 0.18f + i * (H / rungs);
                    var sc = d.x != 0 ? new Vector3(0.03f, 0.025f, 0.26f) : new Vector3(0.26f, 0.025f, 0.03f);
                    TPart(t, tr, PrimitiveType.Cube, outward + Vector3.up * y, sc, yellow);
                }
            }

            // damage: scorch + debris when a level is half destroyed
            if (t.hp < t.maxHp && t.hp % 2 == 1)
            {
                TPart(t, tr, PrimitiveType.Cube, new Vector3(0.15f, H + 0.08f, -0.15f), new Vector3(0.35f, 0.14f, 0.3f), MatSpec.Tex(Tex.Rubble(), Color.white), false, new Vector3(8, 25, 5));
                TPart(t, tr, PrimitiveType.Cube, new Vector3(-0.2f, H + 0.05f, 0.2f), new Vector3(0.2f, 0.1f, 0.25f), MatSpec.Tex(Tex.Rubble(), new Color(0.6f, 0.6f, 0.6f)), false, new Vector3(-6, 70, 3));
            }
        }

        void BuildPortal(Tile t, Transform tr)
        {
            TPart(t, tr, PrimitiveType.Cube, new Vector3(0, -0.09f, 0), new Vector3(1f, 0.1f, 1f), MatSpec.Tex(Tex.Asphalt(), new Color(0.7f, 0.6f, 0.8f)), true);
            var purple = new Color(0.7f, 0.2f, 1f);
            TPart(t, tr, PrimitiveType.Cylinder, new Vector3(0, -0.03f, 0), new Vector3(0.95f, 0.005f, 0.95f), MatSpec.Glow(purple * 0.6f, 1.5f));
            var ring = new GameObject("Ring").transform;
            ring.SetParent(tr, false);
            ring.localPosition = new Vector3(0, 0.75f, 0);
            ring.gameObject.AddComponent<Spin>().speed = new Vector3(0, 40, 0);
            var seg = MatSpec.Glow(purple, 3.5f);
            for (int i = 0; i < 14; i++)
            {
                float a = i / 14f * Mathf.PI * 2;
                TPart(t, ring, PrimitiveType.Cube, new Vector3(Mathf.Cos(a) * 0.42f, Mathf.Sin(a) * 0.62f, 0), new Vector3(0.1f, 0.14f, 0.08f), seg, false, new Vector3(0, 0, a * Mathf.Rad2Deg));
            }
            var core = TPart(t, ring, PrimitiveType.Sphere, Vector3.zero, new Vector3(0.75f, 1.15f, 0.06f), new MatSpec { color = new Color(0.5f, 0.1f, 0.8f, 0.55f), emission = purple * 2f, transparent = true });
            core.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var lg = new GameObject("PortalLight").AddComponent<Light>();
            lg.transform.SetParent(tr, false);
            lg.transform.localPosition = new Vector3(0, 1f, 0);
            lg.type = LightType.Point; lg.color = purple; lg.range = 4f; lg.intensity = 2.5f; lg.shadows = LightShadows.None;
        }

        void BuildProp(Tile t, Transform tr)
        {
            var p = t.prop;
            var holder = new GameObject(p.name).transform;
            holder.SetParent(tr, false);
            holder.localPosition = new Vector3(0, t.TopY, 0);
            p.go = holder.gameObject;
            if (p.name == "Car")
            {
                bool alongZ = IsRoad(t.x) || !IsRoad(t.z);
                holder.localRotation = Quaternion.Euler(0, (alongZ ? 0 : 90) + (H01(t.x, t.z, 5) > 0.5f ? 180 : 0), 0);
                var body = new MatSpec { color = p.color, smooth = 0.75f, metal = 0.4f };
                var glass = MatSpec.Of(new Color(0.08f, 0.1f, 0.13f), 0.95f);
                var tire = MatSpec.Of(new Color(0.06f, 0.06f, 0.06f), 0.1f);
                TPart(t, holder, PrimitiveType.Cube, new Vector3(0, 0.17f, 0), new Vector3(0.42f, 0.18f, 0.86f), body, true);
                TPart(t, holder, PrimitiveType.Cube, new Vector3(0, 0.33f, -0.05f), new Vector3(0.38f, 0.16f, 0.46f), body);
                TPart(t, holder, PrimitiveType.Cube, new Vector3(0, 0.33f, -0.05f), new Vector3(0.39f, 0.13f, 0.4f), glass);
                for (int i = 0; i < 4; i++)
                {
                    float x = (i % 2 == 0 ? -1 : 1) * 0.2f, z = (i < 2 ? -1 : 1) * 0.27f;
                    TPart(t, holder, PrimitiveType.Cylinder, new Vector3(x, 0.09f, z), new Vector3(0.17f, 0.03f, 0.17f), tire, false, new Vector3(0, 0, 90));
                }
                for (int s = -1; s <= 1; s += 2)
                {
                    TPart(t, holder, PrimitiveType.Cube, new Vector3(s * 0.14f, 0.19f, 0.43f), new Vector3(0.08f, 0.04f, 0.02f), MatSpec.Glow(new Color(1f, 0.95f, 0.8f), 3f));
                    TPart(t, holder, PrimitiveType.Cube, new Vector3(s * 0.14f, 0.19f, -0.43f), new Vector3(0.08f, 0.04f, 0.02f), MatSpec.Glow(new Color(1f, 0.1f, 0.05f), 2f));
                }
            }
            else if (p.name == "Tree")
            {
                var trunk = MatSpec.Of(new Color(0.3f, 0.21f, 0.13f), 0.1f);
                TPart(t, holder, PrimitiveType.Cylinder, new Vector3(0, 0.3f, 0), new Vector3(0.09f, 0.3f, 0.09f), trunk, true);
                for (int i = 0; i < 4; i++)
                {
                    float a = H01(t.x, t.z, 50 + i), b = H01(t.x, t.z, 60 + i);
                    var leaf = MatSpec.Of(Color.Lerp(new Color(0.12f, 0.3f, 0.1f), new Color(0.25f, 0.45f, 0.15f), a), 0.1f);
                    TPart(t, holder, PrimitiveType.Sphere, new Vector3((a - 0.5f) * 0.3f, 0.75f + b * 0.25f, (b - 0.5f) * 0.3f), Vector3.one * (0.42f + a * 0.2f), leaf);
                }
            }
            else
            {
                var wood = MatSpec.Tex(Tex.Wood(), Color.white);
                TPart(t, holder, PrimitiveType.Cube, new Vector3(-0.12f, 0.17f, 0), new Vector3(0.34f, 0.34f, 0.34f), wood, true, new Vector3(0, 8, 0));
                TPart(t, holder, PrimitiveType.Cube, new Vector3(0.18f, 0.13f, 0.12f), new Vector3(0.26f, 0.26f, 0.26f), wood, false, new Vector3(0, -15, 0));
                TPart(t, holder, PrimitiveType.Cube, new Vector3(-0.1f, 0.45f, 0.02f), new Vector3(0.24f, 0.22f, 0.24f), wood, false, new Vector3(0, 30, 0));
                TPart(t, holder, PrimitiveType.Cylinder, new Vector3(0.2f, 0.17f, -0.22f), new Vector3(0.18f, 0.17f, 0.18f), new MatSpec { color = new Color(0.15f, 0.3f, 0.55f), smooth = 0.5f, metal = 0.3f });
            }
        }

        public void ApplyTileFog(Tile t)
        {
            bool ghost = t.ghost && t.type == TileType.Building;
            for (int i = 0; i < t.renderers.Count; i++)
            {
                var r = t.renderers[i];
                if (r == null) continue;
                r.sharedMaterial = Mat(FogSpec(t.specs[i], t.fog, ghost));
                r.shadowCastingMode = ghost ? ShadowCastingMode.Off : ShadowCastingMode.On;
            }
            foreach (var l in t.root.GetComponentsInChildren<Light>()) l.enabled = t.fog != FogState.Unexplored || t.type == TileType.Portal;
        }

        // ---------- units ----------

        void SpawnSquad()
        {
            var kinds = new[] { UnitKind.Rifleman, UnitKind.Sniper, UnitKind.Heavy, UnitKind.Scout };
            for (int i = 0; i < kinds.Length; i++)
            {
                var u = Unit.Make(kinds[i]);
                u.x = 9 + i; u.z = 2; u.ap = 2;
                AddUnit(u);
            }
            selected = units[0];
        }

        public static Color UnitColor(UnitKind k)
        {
            switch (k)
            {
                case UnitKind.Rifleman: return new Color(0.2f, 0.55f, 0.95f);
                case UnitKind.Sniper: return new Color(0.2f, 0.8f, 0.55f);
                case UnitKind.Heavy: return new Color(0.95f, 0.6f, 0.15f);
                case UnitKind.Scout: return new Color(0.95f, 0.9f, 0.3f);
                case UnitKind.Crawler: return new Color(0.55f, 0.2f, 0.75f);
                case UnitKind.Spitter: return new Color(0.35f, 0.85f, 0.25f);
                default: return new Color(0.75f, 0.25f, 0.45f);
            }
        }

        public void AddUnit(Unit u)
        {
            units.Add(u);
            var root = new GameObject(u.name);
            root.transform.SetParent(world, false);
            root.transform.position = UnitPos(u);
            root.AddComponent<Pick>().p = u.P;
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 0.6f, 0);
            col.size = new Vector3(0.7f, 1.2f, 0.7f);
            var model = new GameObject("Model").transform;
            model.SetParent(root.transform, false);
            if (!u.Human) root.transform.rotation = Quaternion.Euler(0, 180, 0); // aliens face south at spawn

            model.localScale = Vector3.one * (u.Human ? 1.35f : u.kind == UnitKind.Brood ? 1.1f : 1.3f);
            // team ring on the ground (class colour for soldiers, red for aliens)
            var ringCol = u.Human ? UnitColor(u.kind) : new Color(1f, 0.15f, 0.2f);
            var ring = Part(root.transform, PrimitiveType.Cylinder, new Vector3(0, 0.025f, 0), new Vector3(0.78f, 0.006f, 0.78f),
                new MatSpec { color = new Color(ringCol.r, ringCol.g, ringCol.b, 0.5f), emission = ringCol * 1.4f, transparent = true }, default, false);
            ring.name = "Ring";
            Part(root.transform, PrimitiveType.Cylinder, new Vector3(0, 0.03f, 0), new Vector3(0.62f, 0.007f, 0.62f),
                new MatSpec { color = new Color(0.02f, 0.02f, 0.03f, 0.55f), transparent = true }, default, false);

            switch (u.kind)
            {
                case UnitKind.Crawler: BuildCrawler(model); break;
                case UnitKind.Spitter: BuildSpitter(model); break;
                case UnitKind.Brood: BuildBrood(model); break;
                default: BuildSoldier(model, u.kind); break;
            }
            var idle = model.gameObject.AddComponent<Idle>();
            if (u.kind == UnitKind.Brood) { idle.bob = 0; idle.pulse = 0.04f; idle.speed = 2.5f; }
            else if (!u.Human) { idle.bob = 0.02f; idle.speed = 3.2f; }

            u.go = root;
            u.rends = root.GetComponentsInChildren<Renderer>();
            if (!u.Human) SetAlienVisible(u, T(u.P).fog == FogState.Visible);
        }

        void BuildSoldier(Transform m, UnitKind k)
        {
            var accent = UnitColor(k);
            var uniform = MatSpec.Of(Color.Lerp(new Color(0.42f, 0.45f, 0.38f), accent, 0.18f), 0.2f);
            var armor = MatSpec.Of(Color.Lerp(new Color(0.3f, 0.32f, 0.3f), accent, 0.55f), 0.45f);
            var boots = MatSpec.Of(new Color(0.08f, 0.07f, 0.06f), 0.3f);
            var skin = MatSpec.Of(new Color(0.85f, 0.66f, 0.52f), 0.3f);
            var acc = new MatSpec { color = accent, emission = accent * 0.6f, smooth = 0.4f };
            var gun = new MatSpec { color = new Color(0.1f, 0.1f, 0.11f), smooth = 0.5f, metal = 0.6f };
            var visor = MatSpec.Glow(accent, 2.5f);

            // legs + boots
            for (int s = -1; s <= 1; s += 2)
            {
                Part(m, PrimitiveType.Capsule, new Vector3(s * 0.07f, 0.2f, 0), new Vector3(0.11f, 0.17f, 0.12f), uniform);
                Part(m, PrimitiveType.Cube, new Vector3(s * 0.07f, 0.04f, 0.025f), new Vector3(0.1f, 0.08f, 0.16f), boots);
                Part(m, PrimitiveType.Cube, new Vector3(s * 0.075f, 0.24f, 0.05f), new Vector3(0.09f, 0.07f, 0.04f), armor); // knee pads
            }
            // torso, vest, belt, backpack
            Part(m, PrimitiveType.Cube, new Vector3(0, 0.52f, 0), new Vector3(0.28f, 0.3f, 0.17f), uniform);
            Part(m, PrimitiveType.Cube, new Vector3(0, 0.54f, 0.005f), new Vector3(0.31f, 0.24f, 0.21f), armor);
            Part(m, PrimitiveType.Cube, new Vector3(0, 0.39f, 0), new Vector3(0.3f, 0.05f, 0.19f), boots);
            Part(m, PrimitiveType.Cube, new Vector3(0, 0.55f, -0.15f), new Vector3(0.22f, 0.26f, 0.11f), MatSpec.Of(new Color(0.3f, 0.3f, 0.24f), 0.2f));
            Part(m, PrimitiveType.Cube, new Vector3(0, 0.55f, 0.11f), new Vector3(0.14f, 0.05f, 0.01f), acc); // chest stripe
            // shoulder pads in class colour
            for (int s = -1; s <= 1; s += 2)
                Part(m, PrimitiveType.Sphere, new Vector3(s * 0.17f, 0.64f, 0), new Vector3(0.13f, 0.09f, 0.15f), acc);
            // arms forward holding the weapon
            Part(m, PrimitiveType.Capsule, new Vector3(-0.17f, 0.52f, 0.07f), new Vector3(0.075f, 0.14f, 0.075f), uniform, new Vector3(-60, 0, 10));
            Part(m, PrimitiveType.Capsule, new Vector3(0.15f, 0.52f, 0.09f), new Vector3(0.075f, 0.14f, 0.075f), uniform, new Vector3(-70, 0, -15));
            // head + helmet + visor
            Part(m, PrimitiveType.Sphere, new Vector3(0, 0.79f, 0.01f), new Vector3(0.16f, 0.17f, 0.16f), skin);
            Part(m, PrimitiveType.Sphere, new Vector3(0, 0.83f, -0.005f), new Vector3(0.2f, 0.15f, 0.21f), armor);
            Part(m, PrimitiveType.Cube, new Vector3(0, 0.79f, 0.08f), new Vector3(0.13f, 0.035f, 0.02f), visor, default, false);

            // weapons
            switch (k)
            {
                case UnitKind.Rifleman:
                    Part(m, PrimitiveType.Cube, new Vector3(0.05f, 0.5f, 0.24f), new Vector3(0.05f, 0.07f, 0.34f), gun);
                    Part(m, PrimitiveType.Cylinder, new Vector3(0.05f, 0.51f, 0.47f), new Vector3(0.022f, 0.08f, 0.022f), gun, new Vector3(90, 0, 0));
                    Part(m, PrimitiveType.Cube, new Vector3(0.05f, 0.44f, 0.27f), new Vector3(0.035f, 0.09f, 0.05f), gun, new Vector3(15, 0, 0));
                    break;
                case UnitKind.Sniper:
                    Part(m, PrimitiveType.Cube, new Vector3(0.05f, 0.5f, 0.22f), new Vector3(0.045f, 0.06f, 0.4f), gun);
                    Part(m, PrimitiveType.Cylinder, new Vector3(0.05f, 0.5f, 0.55f), new Vector3(0.018f, 0.16f, 0.018f), gun, new Vector3(90, 0, 0));
                    Part(m, PrimitiveType.Cylinder, new Vector3(0.05f, 0.56f, 0.24f), new Vector3(0.035f, 0.08f, 0.035f), gun, new Vector3(90, 0, 0));
                    Part(m, PrimitiveType.Sphere, new Vector3(0.05f, 0.56f, 0.33f), Vector3.one * 0.03f, MatSpec.Glow(new Color(0.3f, 1f, 0.6f), 2f));
                    break;
                case UnitKind.Heavy:
                    Part(m, PrimitiveType.Cylinder, new Vector3(0.17f, 0.72f, 0.05f), new Vector3(0.11f, 0.3f, 0.11f), MatSpec.Of(new Color(0.28f, 0.32f, 0.22f), 0.3f), new Vector3(90, 0, 0));
                    Part(m, PrimitiveType.Cylinder, new Vector3(0.17f, 0.72f, 0.36f), new Vector3(0.13f, 0.02f, 0.13f), gun, new Vector3(90, 0, 0));
                    Part(m, PrimitiveType.Cube, new Vector3(0.05f, 0.48f, 0.2f), new Vector3(0.05f, 0.07f, 0.24f), gun);
                    break;
                default: // scout SMG
                    Part(m, PrimitiveType.Cube, new Vector3(0.05f, 0.5f, 0.2f), new Vector3(0.05f, 0.07f, 0.22f), gun);
                    Part(m, PrimitiveType.Cube, new Vector3(0.05f, 0.43f, 0.2f), new Vector3(0.03f, 0.1f, 0.04f), gun);
                    Part(m, PrimitiveType.Cylinder, new Vector3(0.0f, 0.95f, -0.08f), new Vector3(0.01f, 0.12f, 0.01f), gun); // antenna
                    break;
            }
        }

        void BuildCrawler(Transform m)
        {
            var shell = new MatSpec { color = new Color(0.38f, 0.14f, 0.5f), smooth = 0.85f, metal = 0.2f };
            var flesh = new MatSpec { color = new Color(0.55f, 0.2f, 0.6f), smooth = 0.6f };
            var glow = MatSpec.Glow(new Color(1f, 0.25f, 0.7f), 3f);
            Part(m, PrimitiveType.Sphere, new Vector3(0, 0.3f, -0.18f), new Vector3(0.42f, 0.3f, 0.5f), shell);
            Part(m, PrimitiveType.Sphere, new Vector3(0, 0.32f, 0.08f), new Vector3(0.3f, 0.24f, 0.3f), flesh);
            Part(m, PrimitiveType.Sphere, new Vector3(0, 0.3f, 0.27f), new Vector3(0.2f, 0.17f, 0.22f), shell);
            for (int i = 0; i < 3; i++) Part(m, PrimitiveType.Sphere, new Vector3(0, 0.44f, -0.3f + i * 0.12f), new Vector3(0.06f, 0.03f, 0.06f), glow, default, false);
            for (int s = -1; s <= 1; s += 2)
            {
                Part(m, PrimitiveType.Sphere, new Vector3(s * 0.06f, 0.34f, 0.37f), Vector3.one * 0.05f, MatSpec.Glow(new Color(1f, 0.45f, 0.1f), 4f), default, false);
                Part(m, PrimitiveType.Capsule, new Vector3(s * 0.06f, 0.24f, 0.4f), new Vector3(0.03f, 0.08f, 0.03f), shell, new Vector3(60, s * -20, 0)); // mandible
                for (int j = 0; j < 3; j++)
                {
                    float z = -0.12f + j * 0.16f;
                    Part(m, PrimitiveType.Capsule, new Vector3(s * 0.25f, 0.36f, z), new Vector3(0.035f, 0.13f, 0.035f), shell, new Vector3(0, 0, s * -55));
                    Part(m, PrimitiveType.Capsule, new Vector3(s * 0.4f, 0.16f, z + 0.02f), new Vector3(0.03f, 0.15f, 0.03f), flesh, new Vector3(0, 0, s * 20));
                }
            }
        }

        void BuildSpitter(Transform m)
        {
            var skin = new MatSpec { color = new Color(0.28f, 0.45f, 0.2f), smooth = 0.7f };
            var dark = new MatSpec { color = new Color(0.17f, 0.24f, 0.13f), smooth = 0.5f };
            var acid = new MatSpec { color = new Color(0.4f, 1f, 0.2f, 0.85f), emission = new Color(0.4f, 1f, 0.2f) * 2.2f, transparent = true, smooth = 0.9f };
            for (int s = -1; s <= 1; s += 2)
            {
                Part(m, PrimitiveType.Capsule, new Vector3(s * 0.12f, 0.28f, -0.02f), new Vector3(0.1f, 0.15f, 0.1f), skin, new Vector3(-25, 0, 0));
                Part(m, PrimitiveType.Capsule, new Vector3(s * 0.12f, 0.1f, 0.04f), new Vector3(0.07f, 0.12f, 0.07f), dark, new Vector3(25, 0, 0));
                Part(m, PrimitiveType.Capsule, new Vector3(s * 0.2f, 0.55f, 0.12f), new Vector3(0.05f, 0.14f, 0.05f), skin, new Vector3(-50, 0, s * -15)); // claws
            }
            Part(m, PrimitiveType.Capsule, new Vector3(0, 0.55f, 0), new Vector3(0.32f, 0.26f, 0.3f), skin, new Vector3(35, 0, 0));
            Part(m, PrimitiveType.Sphere, new Vector3(0, 0.7f, -0.17f), new Vector3(0.3f, 0.32f, 0.3f), acid); // acid sac
            Part(m, PrimitiveType.Capsule, new Vector3(0, 0.8f, 0.17f), new Vector3(0.1f, 0.12f, 0.1f), skin, new Vector3(50, 0, 0));
            Part(m, PrimitiveType.Sphere, new Vector3(0, 0.88f, 0.29f), new Vector3(0.17f, 0.15f, 0.26f), dark);
            Part(m, PrimitiveType.Sphere, new Vector3(0, 0.84f, 0.42f), new Vector3(0.08f, 0.06f, 0.05f), MatSpec.Glow(new Color(0.6f, 1f, 0.2f), 4f), default, false);
            for (int s = -1; s <= 1; s += 2)
                Part(m, PrimitiveType.Sphere, new Vector3(s * 0.06f, 0.93f, 0.38f), Vector3.one * 0.035f, MatSpec.Glow(new Color(1f, 0.9f, 0.2f), 4f), default, false);
        }

        void BuildBrood(Transform m)
        {
            var flesh = new MatSpec { color = new Color(0.35f, 0.08f, 0.15f), smooth = 0.85f };
            var vein = MatSpec.Glow(new Color(1f, 0.3f, 0.55f), 2.5f);
            Part(m, PrimitiveType.Sphere, new Vector3(0, 0.42f, 0), new Vector3(0.75f, 0.8f, 0.75f), flesh);
            Part(m, PrimitiveType.Sphere, new Vector3(0, 0.78f, 0), new Vector3(0.28f, 0.2f, 0.28f), MatSpec.Glow(new Color(1f, 0.45f, 0.7f), 2f));
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f;
                var dir = Quaternion.Euler(0, a, 0);
                Part(m, PrimitiveType.Cube, dir * new Vector3(0, 0.45f, 0.36f), new Vector3(0.03f, 0.55f, 0.02f), vein, new Vector3(0, a, 0), false);
                Part(m, PrimitiveType.Capsule, dir * new Vector3(0, 0.06f, 0.48f), new Vector3(0.07f, 0.2f, 0.07f), flesh, new Vector3(80, a, 0));
            }
            var lg = new GameObject("Glow").AddComponent<Light>();
            lg.transform.SetParent(m, false);
            lg.transform.localPosition = new Vector3(0, 1f, 0);
            lg.type = LightType.Point; lg.color = new Color(1f, 0.3f, 0.55f); lg.range = 3f; lg.intensity = 1.8f; lg.shadows = LightShadows.None;
        }

        public void SetAlienVisible(Unit u, bool v)
        {
            if (u.rends != null) foreach (var r in u.rends) if (r != null) r.enabled = v;
            if (u.go != null) foreach (var l in u.go.GetComponentsInChildren<Light>()) l.enabled = v;
        }

        public void PlaceUnitGO(Unit u)
        {
            if (u.go != null)
            {
                u.go.transform.position = UnitPos(u);
                u.go.GetComponent<Pick>().p = u.P;
            }
        }

        // ---------- markers (highlights, intents, paths) ----------

        public GameObject Marker(Vector2Int p, Color c, float size = 0.9f, float lift = 0.03f)
        {
            float y = In(p) ? T(p).TopY : 0f;
            return MarkerAt(new Vector3(p.x, y + lift, p.y), new Vector3(size, 0.02f, size), c);
        }

        public GameObject MarkerAt(Vector3 pos, Vector3 scale, Color c)
        {
            GameObject g;
            if (markerPool.Count > 0) { g = markerPool[markerPool.Count - 1]; markerPool.RemoveAt(markerPool.Count - 1); g.SetActive(true); }
            else
            {
                g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                DestroyImmediate(g.GetComponent<Collider>());
                g.name = "Marker";
                var r0 = g.GetComponent<Renderer>();
                r0.shadowCastingMode = ShadowCastingMode.Off;
                r0.receiveShadows = false;
            }
            g.transform.position = pos;
            g.transform.rotation = Quaternion.identity;
            g.transform.localScale = scale;
            var col = c; col.a = 0.42f;
            g.GetComponent<Renderer>().sharedMaterial = Mat(new MatSpec { color = col, emission = c * 0.45f, transparent = true, smooth = 0.3f });
            markers.Add(g);
            return g;
        }

        public void ClearMarkers()
        {
            foreach (var g in markers) { if (g != null) { g.SetActive(false); markerPool.Add(g); } }
            markers.Clear();
        }
    }

    public class Pick : MonoBehaviour { public Vector2Int p; }
}
