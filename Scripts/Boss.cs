using Godot;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace BreakingRules;

/// <summary>
/// Boss：不再有接触伤害。改为 7 种主动攻击模组，每次攻击前都有红色描边短暂闪烁（预警），
/// 玩家可按 S 防御，在攻击命中瞬间完全抵挡伤害。
/// 攻击序列用 async/ToSignal 编排，死亡时通过 CancellationToken 取消进行中的攻击。
/// </summary>
[GlobalClass]
public partial class Boss : CharacterBody2D
{
    // ---- 配置（由 BossRoster / LevelDirector 注入） ----
    [Export] public int MaxHp { get; set; } = 24;
    [Export] public float SpawnInterval { get; set; } = 10f;     // 生成条文间隔
    [Export] public float AttackInterval { get; set; } = 2.0f;    // 两次主动攻击间隔（越低越频繁，常规攻击节奏）
    [Export] public float HoverSpeed { get; set; } = 100f;       // 平时飘移速度（主动靠近玩家）
    [Export] public float BaseDamage { get; set; } = 1f;         // 每个攻击基础伤害（暴怒 +1）
    [Export] public float TelegraphTime { get; set; } = 0.45f;   // 红色描边闪烁时长（预警窗口）
    [Export] public float HitRange { get; set; } = 90f;          // 近战命中半径
    [Export] public float LungeDist { get; set; } = 220f;        // 扑击突进距离
    [Export] public float BulletDamage { get; set; } = 1f;
    [Export] public float BulletSpeed { get; set; } = 360f;
    [Export] public int BulletCount { get; set; } = 6;
    [Export] public float BulletInterval { get; set; } = 0.6f;
    [Export] public float BeamRange { get; set; } = 320f;
    [Export] public float EnrageDuration { get; set; } = 10f;
    [Export] public float HealPercent { get; set; } = 0.15f;
    [Export] public float HealDelay { get; set; } = 2f;
    [Export] public string BossName { get; set; } = "初审官";
    [Export] public string TexturePath { get; set; } = "res://Assets/PNG/Enemies/Tiles/tile_0000.png";
    [Export] public Color Tint { get; set; } = Colors.White;

    [Signal] public delegate void HealthChangedEventHandler(int hp, int maxHp);
    [Signal] public delegate void DiedEventHandler();

    private enum AttackKind { Lunge, Claws, Gun, Beam, Blink, Enrage, Heal }

    // 平时与玩家保持的距离（主动靠近到此距离，不贴脸也不拉太远）
    private const float RestDistance = 140f;

    // 投技（抓取）：与玩家近距离持续接触超过 GrabHold 秒后可能触发；无前摇，
    // 仅青色护盾可挡。触发后冻结双方 1 秒并播放 X 射线窒息滤镜，再击飞玩家。
    private const float GrabRange = 110f;   // 触发所需近距离（< 常规保持距离 140，须玩家主动贴脸）
    private const float GrabHold = 3f;      // 持续接触达到此秒数才进入判定
    private const float GrabChance = 0.6f;  // 进入判定后的触发概率
    private const float GrabCooldown = 8f;  // 两次投技最小间隔

    private AnimatedSprite2D _sprite;
    private Sprite2D _outline;      // 红色描边光环（仅预警时明灭，身体本体 Tint 保持不变）
    private Player _target;
    private Color _baseTint = Colors.White;
    private int _hp;
    private float _spawnTimer;
    private float _attackTimer;
    private int _enrageBonus;
    private bool _attackActive;
    private CancellationTokenSource _cts;
    private CancellationTokenSource _buffCts;   // 自愈/暴怒增益的独立取消源（非阻塞，不参与攻击冻结）
    private Label _statusIcon;   // 暴怒/自愈头顶标志
    private float _grabProx;            // 当前已持续近距离接触时长
    private float _grabCd;              // 投技冷却
    private bool _grabActive;           // 投技进行中（防止重入）
    private CancellationTokenSource _gcts;  // 投技独立取消源（死亡/场景切换取消）

