using Godot;

// A wandering human. Timer-based FSM: Idle <-> Walk, with a Panic state
// triggered when another NPC dies within 150 px.
public partial class NPC : CharacterBody2D
{
    // Fired once when this NPC is tagged. World relays it so nearby NPCs panic.
    [Signal]
    public delegate void DiedEventHandler(NPC npc);

    private enum State { Idle, Walk, Panic }

    private const float PanicSpeedMultiplier = 2.0f;  // panic = double speed
    private const float PanicSeconds = 2.5f;
    private const float PanicRadius = 150.0f;         // px from a death that causes panic
    private const float FootstepInterval = 0.32f;     // seconds between steps at base speed

    public bool IsDead { get; private set; }
    public bool IsAcquired { get; private set; }

    private State _state = State.Idle;
    private Vector2 _walkDirection = Vector2.Zero;
    private Timer _stateTimer;
    private Node2D _visual;
    private ColorRect _acquiredMarker;
    private AudioStreamPlayer2D _deathSound;
    private AudioStreamPlayer2D _panicSound;
    private AudioStreamPlayer2D _footstepPlayer;
    private AudioStream[] _footstepSounds;
    private int _footstepIndex;
    private float _footstepTimer;
    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        _visual = GetNode<Node2D>("Visual");
        _acquiredMarker = GetNode<ColorRect>("AcquiredMarker");
        _stateTimer = GetNode<Timer>("StateTimer");
        _stateTimer.Timeout += OnStateTimerTimeout;

        // Same shared placeholder art as the players.
        var skin = GetNode<Sprite2D>("Visual/SkinSprite");
        skin.Texture = CharacterArt.SkinTexture;
        skin.Modulate = CharacterArt.SkinTone;
        GetNode<Sprite2D>("Visual/ClothesSprite").Texture = CharacterArt.ClothesTexture;

        _deathSound     = GetNode<AudioStreamPlayer2D>("DeathSound");
        _panicSound     = GetNode<AudioStreamPlayer2D>("PanicSound");
        _footstepPlayer = GetNode<AudioStreamPlayer2D>("FootstepPlayer");

        _deathSound.Stream       = GD.Load<AudioStream>("res://audio/impactPunch_heavy_000.ogg");
        _panicSound.Stream       = GD.Load<AudioStream>("res://audio/phaserDown1.ogg");
        _footstepPlayer.VolumeDb = -24f; // 23 NPCs stepping simultaneously — keep them ambient
        _footstepSounds = new AudioStream[]
        {
            GD.Load<AudioStream>("res://audio/footstep_carpet_000.ogg"),
            GD.Load<AudioStream>("res://audio/footstep_carpet_001.ogg"),
            GD.Load<AudioStream>("res://audio/footstep_carpet_002.ogg"),
            GD.Load<AudioStream>("res://audio/footstep_carpet_003.ogg"),
            GD.Load<AudioStream>("res://audio/footstep_carpet_004.ogg"),
        };

        _rng.Randomize();
        EnterIdle();
    }

    public override void _PhysicsProcess(double delta)
    {
        float speed = _state switch
        {
            State.Walk => GameRules.BaseSpeed,
            State.Panic => GameRules.BaseSpeed * PanicSpeedMultiplier,
            _ => 0.0f, // Idle
        };
        Velocity = _walkDirection * speed;
        MoveAndSlide();

        if (speed > 0f)
        {
            float multiplier = _state == State.Panic ? PanicSpeedMultiplier : 1.0f;
            float interval = FootstepInterval / multiplier;
            _footstepTimer -= (float)delta;
            if (_footstepTimer <= 0f)
            {
                _footstepPlayer.Stream = _footstepSounds[_footstepIndex % _footstepSounds.Length];
                _footstepPlayer.Play();
                _footstepIndex++;
                _footstepTimer = interval;
            }
        }
        else
        {
            _footstepTimer = 0f;
        }
    }

    // Wander loop: idle 1-3s -> walk 1-3s -> idle... Panic also funnels
    // back into Idle when its 2.5 seconds run out.
    private void OnStateTimerTimeout()
    {
        if (_state == State.Idle)
            EnterWalk();
        else
            EnterIdle();
    }

    private void EnterIdle()
    {
        _state = State.Idle;
        _walkDirection = Vector2.Zero;
        _stateTimer.Start(_rng.RandfRange(1.0f, 3.0f));
    }

    private void EnterWalk()
    {
        _state = State.Walk;
        _walkDirection = RandomDirection();
        _stateTimer.Start(_rng.RandfRange(1.0f, 3.0f));
    }

    // World calls this on every NPC whenever any NPC dies.
    public void OnNearbyDeath(Vector2 deathPosition)
    {
        if (IsDead || GlobalPosition.DistanceTo(deathPosition) > PanicRadius)
            return;

        bool wasAlreadyPanicking = _state == State.Panic;
        _state = State.Panic;
        _walkDirection = FleeDirection(deathPosition);
        _stateTimer.Start(PanicSeconds);

        if (!wasAlreadyPanicking)
            _panicSound.Play();
    }

    // Returns the 8-way direction that best points away from the incident.
    private Vector2 FleeDirection(Vector2 threatPosition)
    {
        Vector2 away = (GlobalPosition - threatPosition).Normalized();

        // Pick whichever of the 8 directions has the highest dot product with "away".
        Vector2 best = EightDirections[0];
        float bestDot = away.Dot(best);
        for (int i = 1; i < EightDirections.Length; i++)
        {
            float dot = away.Dot(EightDirections[i]);
            if (dot > bestDot)
            {
                bestDot = dot;
                best = EightDirections[i];
            }
        }
        return best;
    }

    // Tagged by a player: freeze, fall sideways, announce the death.
    public void Kill()
    {
        if (IsDead)
            return;

        IsDead = true;
        SetPhysicsProcess(false);
        _stateTimer.Stop();
        Velocity = Vector2.Zero;
        _visual.RotationDegrees = 90.0f;
        _deathSound.Play();
        EmitSignal(SignalName.Died, this);
    }

    // Proximity upload finished: show a floating square in the owner's color.
    public void MarkAcquired(Color ownerColor)
    {
        IsAcquired = true;
        _acquiredMarker.Color = ownerColor;
        _acquiredMarker.Visible = true;
    }

    private static readonly Vector2[] EightDirections =
    {
        Vector2.Right,
        Vector2.Left,
        Vector2.Up,
        Vector2.Down,
        new Vector2( 1,  1).Normalized(),
        new Vector2(-1,  1).Normalized(),
        new Vector2( 1, -1).Normalized(),
        new Vector2(-1, -1).Normalized(),
    };

    private Vector2 RandomDirection()
    {
        return EightDirections[_rng.RandiRange(0, EightDirections.Length - 1)];
    }
}
