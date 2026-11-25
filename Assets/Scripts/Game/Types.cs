using System.Collections.Generic;
using UnityEngine;

namespace SpaceDefender
{
    public enum TileType { Street, Park, Water, Building, Rubble, Portal }
    public enum FogState { Unexplored, Explored, Visible }
    public enum Faction { Human, Alien }
    public enum UnitKind { Rifleman, Sniper, Heavy, Scout, Crawler, Spitter, Brood }
    public enum IntentType { None, Move, Melee, Shoot, Grenade, Hatch }

    public static class K
    {
        public const int W = 20, H = 20;
        public const float Level = 1f;     // world height of one building level
        public const int MaxTurns = 12;     // survive this many turns to win
        public const float LoseIntegrity = 0.5f;
    }

    public class Prop
    {
        public string name;
        public int hp;
        public bool explosive;
        public float height = 0.5f;
        public Color color;
        public GameObject go;
    }

    public class Tile
    {
        public int x, z;
        public TileType type;
        public int hp, maxHp;          // buildings only: 2 hp per level
        public bool ladder;
        public Prop prop;
        public FogState fog;
        public int district;
        public Color baseColor;
        public GameObject root;
        public readonly List<Renderer> renderers = new List<Renderer>();
        public readonly List<Color> colors = new List<Color>();

        public Vector2Int P => new Vector2Int(x, z);
        public int Height => type == TileType.Building ? Mathf.CeilToInt(hp / 2f) : 0;
        public float TopY => Height * K.Level;
        public bool Walkable => type != TileType.Water && prop == null;
    }

    public class Intent
    {
        public IntentType type;
        public Vector2Int target;
        public Vector2Int moveTo;
        public List<Vector2Int> path = new List<Vector2Int>();
    }

    public class Unit
    {
        public UnitKind kind;
        public Faction faction;
        public string name;
        public int x, z;
        public int hp, maxHp, ap;
        public int move, sight, aim, optRange, damage;
        public int grenades, special;
        public bool climber;
        public bool alive = true;
        public int aimBonus;           // sniper steady aim
        public GameObject go;
        public Renderer[] rends;

        // aliens
        public Intent intent = new Intent();
        public int cooldown;
        public int hatchTimer;

        public Vector2Int P => new Vector2Int(x, z);
        public bool Human => faction == Faction.Human;

        public static Unit Make(UnitKind k)
        {
            var u = new Unit { kind = k, faction = k <= UnitKind.Scout ? Faction.Human : Faction.Alien };
            switch (k)
            {
                case UnitKind.Rifleman: u.Set("Rifleman", 6, 5, 7, 70, 6, 3); u.grenades = 2; break;
                case UnitKind.Sniper: u.Set("Sniper", 4, 4, 9, 80, 12, 4); u.grenades = 0; u.special = 99; break;
                case UnitKind.Heavy: u.Set("Heavy", 7, 4, 6, 60, 7, 3); u.grenades = 1; u.special = 2; break;
                case UnitKind.Scout: u.Set("Scout", 4, 7, 10, 65, 4, 2); u.grenades = 1; u.special = 2; u.climber = true; break;
                case UnitKind.Crawler: u.Set("Crawler", 3, 5, 6, 85, 1, 2); u.climber = true; break;
                case UnitKind.Spitter: u.Set("Spitter", 4, 4, 8, 60, 8, 2); break;
                case UnitKind.Brood: u.Set("Brood Pod", 6, 0, 4, 0, 0, 0); u.hatchTimer = 3; break;
            }
            return u;
        }

        void Set(string n, int hp_, int mv, int sg, int am, int rng, int dmg)
        {
            name = n; hp = maxHp = hp_; move = mv; sight = sg; aim = am; optRange = rng; damage = dmg;
        }

        public string SpecialName
        {
            get
            {
                switch (kind)
                {
                    case UnitKind.Sniper: return "Steady Aim";
                    case UnitKind.Heavy: return "Rocket";
                    case UnitKind.Scout: return "Sensor Beacon";
                    default: return null;
                }
            }
        }
    }

    public class Beacon { public Vector2Int p; public int turnsLeft; public GameObject go; }
    public class FloatText { public Vector3 pos; public string text; public Color color; public float t; }
    public class Ping { public Vector2Int p; public int turnsLeft; }
}
