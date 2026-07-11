using Godot;
using System.Linq;

namespace BreakingRules;

[GlobalClass]
public partial class Player : CharacterBody2D
{
    [Export] public float Speed { get; set; } = 260f;
    [Export] public float JumpVelocity { get; set; } = -600f;
    [Export] public float Gravity { get; set; } = 1100f;
    [Export] public int MaxHp { get; set; } = 5;
    [Export] public int Ink { get; set; } = 100;          // 当前墨量（同时是上限）
    [Export] public float InkRegen { get; set; } = 10f;   // 每秒回复（真空期×2）
    [Export] public int StrikeCost { get; set; } = 30;    // 划除一条条文消耗
    [Export] public float AttackPower { get; set; } = 1f;
    [Export] public float AttackCooldown { get; set; } = 0.32f;

    [Signal] public delegate void HealthChangedEventHandler(int hp, int maxHp);
    [Signal] public delegate void InkChangedEventHandler(int ink, int maxInk);
    [Signal] public delegate void DiedEventHandler();

    // 角色基准缩放（开局即小；攻击挥动也以此为准，避免挥砍后角色被缩水）
    private const float BaseScale = 1.5f;

    // 技能系统
    private float _cd1, _cd2;
    private const float Cd1 = 10f;   // 技能1（毁灭直线）冷却
    private const float Cd2 = 8f;    // 技能2（八向射线）冷却
    private const float Skill1Damage = 5f;
    private const float Skill2Damage = 4f;
    private const float Skill1Band = 40f;    // 直线命中：与 Boss 同平面的纵向容差
    private const float Skill2Range = 320f;  // 射线命中：以角色为中心的半径
    public int SkillPoints { get; private set; }
    public const int SkillCount = 2;

    private Sprite2D _sprite;
    private Camera2D _camera;
    private int _hp;
    private float _ink;
    private float _invuln;
    private float _attackCd;
    private int _lastInkShown = -1;

    // 地图拾取物增益（临时）
    private float _speedBuff = 1f;   // 速度倍率
    private float _speedBuffT;       // 剩余时长
    private float _jumpBuff = 1f;    // 跳跃倍率
    private float _jumpBuffT;

    public int Hp => _hp;

    public override void _Ready()
    {
        _hp = MaxHp;
        _ink = Ink;
        _sprite = GetNode<Sprite2D>("Sprite");
        _sprite.Texture = GD.Load<Texture2D>("res://Assets/PNG/Players/Tiles/tile_0000.png");
        _sprite.Scale = new Vector2(BaseScale, BaseScale);
        _camera = GetNode<Camera2D>("Camera2D");
        // 单屏竞技场：把相机限位框设为整个 960x540 场地。
        // 视口尺寸正好等于限位框 -> 相机被锁死、静止不滚动。
        // 注意：不能把左右/上下都设成同一个值（0 宽），那样 Godot 无法满足
        // 会退化为「跟随玩家」，方块跑出屏幕后相机也跟着跑、就回不来了。
        _camera.LimitLeft = 0;
        _camera.LimitTop = 0;
        _camera.LimitRight = 960;
        _camera.LimitBottom = 540;
        AddToGroup("player");
    }

