using Godot;

namespace BreakingRules;

[GlobalClass]
public partial class RunState : Node
{
    public static RunState Instance { get; private set; }

    public int ObeyCount { get; private set; }
    public int ExploitCount { get; private set; }
    public int BreakCount { get; private set; }
    public int StrikeCount { get; private set; }

    public override void _EnterTree()
    {
        Instance = this;
        base._EnterTree();
    }

    // 重开场景时复位累计计数（Autoload 跨场景存活）
    public void Reset()
    {
        ObeyCount = 0;
        ExploitCount = 0;
        BreakCount = 0;
        StrikeCount = 0;
    }

    public void RecordObey() => ObeyCount++;
    public void RecordExploit() => ExploitCount++;
    public void RecordBreak() => BreakCount++;
    public void RecordStrike() => StrikeCount++;

    public string DominantStance()
    {
        if (BreakCount >= ObeyCount && BreakCount >= ExploitCount) return "自由";
        if (ExploitCount >= ObeyCount) return "投机";
        return "守序";
    }
}
