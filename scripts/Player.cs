using Godot;
using System.Collections.Generic;

// One of the two alien players. 8-way movement, a sprint "glitch" that
// recolors the skin, a passive proximity upload, and an active tag attack.
public partial class Player : CharacterBody2D
{
    // Fired when this player finishes a 3-second proximity upload on an NPC.
    [Signal]
    public delegate void NpcAcquiredEventHandler(Player player);

    // Fired when this player's tag connects with the rival player.
    [Signal]
    public delegate void RivalTaggedEventHandler(Player tagger);

    // 1 or 2; set by World before the player enters the tree.
    [Export]
    public int PlayerNumber { get; set; } = 1;

    private const float SprintMultiplier = 1.5f;   // sprint = +50% speed
    private const float UploadSeconds = 3.0f;      // continuous time to acquire an NPC
    private const float TagActiveSeconds = 0.15f;  // how long the tag hitbox stays live
    private const float TagReach = 30.0f;          // hitbox center distance ahead of the body
    private const float FootstepInterval = 0.32f;  // seconds between steps at base speed

    // P1 glitches green, P2 glitches blue. Also marks acquired NPCs.
    public Color PlayerColor => PlayerNumber == 1
        ? new Color(0.2f, 1.0f, 0.2f)
        : new Color(0.3f, 0.5f, 1.0f);

    // True once the rival has tagged this player (eliminated).
    public bool IsDown { get; private set; }

    private Node2D _visual;
    private Sprite2D _skinSprite;
    private Sprite2D _clothesSprite;
    private Area2D _uploadArea;
    private Area2D _tagArea;
    private CollisionShape2D _tagShape;
    private ColorRect _tagFlash;

    private string _prefix;                  // "p1" or "p2", prefix for input action names
    private Vector2 _facing = Vector2.Down;  // last non-zero movement direction
    private float _tagTimeLeft;              // seconds the tag hitbox stays active

    private AudioStreamPlayer2D _tagSound;
    private AudioStreamPlayer2D _glitchSound;
    private AudioStreamPlayer2D _footstepPlayer;
    private AudioStream[] _footstepSounds;
    private int _footstepIndex;
    private float _footstepTimer;
    private bool _wasSprinting;

    // Seconds each nearby un-acquired NPC has spent inside the upload radius.
    private readonly Dictionary<NPC, float> _uploadProgress = new();

    public override void _Ready()
    {
        _visual = GetNode<Node2D>("Visual");
        _skinSprite = GetNode<Sprite2D>("Visual/SkinSprite");
        _clothesSprite = GetNode<Sprite2D>("Visual/ClothesSprite");
        _uploadArea = GetNode<Area2D>("UploadArea");
        _tagArea = GetNode<Area2D>("TagArea");
        _tagShape = GetNode<CollisionShape2D>("TagArea/TagShape");
        _tagFlash = GetNode<ColorRect>("TagArea/TagFlash");

        _prefix = PlayerNumber == 1 ? "p1" : "p2";

        // Everyone shares the exact same placeholder art.
        _skinSprite.Texture = CharacterArt.SkinTexture;
        _clothesSprite.Texture = CharacterArt.ClothesTexture;
        _skinSprite.Modulate = CharacterArt.SkinTone;

        _tagSound      = GetNode<AudioStreamPlayer2D>("TagSound");
        _glitchSound   = GetNode<AudioStreamPlayer2D>("GlitchSound");
        _footstepPlayer = GetNode<AudioStreamPlayer2D>("FootstepPlayer");

        _tagSound.Stream       = GD.Load<AudioStream>("res://audio/laserSmall_000.ogg");
        _glitchSound.Stream    = GD.Load<AudioStream>("res://audio/phaserDown3.ogg");
        _footstepPlayer.VolumeDb = -18f;
        _footstepSounds = new AudioStream[]
        {
            GD.Load<AudioStream>("res://audio/footstep_carpet_000.ogg"),
            GD.Load<AudioStream>("res://audio/footstep_carpet_001.ogg"),
            GD.Load<AudioStream>("res://audio/footstep_carpet_002.ogg"),
            GD.Load<AudioStream>("res://audio/footstep_carpet_003.ogg"),
            GD.Load<AudioStream>("res://audio/footstep_carpet_004.ogg"),
        };
    }