    // 卡牌相关状态
    private float _stunT;          // 闪现斩眩晕剩余时间（⌛ 图标 + 击倒）
    private int _despiseStacks;    // 蔑视之刃叠层数（0..5）
    private float _despiseT;       // 蔑视层数剩余时长（3s）

    // 候选基础规则类型（随机抽一条；【左右反转】仅在全图规则路径中独立出现）
    private static readonly RuleMode[] RuleTypes =
    {
        RuleMode.NoJump, RuleMode.NoAttack, RuleMode.Slow
    };
    // 可站立表面（top=碰撞顶面 Y，xMin/xMax=该表面可落点的 x 范围）
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
        _sprite = new AnimatedSprite2D();
        SetupBossAnimation();
        _sprite.Scale = new Vector2(2f, 2f);   // 敌方贴图 16px，×2 与玩家一致
        _baseTint = Tint;
        _sprite.Modulate = _baseTint;
        AddChild(_sprite);
        // 红色描边光环：比身体稍大、置于体后，预警时明灭闪烁；
        // 身体本体始终保留自身 Tint，故红 BOSS 也能清晰区分「预警」与「常态」。
        _outline = new Sprite2D();
        _outline.Texture = GD.Load<Texture2D>("res://Assets/PNG/Enemies/Tiles/tile_0000.png");
        _outline.Modulate = new Color(1f, 0.2f, 0.2f, 1f);
        _outline.Scale = new Vector2(2.6f, 2.6f);
        _outline.ZIndex = -1;
        _outline.Visible = false;
        AddChild(_outline);
        AddToGroup("boss");

