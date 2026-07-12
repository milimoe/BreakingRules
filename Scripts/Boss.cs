using Godot;

namespace BreakingRules;

/// <summary>
/// Boss「初审官」：单阶段，满血。定时生成禁跳区条文 + 平A + 冲锋。
/// 胜利 = 打空其血量；失败 = 玩家血 0。
/// </summary>
[GlobalClass]
public partial class Boss : CharacterBody2D
{
    [Export] public int MaxHp { get; set; } = 24;
    [Export] public float SpawnInterval { get; set; } = 10f;
    [Export] public float ChargeInterval { get; set; } = 6f;
    [Export] public float ChargeSpeed { get; set; } = 620f;
    [Export] public float ChargeDuration { get; set; } = 0.4f;
    [Export] public float MeleeDamage { get; set; } = 1f;
    [Export] public float MeleeCooldown { get; set; } = 1.2f;
    [Export] public float HoverSpeed { get; set; } = 40f;
    [Export] public string BossName { get; set; } = "初审官";
    [Export] public string TexturePath { get; set; } = "res://Assets/PNG/Enemies/Tiles/tile_0000.png";
    [Export] public Color Tint { get; set; } = Colors.White;

    [Signal] public delegate void HealthChangedEventHandler(int hp, int maxHp);
    [Signal] public delegate void DiedEventHandler();

    private Sprite2D _sprite;
    private Player _target;
    private Color _baseTint = Colors.White;
    private int _hp;
    private float _spawnTimer;
    private float _chargeTimer;
    private float _chargeLeft;
    private float _meleeCd;
    private Vector2 _chargeDir;
    private bool _touchingPlayer;   // 玩家本体是否正贴着 Boss（接触伤害区）

    // 候选规则类型（随机抽一条）
    private static readonly RuleMode[] RuleTypes =
    {
        RuleMode.NoJump, RuleMode.NoAttack, RuleMode.NoStrike, RuleMode.Slow
    };
    // 可站立表面（top=碰撞顶面 Y，xMin/xMax=该表面可落点的 x 范围）。
    // 规则锚定在表面上、随机选一个表面 + 随机 x，从而散布到竞技场各处。
    private static readonly (float top, float xMin, float xMax)[] Surfaces =
    {
        (500f,  30f, 930f),  // 地面
        (399f, 110f, 250f),  // PlatformA
        (319f, 310f, 450f),  // PlatformB
        (399f, 510f, 650f),  // PlatformC
        (319f, 690f, 830f),  // PlatformD
        (239f, 410f, 550f),  // PlatformE
    };

    public int Hp => _hp;

    public override void _Ready()
    {
        _hp = MaxHp;
        _sprite = new Sprite2D();
        _sprite.Texture = GD.Load<Texture2D>(TexturePath);
        _sprite.Scale = new Vector2(2f, 2f);
        _baseTint = Tint;
        _sprite.Modulate = _baseTint;
        AddChild(_sprite);
        AddToGroup("boss");

        // 接触伤害区：仅当玩家本体真正碰到 Boss 时才结算（不再用距离判定）。
        var touch = new Area2D();
        touch.Name = "TouchZone";
        var tz = new CollisionShape2D();
        tz.Shape = new RectangleShape2D { Size = new Vector2(50f, 50f) };
        touch.AddChild(tz);
        AddChild(touch);
        touch.BodyEntered += OnTouchEntered;
        touch.BodyExited += OnTouchExited;
        _target = GetTree().GetFirstNodeInGroup("player") as Player;
        _spawnTimer = 5f;
        _chargeTimer = ChargeInterval;
        EmitSignal(SignalName.HealthChanged, _hp, MaxHp);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_target == null || !IsInstanceValid(_target))
            _target = GetTree().GetFirstNodeInGroup("player") as Player;
        if (_target == null) return;

        float d = (float)delta;

        // 生成条文：随机类型 + 随机表面 + 随机 x → 散布到竞技场各处
        _spawnTimer -= d;
        if (_spawnTimer <= 0f)
        {
            _spawnTimer = SpawnInterval;
            var mode = RuleTypes[(int)GD.RandRange(0f, RuleTypes.Length)];
            var s = Surfaces[(int)GD.RandRange(0f, Surfaces.Length)];
            float x = (float)GD.RandRange(s.xMin, s.xMax);
            float bandY = s.top - 52f; // 条文飘在表面上方，区域正好罩住站立点
            Shout(ShoutFor(mode));
            RuleManager.Instance?.SpawnRule(mode, new Vector2(x, bandY));
        }

        // 冲锋
        _chargeTimer -= d;
        if (_chargeLeft > 0f)
        {
            _chargeLeft -= d;
            Velocity = _chargeDir * ChargeSpeed;
        }
        else
        {
            if (_chargeTimer <= 0f)
            {
                _chargeTimer = ChargeInterval;
                _chargeLeft = ChargeDuration;
                _chargeDir = (_target.GlobalPosition - GlobalPosition).Normalized();
            }
            else
            {
                // 平时缓慢靠近玩家
                Vector2 toPlayer = (_target.GlobalPosition - GlobalPosition).Normalized();
                Velocity = toPlayer * HoverSpeed;
            }

            // 接触伤害：仅当玩家本体正贴着 Boss 时才结算
            _meleeCd -= d;
            if (_meleeCd <= 0f && _touchingPlayer)
            {
                _meleeCd = MeleeCooldown;
                _target.TakeDamage((int)MeleeDamage);
            }
        }

        MoveAndSlide();
    }

    private void OnTouchEntered(Node body)
    {
        if (body is Player) _touchingPlayer = true;
    }

    private void OnTouchExited(Node body)
    {
        if (body is Player) _touchingPlayer = false;
    }

    public void TakeDamage(int amount)
    {
        if (_hp <= 0) return;
        _hp -= amount;
        EmitSignal(SignalName.HealthChanged, _hp, MaxHp);
        // 受击闪烁（闪白后回到本 BOSS 的染色）
        var t = CreateTween();
        t.TweenProperty(_sprite, "modulate", Colors.White, 0.06f);
        t.TweenProperty(_sprite, "modulate", _baseTint, 0.06f);
        RuleManager.Instance?.PlaySFX("boss_hit");
        if (_hp <= 0)
            EmitSignal(SignalName.Died);
    }

    private static string ShoutFor(RuleMode m) => m switch
    {
        RuleMode.NoJump   => "禁跳！",
        RuleMode.NoAttack => "禁武！",
        RuleMode.NoStrike => "禁改！",
        RuleMode.Slow     => "限速！",
        _                 => "条文！",
    };

    private void Shout(string text)
    {
        var lbl = new Label();
        lbl.Text = text;
        lbl.AddThemeFontSizeOverride("font_size", 22);
        lbl.HorizontalAlignment = HorizontalAlignment.Center;
        lbl.Size = new Vector2(120f, 30f);
        lbl.Position = new Vector2(-60f, -60f);
        lbl.Modulate = Colors.Yellow;
        AddChild(lbl);
        var t = CreateTween();
        t.TweenProperty(lbl, "position:y", -90f, 0.6f);
        t.Parallel().TweenProperty(lbl, "modulate:a", 0f, 0.6f);
        t.TweenCallback(Callable.From(() => lbl.QueueFree()));
    }
}
