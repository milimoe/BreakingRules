using Godot;
using System;

namespace BreakingRules;

/// <summary>
/// 标题界面：展示游戏标题、英文代码、囚徒背景简介，以及三个竖排按钮
/// （开始游戏 / 规则介绍 / 退出游戏）。点击「规则介绍」弹出小窗，完整展示
/// 游戏规则与操作指南。启动游戏会重置一局进度并切到 Main 场景；退出则 Quit。
/// </summary>
public partial class Title : Control
{
    private Control _rulesOverlay;   // 规则介绍弹窗根（遮罩+窗口），null = 未打开
    private Control _settingsOverlay; // 设置弹窗根，null = 未打开
    private Label _resLabel;         // 设置面板：当前分辨率
    private Label _volLabel;         // 设置面板：当前音量
    private Godot.Collections.Dictionary<string, Label> _bindLabels = new();  // action -> 当前按键 Label
    private Godot.Collections.Dictionary<string, Button> _cancelButtons = new();  // action -> 听键时的【取消】按钮
    private bool _listening = false;  // 是否处于「听键」状态
    private string _listeningAction;  // 正在重绑的 action
    private Button _listeningBtn;     // 正在闪烁的「重新绑定」按钮

    public override void _Ready()
    {
        Settings.Load();
        Settings.ApplyResolution();   // 启动即套用上次保存的窗口分辨率
        BuildBackground();
        BuildTitle();
        BuildIntro();
        BuildButtons();
    }

    // ---------- 背景：暗紫黑底 + 顶部聚光 + 底部牢笼栅栏 ----------
    private void BuildBackground()
    {
        var bg = new ColorRect();
        bg.Color = new Color(0.05f, 0.035f, 0.075f, 1f);
        bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        bg.MouseFilter = Control.MouseFilterEnum.Stop;   // 吞点击，避免穿透
        AddChild(bg);

        // 顶部暖光晕（营造聚光感）
        var glow = new ColorRect();
        glow.Color = new Color(0.26f, 0.19f, 0.11f, 0.55f);
        glow.Position = new Vector2(0f, 0f);
        glow.Size = new Vector2(960f, 220f);
        glow.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(glow);

        // 标题下方一道金色细线，提升精致感
        var line = new ColorRect();
        line.Color = new Color(0.85f, 0.7f, 0.4f, 0.5f);
        line.Position = new Vector2(330f, 172f);
        line.Size = new Vector2(300f, 2f);
        line.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(line);

        // 底部牢笼栅栏（呼应「规则之塔 / 牢笼」主题）
        int bars = 13;
        float w = 960f / bars;
        for (int i = 0; i < bars; i++)
        {
            var bar = new ColorRect();
            bar.Color = new Color(0.45f, 0.35f, 0.55f, 0.30f);
            bar.Position = new Vector2(i * w + w * 0.22f, 470f);
            bar.Size = new Vector2(w * 0.16f, 70f);
            bar.MouseFilter = Control.MouseFilterEnum.Ignore;
            AddChild(bar);
        }
    }

    // ---------- 标题 ----------
    private void BuildTitle()
    {
        var t = new Label();
        t.Text = "终审日";
        t.HorizontalAlignment = HorizontalAlignment.Center;
        t.AddThemeFontSizeOverride("font_size", 60);
        t.Modulate = new Color(1f, 0.84f, 0.32f);     // 金色
        t.Position = new Vector2(0f, 34f);
        t.Size = new Vector2(960f, 72f);
        t.AddThemeConstantOverride("outline_size", 3);
        t.AddThemeColorOverride("font_outline_color", new Color(0.08f, 0.05f, 0.12f));
        AddChild(t);

        var sub = new Label();
        sub.Text = "~规则就是用来打破的~";
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        sub.AddThemeFontSizeOverride("font_size", 22);
        sub.Modulate = new Color(0.88f, 0.80f, 1f);    // 淡紫
        sub.Position = new Vector2(0f, 112f);
        sub.Size = new Vector2(960f, 30f);
        AddChild(sub);

        var code = new Label();
        code.Text = "Breaking Rules";
        code.HorizontalAlignment = HorizontalAlignment.Center;
        code.AddThemeFontSizeOverride("font_size", 15);
        code.Modulate = new Color(0.62f, 0.56f, 0.78f, 0.85f);
        code.Position = new Vector2(0f, 144f);
        code.Size = new Vector2(960f, 22f);
        AddChild(code);
    }

