using System.Text;
using UnityEngine;

namespace SpaceDefender
{
    public partial class Game
    {
        float GuiScale => Mathf.Max(0.6f, Screen.height / 720f);
        GUIStyle box, label, small, title, button, center, worldLabel;
        Texture2D panelTex, barBg, barFg, barRed, barEmpty;
        bool showHelp;

        static Texture2D SolidTex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        void InitStyles()
        {
            if (box != null) return;
            panelTex = SolidTex(new Color(0.05f, 0.07f, 0.12f, 0.86f));
            barBg = SolidTex(new Color(0.2f, 0.2f, 0.25f, 1));
            barFg = SolidTex(new Color(0.3f, 0.85f, 0.45f, 1));
            barRed = SolidTex(new Color(0.9f, 0.3f, 0.25f, 1));
            barEmpty = SolidTex(new Color(0.12f, 0.12f, 0.15f, 1));
            box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(10, 10, 8, 8) };
            box.normal.background = panelTex;
            label = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true, richText = true };
            label.normal.textColor = new Color(0.9f, 0.92f, 0.95f);
            small = new GUIStyle(label) { fontSize = 12 };
            title = new GUIStyle(label) { fontSize = 22, fontStyle = FontStyle.Bold };
            center = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
            button = new GUIStyle(GUI.skin.button) { fontSize = 14, richText = true };
            worldLabel = new GUIStyle(label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
        }

        Rect Panel(Rect r)
        {
            uiRects.Add(r);
            GUI.Box(r, GUIContent.none, box);
            return r;
        }

        void OnGUI()
        {
            InitStyles();
            float s = GuiScale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            float W = Screen.width / s, H = Screen.height / s;
            if (Event.current.type == EventType.Layout) { uiRectsLast.Clear(); uiRectsLast.AddRange(uiRects); uiRects.Clear(); }

            DrawWorldLabels(s);

            if (phase == Phase.Intro) { DrawIntro(W, H); return; }

            // ---- top bar ----
            var top = Panel(new Rect(10, 10, 360, 86));
            GUI.Label(new Rect(top.x + 10, top.y + 6, 340, 24),
                $"<b>SPACE DEFENDER</b>   Turn <b>{Mathf.Min(turn, K.MaxTurns)}/{K.MaxTurns}</b>   {(phase == Phase.Alien ? "<color=#ff7070>Aliens moving...</color>" : phase == Phase.Player ? "<color=#8fd>Your move</color>" : "")}", label);
            float pct = IntegrityPct();
            GUI.Label(new Rect(top.x + 10, top.y + 32, 140, 20), "City power grid", small);
            GUI.DrawTexture(new Rect(top.x + 120, top.y + 36, 220, 12), barBg);
            GUI.DrawTexture(new Rect(top.x + 120, top.y + 36, 220 * Mathf.Clamp01(pct), 12), pct < 0.65f ? barRed : barFg);
            GUI.DrawTexture(new Rect(top.x + 120 + 220 * K.LoseIntegrity, top.y + 33, 2, 18), barRed);
            int seen = 0;
            foreach (var u in units) if (u.alive && !u.Human && Visible(u.P)) seen++;
            GUI.Label(new Rect(top.x + 10, top.y + 56, 340, 22),
                $"{Mathf.RoundToInt(pct * 100)}%  (lose below {K.LoseIntegrity * 100:0}%)   Aliens in sight: <b>{seen}</b>", small);

            // ---- log ----
            var lg = Panel(new Rect(W - 370, 10, 360, 150));
            var sb = new StringBuilder();
            foreach (var l in log) sb.AppendLine(l);
            GUI.Label(new Rect(lg.x + 10, lg.y + 6, 340, 140), sb.ToString(), small);

            // ---- help button ----
            var hb = new Rect(W - 370, 166, 110, 26); uiRects.Add(hb);
            if (GUI.Button(hb, showHelp ? "Hide help" : "Help (H)", button)) showHelp = !showHelp;
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.hKey.wasPressedThisFrame && Event.current.type == EventType.Repaint) showHelp = !showHelp;
            if (showHelp) DrawHelp(W);

            // ---- selected unit panel ----
            if (selected != null && selected.alive && phase == Phase.Player) DrawUnitPanel(W, H);

            // ---- squad roster ----
            DrawRoster(H);

            // ---- camera buttons ----
            {
                float cx = W - 190, cy = H - 116;
                if (Btn(new Rect(cx, cy, 56, 38), "<b><<</b>")) RotateView(1);
                if (Btn(new Rect(cx + 62, cy, 56, 38), topView ? "<b>3D</b>" : "<b>Top</b>")) ToggleTopView();
                if (Btn(new Rect(cx + 124, cy, 56, 38), "<b>>></b>")) RotateView(-1);
                GUI.Label(new Rect(cx, cy - 18, 180, 18), "<color=#9ab>Rotate (Q/E) · Top view (T)</color>", small);
            }

            // ---- end turn ----
            if (phase == Phase.Player)
            {
                var et = new Rect(W - 190, H - 70, 180, 56); uiRects.Add(et);
                bool anyAp = false;
                foreach (var u in units) if (u.alive && u.Human && u.ap > 0) anyAp = true;
                if (GUI.Button(et, anyAp ? "End Turn  (Space)" : "<b><color=#ffd24a>End Turn  (Space)</color></b>", button) && !busy) EndTurn();
            }

            DrawTooltip(W, H);

            if (phase == Phase.Won || phase == Phase.Lost) DrawEnd(W, H);
        }

