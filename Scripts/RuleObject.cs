using Godot;

namespace BreakingRules;

public enum RuleMode
{
    NoJump,   // 禁跳区：区内按跳 = 违规
    NoAttack, // 禁武区：区内攻击 = 违规
    Slow,     // 限速区：区内移速大幅降低（软约束，非违规）
    Invert,   // 全图规则：左右反转（按 A 往右、按 D 往左）
    Spring    // 反转后：超级弹簧，接触即弹飞
}

/// <summary>
/// 空中飘着的「规则条文」实体。Boss 生成，类型由 RuleManager/Boss 指定。
/// 含：飘动白条黑字 Band、规则区域 Zone、被划除后变超级弹簧。
/// 碰撞检测由 Player 通过 Contains() 手动查询（无需配置碰撞层）。
/// 支持：全图规则(IsGlobal)、跟随规则(Follow)、动态区域尺寸(ZoneSize)。
/// </summary>
[GlobalClass]
public partial class RuleObject : Node2D
{
    private const int BandW = 240;
    private const int BandH = 48;
    private const float FollowDuration = 12f;   // 跟随规则的跟随时长（秒），到点后区域消失
    private const float GlobalDuration = 10f;  // 全图规则的持续时长（秒），到点后规则整体消失

    [Export] public float StrikeRange { get; set; } = 120f;
    [Export] public Vector2 ZoneSize { get; set; } = new Vector2(160f, 90f);
    [Export] public float SpringVelocity { get; set; } = -700f;
    [Export] public RuleMode Mode { get; set; } = RuleMode.NoJump;
    [Export] public int RuleIndex { get; set; } = 1;
    [Export] public bool IsGlobal { get; set; }   // 全图规则：约束全图生效，但地图上的消除区域仍局部
    [Export] public bool Follow { get; set; }     // 跟随规则：区域跟随玩家（约 1s 延迟），描述加【跟随】

    private Node2D _bandRoot;
    private Sprite2D _band;
    private Sprite2D _zoneVisual;
    private Label _countdown;       // 跟随规则的区域倒计时
    private Label _globalCountdown; // 全图规则的剩余时长倒计时
    private Vector2 _zoneOffset;
    private Tween _bobTween;        // 飘动 tween（无限循环）；划除后须先 Kill 再释放目标节点
    private float _followRemaining; // 跟随剩余时长
    private float _globalRemaining; // 全图剩余时长

    public override void _Ready()
    {
        _bandRoot = new Node2D();
        AddChild(_bandRoot);

        _band = new Sprite2D();
        _band.Texture = Util.Square(BandW, BandH, Colors.White);
        _bandRoot.AddChild(_band);

        var (text, zoneColor) = Describe(Mode, RuleIndex, IsGlobal, Follow);
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

        // 跟随规则：区域内显示倒计时（到 0 后区域定格，不再跟随）
        if (Follow)
        {
            _followRemaining = FollowDuration;
            _countdown = new Label();
            _countdown.AddThemeFontSizeOverride("font_size", 16);
            _countdown.HorizontalAlignment = HorizontalAlignment.Center;
            _countdown.Size = new Vector2(60f, 22f);
            _countdown.Position = _zoneOffset + new Vector2(-30f, -14f);
            _countdown.Modulate = Colors.White;
            _countdown.Text = $"{Mathf.Ceil(_followRemaining)}";
            AddChild(_countdown);
        }

        // 全图规则：条文顶部显示剩余时长倒计时（最多 GlobalDuration 秒后规则整体消失）
        if (IsGlobal)
        {
            _globalRemaining = GlobalDuration;
            _globalCountdown = new Label();
            _globalCountdown.AddThemeFontSizeOverride("font_size", 18);
            _globalCountdown.HorizontalAlignment = HorizontalAlignment.Center;
            _globalCountdown.Size = new Vector2(80f, 24f);
            _globalCountdown.Position = new Vector2(-40f, -BandH / 2f - 26f);
            _globalCountdown.Modulate = new Color(1f, 0.85f, 0.3f);
            _globalCountdown.Text = $"{Mathf.Ceil(_globalRemaining)}";
            AddChild(_globalCountdown);
        }

        // 上下飘动（无限循环）。注意：本节点被划除时会释放 _bandRoot，
        // 必须在释放前 Kill 此 tween，否则无限循环 tween 的目标失效后
        // total_time==0 + loops==-1 触发 Godot "Infinite loop detected"。
        _bobTween = CreateTween();
        _bobTween.SetLoops(-1);
        _bobTween.TweenProperty(_bandRoot, "position:y", 8f, 1.1f).From(-8f);
        _bobTween.TweenProperty(_bandRoot, "position:y", -8f, 1.1f);
    }

