using Godot;
using System.Collections.Generic;

// TileMapLayer that procedurally carves a recursive-backtracker maze on _Ready().
// The grid uses the standard "walls are tiles" layout:
//   - room cells sit at odd tile coordinates (1,1), (3,1), (1,3) ...
//   - the tiles between them are walls, carved away to open a passage
// Centered in the 1152x648 viewport at runtime; QueueFree()s itself if disabled.
public partial class MazeGenerator : TileMapLayer
{
    private const int TileSize = 32;
    private const int MazeCols = 9; // rooms wide  → tile grid = 19 tiles wide  (608 px)
    private const int MazeRows = 5; // rooms tall  → tile grid = 11 tiles tall  (352 px)

    // Warm tan/brown so cubicle walls read differently from the dark arena border.
    private static readonly Color WallColor = new Color(0.58f, 0.44f, 0.28f);

    private readonly bool[,] _visited = new bool[MazeCols, MazeRows];
    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        if (!GameRules.MazeEnabled)
        {
            QueueFree();
            return;
        }

        _rng.Randomize();
        BuildTileSet();
        GenerateMaze();
        CenterInArena();
    }

    private void BuildTileSet()
    {
        var ts = new TileSet();
        ts.TileSize = new Vector2I(TileSize, TileSize);

        // Physics layer 0 of this TileSet maps to collision layer 1 (the wall layer).
        // Players and NPCs carry collision_mask = 7 (bits 1+2+4), so they'll stop here.
        ts.AddPhysicsLayer(0);
        ts.SetPhysicsLayerCollisionLayer(0, 1u);
        ts.SetPhysicsLayerCollisionMask(0, 0u);

        // Single solid tile drawn as a flat color rectangle.
        var img = Image.CreateEmpty(TileSize, TileSize, false, Image.Format.Rgba8);
        img.Fill(WallColor);
        var source = new TileSetAtlasSource();
        source.Texture = ImageTexture.CreateFromImage(img);
        source.TextureRegionSize = new Vector2I(TileSize, TileSize);
        source.CreateTile(Vector2I.Zero);

        // Source must be in the TileSet before GetTileData can see the physics layers.
        ts.AddSource(source);
        TileSet = ts;

        // Full-tile collision polygon so the entire tile blocks movement.
        float h = TileSize / 2f;
        var tileData = source.GetTileData(Vector2I.Zero, 0);
        tileData.AddCollisionPolygon(0);
        tileData.SetCollisionPolygonPoints(0, 0, new Vector2[]
        {
            new Vector2(-h, -h),
            new Vector2( h, -h),
            new Vector2( h,  h),
            new Vector2(-h,  h),
        });
    }

    private void GenerateMaze()
    {
        // Fill every tile position as a wall to start.
        for (int tx = 0; tx < 2 * MazeCols + 1; tx++)
            for (int ty = 0; ty < 2 * MazeRows + 1; ty++)
                SetCell(new Vector2I(tx, ty), 0, Vector2I.Zero);

        // Carve passages outward from a random starting room.
        CarveFrom(_rng.RandiRange(0, MazeCols - 1), _rng.RandiRange(0, MazeRows - 1));
    }

    // Recursive backtracker: visits every room exactly once, removing the wall
    // tile between each pair of connected rooms to open a passage.
    private void CarveFrom(int col, int row)
    {
        _visited[col, row] = true;
        EraseCell(new Vector2I(col * 2 + 1, row * 2 + 1)); // open this room

        // Randomise direction order so the maze topology is different every run.
        var dirs = new List<(int dc, int dr)> { (1, 0), (-1, 0), (0, 1), (0, -1) };
        for (int i = dirs.Count - 1; i > 0; i--)
        {
            int j = _rng.RandiRange(0, i);
            (dirs[i], dirs[j]) = (dirs[j], dirs[i]);
        }

        foreach ((int dc, int dr) in dirs)
        {
            int nc = col + dc;
            int nr = row + dr;
            if (nc < 0 || nc >= MazeCols || nr < 0 || nr >= MazeRows || _visited[nc, nr])
                continue;

            // Erase the wall tile that separates the two rooms.
            EraseCell(new Vector2I(col * 2 + 1 + dc, row * 2 + 1 + dr));
            CarveFrom(nc, nr);
        }
    }

    private void CenterInArena()
    {
        float mazeW = (2 * MazeCols + 1) * TileSize; // 608
        float mazeH = (2 * MazeRows + 1) * TileSize; // 352
        Position = new Vector2(
            Mathf.Round((1152f - mazeW) / 2f), // 272 — leaves ~240 px on each side
            Mathf.Round((648f  - mazeH) / 2f)  // 148 — leaves ~116 px top and bottom
        );
    }
}