    // ---------- 背景简介（囚徒故事） ----------
    private void BuildIntro()
    {
        int best = RunState.Instance != null ? RunState.Instance.BestClearCount : 0;
        var intro = new Label();
        intro.Text =
            "你是被囚禁在「规则之塔」底层的囚徒，却坚信这场审判并不公正。\n" +
            "牢笼如塔，将你牢牢禁锢在最底层；若要挣脱，就必须一层层向上，\n" +
            "逐一质疑每一层法官颁布的「规则」。\n" +
            "从最底层的「初审官」开始，帮他打破这座牢笼。\n\n" +
            $"历史最高通关：{best} 层";
        intro.HorizontalAlignment = HorizontalAlignment.Center;
        intro.VerticalAlignment = VerticalAlignment.Center;
        intro.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        intro.AddThemeFontSizeOverride("font_size", 16);
        intro.Modulate = new Color(0.93f, 0.93f, 0.98f);
        intro.Position = new Vector2(150f, 192f);
        intro.Size = new Vector2(660f, 140f);
        AddChild(intro);
    }

    // ---------- 2x2 网格按钮 ----------
    private void BuildButtons()
    {
        var labels = new[] { "开始游戏", "规则介绍", "设置", "退出游戏" };
        float bw = 220f, bh = 50f, colGap = 24f, rowGap = 18f;
        int cols = 2;
        float gridW = cols * bw + (cols - 1) * colGap;   // 2*220 + 24 = 464
        float startX = (960f - gridW) / 2f;
        float startY = 354f;
        for (int i = 0; i < labels.Length; i++)
        {
            int idx = i;
            Action act = idx == 0 ? (Action)StartGame
                        : idx == 1 ? OpenRules
                        : idx == 2 ? OpenSettings
                        : QuitGame;
            int col = i % cols;
            int row = i / cols;
            float px = startX + col * (bw + colGap);
            float py = startY + row * (bh + rowGap);
            AddChild(MakeButton(labels[i], new Vector2(px, py), bw, bh, act));
        }
    }

    private Button MakeButton(string text, Vector2 pos, float w, float h, Action act)
    {
        var btn = new Button();
        btn.Text = text;
        btn.Position = pos;
        btn.Size = new Vector2(w, h);
        btn.AddThemeFontSizeOverride("font_size", 21);
        btn.Alignment = HorizontalAlignment.Center;
        btn.FocusMode = Control.FocusModeEnum.None;   // 键盘不自动激活，统一走 _UnhandledInput
        btn.AddThemeStyleboxOverride("normal", MakeBtnStyle(false, false));
        btn.AddThemeStyleboxOverride("hover", MakeBtnStyle(true, false));
        btn.AddThemeStyleboxOverride("pressed", MakeBtnStyle(false, true));
        btn.AddThemeStyleboxOverride("focus", MakeBtnStyle(false, false));
        btn.AddThemeColorOverride("font_color", new Color(0.96f, 0.94f, 1f));
        btn.AddThemeColorOverride("font_hover_color", new Color(1f, 0.9f, 0.5f));
        btn.AddThemeColorOverride("font_pressed_color", new Color(1f, 0.95f, 0.7f));
        btn.Pressed += () => act();
        WireButtonSfx(btn);
        return btn;
    }

