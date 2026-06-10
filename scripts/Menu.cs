using Godot;

// Version-select screen shown at launch.
public partial class Menu : Control
{
    public override void _Ready()
    {
        var quotaCheck = GetNode<CheckBox>("CenterContainer/VBoxContainer/QuotaCheckBox");
        quotaCheck.ButtonPressed = GameRules.QuotaEnabled;
        quotaCheck.Toggled += (pressed) => GameRules.QuotaEnabled = pressed;

        GetNode<Button>("CenterContainer/VBoxContainer/StartButton").Pressed += () =>
            GetTree().ChangeSceneToFile("res://scenes/World.tscn");
    }
}
