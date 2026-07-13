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

    public override void _Ready()
    {
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

    // ---------- 三个竖排按钮 ----------
    private void BuildButtons()
    {
        var labels = new[] { "开始游戏", "规则介绍", "退出游戏" };
        float bw = 250f, bh = 52f, gap = 16f;
        float x = (960f - bw) / 2f;
        float startY = 348f;
        for (int i = 0; i < labels.Length; i++)
        {
            int idx = i;
            Action act = idx == 0 ? (Action)StartGame
                        : idx == 1 ? OpenRules
                        : QuitGame;
            AddChild(MakeButton(labels[i], new Vector2(x, startY + i * (bh + gap)), bw, bh, act));
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
        return btn;
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
        win.CustomMinimumSize = new Vector2(680f, 460f);
        win.AnchorLeft = win.AnchorRight = win.AnchorTop = win.AnchorBottom = 0.5f;
        win.OffsetLeft = -340f;
        win.OffsetRight = 340f;
        win.OffsetTop = -230f;
        win.OffsetBottom = 230f;
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
            "· 走到规则区，长按 Q 划除规则，将其反转成弹簧，并开启真空期（攻击×3、移速×2）。\n" +
            "· 击败每一层法官，向上一层发起挑战；打穿最终 BOSS「终裁者」后开启无尽模式。\n\n" +
            "记住——规则就是用来打破的。";
    }

    private static string ControlsText()
    {
        return
            "移动：A / D（部分规则下左右反转）\n" +
            "跳跃：W / ↑\n" +
            "攻击 Boss：J / 空格\n" +
            "划除条文：长按 Q 约 1 秒（期间按任意键或松手取消，不进入冷却；完成进 5 秒冷却）\n" +
            "防御（按住 S）：BOSS 攻击前红色描边闪烁预警；防御中完全抵挡 BOSS 伤害，但不可移动/攻击\n" +
            "技能 1 毁灭直线 / 2 八向射线（各消耗 1 技能点，地图随机掉落宝珠获取）\n" +
            "技能 3 青色护盾（3 秒无敌，可抵挡投技）/ 4 自我治愈（+2 HP）\n" +
            "规则条文可能全图生效或跟随你（标注【全图】/【跟随】），走到消除区长按 Q 划除。\n" +
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
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;
        // 弹窗打开时：仅 Esc 关闭，屏蔽其它（避免误触开始游戏）
        if (_rulesOverlay != null)
        {
            if (key.Keycode == Key.Escape) CloseRules();
            return;
        }
        if (key.Keycode == Key.Enter || key.Keycode == Key.KpEnter || key.Keycode == Key.Space)
            StartGame();
        else if (key.Keycode == Key.Escape)
            QuitGame();
    }
}
