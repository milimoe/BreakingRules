using Godot;
using System.Collections.Generic;

namespace BreakingRules;

/// <summary>
/// 规则管理器（Autoload 单例）。取代原 RuleEngine。
/// 负责：条文对象生命周期（≤3）、真空期状态机、全局 Buff 倍率、信号广播。
/// </summary>
[GlobalClass]
public partial class RuleManager : Node
{
    public static RuleManager Instance { get; private set; }

    [Signal] public delegate void RuleStruckEventHandler(RuleObject rule);
    [Signal] public delegate void VacuumStartedEventHandler();
    [Signal] public delegate void VacuumEndedEventHandler();

    [Export] public float VacuumTime { get; set; } = 8f;
    [Export] public int MaxActiveRules { get; set; } = 3;
    [Export] public float AttackMultVacuum { get; set; } = 3f;
    [Export] public float SpeedMultVacuum { get; set; } = 2f;

    private readonly List<RuleObject> _active = new();
    private float _vacuumRemaining;
    private bool _isVacuum;

    // 随机掉落技能宝珠
    [Export] public float SkillDropInterval { get; set; } = 6f;
    private float _dropTimer = 5f;
    private static readonly Vector2[] DropSpots =
    {
        new(140f, 470f), new(320f, 470f), new(500f, 470f), new(680f, 470f), new(840f, 470f),
        new(180f, 388f), new(380f, 308f), new(580f, 388f), new(760f, 308f), new(480f, 228f),
    };

    // ---- 音效：Autoload 上的 AudioStreamPlayer + id→ogg 映射 ----
    private AudioStreamPlayer _sfx;
    private readonly Dictionary<string, string> _sfxPaths = new()
    {
        { "paper_tear",   "res://Assets/Sounds/shoot-a.ogg" },    // 划除条文
        { "violation",    "res://Assets/Sounds/error-b.ogg" },    // 违规
        { "boss_hit",     "res://Assets/Sounds/hurt-c.ogg" },     // 击中 Boss
        { "win",          "res://Assets/Sounds/select-a.ogg" },   // 胜利
        { "vacuum_start", "res://Assets/Sounds/explosion-b.ogg" },// 真空期开始
        { "vacuum_end",   "res://Assets/Sounds/fall-a.ogg" },     // 真空期结束
    };
    private readonly Dictionary<string, AudioStream> _sfxStreams = new();

    public override void _EnterTree()
    {
        Instance = this;
        _sfx = new AudioStreamPlayer();
        _sfx.ProcessMode = Node.ProcessModeEnum.Always; // 暂停游戏时胜利音效仍要播完
        AddChild(_sfx);
        foreach (var kv in _sfxPaths)
        {
            var s = GD.Load<AudioStream>(kv.Value);
            if (s != null) _sfxStreams[kv.Key] = s;
        }
        base._EnterTree();
    }

    /// <summary>场景重开时 Autoload 不重新 _EnterTree，必须显式复位。</summary>
    public void Reset()
    {
        _active.Clear();
        _vacuumRemaining = 0f;
        _isVacuum = false;
    }

    public IReadOnlyList<RuleObject> ActiveRules => _active;
    public bool IsVacuum => _isVacuum;
    public float VacuumRemaining => _vacuumRemaining;

    public float AttackMult => _isVacuum ? AttackMultVacuum : 1f;
    public float SpeedMult => _isVacuum ? SpeedMultVacuum : 1f;

    /// <summary>Boss 调用：在 anchor 处生成一条指定类型的「规则条文」（场上已达上限则忽略）。</summary>
    public void SpawnRule(RuleMode mode, Vector2 anchor)
    {
        if (_active.Count >= MaxActiveRules) return;
        var scene = GD.Load<PackedScene>("res://Scenes/RuleObject.tscn");
        if (scene == null) return;
        var obj = scene.Instantiate<RuleObject>();
        obj.GlobalPosition = anchor;
        obj.RuleIndex = _active.Count + 1;
        obj.Mode = mode;
        GetTree().CurrentScene.AddChild(obj);
        _active.Add(obj);
        obj.TreeExiting += () => _active.Remove(obj);
    }

    /// <summary>玩家靠近按 Q 划除时调用：触发反转 + 开启真空期。</summary>
    public void StrikeRule(RuleObject rule)
    {
        if (rule == null || !IsInstanceValid(rule)) return;
        _active.Remove(rule);
        rule.ApplyStrike();
        EmitSignal(SignalName.RuleStruck, rule);
        RunState.Instance?.RecordStrike();
        StartVacuum();
    }

    public void StartVacuum()
    {
        _vacuumRemaining = VacuumTime;
        if (!_isVacuum)
        {
            _isVacuum = true;
            EmitSignal(SignalName.VacuumStarted);
            PlaySFX("vacuum_start");
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        // 树被暂停（胜负对话框弹出）或当前不在游戏主场景（标题界面）时，
        // 彻底冻结：不生成技能宝珠，也不推进真空期倒计时。
        // —— 修复「标题界面 / 游戏结束时道具还在一直刷新」。
        if (GetTree().Paused || GetTree().GetFirstNodeInGroup("player") == null) return;

        float d = (float)delta;
        // 随机掉落技能宝珠
        _dropTimer -= d;
        if (_dropTimer <= 0f)
        {
            _dropTimer = SkillDropInterval;
            SpawnSkillDrop();
        }
        if (!_isVacuum) return;
        _vacuumRemaining -= (float)delta;
        if (_vacuumRemaining <= 0f)
        {
            _vacuumRemaining = 0f;
            _isVacuum = false;
            EmitSignal(SignalName.VacuumEnded);
            PlaySFX("vacuum_end");
        }
    }

    /// <summary>播放一个音效。id 见 _sfxPaths；未注册或资源缺失则静默忽略。</summary>
    public void PlaySFX(string id)
    {
        if (_sfx == null) return;
        if (_sfxStreams.TryGetValue(id, out var stream))
        {
            _sfx.Stream = stream;
            _sfx.Play();
        }
    }

    /// <summary>在地图随机位置投放一个「技能宝珠」拾取物（捡起后获得技能点）。</summary>
    private void SpawnSkillDrop()
    {
        var scene = GetTree().CurrentScene;
        if (scene == null) return;
        int idx = (int)GD.RandRange(0f, DropSpots.Length);
        var drop = new SkillDrop();
        drop.Position = DropSpots[idx];
        scene.AddChild(drop);
    }
}
