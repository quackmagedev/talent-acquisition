using Godot;
using System.Collections.Generic;

// Game manager: spawns the 17 entities, tracks quotas, relays death panic,
// and resolves the win conditions.
public partial class World : Node2D
{
    [Export]
    public PackedScene PlayerScene { get; set; }

    [Export]
    public PackedScene NpcScene { get; set; }

    private const int NpcCount = 23;
    private const int DeskCount = 10;
    private const float MinSpawnSpacing = 60.0f; // keeps spawns from stacking
    private const float MinDeskSpacing = 90.0f;  // desks are bigger, so a wider gap

    // Interior of the walled arena, inset so nothing spawns inside a wall.
    private static readonly Rect2 SpawnBounds = new Rect2(64, 64, 1024, 520);

    // Desk colors: surface and a slightly darker edge strip along the back.
    private static readonly Color DeskSurface = new Color(0.55f, 0.38f, 0.20f);
    private static readonly Color DeskEdge   = new Color(0.38f, 0.25f, 0.12f);

    private Label _quotaP1Label;
    private Label _quotaP2Label;
    private Control _gameOverPanel;
    private Label _gameOverLabel;
    private PauseToggle _pauseToggle;
    private AudioStreamPlayer _bgMusic;
    private AudioStreamPlayer _acquireSound;
    private AudioStreamPlayer _winSound;

    private readonly List<NPC> _npcs = new();
    private readonly int[] _quotas = new int[2]; // [0] = Player 1, [1] = Player 2
    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        _quotaP1Label = GetNode<Label>("HUD/QuotaP1Label");
        _quotaP2Label = GetNode<Label>("HUD/QuotaP2Label");
        _gameOverPanel = GetNode<Control>("HUD/GameOverPanel");
        _gameOverLabel = GetNode<Label>("HUD/GameOverPanel/GameOverBox/GameOverLabel");
        _pauseToggle  = GetNode<PauseToggle>("PauseToggle");
        _bgMusic      = GetNode<AudioStreamPlayer>("BGMusic");
        _acquireSound = GetNode<AudioStreamPlayer>("AcquireSound");
        _winSound     = GetNode<AudioStreamPlayer>("WinSound");

        var music = GD.Load<AudioStreamMP3>("res://audio/alien-techno.mp3");
        music.Loop = true;
        _bgMusic.Stream = music;
        _bgMusic.Play();

        _acquireSound.Stream = GD.Load<AudioStream>("res://audio/confirmation_001.ogg");
        _winSound.Stream     = GD.Load<AudioStream>("res://audio/jingles_NES00.ogg");
        // Must play through the tree pause that immediately follows EndGame().
        _winSound.ProcessMode = ProcessModeEnum.Always;

        // process_mode = Always on the panel means the buttons work while paused.
        GetNode<Button>("HUD/GameOverPanel/GameOverBox/StartOverButton").Pressed += OnStartOver;
        GetNode<Button>("HUD/GameOverPanel/GameOverBox/MenuButton").Pressed += OnBackToMenu;

        // v0.1 disables the quota system entirely, so hide its UI.
        _quotaP1Label.Visible = GameRules.QuotaEnabled;
        _quotaP2Label.Visible = GameRules.QuotaEnabled;

