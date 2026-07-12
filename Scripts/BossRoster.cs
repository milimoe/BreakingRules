using Godot;

namespace BreakingRules;

/// <summary>
/// 单个 BOSS 的配置（数据）。LevelDirector 按当前关卡索引取配置并据此生成 Boss 实例。
/// </summary>
public class BossConfig
{
    public string Name = "BOSS";
    public int MaxHp = 24;
    public float SpawnInterval = 10f;
    public float ChargeInterval = 6f;
    public float ChargeSpeed = 620f;
    public float ChargeDuration = 0.4f;
    public float MeleeDamage = 1f;
    public float MeleeCooldown = 1.2f;
    public float HoverSpeed = 40f;
    public string TexturePath = "res://Assets/PNG/Enemies/Tiles/tile_0000.png";
    public Color Tint = Colors.White;

    public BossConfig Clone() => (BossConfig)MemberwiseClone();
}

/// <summary>
/// BOSS 名册。设计数个有名字的 BOSS；打败最后一个后进入「无尽模式」（循环 + 难度递增）。
/// </summary>
public static class BossRoster
{
    public static readonly System.Collections.Generic.List<BossConfig> Configs = new()
    {
        // 1) 初审官：基准关，节奏平稳
        new BossConfig
        {
            Name = "初审官", MaxHp = 24, SpawnInterval = 10f, ChargeInterval = 6f,
            ChargeSpeed = 600f, ChargeDuration = 0.4f, MeleeDamage = 1f, MeleeCooldown = 1.2f,
            HoverSpeed = 40f, Tint = new Color(0.72f, 0.42f, 0.92f),
        },
        // 2) 改写者：更快、出条更勤
        new BossConfig
        {
            Name = "改写者", MaxHp = 30, SpawnInterval = 8f, ChargeInterval = 5f,
            ChargeSpeed = 680f, ChargeDuration = 0.45f, MeleeDamage = 1f, MeleeCooldown = 1.0f,
            HoverSpeed = 55f, Tint = new Color(0.40f, 0.72f, 0.95f),
        },
        // 3) 缚律者：更肉、冲锋更频繁
        new BossConfig
        {
            Name = "缚律者", MaxHp = 38, SpawnInterval = 9f, ChargeInterval = 4f,
            ChargeSpeed = 720f, ChargeDuration = 0.5f, MeleeDamage = 1.5f, MeleeCooldown = 1.1f,
            HoverSpeed = 30f, Tint = new Color(0.95f, 0.62f, 0.30f),
        },
        // 4) 缄默官：飘忽、范围伤害
        new BossConfig
        {
            Name = "缄默官", MaxHp = 34, SpawnInterval = 7f, ChargeInterval = 5.5f,
            ChargeSpeed = 640f, ChargeDuration = 0.4f, MeleeDamage = 1f, MeleeCooldown = 1.0f,
            HoverSpeed = 65f, Tint = new Color(0.50f, 0.90f, 0.60f),
        },
        // 5) 终裁者：最终 BOSS，高压。击败后开启无尽模式
        new BossConfig
        {
            Name = "终裁者", MaxHp = 50, SpawnInterval = 7f, ChargeInterval = 4.5f,
            ChargeSpeed = 760f, ChargeDuration = 0.5f, MeleeDamage = 2f, MeleeCooldown = 0.9f,
            HoverSpeed = 50f, Tint = new Color(0.96f, 0.32f, 0.36f),
        },
    };

    public static int Count => Configs.Count;

    /// <summary>
    /// 取第 stage 关的 BOSS 配置。stage &lt; Count 为固定序列；
    /// stage &gt;= Count 进入无尽模式：循环名册并对每轮 (loops) 递增难度、名称加「·狂」。
    /// </summary>
    public static BossConfig GetConfig(int stage)
    {
        int n = Configs.Count;
        int idx = ((stage % n) + n) % n;
        int loops = stage / n; // 0=首次通关序列，>=1=无尽轮回
        var baseCfg = Configs[idx];
        if (loops <= 0) return baseCfg;

        var c = baseCfg.Clone();
        c.Name = baseCfg.Name + "·狂";
        c.MaxHp = (int)Mathf.Round(baseCfg.MaxHp * (1f + 0.25f * loops));
        c.SpawnInterval = Mathf.Max(3f, baseCfg.SpawnInterval * (1f - 0.08f * loops));
        c.ChargeInterval = Mathf.Max(3f, baseCfg.ChargeInterval * (1f - 0.08f * loops));
        c.MeleeDamage = baseCfg.MeleeDamage + 0.5f * loops;
        return c;
    }
}
