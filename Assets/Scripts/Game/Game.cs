using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpaceDefender
{
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            SceneManager.sceneLoaded -= OnLoaded;
            SceneManager.sceneLoaded += OnLoaded;
            Ensure();
        }

        static void OnLoaded(Scene s, LoadSceneMode m) => Ensure();

        // Referenced so the web build keeps these engine classes (CreatePrimitive adds them at runtime).
        [UnityEngine.Scripting.Preserve]
        static readonly System.Type[] KeepEngineClasses =
            { typeof(CapsuleCollider), typeof(SphereCollider), typeof(BoxCollider), typeof(MeshCollider) };

        static void Ensure()
        {
            if (Object.FindAnyObjectByType<Game>() == null)
                new GameObject("SpaceDefender").AddComponent<Game>();
        }
    }

    public enum Phase { Intro, Player, Alien, Won, Lost }
    public enum Mode { Move, Shoot, Grenade, Special }

    /// Main game controller. Split across partial files:
    /// Game.Map (generation + visuals), Game.Rules (movement, LOS, hit chance, explosions),
    /// Game.AI (alien intents + phase), Game.Input (camera + clicks), Game.UI (HUD).
    public partial class Game : MonoBehaviour
    {
        public static Game I;

        public Tile[,] tiles;
        public readonly List<Unit> units = new List<Unit>();
        public readonly List<Beacon> beacons = new List<Beacon>();
        public readonly List<Ping> pings = new List<Ping>();
        public readonly List<Vector2Int> portals = new List<Vector2Int>();
        readonly List<string> log = new List<string>();
        readonly List<FloatText> floats = new List<FloatText>();

        public Phase phase = Phase.Intro;
        public Mode mode = Mode.Move;
        public int turn = 1;
        public Unit selected;
        public bool busy;
        int startIntegrity;
        System.Random rng;
        Transform world;

        void Awake()
        {
            I = this;
            rng = new System.Random();
        }

        void Start()
        {
            SetupMaterials();
            NewGame();
#if UNITY_EDITOR
            TryAutoTest();
#endif
        }

        public void NewGame()
        {
            StopAllCoroutines();
            if (world != null) Destroy(world.gameObject);
            world = new GameObject("World").transform;
            units.Clear(); beacons.Clear(); pings.Clear(); portals.Clear(); log.Clear(); floats.Clear();
            ClearMarkers();
            turn = 1; selected = null; busy = false; mode = Mode.Move;

            GenerateMap();
            BuildMapVisuals();
            startIntegrity = Integrity();
            SpawnSquad();
            SetupCamera();
            SpawnInitialAliens();
            UpdateFog();
            phase = Phase.Intro;
        }

        public void BeginPlay()
        {
            phase = Phase.Player;
            StartPlayerTurn();
        }

        // ---------- turn flow ----------

        void StartPlayerTurn()
        {
            foreach (var u in units) if (u.alive && u.Human) { u.ap = 2; }
            for (int i = beacons.Count - 1; i >= 0; i--)
                if (--beacons[i].turnsLeft <= 0) { Destroy(beacons[i].go); beacons.RemoveAt(i); }
            UpdateFog();
            PlanAlienIntents();
            PlanSpawns();
            if (selected == null || !selected.alive) selected = FirstReady();
            mode = Mode.Move;
            RefreshHighlights();
            Log($"— Turn {turn}/{K.MaxTurns}: your move —");
        }

        public void EndTurn()
        {
            if (phase != Phase.Player || busy) return;
            Run(AlienPhase());
        }

        IEnumerator AlienPhase()
        {
            busy = true;
            phase = Phase.Alien;
            ClearMarkers();
            for (int i = pings.Count - 1; i >= 0; i--) if (--pings[i].turnsLeft <= 0) pings.RemoveAt(i);

            yield return RunAliens();
            if (CheckEnd()) { busy = false; yield break; }

            DoSpawns();
            UpdateFog();
            if (CheckEnd()) { busy = false; yield break; }

            if (turn >= K.MaxTurns)
            {
                phase = Phase.Won;
                Log("The city held. Reinforcements have arrived!");
                busy = false;
                yield break;
            }
            turn++;
            phase = Phase.Player;
            busy = false;
            StartPlayerTurn();
        }

        bool CheckEnd()
        {
            bool anyHuman = false;
            foreach (var u in units) if (u.alive && u.Human) anyHuman = true;
            if (!anyHuman) { phase = Phase.Lost; Log("Your squad has fallen."); return true; }
            if (IntegrityPct() < K.LoseIntegrity) { phase = Phase.Lost; Log("The city's power grid has collapsed."); return true; }
            return false;
        }

        public int Integrity()
        {
            int s = 0;
            foreach (var t in tiles) if (t.type == TileType.Building) s += t.hp;
            return s;
        }

        public float IntegrityPct() => startIntegrity <= 0 ? 1f : Integrity() / (float)startIntegrity;

        public Unit FirstReady()
        {
            foreach (var u in units) if (u.alive && u.Human && u.ap > 0) return u;
            foreach (var u in units) if (u.alive && u.Human) return u;
            return null;
        }

        public void Log(string s)
        {
            log.Add(s);
            if (log.Count > 7) log.RemoveAt(0);
        }

        public void Float(Vector3 pos, string text, Color c)
        {
            floats.Add(new FloatText { pos = pos, text = text, color = c, t = 0 });
        }

        // ---------- coroutines ----------

        /// Starts a coroutine. In the editor smoke test the game is driven manually (see Game.AutoTest).
        public void Run(IEnumerator e)
        {
#if UNITY_EDITOR
            if (manualDrive) { runners.Add(new Stack<IEnumerator>(new[] { e })); return; }
#endif
            StartCoroutine(e);
        }

        /// Frame time used by animations.
        public float Dt
        {
            get
            {
#if UNITY_EDITOR
                if (manualDrive) return 0.05f;
#endif
                return Mathf.Min(Time.deltaTime, 0.1f);
            }
        }

        // ---------- helpers ----------

        public bool In(int x, int z) => x >= 0 && z >= 0 && x < K.W && z < K.H;
        public bool In(Vector2Int p) => In(p.x, p.y);
        public Tile T(Vector2Int p) => tiles[p.x, p.y];

        public Unit UnitAt(int x, int z)
        {
            foreach (var u in units) if (u.alive && u.x == x && u.z == z) return u;
            return null;
        }
        public Unit UnitAt(Vector2Int p) => UnitAt(p.x, p.y);

        public Vector3 TopOf(Vector2Int p) => new Vector3(p.x, tiles[p.x, p.y].TopY, p.y);
        public Vector3 UnitPos(Unit u) => TopOf(u.P);

        public int R(int maxExclusive) => rng.Next(maxExclusive);
        public float Rf() => (float)rng.NextDouble();

        public static readonly Vector2Int[] Dirs =
            { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
    }
}
