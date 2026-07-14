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
    [Export] public float AttackPower { get; set; } = 1f;
    [Export] public float AttackCooldown { get; set; } = 0.32f;

    [Signal] public delegate void HealthChangedEventHandler(int hp, int maxHp);
    [Signal] public delegate void DiedEventHandler();

    // 角色基准缩放（开局即小；攻击挥动也以此为准，避免挥砍后角色被缩水）
    private const float BaseScale = 1.5f;

    // 玩家动画帧（Players/Tiles/tile_0000~0007 共 8 张，均为同一角色）。
    // 帧区间集中在此：若后续确认哪些帧是走、哪些是攻击，只改这里即可。
    private const int RunFrom = 0, RunTo = 7;   // 移动（循环）
    private const int AtkFrom = 0, AtkTo = 7;   // 攻击（单次、稍快）
    private const int RunFps = 10;
    private const int AttackFps = 18;

    // 技能系统
    private float _cd1, _cd2, _cd3, _cd4, _qCd;
    private const float Cd1 = 10f;   // 技能1（毁灭直线）冷却
    private const float Cd2 = 8f;    // 技能2（八向射线）冷却
    private const float Cd3 = 14f;   // 技能3（青色护盾）冷却
    private const float Cd4 = 18f;   // 技能4（自我治愈）冷却
    private const float ShieldDur = 3f;  // 青色护盾无敌时长
    private const float QCd = 5f;    // 划除技能（Q 键）：5 秒冷却、无次数限制
    private const float Skill1Damage = 4f;   // 技能1 伤害（略降）
    private const float Skill2Damage = 3f;   // 技能2 伤害（略降）
    private const float Skill1Band = 40f;    // 直线命中：与 Boss 同平面的纵向容差
    private const float Skill2Range = 320f;  // 射线命中：以角色为中心的半径
    public int SkillPoints { get; private set; }
    public const int SkillCount = 4;
    public const int SkillMax = 10;     // 技能点上限

    private AnimatedSprite2D _sprite;
    private Camera2D _camera;
    private Sprite2D _shield;   // 防御护盾（按 S 显示），独立子节点避免与 modulate 闪烁冲突
    private Sprite2D _shield3;   // 青色护盾（技能3）：3秒无敌，独立子节点避免与 modulate 闪烁冲突
    private float _shieldTimer;  // 青色护盾剩余无敌时间
    private bool _frozen;        // 投技窒息期间冻结输入/物理（非树暂停，滤镜动画照常）

    // 划除（Q）长按蓄力机制
    private bool _striking;          // 是否正在长按蓄力
    private float _strikeProgress;   // 蓄力进度（秒）
    private const float StrikeChargeTime = 1f;  // 长按约 1 秒完成
    private Node2D _strikeBar;       // 角色下方蓄力进度条容器
    private Sprite2D _strikeFill;    // 进度条填充（左对齐增长）
    private bool _attacking;
    private int _hp;
    private float _invuln;
    private float _attackCd;

    // 地图拾取物增益（临时）
    private float _speedBuff = 1f;   // 速度倍率
    private float _speedBuffT;       // 剩余时长
    private float _jumpBuff = 1f;    // 跳跃倍率
    private float _jumpBuffT;

    public int Hp => _hp;

    // ---------- 能量条 / 大招系统 ----------
    private float _energy;           // 当前能量（满 EnergyMax 可释放大招）
    private int _selectedUlt;        // 当前选中的大招（0 乱刀斩 / 1 闪现斩 / 2 时间怀表）
    private float _eHeld;            // E 键长按计时（满 0.6s 释放）
    private bool _ultFired;          // 本次长按是否已释放（防重复）
    private bool _fWasDown;          // F 键边沿检测
    private float _saltBuff;         // 伤口撒盐：违令扣血后 2s 内下次攻击 +50%
    private float _regenT;           // 时间怀表：回血剩余时间
    private float _regenAccum;       // 时间怀表：回血累积器
    private int _defenseCount;       // 无罪推定：本场防御计数
    private bool _pardonUsed;        // 特赦令：本场是否已触发

    // 大招独立冷却（剩余秒）；与能量消耗相互独立，释放后进入冷却
    private float[] _ultCd = { 0f, 0f, 0f };
    private static readonly float[] UltCdMax = { 20f, 18f, 25f };  // 大招 CD：乱刀斩 / 闪现斩 / 时间怀表

    public float Energy => _energy;
    public float EnergyMax => 50f;   // 能量满值固定 50（闪电宣读改为消除额外 +10 能量，不提升上限）
    public int SelectedUlt => _selectedUlt;
    public bool IsEnergyFull => _energy >= EnergyMax;
    public float UltCdRemaining(int i) => _ultCd[i];
    public float UltCdMaxValue(int i) => UltCdMax[i];
    public bool UltReady(int i) => _ultCd[i] <= 0f;

    /// <summary>特赦令是否已在本关使用（用于卡牌查看窗口灰色显示）。</summary>
    public bool PardonUsed => _pardonUsed;

    /// <summary>统一查询是否持有某张卡（卡牌存储于 RunState 跨关保留）。</summary>
    private static bool HasCard(string id) => RunState.Instance != null && RunState.Instance.HasCard(id);

    public override void _Ready()
    {
        // 跨关继承：进入下一关时携带上一关的当前生命与技能点；
        // 新开局 / 重新开始由 RunState.Carry=false 走默认（满血、0 技能点）。
        if (RunState.Instance != null && RunState.Instance.Carry)
        {
            _hp = Mathf.Min(RunState.Instance.CarryHp, MaxHp);
            SkillPoints = Mathf.Min(RunState.Instance.CarrySkill, SkillMax);
            _energy = RunState.Instance.CarryEnergy;        // 能量条跨关继承
            _selectedUlt = RunState.Instance.CarryUlt;      // 所选大招跨关继承
        }
        else
        {
            _hp = MaxHp;
            SkillPoints = 0;
            _energy = 0f;
            _selectedUlt = 0;
        }
        _sprite = GetNode<AnimatedSprite2D>("Sprite");
        SetupPlayerAnimation();
        _sprite.Scale = new Vector2(BaseScale, BaseScale);
        // 防御护盾：按 S 时显示；用独立子节点(白方块+蓝色半透明)避免触碰 _sprite.Modulate
        //（受击闪烁/攻击红闪都改 modulate，护盾独立显示不会互相打架）。
        _shield = new Sprite2D();
        _shield.Texture = Util.Square(46, 46, Colors.White);
        _shield.Modulate = new Color(0.4f, 0.7f, 1f, 0.35f);
        _shield.Visible = false;
        AddChild(_shield);
        // 青色护盾（技能3）：比防御护盾略大、青色，激活时显示
        _shield3 = new Sprite2D();
        _shield3.Texture = Util.Square(54, 54, Colors.White);
        _shield3.Modulate = new Color(0.3f, 1f, 0.95f, 0.4f);
        _shield3.Visible = false;
        AddChild(_shield3);
        // 划除蓄力进度条（长按 Q 时显示于角色下方）
        _strikeBar = new Node2D();
        var barBg = new Sprite2D();
        barBg.Texture = Util.Square(60, 8, new Color(0.15f, 0.15f, 0.15f, 0.85f));
        barBg.Position = new Vector2(0f, 34f);
        _strikeBar.AddChild(barBg);
        _strikeFill = new Sprite2D();
        _strikeFill.Texture = Util.Square(60, 8, new Color(0.3f, 1f, 0.95f));
        _strikeFill.Centered = false;
        _strikeFill.Position = new Vector2(-30f, 30f);   // 左上角对齐 bg 左缘
        _strikeBar.AddChild(_strikeFill);
        _strikeBar.Visible = false;
        AddChild(_strikeBar);
        _camera = GetNode<Camera2D>("Camera2D");
        Juice.Instance?.RegisterCamera(_camera);   // 把相机交给统一反馈中枢抖动
        // 单屏竞技场：把相机限位框设为整个 960x540 场地。
        // 视口尺寸正好等于限位框 -> 相机被锁死、静止不滚动。
        // 注意：不能把左右/上下都设成同一个值（0 宽），那样 Godot 无法满足
        // 会退化为「跟随玩家」，方块跑出屏幕后相机也跟着跑、就回不来了。
        _camera.LimitLeft = 0;
        _camera.LimitTop = 0;
        _camera.LimitRight = 960;
        _camera.LimitBottom = 540;
        AddToGroup("player");
        // 每场（每关载入）重置的计数 / 大招状态（卡牌持有跨关保留，不在此清）
        _defenseCount = 0;
        _pardonUsed = false;
        _saltBuff = 0f;
        _regenT = 0f;
        _regenAccum = 0f;
        for (int i = 0; i < _ultCd.Length; i++) _ultCd[i] = 0f;  // 大招 CD 每关重置
        // 注意：_selectedUlt / _energy 已在上方 Carry 分支中决定（继承或归零），此处不再复位
        // 通知 HUD 同步初始生命（含跨关继承值）；HUD 若尚未订阅则直接读取 _player.Hp 也能得到正确值
        EmitSignal(SignalName.HealthChanged, _hp, MaxHp);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_frozen) return;   // 投技窒息冻结：暂停一切输入与物理；滤镜/计时由 BOSS 侧独立推进
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

        // 防御护盾跟随显示（不依赖 _sprite.Modulate）
        bool guarding = IsGuarding;
        if (_shield != null) _shield.Visible = guarding;

        // 重力
        if (!IsOnFloor())
            Velocity = new Vector2(Velocity.X, Velocity.Y + Gravity * d);

        // 水平移动（防御中禁止移动，把输入归零；真空期×2，叠加拾取加速；限速区内移速大幅降低）
        float dir = guarding ? 0f : Input.GetAxis("move_left", "move_right");
        float spd = Speed * (RuleManager.Instance != null ? RuleManager.Instance.SpeedMult : 1f) * _speedBuff;
        // 青色护盾激活期间：无视一切限制规则（含全图/跟随规则），行动不受约束、不掉血
        bool shield = IsShieldActive();
        if (RuleManager.Instance != null && !shield)
        {
            foreach (var r in RuleManager.Instance.ActiveRules)
                if (IsInstanceValid(r) && r.Mode == RuleMode.Slow && r.Contains(GlobalPosition))
                { spd *= 0.4f; break; }
        }
        // 左右反转全图规则：翻转水平输入（按 A 往右、按 D 往左），朝向同步翻转
        // 护盾期间免疫反转，保持正常左右操作
        if (!shield && RuleManager.Instance != null && RuleManager.Instance.Inverted) dir = -dir;

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

        // 跳跃 / 违规判定（防御中禁止起跳：防御=扎根，不能借跳位移）
        if (Input.IsActionJustPressed("jump") && !guarding)
        {
            // 护盾期间无视禁跳规则，可正常起跳（不取消、不惩罚）
            bool inNoJump = !shield && RuleManager.Instance != null &&
                RuleManager.Instance.ActiveRules.Any(r => IsInstanceValid(r) &&
                    r.Mode == RuleMode.NoJump && r.Contains(GlobalPosition));
            if (inNoJump)
            {
                // 违规罚血 + 撒盐窗口（违令扣血后 2s 内下次攻击 +50%）
                if (_invuln <= 0) TakeDamage(1);
                _saltBuff = 2f;
                ShowPopup("违规！", Colors.Red);
                Shake();
                Juice.Instance?.FlashEdge(Colors.Red, 0.3f);   // 违令而行：屏幕边缘刺痛红闪
                RuleManager.Instance?.PlaySFX("violation");
                if (!HasCard("salt"))
                    Velocity = new Vector2(Velocity.X, 0);  // 不持有「伤口撒盐」：取消起跳（被禁跳拦住）
                else if (IsOnFloor())
                    Velocity = new Vector2(Velocity.X, JumpVelocity * _jumpBuff);  // 持有盐：仍可跳出禁跳区（行动不被阻止）
            }
            else if (IsOnFloor())
            {
                Velocity = new Vector2(Velocity.X, JumpVelocity * _jumpBuff);
                RuleManager.Instance?.PlaySFX("jump");
            }
        }

        // 普攻（禁武区内攻击 = 违规；防御中不可攻击，避免「站着格挡还能输出」的逃课）
        _attackCd -= d;
        if (Input.IsActionPressed("attack") && _attackCd <= 0f && !guarding)
        {
            _attackCd = AttackCooldown;
            // 护盾期间无视禁武规则，可正常攻击（不惩罚）
            bool inNoAttack = !shield && RuleManager.Instance != null &&
                RuleManager.Instance.ActiveRules.Any(r => IsInstanceValid(r) &&
                    r.Mode == RuleMode.NoAttack && r.Contains(GlobalPosition));
            if (inNoAttack && !HasCard("salt"))
            {
                // 禁武区违规：罚血 + 撒盐窗口（不持有「伤口撒盐」则禁止攻击）
                if (_invuln <= 0) TakeDamage(1);
                _saltBuff = 2f;
                ShowPopup("违规！", Colors.Red);
                Shake();
                Juice.Instance?.FlashEdge(Colors.Red, 0.3f);   // 违令而行：屏幕边缘刺痛红闪
                RuleManager.Instance?.PlaySFX("violation");
            }
            else
            {
                // 普通攻击，或持有「伤口撒盐」时在禁武区内照常攻击（行动不被阻止）
                Swing();
            }
        }

        // 划除条文（Q）：长按约 1 秒释放；期间按任意其他键或提前松手=取消（不进 CD），完成才进 CD。
        // Q 现已可在任意位置释放（不再受区域/禁改限制）。
        if (_striking)
        {
            if (!Input.IsActionPressed("strike") || OtherKeyPressed())
                CancelStrike();
            else
            {
                _strikeProgress += d;
                if (_strikeFill != null)
                    _strikeFill.Scale = new Vector2(Mathf.Clamp(_strikeProgress / StrikeChargeTimeEffective, 0f, 1f), 1f);
                if (_strikeProgress >= StrikeChargeTimeEffective) CompleteStrike();
            }
        }
        else if (_qCd <= 0f && !guarding && Input.IsActionJustPressed("strike"))
        {
            _striking = true;
            _strikeProgress = 0f;
            if (_strikeBar != null) _strikeBar.Visible = true;
            if (_strikeFill != null) _strikeFill.Scale = new Vector2(0f, 1f);
        }

        // 技能（数字键 1 / 2）：CD 制。空格同时触发跳+攻击，故技能独立键避免误触。
        _cd1 = Mathf.Max(0f, _cd1 - d);
        _cd2 = Mathf.Max(0f, _cd2 - d);
        _qCd = Mathf.Max(0f, _qCd - d); // 划除技能（Q）冷却倒计时
        _cd3 = Mathf.Max(0f, _cd3 - d);
        _cd4 = Mathf.Max(0f, _cd4 - d);
        if (_shieldTimer > 0f) { _shieldTimer -= d; if (_shieldTimer < 0f) _shieldTimer = 0f; }
        if (_shield3 != null) _shield3.Visible = _shieldTimer > 0f;
        // 技能 1~4：统一走 TryCast —— 需冷却就绪、非防御、且各消耗 1 技能点
        if (Input.IsActionJustPressed("skill1")) TryCast(1, CastSkill1, ref _cd1, Cd1);
        if (Input.IsActionJustPressed("skill2")) TryCast(2, CastSkill2, ref _cd2, Cd2);
        if (Input.IsActionJustPressed("skill3")) TryCast(3, CastSkill3, ref _cd3, Cd3);
        if (Input.IsActionJustPressed("skill4")) TryCast(4, CastSkill4, ref _cd4, Cd4);

        // 大招：F 循环切换选中，长按 E(0.6s) 释放当前选中（能量满时）
        bool eDown = Input.IsKeyPressed(Key.E);
        if (eDown)
        {
            _eHeld += d;
            if (_eHeld >= 0.6f && !_ultFired && IsEnergyFull)
            {
                ReleaseUltimate(_selectedUlt);
                _ultFired = true;
            }
        }
        else { _eHeld = 0f; _ultFired = false; }

        bool fDown = Input.IsKeyPressed(Key.F);
        if (fDown && !_fWasDown)
        {
            _selectedUlt = (_selectedUlt + 1) % 3;
            ShowPopup(UltName(_selectedUlt), Colors.Gold);
        }
        _fWasDown = fDown;

        // 伤口撒盐 / 时间怀表 计时
        if (_saltBuff > 0f) _saltBuff -= d;
        if (_regenT > 0f)
        {
            _regenT -= d;
            _regenAccum += d;
            while (_regenAccum >= 1f)
            {
                _regenAccum -= 1f;
                if (_hp < MaxHp)
                {
                    _hp++;
                    EmitSignal(SignalName.HealthChanged, _hp, MaxHp);
                    Juice.Instance?.FlashEdge(new Color(0.4f, 0.95f, 0.5f), 0.22f);   // 时间怀表回血：边缘绿闪
                }
            }
        }

        // 大招冷却递减（每个大招独立计时）
        for (int i = 0; i < _ultCd.Length; i++)
            if (_ultCd[i] > 0f) _ultCd[i] = Mathf.Max(0f, _ultCd[i] - d);

        MoveAndSlide();

        // 竞技场硬边界（单屏静止相机）：任何穿地 / 越界 / 被弹簧弹飞都必须被拦在
        // 960x540 内，否则方块掉出世界后就再也回不来了（之前“Player没了”的根因）。
        // 地面正好等于屏幕宽，两侧无墙，走到边缘外会踏空坠落——这里用钳制兜底。
        const float half = 16f;
        Position = new Vector2(
            Mathf.Clamp(Position.X, half, 960f - half),
            Mathf.Clamp(Position.Y, 0f, 504f)
        );

        UpdateAnim(dir);
    }

    private void Swing()
    {
        RuleManager.Instance?.PlaySFX("attack");
        // 触发攻击动画（单次、稍快）；结束由 OnAnimFinished 复位
        _attacking = true;
        _sprite.Play("attack");

        var tween = CreateTween();
        tween.TweenProperty(_sprite, "scale", Vector2.One * BaseScale * 1.4f, 0.08f).From(Vector2.One * BaseScale);
        tween.TweenProperty(_sprite, "scale", Vector2.One * BaseScale, 0.08f);

        int dealt = ComputeAttackDamage(AttackPower);
        bool hit = false;
        foreach (Node n in GetTree().GetNodesInGroup("boss"))
            if (n is Boss b && GlobalPosition.DistanceTo(b.GlobalPosition) < 84f)
            {
                hit = true;
                b.AddDespise();   // 蔑视之刃：普攻命中叠加蔑视
                b.TakeDamage(dealt);
            }
        // 仅在真正命中 Boss 时才有打击反馈与能量积累；对着空气空挥不做任何增益
        if (hit)
        {
            GainEnergyForDamage(dealt);
            Juice.Instance?.HitStop(0.05f);   // 普攻命中顿帧
            Juice.Instance?.Shake(6f, 0.12f, Juice.ShakeAxis.Both);
        }
    }

    /// <summary>统一计算一次玩家攻击的最终伤害：含真空期攻击倍率、伤口撒盐(+50%)、终审判决(血=1 时 +30%)。</summary>
    private int ComputeAttackDamage(float baseDmg)
    {
        float dmg = baseDmg * (RuleManager.Instance != null ? RuleManager.Instance.AttackMult : 1f);
        if (_saltBuff > 0f) { dmg *= 1.5f; _saltBuff = 0f; }   // 伤口撒盐：本次攻击消耗
        if (HasCard("final") && _hp == 1) dmg *= 1.3f;         // 终审判决：丝血攻击 +30%
        return (int)Mathf.Round(dmg);
    }

    /// <summary>积累能量：基础量 × 终审判决(血=1 时 +30%)。能量上限见 EnergyMax。</summary>
    private void GainEnergy(float amount)
    {
        float mult = 1f;
        if (HasCard("final") && _hp == 1) mult = 1.3f;
        _energy = Mathf.Min(EnergyMax, _energy + amount * mult);
    }
    private void GainEnergyForDamage(int dealt) => GainEnergy(dealt * 2f);  // 每造成 1 点伤害 +2 能量

    private static string UltName(int i) => i switch
    {
        0 => "乱刀斩",
        1 => "闪现斩",
        _ => "时间怀表",
    };

    /// <summary>释放选中的大招（消耗全部能量）。三种：乱刀斩12 / 闪现斩8+眩晕 / 时间怀表回血7s。</summary>
    private void ReleaseUltimate(int idx)
    {
        if (_ultCd[idx] > 0f) { ShowPopup("大招冷却中", Colors.Gray); return; }  // 冷却未就绪先拦截（不消耗能量）
        if (Energy < EnergyMax) return;
        _energy = 0f;
        var boss = GetTree().GetFirstNodeInGroup("boss") as Boss;
        switch (idx)
        {
            case 0: // 乱刀斩：原地重击 BOSS 12 点
                if (boss != null && IsInstanceValid(boss))
                {
                    boss.TakeDamage(ComputeAttackDamage(12f));
                    Juice.Instance?.HitStop(0.18f);                  // 爆气大招命中顿帧
                    Juice.Instance?.Shake(14f, 0.4f, Juice.ShakeAxis.Horizontal);   // 乱刀斩：高频横向抖动
                    Juice.Instance?.AfterImage(_sprite,
                        _sprite.SpriteFrames.GetFrameTexture(_sprite.Animation, _sprite.Frame),
                        Colors.Red, 4, 0.3f);   // 释放瞬间撕裂残影
                }
                ShowPopup("乱刀斩!", Colors.Gold);
                break;
            case 1: // 闪现斩：瞬移到 BOSS 身后，造成 8 点并击倒 + 2s 眩晕
                if (boss != null && IsInstanceValid(boss))
                {
                    Juice.Instance?.Shake(12f, 0.22f, Juice.ShakeAxis.Vertical);  // 闪现出现：纵向抖动
                    Vector2 behind = boss.GlobalPosition + new Vector2(boss.GlobalPosition.X < 480f ? 70f : -70f, 0f);
                    GlobalPosition = new Vector2(Mathf.Clamp(behind.X, 16f, 944f), boss.GlobalPosition.Y);
                    boss.TakeDamage(ComputeAttackDamage(8f));
                    boss.ApplyStun(2f);
                    Juice.Instance?.HitStop(0.18f);                  // 背刺命中顿帧
                    Juice.Instance?.Shake(12f, 0.26f, Juice.ShakeAxis.Horizontal); // 背刺命中：横向抖动
                    Juice.Instance?.AfterImage(_sprite,
                        _sprite.SpriteFrames.GetFrameTexture(_sprite.Animation, _sprite.Frame),
                        new Color(0.5f, 0.95f, 1f), 4, 0.3f);
                }
                ShowPopup("闪现斩!", Colors.Gold);
                break;
            case 2: // 时间怀表：每秒回复 1 点生命，持续 7 秒（无顿帧，柔绿反馈）
                _regenT = 7f;
                _regenAccum = 0f;
                Juice.Instance?.Shake(5f, 0.25f, Juice.ShakeAxis.Both);
                Juice.Instance?.FlashEdge(new Color(0.4f, 0.95f, 0.5f), 0.35f);
                ShowPopup("时间怀表!", new Color(0.5f, 0.9f, 1f));
                break;
        }
        _ultCd[idx] = UltCdMax[idx];   // 释放后进入独立冷却（与能量消耗无关）
        RuleManager.Instance?.PlaySFX("vacuum_start");
    }

    /// <summary>用 Players/Tiles/tile_0000~0007 八张同角色图构建 SpriteFrames：
    /// "run"（移动，循环）与 "attack"（攻击，单次、稍快）。帧区间集中在常量，便于重映射。</summary>
    private void SetupPlayerAnimation()
    {
        var frames = new SpriteFrames();
        if (frames.HasAnimation("default")) frames.RemoveAnimation("default");
        frames.AddAnimation("run");
        frames.AddAnimation("attack");
        for (int i = RunFrom; i <= RunTo; i++) frames.AddFrame("run", LoadFrame(i));
        for (int i = AtkFrom; i <= AtkTo; i++) frames.AddFrame("attack", LoadFrame(i));
        frames.SetAnimationSpeed("run", RunFps);
        frames.SetAnimationLoopMode("run", SpriteFrames.LoopMode.Linear);
        frames.SetAnimationSpeed("attack", AttackFps);
        frames.SetAnimationLoopMode("attack", SpriteFrames.LoopMode.None);
        _sprite.SpriteFrames = frames;
        // 注意：本 Godot 4.7 构建下 animation_finished 信号实际以 0 参数发出，
        // 故用 0 参数回调（攻击动画为唯一非循环动画，结束后复位即可）。
        _sprite.Connect(AnimatedSprite2D.SignalName.AnimationFinished, Callable.From(OnAnimFinished));
        _sprite.Play("run");
    }

    private static Texture2D LoadFrame(int i) =>
        GD.Load<Texture2D>($"res://Assets/PNG/Players/Tiles/tile_{i:D4}.png");

    private void OnAnimFinished()
    {
        // 仅 "attack"（唯一非循环动画）会触发 animation_finished；复位后
        // 由 _PhysicsProcess 的 UpdateAnim 决定下一帧是跑/站 状态。
        _attacking = false;
    }

    /// <summary>根据移动输入切换 跑/站 动画与朝向；攻击动画进行中时让位。</summary>
    private void UpdateAnim(float dir)
    {
        if (_attacking) return;
        if (Mathf.Abs(dir) > 0.01f)
        {
            if (_sprite.Animation != "run" || !_sprite.IsPlaying()) _sprite.Play("run");
            if (dir != 0f) _sprite.FlipH = dir < 0f;
        }
        else
        {
            _sprite.Pause();
            _sprite.Frame = 0; // 站立中立帧
        }
    }

    /// <summary>长按 Q 完成：划除最近的可划除规则（任意类型，不再受区域/禁改限制），触发反转 + 真空期。</summary>
    private void CompleteStrike()
    {
        _striking = false;
        if (_strikeBar != null) _strikeBar.Visible = false;
        _qCd = QCd;   // 完成才进 CD；取消不进 CD
        if (RuleManager.Instance == null) return;
        // 就近划除任意非 Spring 条文（全图/跟随规则均可在其消除区附近划除）
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
        var slash = CreateTween();
        slash.TweenProperty(_sprite, "modulate", Colors.Red, 0.06f);
        slash.TweenProperty(_sprite, "modulate", Colors.White, 0.12f);
        Shake();
        RuleManager.Instance.PlaySFX("paper_tear");
        RuleManager.Instance.StrikeRule(nearest);
        Juice.Instance?.HitStop(0.06f);             // 成功消除规则：一顿微帧
        Juice.Instance?.FlashEdge(Colors.Gold, 0.3f);   // 金边闪光：规则崩坏瞬间
        GainEnergy(10);                          // 消除规则 +10 能量（基础）
        if (HasCard("lightning")) GainEnergy(10);// 闪电宣读：消除额外 +10 能量
        if (HasCard("chain")) { CastRayFree(0.5f); ShowPopup("连锁违宪!", new Color(0.5f, 0.9f, 1f)); } // 卡牌被动浮空提示
    }

    private void CancelStrike()
    {
        _striking = false;
        if (_strikeBar != null) _strikeBar.Visible = false;
        RuleManager.Instance?.PlaySFX("cancel"); // 取消蓄力给一点反馈
        // 取消不进 CD
    }

    /// <summary>长按蓄力期间是否按下了「其他」键（任意其他键取消划除）。</summary>
    private static bool OtherKeyPressed()
    {
        return Input.IsActionJustPressed("move_left") || Input.IsActionJustPressed("move_right") ||
               Input.IsActionJustPressed("move_up") || Input.IsActionJustPressed("move_down") ||
               Input.IsActionJustPressed("jump") || Input.IsActionJustPressed("attack") ||
               Input.IsActionJustPressed("guard") ||
               Input.IsActionJustPressed("skill1") || Input.IsActionJustPressed("skill2") ||
               Input.IsActionJustPressed("skill3") || Input.IsActionJustPressed("skill4");
    }

    // ---------- 技能系统 ----------
    public bool IsSkillReady(int i) => i switch { 1 => _cd1 <= 0f, 2 => _cd2 <= 0f, 3 => _cd3 <= 0f, 4 => _cd4 <= 0f, _ => false };
    public float SkillCd(int i) => i switch { 1 => _cd1, 2 => _cd2, 3 => _cd3, 4 => _cd4, _ => 0f };
    public float SkillCdMax(int i) => i switch { 1 => Cd1, 2 => Cd2, 3 => Cd3, 4 => Cd4, _ => 0f };

    /// <summary>统一释放技能 1~4：需冷却就绪、非防御、且至少有 1 技能点；释放即消耗 1 技能点并进入冷却。</summary>
    private void TryCast(int id, System.Action cast, ref float cd, float cdMax)
    {
        if (IsGuarding || cd > 0f) return;                       // 防御中 / 冷却中：静默不触发
        if (SkillPoints <= 0) { ShowPopup("需要技能点", Colors.Gray); return; }
        SkillPoints--;                                           // 消耗 1 技能点（HUD 实时读取）
        cd = cdMax;                                              // 进入冷却
        cast();
    }

    // 划除技能（Q 键）：5 秒冷却、无次数限制
    public bool IsQReady => _qCd <= 0f;
    public float QSkillCd => _qCd;
    public float QSkillCdMax => QCd;
    // Q 蓄力有效时长：闪电宣读 1s→0.5s；终审判决(血=1) 再减半
    private float StrikeChargeTimeEffective =>
        (HasCard("lightning") ? 0.5f : StrikeChargeTime) *
        (HasCard("final") && _hp == 1 ? 0.5f : 1f);

    /// <summary>技能1：在角色所在水平面释放一条贯穿全屏的毁灭直线，命中同平面 Boss。</summary>
    private void CastSkill1()
    {
        float py = GlobalPosition.Y;
        SpawnBeam(new Vector2[] { new Vector2(0f, py), new Vector2(960f, py) },
                  new Color(1f, 0.35f, 0.3f), 0.35f);
        var boss = GetTree().GetFirstNodeInGroup("boss") as Boss;
        int dealt = ComputeAttackDamage(Skill1Damage);
        if (boss != null && IsInstanceValid(boss) && Mathf.Abs(boss.GlobalPosition.Y - py) < Skill1Band)
        {
            boss.TakeDamage(dealt);
            Juice.Instance?.HitStop(0.1f);   // 1 技能命中顿帧
            Juice.Instance?.Shake(9f, 0.2f, Juice.ShakeAxis.Both);
        }
        GainEnergyForDamage(dealt);
        RuleManager.Instance?.PlaySFX("vacuum_start");
    }

    /// <summary>技能2 / 申辩 / 连锁违宪 共用：以角色为中心向 8 方向释放射线（dmgMul 默认 1；申辩=1、连锁违宪=0.5）。</summary>
    private void CastRayFree(float dmgMul = 1f)
    {
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
        int dealt = ComputeAttackDamage(Skill2Damage * dmgMul);
        var boss = GetTree().GetFirstNodeInGroup("boss") as Boss;
        if (boss != null && IsInstanceValid(boss) && c.DistanceTo(boss.GlobalPosition) < Skill2Range)
            boss.TakeDamage(dealt);
        GainEnergyForDamage(dealt);
    }

    /// <summary>技能2：以角色为中心向 8 个方向释放射线，命中范围内 Boss（消耗技能点）。</summary>
    private void CastSkill2()
    {
        CastRayFree(1f);
        Juice.Instance?.HitStop(0.1f);   // 2 技能命中顿帧
        Juice.Instance?.Shake(9f, 0.2f, Juice.ShakeAxis.Both);
        RuleManager.Instance?.PlaySFX("vacuum_start");
    }

    /// <summary>技能3：青色护盾——3 秒无敌（可抵挡投技），14 秒 CD。</summary>
    private void CastSkill3()
    {
        _shieldTimer = ShieldDur;
        if (_shield3 != null) _shield3.Visible = true;
        ShowPopup("护盾!", new Color(0.3f, 1f, 0.95f));
        RuleManager.Instance?.PlaySFX("vacuum_start");
        Shake();
    }

    /// <summary>技能4：自我治愈——立即回复 2 点生命值，18 秒 CD。</summary>
    private void CastSkill4()
    {
        if (_hp < MaxHp) _hp = Mathf.Min(MaxHp, _hp + 2);
        EmitSignal(SignalName.HealthChanged, _hp, MaxHp);
        ShowPopup("+2", new Color(0.4f, 1f, 0.5f));
        RuleManager.Instance?.PlaySFX("fall-a");   // 治愈类音效
        Shake();
        if (HasCard("plea")) { CastRayFree(1f); ShowPopup("申辩!", new Color(0.5f, 0.9f, 1f)); } // 卡牌被动浮空提示
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
        RuleManager.Instance?.PlaySFX("pickup");
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

    /// <summary>捡起技能宝珠后调用：技能点 +1（HUD 技能条显示）。</summary>
    public void AddSkillPoint() => SkillPoints = Mathf.Min(SkillMax, SkillPoints + 1);

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

    /// <summary>是否正在防御（按住 S）。BOSS 攻击命中瞬间若防御则被完全抵挡。</summary>
    public bool IsGuarding => Input.IsActionPressed("guard");

    /// <summary>来自 BOSS 攻击的伤害：防御中完全抵挡（优先于无敌帧，按住即生效）；
    /// 否则扣血并给短无敌帧（避免弹幕单帧叠伤）。</summary>
    public void TakeBossDamage(int amount)
    {
        if (_hp <= 0) return;
        if (IsShieldActive()) { OnShieldBlock(); return; }   // 青色护盾：3 秒无敌，优先于一切 BOSS 伤害
        if (IsGuarding) { OnGuardBlock(); return; }   // 防御优先：无视无敌帧，按住 S 即生效
        if (_invuln > 0f) return;
        Juice.Instance?.Shake(8f, 0.25f, Juice.ShakeAxis.Both);          // 受 BOSS 伤害：冲击震动
        Juice.Instance?.FlashEdge(new Color(1f, 0.3f, 0.3f), 0.25f, 0.6f); // 受击边缘红闪
        _hp -= amount;
        if (_hp <= 0)
        {
            if (HasCard("pardon") && !_pardonUsed)   // 特赦令：致命伤免死（锁 1 血 + 3s 青盾），每场 1 次
            {
                _pardonUsed = true;
                _hp = 1;
                _shieldTimer = Mathf.Max(_shieldTimer, 3f);
                if (_shield3 != null) _shield3.Visible = true;
                ShowPopup("特赦!", new Color(0.3f, 1f, 0.95f));
                RuleManager.Instance?.PlaySFX("vacuum_start");
            }
            else
            {
                RuleManager.Instance?.PlaySFX("player_hurt");
                EmitSignal(SignalName.HealthChanged, _hp, MaxHp);
                EmitSignal(SignalName.Died);
                return;
            }
        }
        _invuln = 0.5f;
        RuleManager.Instance?.PlaySFX("player_hurt");
        EmitSignal(SignalName.HealthChanged, _hp, MaxHp);
    }

    public void OnShieldBlock()
    {
        ShowPopup("护盾!", new Color(0.3f, 1f, 0.95f));
        RuleManager.Instance?.PlaySFX("vacuum_start");
    }

    private void OnGuardBlock()
    {
        Juice.Instance?.HitStop(0.08f);                          // 防御成功顿帧
        Juice.Instance?.Shake(4f, 0.1f, Juice.ShakeAxis.Both);  // 防御成功轻微震动
        ShowPopup("格挡!", new Color(0.5f, 0.8f, 1f));
        RuleManager.Instance?.PlaySFX("vacuum_start");
        GainEnergy(2);   // 成功防御 +2 能量
        _defenseCount++;
        if (HasCard("innocent") && _defenseCount % 10 == 0)
        {
            RuleManager.Instance?.ClearAllRules();       // 无罪推定：清除全屏规则
            _shieldTimer = Mathf.Max(_shieldTimer, 2f);  // 并获得 2s 青色护盾
            if (_shield3 != null) _shield3.Visible = true;
            ShowPopup("无罪推定!", new Color(0.3f, 1f, 0.95f));
        }
    }

    /// <summary>青色护盾（技能3）是否生效中：3 秒无敌，可抵挡投技。</summary>
    public bool IsShieldActive() => _shieldTimer > 0f;

    /// <summary>投技冻结：true 时 _PhysicsProcess 早退，暂停输入与物理（非树暂停，滤镜动画照常）。</summary>
    public void SetFrozen(bool f) => _frozen = f;

    /// <summary>投技击飞：给一个强上抛速度（重力随后接管落回）。</summary>
    public void KnockUp(float vy = -760f) => Velocity = new Vector2(Velocity.X, vy);

    /// <summary>投技伤害：仅青色护盾可挡，无视防御 S 与无敌帧（让该技能真正危险、必须靠护盾应对）。</summary>
    public void TakeGrab(int amount)
    {
        if (IsShieldActive()) { OnShieldBlock(); return; }
        if (_hp <= 0) return;
        _hp -= amount;
        if (_hp <= 0)
        {
            if (HasCard("pardon") && !_pardonUsed)   // 特赦令：致命伤免死（锁 1 血 + 3s 青盾），每场 1 次
            {
                _pardonUsed = true;
                _hp = 1;
                _shieldTimer = Mathf.Max(_shieldTimer, 3f);
                if (_shield3 != null) _shield3.Visible = true;
                ShowPopup("特赦!", new Color(0.3f, 1f, 0.95f));
                RuleManager.Instance?.PlaySFX("vacuum_start");
            }
            else
            {
                RuleManager.Instance?.PlaySFX("player_hurt");
                EmitSignal(SignalName.HealthChanged, _hp, MaxHp);
                EmitSignal(SignalName.Died);
                return;
            }
        }
        Juice.Instance?.BigHit(1.2f, new Color(1f, 0.2f, 0.2f));   // 投技命中：剧烈震动 + 裂痕
        _invuln = 0.5f;
        RuleManager.Instance?.PlaySFX("player_hurt");
        EmitSignal(SignalName.HealthChanged, _hp, MaxHp);
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
        // 统一收口到 Juice 的有方向 trauma 震动（默认中等双向）
        Juice.Instance?.Shake(6f, 0.14f, Juice.ShakeAxis.Both);
    }
}
