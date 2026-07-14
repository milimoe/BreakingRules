using Godot;
using System.Collections.Generic;

namespace BreakingRules;

[GlobalClass]
public partial class RunState : Node
{
    public static RunState Instance { get; private set; }

    // 单次场景内的「姿态」计数（每次（重）载入场景复位）
    public int ObeyCount { get; private set; }
    public int ExploitCount { get; private set; }
    public int BreakCount { get; private set; }
    public int StrikeCount { get; private set; }

    // 一局之内跨场景保留（下一关时不清零；重新开始才清零）
    public int ClearCount { get; private set; }
    public int CurrentStage { get; set; }

    // 跨关继承：进入下一关时携带上一关的「当前生命」与「技能点」；
    // 重新开始 / 新开局时 Carry=false，回退为满血 + 0 技能点。
    public int CarryHp { get; set; }
    public int CarrySkill { get; set; }
    public bool Carry { get; set; }

    // 能量条 / 大招跨关继承：进入下一关时携带上一关的「当前能量」与「所选大招」。
    // 重新开始 / 新开局时 Carry=false，回退为 0 能量 + 默认大招。
    public float CarryEnergy { get; set; }
    public int CarryUlt { get; set; }

    // 卡牌系统：一局之内（跨关）保留已获得的卡牌；新开局 / 重新开始清零。
    // 第 3 关起每关开局 3 选 1，最多持有 5 张；选第 6 张时须替换一张（换掉的回卡池）。
    public const int MaxCards = 5;
    private readonly List<string> _ownedCards = new();
    public IReadOnlyList<string> OwnedCards => _ownedCards;
    public bool HasCard(string id) => _ownedCards.Contains(id);
    public void AddCard(string id)
    {
        if (!_ownedCards.Contains(id) && _ownedCards.Count < MaxCards)
            _ownedCards.Add(id);
    }
    public void RemoveCard(string id) => _ownedCards.Remove(id);
    public void ClearCards() => _ownedCards.Clear();

    // 历史最高通关数（持久化到 user://，跨应用启动保留）
    public int BestClearCount { get; private set; }

    private const string SavePath = "user://breaking_rules.save";

    public override void _EnterTree()
    {
        Instance = this;
        LoadBest();
        base._EnterTree();
    }

    /// <summary>每次（重）载入场景时复位「姿态」计数。通关数/当前关/历史最高不在此清零（跨场景保留）。</summary>
    public void Reset()
    {
        ObeyCount = 0;
        ExploitCount = 0;
        BreakCount = 0;
        StrikeCount = 0;
    }

    /// <summary>从头开始一局：通关数与当前关归零。历史最高保留。</summary>
    public void StartNewRun()
    {
        ClearCount = 0;
        CurrentStage = 0;
        Carry = false;   // 新开局：不继承（满血、0 技能点）
        ClearCards();    // 新开局：清空已获得卡牌
    }

    /// <summary>击败一个 BOSS：通关数 +1；若刷新纪录则立即落盘。</summary>
    public void RecordClear()
    {
        ClearCount++;
        if (ClearCount > BestClearCount)
        {
            BestClearCount = ClearCount;
            SaveBest();
        }
    }

    private void SaveBest()
    {
        using var f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Write);
        if (f == null) return;
        var d = new Godot.Collections.Dictionary { { "best", BestClearCount } };
        f.StoreString(Json.Stringify(d));
    }

    private void LoadBest()
    {
        if (!FileAccess.FileExists(SavePath)) return;
        using var f = FileAccess.Open(SavePath, FileAccess.ModeFlags.Read);
        if (f == null) return;
        var v = Json.ParseString(f.GetAsText());
        if (v.VariantType == Variant.Type.Dictionary)
        {
            var d = (Godot.Collections.Dictionary)v;
            if (d.ContainsKey("best")) BestClearCount = (int)d["best"];
        }
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
