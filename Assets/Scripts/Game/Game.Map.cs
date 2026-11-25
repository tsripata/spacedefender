using System.Collections.Generic;
using UnityEngine;

namespace SpaceDefender
{
    public partial class Game
    {
        Material baseMat;
        readonly Dictionary<Color32, Material> mats = new Dictionary<Color32, Material>();
        readonly List<GameObject> markers = new List<GameObject>();
        readonly List<GameObject> markerPool = new List<GameObject>();

        static readonly string[] DistrictNames = { "Residential", "Downtown", "Industrial", "Park", "Harbor" };
        static readonly Color[] DistrictColors =
        {
            new Color(0.80f, 0.72f, 0.60f), // residential
            new Color(0.55f, 0.65f, 0.78f), // downtown
            new Color(0.62f, 0.58f, 0.55f), // industrial
            new Color(0.40f, 0.62f, 0.35f), // park
            new Color(0.70f, 0.70f, 0.72f), // harbor
        };

        public static readonly Color ColStreet = new Color(0.28f, 0.29f, 0.32f);
        public static readonly Color ColPark = new Color(0.33f, 0.55f, 0.28f);
        public static readonly Color ColWater = new Color(0.18f, 0.38f, 0.62f);
        public static readonly Color ColRubble = new Color(0.45f, 0.40f, 0.36f);
        public static readonly Color ColPortal = new Color(0.65f, 0.15f, 0.85f);

        // ---------- materials ----------

        void SetupMaterials()
        {
            // URP Lit material asset (created by the editor bridge) so the shader ships with the build.
            baseMat = Resources.Load<Material>("SD_BaseLit");
            if (baseMat == null)
            {
                var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                baseMat = probe.GetComponent<Renderer>().sharedMaterial;
                Destroy(probe);
            }
        }

        public Material Mat(Color c)
        {
            Color32 key = c;
            if (!mats.TryGetValue(key, out var m))
            {
                m = new Material(baseMat);
                m.color = c;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
                mats[key] = m;
            }
            return m;
        }

        GameObject Box(Transform parent, Vector3 pos, Vector3 scale, Color c, bool collider = true,
            PrimitiveType type = PrimitiveType.Cube)
        {
            var g = GameObject.CreatePrimitive(type);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = pos;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = Mat(c);
            if (!collider) DestroyImmediate(g.GetComponent<Collider>());
            return g;
        }

        // ---------- generation ----------

        static bool IsRoad(int v) => v % 5 == 2;

        void GenerateMap()
        {
            tiles = new Tile[K.W, K.H];
            for (int x = 0; x < K.W; x++)
                for (int z = 0; z < K.H; z++)
                    tiles[x, z] = new Tile { x = x, z = z, type = TileType.Street, fog = FogState.Unexplored };

            // Blocks between roads: index = (v+2)/5 -> 0..4
            for (int bx = 0; bx < 5; bx++)
                for (int bz = 0; bz < 5; bz++)
                {
                    int district = DistrictOf(bx, bz);
                    var xs = BlockRange(bx);
                    var zs = BlockRange(bz);
                    foreach (var t in BlockTiles(xs, zs)) t.district = district;

                    if (district == 3) { MakePark(xs, zs); continue; }

                    // 2x2 lots
                    for (int lx = xs.x; lx <= xs.y; lx += 2)
                        for (int lz = zs.x; lz <= zs.y; lz += 2)
                        {
                            var lot = new List<Tile>();
                            for (int x = lx; x <= Mathf.Min(lx + 1, xs.y); x++)
                                for (int z = lz; z <= Mathf.Min(lz + 1, zs.y); z++) lot.Add(tiles[x, z]);
                            float chance = district == 1 ? 0.9f : district == 4 ? 0.45f : 0.75f;
                            if (Rf() > chance)
                            {
                                // open lot / plaza with maybe a crate or car
                                if (Rf() < 0.5f) AddProp(lot[R(lot.Count)], district == 2 ? "Crates" : "Car");
                                continue;
                            }
                            int h = district switch
                            {
                                1 => 2 + R(3),          // downtown 2-4
                                2 => 1 + R(2),          // industrial 1-2
                                4 => 1,                 // harbor sheds
                                _ => 1 + (Rf() < 0.35f ? 1 : 0) // residential 1-2
                            };
                            var col = DistrictColors[district] * (0.88f + Rf() * 0.2f);
                            col.a = 1;
                            foreach (var t in lot)
                            {
                                t.type = TileType.Building;
                                t.hp = t.maxHp = h * 2;
                                t.baseColor = col;
                            }
                            lot[R(lot.Count)].ladder = true;
                        }
                }

            // Harbor water along the south edge (z = 0), piers on road columns.
            for (int x = 0; x < K.W; x++)
                if (!IsRoad(x)) { var t = tiles[x, 0]; t.type = TileType.Water; t.prop = null; t.hp = t.maxHp = 0; t.ladder = false; }

            // Cars on roads
            for (int x = 0; x < K.W; x++)
                for (int z = 1; z < K.H; z++)
                {
                    var t = tiles[x, z];
                    if (t.type == TileType.Street && (IsRoad(x) || IsRoad(z)) && t.prop == null && Rf() < 0.05f && z > 4)
                        AddProp(t, "Car");
                }

            // Portals at the north and east edges
            portals.Add(new Vector2Int(2, 19));
            portals.Add(new Vector2Int(17, 19));
            portals.Add(new Vector2Int(19, 12));
            foreach (var p in portals)
            {
                var t = tiles[p.x, p.y];
                t.type = TileType.Portal; t.prop = null; t.hp = t.maxHp = 0; t.ladder = false;
            }

            // Keep the squad's start area clear
            for (int x = 8; x <= 13; x++) { var t = tiles[x, 2]; ClearToStreet(t); }
        }

