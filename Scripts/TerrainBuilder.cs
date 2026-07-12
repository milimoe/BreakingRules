using Godot;
using System;

namespace BreakingRules;

/// <summary>
/// 运行时构建 TileSet + TileMapLayer，仅用于「像素美术视觉层」美化地形。
///
/// 设计决策（重要）：
/// - 碰撞体保留 Main.tscn 中原有的 6 个 StaticBody2D（Ground / PlatformA~E），
///   它们已被验证可正常站立、可跳跃。美术升级时不应删除它们。
/// - TileMap 只负责好看，不烘焙碰撞多边形，避免与地面 StaticBody2D 形成
///   双重碰撞 / 边缘卡顿。两者几何对齐（平台/地面顶面对齐 16px 网格）。
/// - 所有瓦片引用集中在文件顶部常量；换美术只改一行（配合 res:// 路径）。
///   瓦片实际配色已通过离线解码确认：tile_0023=亮绿草顶，tile_0108=棕土填充，tile_0205=红砖平台。
/// </summary>
public static class TerrainBuilder
{
    private const int TILE = 16;

    // ---- 地形象素素材（res://Assets/PNG/Tiles，16px）----
    private const string PathGrassTop = "res://Assets/PNG/Tiles/Tiles/tile_0023.png"; // 亮绿草顶
    private const string PathDirtFill = "res://Assets/PNG/Tiles/Tiles/tile_0108.png"; // 棕土填充
    private const string PathPlatform = "res://Assets/PNG/Tiles/Tiles/tile_0205.png"; // 红砖平台

    // ---- 地面：整屏宽 60 格，顶行 row 31（y=496），草顶 + 2 行土 ----
    // 对齐到原 StaticBody2D Ground（中心 520，高 40 -> 顶 y=500）。row31 顶 y=496，
    // 玩家落脚在 y≈500 处，视觉上正好踩在草皮上（略嵌入 4px，无碍）。
    private const int GroundTopRow = 31;
    private const int GroundCols = 60;   // 0..59 覆盖 0..960
    private const int GroundDepth = 3;   // 行数（含草顶）

    // ---- 平台（瓦片坐标，col/row 为平台左上；宽 10 格=160px，高 1 格）----
    // 对齐到 16px 网格，贴近原 StaticBody2D 平台位置（±4px 内，顶面一致）。
    private static readonly (int col, int row)[] Platforms =
    {
        (6, 25),   // 原 PlatformA (180,410) -> 顶 y≈400
        (18, 20),  // 原 PlatformB (380,330) -> 顶 y≈320
        (31, 25),  // 原 PlatformC (580,410) -> 顶 y≈400
        (42, 20),  // 原 PlatformD (760,330) -> 顶 y≈320
        (25, 15),  // 原 PlatformE (480,250) -> 顶 y≈240
    };
    private const int PlatWidth = 10;
    private const int PlatHeight = 1;

    public static void Build(Node2D host)
    {
        if (host == null) return;

        // host 是 LevelDirector 自身；真正的 World（含 Ground/Platform* 碰撞体）是其父节点。
        var world = host.GetParent() as Node2D;
        if (world != null)
            HideOldPlaceholderSprites(world);

        // 平台选择：常规 BOSS 关用固定布局；无尽模式（CurrentStage>=名册数）随机生成。
        bool endless = IsEndless();
        (int col, int row)[] plats = endless ? GenerateRandomPlatforms() : Platforms;

        var ts = new TileSet();
        ts.TileSize = new Vector2I(TILE, TILE);

        int grassSrc = AddTileSource(ts, PathGrassTop);
        int dirtSrc = AddTileSource(ts, PathDirtFill);
        int platSrc = AddTileSource(ts, PathPlatform);

        var tileMap = new TileMapLayer();
        tileMap.Name = "Terrain";
        tileMap.TileSet = ts;
        tileMap.ZIndex = -10; // 置于玩家/敌人/道具之下，作为背景地形

        // 关键修复：LevelDirector._Ready 期间其父节点(World)仍在 setup 子节点，
        // 对其同步 AddChild 会被 Godot 拒绝("Parent node is busy setting up children")，
        // 导致整个 TileMap 未加入场景（表现为「看不到平台」）。
        // 改为加到 LevelDirector 自身——_Ready 中给"自己"加子节点是允许的
        // （BuildOverlay 就是这么干的），从而彻底绕开父节点 busy 的限制，无需延迟。
        host.AddChild(tileMap);

        // 地面（纯视觉，固定）
        for (int c = 0; c < GroundCols; c++)
        {
            tileMap.SetCell(new Vector2I(c, GroundTopRow), grassSrc, Vector2I.Zero);
            for (int r = 1; r < GroundDepth; r++)
                tileMap.SetCell(new Vector2I(c, GroundTopRow + r), dirtSrc, Vector2I.Zero);
        }
        // 平台（纯视觉，单格厚）
        foreach (var p in plats)
            for (int r = 0; r < PlatHeight; r++)
                for (int c = 0; c < PlatWidth; c++)
                    tileMap.SetCell(new Vector2I(p.col + c, p.row + r), platSrc, Vector2I.Zero);

        // 无尽模式：Main.tscn 里 PlatformA~E 的碰撞体原本是固定位置，这里把它们
        // 移动到随机平台处，让「碰撞」与「视觉」同步随机化。
        // 常规关则保持 Main.tscn 原位置不变（碰撞体原样保留即可）。
        if (endless && world != null)
            RepositionPlatformColliders(world, plats);
    }