        void DrawIntro(float W, float H)
        {
            var r = Panel(new Rect(W / 2 - 300, H / 2 - 230, 600, 460));
            GUI.Label(new Rect(r.x + 20, r.y + 14, 560, 34), "SPACE DEFENDER", title);
            GUI.Label(new Rect(r.x + 20, r.y + 52, 560, 340),
                "Aliens are pouring through portals in the north of the city. Hold out for <b>12 turns</b> and keep the city's power grid above <b>50%</b>.\n\n" +
                "- Each soldier has <b>2 actions</b>: move + move, or move + shoot. Shooting or throwing ends that soldier's turn.\n" +
                "- Every shot shows a <b>hit %</b>: closer is better, <b>high ground</b> gives up to +20%, <b>cover</b> (walls -40%, cars/crates -20%) protects targets.\n" +
                "- Climb buildings on <color=#f2c21a>yellow ladders</color>, or step up one level at a time. Rooftops see further.\n" +
                "- <b>Fog of war</b>: you only see what your squad (or a Scout beacon) can see.\n" +
                "- Visible aliens <b>telegraph</b> their attacks: <color=#f33>red tiles</color> = attack target, <color=#8f3>green area</color> = acid grenade. Move out of the way!\n" +
                "- Grenades and rockets blow up cover, cars (chain reactions!) and even building floors.\n\n" +
                "<b>Controls:</b> click a soldier, click a blue tile to move · 1 Shoot · 2 Grenade · 3 Special · Tab next soldier · Space end turn · WASD / right-drag pan · Q/E or the << >> buttons rotate 90° · T top view · scroll zoom. Buildings blocking your soldier turn see-through.",
                label);
            var b = new Rect(r.x + 200, r.y + 400, 200, 44); uiRects.Add(b);
            if (GUI.Button(b, "<b>Defend the city</b>", button)) BeginPlay();
        }

        void DrawHelp(float W)
        {
            var r = Panel(new Rect(W - 370, 198, 360, 230));
            GUI.Label(new Rect(r.x + 10, r.y + 6, 340, 220),
                "<b>Controls</b>\nLeft click: select / move / target\n1 Shoot · 2 Grenade · 3 Special · Esc cancel\nTab: next soldier · Space: end turn\nWASD / arrows / right-drag: pan\nQ / E or << >>: rotate 90° · T: top view\nScroll: zoom\n\n<b>Map colours</b>\nBlue tiles: where you can move\n<color=#fd3>Yellow</color>: ladders / ladder climbs on your path\n<color=#f33>Red</color>: alien attack target  <color=#8f3>Green</color>: acid blast\n<color=#c3e>Purple</color>: alien destination / portal opening",
                small);
        }