        void ClearToStreet(Tile t)
        {
            t.type = TileType.Street; t.prop = null; t.hp = t.maxHp = 0; t.ladder = false;
        }

        int DistrictOf(int bx, int bz)
        {
            if (bz == 0) return 4;                                   // harbor (south)
            if ((bx == 2 && bz == 2) || (bx == 1 && bz == 2) || (bx == 2 && bz == 3)) return 1; // downtown
            if (bx == 1 && bz == 3) return 3;                        // park
            if (bx == 3 && bz == 1) return 2;                        // industrial
            if (bx == 3 && bz == 2) return 2;
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
                if (Rf() < 0.22f) AddProp(t, "Tree");
            }
            // small pond
            tiles[xs.x + 1, zs.x + 1].type = TileType.Water;
            tiles[xs.x + 1, zs.x + 1].prop = null;
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
            Color[] c = { new Color(0.8f, 0.2f, 0.2f), new Color(0.9f, 0.85f, 0.3f), new Color(0.2f, 0.45f, 0.8f), new Color(0.9f, 0.9f, 0.9f) };
            return c[R(c.Length)];
        }

        // ---------- visuals ----------

        void BuildMapVisuals()
        {
            var ground = Box(world, new Vector3(9.5f, -0.6f, 9.5f), new Vector3(K.W + 6, 1f, K.H + 6), new Color(0.12f, 0.13f, 0.16f), false);
            ground.name = "Ground";
            foreach (var t in tiles) RebuildTile(t);
        }

