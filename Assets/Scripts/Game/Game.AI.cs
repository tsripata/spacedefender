using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SpaceDefender
{
    public partial class Game
    {
        readonly List<(Vector2Int portal, UnitKind kind)> pendingSpawns = new List<(Vector2Int, UnitKind)>();

        void SpawnInitialAliens()
        {
            pendingSpawns.Clear();
            SpawnAlien(UnitKind.Crawler, portals[0]);
            SpawnAlien(UnitKind.Crawler, portals[1]);
            SpawnAlien(UnitKind.Spitter, portals[2]);
        }

        Unit SpawnAlien(UnitKind k, Vector2Int near)
        {
            var spot = FreeTileNear(near, 3);
            if (!spot.HasValue) return null;
            var u = Unit.Make(k);
            u.x = spot.Value.x; u.z = spot.Value.y;
            AddUnit(u);
            return u;
        }

        Vector2Int? FreeTileNear(Vector2Int c, int radius)
        {
            for (int r = 0; r <= radius; r++)
                for (int x = c.x - r; x <= c.x + r; x++)
                    for (int z = c.y - r; z <= c.y + r; z++)
                    {
                        var p = new Vector2Int(x, z);
                        if (!In(p) || UnitAt(p) != null) continue;
                        var t = T(p);
                        if (t.Walkable && t.type != TileType.Building) return p;
                    }
            return null;
        }

        int AliveAliens()
        {
            int n = 0;
            foreach (var u in units) if (u.alive && !u.Human) n++;
            return n;
        }

        // ---------- spawning ----------

        void PlanSpawns()
        {
            pendingSpawns.Clear();
            if (turn >= K.MaxTurns) return;
            if (AliveAliens() >= 14) return;
            // A wave arrives every turn; bigger as the night goes on.
            int count = 1 + turn / 4;
            for (int i = 0; i < count; i++)
            {
                var portal = portals[(turn + i) % portals.Count];
                var kind = (turn + i) % 3 == 0 ? UnitKind.Spitter : UnitKind.Crawler;
                pendingSpawns.Add((portal, kind));
            }
            if (turn == 3 || turn == 7)
                pendingSpawns.Add((new Vector2Int(-1, -1), UnitKind.Brood)); // brood lands somewhere in the north
        }

        void DoSpawns()
        {
            foreach (var s in pendingSpawns)
            {
                if (s.kind == UnitKind.Brood)
                {
                    for (int tries = 0; tries < 40; tries++)
                    {
                        var p = new Vector2Int(2 + R(16), 12 + R(7));
                        if (UnitAt(p) == null && T(p).Walkable && T(p).type != TileType.Building && T(p).fog != FogState.Visible)
                        {
                            SpawnAlien(UnitKind.Brood, p);
                            Log("Something landed in the north of the city...");
                            pings.Add(new Ping { p = p, turnsLeft = 2 });
                            break;
                        }
                    }
                    continue;
                }
                var blocker = UnitAt(s.portal);
                if (blocker != null && blocker.Human)
                {
                    Log($"{blocker.name} blocks a portal! (-1 HP)");
                    DamageUnit(blocker, 1, "portal");
                    continue;
                }
                var u = SpawnAlien(s.kind, s.portal);
                if (u != null && Visible(u.P)) Log($"A {u.name} emerges from a portal.");
            }
            pendingSpawns.Clear();
        }

        // ---------- planning (telegraphed intents) ----------

        void PlanAlienIntents()
        {
            foreach (var a in units)
            {
                if (!a.alive || a.Human) continue;
                a.intent = new Intent { type = IntentType.None };
                switch (a.kind)
                {
                    case UnitKind.Crawler: PlanMelee(a); break;
                    case UnitKind.Spitter: PlanSpitter(a); break;
                    case UnitKind.Brood:
                        if (a.hatchTimer <= 1) a.intent.type = IntentType.Hatch;
                        break;
                }
            }
        }

        void PlanMelee(Unit a)
        {
            var all = Paths(a, 999);
            Vector2Int? bestTarget = null; Vector2Int bestStand = default; int bestScore = int.MaxValue;

            void Consider(Vector2Int target, int bias)
            {
                foreach (var d in Dirs)
                {
                    var stand = target + d;
                    if (!all.cost.TryGetValue(stand, out var c)) continue;
                    int score = c + bias;
                    if (score < bestScore) { bestScore = score; bestTarget = target; bestStand = stand; }
                }
            }

            foreach (var h in units) if (h.alive && h.Human) Consider(h.P, 0);
            // buildings near the alien (prefer tall ones: they power more of the grid)
            foreach (var t in tiles)
                if (t.type == TileType.Building && Vector2.Distance(t.P, a.P) <= 7) Consider(t.P, 6 - t.Height);

            if (!bestTarget.HasValue) { a.intent.type = IntentType.None; return; }
            var path = all.PathTo(bestStand);
            // truncate to move points
            var trimmed = new List<Vector2Int> { path[0] };
            for (int i = 1; i < path.Count; i++)
            {
                if (all.cost[path[i]] > a.move) break;
                trimmed.Add(path[i]);
            }
            // don't end on another unit
            while (trimmed.Count > 1 && UnitAt(trimmed[trimmed.Count - 1]) != null) trimmed.RemoveAt(trimmed.Count - 1);
            a.intent.path = trimmed;
            a.intent.moveTo = trimmed[trimmed.Count - 1];
            var end = a.intent.moveTo;
            bool adjacent = Mathf.Abs(end.x - bestTarget.Value.x) + Mathf.Abs(end.y - bestTarget.Value.y) == 1;
            a.intent.type = adjacent ? IntentType.Melee : IntentType.Move;
            a.intent.target = bestTarget.Value;
        }

        void PlanSpitter(Unit a)
        {
            // 1) shoot a visible human
            Unit best = null; int bestChance = -1;
            foreach (var h in units)
            {
                if (!h.alive || !h.Human) continue;
                var s = ShotInfo(a, h.P);
                if (s.possible && Vector2.Distance(a.P, h.P) <= 9 && s.chance > bestChance) { bestChance = s.chance; best = h; }
            }
            // 2) acid grenade if someone is close (even without line of sight)
            if (a.cooldown <= 0)
            {
                foreach (var h in units)
                    if (h.alive && h.Human && Vector2.Distance(a.P, h.P) <= 6 && (best == null || bestChance < 40))
                    {
                        a.intent.type = IntentType.Grenade;
                        a.intent.target = h.P;
                        return;
                    }
            }
            if (best != null) { a.intent.type = IntentType.Shoot; a.intent.target = best.P; return; }

            // 3) advance toward the nearest human
            var all = Paths(a, 999);
            Unit near = null; int nc = int.MaxValue;
            foreach (var h in units)
            {
                if (!h.alive || !h.Human) continue;
                foreach (var d in Dirs)
                    if (all.cost.TryGetValue(h.P + d, out var c) && c < nc) { nc = c; near = h; }
            }
            if (near == null) return;
            Vector2Int stand = near.P; int sc = int.MaxValue;
            foreach (var d in Dirs) if (all.cost.TryGetValue(near.P + d, out var c) && c < sc) { sc = c; stand = near.P + d; }
            var path = all.PathTo(stand);
            var trimmed = new List<Vector2Int> { path[0] };
            for (int i = 1; i < path.Count; i++) { if (all.cost[path[i]] > a.move) break; trimmed.Add(path[i]); }
            while (trimmed.Count > 1 && UnitAt(trimmed[trimmed.Count - 1]) != null) trimmed.RemoveAt(trimmed.Count - 1);
            a.intent.type = IntentType.Move;
            a.intent.path = trimmed;
            a.intent.moveTo = trimmed[trimmed.Count - 1];
            a.intent.target = near.P;
        }

        // ---------- execution ----------

        IEnumerator RunAliens()
        {
            var list = new List<Unit>();
            foreach (var u in units) if (u.alive && !u.Human) list.Add(u);

            foreach (var a in list)
            {
                if (!a.alive) continue;
                var it = a.intent;
                bool seen = Visible(a.P);
                if (a.cooldown > 0) a.cooldown--;

                switch (it.type)
                {
                    case IntentType.Move:
                    case IntentType.Melee:
                        {
                            // re-validate path (things may have moved)
                            var path = new List<Vector2Int> { a.P };
                            for (int i = 1; i < it.path.Count; i++)
                            {
                                var p = it.path[i];
                                if (UnitAt(p) != null) break;
                                path.Add(p);
                            }
                            if (path.Count > 1) yield return MoveAlong(a, path, 0.1f);
                            seen = Visible(a.P);
                            if (it.type == IntentType.Melee)
                            {
                                int man = Mathf.Abs(a.x - it.target.x) + Mathf.Abs(a.z - it.target.y);
                                if (man == 1) yield return Melee(a, it.target, seen);
                            }
                            break;
                        }
                    case IntentType.Shoot:
                        {
                            var victim = UnitAt(it.target);
                            if (victim != null && victim.Human && ShotInfo(a, it.target).possible)
                            {
                                if (!seen) NoisePing(a.P);
                                yield return FireAt(a, it.target);
                            }
                            else
                            {
                                if (seen) Float(UnitPos(a) + Vector3.up * 1.4f, "No target", Color.white);
                                var t = T(it.target);
                                if (t.type == TileType.Building && CanSee(a, it.target)) { DamageTile(t, 1); NoisePing(it.target); }
                            }
                            break;
                        }
                    case IntentType.Grenade:
                        {
                            if (!seen) NoisePing(it.target);
                            else Log("The Spitter hurls an acid grenade!");
                            a.cooldown = 3;
                            yield return Explode(it.target, 1.5f, 2, 2, new Color(0.45f, 1f, 0.2f));
                            break;
                        }
                    case IntentType.Hatch:
                        {
                            var spot = FreeTileNear(a.P, 2);
                            if (spot.HasValue && spot.Value != a.P)
                            {
                                SpawnAlien(UnitKind.Crawler, spot.Value);
                                if (seen) Log("The Brood Pod hatches a Crawler!");
                            }
                            a.hatchTimer = 3;
                            break;
                        }
                    default:
                        if (a.kind == UnitKind.Brood) a.hatchTimer--;
                        break;
                }
                if (CheckEndQuick()) yield break;
                if (seen) yield return new WaitForSeconds(0.2f);
            }
        }

        bool CheckEndQuick()
        {
            foreach (var u in units) if (u.alive && u.Human) return IntegrityPct() < K.LoseIntegrity;
            return true;
        }

        IEnumerator Melee(Unit a, Vector2Int target, bool seen)
        {
            var victim = UnitAt(target);
            var t = T(target);
            if (a.go != null)
            {
                var look = TopOf(target) - UnitPos(a); look.y = 0;
                if (look.sqrMagnitude > 0.01f) a.go.transform.rotation = Quaternion.LookRotation(look);
                // lunge
                var home = a.go.transform.position;
                var mid = Vector3.Lerp(home, TopOf(target), 0.4f);
                float k = 0;
                while (k < 1 && seen) { k += Dt / 0.15f; a.go.transform.position = Vector3.Lerp(home, mid, Mathf.Sin(k * Mathf.PI)); yield return null; }
                a.go.transform.position = home;
            }
            if (!Visible(target) && !seen) NoisePing(target);

            if (victim != null && victim.Human)
            {
                if (R(100) < a.aim)
                {
                    Log($"{a.name} slashes {victim.name}!");
                    DamageUnit(victim, a.damage, a.name);
                }
                else
                {
                    Float(TopOf(target) + Vector3.up * 1.5f, "Dodged", Color.white);
                    Log($"{victim.name} dodges the {a.name}.");
                }
            }
            else if (t.type == TileType.Building)
            {
                if (seen || Visible(target)) Log($"{a.name} tears into a building.");
                DamageTile(t, 1);
            }
            else if (seen) Float(TopOf(target) + Vector3.up, "Whiff", Color.white);
        }

        void NoisePing(Vector2Int p)
        {
            if (Visible(p)) return;
            pings.Add(new Ping { p = p, turnsLeft = 2 });
            Log($"Noise heard near ({p.x},{p.y})...");
        }
    }
}