    /// <summary>给按钮接上悬停 / 点击提示音（触碰播放 ui_hover，按下播放 ui_click）。</summary>
    private static void WireButtonSfx(Button btn)
    {
        btn.MouseEntered += () => RuleManager.Instance?.PlaySFX("ui_hover");
        btn.Pressed += () => RuleManager.Instance?.PlaySFX("ui_click");
    }

    private static StyleBoxFlat MakeBtnStyle(bool hover, bool pressed)
    {
        var sb = new StyleBoxFlat();
        sb.BgColor = pressed ? new Color(0.42f, 0.30f, 0.55f, 0.98f)
                    : hover ? new Color(0.30f, 0.20f, 0.42f, 0.96f)
                    : new Color(0.16f, 0.12f, 0.22f, 0.92f);
        sb.BorderColor = (hover || pressed)
            ? new Color(0.95f, 0.8f, 0.45f, 1f)
            : new Color(0.55f, 0.45f, 0.8f, 0.9f);
        sb.BorderWidthLeft = sb.BorderWidthTop = sb.BorderWidthRight = sb.BorderWidthBottom = 2;
        sb.CornerRadiusTopLeft = sb.CornerRadiusTopRight =
            sb.CornerRadiusBottomLeft = sb.CornerRadiusBottomRight = 10;
        sb.ContentMarginLeft = sb.ContentMarginRight = 12;
        sb.ContentMarginTop = sb.ContentMarginBottom = 8;
        return sb;
    }

    // ---------- 规则介绍弹窗 ----------
    private void OpenRules()
    {
        if (_rulesOverlay != null) return;

        var overlay = new Control();
        overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        overlay.MouseFilter = Control.MouseFilterEnum.Stop;

        // 半透明遮罩（点遮罩任意处关闭）
        var dim = new ColorRect();
        dim.Color = new Color(0f, 0f, 0f, 0.62f);
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        dim.MouseFilter = Control.MouseFilterEnum.Stop;
        dim.GuiInput += (ev) =>
        {
            if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                CloseRules();
        };
        overlay.AddChild(dim);

        // 窗口面板：手动锚点居中于屏幕中心（不用 Container，避免子节点尺寸被布局
        // 覆盖导致窗口偏移——CenterContainer 只认 CustomMinimumSize，忽略手动 Size）。
        var win = new Panel();
        win.CustomMinimumSize = new Vector2(700f, 500f);
        win.AnchorLeft = win.AnchorRight = win.AnchorTop = win.AnchorBottom = 0.5f;
        win.OffsetLeft = -350f;
        win.OffsetRight = 350f;
        win.OffsetTop = -250f;
        win.OffsetBottom = 250f;
        win.MouseFilter = Control.MouseFilterEnum.Stop;
        var ws = new StyleBoxFlat();
        ws.BgColor = new Color(0.09f, 0.07f, 0.15f, 0.98f);
        ws.BorderColor = new Color(0.85f, 0.7f, 0.4f, 1f);
        ws.BorderWidthLeft = ws.BorderWidthTop = ws.BorderWidthRight = ws.BorderWidthBottom = 3;
        ws.CornerRadiusTopLeft = ws.CornerRadiusTopRight =
            ws.CornerRadiusBottomLeft = ws.CornerRadiusBottomRight = 12;
        ws.ContentMarginLeft = ws.ContentMarginRight = 18;
        ws.ContentMarginTop = ws.ContentMarginBottom = 18;
        win.AddThemeStyleboxOverride("panel", ws);
        overlay.AddChild(win);

        // 窗口标题
        var wtitle = new Label();
        wtitle.Text = "规则与操作指南";
        wtitle.AddThemeFontSizeOverride("font_size", 24);
        wtitle.Modulate = new Color(1f, 0.85f, 0.4f);
        wtitle.Position = new Vector2(20f, 14f);
        wtitle.Size = new Vector2(520f, 34f);
        win.AddChild(wtitle);

        // 关闭按钮
        var close = new Button();
        close.Text = "关闭 ✕";
        close.Position = new Vector2(win.Size.X - 112f, 14f);
        close.Size = new Vector2(94f, 34f);
        close.AddThemeFontSizeOverride("font_size", 16);
        close.FocusMode = Control.FocusModeEnum.None;
        close.AddThemeStyleboxOverride("normal", MakeBtnStyle(false, false));
        close.AddThemeStyleboxOverride("hover", MakeBtnStyle(true, false));
        close.AddThemeColorOverride("font_color", new Color(0.96f, 0.94f, 1f));
        WireButtonSfx(close);
        close.Pressed += CloseRules;
        win.AddChild(close);

        // 滚动内容区
        var sc = new ScrollContainer();
        sc.Position = new Vector2(16f, 58f);
        sc.Size = new Vector2(win.Size.X - 32f, win.Size.Y - 74f);
        sc.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        sc.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
        sc.AddThemeStyleboxOverride("bg", new StyleBoxFlat());
        win.AddChild(sc);

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        vbox.AddThemeConstantOverride("separation", 10);
        sc.AddChild(vbox);

        var rules = new Label();
        rules.Text = RulesText();
        rules.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        rules.AddThemeFontSizeOverride("font_size", 15);
        rules.Modulate = new Color(0.9f, 0.9f, 0.95f);
        vbox.AddChild(rules);

        var sep = new ColorRect();
        sep.Color = new Color(0.5f, 0.4f, 0.6f, 0.5f);
        sep.Size = new Vector2(sc.Size.X - 4f, 1);
        vbox.AddChild(sep);

        var ctrlTitle = new Label();
        ctrlTitle.Text = "操作指南";
        ctrlTitle.AddThemeFontSizeOverride("font_size", 17);
        ctrlTitle.Modulate = new Color(1f, 0.85f, 0.5f);
        vbox.AddChild(ctrlTitle);

        var ctrl = new Label();
        ctrl.Text = ControlsText();
        ctrl.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        ctrl.AddThemeFontSizeOverride("font_size", 15);
        ctrl.Modulate = new Color(0.92f, 0.92f, 0.97f);
        vbox.AddChild(ctrl);

        AddChild(overlay);
        _rulesOverlay = overlay;

        // 淡入动画
        overlay.Modulate = new Color(1f, 1f, 1f, 0f);
        var tw = CreateTween();
        tw.TweenProperty(overlay, "modulate", new Color(1f, 1f, 1f, 1f), 0.18f);
    }

