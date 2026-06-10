using Godot;

// Handles Escape pause/unpause. Lives as a child of World with
// process_mode = Always so it keeps receiving input while the tree is
// paused, without forcing all of World's other children to do the same.
public partial class PauseToggle : Node
{
    private Control _pausePanel;

    // World sets this to true on game-over so Escape can't unlock that screen.
    public bool IsGameOver { get; set; }

    public override void _Ready()
    {
        _pausePanel = GetParent().GetNode<Control>("HUD/PausePanel");
        GetParent().GetNode<Button>("HUD/PausePanel/PauseBox/PauseMenuButton").Pressed += OnBackToMenu;
    }

    public override void _UnhandledInput(InputEvent evt)
    {
        if (IsGameOver || !evt.IsActionPressed("ui_cancel"))
            return;

        bool nowPaused = !GetTree().Paused;
        GetTree().Paused = nowPaused;
        _pausePanel.Visible = nowPaused;
    }

    private void OnBackToMenu()
    {
        GetTree().Paused = false;
        GetTree().ChangeSceneToFile("res://scenes/Menu.tscn");
    }
}
