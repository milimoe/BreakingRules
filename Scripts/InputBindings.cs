using Godot;

namespace BreakingRules;

/// <summary>
/// 按键改键中枢（Autoload）。
/// - 启动(_Ready)时从 user://controls.cfg 回灌已保存的绑定到 InputMap；
/// - 每个 action 支持「主键 + 次键」两槽，可绑定键盘或鼠标（左/右键、中键、滚轮上/下、侧键1/2）；
/// - 提供 KeyLabel / KeyLabelPrimary / KeyLabelSecondary 读取某 action 当前按键（规则界面 / 操作指南 / HUD 同步显示）；
/// - 提供 RebindSlot / ResetToDefaults 在运行时改键并持久化。
/// 配置文件格式：bindings/&lt;action&gt; = Godot Array[string]，元素为 "k:&lt;physicalKeycode&gt;" 或 "m:&lt;MouseButton&gt;"，
/// 顺序即 [主键, 次键]；空串 "" 表示该槽未绑定。旧版（单 int）配置会被迁移为主键。
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

    /// <summary>单个绑定描述：键盘 physical keycode 或鼠标 button index。</summary>
    public readonly struct Binding
    {
        public enum Kind : byte { Key, Mouse }
        public Kind Type { get; }
        public int Code { get; }   // physical keycode 或 MouseButton 值

        public Binding(Kind t, int code) { Type = t; Code = code; }

        public string Serialize() => Type == Kind.Key ? $"k:{Code}" : $"m:{Code}";

        public static Binding? Parse(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length < 3) return null;
            char t = s[0];
            if (!int.TryParse(s.Substring(2), out int code)) return null;
            return t == 'k' ? new Binding(Kind.Key, code) : new Binding(Kind.Mouse, code);
        }

        public InputEvent ToInputEvent()
        {
            if (Type == Kind.Key)
                return new InputEventKey { PhysicalKeycode = (Key)Code, Pressed = false };
            return new InputEventMouseButton { ButtonIndex = (MouseButton)Code, Pressed = false };
        }

        public string Label()
        {
            if (Type == Kind.Key) return OS.GetKeycodeString((Key)Code);
            return MouseLabel((MouseButton)Code);
        }

        public static string MouseLabel(MouseButton b) => (int)b switch
        {
            1 => "鼠标左键",
            2 => "鼠标右键",
            3 => "鼠标中键",
            4 => "滚轮上",
            5 => "滚轮下",
            6 => "滚轮左",
            7 => "滚轮右",
            8 => "侧键1",
            9 => "侧键2",
            _ => b.ToString(),
        };
    }

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
            {
                // 旧版单键格式迁移：作为主键，次键留空
                var prim = Binding.Parse($"k:{v.AsInt32()}");
                if (prim.HasValue) SetBindings(action, prim, null);
            }
            else if (v.VariantType == Variant.Type.Array)
            {
                var arr = v.AsGodotArray();
                Binding? prim = arr.Count > 0 ? Binding.Parse(arr[0].ToString()) : null;
                Binding? sec = arr.Count > 1 ? Binding.Parse(arr[1].ToString()) : null;
                SetBindings(action, prim, sec);
            }
        }
    }

    // 读取某 action 当前的主/次键（按 InputMap 事件列表顺序推断：首个=主键，次个=次键）。
    public static (Binding? primary, Binding? secondary) ReadBindings(string action)
    {
        if (!InputMap.HasAction(action)) return (null, null);
        Binding? prim = null, sec = null;
        foreach (InputEvent e in InputMap.ActionGetEvents(action))
        {
            var b = FromEvent(e);
            if (b.HasValue)
            {
                if (prim == null) prim = b;
                else if (sec == null) sec = b;
            }
        }
        return (prim, sec);
    }

    private static Binding? FromEvent(InputEvent e)
    {
        if (e is InputEventKey k && k.PhysicalKeycode != Key.None)
            return new Binding(Binding.Kind.Key, (int)k.PhysicalKeycode);
        if (e is InputEventMouseButton mb && mb.ButtonIndex != MouseButton.None)
            return new Binding(Binding.Kind.Mouse, (int)mb.ButtonIndex);
        return null;
    }

    // 应用主/次键到 InputMap（清空原有全部事件后逐个添加）。
    public static void SetBindings(string action, Binding? primary, Binding? secondary)
    {
        if (!InputMap.HasAction(action)) return;
        var snapshot = new System.Collections.Generic.List<InputEvent>();
        foreach (InputEvent e in InputMap.ActionGetEvents(action)) snapshot.Add(e);
        foreach (var e in snapshot) InputMap.ActionEraseEvent(action, e);
        if (primary.HasValue) InputMap.ActionAddEvent(action, primary.Value.ToInputEvent());
        if (secondary.HasValue) InputMap.ActionAddEvent(action, secondary.Value.ToInputEvent());
    }

    // 改单个槽位（slot 0=主键, 1=次键），保留另一槽位当前绑定。
    public static void SetSlot(string action, int slot, Binding? binding)
    {
        var (p, s) = ReadBindings(action);
        if (slot == 0) SetBindings(action, binding, s);
        else SetBindings(action, p, binding);
    }

    // 改单个槽位并持久化（推荐入口）。
    public static void RebindSlot(string action, int slot, Binding? binding)
    {
        SetSlot(action, slot, binding);
        var (p, s) = ReadBindings(action);
        SaveBindings(action, p, s);
    }

    // 保存某 action 的主/次键到配置文件（保留其它 action 的绑定）。
    public static void SaveBindings(string action, Binding? primary, Binding? secondary)
    {
        var cfg = new ConfigFile();
        if (cfg.Load(CfgPath) == Error.Ok) { /* 首次：空配置，直接写入 */ }
        var arr = new Godot.Collections.Array();
        arr.Add(primary.HasValue ? primary.Value.Serialize() : "");
        arr.Add(secondary.HasValue ? secondary.Value.Serialize() : "");
        cfg.SetValue("bindings", action, arr);
        cfg.Save(CfgPath);
    }

    // 默认绑定（与 project.godot 的 [input] 保持一致）：action -> [主键, 次键]。
    // 用于「恢复默认」：还原 InputMap 到出厂状态并清除已保存的自定义绑定。
    private static readonly System.Collections.Generic.Dictionary<string, Binding?[]> Defaults = new()
    {
        ["move_left"]  = new Binding?[] { new Binding(Binding.Kind.Key, 65), new Binding(Binding.Kind.Key, 4194319) },   // A / ←
        ["move_right"] = new Binding?[] { new Binding(Binding.Kind.Key, 68), new Binding(Binding.Kind.Key, 4194321) },   // D / →
        ["jump"]       = new Binding?[] { new Binding(Binding.Kind.Key, 32), new Binding(Binding.Kind.Key, 87) },         // Space / W
        ["attack"]     = new Binding?[] { new Binding(Binding.Kind.Mouse, 1), new Binding(Binding.Kind.Key, 74) },        // 鼠标左键 / J
        ["guard"]      = new Binding?[] { new Binding(Binding.Kind.Mouse, 2), new Binding(Binding.Kind.Key, 83) },        // 鼠标右键 / S
        ["strike"]     = new Binding?[] { new Binding(Binding.Kind.Key, 81), null },                                     // Q
        ["skill1"]     = new Binding?[] { new Binding(Binding.Kind.Key, 49), null },                                     // 1
        ["skill2"]     = new Binding?[] { new Binding(Binding.Kind.Key, 50), null },                                     // 2
        ["skill3"]     = new Binding?[] { new Binding(Binding.Kind.Key, 51), null },                                     // 3
        ["skill4"]     = new Binding?[] { new Binding(Binding.Kind.Key, 52), null },                                     // 4
        ["ult_switch"] = new Binding?[] { new Binding(Binding.Kind.Key, 70), null },                                     // F
        ["ult_release"]= new Binding?[] { new Binding(Binding.Kind.Key, 69), null },                                     // E
    };

    // 恢复全部默认绑定：还原 InputMap，并清除已保存的自定义绑定（下次启动沿用 project.godot 默认）。
    public static void ResetToDefaults()
    {
        foreach (var kv in Defaults)
            SetBindings(kv.Key, kv.Value[0], kv.Value[1]);
        var cfg = new ConfigFile();
        if (cfg.Load(CfgPath) == Error.Ok && cfg.HasSection("bindings"))
        {
            cfg.EraseSection("bindings");
            cfg.Save(CfgPath);
        }
    }

    // 主键显示（无则 "—"）。
    public static string KeyLabelPrimary(string action)
    {
        var (p, _) = ReadBindings(action);
        return p.HasValue ? p.Value.Label() : "—";
    }

    // 次键显示（无则 "—"）。
    public static string KeyLabelSecondary(string action)
    {
        var (_, s) = ReadBindings(action);
        return s.HasValue ? s.Value.Label() : "—";
    }

    // 合并显示：主键( / 次键)，仅主键则只显示主键，两者皆无则 "—"。
    public static string KeyLabel(string action)
    {
        var (p, s) = ReadBindings(action);
        if (p.HasValue && s.HasValue) return p.Value.Label() + " / " + s.Value.Label();
        if (p.HasValue) return p.Value.Label();
        if (s.HasValue) return s.Value.Label();
        return "—";
    }
}