    private void CloseRules()
    {
        if (_rulesOverlay == null) return;
        var ov = _rulesOverlay;
        _rulesOverlay = null;
        var tw = CreateTween();
        tw.TweenProperty(ov, "modulate", new Color(1f, 1f, 1f, 0f), 0.15f);
        tw.TweenCallback(Callable.From(() => ov.QueueFree()));
    }

    // ---------- 游戏设置弹窗 ----------
    private void OpenSettings()
    {
        if (_settingsOverlay != null) return;

        var overlay = new Control();
        overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        overlay.MouseFilter = Control.MouseFilterEnum.Stop;

        var dim = new ColorRect();
        dim.Color = new Color(0f, 0f, 0f, 0.62f);
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        dim.MouseFilter = Control.MouseFilterEnum.Stop;
        dim.GuiInput += (ev) =>
        {
            if (_listening) return;   // 听键中点击遮罩不关闭，避免打断改键
            if (ev is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                CloseSettings();
        };
        overlay.AddChild(dim);

        var win = new Panel();
        win.CustomMinimumSize = new Vector2(700f, 460f);
        win.AnchorLeft = win.AnchorRight = win.AnchorTop = win.AnchorBottom = 0.5f;
        win.OffsetLeft = -350f; win.OffsetRight = 350f;
        win.OffsetTop = -230f; win.OffsetBottom = 230f;
        win.MouseFilter = Control.MouseFilterEnum.Stop;
        var ws = new StyleBoxFlat();
        ws.BgColor = new Color(0.09f, 0.07f, 0.15f, 0.98f);
        ws.BorderColor = new Color(0.85f, 0.7f, 0.4f, 1f);
        ws.BorderWidthLeft = ws.BorderWidthTop = ws.BorderWidthRight = ws.BorderWidthBottom = 3;
        ws.CornerRadiusTopLeft = ws.CornerRadiusTopRight =
            ws.CornerRadiusBottomLeft = ws.CornerRadiusBottomRight = 12;
        ws.ContentMarginLeft = ws.ContentMarginRight = 18;
        ws.ContentMarginTop = ws.ContentMarginBottom = 18;
        win.AddThemeStyleboxOverride("panel", ws);
        overlay.AddChild(win);

        var wtitle = new Label();
        wtitle.Text = "游戏设置";
        wtitle.AddThemeFontSizeOverride("font_size", 24);
        wtitle.Modulate = new Color(1f, 0.85f, 0.4f);
        wtitle.Position = new Vector2(20f, 14f);
        wtitle.Size = new Vector2(520f, 34f);
        win.AddChild(wtitle);

        var close = new Button();
        close.Text = "关闭 ✕";
        close.Position = new Vector2(win.Size.X - 112f, 14f);
        close.Size = new Vector2(94f, 34f);
        close.AddThemeFontSizeOverride("font_size", 16);
        close.FocusMode = Control.FocusModeEnum.None;
        close.AddThemeStyleboxOverride("normal", MakeBtnStyle(false, false));
        close.AddThemeStyleboxOverride("hover", MakeBtnStyle(true, false));
        close.AddThemeColorOverride("font_color", new Color(0.96f, 0.94f, 1f));
        WireButtonSfx(close);
        close.Pressed += CloseSettings;
        win.AddChild(close);

        // 滚动内容区：分辨率 / 音量 / 操作按键 全部放入，超出可滚动
        var body = new ScrollContainer();
        body.Position = new Vector2(16f, 56f);
        body.Size = new Vector2(win.Size.X - 32f, win.Size.Y - 72f);
        body.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        body.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
        body.AddThemeStyleboxOverride("bg", new StyleBoxFlat());
        win.AddChild(body);

        var col = new VBoxContainer();
        col.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        col.AddThemeConstantOverride("separation", 10);
        body.AddChild(col);

        // 分辨率行
        var resRow = AddRow(col, "分辨率");
        resRow.AddChild(MakeButton("◀", Vector2.Zero, 46f, 40f, () => CycleResolution(-1)));
        _resLabel = new Label();
        _resLabel.Text = Settings.ResolutionLabel();
        _resLabel.AddThemeFontSizeOverride("font_size", 18);
        _resLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _resLabel.Modulate = new Color(1f, 0.9f, 0.5f);
        _resLabel.CustomMinimumSize = new Vector2(232f, 34f);
        _resLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        resRow.AddChild(_resLabel);
        resRow.AddChild(MakeButton("▶", Vector2.Zero, 46f, 40f, () => CycleResolution(1)));

        // 主音量行
        var volRow = AddRow(col, "主音量");
        volRow.AddChild(MakeButton("－", Vector2.Zero, 46f, 40f, () => StepVolume(-1)));
        _volLabel = new Label();
        _volLabel.Text = Settings.VolumeLabel();
        _volLabel.AddThemeFontSizeOverride("font_size", 18);
        _volLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _volLabel.Modulate = new Color(1f, 0.9f, 0.5f);
        _volLabel.CustomMinimumSize = new Vector2(232f, 34f);
        _volLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        volRow.AddChild(_volLabel);
        volRow.AddChild(MakeButton("＋", Vector2.Zero, 46f, 40f, () => StepVolume(1)));

        // 分隔标题行：标题 + 恢复默认按钮
        var rebindHeader = new HBoxContainer();
        rebindHeader.AddThemeConstantOverride("separation", 8);
        var sepCap = new Label();
        sepCap.Text = "操作按键";
        sepCap.AddThemeFontSizeOverride("font_size", 18);
        sepCap.Modulate = new Color(1f, 0.85f, 0.4f);
        sepCap.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        rebindHeader.AddChild(sepCap);
        rebindHeader.AddChild(MakeButton("恢复默认", Vector2.Zero, 110f, 34f, ResetBindings));
        col.AddChild(rebindHeader);

        // 改键行（每行：动作名 + 当前键 + 重新绑定按钮 + 听键时出现的【取消】按钮）
        foreach (var (disp, act) in InputBindings.Rebindable)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var name = new Label();
            name.Text = disp;
            name.AddThemeFontSizeOverride("font_size", 16);
            name.Modulate = new Color(0.9f, 0.9f, 0.96f);
            name.CustomMinimumSize = new Vector2(130f, 36f);
            name.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(name);

            var cur = new Label();
            cur.Text = InputBindings.KeyLabel(act);
            cur.AddThemeFontSizeOverride("font_size", 16);
            cur.HorizontalAlignment = HorizontalAlignment.Center;
            cur.Modulate = new Color(1f, 0.9f, 0.5f);
            cur.CustomMinimumSize = new Vector2(140f, 36f);
            cur.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            row.AddChild(cur);
            _bindLabels[act] = cur;

            var btn = MakeButton("重新绑定", Vector2.Zero, 120f, 36f, () => { });
            btn.Pressed += () => BeginRebind(act, btn);
            row.AddChild(btn);

            var cancelBtn = MakeButton("取消", Vector2.Zero, 70f, 36f, () => { });
            cancelBtn.Visible = false;   // 仅在听键中出现，避免常驻占用空间
            cancelBtn.Pressed += CancelRebind;
            row.AddChild(cancelBtn);
            _cancelButtons[act] = cancelBtn;

            col.AddChild(row);
        }

        var hint = new Label();
        hint.Text = "分辨率 / 音量 / 按键实时生效并自动保存 · 点「重新绑定」后按任意键（【取消】或 Esc 取消）· 【恢复默认】还原全部按键 · Esc 关闭";
        hint.AddThemeFontSizeOverride("font_size", 13);
        hint.Modulate = new Color(0.6f, 0.56f, 0.72f);
        hint.AutowrapMode = TextServer.AutowrapMode.Word;
        col.AddChild(hint);

        AddChild(overlay);
        _settingsOverlay = overlay;

        overlay.Modulate = new Color(1f, 1f, 1f, 0f);
        var tw = CreateTween();
        tw.TweenProperty(overlay, "modulate", new Color(1f, 1f, 1f, 1f), 0.18f);
    }

