using Godot;
using System;

namespace BreakingRules;

[GlobalClass]
public partial class LevelDirector : Node2D
{
    private Player _player;
    private CanvasLayer _dialog;
    private Label _dlgTitle;
    private Label _dlgStats;
    private VBoxContainer _dlgButtons;
    private bool _ended;
    private bool _canNext;
    private CanvasLayer _pauseLayer;   // ESC 暂停对话框（独立层，与胜负对话框区分）
    private bool _paused;

    public override void _Ready()
    {
        // 暂停游戏时仍需接收 R / Enter 等按键与对话框按钮，故本节点设为 Always。
        ProcessMode = Node.ProcessModeEnum.Always;

        // 先于其余节点复位 Autoload（Boss/玩家随后 _Ready 才会登记）。
        // 注意：Reset() 只清姿态计数；通关数/当前关/历史最高跨场景保留。
        RunState.Instance?.Reset();
        RuleManager.Instance?.Reset();

        _player = GetParent()?.GetNode<Player>("Player");
        if (_player != null)
            _player.Connect(Player.SignalName.Died, Callable.From(OnPlayerDied));

        // 地形与 Boss 都延迟到场景树空闲时生成，避免父节点(World)仍在 setup 子节点
        // 而触发 "Parent node is busy setting up children"。
        CallDeferred(nameof(BuildTerrain));
        CallDeferred(nameof(SpawnBoss));
        BuildDialog();
        BuildPauseDialog();
        // 注意：BGM 由 RuleManager 在游戏启动即全局循环播放（跨场景续播、暂停也不断），
        // 此处不再单独启停。
    }

    private void BuildTerrain()
    {
        TerrainBuilder.Build(this);
    }

    /// <summary>按 RunState.CurrentStage 生成对应 BOSS（配置驱动），加到 World 并连接死亡信号。</summary>
    private void SpawnBoss()
    {
        int stage = RunState.Instance != null ? RunState.Instance.CurrentStage : 0;
        var cfg = BossRoster.GetConfig(stage);
        var scene = GD.Load<PackedScene>("res://Scenes/Boss.tscn");
        if (scene == null) return;
        var boss = scene.Instantiate<Boss>();
        boss.BossName = cfg.Name;
        boss.MaxHp = cfg.MaxHp;
        boss.SpawnInterval = cfg.SpawnInterval;
        boss.AttackInterval = cfg.AttackInterval;
        boss.HoverSpeed = cfg.HoverSpeed;
        boss.BaseDamage = cfg.BaseDamage;
        boss.BulletCount = cfg.BulletCount;
        boss.TexturePath = cfg.TexturePath;
        boss.Tint = cfg.Tint;
        boss.GlobalPosition = new Vector2(820f, 300f);
        GetParent()?.AddChild(boss);
        boss.Connect(Boss.SignalName.Died, Callable.From(OnBossDied));
    }

    private void OnPlayerDied()
    {
        if (!_ended) Lose();
    }

    private void OnBossDied()
    {
        if (!_ended) Win();
    }

    private void Win()
    {
        _ended = true;
        _canNext = true;
        RuleManager.Instance?.PlaySFX("win");

        var rs = RunState.Instance;
        rs?.RecordClear();

        int stage = rs != null ? rs.CurrentStage : 0;
        int next = stage + 1;
        string bossName = $"第{stage + 1}关 {BossRoster.GetConfig(stage).Name}";
        string title = $"规则崩坏 · {bossName} 倒下！";
        string nextLabel = next < BossRoster.Count ? "下一关  (Enter)" : "无尽模式  (Enter)";
        string stats = StatsLine(next >= BossRoster.Count);

        ShowDialog(title, stats,
            (nextLabel, new Action(OnNext)),
            ("重新开始  (R)", new Action(OnRestart)),
            ("返回标题  (T)", new Action(OnReturnToTitle)));

        GetTree().Paused = true; // 冻结全场：玩家/Boss/条文均停止
    }

    private void Lose()
    {
        _ended = true;
        _canNext = false;
        string stats = StatsLine(false);
        ShowDialog("被终审 · 失败", stats,
            ("重新开始  (R)", new Action(OnRestart)),
            ("返回标题  (T)", new Action(OnReturnToTitle)));
        GetTree().Paused = true;
    }

    private string StatsLine(bool endlessUnlocked)
    {
        var rs = RunState.Instance;
        int cur = rs != null ? rs.ClearCount : 0;
        int best = rs != null ? rs.BestClearCount : 0;
        string extra = endlessUnlocked ? "\n★ 最终 BOSS 已击败 — 无尽模式开启！" : "";
        return $"已通关 {cur} 关 · 历史最高 {best} 关{extra}";
    }

