using Godot;
using System;
using System.Collections.Generic;

namespace BreakingRules;

/// <summary>
/// 卡牌定义（静态数据）。每张卡的“效果”在 Player / Boss 对应逻辑中按 id 判定，
/// 此处只保存展示用的元数据（id / 名称 / 描述 / 配色）。
/// </summary>
public static class CardDefs
{
    // (id, 名称, 描述, 配色)
    public static readonly (string id, string name, string desc, Color color)[] All =
    {
        // ⚔️ 攻击与伤害类
        ("reverse",  "逆转裁判",  "对生命值低于 40% 的敌人，所有伤害提升 100%",     new Color(0.95f, 0.30f, 0.30f)),
        ("salt",     "伤口撒盐",  "违令而行（禁区扣血）后 2 秒内，下次攻击伤害 +50%；持有此卡时触犯规则不阻止行动（禁跳仍可跳、禁武仍可攻）", new Color(0.90f, 0.50f, 0.20f)),
        ("despise",  "蔑视之刃",  "普攻命中叠加「蔑视」，敌人受伤 +5%/层（最多 5 层）", new Color(0.95f, 0.40f, 0.40f)),
        // 🛡️ 防御与恢复类
        ("plea",     "申辩",      "使用「4-自我治愈」时，免费释放一次八向射线",       new Color(0.40f, 0.90f, 0.60f)),
        ("innocent", "无罪推定",  "每关累计防御 10 次：清除全屏规则 + 获得 2s 青色护盾", new Color(0.30f, 1.00f, 0.95f)),
        ("pardon",   "特赦令",    "致命伤免死（锁 1 血 + 3s 青色护盾），每关 1 次（用后变灰）", new Color(0.30f, 1.00f, 0.95f)),
        // ✨ 技能与蓄力类
        ("lightning","闪电宣读",  "Q 蓄力时间 1s→0.5s，消除规则额外 +10 能量",     new Color(0.95f, 0.95f, 0.30f)),
        ("chain",    "连锁违宪",  "Q 消除规则后，免费释放一次 50% 伤害的八向射线",     new Color(0.50f, 0.90f, 1.00f)),
        // 🔀 机制改变类
        ("counter",  "反诉",      "身处禁攻/限速区时 BOSS 也受该限制（禁攻不挡投技）", new Color(0.80f, 0.50f, 0.90f)),
        ("final",    "终审判决",  "生命=1 时：Q 蓄力减半、攻击 +30%、能量积累 +30%",   new Color(1.00f, 0.40f, 0.40f)),
    };

    public static (string id, string name, string desc, Color color)? Get(string id)
    {
        foreach (var c in All)
            if (c.id == id) return c;
        return null;
    }

    // 独立随机源（带时间种子），不依赖 Godot 全局 RNG —— 否则每次运行打乱模式相同，
    // 会表现为“每次都按顺序出同一组 3 张”。
    private static readonly System.Random _rng = new System.Random();

    /// <summary>从 src 随机抽取 n 个不重复元素（Fisher–Yates 洗牌后取前 n 个）。</summary>
    public static List<string> DrawDistinct(List<string> src, int n)
    {
        var tmp = new List<string>(src);
        for (int i = tmp.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(i + 1);
            (tmp[i], tmp[j]) = (tmp[j], tmp[i]);
        }
        var r = new List<string>();
        for (int i = 0; i < tmp.Count && r.Count < n; i++) r.Add(tmp[i]);
        return r;
    }
}