    public override void _PhysicsProcess(double delta)
    {
        float d = (float)delta;

        // 增益倒计时
        if (_speedBuffT > 0f) { _speedBuffT -= d; if (_speedBuffT <= 0f) _speedBuff = 1f; }
        if (_jumpBuffT > 0f)  { _jumpBuffT  -= d; if (_jumpBuffT  <= 0f) _jumpBuff  = 1f; }

        // 受击无敌帧闪烁（用 alpha 明暗，任何底色都看得清）
        if (_invuln > 0)
        {
            _invuln -= d;
            float a = (_invuln % 0.16f < 0.08f) ? 1f : 0.25f;
            _sprite.Modulate = new Color(1f, 1f, 1f, a);
            if (_invuln <= 0f) _sprite.Modulate = Colors.White;
        }

        // 重力
        if (!IsOnFloor())
            Velocity = new Vector2(Velocity.X, Velocity.Y + Gravity * d);

        // 水平移动（真空期×2，叠加拾取加速；限速区内移速大幅降低）
        float dir = Input.GetAxis("move_left", "move_right");
        float spd = Speed * (RuleManager.Instance != null ? RuleManager.Instance.SpeedMult : 1f) * _speedBuff;
        if (RuleManager.Instance != null)
        {
            foreach (var r in RuleManager.Instance.ActiveRules)
                if (IsInstanceValid(r) && r.Mode == RuleMode.Slow && r.Contains(GlobalPosition))
                { spd *= 0.4f; break; }
        }
        Velocity = new Vector2(dir * spd, Velocity.Y);

        // 弹簧区：接触即弹飞
        if (RuleManager.Instance != null)
        {
            foreach (var r in RuleManager.Instance.ActiveRules)
            {
                if (!IsInstanceValid(r)) continue;
                if (r.Mode == RuleMode.Spring && r.Contains(GlobalPosition) && Velocity.Y > -50f)
                    Velocity = new Vector2(Velocity.X, r.SpringVelocity);
            }
        }

        // 跳跃 / 违规判定
        if (Input.IsActionJustPressed("jump"))
        {
            bool inNoJump = RuleManager.Instance != null &&
                RuleManager.Instance.ActiveRules.Any(r => IsInstanceValid(r) &&
                    r.Mode == RuleMode.NoJump && r.Contains(GlobalPosition));
            if (inNoJump)
            {
                Velocity = new Vector2(Velocity.X, 0); // 取消起跳
                if (_invuln <= 0) TakeDamage(1);
                ShowPopup("违规！", Colors.Red);
                Shake();
                RuleManager.Instance?.PlaySFX("violation");
            }
            else if (IsOnFloor())
            {
                Velocity = new Vector2(Velocity.X, JumpVelocity * _jumpBuff);
            }
        }

        // 普攻（禁武区内攻击 = 违规）
        _attackCd -= d;
        if (Input.IsActionPressed("attack") && _attackCd <= 0f)
        {
            _attackCd = AttackCooldown;
            bool inNoAttack = RuleManager.Instance != null &&
                RuleManager.Instance.ActiveRules.Any(r => IsInstanceValid(r) &&
                    r.Mode == RuleMode.NoAttack && r.Contains(GlobalPosition));
            if (inNoAttack)
            {
                if (_invuln <= 0) TakeDamage(1);
                ShowPopup("违规！", Colors.Red);
                Shake();
                RuleManager.Instance?.PlaySFX("violation");
            }
            else
            {
                Swing();
            }
        }

        // 划除条文（Q）
        if (Input.IsActionJustPressed("strike"))
            TryStrike();

        // 技能（数字键 1 / 2）：CD 制。空格同时触发跳+攻击，故技能独立键避免误触。
        _cd1 = Mathf.Max(0f, _cd1 - d);
        _cd2 = Mathf.Max(0f, _cd2 - d);
        if (Input.IsActionJustPressed("skill1") && _cd1 <= 0f) CastSkill1();
        if (Input.IsActionJustPressed("skill2") && _cd2 <= 0f) CastSkill2();

        MoveAndSlide();

        // 竞技场硬边界（单屏静止相机）：任何穿地 / 越界 / 被弹簧弹飞都必须被拦在
        // 960x540 内，否则方块掉出世界后就再也回不来了（之前“Player没了”的根因）。
        // 地面正好等于屏幕宽，两侧无墙，走到边缘外会踏空坠落——这里用钳制兜底。
        const float half = 16f;
        Position = new Vector2(
            Mathf.Clamp(Position.X, half, 960f - half),
            Mathf.Clamp(Position.Y, 0f, 504f)
        );

        // 墨回复（真空期×2）
        if (_ink < Ink)
        {
            float regen = InkRegen * (RuleManager.Instance != null ? RuleManager.Instance.InkRegenMult : 1f);
            _ink = Mathf.Min(Ink, _ink + regen * d);
            EmitInkIfChanged();
        }
    }