    private void CloseSettings()
    {
        if (_settingsOverlay == null) return;
        var ov = _settingsOverlay;
        _settingsOverlay = null;
        var tw = CreateTween();
        tw.TweenProperty(ov, "modulate", new Color(1f, 1f, 1f, 0f), 0.15f);
        tw.TweenCallback(Callable.From(() => ov.QueueFree()));
    }

    // 设置面板里的一行（左侧标题 + 右侧控件由调用方追加）
    private HBoxContainer AddRow(VBoxContainer col, string caption)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 8);
        var cap = new Label();
        cap.Text = caption;
        cap.AddThemeFontSizeOverride("font_size", 18);
        cap.Modulate = new Color(0.92f, 0.92f, 0.98f);
        cap.CustomMinimumSize = new Vector2(120f, 40f);
        cap.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        row.AddChild(cap);
        col.AddChild(row);
        return row;
    }

    // 进入「听键」状态：按钮变提示，等待下一次按键
    private void BeginRebind(string action, Button btn)
    {
        if (_listening) return;
        _listening = true;
        _listeningAction = action;
        _listeningBtn = btn;
        btn.Text = "按任意键…";
        btn.Modulate = new Color(1f, 0.6f, 0.3f);
        if (_cancelButtons.TryGetValue(action, out var cb)) cb.Visible = true;
    }

    // 取消当前听键（不改动绑定），恢复按钮与【取消】按钮状态。
    private void CancelRebind()
    {
        if (!_listening) return;
        _listening = false;
        if (_listeningBtn != null)
        {
            _listeningBtn.Text = "重新绑定";
            _listeningBtn.Modulate = Colors.White;
        }
        if (_listeningAction != null && _cancelButtons.TryGetValue(_listeningAction, out var cb))
            cb.Visible = false;
        _listeningAction = null;
        _listeningBtn = null;
    }

    // 恢复全部按键到默认，并刷新面板显示。
    private void ResetBindings()
    {
        InputBindings.ResetToDefaults();
        RefreshBindLabels();
    }

    // 刷新所有「当前按键」Label 为最新绑定。
    private void RefreshBindLabels()
    {
        foreach (var kv in _bindLabels)
            kv.Value.Text = InputBindings.KeyLabel(kv.Key);
    }

    private void CycleResolution(int dir)
    {
        Settings.ResolutionIndex += dir;
        Settings.Clamp();
        Settings.ApplyResolution();
        Settings.Save();
        if (_resLabel != null) _resLabel.Text = Settings.ResolutionLabel();
    }

    private void StepVolume(int dir)
    {
        Settings.MasterVolume += dir * 0.1f;
        Settings.Clamp();
        Settings.ApplyVolume();
        Settings.Save();
        if (_volLabel != null) _volLabel.Text = Settings.VolumeLabel();
        // 步进音效由 MakeButton 在 ApplyVolume 之后播放，故可即时试听新音量（0 则自然无声）
    }

    // ---------- 文案 ----------
    private static string RulesText()
    {
        return
            "【背景】\n" +
            "你是一名被囚禁在「规则之塔」底层的囚徒。你坚信这场审判并不公正，决定亲手打破这座牢笼。\n\n" +
            "牢笼如同一座高塔，将你牢牢禁锢在最底层。想要挣脱，就必须一层层向上，逐一质疑每层法官颁布的「规则」。\n\n" +
            "【玩法】\n" +
            "· 从最底层的「初审官」开始，你将帮助囚徒向上攀爬，挑战每一层的法官（BOSS）。\n" +
            "· BOSS 会不断颁布规则：禁跳 / 禁武 / 限速。规则可能升级为「全图生效」或「跟随你移动」。\n" +
            "· 走到规则区，长按 " + InputBindings.KeyLabel("strike") + " 划除规则，将其反转成弹簧，并开启真空期（攻击×3、移速×2）。\n" +
            "· 击败每一层法官，向上一层发起挑战；打穿最终 BOSS「终裁者」后开启无尽模式。\n\n" +
            "【能量条与大招】\n" +
            "· 能量上限 50：造成伤害、防御成功、消除规则都会积攒能量。\n" +
            "· 能量充满后，按 " + InputBindings.KeyLabel("ult_switch") + " 循环切换三种大招，长按 " + InputBindings.KeyLabel("ult_release") + "（约 0.6 秒）释放。\n" +
            "· 乱刀斩（重创）/ 闪现斩（眩晕）/ 时间怀表（持续回血），各自独立冷却（20 / 18 / 25 秒）。\n" +
            "· 能量值与大招选择会继承到下一关。\n\n" +
            "【卡牌系统】\n" +
            "· 第 3 关起，每通过一关可从随机抽出的 3 张卡中选取 1 张（卡池每次随机抽取，不固定顺序）。\n" +
            "· 最多持有 5 张；满 5 张后再选取，将替换你已持有的一张。\n" +
            "· 卡牌分攻击、防御、蓄力、机制四类，按流派自由搭配，强化你的破规之路。\n\n" +
            "记住——规则就是用来打破的。";
    }

    private static string ControlsText()
    {
        return
            "移动：" + InputBindings.KeyLabel("move_left") + " / " + InputBindings.KeyLabel("move_right") + "（部分规则下左右反转）\n" +
            "跳跃：" + InputBindings.KeyLabel("jump") + "\n" +
            "攻击 Boss：" + InputBindings.KeyLabel("attack") + "\n" +
            "划除条文：长按 " + InputBindings.KeyLabel("strike") + " 约 1 秒（期间按任意键或松手取消，不进入冷却；完成进 5 秒冷却）\n" +
            "防御（按住 " + InputBindings.KeyLabel("guard") + "）：BOSS 攻击前红色描边闪烁预警；防御中完全抵挡 BOSS 伤害，但不可移动/攻击\n" +
            "技能 " + InputBindings.KeyLabel("skill1") + " 毁灭直线 / " + InputBindings.KeyLabel("skill2") + " 八向射线（各消耗 1 技能点，地图随机掉落宝珠获取）\n" +
            "技能 " + InputBindings.KeyLabel("skill3") + " 青色护盾（3 秒无敌，可抵挡投技）/ " + InputBindings.KeyLabel("skill4") + " 自我治愈（+2 HP）\n" +
            "大招：" + InputBindings.KeyLabel("ult_switch") + " 切换选中，长按 " + InputBindings.KeyLabel("ult_release") + " 释放（需能量充满；乱刀斩CD20s / 闪现斩CD18s / 时间怀表CD25s）\n" +
            "规则条文可能全图生效或跟随你（标注【全图】/【跟随】），走到消除区长按 " + InputBindings.KeyLabel("strike") + " 划除。\n" +
            "本游戏的操作说明也是规则，但你不需要遵守它们。";
    }

    // ---------- 动作 ----------
    private void StartGame()
    {
        RunState.Instance?.StartNewRun();
        GetTree().ChangeSceneToFile("res://Scenes/Main.tscn");
    }

    private void QuitGame() => GetTree().Quit();

    public override void _UnhandledInput(InputEvent @event)
    {
        // 听键中：捕获下一个按键，重绑并持久化，消费事件避免误触暂停/关闭
        if (_listening && @event is InputEventKey key && key.Pressed && !key.Echo)
        {
            // Esc 视为取消本次重绑，避免把 Esc 本身绑成动作键
            if (key.Keycode == Key.Escape)
            {
                CancelRebind();
                GetViewport().SetInputAsHandled();
                return;
            }
            int code = (int)key.PhysicalKeycode;
            InputBindings.ApplyAction(_listeningAction, code);
            InputBindings.SaveBinding(_listeningAction, code);
            if (_bindLabels.TryGetValue(_listeningAction, out var lbl))
                lbl.Text = InputBindings.KeyLabel(_listeningAction);
            if (_listeningBtn != null)
            {
                _listeningBtn.Text = "重新绑定";
                _listeningBtn.Modulate = Colors.White;
            }
            if (_listeningAction != null && _cancelButtons.TryGetValue(_listeningAction, out var cb))
                cb.Visible = false;
            _listening = false;
            _listeningAction = null;
            _listeningBtn = null;
            GetViewport().SetInputAsHandled();
            return;
        }
        if (@event is not InputEventKey key2 || !key2.Pressed || key2.Echo) return;
        // 任一弹窗打开时：仅 Esc 关闭，屏蔽其它（避免误触开始游戏）
        if (_rulesOverlay != null)
        {
            if (key2.Keycode == Key.Escape) CloseRules();
            return;
        }
        if (_settingsOverlay != null)
        {
            if (key2.Keycode == Key.Escape) CloseSettings();
            return;
        }
        if (key2.Keycode == Key.Enter || key2.Keycode == Key.KpEnter || key2.Keycode == Key.Space)
            StartGame();
        else if (key2.Keycode == Key.Escape)
            QuitGame();
    }
}
