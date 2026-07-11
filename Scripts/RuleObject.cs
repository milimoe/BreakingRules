using Godot;

namespace BreakingRules;

public enum RuleMode
{
    NoJump,   // 禁跳区：区内按跳 = 违规
    NoAttack, // 禁武区：区内攻击 = 违规
    NoStrike, // 禁改区：区内按 Q 划除 = 违规
    Slow,     // 限速区：区内移速大幅降低（软约束，非违规）
    Spring    // 反转后：超级弹簧，接触即弹飞
}

/// <summary>
/// 空中飘着的「规则条文」实体。Boss 生成，类型由 RuleManager/Boss 指定。
/// 含：飘动白条黑字 Band、规则区域 Zone、被划除后变超级弹簧。
/// 碰撞检测由 Player 通过 Contains() 手动查询（无需配置碰撞层）。
/// </summary>
[GlobalClass]
public partial class RuleObject : Node2D
{
    private const int BandW = 240;
    private const int BandH = 48;

    [Export] public float StrikeRange { get; set; } = 120f;
    [Export] public Vector2 ZoneSize { get; set; } = new Vector2(160f, 90f);
    [Export] public float SpringVelocity { get; set; } = -700f;
    [Export] public RuleMode Mode { get; set; } = RuleMode.NoJump;
    [Export] public int RuleIndex { get; set; } = 1;

    private Node2D _bandRoot;
    private Sprite2D _band;
    private Sprite2D _zoneVisual;
    private Vector2 _zoneOffset;

    public override void _Ready()
    {
        _bandRoot = new Node2D();
        AddChild(_bandRoot);

        _band = new Sprite2D();
        _band.Texture = Util.Square(BandW, BandH, Colors.White);
        _bandRoot.AddChild(_band);

        var (text, zoneColor) = Describe(Mode, RuleIndex);
        var label = new Label();
        label.Text = text;
        label.AddThemeFontSizeOverride("font_size", 18);
        label.HorizontalAlignment = HorizontalAlignment.Center;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.Size = new Vector2(BandW, BandH);
        label.Position = new Vector2(-BandW / 2f, -BandH / 2f);
        label.Modulate = Colors.Black;
        _bandRoot.AddChild(label);

        // 规则区域落在表面附近：条文飘在补丁正上方，玩家站在表面即可被约束。
        _zoneOffset = new Vector2(0f, 40f);
        _zoneVisual = new Sprite2D();
        _zoneVisual.Texture = Util.Square((int)ZoneSize.X, (int)ZoneSize.Y, Colors.White);
        _zoneVisual.Modulate = zoneColor;
        _zoneVisual.Position = _zoneOffset;
        AddChild(_zoneVisual);

        // 上下飘动
        var bob = CreateTween();
        bob.SetLoops(-1);
        bob.TweenProperty(_bandRoot, "position:y", 8f, 1.1f).From(-8f);
        bob.TweenProperty(_bandRoot, "position:y", -8f, 1.1f);
    }

    /// <summary>按规则类型给出条文文本与区域颜色（半透明）。</summary>
    private static (string text, Color zone) Describe(RuleMode mode, int idx)
    {
        return mode switch
        {
            RuleMode.NoJump   => ($"第 {idx} 条：此区禁止起跳", new Color(1f, 0.2f, 0.2f, 0.35f)),
            RuleMode.NoAttack => ($"第 {idx} 条：此区禁止攻击", new Color(1f, 0.6f, 0.2f, 0.35f)),
            RuleMode.NoStrike => ($"第 {idx} 条：此区禁止划除", new Color(0.7f, 0.3f, 1f, 0.35f)),
            RuleMode.Slow     => ($"第 {idx} 条：此区限速行进", new Color(0.3f, 0.6f, 1f, 0.30f)),
            _                 => ($"第 {idx} 条：规则", new Color(1f, 1f, 1f, 0.30f)),
        };
    }

    /// <summary>玩家是否处在规则区域内（世界坐标）。</summary>
    public bool Contains(Vector2 worldPos)
    {
        Vector2 center = GlobalPosition + _zoneOffset;
        return Mathf.Abs(worldPos.X - center.X) <= ZoneSize.X / 2f &&
               Mathf.Abs(worldPos.Y - center.Y) <= ZoneSize.Y / 2f;
    }

    /// <summary>玩家是否够近、可按 Q 划除此条文。</summary>
    public bool NearBand(Vector2 worldPos) => GlobalPosition.DistanceTo(worldPos) <= StrikeRange;

    /// <summary>被划除：反转成超级弹簧，白条淡出，保留弹簧区 8 秒后自毁。</summary>
    public void ApplyStrike()
    {
        Mode = RuleMode.Spring;
        _zoneVisual.Modulate = new Color(1f, 0.85f, 0.2f, 0.45f); // 黄

        if (_bandRoot != null)
        {
            var t = CreateTween();
            t.TweenProperty(_bandRoot, "modulate:a", 0f, 0.25f);
            t.TweenCallback(Callable.From(() => _bandRoot.QueueFree()));
        }

        // 弹簧区随真空期窗口（8s）结束后自毁
        var clear = GetTree().CreateTimer(8f);
        clear.Timeout += () => QueueFree();
    }
}
