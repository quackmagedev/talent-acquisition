// Global match settings chosen on the menu screen, plus shared tuning constants.
public static class GameRules
{
    public static bool QuotaEnabled = false;

    // Shelved for now — set to true when ready to bring the maze back.
    public static bool MazeEnabled = false;

    // Every entity (players and NPCs) moves at this exact speed, so an alien
    // walking normally is indistinguishable from a human.
    public const float BaseSpeed = 150.0f;

    // Acquisitions needed to win by quota.
    public const int QuotaTarget = 5;
}
