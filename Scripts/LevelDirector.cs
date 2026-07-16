using Godot;
using System;
using System.Linq;

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
    private bool _cardPicking;          // 卡牌选择浮层是否打开（打开时屏蔽 ESC 等输入）
    private CanvasLayer _cardLayer;
    private Label _cardTitle;
    private ScrollContainer _cardScroll;   // 卡片列表可滚动容器（换卡 5 张时出滚动条）
    private VBoxContainer _cardBody;
    private HBoxContainer _cardActions;     // 底部操作区：跳过 / 确认 / 取消
    private string _swapNewId;              // 换卡待加入的新卡
    private string _selectedOldId;          // 换卡当前选中的旧卡（null=未选）
    private CanvasLayer _cardViewLayer; // ESC 暂停内的「查看卡牌」窗口（列出已选卡，特赦令已用则灰）
    private bool _cardViewOpen;

    // ---------- 过场对话（AVG 风格） ----------
    private CanvasLayer _dlgLayer;          // 对话浮层，null = 未激活
    private Label _dlgSpeaker;              // 说话者名
    private Label _dlgText;                 // 正文（打字机）
    private bool _dlgActive;                // 对话进行中（拦截输入 / 驱动打字机）
    private System.Collections.Generic.List<(string speaker, string text)> _dlgLines;
    private int _dlgIndex;
    private System.Action _dlgOnDone;       // 对话正常结束（最后一行后）的回调
    private bool _dlgTyping;                // 当前行是否正在打字
    private string _dlgFull;                // 当前行完整文本
    private int _dlgShown;                  // 已显示字符数
    private const float DlgCps = 46f;      // 打字机速度（字符/秒）

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

        // 关卡开场：第 1/2/3 关先放前置过场对话，结束后再按需弹出卡牌浮层并开战。
        CallDeferred(nameof(StartLevelIntro));
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
        if (_ended) return;
        _ended = true;   // 先锁，避免演出期间 ESC / 重入触发暂停
        // 慢动作演出期间玩家仍可控：临时无敌，避免此时违规（撞入规则区）掉血造成混乱。
        _player?.SetDeathInvuln(true);
        // 死亡演出：放慢游戏速度 + 镜头拉近 BOSS，结束后再弹结算（不直接暂停）。
        // 用 Engine.TimeScale 做慢动作（非 Paused），故 Tween / 浮字 / 镜头都随之慢放，更有戏剧感。
        Engine.TimeScale = 0.35f;
        var boss = GetTree().GetFirstNodeInGroup("boss") as Boss;
        Vector2 bossPos = boss != null && IsInstanceValid(boss) ? boss.GlobalPosition : new Vector2(480f, 270f);
        Juice.Instance?.BossDeathCinematic(bossPos, () =>
        {
            Engine.TimeScale = 1.0f;   // 还原时间缩放（须在 Paused 前）
            _player?.SetDeathInvuln(false);  // 演出结束：解除临时无敌（随后 Win 会暂停全场）
            int stage = RunState.Instance != null ? RunState.Instance.CurrentStage : 0;
            // 第 5 关（CurrentStage==4）击败终裁者：先放结局升华对话（暂停 BGM），再弹结算窗。
            if (stage == 4)
            {
                var lines = GetPostDialogue();
                if (lines != null) ShowDialogue(lines, Win);
                else Win();
            }
            else
            {
                Win();
            }
        });
    }

    private void Win()
    {
        Juice.Instance?.CancelHitStop();   // 作废进行中的顿帧，避免误解除本永久暂停
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
        Juice.Instance?.CancelHitStop();   // 作废进行中的顿帧，避免误解除本永久暂停
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
        panel.Position = new Vector2(300f, 120f);  // 垂直居中：(540-300)/2
        panel.Size = new Vector2(360f, 300f);      // 容纳标题 + 三个按钮（返回游戏/返回标题/查看卡牌）
        var ps = new StyleBoxFlat();
        ps.BgColor = new Color(0.08f, 0.06f, 0.12f, 0.96f);
        ps.BorderColor = new Color(1f, 0.85f, 0.25f, 0.95f);
        ps.BorderWidthLeft = ps.BorderWidthTop = ps.BorderWidthRight = ps.BorderWidthBottom = 4;
        panel.AddThemeStyleboxOverride("panel", ps);
        panel.MouseFilter = Control.MouseFilterEnum.Stop;
        _pauseLayer.AddChild(panel);

        var vbox = new VBoxContainer();
        // vbox 相对 panel（左上角原点）
        vbox.Position = new Vector2(50f, 36f);
        vbox.Size = new Vector2(260f, 228f);   // 高于内容最小高度(~214)，留足底部余量
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

        var viewCards = new Button();
        viewCards.Text = "查看卡牌";
        viewCards.CustomMinimumSize = new Vector2(260f, 46f);
        viewCards.AddThemeFontSizeOverride("font_size", 18);
        viewCards.Alignment = HorizontalAlignment.Center;
        viewCards.ProcessMode = Node.ProcessModeEnum.Always;
        viewCards.FocusMode = Control.FocusModeEnum.None;
        viewCards.Pressed += OpenCardView;
        WireButtonSfx(viewCards);
        vbox.AddChild(viewCards);

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
        Juice.Instance?.CancelHitStop();   // 作废进行中的顿帧，避免 ESC 暂停期间误解除
        _paused = true;
        GetTree().Paused = true; // 冻结游戏（BGM 因 Always 仍播放）
        _pauseLayer.Visible = true;
    }

    private void ResumeGame()
    {
        Juice.Instance?.CancelHitStop();   // 清理可能残留的顿帧计时
        _paused = false;
        GetTree().Paused = false;
        _pauseLayer.Visible = false;
        if (_cardViewOpen) CloseCardView();   // 顺手关掉卡牌窗口，避免残留
    }

    // ---------- ESC 暂停内的「查看卡牌」窗口 ----------
    private void OpenCardView()
    {
        if (RunState.Instance == null) return;
        CloseCardView();   // 确保单个实例
        _cardViewOpen = true;
        _cardViewLayer = new CanvasLayer();
        _cardViewLayer.Layer = 35;    // 高于 ESC 暂停(25)
        _cardViewLayer.ProcessMode = Node.ProcessModeEnum.Always; // 暂停时窗口可交互
        AddChild(_cardViewLayer);

        var dim = new ColorRect();
        dim.Color = new Color(0f, 0f, 0f, 0.55f);
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        dim.MouseFilter = Control.MouseFilterEnum.Stop;
        _cardViewLayer.AddChild(dim);

        var panel = new Panel();
        panel.Position = new Vector2(200f, 60f);
        panel.Size = new Vector2(560f, 420f);
        var ps = new StyleBoxFlat();
        ps.BgColor = new Color(0.08f, 0.06f, 0.12f, 0.97f);
        ps.BorderColor = new Color(1f, 0.85f, 0.25f, 0.95f);
        ps.BorderWidthLeft = ps.BorderWidthTop = ps.BorderWidthRight = ps.BorderWidthBottom = 4;
        panel.AddThemeStyleboxOverride("panel", ps);
        panel.MouseFilter = Control.MouseFilterEnum.Stop;
        _cardViewLayer.AddChild(panel);

        var title = new Label();
        title.Text = "已选卡牌";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 24);
        title.Modulate = new Color(1f, 0.9f, 0.5f);
        title.Position = new Vector2(0f, 14f);
        title.Size = new Vector2(560f, 32f);
        panel.AddChild(title);

        var body = new VBoxContainer();
        body.Position = new Vector2(30f, 56f);
        body.Size = new Vector2(500f, 320f);
        body.AddThemeConstantOverride("separation", 8);
        panel.AddChild(body);

        if (RunState.Instance.OwnedCards.Count == 0)
        {
            var empty = new Label();
            empty.Text = "（暂无卡牌）";
            empty.HorizontalAlignment = HorizontalAlignment.Center;
            empty.AddThemeFontSizeOverride("font_size", 18);
            empty.Modulate = Colors.Gray;
            body.AddChild(empty);
        }
        foreach (var id in RunState.Instance.OwnedCards)
        {
            var card = CardDefs.Get(id).Value;
            bool spent = (id == "pardon" && _player != null && _player.PardonUsed);
            var row = new VBoxContainer();
            row.AddThemeConstantOverride("separation", 2);
            var n = new Label();
            n.Text = spent ? $"{card.name}（已使用）" : card.name;
            n.AddThemeFontSizeOverride("font_size", 18);
            n.HorizontalAlignment = HorizontalAlignment.Center;
            n.Modulate = spent ? Colors.Gray : card.color;
            var d = new Label();
            d.Text = card.desc;
            d.AddThemeFontSizeOverride("font_size", 13);
            d.AutowrapMode = TextServer.AutowrapMode.Word;
            d.HorizontalAlignment = HorizontalAlignment.Center;
            d.CustomMinimumSize = new Vector2(460f, 30f);
            d.Modulate = spent ? Colors.Gray : new Color(0.9f, 0.9f, 0.95f);
            row.AddChild(n);
            row.AddChild(d);
            body.AddChild(row);
        }

        var close = new Button();
        close.Text = "关闭  (Esc)";
        close.CustomMinimumSize = new Vector2(200f, 40f);
        close.Position = new Vector2(180f, 372f);
        close.Alignment = HorizontalAlignment.Center;
        close.ProcessMode = Node.ProcessModeEnum.Always;
        close.FocusMode = Control.FocusModeEnum.None;
        close.Pressed += CloseCardView;
        WireButtonSfx(close);
        panel.AddChild(close);
    }

    private void CloseCardView()
    {
        _cardViewOpen = false;
        _cardViewLayer?.QueueFree();
        _cardViewLayer = null;
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
        Juice.Instance?.CancelHitStop();   // 清理可能残留的顿帧计时
        if (!_ended) return;
        _ended = false;
        // 跨关继承：先暂存当前玩家生命与技能点，重载后 Player._Ready 据此恢复
        // （不回复生命、不清空技能点；重新开始 / 新开局才会清零）
        if (_player != null && RunState.Instance != null)
        {
            RunState.Instance.CarryHp = _player.Hp;
            RunState.Instance.CarrySkill = _player.SkillPoints;
            RunState.Instance.CarryEnergy = _player.Energy;     // 能量条跨关继承
            RunState.Instance.CarryUlt = _player.SelectedUlt;  // 所选大招跨关继承
            RunState.Instance.Carry = true;
        }
        if (RunState.Instance != null) RunState.Instance.CurrentStage++;
        GetTree().Paused = false; // 重开/下一关前必须解除暂停
        GetTree().ReloadCurrentScene();
    }

    private void OnRestart()
    {
        Juice.Instance?.CancelHitStop();   // 清理可能残留的顿帧计时
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
        Juice.Instance?.CancelHitStop();   // 作废进行中的顿帧，切场景前清理
        _ended = false;
        _paused = false;
        if (_cardViewOpen) CloseCardView();
        if (_pauseLayer != null) _pauseLayer.Visible = false;
        GetTree().Paused = false;
        GetTree().ChangeSceneToFile("res://Scenes/Title.tscn");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // 过场对话进行中：空格/回车 继续一行，Esc 直接跳过整段对话。
        if (_dlgActive)
        {
            if (@event is InputEventKey k && k.Pressed && !k.Echo)
            {
                if (k.Keycode == Key.Escape) SkipDialogue();
                else if (k.Keycode == Key.Space || k.Keycode == Key.Enter || k.Keycode == Key.KpEnter) AdvanceDialogue();
            }
            return;
        }
        if (_cardPicking) return;   // 卡牌选择期间屏蔽一切按键（ESC/胜负快捷键）
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            // 游戏中按 ESC：暂停；再按 ESC 默认返回游戏
            if (key.Keycode == Key.Escape)
            {
                if (!_ended)
                {
                    if (_cardViewOpen) CloseCardView();   // 卡牌查看窗口优先关闭（回到暂停菜单）
                    else if (_paused) ResumeGame();
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

    // ---------- 卡牌选择浮层（第 3 关起每关开局） ----------
    private void ShowCardPicker()
    {
        Juice.Instance?.CancelHitStop();   // 作废进行中的顿帧，避免开局选牌暂停期间误解除
        if (RunState.Instance == null) return;
        _cardPicking = true;
        GetTree().Paused = true;   // 开局暂停，玩家选牌
        BuildCardLayer();
        ShowThreeChoices();
    }

    private void BuildCardLayer()
    {
        _cardLayer?.QueueFree();
        _cardLayer = new CanvasLayer();
        _cardLayer.Layer = 30;    // 高于胜负对话框(20)与 ESC 暂停(25)
        _cardLayer.ProcessMode = Node.ProcessModeEnum.Always; // 暂停时浮层可交互
        AddChild(_cardLayer);

        var dim = new ColorRect();
        dim.Color = new Color(0f, 0f, 0f, 0.72f);
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        dim.MouseFilter = Control.MouseFilterEnum.Stop;
        _cardLayer.AddChild(dim);

        var panel = new Panel();
        panel.Position = new Vector2(120f, 50f);
        panel.Size = new Vector2(720f, 460f);
        var ps = new StyleBoxFlat();
        ps.BgColor = new Color(0.08f, 0.06f, 0.12f, 0.97f);
        ps.BorderColor = new Color(1f, 0.85f, 0.25f, 0.95f);
        ps.BorderWidthLeft = ps.BorderWidthTop = ps.BorderWidthRight = ps.BorderWidthBottom = 4;
        panel.AddThemeStyleboxOverride("panel", ps);
        panel.MouseFilter = Control.MouseFilterEnum.Stop;
        _cardLayer.AddChild(panel);

        _cardTitle = new Label();
        _cardTitle.Position = new Vector2(0f, 14f);
        _cardTitle.Size = new Vector2(720f, 30f);
        _cardTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _cardTitle.AddThemeFontSizeOverride("font_size", 22);
        _cardTitle.Modulate = new Color(1f, 0.9f, 0.5f);
        panel.AddChild(_cardTitle);

        // 可滚动卡片区：高度受限，卡片过多（换卡 5 张）时自动出现纵向滚动条
        _cardScroll = new ScrollContainer();
        _cardScroll.Position = new Vector2(40f, 56f);
        _cardScroll.Size = new Vector2(640f, 312f);   // 高度足够完整显示 3 张卡（不滚）；换卡 5 张时自动出滚动条
        _cardScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        _cardScroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
        panel.AddChild(_cardScroll);

        _cardBody = new VBoxContainer();
        _cardBody.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _cardBody.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        _cardBody.AddThemeConstantOverride("separation", 12);
        _cardScroll.AddChild(_cardBody);

        // 底部操作区（跳过 / 确认 / 取消），居中排布
        _cardActions = new HBoxContainer();
        _cardActions.Position = new Vector2(40f, 380f);
        _cardActions.Size = new Vector2(640f, 64f);
        _cardActions.AddThemeConstantOverride("separation", 24);
        _cardActions.Alignment = BoxContainer.AlignmentMode.Center;
        panel.AddChild(_cardActions);
    }

    private void ShowThreeChoices()
    {
        _cardTitle.Text = $"选择一张卡牌（第{RunState.Instance.CurrentStage + 1}关）";
        ClearCardBody();
        ClearActions();
        var pool = new System.Collections.Generic.List<string>();
        foreach (var card in CardDefs.All)
            if (!RunState.Instance.OwnedCards.Contains(card.id)) pool.Add(card.id);
        var picks = CardDefs.DrawDistinct(pool, 3);
        foreach (var id in picks)
        {
            var card = CardDefs.Get(id).Value;
            _cardBody.AddChild(MakeCardButton(card.name, card.desc, card.color, () => OnPickCard(id)));
        }
        // 选满 5 张后再次开选卡界面时，提供【跳过】按钮（保留当前 5 张，不抽第 6 张）
        if (RunState.Instance.OwnedCards.Count >= RunState.MaxCards)
            AddActionButton("跳过（保留当前 5 张）", () => CloseCardPicker(), false);
    }

    // 满 5 张时选第 6 张：进入替换步骤（先点选旧卡，再按【确认】才生效；【取消】=保留当前 5 张）
    private void ShowSwapStep(string newId)
    {
        _swapNewId = newId;
        _selectedOldId = null;
        RenderSwap();
    }

    private void RenderSwap()
    {
        var card = CardDefs.Get(_swapNewId).Value;
        _cardTitle.Text = $"已满 5 张：选择一张替换（{card.name} 将加入，点选后按【确认】）";
        ClearCardBody();
        foreach (var oldId in RunState.Instance.OwnedCards)
        {
            var oc = CardDefs.Get(oldId).Value;
            bool sel = oldId == _selectedOldId;
            _cardBody.AddChild(MakeCardButton(oc.name, oc.desc, oc.color,
                () => { _selectedOldId = oldId; RenderSwap(); }, sel));
        }
        ClearActions();
        AddActionButton("确认替换", () => OnConfirmSwap(), true);
        AddActionButton("取消", () => CloseCardPicker(), false);
    }

    private void OnConfirmSwap()
    {
        if (RunState.Instance == null || _selectedOldId == null) return;  // 未选旧卡则无效
        RunState.Instance.RemoveCard(_selectedOldId);   // 换掉的卡回到卡池（下次抽取可能出现）
        RunState.Instance.AddCard(_swapNewId);
        CloseCardPicker();
    }

    private void ClearCardBody()
    {
        foreach (Node c in _cardBody.GetChildren()) c.QueueFree();
    }

    private void ClearActions()
    {
        foreach (Node c in _cardActions.GetChildren()) c.QueueFree();
    }

    private void AddActionButton(string label, System.Action act, bool emphasize)
    {
        var btn = new Button();
        btn.Text = label;
        btn.CustomMinimumSize = new Vector2(220f, 48f);
        btn.AddThemeFontSizeOverride("font_size", 18);
        btn.Alignment = HorizontalAlignment.Center;
        btn.ProcessMode = Node.ProcessModeEnum.Always;
        btn.FocusMode = Control.FocusModeEnum.None;   // 键盘不抢焦点
        if (emphasize)
        {
            var ps = new StyleBoxFlat();
            ps.BgColor = new Color(0.30f, 0.20f, 0.10f, 0.95f);
            ps.BorderColor = Colors.Gold;
            ps.BorderWidthLeft = ps.BorderWidthTop = ps.BorderWidthRight = ps.BorderWidthBottom = 3;
            btn.AddThemeStyleboxOverride("normal", ps);
            btn.AddThemeColorOverride("font_color", Colors.Gold);
        }
        btn.Pressed += () => act();
        WireButtonSfx(btn);
        _cardActions.AddChild(btn);
    }

    private Button MakeCardButton(string name, string desc, Color color, System.Action act, bool selected = false)
    {
        var btn = new Button();
        btn.Text = "";
        btn.CustomMinimumSize = new Vector2(640f, 92f);
        btn.AddThemeFontSizeOverride("font_size", 18);
        btn.Alignment = HorizontalAlignment.Center;
        btn.ProcessMode = Node.ProcessModeEnum.Always;
        btn.FocusMode = Control.FocusModeEnum.None;   // 键盘不抢焦点
        if (selected)
        {
            // 选中态高亮：金色边框 + 暖色底，直观区分已选卡片
            var ps = new StyleBoxFlat();
            ps.BgColor = new Color(0.25f, 0.18f, 0.05f, 0.6f);
            ps.BorderColor = Colors.Gold;
            ps.BorderWidthLeft = ps.BorderWidthTop = ps.BorderWidthRight = ps.BorderWidthBottom = 4;
            btn.AddThemeStyleboxOverride("normal", ps);
            btn.AddThemeStyleboxOverride("hover", ps);
            btn.AddThemeStyleboxOverride("pressed", ps);
        }
        var vb = new VBoxContainer();
        vb.AddThemeConstantOverride("separation", 4);
        var n = new Label();
        n.Text = name; n.AddThemeFontSizeOverride("font_size", 20);
        n.Modulate = color; n.HorizontalAlignment = HorizontalAlignment.Center;
        var d = new Label();
        d.Text = desc; d.AddThemeFontSizeOverride("font_size", 15);
        d.AutowrapMode = TextServer.AutowrapMode.Word;
        d.HorizontalAlignment = HorizontalAlignment.Center;
        d.CustomMinimumSize = new Vector2(600f, 44f);
        vb.AddChild(n); vb.AddChild(d);
        btn.AddChild(vb);
        btn.Pressed += () => act();
        WireButtonSfx(btn);
        return btn;
    }

    private void OnPickCard(string id)
    {
        if (RunState.Instance == null) return;
        if (RunState.Instance.OwnedCards.Count < RunState.MaxCards)
        {
            RunState.Instance.AddCard(id);
            CloseCardPicker();
        }
        else
        {
            ShowSwapStep(id);   // 已满 5 张 → 进入替换步骤
        }
    }

    private void CloseCardPicker()
    {
        _cardPicking = false;
        GetTree().Paused = false;
        _cardLayer?.QueueFree();
        _cardLayer = null;
    }

    // ---------- 过场对话（AVG 风格） ----------
    /// <summary>关卡开场引导：第 1/2/3 关先放前置对话，结束后再按需弹卡牌浮层并开战。</summary>
    private void StartLevelIntro()
    {
        int stage = RunState.Instance != null ? RunState.Instance.CurrentStage : 0;
        var lines = GetPreDialogue(stage);
        if (lines != null) ShowDialogue(lines, AfterPreDialogue);
        else AfterPreDialogue();
    }

    private void AfterPreDialogue()
    {
        int stage = RunState.Instance != null ? RunState.Instance.CurrentStage : 0;
        if (stage >= 2) ShowCardPicker();   // 第 3 关起每关开局选卡
    }

    /// <summary>第 1/2/3 关开战前的前置对话（背景 / 提醒 / 卡牌系统觉醒）；其余关返回 null。</summary>
    private System.Collections.Generic.List<(string, string)> GetPreDialogue(int stage)
    {
        if (stage == 0)  // 第 1 关：背景介绍 + 主角目标
            return new System.Collections.Generic.List<(string, string)>
            {
                ("旁白",  "「规则之塔」——一座由层层法条堆砌的牢笼。你被囚禁在最底层，无人记得你的罪名。"),
                ("囚徒",  "他们说，规则就是秩序。可我的审判，从头到尾都不曾公正。"),
                ("旁白",  "塔有九十九层，每层都有一位法官把守。初审官，是其中最微不足道的一个。"),
                ("囚徒",  "但再微不足道，也是一道门。打破它，我便能向上。"),
                ("旁白",  "……那么，开始你的第一次打破吧。"),
            };
        if (stage == 1)  // 第 2 关：BOSS 增强 + 规则可能遍布全图
            return new System.Collections.Generic.List<(string, string)>
            {
                ("旁白",  "第二层。守门的法官，似乎比初审官更熟悉「力量」的意味。"),
                ("囚徒",  "他的条文……比之前更密了。"),
                ("旁白",  "警告：此后颁布的规则可能遍布全场（标注【全图】），甚至尾随你移动（标注【跟随】）。"),
                ("囚徒",  "无孔不入么。那我就划得更快。"),
                ("旁白",  "小心——真空期短暂，别在规则的缝隙间犹豫。"),
            };
        if (stage == 2)  // 第 3 关：外部力量（卡牌系统）觉醒
            return new System.Collections.Generic.List<(string, string)>
            {
                ("旁白",  "第三层。某种「外力」开始渗入这座塔。"),
                ("？？？", "囚徒。你听见了吗？是规则在尖叫。"),
                ("？？？", "我们是被塔吞没的「前例」，如今化作了卡牌。拿走我们，你便能改写审判。"),
                ("囚徒",  "卡牌……可我凭什么相信，你们不是另一种牢笼？"),
                ("？？？", "信不信由你。但每破一层，便有一张落入你手中。"),
                ("旁白",  "（此后每通过一关，可从三张卡中择一；满五张则需替换。流派，由你定义。）"),
            };
        return null;
    }

    /// <summary>第 5 关（CurrentStage==4）击败终裁者后的结局升华对话（含无尽模式介绍）。</summary>
    private System.Collections.Generic.List<(string, string)> GetPostDialogue()
    {
        return new System.Collections.Generic.List<(string, string)>
        {
            ("旁白",  "终裁者的身影崩解成无数碎片，规则的回响终于沉寂。"),
            ("囚徒",  "原来站在最高处的，也不过是个写规则的人。"),
            ("？？？", "你打破了终审。可塔外，还有无数座塔。"),
            ("？？？", "从今往后，没有新的法官——只有无穷的层，与无穷的你。"),
            ("旁白",  "【无尽模式开启】层数不再有尽头，难度持续攀升。规则，由你继续打破。"),
            ("囚徒",  "……那就，继续向上吧。"),
        };
    }

    // ---------- 对话 UI 与流程 ----------
    /// <summary>显示一段 AVG 对话：冻结全场 + 暂停 BGM，逐字打字机，点击 / 空格继续，Esc /【跳过】一键跳过。
    /// 全部行播完后调用 onDone（如开战前的卡牌浮层、或击败后的结算窗）。</summary>
    private void ShowDialogue(System.Collections.Generic.List<(string, string)> lines, System.Action onDone)
    {
        if (_dlgActive || lines == null || lines.Count == 0) { onDone?.Invoke(); return; }
        _dlgActive = true;
        _dlgLines = lines;
        _dlgIndex = 0;
        _dlgOnDone = onDone;

        _dlgLayer = new CanvasLayer();
        _dlgLayer.Layer = 40;   // 高于卡牌浮层(30)与胜负对话框(20)
        _dlgLayer.ProcessMode = Node.ProcessModeEnum.Always;  // 暂停时浮层可交互
        AddChild(_dlgLayer);

        // 遮罩：吞点击，并作为「点击任意处继续」的捕获层（MouseFilter=Stop）
        var dim = new ColorRect();
        dim.Color = new Color(0f, 0f, 0f, 0.55f);
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        dim.MouseFilter = Control.MouseFilterEnum.Stop;
        dim.GuiInput += (ev) =>
        {
            if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                AdvanceDialogue();
        };
        _dlgLayer.AddChild(dim);

        // 对话面板（底部 AVG 风格）。MouseFilter=Ignore，让非按钮点击穿透到 dim 触发继续。
        var panel = new Panel();
        panel.Position = new Vector2(90f, 350f);
        panel.Size = new Vector2(780f, 178f);
        var ps = new StyleBoxFlat();
        ps.BgColor = new Color(0.06f, 0.05f, 0.13f, 0.98f);
        ps.BorderColor = new Color(0.85f, 0.7f, 0.4f, 1f);
        ps.BorderWidthLeft = ps.BorderWidthTop = ps.BorderWidthRight = ps.BorderWidthBottom = 3;
        ps.CornerRadiusTopLeft = ps.CornerRadiusTopRight =
            ps.CornerRadiusBottomLeft = ps.CornerRadiusBottomRight = 12;
        ps.ContentMarginLeft = ps.ContentMarginRight = 18;
        ps.ContentMarginTop = ps.ContentMarginBottom = 14;
        panel.AddThemeStyleboxOverride("panel", ps);
        panel.MouseFilter = Control.MouseFilterEnum.Ignore;
        _dlgLayer.AddChild(panel);

        _dlgSpeaker = new Label();
        _dlgSpeaker.Position = new Vector2(24f, 16f);
        _dlgSpeaker.Size = new Vector2(420f, 30f);
        _dlgSpeaker.AddThemeFontSizeOverride("font_size", 20);
        _dlgSpeaker.HorizontalAlignment = HorizontalAlignment.Left;
        _dlgSpeaker.Modulate = new Color(1f, 0.85f, 0.4f);
        _dlgSpeaker.MouseFilter = Control.MouseFilterEnum.Ignore;
        panel.AddChild(_dlgSpeaker);

        _dlgText = new Label();
        _dlgText.Position = new Vector2(24f, 54f);
        _dlgText.Size = new Vector2(732f, 96f);
        _dlgText.AddThemeFontSizeOverride("font_size", 17);
        _dlgText.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _dlgText.VerticalAlignment = VerticalAlignment.Top;
        _dlgText.Modulate = new Color(0.94f, 0.94f, 0.98f);
        _dlgText.MouseFilter = Control.MouseFilterEnum.Ignore;
        panel.AddChild(_dlgText);

        var hint = new Label();
        hint.Text = "空格 / 点击 继续 · Esc 跳过";
        hint.Position = new Vector2(508f, 148f);
        hint.Size = new Vector2(248f, 20f);
        hint.AddThemeFontSizeOverride("font_size", 13);
        hint.HorizontalAlignment = HorizontalAlignment.Right;
        hint.Modulate = new Color(0.6f, 0.56f, 0.72f);
        hint.MouseFilter = Control.MouseFilterEnum.Ignore;
        panel.AddChild(hint);

        // 【跳过】按钮：一键跳过整段对话（MouseFilter=Stop，点击不触发 dim 的继续）
        var skip = new Button();
        skip.Text = "跳过 ▶▶";
        skip.Position = new Vector2(panel.Size.X - 116f, 14f);
        skip.Size = new Vector2(100f, 34f);
        skip.AddThemeFontSizeOverride("font_size", 16);
        skip.Alignment = HorizontalAlignment.Center;
        skip.ProcessMode = Node.ProcessModeEnum.Always;
        skip.FocusMode = Control.FocusModeEnum.None;
        skip.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = new Color(0.30f, 0.18f, 0.12f, 0.95f),
            BorderColor = new Color(0.95f, 0.7f, 0.5f, 1f),
            BorderWidthLeft = 2,
            BorderWidthTop = 2,
            BorderWidthRight = 2,
            BorderWidthBottom = 2,
        });
        skip.AddThemeColorOverride("font_color", new Color(1f, 0.9f, 0.8f));
        skip.Pressed += SkipDialogue;
        WireButtonSfx(skip);
        panel.AddChild(skip);

        RuleManager.Instance?.PauseBGM();
        GetTree().Paused = true;   // 冻结游戏（玩家/BOSS 不动）；对话结束后由 onDone 恢复/进入下一步
        ShowLine(0);
    }

    private void ShowLine(int i)
    {
        if (_dlgLines == null || i < 0 || i >= _dlgLines.Count) return;
        var (sp, tx) = _dlgLines[i];
        _dlgSpeaker.Text = sp;
        _dlgFull = tx;
        _dlgShown = 0;
        _dlgTyping = true;
        _dlgText.Text = "";
    }

    /// <summary>推进对话：打字中则瞬间补全本行；否则进入下一行；末行后结束。</summary>
    private void AdvanceDialogue()
    {
        if (!_dlgActive) return;
        if (_dlgTyping)
        {
            _dlgShown = _dlgFull.Length;
            _dlgText.Text = _dlgFull;
            _dlgTyping = false;
            return;
        }
        _dlgIndex++;
        if (_dlgIndex >= _dlgLines.Count) EndDialogue();
        else ShowLine(_dlgIndex);
    }

    /// <summary>结束对话（正常播完或中途跳过）：恢复 BGM 与树，清理浮层，触发 onDone。</summary>
    private void EndDialogue()
    {
        if (!_dlgActive) return;
        _dlgActive = false;
        RuleManager.Instance?.ResumeBGM();
        GetTree().Paused = false;
        var layer = _dlgLayer;
        _dlgLayer = null;
        layer?.QueueFree();
        var done = _dlgOnDone;
        _dlgOnDone = null;
        done?.Invoke();
    }

    /// <summary>一键跳过：直接结束整段对话。</summary>
    private void SkipDialogue() => EndDialogue();

    public override void _Process(double delta)
    {
        // 对话打字机：本节点 Always，暂停期间仍推进，逐字显示当前行。
        if (_dlgActive && _dlgTyping)
        {
            _dlgShown += (int)(DlgCps * (float)delta);
            if (_dlgShown >= _dlgFull.Length)
            {
                _dlgShown = _dlgFull.Length;
                _dlgTyping = false;
                _dlgText.Text = _dlgFull;
            }
            else
            {
                _dlgText.Text = _dlgFull.Substring(0, _dlgShown);
            }
        }
    }
}