        public void RebuildTile(Tile t)
        {
            if (t.root != null) Destroy(t.root);
            t.renderers.Clear(); t.colors.Clear();
            var root = new GameObject($"Tile {t.x},{t.z}");
            root.transform.SetParent(world, false);
            root.transform.localPosition = new Vector3(t.x, 0, t.z);
            root.AddComponent<Pick>().p = t.P;
            t.root = root;

            switch (t.type)
            {
                case TileType.Building:
                    {
                        float h = t.Height * K.Level;
                        Add(t, Box(root.transform, new Vector3(0, h / 2f, 0), new Vector3(0.96f, h, 0.96f), t.baseColor));
                        // floor lines / windows hint
                        for (int lv = 1; lv < t.Height; lv++)
                            Add(t, Box(root.transform, new Vector3(0, lv * K.Level, 0), new Vector3(0.98f, 0.06f, 0.98f), t.baseColor * 0.7f, false));
                        Add(t, Box(root.transform, new Vector3(0, h + 0.03f, 0), new Vector3(0.9f, 0.06f, 0.9f), t.baseColor * 0.85f, false));
                        if (t.ladder)
                            Add(t, Box(root.transform, new Vector3(0.5f, h / 2f, 0), new Vector3(0.06f, h, 0.3f), new Color(0.95f, 0.75f, 0.1f), false));
                        if (t.hp < t.maxHp && t.hp % 2 == 1)
                            Add(t, Box(root.transform, new Vector3(0.2f, h + 0.1f, -0.2f), new Vector3(0.3f, 0.2f, 0.3f), ColRubble, false));
                        break;
                    }
                case TileType.Water:
                    Add(t, Box(root.transform, new Vector3(0, -0.15f, 0), new Vector3(1f, 0.1f, 1f), ColWater)); break;
                case TileType.Park:
                    Add(t, Box(root.transform, new Vector3(0, -0.05f, 0), new Vector3(0.98f, 0.1f, 0.98f), ColPark)); break;
                case TileType.Rubble:
                    Add(t, Box(root.transform, new Vector3(0, -0.02f, 0), new Vector3(0.98f, 0.12f, 0.98f), ColRubble));
                    Add(t, Box(root.transform, new Vector3(-0.2f, 0.12f, 0.15f), new Vector3(0.35f, 0.18f, 0.3f), ColRubble * 0.8f, false));
                    break;
                case TileType.Portal:
                    Add(t, Box(root.transform, new Vector3(0, -0.05f, 0), new Vector3(0.98f, 0.1f, 0.98f), ColStreet));
                    Add(t, Box(root.transform, new Vector3(0, 0.05f, 0), new Vector3(0.8f, 0.04f, 0.8f), ColPortal, false, PrimitiveType.Cylinder));
                    Add(t, Box(root.transform, new Vector3(0, 0.9f, 0), new Vector3(0.12f, 0.9f, 0.12f), ColPortal * 0.8f, false, PrimitiveType.Cylinder));
                    break;
                default:
                    {
                        bool road = IsRoad(t.x) || IsRoad(t.z);
                        var c = road ? ColStreet : ColStreet * 1.25f;
                        c.a = 1;
                        Add(t, Box(root.transform, new Vector3(0, -0.05f, 0), new Vector3(0.98f, 0.1f, 0.98f), c));
                        if (road && IsRoad(t.x) != IsRoad(t.z))
                        {
                            var dash = IsRoad(t.x) ? new Vector3(0.06f, 0.02f, 0.4f) : new Vector3(0.4f, 0.02f, 0.06f);
                            Add(t, Box(root.transform, new Vector3(0, 0.01f, 0), dash, new Color(0.8f, 0.78f, 0.5f), false));
                        }
                        break;
                    }
            }

            if (t.prop != null)
            {
                var p = t.prop;
                float y = t.TopY;
                if (p.name == "Car")
                {
                    p.go = Box(root.transform, new Vector3(0, y + 0.2f, 0), new Vector3(0.45f, 0.32f, 0.78f), p.color);
                    Add(t, p.go);
                    Add(t, Box(p.go.transform, new Vector3(0, 0.65f, -0.05f), new Vector3(0.85f, 0.7f, 0.55f), new Color(0.15f, 0.2f, 0.25f), false));
                }
                else if (p.name == "Tree")
                {
                    p.go = Box(root.transform, new Vector3(0, y + 0.25f, 0), new Vector3(0.12f, 0.25f, 0.12f), new Color(0.4f, 0.28f, 0.15f), true, PrimitiveType.Cylinder);
                    Add(t, p.go);
                    Add(t, Box(p.go.transform, new Vector3(0, 2.2f, 0), new Vector3(5f, 2.6f, 5f), p.color, false, PrimitiveType.Sphere));
                }
                else
                {
                    p.go = Box(root.transform, new Vector3(0, y + 0.3f, 0), new Vector3(0.6f, 0.6f, 0.6f), p.color);
                    Add(t, p.go);
                }
            }
            ApplyTileFog(t);
        }

        void Add(Tile t, GameObject g)
        {
            var r = g.GetComponent<Renderer>();
            t.renderers.Add(r);
            t.colors.Add(r.sharedMaterial.color);
        }