    private void Swing()
    {
        var tween = CreateTween();
        tween.TweenProperty(_sprite, "scale", Vector2.One * BaseScale * 1.4f, 0.08f).From(Vector2.One * BaseScale);
        tween.TweenProperty(_sprite, "scale", Vector2.One * BaseScale, 0.08f);

        float dmg = AttackPower * (RuleManager.Instance != null ? RuleManager.Instance.AttackMult : 1f);
        foreach (Node n in GetTree().GetNodesInGroup("boss"))
            if (n is Boss b && GlobalPosition.DistanceTo(b.GlobalPosition) < 84f)
                b.TakeDamage((int)Mathf.Round(dmg));
    }

    private void TryStrike()
    {
        if (RuleManager.Instance == null) return;
        if (_ink < StrikeCost) { ShowPopup("墨不足", Colors.Gray); return; }

        // 禁改区：区内按 Q 划除 = 违规（可从区外边缘划掉该区本身）
        bool inNoStrike = RuleManager.Instance.ActiveRules.Any(r => IsInstanceValid(r) &&
            r.Mode == RuleMode.NoStrike && r.Contains(GlobalPosition));
        if (inNoStrike)
        {
            if (_invuln <= 0) TakeDamage(1);
            ShowPopup("违规！", Colors.Red);
            Shake();
            RuleManager.Instance?.PlaySFX("violation");
            return;
        }

        // 划除任意非 Spring 条文（不限禁跳区），最近且进入划除范围者优先
        RuleObject nearest = null;
        float best = float.MaxValue;
        foreach (var r in RuleManager.Instance.ActiveRules)
        {
            if (!IsInstanceValid(r) || r.Mode == RuleMode.Spring) continue;
            if (!r.NearBand(GlobalPosition)) continue;
            float dist = GlobalPosition.DistanceTo(r.GlobalPosition);
            if (dist < best) { best = dist; nearest = r; }
        }
        if (nearest == null) return;

        _ink -= StrikeCost;
        EmitInkIfChanged();
        // 红色划痕瞬间
        var slash = CreateTween();
        slash.TweenProperty(_sprite, "modulate", Colors.Red, 0.06f);
        slash.TweenProperty(_sprite, "modulate", Colors.White, 0.12f);
        Shake();
        RuleManager.Instance.PlaySFX("paper_tear");
        RuleManager.Instance.StrikeRule(nearest);
    }

    // ---------- 技能系统 ----------
    public bool IsSkillReady(int i) => i == 1 ? _cd1 <= 0f : _cd2 <= 0f;
    public float SkillCd(int i) => i == 1 ? _cd1 : _cd2;
    public float SkillCdMax(int i) => i == 1 ? Cd1 : Cd2;

    /// <summary>技能1：在角色所在水平面释放一条贯穿全屏的毁灭直线，命中同平面 Boss。</summary>
    private void CastSkill1()
    {
        _cd1 = Cd1;
        float py = GlobalPosition.Y;
        SpawnBeam(new Vector2[] { new Vector2(0f, py), new Vector2(960f, py) },
                  new Color(1f, 0.35f, 0.3f), 0.35f);
        var boss = GetTree().GetFirstNodeInGroup("boss") as Boss;
        if (boss != null && IsInstanceValid(boss) && Mathf.Abs(boss.GlobalPosition.Y - py) < Skill1Band)
            boss.TakeDamage((int)Mathf.Round(Skill1Damage * (RuleManager.Instance != null ? RuleManager.Instance.AttackMult : 1f)));
        RuleManager.Instance?.PlaySFX("vacuum_start");
        Shake();
    }