        void DrawUnitPanel(float W, float H)
        {
            var u = selected;
            var r = Panel(new Rect(W / 2 - 300, H - 128, 600, 118));
            var col = UnitColor(u.kind);
            GUI.Label(new Rect(r.x + 12, r.y + 6, 300, 24), $"<b><color=#{ColorUtility.ToHtmlStringRGB(col)}>{u.name}</color></b>   HP {u.hp}/{u.maxHp}   Actions {u.ap}/2", label);
            GUI.Label(new Rect(r.x + 12, r.y + 28, 580, 20),
                $"Move {u.move} · Sight {u.sight} · Aim {u.aim}% · Range {u.optRange} · Dmg {u.damage} · Height L{T(u.P).Height}{(u.aimBonus > 0 ? " · <color=#ffd24a>Steady +20%</color>" : "")}", small);

            float bx = r.x + 12, by = r.y + 58, bw = 140, bh = 46;
            bool can = u.ap > 0 && !busy;
            GUI.enabled = can;
            if (Btn(new Rect(bx, by, bw, bh), $"{(mode == Mode.Shoot ? "<b>> </b>" : "")}1 · Shoot")) SetMode(Mode.Shoot);
            GUI.enabled = can && u.grenades > 0;
            if (Btn(new Rect(bx + 146, by, bw, bh), $"{(mode == Mode.Grenade ? "<b>> </b>" : "")}2 · Grenade ({u.grenades})")) SetMode(Mode.Grenade);
            var sp = u.SpecialName;
            GUI.enabled = can && sp != null && u.special > 0;
            string spLabel = sp == null ? "3 · -" : u.kind == UnitKind.Sniper ? $"3 · {sp}" : $"3 · {sp} ({u.special})";
            if (Btn(new Rect(bx + 292, by, bw + 10, bh), $"{(mode == Mode.Special ? "<b>> </b>" : "")}{spLabel}")) SetMode(Mode.Special);
            GUI.enabled = !busy;
            if (Btn(new Rect(bx + 448, by, 128, bh), "Tab · Next")) NextUnit();
            GUI.enabled = true;
        }

        bool Btn(Rect r, string text)
        {
            uiRects.Add(r);
            return GUI.Button(r, text, button);
        }

        void DrawRoster(float H)
        {
            float y = 104;
            foreach (var u in units)
            {
                if (!u.Human) continue;
                var r = new Rect(10, y, 210, 34);
                uiRects.Add(r);
                string st = u.alive ? $"{(selected == u ? "<b>> </b>" : "")}{u.name}" : $"<color=#777>{u.name} - down</color>";
                if (GUI.Button(r, "", button) && u.alive && phase == Phase.Player && !busy)
                {
                    Select(u);
                    FocusOn(u.P);
                }
                GUI.Label(new Rect(r.x + 8, r.y + 2, 120, 18), $"<color=#{ColorUtility.ToHtmlStringRGB(UnitColor(u.kind))}>{st}</color>", small);
                if (u.alive)
                {
                    HpBar(new Rect(r.x + 8, r.y + 21, 110, 7), u.hp, u.maxHp, true);
                    GUI.Label(new Rect(r.x + 140, r.y + 6, 70, 20), u.ap > 0 ? $"<color=#8fd>{u.ap} AP</color>" : "<color=#777>done</color>", small);
                }
                y += 38;
            }
        }

        void HpBar(Rect r, int hp, int max, bool human)
        {
            if (max <= 0) return;
            GUI.DrawTexture(new Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2), barBg);
            float w = (r.width - (max - 1) * 2f) / max;
            for (int i = 0; i < max; i++)
                GUI.DrawTexture(new Rect(r.x + i * (w + 2), r.y, w, r.height), i < hp ? (human ? barFg : barRed) : barEmpty);
        }

