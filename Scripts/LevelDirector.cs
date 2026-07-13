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
            _dlgButtons.AddChild(btn);
        }
        _dialog.Visible = true;
    }

    private void OnNext()
    {
        if (!_ended) return;
        _ended = false;
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

    /// <summary>返回标题界面：必须先把暂停解除，否则标题界面会继承暂停状态。</summary>
    private void OnReturnToTitle()
    {
        if (!_ended) return;
        _ended = false;
        GetTree().Paused = false;
        GetTree().ChangeSceneToFile("res://Scenes/Title.tscn");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_ended) return;
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;
        if (key.Keycode == Key.R)
            OnRestart();
        else if ((key.Keycode == Key.Enter || key.Keycode == Key.KpEnter || key.Keycode == Key.N) && _canNext)
            OnNext();
        else if (key.Keycode == Key.T)
            OnReturnToTitle();
    }
}
