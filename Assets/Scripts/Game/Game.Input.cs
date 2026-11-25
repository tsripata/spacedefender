using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SpaceDefender
{
    public partial class Game
    {
        Camera cam;
        Vector3 camTarget;
        float yaw = 45, targetYaw = 45, zoom = 20, pitch = 55;
        public Vector2Int? hover;
        PathResult reach;
        readonly List<Rect> uiRects = new List<Rect>();
        readonly List<Rect> uiRectsLast = new List<Rect>();

        void SetupCamera()
        {
            cam = Camera.main;
            if (cam == null)
            {
                var g = new GameObject("Main Camera");
                g.tag = "MainCamera";
                cam = g.AddComponent<Camera>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.04f, 0.05f, 0.09f);
            cam.fieldOfView = 45;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200;
            camTarget = new Vector3(10.5f, 0, 6f);
            RenderSettings.ambientLight = new Color(0.45f, 0.45f, 0.5f);
            var sun = FindAnyObjectByType<Light>();
            if (sun != null && sun.type == LightType.Directional)
            {
                sun.transform.rotation = Quaternion.Euler(50, -30, 0);
                sun.shadows = LightShadows.Soft;
                sun.intensity = 1.2f;
            }
        }

        public void FocusOn(Vector2Int p)
        {
            camTarget = new Vector3(p.x, 0, p.y);
        }

        void LateUpdate()
        {
            if (cam == null) return;
            yaw = Mathf.LerpAngle(yaw, targetYaw, Time.deltaTime * 10f);
            var rot = Quaternion.Euler(pitch, yaw, 0);
            cam.transform.rotation = rot;
            cam.transform.position = camTarget + Vector3.up * 1f - rot * Vector3.forward * zoom;
        }

        bool PointerOverUI()
        {
            var m = MouseGui();
            foreach (var r in uiRectsLast) if (r.Contains(m)) return true;
            return false;
        }

        Vector2 MouseGui()
        {
            if (Mouse.current == null) return new Vector2(-1, -1);
            var mp = Mouse.current.position.ReadValue();
            float s = GuiScale;
            return new Vector2(mp.x / s, (Screen.height - mp.y) / s);
        }

        void Update()
        {
            HandleCamera();
            UpdateHover();
            if (phase == Phase.Player && !busy) HandleKeys();
            for (int i = floats.Count - 1; i >= 0; i--)
            {
                floats[i].t += Time.deltaTime;
                floats[i].pos += Vector3.up * Time.deltaTime * 0.6f;
                if (floats[i].t > 1.6f) floats.RemoveAt(i);
            }
        }

        void HandleCamera()
        {
            var kb = Keyboard.current;
            var ms = Mouse.current;
            var fwd = Quaternion.Euler(0, yaw, 0) * Vector3.forward;
            var right = Quaternion.Euler(0, yaw, 0) * Vector3.right;
            float sp = Time.deltaTime * (8 + zoom * 0.4f);
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) camTarget += fwd * sp;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) camTarget -= fwd * sp;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) camTarget -= right * sp;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) camTarget += right * sp;
                if (kb.qKey.wasPressedThisFrame) targetYaw += 90;
                if (kb.eKey.wasPressedThisFrame) targetYaw -= 90;
            }
            if (ms != null)
            {
                float sc = ms.scroll.ReadValue().y;
                if (Mathf.Abs(sc) > 0.01f && !PointerOverUI()) zoom = Mathf.Clamp(zoom - Mathf.Sign(sc) * 1.5f, 8, 34);
                if (ms.rightButton.isPressed || ms.middleButton.isPressed)
                {
                    var d = ms.delta.ReadValue();
                    camTarget -= (right * d.x + fwd * d.y) * 0.02f * (zoom / 20f);
                }
            }
            camTarget.x = Mathf.Clamp(camTarget.x, -2, K.W + 1);
            camTarget.z = Mathf.Clamp(camTarget.z, -4, K.H + 1);
        }

        void BlastPreview(Vector2Int c)
        {
            for (int x = -2; x <= 2; x++)
                for (int z = -2; z <= 2; z++)
                {
                    var q = c + new Vector2Int(x, z);
                    if (In(q) && Vector2.Distance(q, c) <= 1.51f) Marker(q, new Color(1f, 0.55f, 0.1f), 0.9f, 0.07f);
                }
        }

        Vector2Int? lastHover;

        void UpdateHover()
        {
            UpdateHoverRay();
            // redraw blast previews when the aimed tile changes
            if (hover != lastHover)
            {
                lastHover = hover;
                if (phase == Phase.Player && !busy && (mode == Mode.Grenade || (mode == Mode.Special && selected != null && selected.kind == UnitKind.Heavy)))
                    RefreshHighlights();
            }
        }

        void UpdateHoverRay()
        {
            hover = null;
            if (cam == null || Mouse.current == null || PointerOverUI()) return;
            var ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (Physics.Raycast(ray, out var hit, 300f))
            {
                var pk = hit.collider.GetComponentInParent<Pick>();
                if (pk != null) hover = pk.p;
            }
        }

        void HandleKeys()
        {
            var kb = Keyboard.current;
            var ms = Mouse.current;
            if (kb != null)
            {
                if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame) { EndTurn(); return; }
                if (kb.tabKey.wasPressedThisFrame) NextUnit();
                if (kb.digit1Key.wasPressedThisFrame) SetMode(Mode.Shoot);
                if (kb.digit2Key.wasPressedThisFrame) SetMode(Mode.Grenade);
                if (kb.digit3Key.wasPressedThisFrame) SetMode(Mode.Special);
                if (kb.escapeKey.wasPressedThisFrame) SetMode(Mode.Move);
            }
            if (ms != null && ms.leftButton.wasPressedThisFrame && hover.HasValue && !PointerOverUI())
                OnTileClick(hover.Value);
        }

        public void NextUnit()
        {
            var list = units.FindAll(u => u.alive && u.Human);
            if (list.Count == 0) return;
            int i = selected == null ? -1 : list.IndexOf(selected);
            for (int k = 1; k <= list.Count; k++)
            {
                var c = list[(i + k) % list.Count];
                if (c.ap > 0 || k == list.Count) { Select(c); return; }
            }
        }

        public void Select(Unit u)
        {
            selected = u;
            mode = Mode.Move;
            RefreshHighlights();
        }

        public void SetMode(Mode m)
        {
            if (selected == null || selected.ap <= 0) { mode = Mode.Move; RefreshHighlights(); return; }
            if (m == Mode.Grenade && selected.grenades <= 0) { Log($"{selected.name} has no grenades left."); return; }
            if (m == Mode.Special && (selected.SpecialName == null || selected.special <= 0)) { Log($"{selected.name} has no special ability ready."); return; }
            if (m == Mode.Special && selected.kind == UnitKind.Sniper)
            {
                // Steady Aim: instant, costs 1 AP
                if (selected.aimBonus > 0) { Log("Already steadied."); return; }
                selected.aimBonus = 20; selected.ap -= 1;
                Log("Sniper steadies aim (+20% on next shot).");
                mode = Mode.Move; RefreshHighlights(); return;
            }
            mode = mode == m ? Mode.Move : m;
            RefreshHighlights();
        }

        void OnTileClick(Vector2Int p)
        {
            var u = UnitAt(p);
            if (u != null && u.Human && mode != Mode.Grenade && mode != Mode.Special) { Select(u); return; }
            if (selected == null || !selected.alive || selected.ap <= 0) return;

            switch (mode)
            {
                case Mode.Move:
                    if (reach != null && reach.cost.ContainsKey(p) && p != selected.P && UnitAt(p) == null)
                        Run(DoMove(selected, reach.PathTo(p)));
                    break;
                case Mode.Shoot:
                    {
                        var target = UnitAt(p);
                        bool alien = target != null && !target.Human && Visible(p);
                        bool prop = T(p).prop != null && Visible(p);
                        if (!alien && !prop) { Log("Pick a visible alien (or a car to blow up)."); return; }
                        var info = ShotInfo(selected, p);
                        if (!info.possible) { Log(info.why); return; }
                        Run(DoShoot(selected, p));
                        break;
                    }
                case Mode.Grenade:
                    if (Vector2.Distance(selected.P, p) > ThrowRange(selected)) { Log("Too far to throw."); return; }
                    if (T(p).fog == FogState.Unexplored) { Log("You can't throw into unexplored territory."); return; }
                    Run(DoGrenade(selected, p));
                    break;
                case Mode.Special:
                    if (selected.kind == UnitKind.Heavy)
                    {
                        if (Vector2.Distance(selected.P, p) > 9) { Log("Rocket out of range."); return; }
                        if (!CanSee(selected, p, 0.3f)) { Log("No line of sight for the rocket."); return; }
                        Run(DoRocket(selected, p));
                    }
                    else if (selected.kind == UnitKind.Scout)
                    {
                        if (Vector2.Distance(selected.P, p) > 5) { Log("Beacon range is 5."); return; }
                        if (T(p).type == TileType.Water) return;
                        PlaceBeacon(selected, p);
                    }
                    break;
            }
        }

        IEnumerator DoMove(Unit u, List<Vector2Int> path)
        {
            busy = true; ClearMarkers();
            u.ap -= 1;
            yield return MoveAlong(u, path);
            busy = false;
            AfterAction(u);
        }

        IEnumerator DoShoot(Unit u, Vector2Int p)
        {
            busy = true; ClearMarkers();
            u.ap = 0;
            yield return FireAt(u, p);
            UpdateFog();
            busy = false;
            AfterAction(u);
        }

        IEnumerator DoGrenade(Unit u, Vector2Int p)
        {
            busy = true; ClearMarkers();
            u.ap = 0; u.grenades--;
            Log($"{u.name} throws a frag grenade!");
            yield return Lob(Eye(u), TopOf(p) + Vector3.up * 0.2f, new Color(0.3f, 0.35f, 0.2f));
            yield return Explode(p, 1.5f, 3, 2, new Color(1f, 0.6f, 0.15f));
            busy = false;
            AfterAction(u);
        }

        IEnumerator DoRocket(Unit u, Vector2Int p)
        {
            busy = true; ClearMarkers();
            u.ap = 0; u.special--;
            Log("Heavy fires a rocket!");
            yield return Tracer(Eye(u), TopOf(p) + Vector3.up * 0.4f, new Color(1f, 0.5f, 0.1f));
            yield return Explode(p, 1.5f, 4, 3, new Color(1f, 0.45f, 0.1f));
            busy = false;
            AfterAction(u);
        }

        void PlaceBeacon(Unit u, Vector2Int p)
        {
            u.ap -= 1; u.special--;
            var b = new Beacon { p = p, turnsLeft = 3 };
            var g = Box(world, TopOf(p) + Vector3.up * 0.4f, new Vector3(0.12f, 0.4f, 0.12f), new Color(0.3f, 0.9f, 1f), false, PrimitiveType.Cylinder);
            Box(g.transform, new Vector3(0, 1.1f, 0), new Vector3(2.2f, 0.4f, 2.2f), new Color(0.6f, 1f, 1f), false, PrimitiveType.Sphere);
            b.go = g;
            beacons.Add(b);
            Log("Sensor beacon deployed (3 turns).");
            UpdateFog();
            AfterAction(u);
        }

        IEnumerator Lob(Vector3 a, Vector3 b, Color c)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            DestroyImmediate(g.GetComponent<Collider>());
            g.GetComponent<Renderer>().sharedMaterial = Mat(c);
            g.transform.localScale = Vector3.one * 0.2f;
            float t = 0, h = 1.5f + Vector3.Distance(a, b) * 0.2f;
            while (t < 1)
            {
                t += Dt / 0.5f;
                var p = Vector3.Lerp(a, b, t);
                p.y += Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI) * h;
                g.transform.position = p;
                yield return null;
            }
            Destroy(g);
        }

        void AfterAction(Unit u)
        {
            if (phase != Phase.Player) return;
            if (CheckEnd()) return;
            mode = Mode.Move;
            if (!u.alive || u.ap <= 0)
            {
                var next = FirstReady();
                if (next != null && next.ap > 0) selected = next;
            }
            PlanAlienIntentsKeepTargets();
            RefreshHighlights();
        }

        /// After the player acts, alien intents stay the same (they are telegraphed),
        /// but intents of aliens that died are dropped. Nothing to recompute.
        void PlanAlienIntentsKeepTargets() { }

        // ---------- highlights ----------

        public void RefreshHighlights()
        {
            ClearMarkers();
            reach = null;
            if (phase != Phase.Player) return;

            // telegraphed alien intents (only for aliens you can see)
            foreach (var a in units)
            {
                if (!a.alive || a.Human || !Visible(a.P)) continue;
                var it = a.intent;
                if (it.type == IntentType.Melee || it.type == IntentType.Shoot)
                    Marker(it.target, new Color(0.95f, 0.15f, 0.15f), 0.95f, 0.06f);
                if (it.type == IntentType.Grenade)
                    for (int x = -2; x <= 2; x++)
                        for (int z = -2; z <= 2; z++)
                        {
                            var q = it.target + new Vector2Int(x, z);
                            if (In(q) && Vector2.Distance(q, it.target) <= 1.51f) Marker(q, new Color(0.5f, 0.95f, 0.15f), 0.9f, 0.06f);
                        }
                if ((it.type == IntentType.Move || it.type == IntentType.Melee) && it.moveTo != a.P)
                    Marker(it.moveTo, new Color(0.55f, 0.2f, 0.7f), 0.5f, 0.05f);
            }
            foreach (var s in pendingSpawns)
                if (s.kind != UnitKind.Brood && Visible(s.portal)) Marker(s.portal, new Color(0.9f, 0.2f, 0.9f), 0.95f, 0.08f);

            if (selected == null || !selected.alive) return;
            Marker(selected.P, new Color(1f, 0.9f, 0.2f), 0.95f, 0.02f);
            if (selected.ap <= 0) return;

            switch (mode)
            {
                case Mode.Move:
                    reach = Paths(selected, selected.move);
                    foreach (var kv in reach.cost)
                        if (kv.Key != selected.P && UnitAt(kv.Key) == null)
                            Marker(kv.Key, T(kv.Key).Height > 0 ? new Color(0.3f, 0.75f, 1f) : new Color(0.2f, 0.55f, 0.85f), 0.8f, 0.02f);
                    break;
                case Mode.Shoot:
                    foreach (var a in units)
                        if (a.alive && !a.Human && Visible(a.P) && ShotInfo(selected, a.P).possible)
                            Marker(a.P, new Color(1f, 0.5f, 0.1f), 1f, 0.01f);
                    break;
                case Mode.Grenade:
                    {
                        float r = ThrowRange(selected);
                        foreach (var t in tiles)
                            if (Vector2.Distance(t.P, selected.P) <= r && t.fog != FogState.Unexplored)
                                Marker(t.P, new Color(0.55f, 0.45f, 0.25f), 0.6f, 0.02f);
                        if (hover.HasValue && Vector2.Distance(hover.Value, selected.P) <= r) BlastPreview(hover.Value);
                        break;
                    }
                case Mode.Special:
                    {
                        float r = selected.kind == UnitKind.Heavy ? 9 : 5;
                        foreach (var t in tiles)
                            if (Vector2.Distance(t.P, selected.P) <= r && t.fog != FogState.Unexplored)
                                Marker(t.P, selected.kind == UnitKind.Heavy ? new Color(0.7f, 0.35f, 0.2f) : new Color(0.3f, 0.8f, 0.9f), 0.5f, 0.02f);
                        if (selected.kind == UnitKind.Heavy && hover.HasValue && Vector2.Distance(hover.Value, selected.P) <= r) BlastPreview(hover.Value);
                        break;
                    }
            }
        }
    }
}