        void DrawTooltip(float W, float H)
        {
            if (!hover.HasValue || phase != Phase.Player) return;
            var p = hover.Value;
            var t = T(p);
            var sb = new StringBuilder();
            var u = UnitAt(p);
            if (t.fog == FogState.Unexplored) sb.Append("<color=#888>Unexplored</color>");
            else
            {
                sb.Append(t.type == TileType.Building ? $"Building · level {t.Height} · {t.hp}/{t.maxHp} hp{(t.ladder ? " · ladder" : "")}" :
                          t.type == TileType.Portal ? "<color=#c3e>Alien portal</color>" : t.type.ToString());
                if (t.prop != null) sb.Append($" · {t.prop.name}{(t.prop.explosive ? " (explosive)" : "")}");
                if (u != null && (u.Human || Visible(p))) sb.Append($"\n<b>{u.name}</b> HP {u.hp}/{u.maxHp}");
                if (u != null && !u.Human && Visible(p)) sb.Append($"\nPlans: {DescribeIntent(u)}");
            }

            if (selected != null && selected.alive && selected.ap > 0)
            {
                if (mode == Mode.Shoot && ((u != null && !u.Human && Visible(p)) || (t.prop != null && Visible(p))))
                {
                    var info = ShotInfo(selected, p);
                    if (!info.possible) sb.Append($"\n<color=#f77>{info.why}</color>");
                    else
                    {
                        sb.Append($"\n<b><size=18>Hit {info.chance}%</size></b>  · crit {info.crit}% · dmg {info.damage}");
                        foreach (var l in info.lines) sb.Append($"\n  <color=#aab>{l}</color>");
                    }
                }
                else if (mode == Mode.Grenade)
                {
                    float d = Vector2.Distance(selected.P, p);
                    sb.Append(d <= ThrowRange(selected) ? "\n<b>Frag grenade</b>: 3 dmg in radius 1.5, wrecks cover & floors" : "\n<color=#f77>Too far to throw</color>");
                }
                else if (mode == Mode.Special && selected.kind == UnitKind.Heavy)
                    sb.Append("\n<b>Rocket</b>: 4 dmg in radius 1.5, needs line of sight");
                else if (mode == Mode.Special && selected.kind == UnitKind.Scout)
                    sb.Append("\n<b>Sensor beacon</b>: reveals radius 5 for 3 turns");
                else if (mode == Mode.Move && reach != null && reach.cost.TryGetValue(p, out var c) && p != selected.P)
                {
                    sb.Append($"\nMove cost {c}/{selected.move}");
                    var path = reach.PathTo(p);
                    int ladders = 0, steps = 0;
                    for (int i = 1; i < path.Count; i++)
                    {
                        int d = System.Math.Abs(T(path[i]).Height - T(path[i - 1]).Height);
                        if (d >= 2) ladders++; else if (d == 1) steps++;
                    }
                    if (ladders > 0) sb.Append($"\n<color=#fd3>Climbs a ladder</color> to level {T(p).Height}");
                    else if (steps > 0) sb.Append($"\n<color=#7ff>Steps up/down {steps} level(s)</color>");
                    if (T(p).Height >= 2) sb.Append("\n<color=#8fd>High ground: +hit chance, +2 sight</color>");
                }
            }

            var m = MouseGui();
            var content = new GUIContent(sb.ToString());
            float w = 300;
            float h = label.CalcHeight(content, w - 20) + 16;
            var r = new Rect(Mathf.Min(m.x + 18, W - w - 6), Mathf.Min(m.y + 18, H - h - 6), w, h);
            GUI.Box(r, GUIContent.none, box);
            GUI.Label(new Rect(r.x + 10, r.y + 8, w - 20, h - 12), content, label);
        }