    // ---------- 对话框（按钮 + 快捷键） ----------
    private void BuildDialog()
    {
        _dialog = new CanvasLayer();
        _dialog.Layer = 20;
        _dialog.ProcessMode = Node.ProcessModeEnum.Always; // 暂停时对话框仍可交互
        AddChild(_dialog);

        var dim = new ColorRect();
        dim.Color = new Color(0f, 0f, 0f, 0.62f);
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        dim.MouseFilter = Control.MouseFilterEnum.Stop; // 吞掉点击，避免穿透到游戏
        _dialog.AddChild(dim);

        var panel = new Panel();
        panel.Position = new Vector2(210f, 96f);
        panel.Size = new Vector2(540f, 368f);
        var ps = new StyleBoxFlat();
        ps.BgColor = new Color(0.08f, 0.06f, 0.12f, 0.96f);
        ps.BorderColor = new Color(1f, 0.85f, 0.25f, 0.95f);
        ps.BorderWidthLeft = ps.BorderWidthTop = ps.BorderWidthRight = ps.BorderWidthBottom = 4;
        panel.AddThemeStyleboxOverride("panel", ps);
        panel.MouseFilter = Control.MouseFilterEnum.Stop;
        _dialog.AddChild(panel);

        var vbox = new VBoxContainer();
        // vbox 是 panel 的子节点：Position/Size 必须相对 panel（左上角原点）。
        // 之前写成屏幕绝对坐标(250,138)，导致整体被推到右下角、溢出面板。
        vbox.Position = new Vector2(30f, 30f);
        vbox.Size = new Vector2(480f, 308f);
        vbox.AddThemeConstantOverride("separation", 16);
        panel.AddChild(vbox);

        _dlgTitle = new Label();
        _dlgTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _dlgTitle.AddThemeFontSizeOverride("font_size", 26);
        _dlgTitle.Modulate = new Color(1f, 0.9f, 0.5f);
        _dlgTitle.AutowrapMode = TextServer.AutowrapMode.Off;
        vbox.AddChild(_dlgTitle);

        _dlgStats = new Label();
        _dlgStats.HorizontalAlignment = HorizontalAlignment.Center;
        _dlgStats.VerticalAlignment = VerticalAlignment.Center;
        _dlgStats.AddThemeFontSizeOverride("font_size", 18);
        _dlgStats.Modulate = new Color(0.9f, 0.9f, 0.95f);
        vbox.AddChild(_dlgStats);

        var spacer = new Control();
        spacer.Size = new Vector2(0f, 6f);
        vbox.AddChild(spacer);

        _dlgButtons = new VBoxContainer();
        _dlgButtons.AddThemeConstantOverride("separation", 12);
        _dlgButtons.Alignment = BoxContainer.AlignmentMode.Center;
        vbox.AddChild(_dlgButtons);

        _dialog.Visible = false;
    }

    // ---------- ESC 暂停（游戏中） ----------
    private void BuildPauseDialog()
    {
        _pauseLayer = new CanvasLayer();
        _pauseLayer.Layer = 25; // 高于游戏、低于胜负对话框(20) ；二者不会同时出现
        _pauseLayer.ProcessMode = Node.ProcessModeEnum.Always; // 暂停时仍可交互
        AddChild(_pauseLayer);

        var dim = new ColorRect();
        dim.Color = new Color(0f, 0f, 0f, 0.62f);
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        dim.MouseFilter = Control.MouseFilterEnum.Stop; // 吞掉点击，避免穿透到游戏
        _pauseLayer.AddChild(dim);

        var panel = new Panel();
        panel.Position = new Vector2(300f, 145f);  // 垂直居中：(540-250)/2
        panel.Size = new Vector2(360f, 250f);      // 加高以容纳标题 + 两个按钮，避免第二按钮溢出底部
        var ps = new StyleBoxFlat();
        ps.BgColor = new Color(0.08f, 0.06f, 0.12f, 0.96f);
        ps.BorderColor = new Color(1f, 0.85f, 0.25f, 0.95f);
        ps.BorderWidthLeft = ps.BorderWidthTop = ps.BorderWidthRight = ps.BorderWidthBottom = 4;
        panel.AddThemeStyleboxOverride("panel", ps);
        panel.MouseFilter = Control.MouseFilterEnum.Stop;
        _pauseLayer.AddChild(panel);

        var vbox = new VBoxContainer();
        // vbox 相对 panel（左上角原点）
        vbox.Position = new Vector2(50f, 40f);
        vbox.Size = new Vector2(260f, 170f);   // 高于内容最小高度(~165)，留足底部余量
        vbox.AddThemeConstantOverride("separation", 16);
        panel.AddChild(vbox);

        var title = new Label();
        title.Text = "暂停";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 28);
        title.Modulate = new Color(1f, 0.9f, 0.5f);
        vbox.AddChild(title);