    public override void _PhysicsProcess(double delta)
    {
        // 全图规则倒计时：到点后规则整体（全图效果 + 局部消除区）一起消失
        if (IsGlobal && _globalRemaining > 0f)
        {
            _globalRemaining -= (float)delta;
            if (_globalCountdown != null)
                _globalCountdown.Text = $"{Mathf.Ceil(Mathf.Max(0f, _globalRemaining))}";
            if (_globalRemaining <= 0f)
            {
                QueueFree();
                return;
            }
        }

        // 跟随规则：区域以约 1 秒延迟平滑追向玩家；倒计时归零后定格（不再跟随）。
        if (Follow && _followRemaining > 0f)
        {
            var p = GetTree().GetFirstNodeInGroup("player") as Player;
            if (p != null)
            {
                float k = 1f - Mathf.Exp(-(float)delta * 1.0f); // 时间常数 ~1s
                GlobalPosition = GlobalPosition.Lerp(p.GlobalPosition + new Vector2(0f, -24f), k);
            }
            _followRemaining -= (float)delta;
            if (_countdown != null) _countdown.Text = $"{Mathf.Ceil(Mathf.Max(0f, _followRemaining))}";
            // 倒计时归零：跟随规则整体消失（与全图规则分支一致），不再定格残留
            if (_followRemaining <= 0f)
            {
                QueueFree();
                return;
            }
        }
    }

    /// <summary>按规则类型给出条文文本与区域颜色（半透明）。全图加【全图】前缀，跟随加【跟随】后缀。</summary>
    private static (string text, Color zone) Describe(RuleMode mode, int idx, bool global, bool follow)
    {
        string eff = mode switch
        {
            RuleMode.NoJump   => "禁止起跳",
            RuleMode.NoAttack => "禁止攻击",
            RuleMode.Slow     => "限速行进",
            RuleMode.Invert   => "左右反转",
            _                 => "限制",
        };
        string suffix = follow ? "【跟随】" : "";
        string label = global
            ? $"【全图】第 {idx} 条：{eff}{suffix}"
            : $"第 {idx} 条：此区{eff}{suffix}";
        Color zone = mode switch
        {
            RuleMode.NoJump   => new Color(1f, 0.2f, 0.2f, 0.35f),
            RuleMode.NoAttack => new Color(1f, 0.6f, 0.2f, 0.35f),
            RuleMode.Slow     => new Color(0.3f, 0.6f, 1f, 0.30f),
            RuleMode.Invert   => new Color(0.9f, 0.3f, 0.9f, 0.35f),
            _                 => new Color(1f, 1f, 1f, 0.30f),
        };
        return (label, zone);
    }

    /// <summary>玩家是否处在规则区域内（世界坐标）。全图规则恒为 true。</summary>
    public bool Contains(Vector2 worldPos)
    {
        if (IsGlobal) return true;
        Vector2 center = GlobalPosition + _zoneOffset;
        return Mathf.Abs(worldPos.X - center.X) <= ZoneSize.X / 2f &&
               Mathf.Abs(worldPos.Y - center.Y) <= ZoneSize.Y / 2f;
    }

    /// <summary>玩家是否够近、可长按 Q 划除此条文（消除区局部，无论是否全图）。</summary>
    public bool NearBand(Vector2 worldPos) => GlobalPosition.DistanceTo(worldPos) <= StrikeRange;

    /// <summary>被划除：反转成超级弹簧，白条淡出，保留弹簧区 8 秒后自毁。</summary>
    public void ApplyStrike()
    {
        // 先停掉无限飘动 tween，避免其目标 _bandRoot 被释放后触发 "Infinite loop detected"
        _bobTween?.Kill();
        _bobTween = null;
        Follow = false;    // 消除后停止跟随
        IsGlobal = false;  // 消除后不再全图生效（变为局部弹簧）
        if (_countdown != null) _countdown.Visible = false;
        if (_globalCountdown != null) _globalCountdown.Visible = false;

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