    public override void _PhysicsProcess(double delta)
    {
        if (IsDown)
            return; // eliminated players do nothing

        // --- Movement (8-way) and the sprint glitch ---
        Vector2 input = Input.GetVector(
            _prefix + "_left", _prefix + "_right", _prefix + "_up", _prefix + "_down");
        bool sprinting = Input.IsActionPressed(_prefix + "_sprint");

        Velocity = input * GameRules.BaseSpeed * (sprinting ? SprintMultiplier : 1.0f);
        MoveAndSlide();

        if (input != Vector2.Zero)
            _facing = input.Normalized();

        // Sprinting exposes the alien: skin shifts to the player's color.
        _skinSprite.Modulate = sprinting ? PlayerColor : CharacterArt.SkinTone;

        // Glitch sound fires once at the moment sprint begins.
        if (sprinting && !_wasSprinting)
            _glitchSound.Play();
        _wasSprinting = sprinting;

        // Footstep sounds: step interval shortens proportionally when sprinting.
        if (input != Vector2.Zero)
        {
            float interval = FootstepInterval / (sprinting ? SprintMultiplier : 1.0f);
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
            _footstepTimer = 0f; // reset so next step plays immediately on next movement
        }

        // --- Active mechanic: tag ---
        if (Input.IsActionJustPressed(_prefix + "_tag") && _tagTimeLeft <= 0.0f)
            StartTag();
        if (_tagTimeLeft > 0.0f)
            UpdateTag((float)delta);

        // --- Passive mechanic: proximity upload (disabled in v0.1) ---
        if (GameRules.QuotaEnabled)
            UpdateUpload((float)delta);
    }

    // Swings the brief hitbox around to face the current movement direction.
    private void StartTag()
    {
        _tagArea.Position = _facing * TagReach;
        _tagArea.Rotation = _facing.Angle();
        _tagShape.Disabled = false;
        _tagFlash.Visible = true;
        _tagTimeLeft = TagActiveSeconds;
        _tagSound.Play();
    }

    // While the hitbox is live, anything caught in it gets hit.
    private void UpdateTag(float delta)
    {
        foreach (Node2D body in _tagArea.GetOverlappingBodies())
        {
            if (body is NPC npc)
            {
                npc.Kill(); // Kill() ignores already-dead NPCs
            }
            else if (body is Player rival && rival != this && !rival.IsDown)
            {
                rival.FallOver();
                EmitSignal(SignalName.RivalTagged, this);
            }
        }

        _tagTimeLeft -= delta;
        if (_tagTimeLeft <= 0.0f)
        {
            _tagShape.Disabled = true;
            _tagFlash.Visible = false;
        }
    }

    // Accumulates in-range time per NPC; 3 continuous seconds completes the upload.
    private void UpdateUpload(float delta)
    {
        var inRange = new HashSet<NPC>();
        foreach (Node2D body in _uploadArea.GetOverlappingBodies())
        {
            if (body is not NPC npc || npc.IsAcquired || npc.IsDead)
                continue;

            inRange.Add(npc);
            _uploadProgress.TryGetValue(npc, out float elapsed);
            elapsed += delta;
            _uploadProgress[npc] = elapsed;

            if (elapsed >= UploadSeconds)
            {
                npc.MarkAcquired(PlayerColor);
                EmitSignal(SignalName.NpcAcquired, this);
            }
        }

        // The timer must be CONTINUOUS: forget NPCs that left the radius
        // (or died / were acquired) so their progress resets to zero.
        var stale = new List<NPC>();
        foreach (NPC tracked in _uploadProgress.Keys)
        {
            if (!inRange.Contains(tracked))
                stale.Add(tracked);
        }
        foreach (NPC tracked in stale)
            _uploadProgress.Remove(tracked);
    }

    // Called when the rival's tag connects: fall sideways and stop acting.
    public void FallOver()
    {
        IsDown = true;
        Velocity = Vector2.Zero;
        _visual.RotationDegrees = 90.0f;
    }
}