        var resume = new Button();
        resume.Text = "返回游戏  (Esc)";
        resume.CustomMinimumSize = new Vector2(260f, 46f);
        resume.AddThemeFontSizeOverride("font_size", 18);
        resume.Alignment = HorizontalAlignment.Center;
        resume.ProcessMode = Node.ProcessModeEnum.Always; // 暂停时按钮可点
        resume.FocusMode = Control.FocusModeEnum.None;     // 键盘不自动激活按钮，ESC 仍可触发
        resume.Pressed += ResumeGame;
        WireButtonSfx(resume);
        vbox.AddChild(resume);

        var toTitle = new Button();
        toTitle.Text = "返回标题";
        toTitle.CustomMinimumSize = new Vector2(260f, 46f);
        toTitle.AddThemeFontSizeOverride("font_size", 18);
        toTitle.Alignment = HorizontalAlignment.Center;
        toTitle.ProcessMode = Node.ProcessModeEnum.Always;
        toTitle.FocusMode = Control.FocusModeEnum.None;
        toTitle.Pressed += OnReturnToTitle;
        WireButtonSfx(toTitle);
        vbox.AddChild(toTitle);

        _pauseLayer.Visible = false;
    }

    /// <summary>给按钮接上悬停 / 点击提示音（触碰播放 ui_hover，按下播放 ui_click）。</summary>
    private static void WireButtonSfx(Button btn)
    {
        btn.MouseEntered += () => RuleManager.Instance?.PlaySFX("ui_hover");
        btn.Pressed += () => RuleManager.Instance?.PlaySFX("ui_click");
    }

    private void PauseGame()
    {
        _paused = true;
        GetTree().Paused = true; // 冻结游戏（BGM 因 Always 仍播放）
        _pauseLayer.Visible = true;
    }

    private void ResumeGame()
    {
        _paused = false;
        GetTree().Paused = false;
        _pauseLayer.Visible = false;
    }

    private void ShowDialog(string title, string stats, params (string label, Action action)[] buttons)
    {
        _dlgTitle.Text = title;
        _dlgStats.Text = stats;
        foreach (Node child in _dlgButtons.GetChildren())
            child.QueueFree();
        foreach (var (label, action) in buttons)
        {
            var act = action; // 复制局部变量，避免闭包共享
            var btn = new Button();
            btn.Text = label;
            btn.CustomMinimumSize = new Vector2(300f, 46f);
            btn.AddThemeFontSizeOverride("font_size", 18);
            btn.Alignment = HorizontalAlignment.Center;
            btn.ProcessMode = Node.ProcessModeEnum.Always; // 暂停时按钮可点
            btn.FocusMode = Control.FocusModeEnum.None;     // 键盘不自动激活按钮，避免与下方快捷键重复触发
            btn.Pressed += () => act();
            WireButtonSfx(btn);
            _dlgButtons.AddChild(btn);
        }
        _dialog.Visible = true;
    }

    private void OnNext()
    {
        if (!_ended) return;
        _ended = false;
        // 跨关继承：先暂存当前玩家生命与技能点，重载后 Player._Ready 据此恢复
        // （不回复生命、不清空技能点；重新开始 / 新开局才会清零）
        if (_player != null && RunState.Instance != null)
        {
            RunState.Instance.CarryHp = _player.Hp;
            RunState.Instance.CarrySkill = _player.SkillPoints;
            RunState.Instance.Carry = true;
        }
        if (RunState.Instance != null) RunState.Instance.CurrentStage++;
        GetTree().Paused = false; // 重开/下一关前必须解除暂停
        GetTree().ReloadCurrentScene();
    }

    private void OnRestart()
    {
        if (!_ended) return;
        _ended = false;
        RunState.Instance?.StartNewRun();
        GetTree().Paused = false;
        GetTree().ReloadCurrentScene();
    }

    /// <summary>返回标题界面：先解除暂停（ESC 暂停或胜负暂停均可能处于 Paused），
    /// 否则标题界面会继承暂停状态。BGM 为全局循环，不在此停止。</summary>
    private void OnReturnToTitle()
    {
        _ended = false;
        _paused = false;
        if (_pauseLayer != null) _pauseLayer.Visible = false;
        GetTree().Paused = false;
        GetTree().ChangeSceneToFile("res://Scenes/Title.tscn");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            // 游戏中按 ESC：暂停；再按 ESC 默认返回游戏
            if (key.Keycode == Key.Escape)
            {
                if (!_ended)
                {
                    if (_paused) ResumeGame();
                    else PauseGame();
                }
                return; // ESC 不触发下方胜负快捷键
            }

            if (!_ended) return;
            if (key.Keycode == Key.R)
                OnRestart();
            else if ((key.Keycode == Key.Enter || key.Keycode == Key.KpEnter || key.Keycode == Key.N) && _canNext)
                OnNext();
            else if (key.Keycode == Key.T)
                OnReturnToTitle();
        }
    }
}
