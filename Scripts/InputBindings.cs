using Godot;

namespace BreakingRules;

/// <summary>
/// 按键改键中枢（Autoload）。
/// - 启动(_Ready)时从 user://controls.cfg 回灌已保存的绑定到 InputMap；
/// - 提供 KeyLabel / KeyLabels 读取某 action 当前实际按键（规则界面 / 操作指南 / 技能槽同步显示）；
/// - 提供 ApplyAction / SaveBinding 在运行时改键并持久化。
/// 方案A：仅处理战斗键（移动 / 跳 / 攻击 / 防御 / 划除 / 技能1-4 / 大招切换与释放）。
/// </summary>
public partial class InputBindings : Node
{
    public static InputBindings Instance { get; private set; }

    // action -> 设置面板显示名（顺序即面板行顺序）
    public static readonly (string display, string action)[] Rebindable =
    {
        ("移动 左",  "move_left"),
        ("移动 右",  "move_right"),
        ("跳跃",     "jump"),
        ("攻击",     "attack"),
        ("防御",     "guard"),
        ("划除",     "strike"),
        ("技能 1",   "skill1"),
        ("技能 2",   "skill2"),
        ("技能 3",   "skill3"),
        ("技能 4",   "skill4"),
        ("大招切换", "ult_switch"),
        ("大招释放", "ult_release"),
    };

    private const string CfgPath = "user://controls.cfg";

    public override void _Ready()
    {
        Instance = this;
        LoadAndApply();
    }

    // 从配置文件回灌：只覆盖已保存的 action，未保存的沿用 project.godot 默认绑定。
    private void LoadAndApply()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(CfgPath) != Error.Ok) return;
        if (!cfg.HasSection("bindings")) return;
        foreach (var (_, action) in Rebindable)
        {
            var v = cfg.GetValue("bindings", action);
            if (v.VariantType == Variant.Type.Int)
                ApplyAction(action, (int)v);
        }
    }

    // 将某 action 重绑为单个 physical keycode（清除其原有全部事件）。
    public static void ApplyAction(string action, int physicalKeycode)
    {
        if (!InputMap.HasAction(action)) return;
        foreach (InputEvent e in InputMap.ActionGetEvents(action))
            InputMap.ActionEraseEvent(action, e);
        var ev = new InputEventKey
        {
            PhysicalKeycode = (Key)physicalKeycode,
            Pressed = false,
        };
        InputMap.ActionAddEvent(action, ev);
    }

    // 保存单个 action 的 physical keycode 到配置文件（保留其它 action 的绑定）。
    public static void SaveBinding(string action, int physicalKeycode)
    {
        var cfg = new ConfigFile();
        if (cfg.Load(CfgPath) != Error.Ok) { /* 首次：空配置，直接写入 */ }
        cfg.SetValue("bindings", action, physicalKeycode);
        cfg.Save(CfgPath);
    }

    // 某 action 当前实际按键（取第一个事件的 physical keycode）。
    public static string KeyLabel(string action)
    {
        foreach (var e in InputMap.ActionGetEvents(action))
            if (e is InputEventKey k && k.PhysicalKeycode != Key.None)
                return OS.GetKeycodeString(k.PhysicalKeycode);
        return "—";
    }

    // 某 action 全部按键，用 sep 连接（如 "A / D"、"Space / J"）。
    public static string KeyLabels(string action, string sep = " / ")
    {
        string sb = "";
        bool first = true;
        foreach (var e in InputMap.ActionGetEvents(action))
        {
            if (e is InputEventKey k && k.PhysicalKeycode != Key.None)
            {
                if (!first) sb += sep;
                sb += OS.GetKeycodeString(k.PhysicalKeycode);
                first = false;
            }
        }
        return first ? "—" : sb;
    }
}
