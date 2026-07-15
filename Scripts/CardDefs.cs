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
        ("reverse",  "逆转裁判",  "BOSS生命值低于40%：你的伤害提升100%。",     new Color(0.95f, 0.30f, 0.30f)),
        ("salt",     "伤口撒盐",  "持有时违反规则不阻止行动（禁跳可跳、禁武可攻）；违反规则（禁区扣血）后2秒内，下次攻击伤害+50%。", new Color(0.90f, 0.50f, 0.20f)),
        ("despise",  "蔑视之刃",  "普攻命中叠加「蔑视」效果，每层使BOSS额外受到5%伤害，最多5层。", new Color(0.95f, 0.40f, 0.40f)),
        // 🛡️ 防御与恢复类
        ("plea",     "申辩",      "使用「自我治愈」时，自动免费释放一次八向射线。",       new Color(0.40f, 0.90f, 0.60f)),
        ("innocent", "无罪推定",  "每关累计防御10次：清除全屏规则并获得2秒青色护盾。", new Color(0.30f, 1.00f, 0.95f)),
        ("pardon",   "特赦令",    "受到致命伤害时，生命值锁定在1点，并获得一次持续3秒的青色护盾），每关只能生效一次。", new Color(0.30f, 1.00f, 0.95f)),
        // ✨ 技能与蓄力类
        ("lightning","闪电宣读",  "「划除」的蓄力时间从1秒缩短至0.5秒，成功划除后额外获得10点能量。",     new Color(0.95f, 0.95f, 0.30f)),
        ("chain",    "连锁违宪",  "「划除」规则后，自动免费释放一次伤害为50%的八向射线。",     new Color(0.50f, 0.90f, 1.00f)),
        // 🔀 机制改变类
        ("counter",  "反诉",      "身处禁攻/限速区时，BOSS也受该限制（禁攻不挡投技）。", new Color(0.80f, 0.50f, 0.90f)),
        ("final",    "终审判决",  "生命值为1点时：Q蓄力时间减半、攻击提升30%、能量积累效率提升30%。",   new Color(1.00f, 0.40f, 0.40f)),
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