        string DescribeIntent(Unit a)
        {
            var it = a.intent;
            switch (it.type)
            {
                case IntentType.Melee: return "<color=#f55>attack the red tile</color>";
                case IntentType.Shoot: return "<color=#f55>shoot at the red tile</color>";
                case IntentType.Grenade: return "<color=#8f3>throw acid at the green area</color>";
                case IntentType.Move: return "advance";
                case IntentType.Hatch: return "<color=#f5a>hatch a Crawler</color>";
                default: return a.kind == UnitKind.Brood ? $"hatching in {a.hatchTimer} turns" : "wait";
            }
        }

        void DrawWorldLabels(float s)
        {
            if (cam == null) return;
            foreach (var u in units)
            {
                if (!u.alive || u.go == null) continue;
                if (!u.Human && !Visible(u.P)) continue;
                var sp = cam.WorldToScreenPoint(u.go.transform.position + Vector3.up * 1.5f);
                if (sp.z < 0) continue;
                var gp = new Vector2(sp.x / s, (Screen.height - sp.y) / s);
                float bw = Mathf.Min(60, u.maxHp * 9);
                HpBar(new Rect(gp.x - bw / 2, gp.y - 4, bw, 6), u.hp, u.maxHp, u.Human);
            }
            foreach (var pg in pings)
            {
                var sp = cam.WorldToScreenPoint(TopOf(pg.p) + Vector3.up * 1f);
                if (sp.z < 0) continue;
                var gp = new Vector2(sp.x / s, (Screen.height - sp.y) / s);
                GUI.Label(new Rect(gp.x - 40, gp.y - 12, 80, 24), "<color=#ff4040><size=20>!</size> noise</color>", worldLabel);
            }
            foreach (var ps in pendingSpawns)
            {
                if (ps.kind == UnitKind.Brood || !Visible(ps.portal)) continue;
                var sp = cam.WorldToScreenPoint(TopOf(ps.portal) + Vector3.up * 2f);
                if (sp.z < 0) continue;
                var gp = new Vector2(sp.x / s, (Screen.height - sp.y) / s);
                GUI.Label(new Rect(gp.x - 50, gp.y - 12, 100, 24), "<color=#e5f>INCOMING</color>", worldLabel);
            }
            foreach (var f in floats)
            {
                var sp = cam.WorldToScreenPoint(f.pos);
                if (sp.z < 0) continue;
                var gp = new Vector2(sp.x / s, (Screen.height - sp.y) / s);
                var c = f.color; c.a = Mathf.Clamp01(1.6f - f.t);
                var old = worldLabel.normal.textColor;
                worldLabel.normal.textColor = c;
                worldLabel.fontSize = 18;
                GUI.Label(new Rect(gp.x - 60, gp.y - 12, 120, 24), f.text, worldLabel);
                worldLabel.fontSize = 13;
                worldLabel.normal.textColor = old;
            }
        }

        void DrawEnd(float W, float H)
        {
            var r = Panel(new Rect(W / 2 - 220, H / 2 - 110, 440, 220));
            bool won = phase == Phase.Won;
            GUI.Label(new Rect(r.x + 20, r.y + 16, 400, 34), won ? "<color=#8fd>CITY SAVED</color>" : "<color=#f77>CITY LOST</color>", title);
            int alive = 0;
            foreach (var u in units) if (u.alive && u.Human) alive++;
            GUI.Label(new Rect(r.x + 20, r.y + 60, 400, 80),
                $"Turns survived: {Mathf.Min(turn, K.MaxTurns)}/{K.MaxTurns}\nSoldiers standing: {alive}/4\nPower grid: {Mathf.RoundToInt(IntegrityPct() * 100)}%", label);
            var b = new Rect(r.x + 120, r.y + 150, 200, 44); uiRects.Add(b);
            if (GUI.Button(b, "<b>Play again</b>", button)) NewGame();
        }
    }
}
