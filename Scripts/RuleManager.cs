using Godot;
using System.Collections.Generic;
using System.Linq;

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
        { "jump",         "res://Assets/Sounds/jump-a.ogg" },     // 玩家跳跃
        { "attack",       "res://Assets/Sounds/shoot-b.ogg" },    // 玩家普攻挥砍
        { "player_hurt",  "res://Assets/Sounds/hurt-a.ogg" },     // 玩家受击（BOSS 命中）
        { "pickup",       "res://Assets/Sounds/coin-a.ogg" },     // 地图增益拾取（加速/跳/回血）
        { "orb",          "res://Assets/Sounds/coin-b.ogg" },     // 技能宝珠拾取
        { "bullet",       "res://Assets/Sounds/shoot-c.ogg" },    // BOSS 弹幕发射
        { "cancel",       "res://Assets/Sounds/error-a.ogg" },    // 划除蓄力取消
        { "ui_hover",     "res://Assets/Sounds/select-a.ogg" },   // 按钮悬停（触碰）
        { "ui_click",     "res://Assets/Sounds/coin-c.ogg" },     // 按钮点击（确认）
    };
    private readonly Dictionary<string, AudioStream> _sfxStreams = new();

    // ---- BGM：独立的循环播放器（不希望被 SFX 打断）。全局唯一一首，始终循环：
    //      ProcessMode.Always → 树暂停（胜负对话框 / ESC 暂停）时仍持续播放；
    //      跨场景（标题↔战斗）自动续播，任何场景都不主动停止。 ----
    private AudioStreamPlayer _bgm;
    private AudioStream _bgmStream;
    private const string BgmPath = "res://Assets/Sounds/bgm1.mp3";

    public override void _EnterTree()
    {
        Instance = this;
        _sfx = new AudioStreamPlayer();
        _sfx.ProcessMode = Node.ProcessModeEnum.Always; // 暂停游戏时胜利音效仍要播完
        AddChild(_sfx);
        _bgm = new AudioStreamPlayer();
        // 全局循环：设为 Always，即使树暂停（胜负对话框 / ESC 暂停）也持续播放；
        // 且仅此一首 BGM，不在任何场景停止，跨场景（标题↔战斗）自动续播。
        _bgm.ProcessMode = Node.ProcessModeEnum.Always;
        AddChild(_bgm);
        _bgmStream = GD.Load<AudioStream>(BgmPath);
        if (_bgmStream != null)
        {
            _bgm.Stream = _bgmStream;
            if (_bgmStream is AudioStreamMP3 mp3) mp3.Loop = true; // 循环播放
            StartBGM(); // 游戏启动即开始全局循环（Autoload 只初始化一次）
        }
        foreach (var kv in _sfxPaths)
        {
            var s = GD.Load<AudioStream>(kv.Value);
            if (s != null) _sfxStreams[kv.Key] = s;
        }
        Settings.Load();        // 读取上次保存的分辨率 / 音量
        Settings.ApplyVolume(); // 让 BGM 从第一帧起就遵循用户音量
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

    /// <summary>玩家是否处于某类规则区域内（用于「反诉」卡：玩家在禁攻/限速区内时 BOSS 也受限制）。</summary>
    public bool PlayerInMode(RuleMode m)
    {
        var p = GetTree().GetFirstNodeInGroup("player") as Node2D;
        if (p == null) return false;
        foreach (var r in _active)
            if (IsInstanceValid(r) && r.Mode == m && r.Contains(p.GlobalPosition))
                return true;
        return false;
    }

    /// <summary>清除场上所有规则条文（无罪推定卡：累计防御 10 次触发）。</summary>
    public void ClearAllRules()
    {
        foreach (var r in _active.ToArray())
            if (IsInstanceValid(r)) r.QueueFree();
        _active.Clear();
    }

    public float AttackMult => _isVacuum ? AttackMultVacuum : 1f;
    public float SpeedMult => _isVacuum ? SpeedMultVacuum : 1f;

    /// <summary>场上是否存在【左右反转】全图规则（用于翻转玩家左右输入）。</summary>
    public bool Inverted => _active.Any(r => IsInstanceValid(r) && r.Mode == RuleMode.Invert);

    /// <summary>Boss 调用：在 anchor 处生成一条指定类型的「规则条文」（场上已达上限则忽略）。
    /// isGlobal=全图规则（约束全图生效）；follow=区域跟随玩家（约 1s 延迟）。区域尺寸随机。</summary>
    public void SpawnRule(RuleMode mode, Vector2 anchor, bool isGlobal = false, bool follow = false)
    {
        if (_active.Count >= MaxActiveRules) return;
        var scene = GD.Load<PackedScene>("res://Scenes/RuleObject.tscn");
        if (scene == null) return;
        var obj = scene.Instantiate<RuleObject>();
        obj.GlobalPosition = anchor;
        obj.RuleIndex = _active.Count + 1;
        obj.Mode = mode;
        // 区域大小动态变化（合理范围：比基准 160x90 可大可小）
        float w = (float)GD.RandRange(110f, 260f);
        float h = (float)GD.RandRange(72f, 150f);
        obj.ZoneSize = new Vector2(w, h);
        obj.IsGlobal = isGlobal;
        obj.Follow = follow;
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

    /// <summary>开始循环播放 BGM（全局唯一一首，跨场景续播）。已在播放则跳过，避免重复叠加。</summary>
    public void StartBGM()
    {
        if (_bgm == null || _bgmStream == null) return;
        if (_bgm.Playing) return;
        _bgm.Play();
    }

    /// <summary>暂停 BGM（过场对话期间调用）。用 StreamPaused，恢复后从暂停处续播。</summary>
    public void PauseBGM()
    {
        if (_bgm != null && _bgm.Playing) _bgm.StreamPaused = true;
    }

    /// <summary>恢复 BGM（过场对话结束调用）。</summary>
    public void ResumeBGM()
    {
        if (_bgm != null && _bgm.Playing) _bgm.StreamPaused = false;
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
