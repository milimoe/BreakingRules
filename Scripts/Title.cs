using Godot;
using System;

namespace BreakingRules;

/// <summary>
/// 标题界面：展示游戏标题与规则说明，提供「开始游戏 / 退出游戏」两个按钮。
/// 启动游戏会重置一局进度（RunState.StartNewRun）并切到 Main 场景；退出则直接 Quit。
/// </summary>
public partial class Title : Control
{
    public override void _Ready()
    {
        // 背景（暗色，铺满视口）
        var bg = new ColorRect();
        bg.Color = new Color(0.05f, 0.04f, 0.09f, 1f);
        bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        bg.MouseFilter = Control.MouseFilterEnum.Stop; // 吞掉点击，避免穿透
        AddChild(bg);

        // 主标题
        var title = new Label();
        title.Text = "终审日";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.VerticalAlignment = VerticalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 48);
        title.Modulate = new Color(1f, 0.85f, 0.3f);
        title.Position = new Vector2(0f, 30f);
        title.Size = new Vector2(960f, 64f);
        AddChild(title);

        // 副标题（含历史最高记录）
        var sub = new Label();
        int best = RunState.Instance != null ? RunState.Instance.BestClearCount : 0;
        sub.Text = $"Final Revision · 规则即牢笼    历史最高通关 {best} 关";
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        sub.AddThemeFontSizeOverride("font_size", 18);
        sub.Modulate = new Color(0.85f, 0.7f, 1f);
        sub.Position = new Vector2(0f, 100f);
        sub.Size = new Vector2(960f, 28f);
        AddChild(sub);

        // 规则说明面板
        var panel = new Panel();
        panel.Position = new Vector2(110f, 150f);
        panel.Size = new Vector2(740f, 252f);
        var ps = new StyleBoxFlat();
        ps.BgColor = new Color(0.1f, 0.08f, 0.16f, 0.85f);
        ps.BorderColor = new Color(0.6f, 0.5f, 0.85f, 0.9f);
        ps.BorderWidthLeft = ps.BorderWidthTop = ps.BorderWidthRight = ps.BorderWidthBottom = 3;
        panel.AddThemeStyleboxOverride("panel", ps);
        panel.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(panel);

        var rules = new Label();
        rules.Text = RulesText();
        rules.HorizontalAlignment = HorizontalAlignment.Center;
        rules.VerticalAlignment = VerticalAlignment.Center;
        rules.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        rules.AddThemeFontSizeOverride("font_size", 17);
        rules.Modulate = new Color(0.92f, 0.92f, 0.97f);
        rules.Position = new Vector2(132f, 162f);
        rules.Size = new Vector2(696f, 228f);
        AddChild(rules);

        // 按钮（FocusMode.None：键盘不自动激活按钮，统一走 _UnhandledInput，避免重复触发）
        AddChild(MakeButton("开始游戏  (Enter)", new Vector2(300f, 430f), StartGame));
        AddChild(MakeButton("退出游戏  (Esc)", new Vector2(490f, 430f), QuitGame));
    }

    private Button MakeButton(string text, Vector2 pos, Action act)
    {
        var btn = new Button();
        btn.Text = text;
        btn.Position = pos;
        btn.Size = new Vector2(170f, 46f);
        btn.AddThemeFontSizeOverride("font_size", 19);
        btn.Alignment = HorizontalAlignment.Center;
        btn.FocusMode = Control.FocusModeEnum.None;
        btn.Pressed += () => act();
        return btn;
    }

    private static string RulesText()
    {
        return
            "移动 A/D（或 ←/→） · 跳跃 W/↑/空格 · 普攻 J/空格 · 划除规则 Q（5秒冷却）\n" +
            "技能 1 毁灭直线 · 技能 2 八向射线（地图随机掉落技能宝珠，捡起获得技能点）\n" +
            "防御 S：BOSS 攻击前会红色描边闪烁预警，按住 S 可在命中瞬间完全抵挡伤害\n\n" +
            "BOSS 会不断颁布规则：禁跳 / 禁武 / 禁改 / 限速。\n" +
            "在规则区内违规会受罚；用 Q 划除规则可令其反转成弹簧，并开启真空期（攻击×3 移速×2）。\n\n" +
            "击败一名名 BOSS，挑战最终 BOSS 后开启无尽模式。";
    }

    private void StartGame()
    {
        // 从头开始一局：通关数与当前关归零（历史最高保留）。
        RunState.Instance?.StartNewRun();
        GetTree().ChangeSceneToFile("res://Scenes/Main.tscn");
    }

    private void QuitGame() => GetTree().Quit();

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;
        if (key.Keycode == Key.Enter || key.Keycode == Key.KpEnter || key.Keycode == Key.Space)
            StartGame();
        else if (key.Keycode == Key.Escape)
            QuitGame();
    }
}