        _target = GetTree().GetFirstNodeInGroup("player") as Player;
        _spawnTimer = 5f;
        _attackTimer = AttackInterval;
        EmitSignal(SignalName.HealthChanged, _hp, MaxHp);
    }

    public override void _PhysicsProcess(double delta)
    {
        RefreshTarget();
        if (_target == null) return;
        if (_hp <= 0) return;   // 尸体定格：死亡后不再移动 / 抽攻击，供慢动作演出
        float d = (float)delta;

        // 眩晕（闪现斩）：击倒、冻结一切行动与靠近，⌛ 图标常驻；归零复位。
        if (_stunT > 0f)
        {
            _stunT -= d;
            if (_stunT <= 0f) { HideStatusIcon(); if (_sprite != null) _sprite.Rotation = 0f; }
            Velocity = Vector2.Zero;
            MoveAndSlide();
            GlobalPosition = new Vector2(
                Mathf.Clamp(GlobalPosition.X, 40f, 920f),
                Mathf.Clamp(GlobalPosition.Y, 80f, 460f));
            return;
        }
        // 蔑视之刃层数随时间衰减（叠层后 3s 清零）
        if (_despiseStacks > 0)
        {
            _despiseT -= d;
            if (_despiseT <= 0f) _despiseStacks = 0;
        }

        // 生成条文：随机类型 + 随机表面 + 随机 x → 散布到竞技场各处。
        // 第 2 关起基础规则可能升级为全图规则（含专门的【左右反转】）；第 3 关起可能跟随玩家。
        _spawnTimer -= d;
        if (_spawnTimer <= 0f)
        {
            _spawnTimer = SpawnInterval;
            int stage = RunState.Instance != null ? RunState.Instance.CurrentStage : 0;
            bool canGlobal = stage >= 1;   // 第 2 关起：基础规则可能升级为全图
            bool canFollow = stage >= 2;   // 第 3 关起：基础规则区域可能跟随玩家

            RuleMode mode;
            bool isGlobal = false;
            bool follow = false;

            if (canGlobal && GD.Randf() < 0.4f)
            {
                // 全图规则：小概率专门的【左右反转】，否则基础规则升级为全图
                if (GD.Randf() < 0.3f) mode = RuleMode.Invert;
                else mode = RuleTypes[(int)GD.RandRange(0f, RuleTypes.Length - 1f)];
                isGlobal = true;
            }
            else
            {
                mode = RuleTypes[(int)GD.RandRange(0f, RuleTypes.Length - 1f)];
                if (canFollow && GD.Randf() < 0.35f) follow = true;
            }
            if (mode == RuleMode.Invert) isGlobal = true; // 左右反转本质即全图

            var s = Surfaces[(int)GD.RandRange(0f, Surfaces.Length - 1f)];
            float x = (float)GD.RandRange(s.xMin, s.xMax);
            Shout(ShoutFor(mode));
            RuleManager.Instance?.SpawnRule(mode, new Vector2(x, s.top - 52f), isGlobal, follow);
        }

        if (_attackActive)
        {
            // 攻击动画由 tween 控制位置；physics 不移动，避免抢夺 Position
            Velocity = Vector2.Zero;
            return;
        }

        // 攻击调度：普通攻击遵循 AttackInterval 间隔；自愈/暴怒为「非阻塞增益」，
        // 立即发动且不冻结移动，随后本帧立即再抽一个真正的攻击（无需再等一轮间隔）。
        // 间隔期间 boss 走下方 else 分支持续靠近玩家。
        // 反诉：玩家处于禁攻区时，BOSS 也不得发动普通攻击（投技不受此限）
        bool playerNoAttack = RunState.Instance != null && RunState.Instance.HasCard("counter")
            && RuleManager.Instance != null && RuleManager.Instance.PlayerInMode(RuleMode.NoAttack);

        _attackTimer -= d;
        if (_attackTimer <= 0f && !_attackActive && !playerNoAttack)
        {
            var kind = PickAttack();
            if (kind == AttackKind.Enrage || kind == AttackKind.Heal)
            {
                StartBuff(kind);                         // 非阻塞：boss 继续靠近 / 照常攻击
                int g = 0;                               // 极端下连续抽到 buff，最多补抽 8 次取真正攻击
                while ((kind == AttackKind.Enrage || kind == AttackKind.Heal) && g++ < 8)
                    kind = PickAttack();
            }
            _attackTimer = AttackInterval;
            StartAttack(kind);
            return;
        }

        // 投技（抓取）：与玩家近距离持续接触超过 3 秒后可能触发。无前摇，
        // 仅青色护盾可挡；触发后冻结双方 1 秒并播放 X 射线窒息滤镜，再击飞玩家。
        _grabCd = Mathf.Max(0f, _grabCd - d);
        if (_target != null)
        {
            float pd = _target.GlobalPosition.DistanceTo(GlobalPosition);
            _grabProx = pd < GrabRange ? _grabProx + d : 0f;
            if (_grabProx >= GrabHold && _grabCd <= 0f && !_attackActive && !_grabActive)
            {
                _grabProx = 0f;
                _grabCd = GrabCooldown;
                if (GD.Randf() < GrabChance) GrabAsync();
            }
        }

        // 平时主动靠近玩家，维持中距离（RestDistance）。HoverSpeed 已提高，
        // 攻击间隙会明显向玩家逼近，而不是停在远处。
        Vector2 toP = _target.GlobalPosition - GlobalPosition;
        float dist = toP.Length();
        Vector2 dir = dist > 1f ? toP / dist : Vector2.Zero;
        float desired = RestDistance;
        // 反诉：玩家处于限速区时，BOSS 移动也变慢
        bool playerSlow = RunState.Instance != null && RunState.Instance.HasCard("counter")
            && RuleManager.Instance != null && RuleManager.Instance.PlayerInMode(RuleMode.Slow);
        float spd = HoverSpeed * (dist > desired ? 1f : -0.6f) * (playerSlow ? 0.4f : 1f);
        Velocity = dir * spd;
        MoveAndSlide();
        GlobalPosition = new Vector2(
            Mathf.Clamp(GlobalPosition.X, 40f, 920f),
            Mathf.Clamp(GlobalPosition.Y, 80f, 460f));
    }

    // ---------- 攻击调度 ----------
    private void RefreshTarget()
    {
        if (_target == null || !IsInstanceValid(_target))
            _target = GetTree().GetFirstNodeInGroup("player") as Player;
    }

    private AttackKind PickAttack()
    {
        int n = (int)GD.RandRange(0f, 7f); // 0..6 七选一
        return (AttackKind)n;
    }

    private async void StartAttack(AttackKind kind)
    {
        if (_attackActive || _hp <= 0) return;
        _attackActive = true;
        _cts = new CancellationTokenSource();
        try
        {
            await RunAttack(kind, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            // 死亡/场景切换取消：正常退出
        }
        catch (Exception e)
        {
            GD.PrintErr("Boss attack sequence error: ", e);
        }
        finally
        {
            _attackActive = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task RunAttack(AttackKind kind, CancellationToken token)
    {
        switch (kind)
        {
            case AttackKind.Lunge:  await AtkLunge(token);  break;
            case AttackKind.Claws:  await AtkClaws(token);  break;
            case AttackKind.Gun:    await AtkGun(token);    break;
            case AttackKind.Beam:   await AtkBeam(token);   break;
            case AttackKind.Blink:  await AtkBlink(token);  break;
            // Enrage / Heal 不再走攻击序列（见 StartBuff，非阻塞），此处无需分支
        }
    }

    // ---------- 通用小工具 ----------
    private async Task Delay(float sec, CancellationToken token)
    {
        var t = GetTree().CreateTimer(sec);
        await ToSignal(t, "timeout");
        token.ThrowIfCancellationRequested();
    }

    private async Task MoveTo(Vector2 target, float dur, CancellationToken token)
    {
        var tw = CreateTween();
        tw.TweenProperty(this, "global_position", target, dur);
        await ToSignal(tw, "finished");
        token.ThrowIfCancellationRequested();
    }

    /// <summary>红色描边闪烁（预警窗口）：身体本体保持自身 Tint 不变，
    /// 仅让身后红色光环快速明灭 3 次，作为清晰、与 BOSS 体色无关的「即将攻击」预警，
    /// 解决「红 BOSS 看起来一直在红闪、分不清是否预警」的问题。</summary>
    private void FlashRed()
    {
        if (_outline == null) return;
        _outline.Visible = true;
        _outline.Modulate = new Color(1f, 0.2f, 0.2f, 1f);
        var tw = CreateTween();
        // 3 次快速明灭：明显是「警告」而非常驻状态
        for (int i = 0; i < 3; i++)
        {
            tw.TweenProperty(_outline, "modulate:a", 0.1f, TelegraphTime * 0.13f);
            tw.TweenProperty(_outline, "modulate:a", 1f, TelegraphTime * 0.13f);
        }
        tw.TweenCallback(Callable.From(() =>
        {
            if (_outline != null) _outline.Visible = false;
        }));
    }

    private async Task Telegraph(CancellationToken token)
    {
        FlashRed();
        await Delay(TelegraphTime, token);
    }

    private void TryHit(float range)
    {
        var p = _target;
        if (p == null || !IsInstanceValid(p)) return;
        if (p.GlobalPosition.DistanceTo(GlobalPosition) <= range)
            DealDamageToPlayer(BaseDamage);
    }

    private void DealDamageToPlayer(float dmg)
    {
        var p = _target;
        if (p == null || !IsInstanceValid(p)) return;
        int total = (int)Mathf.Round(dmg) + _enrageBonus;
        p.TakeBossDamage(total);
    }

    /// <summary>保持中距离的后撤点：从玩家方向反推 desired 距离。</summary>
    private Vector2 RestPos()
    {
        Vector2 pp = _target != null ? _target.GlobalPosition : GlobalPosition + Vector2.Right * 240f;
        Vector2 dir = GlobalPosition - pp;
        if (dir.Length() < 1f) dir = Vector2.Left;
        return ClampArena(pp + dir.Normalized() * RestDistance);
    }

    private static Vector2 ClampArena(Vector2 p) =>
        new Vector2(Mathf.Clamp(p.X, 40f, 920f), Mathf.Clamp(p.Y, 80f, 460f));

    private void DrawBeam(Vector2[] points, Color color, float life)
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

    // ---------- 7 种攻击模组 ----------
    // 1) 扑击：后撤 1 步 → 红闪 → 朝玩家方向扑 → 命中 1 点
    private async Task AtkLunge(CancellationToken token)
    {
        Vector2 playerPos = _target != null ? _target.GlobalPosition : GlobalPosition + Vector2.Right * 100f;
        Vector2 away = (GlobalPosition - playerPos);
        if (away.Length() > 1f) away = away.Normalized();
        await MoveTo(GlobalPosition + away * 50f, 0.2f, token);   // 后撤
        await Telegraph(token);                                    // 预警
        Vector2 lungeTarget = GlobalPosition + (playerPos - GlobalPosition).Normalized() * LungeDist;
        await MoveTo(ClampArena(lungeTarget), 0.22f, token);       // 扑
        TryHit(HitRange);
        await Delay(0.25f, token);
        await MoveTo(RestPos(), 0.3f, token);                      // 收回
    }

    // 2) 利爪：跳起 → 红闪 → 伸爪 → 命中 1 点
    private async Task AtkClaws(CancellationToken token)
    {
        Vector2 playerPos = _target != null ? _target.GlobalPosition : GlobalPosition;
        await MoveTo(GlobalPosition + new Vector2(0f, -70f), 0.18f, token);  // 跳起
        await Telegraph(token);
        Vector2 dir = (playerPos - GlobalPosition).Normalized();
        DrawBeam(new[] { GlobalPosition, GlobalPosition + dir * 70f }, new Color(1f, 0.9f, 0.9f), 0.2f);
        await MoveTo(GlobalPosition + dir * 60f, 0.12f, token);    // 伸爪前冲
        TryHit(HitRange);
        await Delay(0.3f, token);
        await MoveTo(RestPos(), 0.3f, token);                      // 落回
    }

    // 3) 火枪：掏枪(放大前摇) → 红闪 → 每 0.6s 一发方块弹幕，共 10 发，每发 1 点
    private async Task AtkGun(CancellationToken token)
    {
        var grow = CreateTween();
        grow.TweenProperty(_sprite, "scale", new Vector2(2.4f, 2.4f), 0.15f); // 掏枪前摇
        await Delay(0.2f, token);
        await Telegraph(token);
        for (int i = 0; i < BulletCount; i++)
        {
            token.ThrowIfCancellationRequested();
            Vector2 playerPos = _target != null ? _target.GlobalPosition : GlobalPosition;
            Vector2 dir = (playerPos - GlobalPosition).Normalized();
            SpawnBullet(GlobalPosition, dir);
            await Delay(BulletInterval, token);
        }
        var shrink = CreateTween();
        shrink.TweenProperty(_sprite, "scale", new Vector2(2f, 2f), 0.15f);
    }

    // 4) 射线：左转 + 右转 → 红闪 → 八向射线，命中 1 点
    private async Task AtkBeam(CancellationToken token)
    {
        var t1 = CreateTween(); t1.TweenProperty(_sprite, "rotation", -0.35f, 0.18f);
        await Delay(0.18f, token);
        var t2 = CreateTween(); t2.TweenProperty(_sprite, "rotation", 0.35f, 0.18f);
        await Delay(0.18f, token);
        var t3 = CreateTween(); t3.TweenProperty(_sprite, "rotation", 0f, 0.12f);
        await Delay(0.12f, token);
        await Telegraph(token);
        Vector2 c = GlobalPosition;
        var pts = new System.Collections.Generic.List<Vector2> { c };
        for (int i = 0; i < 8; i++)
        {
            float a = i * Mathf.Pi / 4f;
            Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            pts.Add(c);
            pts.Add(c + d * BeamRange);
        }
        DrawBeam(pts.ToArray(), new Color(1f, 0.4f, 0.9f), 0.4f);
        var p = _target;
        if (p != null && IsInstanceValid(p) && p.GlobalPosition.DistanceTo(c) <= BeamRange)
            DealDamageToPlayer(BaseDamage);
        await Delay(0.4f, token);
    }

    // 5) 闪现：红闪(可见) → 遁入虚空(淡出) → 闪现到玩家后方 → 伸爪，命中 1 点
    private async Task AtkBlink(CancellationToken token)
    {
        Vector2 playerPos = _target != null ? _target.GlobalPosition : GlobalPosition;
        await Telegraph(token);                                    // 红闪预警（仍可见）
        var vanish = CreateTween();
        vanish.TweenProperty(_sprite, "modulate:a", 0f, 0.15f);    // 遁入虚空
        await Delay(0.15f, token);
        Vector2 behind = playerPos + new Vector2(playerPos.X < 480f ? 90f : -90f, 0f);
        GlobalPosition = ClampArena(behind);                       // 闪现到玩家后方
        var appear = CreateTween();
        appear.TweenProperty(_sprite, "modulate:a", 1f, 0.15f);
        await Delay(0.15f, token);
        Vector2 dir = (playerPos - GlobalPosition).Normalized();
        DrawBeam(new[] { GlobalPosition, GlobalPosition + dir * 70f }, new Color(1f, 0.9f, 0.9f), 0.2f);
        await MoveTo(GlobalPosition + dir * 50f, 0.1f, token);     // 伸爪
        TryHit(HitRange);
        await Delay(0.3f, token);
        await MoveTo(RestPos(), 0.3f, token);
    }

    /// <summary>自愈 / 暴怒：非阻塞增益（替代原攻击序列分支）。
    /// 关键改动：不再把 _attackActive 置真，因此 boss 在增益持续期间照常靠近玩家、
    /// 照常发动 2s 间隔内的普通攻击——解决「抽中自愈/暴怒就原地挂机」的问题。
    /// 抽中时本帧还会立即再抽一个真正攻击，故无需再等一轮 AttackInterval。
    /// 死亡/场景切换时由 _buffCts 取消，避免残留增益计时。</summary>
    private async void StartBuff(AttackKind kind)
    {
        if (_hp <= 0) return;
        _buffCts?.Cancel();
        _buffCts = new CancellationTokenSource();
        CancellationToken token = _buffCts.Token;
        try
        {
            if (kind == AttackKind.Enrage)
            {
                _enrageBonus = 1;
                ShowStatusIcon("🔥");
                RuleManager.Instance?.PlaySFX("vacuum_start");
                await Delay(EnrageDuration, token);
                _enrageBonus = 0;
                HideStatusIcon();
            }
            else // Heal
            {
                ShowStatusIcon("❄");
                await Delay(HealDelay, token);
                if (_hp > 0)
                {
                    int heal = (int)Mathf.Ceil(MaxHp * HealPercent);
                    _hp = Mathf.Min(MaxHp, _hp + heal);
                    EmitSignal(SignalName.HealthChanged, _hp, MaxHp);
                    var tw = CreateTween();
                    tw.TweenProperty(_sprite, "modulate", Colors.Green, 0.1f);
                    tw.TweenProperty(_sprite, "modulate", _baseTint, 0.25f);
                }
                HideStatusIcon();
            }
        }
        catch (OperationCanceledException)
        {
            // 死亡/场景切换取消：正常退出
        }
        finally
        {
            _buffCts?.Dispose();
            _buffCts = null;
        }
    }

    private void SpawnBullet(Vector2 origin, Vector2 dir)
    {
        RuleManager.Instance?.PlaySFX("bullet"); // 每发弹幕的发射反馈
        var b = new BossBullet();
        b.GlobalPosition = origin;
        b.Vel = dir * BulletSpeed;
        b.Damage = BulletDamage + _enrageBonus;
        GetTree().CurrentScene?.AddChild(b);
    }

    private void ShowStatusIcon(string text)
    {
        if (_statusIcon == null)
        {
            _statusIcon = new Label();
            _statusIcon.AddThemeFontSizeOverride("font_size", 30);
            _statusIcon.HorizontalAlignment = HorizontalAlignment.Center;
            _statusIcon.Size = new Vector2(60f, 40f);
            _statusIcon.Position = new Vector2(-30f, -90f);
            AddChild(_statusIcon);
        }
        _statusIcon.Text = text;
        _statusIcon.Visible = true;
    }

    private void HideStatusIcon()
    {
        if (_statusIcon != null) _statusIcon.Visible = false;
    }

    // ---------- 投技（抓取）----------
    // 无前摇：与玩家贴脸超过 GrabHold 秒后按概率触发。不进 7 模组轮换、
    // 不显示红色描边预警，唯一防御是玩家提前开「青色护盾」。
    private async void GrabAsync()
    {
        if (_grabActive || _hp <= 0) return;
        _grabActive = true;
        _attackActive = true;   // 复用物理冻结分支（_PhysicsProcess 早退，攻击间隙不移动）
        _gcts?.Cancel();
        _gcts = new CancellationTokenSource();
        var token = _gcts.Token;
        // 投技强力反馈：顿帧 + 视角聚焦（相机放大） + 剧烈震动，让玩家感到真的被击中
        Juice.Instance?.HitStop(0.18f);
        Juice.Instance?.FocusPunch();
        Juice.Instance?.Shake(14f, 0.5f, Juice.ShakeAxis.Both);
        try
        {
            if (_target != null && IsInstanceValid(_target)) _target.SetFrozen(true);
            FlashSuffocation();                          // X 射线 / 窒息对比失真
            await Delay(1.0f, token);                   // 游戏暂停 1 秒
            if (_target != null && IsInstanceValid(_target)) _target.SetFrozen(false);
            // 击飞至空中 + 3 点伤害（仅青色护盾可挡；无视防御 S 与无敌帧）
            if (_target != null && IsInstanceValid(_target))
            {
                if (_target.IsShieldActive()) _target.OnShieldBlock();
                else { _target.KnockUp(-760f); _target.TakeGrab(3); }
            }
        }
        catch (OperationCanceledException)
        {
            if (_target != null) _target.SetFrozen(false);
        }
        catch (Exception e)
        {
            GD.PrintErr("Boss grab error: ", e);
        }
        finally
        {
            _grabActive = false;
            _attackActive = false;
            _gcts?.Dispose();
            _gcts = null;
        }
    }

    /// <summary>全屏 X 射线 / 窒息滤镜：对比失真、在强白与深蓝之间快速交替后淡出，
    /// 模拟「游戏暂停 1 秒」的窒息感。独立 CanvasLayer、不拦截输入。</summary>
    private void FlashSuffocation()
    {
        var layer = new CanvasLayer();
        layer.Layer = 127;
        var rect = new ColorRect();
        rect.Color = new Color(0.7f, 0.95f, 1f, 0f);
        rect.MouseFilter = Control.MouseFilterEnum.Ignore;
        rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(rect);
        GetTree().CurrentScene?.AddChild(layer);
        var tw = CreateTween();
        tw.TweenProperty(rect, "color", new Color(0.92f, 1f, 1f, 0.72f), 0.12f);
        tw.TweenProperty(rect, "color", new Color(0.08f, 0.18f, 0.5f, 0.55f), 0.18f);
        tw.TweenProperty(rect, "color", new Color(0.92f, 1f, 1f, 0.66f), 0.18f);
        tw.TweenProperty(rect, "color", new Color(0.08f, 0.18f, 0.5f, 0.5f), 0.22f);
        tw.TweenProperty(rect, "color", new Color(0f, 0f, 0f, 0f), 0.3f);
        tw.TweenCallback(Callable.From(() => layer.QueueFree()));
    }

    // ---------- 受击 / 死亡 ----------
    // 卡牌相关：蔑视叠加 / 闪现斩眩晕
    /// <summary>蔑视之刃：普攻命中叠加一层（最多 5 层），每层使本 BOSS 受伤 +5%、持续 3s。</summary>
    public void AddDespise()
    {
        if (_despiseStacks < 5) _despiseStacks++;
        _despiseT = 3f;
    }

    /// <summary>闪现斩：眩晕（击倒）指定秒数，头顶显示 ⌛；归零复位朝向。</summary>
    public void ApplyStun(float sec)
    {
        _stunT = Mathf.Max(_stunT, sec);
        ShowStatusIcon("⌛");
        if (_sprite != null) _sprite.Rotation = Mathf.DegToRad(80f); // 击倒倾斜
    }

    public void TakeDamage(int amount)
    {
        if (_hp <= 0) return;
        // 蔑视之刃：敌人受伤 +5%/层（最多 5 层）
        float mult = 1f + 0.05f * _despiseStacks;
        // 逆转裁判：生命值低于 40% 时，所有伤害提升 100%
        if (RunState.Instance != null && RunState.Instance.HasCard("reverse") && _hp <= MaxHp * 0.40f)
            mult *= 2f;
        int eff = (int)Mathf.Round(amount * mult);
        _hp -= eff;
        EmitSignal(SignalName.HealthChanged, _hp, MaxHp);
        SpawnDamageText(eff);   // 受击浮字：小字红伤，>20 放大
        var t = CreateTween();
        t.TweenProperty(_sprite, "modulate", Colors.White, 0.06f);
        t.TweenProperty(_sprite, "modulate", _baseTint, 0.06f);
        RuleManager.Instance?.PlaySFX("boss_hit");
        // 大数字伤害（如大招 8/12）：夸张受击 —— 剧烈震动 + 边缘闪 + 残影撕裂
        if (amount >= 8)
        {
            Juice.Instance?.BigHit(1.0f, new Color(1f, 0.85f, 0.3f));
            Juice.Instance?.AfterImage(_sprite,
                _sprite.SpriteFrames.GetFrameTexture(_sprite.Animation, _sprite.Frame),
                Colors.White, 3, 0.25f);
        }
        if (_hp <= 0)
        {
            _cts?.Cancel();     // 取消进行中的攻击序列
            _gcts?.Cancel();    // 取消进行中的投技序列
            _buffCts?.Cancel(); // 取消进行中的增益计时（自愈/暴怒）
            _enrageBonus = 0;
            if (_target != null) _target.SetFrozen(false);    // 解除可能残留的窒息冻结
            if (_outline != null) _outline.Visible = false;   // 清除可能残留的预警红环
            HideStatusIcon();
            EmitSignal(SignalName.Died);
        }
    }

    private void SetupBossAnimation()
    {
        var frames = new SpriteFrames();
        if (frames.HasAnimation("default")) frames.RemoveAnimation("default");
        frames.AddAnimation("idle");
        for (int i = 0; i <= 7; i++)
            frames.AddFrame("idle", GD.Load<Texture2D>($"res://Assets/PNG/Enemies/Tiles/tile_{i:D4}.png"));
        frames.SetAnimationSpeed("idle", 8);
        frames.SetAnimationLoopMode("idle", SpriteFrames.LoopMode.Linear);
        _sprite.SpriteFrames = frames;
        _sprite.Play("idle");
    }

    private static string ShoutFor(RuleMode m) => m switch
    {
        RuleMode.NoJump   => "禁跳！",
        RuleMode.NoAttack => "禁武！",
        RuleMode.Slow     => "限速！",
        RuleMode.Invert   => "反转！",
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

    /// <summary>受击伤害浮字：在 BOSS 旁生成红色伤害数字，向上飘升并淡出。
    /// amount &gt; 20 时放大显示（大招 / 暴怒加伤等夸张数字）。随树暂停（Tween 随树），
    /// 但死亡演出用 Engine.TimeScale 慢放而非暂停，故击杀瞬间浮字也会在慢动作中飘出。</summary>
    private void SpawnDamageText(int amount)
    {
        var scene = GetTree().CurrentScene;
        if (scene == null) return;
        bool big = amount > 20;
        var lbl = new Label();
        lbl.Text = amount.ToString();
        lbl.AddThemeFontSizeOverride("font_size", big ? 40 : 20);
        lbl.HorizontalAlignment = HorizontalAlignment.Center;
        lbl.VerticalAlignment = VerticalAlignment.Center;
        lbl.Modulate = big ? new Color(1f, 0.35f, 0.25f) : new Color(1f, 0.15f, 0.15f);
        lbl.ZIndex = 60;
        // 随机水平偏移，避免连续受击叠字
        float ox = (float)GD.RandRange(-26f, 26f);
        float oy = (float)GD.RandRange(-10f, 10f);
        lbl.GlobalPosition = new Vector2(GlobalPosition.X + ox, GlobalPosition.Y - 56f + oy);
        scene.AddChild(lbl);
        var tw = CreateTween();
        tw.TweenProperty(lbl, "global_position:y", lbl.GlobalPosition.Y - (big ? 90f : 54f), 0.7f);
        tw.Parallel().TweenProperty(lbl, "modulate:a", 0f, 0.7f);
        tw.TweenCallback(Callable.From(() => lbl.QueueFree()));
    }
}