        _rng.Randomize();
        SpawnDesks();
        SpawnEntities();
        UpdateQuotaLabels();
    }

    private void SpawnEntities()
    {
        List<Vector2> spots = PickSpawnPositions(2 + NpcCount);

        for (int number = 1; number <= 2; number++)
        {
            var player = PlayerScene.Instantiate<Player>();
            player.PlayerNumber = number;
            player.Position = spots[number - 1];
            player.NpcAcquired += OnNpcAcquired;
            player.RivalTagged += OnRivalTagged;
            AddChild(player);
        }

        for (int i = 0; i < NpcCount; i++)
        {
            var npc = NpcScene.Instantiate<NPC>();
            npc.Position = spots[2 + i];
            npc.Died += OnNpcDied;
            AddChild(npc);
            _npcs.Add(npc);
        }
    }

    // Builds DeskCount static desk obstacles at random positions in the arena.
    // Each desk is a StaticBody2D with a ColorRect visual — no scene file needed.
    // Randomly oriented (landscape or portrait) for variety.
    private void SpawnDesks()
    {
        const float deskW = 64f;
        const float deskH = 32f;

        var taken = new List<Vector2>();

        int attempts = 0;
        while (taken.Count < DeskCount && attempts < 1000)
        {
            attempts++;
            var pos = new Vector2(
                _rng.RandfRange(SpawnBounds.Position.X + 40, SpawnBounds.End.X - 40),
                _rng.RandfRange(SpawnBounds.Position.Y + 40, SpawnBounds.End.Y - 40));

            bool tooClose = false;
            foreach (Vector2 existing in taken)
            {
                if (existing.DistanceTo(pos) < MinDeskSpacing) { tooClose = true; break; }
            }
            if (tooClose) continue;
            taken.Add(pos);

            // Randomly rotate 90° so some desks face a different direction.
            bool rotated = _rng.RandiRange(0, 1) == 1;
            float w = rotated ? deskH : deskW;
            float h = rotated ? deskW : deskH;

            var desk = new StaticBody2D();
            desk.CollisionLayer = 1;
            desk.CollisionMask = 0;
            desk.Position = pos;

            var shape = new CollisionShape2D();
            shape.Shape = new RectangleShape2D { Size = new Vector2(w, h) };
            desk.AddChild(shape);

            // Main surface.
            var surface = new ColorRect();
            surface.Color = DeskSurface;
            surface.Size = new Vector2(w, h);
            surface.Position = new Vector2(-w / 2f, -h / 2f);
            desk.AddChild(surface);

            // Thin darker strip along the back edge so it reads as a 3D-ish desk.
            const float edgeThickness = 5f;
            var edge = new ColorRect();
            edge.Color = DeskEdge;
            edge.Size = new Vector2(w, edgeThickness);
            edge.Position = new Vector2(-w / 2f, -h / 2f);
            desk.AddChild(edge);

            AddChild(desk);
        }
    }

    // Random positions inside the arena, kept a minimum distance apart.
    private List<Vector2> PickSpawnPositions(int count)
    {
        var positions = new List<Vector2>();
        while (positions.Count < count)
        {
            var candidate = new Vector2(
                _rng.RandfRange(SpawnBounds.Position.X, SpawnBounds.End.X),
                _rng.RandfRange(SpawnBounds.Position.Y, SpawnBounds.End.Y));

            bool tooClose = false;
            foreach (Vector2 existing in positions)
            {
                if (existing.DistanceTo(candidate) < MinSpawnSpacing)
                {
                    tooClose = true;
                    break;
                }
            }

            if (!tooClose)
                positions.Add(candidate);
        }
        return positions;
    }

    private void OnNpcAcquired(Player player)
    {
        int index = player.PlayerNumber - 1;
        _quotas[index]++;
        UpdateQuotaLabels();
        _acquireSound.Play();

        if (_quotas[index] >= GameRules.QuotaTarget)
            EndGame(player.PlayerNumber, $"PLAYER {player.PlayerNumber} WINS BY QUOTA");
    }

    private void OnRivalTagged(Player tagger)
    {
        EndGame(tagger.PlayerNumber, $"PLAYER {tagger.PlayerNumber} WINS BY HOSTILE TAKEOVER");
    }

    // A death panics every other living NPC near the body (NPC checks the range).
    private void OnNpcDied(NPC dead)
    {
        foreach (NPC npc in _npcs)
        {
            if (npc != dead)
                npc.OnNearbyDeath(dead.GlobalPosition);
        }
    }

    private void UpdateQuotaLabels()
    {
        _quotaP1Label.Text = $"Player 1 Quota: {_quotas[0]}/{GameRules.QuotaTarget}";
        _quotaP2Label.Text = $"Player 2 Quota: {_quotas[1]}/{GameRules.QuotaTarget}";
    }

    private void EndGame(int winnerNumber, string message)
    {
        _pauseToggle.IsGameOver = true;
        _gameOverLabel.Text = $"GAME OVER\n{message}";
        _gameOverLabel.AddThemeColorOverride("font_color",
            winnerNumber == 1 ? new Color(0.2f, 1.0f, 0.2f) : new Color(0.3f, 0.5f, 1.0f));
        _gameOverPanel.Visible = true;
        _winSound.Play();
        GetTree().Paused = true;
    }

    private void OnStartOver()
    {
        GetTree().Paused = false;
        GetTree().ReloadCurrentScene();
    }

    private void OnBackToMenu()
    {
        GetTree().Paused = false;
        GetTree().ChangeSceneToFile("res://scenes/Menu.tscn");
    }
}
