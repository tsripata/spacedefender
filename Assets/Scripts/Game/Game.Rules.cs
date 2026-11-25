using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceDefender
{
    public partial class Game
    {
        // ---------- movement ----------

        /// Cost to step from a to b (adjacent), or -1 if impossible.
        public int StepCost(Unit u, Tile a, Tile b)
        {
            if (b.type == TileType.Water) return -1;
            if (b.prop != null) return -1;
            int d = Mathf.Abs(b.Height - a.Height);
            if (d == 0) return 1;
            if (d == 1) return 2;                       // stairs / step
            if (u.climber) return 1 + d;
            if (a.ladder || b.ladder) return 1 + d;     // ladder / fire escape
            return -1;
        }

        public class PathResult
        {
            public Dictionary<Vector2Int, int> cost = new Dictionary<Vector2Int, int>();
            public Dictionary<Vector2Int, Vector2Int> prev = new Dictionary<Vector2Int, Vector2Int>();

            public List<Vector2Int> PathTo(Vector2Int to)
            {
                var path = new List<Vector2Int>();
                if (!cost.ContainsKey(to)) return path;
                var c = to;
                path.Add(c);
                while (prev.TryGetValue(c, out var p)) { c = p; path.Add(c); }
                path.Reverse();
                return path;   // includes the start tile
            }
        }

        /// Dijkstra from the unit. Other units block tiles (except `passThrough`).
        public PathResult Paths(Unit u, int maxCost, Vector2Int? passThrough = null)
        {
            var res = new PathResult();
            var start = u.P;
            res.cost[start] = 0;
            var open = new List<Vector2Int> { start };
            while (open.Count > 0)
            {
                int bi = 0;
                for (int i = 1; i < open.Count; i++) if (res.cost[open[i]] < res.cost[open[bi]]) bi = i;
                var cur = open[bi];
                open.RemoveAt(bi);
                int cc = res.cost[cur];
                foreach (var d in Dirs)
                {
                    var n = cur + d;
                    if (!In(n)) continue;
                    int step = StepCost(u, T(cur), T(n));
                    if (step < 0) continue;
                    var other = UnitAt(n);
                    if (other != null && other != u && (!passThrough.HasValue || passThrough.Value != n)) continue;
                    int nc = cc + step;
                    if (nc > maxCost) continue;
                    if (res.cost.TryGetValue(n, out var old) && old <= nc) continue;
                    res.cost[n] = nc;
                    res.prev[n] = cur;
                    if (!open.Contains(n)) open.Add(n);
                }
            }
            return res;
        }

        public IEnumerator MoveAlong(Unit u, List<Vector2Int> path, float stepTime = 0.12f)
        {
            for (int i = 1; i < path.Count; i++)
            {
                var from = UnitPos(u);
                u.x = path[i].x; u.z = path[i].y;
                var to = UnitPos(u);
                var look = to - from; look.y = 0;
                if (u.go != null && look.sqrMagnitude > 0.01f) u.go.transform.rotation = Quaternion.LookRotation(look);
                float t = 0;
                bool show = u.Human || T(u.P).fog == FogState.Visible;
                if (!u.Human) SetAlienVisible(u, show);
                float dur = show ? stepTime : 0.01f;
                while (t < 1)
                {
                    t += Dt / dur;
                    if (u.go != null)
                    {
                        var p = Vector3.Lerp(from, to, Mathf.Clamp01(t));
                        p.y += Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * (0.15f + Mathf.Abs(to.y - from.y) * 0.3f);
                        u.go.transform.position = p;
                    }
                    yield return null;
                }
                PlaceUnitGO(u);
                if (u.Human) UpdateFog();
            }
        }

        // ---------- line of sight & fog ----------

        public Vector3 Eye(Unit u) => UnitPos(u) + Vector3.up * 0.75f;

        public bool LineClear(Vector3 a, Vector3 b, Vector2Int skipA, Vector2Int skipB)
        {
            float dist = Vector3.Distance(a, b);
            int steps = Mathf.CeilToInt(dist / 0.2f);
            for (int i = 1; i < steps; i++)
            {
                var p = Vector3.Lerp(a, b, i / (float)steps);
                int tx = Mathf.RoundToInt(p.x), tz = Mathf.RoundToInt(p.z);
                if (!In(tx, tz)) continue;
                var q = new Vector2Int(tx, tz);
                if (q == skipA || q == skipB) continue;
                var t = tiles[tx, tz];
                float top = t.TopY + (t.prop != null ? t.prop.height : 0f);
                if (p.y < top) return false;
            }
            return true;
        }

        public bool CanSee(Unit from, Vector2Int target, float targetLift = 0.5f)
        {
            var a = Eye(from);
            var b = TopOf(target) + Vector3.up * targetLift;
            return LineClear(a, b, from.P, target);
        }

        public void UpdateFog()
        {
            if (tiles == null) return;
            var vis = new bool[K.W, K.H];
            var sources = new List<(Vector3 eye, Vector2Int p, int r)>();
            foreach (var u in units) if (u.alive && u.Human)
                    sources.Add((Eye(u), u.P, u.sight + (T(u.P).Height >= 2 ? 2 : 0)));
            foreach (var b in beacons)
                sources.Add((TopOf(b.p) + Vector3.up * 2.5f, b.p, 5));

            foreach (var s in sources)
            {
                int r = s.r;
                for (int x = s.p.x - r; x <= s.p.x + r; x++)
                    for (int z = s.p.y - r; z <= s.p.y + r; z++)
                    {
                        if (!In(x, z) || vis[x, z]) continue;
                        var d = new Vector2(x - s.p.x, z - s.p.y);
                        if (d.magnitude > r + 0.3f) continue;
                        var q = new Vector2Int(x, z);
                        if (q == s.p || LineClear(s.eye, TopOf(q) + Vector3.up * 0.3f, s.p, q)) vis[x, z] = true;
                    }
            }

            foreach (var t in tiles)
            {
                var nf = vis[t.x, t.z] ? FogState.Visible : (t.fog == FogState.Unexplored ? FogState.Unexplored : FogState.Explored);
                if (nf != t.fog) { t.fog = nf; ApplyTileFog(t); }
            }
            foreach (var u in units) if (u.alive && !u.Human) SetAlienVisible(u, T(u.P).fog == FogState.Visible);
        }

        public bool Visible(Vector2Int p) => In(p) && T(p).fog == FogState.Visible;

        // ---------- hit chance ----------

        public struct Shot
        {
            public int chance, crit, damage;
            public bool possible;
            public string why;
            public List<string> lines;
        }

        public int CoverAt(Vector2Int target, int targetH, Vector2Int from, int shooterH, out string desc)
        {
            int dx = System.Math.Sign(from.x - target.x), dz = System.Math.Sign(from.y - target.y);
            int best = 0; string bestDesc = "";
            void Check(Vector2Int n)
            {
                if (!In(n)) return;
                var t = T(n);
                int c = 0; string d = "";
                if (t.Height > targetH) { c = 40; d = "full cover (wall)"; }
                else if (t.prop != null && t.Height == targetH) { c = 20; d = $"half cover ({t.prop.name.ToLower()})"; }
                if (c > best) { best = c; bestDesc = d; }
            }
            if (dx != 0) Check(new Vector2Int(target.x + dx, target.y));
            if (dz != 0) Check(new Vector2Int(target.x, target.y + dz));
            if (best > 0 && shooterH >= targetH + 2) { best /= 2; bestDesc += ", halved by height"; }
            desc = bestDesc;
            return best;
        }

        public Shot ShotInfo(Unit shooter, Vector2Int target)
        {
            var s = new Shot { lines = new List<string>() };
            var tt = T(target);
            float dist = Vector2.Distance(shooter.P, target);
            int maxRange = shooter.optRange * 2 + 2;
            if (dist > maxRange) { s.why = "Out of range"; return s; }
            if (!CanSee(shooter, target)) { s.why = "No line of sight"; return s; }

            int sh = T(shooter.P).Height, th = tt.Height;
            int chance = shooter.aim + shooter.aimBonus;
            s.lines.Add($"Aim {shooter.aim}%");
            if (shooter.aimBonus > 0) s.lines.Add($"Steady aim +{shooter.aimBonus}%");

            int over = Mathf.Max(0, Mathf.CeilToInt(dist - shooter.optRange));
            if (over > 0) { chance -= over * 5; s.lines.Add($"Distance {dist:0.#} tiles  -{over * 5}%"); }
            if (shooter.kind == UnitKind.Sniper && dist < 3) { chance -= 20; s.lines.Add("Too close for a sniper  -20%"); }

            int dh = sh - th;
            if (dh > 0)
            {
                int b = Mathf.Min(20, 10 * dh) + (shooter.kind == UnitKind.Sniper ? 10 : 0);
                chance += b; s.lines.Add($"High ground (+{dh} lvl)  +{b}%");
            }
            else if (dh <= -2) { chance -= 10; s.lines.Add("Shooting uphill  -10%"); }

            var targetUnit = UnitAt(target);
            int cover = 0;
            if (targetUnit != null)
            {
                cover = CoverAt(target, th, shooter.P, sh, out var cd);
                if (cover > 0) { chance -= cover; s.lines.Add($"Target in {cd}  -{cover}%"); }
                else s.lines.Add("Target exposed / flanked");
            }

            s.chance = Mathf.Clamp(chance, 5, 95);
            s.crit = Mathf.Clamp(10 + (cover == 0 ? 15 : 0) + (dh > 0 ? 10 : 0), 0, 60);
            s.damage = shooter.damage;
            s.possible = true;
            return s;
        }

        // ---------- damage ----------

        public void DamageUnit(Unit u, int dmg, string source)
        {
            if (!u.alive) return;
            u.hp -= dmg;
            Float(UnitPos(u) + Vector3.up * 1.3f, $"-{dmg}", u.Human ? new Color(1f, 0.4f, 0.3f) : new Color(1f, 0.9f, 0.3f));
            if (u.hp <= 0)
            {
                u.alive = false;
                u.hp = 0;
                if (u.go != null) Destroy(u.go);
                Log(u.Human ? $"{u.name} is down! ({source})" : $"{u.name} destroyed.");
                if (selected == u) selected = FirstReady();
            }
        }

        public void DamageTile(Tile t, int dmg)
        {
            if (t.type != TileType.Building || dmg <= 0) return;
            int before = t.Height;
            t.hp = Mathf.Max(0, t.hp - dmg);
            if (t.hp == 0) { t.type = TileType.Rubble; t.ladder = false; }
            int after = t.Height;
            RebuildTile(t);
            if (after < before)
            {
                var u = UnitAt(t.P);
                if (u != null)
                {
                    PlaceUnitGO(u);
                    int fall = before - after;
                    Log($"{u.name} falls as the floor collapses!");
                    DamageUnit(u, fall, "fall");
                }
                Float(TopOf(t.P) + Vector3.up, "Collapse!", new Color(1f, 0.6f, 0.2f));
            }
        }

        public void DamageProp(Tile t, int dmg, List<Vector2Int> chain)
        {
            if (t.prop == null) return;
            t.prop.hp -= dmg;
            if (t.prop.hp <= 0)
            {
                bool boom = t.prop.explosive;
                string n = t.prop.name;
                t.prop = null;
                RebuildTile(t);
                if (boom) { chain?.Add(t.P); Log($"A {n.ToLower()} explodes!"); }
            }
        }

        /// Area explosion. Damages units, props (cars chain-explode) and buildings.
        public IEnumerator Explode(Vector2Int c, float radius, int unitDmg, int centerBuildingDmg, Color col)
        {
            var chain = new List<Vector2Int>();
            yield return Flash(TopOf(c) + Vector3.up * 0.4f, radius, col);
            int r = Mathf.CeilToInt(radius);
            for (int x = c.x - r; x <= c.x + r; x++)
                for (int z = c.y - r; z <= c.y + r; z++)
                {
                    var p = new Vector2Int(x, z);
                    if (!In(p) || Vector2.Distance(p, c) > radius + 0.01f) continue;
                    var t = T(p);
                    var u = UnitAt(p);
                    if (u != null) DamageUnit(u, unitDmg, "explosion");
                    DamageProp(t, 2, chain);
                    DamageTile(t, p == c ? centerBuildingDmg : Mathf.Max(1, centerBuildingDmg / 2));
                }
            UpdateFog();
            foreach (var e in chain)
                yield return Explode(e, 1.2f, 2, 1, new Color(1f, 0.55f, 0.1f));
        }

        IEnumerator Flash(Vector3 pos, float radius, Color col)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            DestroyImmediate(g.GetComponent<Collider>());
            g.GetComponent<Renderer>().sharedMaterial = Mat(col);
            g.transform.position = pos;
            float t = 0;
            while (t < 1)
            {
                t += Dt / 0.35f;
                float s = Mathf.Lerp(0.3f, radius * 2.2f, t);
                g.transform.localScale = new Vector3(s, s * 0.6f, s);
                yield return null;
            }
            Destroy(g);
        }

        public IEnumerator Tracer(Vector3 a, Vector3 b, Color c)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            DestroyImmediate(g.GetComponent<Collider>());
            g.GetComponent<Renderer>().sharedMaterial = Mat(c);
            g.transform.localScale = new Vector3(0.08f, 0.08f, 0.35f);
            g.transform.rotation = Quaternion.LookRotation(b - a);
            float t = 0, dur = Mathf.Max(0.12f, Vector3.Distance(a, b) * 0.02f);
            while (t < 1)
            {
                t += Dt / dur;
                g.transform.position = Vector3.Lerp(a, b, t);
                yield return null;
            }
            Destroy(g);
        }

        /// Resolves a shot (either side). Returns via coroutine.
        public IEnumerator FireAt(Unit shooter, Vector2Int target)
        {
            var info = ShotInfo(shooter, target);
            if (!info.possible) yield break;
            var tu = UnitAt(target);
            var tt = T(target);
            if (shooter.go != null)
            {
                var look = TopOf(target) - UnitPos(shooter); look.y = 0;
                if (look.sqrMagnitude > 0.01f) shooter.go.transform.rotation = Quaternion.LookRotation(look);
            }
            bool hit = R(100) < info.chance;
            var a = Eye(shooter);
            var b = TopOf(target) + Vector3.up * 0.5f;
            if (!hit) b += new Vector3(Rf() - 0.5f, Rf() * 0.6f, Rf() - 0.5f) * 1.4f;
            var col = shooter.Human ? new Color(1f, 0.95f, 0.4f) : new Color(0.5f, 1f, 0.3f);
            if (shooter.Human || Visible(shooter.P) || Visible(target)) yield return Tracer(a, b, col);
            shooter.aimBonus = 0;

            if (hit)
            {
                bool crit = R(100) < info.crit;
                int dmg = info.damage + (crit ? Mathf.CeilToInt(info.damage / 2f) : 0);
                if (tu != null)
                {
                    if (crit) Float(TopOf(target) + Vector3.up * 1.7f, "CRIT!", new Color(1f, 0.8f, 0.2f));
                    Log($"{shooter.name} hits {tu.name} for {dmg}{(crit ? " (crit)" : "")}.");
                    DamageUnit(tu, dmg, shooter.name);
                }
                else if (tt.prop != null)
                {
                    var chain = new List<Vector2Int>();
                    Log($"{shooter.name} hits the {tt.prop.name.ToLower()}.");
                    DamageProp(tt, dmg, chain);
                    foreach (var e in chain) yield return Explode(e, 1.2f, 2, 1, new Color(1f, 0.55f, 0.1f));
                }
                else if (tt.type == TileType.Building)
                {
                    DamageTile(tt, 1);
                }
            }
            else
            {
                Float(TopOf(target) + Vector3.up * 1.5f, "Miss", Color.white);
                Log($"{shooter.name} misses{(tu != null ? " " + tu.name : "")} ({info.chance}%).");
                // stray shots can chip nearby cover
                if (tu != null && R(100) < 30)
                {
                    var d = Dirs[R(4)];
                    var n = target + d;
                    if (In(n) && T(n).prop != null)
                    {
                        var chain = new List<Vector2Int>();
                        DamageProp(T(n), 1, chain);
                        foreach (var e in chain) yield return Explode(e, 1.2f, 2, 1, new Color(1f, 0.55f, 0.1f));
                    }
                }
            }
            yield return new WaitForSeconds(0.25f);
        }

        public float ThrowRange(Unit u) => 6 + (T(u.P).Height >= 2 ? 2 : 0);
    }
}
