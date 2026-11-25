#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SpaceDefender
{
    /// Editor-only smoke test: plays the whole game automatically and writes a report + screenshots
    /// to <project>/ClaudeBridge/. Triggered when ClaudeBridge/autotest.flag exists on play.
    public partial class Game
    {
        static string BridgeDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ClaudeBridge"));

        // Play mode doesn't tick while the Editor is in the background, so the smoke test drives
        // the game's coroutines itself from EditorApplication.update.
        bool manualDrive;
        readonly List<Stack<IEnumerator>> runners = new List<Stack<IEnumerator>>();

        void TryAutoTest()
        {
            var flag = Path.Combine(BridgeDir, "autotest.flag");
            if (!File.Exists(flag)) return;
            File.Delete(flag);
            manualDrive = true;
            Run(AutoTest());
            UnityEditor.EditorApplication.update += Pump;
        }

        void OnDestroy()
        {
            if (manualDrive) UnityEditor.EditorApplication.update -= Pump;
        }

        void Pump()
        {
            if (this == null) { UnityEditor.EditorApplication.update -= Pump; return; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (runners.Count > 0 && sw.ElapsedMilliseconds < 200)
            {
                for (int i = runners.Count - 1; i >= 0; i--)
                {
                    if (i >= runners.Count) continue;
                    var st = runners[i];
                    if (st.Count == 0) { runners.RemoveAt(i); continue; }
                    var top = st.Peek();
                    bool more;
                    try { more = top.MoveNext(); }
                    catch (System.Exception ex)
                    {
                        TLog("EXCEPTION: " + ex);
                        runners.RemoveAt(i);
                        continue;
                    }
                    if (!more) { st.Pop(); continue; }
                    if (top.Current is IEnumerator sub) st.Push(sub);
                    // WaitForSeconds / null: just continue on the next pump step
                }
            }
        }

        void TLog(string s)
        {
            File.AppendAllText(Path.Combine(BridgeDir, "autotest.log"), $"[{Time.realtimeSinceStartup:0.0}] {s}\n");
        }

        void Snap(string name)
        {
            if (cam == null || !File.Exists(Path.Combine(BridgeDir, "snap.flag"))) return;
            var rt = new RenderTexture(1280, 720, 24);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(BridgeDir, name + ".png"), tex.EncodeToPNG());
            cam.targetTexture = prev;
            RenderTexture.active = null;
            Destroy(rt); Destroy(tex);
        }

        void Heartbeat()
        {
            File.WriteAllText(Path.Combine(BridgeDir, "heartbeat.txt"), $"phase={phase} turn={turn} busy={busy} runners={runners.Count}");
        }

        IEnumerator AutoTest()
        {
            File.WriteAllText(Path.Combine(BridgeDir, "autotest.log"), "");
            TLog("autotest start");
            Heartbeat();
            yield return new WaitForSeconds(0.5f);
            Snap("shot_0_intro");
            BeginPlay();
            yield return null;
            TLog($"tiles ok, units={units.Count}, integrity={Integrity()}");
            Snap("shot_1_start");

            int safety = 0;
            while ((phase == Phase.Player || phase == Phase.Alien) && safety++ < 20)
            {
                while (phase == Phase.Alien || busy) yield return null;
                if (phase != Phase.Player) break;

                foreach (var u in new List<Unit>(units))
                {
                    if (!u.alive || !u.Human) continue;
                    Select(u);
                    // try to shoot first
                    Unit target = null; int best = -1;
                    foreach (var a in units)
                        if (a.alive && !a.Human && Visible(a.P))
                        {
                            var info = ShotInfo(u, a.P);
                            if (info.possible && info.chance > best) { best = info.chance; target = a; }
                        }
                    if (target != null && best >= 30)
                    {
                        TLog($"{u.name} shoots {target.name} ({best}%)");
                        yield return DoShoot(u, target.P);
                        continue;
                    }
                    if (u.grenades > 0)
                    {
                        foreach (var a in units)
                            if (a.alive && !a.Human && Visible(a.P) && Vector2.Distance(a.P, u.P) <= ThrowRange(u))
                            {
                                TLog($"{u.name} throws grenade at {a.name}");
                                yield return DoGrenade(u, a.P);
                                break;
                            }
                        if (u.ap == 0) continue;
                    }
                    // move: towards the north, random reachable tile
                    mode = Mode.Move;
                    var r = Paths(u, u.move);
                    var options = new List<Vector2Int>();
                    foreach (var kv in r.cost) if (kv.Key != u.P && UnitAt(kv.Key) == null) options.Add(kv.Key);
                    if (options.Count > 0)
                    {
                        options.Sort((p, q) => (q.y + T(q).Height * 2).CompareTo(p.y + T(p).Height * 2));
                        var dest = options[R(Mathf.Min(4, options.Count))];
                        yield return DoMove(u, r.PathTo(dest));
                    }
                }
                Heartbeat();
                TLog($"turn {turn} end: humans={units.FindAll(x => x.alive && x.Human).Count} aliens={AliveAliens()} visibleAliens={units.FindAll(x => x.alive && !x.Human && Visible(x.P)).Count} grid={IntegrityPct():P0}");
                if (turn == 1 || turn == 4 || turn == 8) Snap($"shot_turn{turn}");
                EndTurn();
                yield return null;
            }
            TLog($"finished: phase={phase} turn={turn}");
            Snap("shot_end");
            File.WriteAllText(Path.Combine(BridgeDir, "autotest.done"), phase.ToString());
        }
    }
}
#endif