        public void ApplyTileFog(Tile t)
        {
            for (int i = 0; i < t.renderers.Count; i++)
            {
                var r = t.renderers[i];
                if (r == null) continue;
                var c = t.colors[i];
                Color shown;
                if (t.fog == FogState.Visible) shown = c;
                else if (t.fog == FogState.Explored)
                {
                    float g = c.grayscale;
                    shown = Color.Lerp(c, new Color(g, g, g), 0.7f) * 0.45f;
                }
                else shown = new Color(0.05f, 0.05f, 0.07f);
                shown.a = 1;
                // quantize a little so the material cache stays small
                shown = new Color(Mathf.Round(shown.r * 64) / 64, Mathf.Round(shown.g * 64) / 64, Mathf.Round(shown.b * 64) / 64, 1);
                r.sharedMaterial = Mat(shown);
            }
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
            var col = UnitColor(u.kind);
            if (u.Human)
            {
                Box(root.transform, new Vector3(0, 0.35f, 0), new Vector3(0.38f, 0.35f, 0.38f), col, true, PrimitiveType.Capsule);
                Box(root.transform, new Vector3(0, 0.82f, 0), new Vector3(0.26f, 0.26f, 0.26f), new Color(0.95f, 0.82f, 0.68f), false, PrimitiveType.Sphere);
                Box(root.transform, new Vector3(0, 0.95f, 0), new Vector3(0.3f, 0.08f, 0.3f), col * 0.7f, false, PrimitiveType.Cylinder);
                // weapon
                float len = u.kind == UnitKind.Sniper ? 0.7f : u.kind == UnitKind.Heavy ? 0.5f : 0.4f;
                float thick = u.kind == UnitKind.Heavy ? 0.14f : 0.07f;
                Box(root.transform, new Vector3(0.2f, 0.45f, 0.15f), new Vector3(thick, thick, len), new Color(0.15f, 0.15f, 0.15f), false);
            }
            else if (u.kind == UnitKind.Crawler)
            {
                Box(root.transform, new Vector3(0, 0.25f, 0), new Vector3(0.55f, 0.3f, 0.7f), col, true, PrimitiveType.Sphere);
                for (int i = -1; i <= 1; i += 2)
                    for (int j = -1; j <= 1; j++)
                        Box(root.transform, new Vector3(0.3f * i, 0.12f, 0.2f * j), new Vector3(0.25f, 0.05f, 0.05f), col * 0.6f, false);
                Box(root.transform, new Vector3(0, 0.32f, 0.33f), new Vector3(0.12f, 0.08f, 0.05f), new Color(1f, 0.3f, 0.2f), false, PrimitiveType.Sphere);
            }
            else if (u.kind == UnitKind.Spitter)
            {
                Box(root.transform, new Vector3(0, 0.45f, 0), new Vector3(0.45f, 0.45f, 0.45f), col, true, PrimitiveType.Capsule);
                Box(root.transform, new Vector3(0, 0.85f, 0.15f), new Vector3(0.22f, 0.22f, 0.4f), col * 0.75f, false, PrimitiveType.Sphere);
                Box(root.transform, new Vector3(0, 0.88f, 0.38f), new Vector3(0.1f, 0.1f, 0.1f), new Color(1f, 1f, 0.2f), false, PrimitiveType.Sphere);
            }
            else
            {
                Box(root.transform, new Vector3(0, 0.4f, 0), new Vector3(0.8f, 0.8f, 0.8f), col, true, PrimitiveType.Sphere);
                Box(root.transform, new Vector3(0, 0.7f, 0), new Vector3(0.35f, 0.35f, 0.35f), new Color(1f, 0.5f, 0.7f), false, PrimitiveType.Sphere);
            }
            u.go = root;
            u.rends = root.GetComponentsInChildren<Renderer>();
            if (!u.Human) SetAlienVisible(u, T(u.P).fog == FogState.Visible);
        }

        public void SetAlienVisible(Unit u, bool v)
        {
            if (u.rends == null) return;
            foreach (var r in u.rends) if (r != null) r.enabled = v;
        }

        public void PlaceUnitGO(Unit u)
        {
            if (u.go != null)
            {
                u.go.transform.position = UnitPos(u);
                u.go.GetComponent<Pick>().p = u.P;
            }
        }

        // ---------- markers (highlights, intents) ----------

        public GameObject Marker(Vector2Int p, Color c, float size = 0.9f, float lift = 0.03f)
        {
            GameObject g;
            if (markerPool.Count > 0) { g = markerPool[markerPool.Count - 1]; markerPool.RemoveAt(markerPool.Count - 1); g.SetActive(true); }
            else
            {
                g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                DestroyImmediate(g.GetComponent<Collider>());
                g.name = "Marker";
            }
            float y = In(p) ? T(p).TopY + (T(p).prop != null && T(p).type != TileType.Building ? 0f : 0f) : 0f;
            g.transform.position = new Vector3(p.x, y + lift, p.y);
            g.transform.localScale = new Vector3(size, 0.04f, size);
            g.GetComponent<Renderer>().sharedMaterial = Mat(c);
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