    /// <summary>是否处于无尽模式：当前关索引已超过固定 BOSS 名册数量。</summary>
    private static bool IsEndless() =>
        RunState.Instance != null && RunState.Instance.CurrentStage >= BossRoster.Count;

    /// <summary>
    /// 无尽模式：在合理范围内随机生成 5 个平台。沿用常规布局的「两低两中一高」节奏，
    /// 仅对每档的 x 与高度做随机抖动，保证可站立、可跳到、且 10 格宽不越界。
    /// </summary>
    private static (int col, int row)[] GenerateRandomPlatforms()
    {
        var rng = new Random();
        int[] rows = { 24, 19, 24, 19, 14 }; // 两低(A/C)、两中(B/D)、一高(E)，对应常规布局
        var result = new (int col, int row)[Platforms.Length];
        for (int i = 0; i < result.Length; i++)
        {
            int row = rows[i] + rng.Next(-2, 3);   // 各档高度抖动 -2..+2 行
            row = Mathf.Clamp(row, 13, 29);        // 限制在可落地/可跳跃到的高度区间
            int col = rng.Next(2, 49);             // 左列：保证 10 格宽不越界(col+9<=58<60)
            result[i] = (col, row);
        }
        return result;
    }

    /// <summary>把 Main.tscn 里 PlatformA~E 的 StaticBody2D 碰撞体移动到随机平台位置（碰撞同步视觉）。</summary>
    private static void RepositionPlatformColliders(Node2D world, (int col, int row)[] plats)
    {
        for (int i = 0; i < plats.Length && i < 5; i++)
        {
            var body = world.GetNodeOrNull<StaticBody2D>($"Platform{(char)('A' + i)}");
            if (body == null) continue;
            // 与常规布局的碰撞体对齐方式一致：中心 = (col*16+80+4, row*16+11)
            body.Position = new Vector2(plats[i].col * 16 + 84, plats[i].row * 16 + 11);
        }
    }

    /// <summary>把 Main.tscn 中旧 StaticBody2D 的占位 Sprite 隐藏（它们本无贴图，纯防御）。</summary>
    private static void HideOldPlaceholderSprites(Node2D world)
    {
        string[] names = { "Ground", "PlatformA", "PlatformB", "PlatformC", "PlatformD", "PlatformE" };
        foreach (var n in names)
        {
            var node = world.GetNodeOrNull<Node2D>(n);
            if (node == null) continue;
            var sprite = node.GetNodeOrNull<Sprite2D>("Sprite");
            if (sprite != null) sprite.Visible = false;
        }
    }

    /// <summary>为单个 16x16 PNG 创建图集源（仅视觉，不烘焙碰撞）。返回 source id 供 SetCell 使用。</summary>
    private static int AddTileSource(TileSet ts, string path)
    {
        var src = new TileSetAtlasSource();
        src.Texture = GD.Load<Texture2D>(path);
        src.TextureRegionSize = new Vector2I(TILE, TILE);
        int id = ts.AddSource(src);
        src.CreateTile(Vector2I.Zero);
        return id;
    }
}
