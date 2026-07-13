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
    private const float ExpandedBottom = 312f;   // 展开时面板下边（offset_bottom 值），容纳 9 行操作明细
    private const float CollapsedBottom = 52f;    // 收起时仅留标题栏高度（含 content_margin，避免裁切标题）

    private VBoxContainer _content;
    private Button _toggle;

    public override void _Ready()
    {
        _content = GetNode<VBoxContainer>("VBox/Content");
        _toggle = GetNode<Button>("VBox/Header/Toggle");
        _toggle.Connect(Button.SignalName.Pressed, Callable.From(OnToggle));
        _expanded = _savedExpanded;   // 重载场景后读取上次状态
        Apply();
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