    /// <summary>技能2：以角色为中心向 8 个方向释放射线，命中范围内 Boss。</summary>
    private void CastSkill2()
    {
        _cd2 = Cd2;
        Vector2 c = GlobalPosition;
        var pts = new System.Collections.Generic.List<Vector2> { c };
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.Pi / 4f;
            Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            pts.Add(c);
            pts.Add(c + dir * Skill2Range);
        }
        SpawnBeam(pts.ToArray(), new Color(0.5f, 0.9f, 1f), 0.35f);
        var boss = GetTree().GetFirstNodeInGroup("boss") as Boss;
        if (boss != null && IsInstanceValid(boss) && c.DistanceTo(boss.GlobalPosition) < Skill2Range)
            boss.TakeDamage((int)Mathf.Round(Skill2Damage * (RuleManager.Instance != null ? RuleManager.Instance.AttackMult : 1f)));
        RuleManager.Instance?.PlaySFX("vacuum_start");
        Shake();
    }

    /// <summary>在世界坐标下生成一条短暂存在的光束（Line2D，淡出后释放）。</summary>
    private void SpawnBeam(Vector2[] points, Color color, float life)
    {
        var line = new Line2D();
        line.Points = points;
        line.Width = 4f;
        line.DefaultColor = color;
        GetTree().CurrentScene?.AddChild(line);
        if (line.GetParent() == null) return;
        var t = CreateTween();
        t.TweenProperty(line, "modulate:a", 0f, life);
        t.TweenCallback(Callable.From(() => line.QueueFree()));
    }

    /// <summary>地图拾取物增益：加速 / 跳跃↑ / 回血。由 Pickup 调用。</summary>
    public void ApplyPickup(string kind)
    {
        switch (kind)
        {
            case "Speed":
                _speedBuff = 1.4f; _speedBuffT = 10f;
                ShowPopup("加速!", new Color(0.3f, 0.85f, 1f));
                break;
            case "Jump":
                _jumpBuff = 1.3f; _jumpBuffT = 10f;
                ShowPopup("跳跃↑", new Color(0.4f, 1f, 0.5f));
                break;
            default: // Heal
                if (_hp < MaxHp) _hp++;
                EmitSignal(SignalName.HealthChanged, _hp, MaxHp);
                ShowPopup("+1", new Color(1f, 0.45f, 0.55f));
                break;
        }
    }

    /// <summary>捡起技能宝珠后调用：技能点 +1（HUD 在生命值下方显示）。</summary>
    public void AddSkillPoint() => SkillPoints++;

    private void EmitInkIfChanged()
    {
        int v = (int)Mathf.Round(_ink);
        if (v != _lastInkShown)
        {
            _lastInkShown = v;
            EmitSignal(SignalName.InkChanged, v, Ink);
        }
    }

    public void TakeDamage(int amount)
    {
        if (_invuln > 0 || _hp <= 0) return;
        _hp -= amount;
        EmitSignal(SignalName.HealthChanged, _hp, MaxHp);
        if (_hp <= 0)
        {
            EmitSignal(SignalName.Died);
            return;
        }
        _invuln = 1.0f;
    }

    private void ShowPopup(string text, Color color)
    {
        var lbl = new Label();
        lbl.Text = text;
        lbl.AddThemeFontSizeOverride("font_size", 18);
        lbl.HorizontalAlignment = HorizontalAlignment.Center;
        lbl.Size = new Vector2(100f, 26f);
        lbl.Position = new Vector2(-50f, -40f);
        lbl.Modulate = color;
        AddChild(lbl);
        var t = CreateTween();
        t.TweenProperty(lbl, "position:y", -70f, 0.6f);
        t.Parallel().TweenProperty(lbl, "modulate:a", 0f, 0.6f);
        t.TweenCallback(Callable.From(() => lbl.QueueFree()));
    }

    private void Shake()
    {
        if (_camera == null) return;
        var t = CreateTween();
        t.TweenProperty(_camera, "offset", new Vector2(6f, 0f), 0.05f);
        t.TweenProperty(_camera, "offset", new Vector2(-6f, 0f), 0.05f);
        t.TweenProperty(_camera, "offset", Vector2.Zero, 0.05f);
    }
}
