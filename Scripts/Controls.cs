using Godot;

namespace BreakingRules;

/// <summary>
/// 右上角操作指南面板。顶部有【收起 / 展开】按钮：
/// 收起时只保留标题与按钮，隐藏所有操作明细，避免长期遮挡界面。
/// 按钮 focus_mode=None，不抢占键盘焦点（游戏全程用键盘操作）。
/// </summary>
public partial class Controls : Panel
{
    // 跨关卡 / 重开保留收起状态：场景重载会新建 Controls 实例，
    // 但 C# 静态字段在同一进程内不随场景重载重置，故用它持久化折叠偏好。
    private static bool _savedExpanded = true;
    private bool _expanded;
    private const float ExpandedBottom = 404f;   // 展开时面板下边（offset_bottom 值），容纳 12 行操作明细（含大招两行）
    private const float CollapsedBottom = 52f;    // 收起时仅留标题栏高度（含 content_margin，避免裁切标题）

    private VBoxContainer _content;
    private Button _toggle;

    public override void _Ready()
    {
        _content = GetNode<VBoxContainer>("VBox/Content");
        _toggle = GetNode<Button>("VBox/Header/Toggle");
        _toggle.Connect(Button.SignalName.Pressed, Callable.From(OnToggle));
        _expanded = _savedExpanded;   // 重载场景后读取上次状态
        SyncKeyLabels();              // 用实际按键覆盖 .tscn 里的写死文本
        Apply();
    }

    // 把各操作明细行的按键替换为当前实际绑定（改键后即时反映，无需重开场景）
    private void SyncKeyLabels()
    {
        if (_content == null) return;
        SetLine("Move",   "移动：" + InputBindings.KeyLabel("move_left") + " / " + InputBindings.KeyLabel("move_right"));
        SetLine("Jump",   "跳跃：" + InputBindings.KeyLabel("jump"));
        SetLine("Attack", "攻击 Boss：" + InputBindings.KeyLabel("attack"));
        SetLine("Strike", "划除条文（长按 " + InputBindings.KeyLabel("strike") + " 约1秒，任意键取消；5秒冷却）");
        SetLine("Guard",  "防御（按住 " + InputBindings.KeyLabel("guard") + "，完全抵挡 BOSS 伤害；防御中不可移动/攻击）");
        SetLine("Skill",  "技能：" + InputBindings.KeyLabel("skill1") + " 毁灭直线 / " + InputBindings.KeyLabel("skill2") + " 八向射线");
        SetLine("Skill3", "护盾：" + InputBindings.KeyLabel("skill3") + " 青色护盾（3秒无敌，可挡投技）");
        SetLine("Skill4", "治愈：" + InputBindings.KeyLabel("skill4") + " 自我治愈（+2 HP）");
        SetLine("Ult",    "大招：" + InputBindings.KeyLabel("ult_switch") + " 切换 · 长按 " + InputBindings.KeyLabel("ult_release") + " 释放（需能量充满）");
    }

    private void SetLine(string nodeName, string text)
    {
        var l = _content.GetNodeOrNull<Label>(nodeName);
        if (l != null) l.Text = text;
    }

    // 点击切换：翻转子节后必须 Apply()，否则只改了布尔、面板与按钮文本纹丝不动（按钮「点了没反应」的根因）。
    private void OnToggle()
    {
        _expanded = !_expanded;
        _savedExpanded = _expanded;   // 持久化，重载场景后保持
        Apply();
    }

    private void Apply()
    {
        if (_content != null) _content.Visible = _expanded;
        if (_toggle != null) _toggle.Text = _expanded ? "收起 ▲" : "展开 ▼";
        // 两个垂直锚点都在顶部(anchor_bottom=0)，故直接改 OffsetBottom 即可改高度。
        OffsetBottom = _expanded ? ExpandedBottom : CollapsedBottom;
    }
}
