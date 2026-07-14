using Godot;

namespace BreakingRules;

/// <summary>
/// HUD：玩家 HP、技能条（Q/1/2 三槽）、Boss 血条、真空期倒计时 + 金框。
/// </summary>
public partial class Hud : Control
{
    private Label _status;
    private ProgressBar _bossBar;
    private Label _bossLabel;
    private Player _player;
    private Boss _boss;
    private Panel _goldFrame;
    private Label _vacuum;
    private Label _skillLabel;
    private TextureRect[] _skillIcons = new TextureRect[5];
    private Label[] _skillCdLabels = new Label[5];

    public override void _Ready()
    {
        _player = GetTree().GetFirstNodeInGroup("player") as Player;

        // HUD 根节点铺满视口：子节点的坐标相对视口原点(0,0)，
        // 配合 project.godot 的 canvas_items 拉伸，整体随窗口等比缩放、对齐不跑偏。
        SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        _status = new Label();
        _status.Position = new Vector2(12, 10);
        _status.AddThemeFontSizeOverride("font_size", 18);
        _status.Modulate = new Color(1f, 0.4f, 0.4f); // 红：生命
        AddChild(_status);

        BuildSkillUi();

        // Boss 血条：顶部居中（稍往下一点），上方用当前 BOSS 名称标记（动态获取）。
        // 用锚点(上中)而非硬编码 x=360，保证任意窗口尺寸下都水平居中。
        _bossLabel = new Label();
        _bossLabel.Text = "BOSS";
        _bossLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _bossLabel.VerticalAlignment = VerticalAlignment.Center;
        _bossLabel.AddThemeFontSizeOverride("font_size", 16);
        _bossLabel.Modulate = new Color(0.85f, 0.7f, 1f); // 浅紫，呼应 BOSS 配色
        _bossLabel.AnchorLeft = 0.5f; _bossLabel.AnchorRight = 0.5f;
        _bossLabel.AnchorTop = 0f; _bossLabel.AnchorBottom = 0f;
        _bossLabel.OffsetLeft = -120f; _bossLabel.OffsetRight = 120f; // 宽 240，水平居中
        _bossLabel.OffsetTop = 8f; _bossLabel.OffsetBottom = 34f;     // 高 26，顶部下移 8
        AddChild(_bossLabel);

        _bossBar = MakeBar(new Color(0.55f, 0.2f, 0.85f), 24f);
        _bossBar.AnchorLeft = 0.5f; _bossBar.AnchorRight = 0.5f;
        _bossBar.AnchorTop = 0f; _bossBar.AnchorBottom = 0f;
        _bossBar.OffsetLeft = -120f; _bossBar.OffsetRight = 120f; // 宽 240，水平居中
        _bossBar.OffsetTop = 40f; _bossBar.OffsetBottom = 58f;    // 高 18，标签下方

        // 真空期金框（默认隐藏）：透明底 + 金色边框，仅描边不遮挡
        _goldFrame = new Panel();
        _goldFrame.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var frameStyle = new StyleBoxFlat();
        frameStyle.BgColor = new Color(0f, 0f, 0f, 0f);
        frameStyle.BorderColor = new Color(1f, 0.85f, 0.2f, 0.95f);
        frameStyle.BorderWidthLeft = 6;
        frameStyle.BorderWidthTop = 6;
        frameStyle.BorderWidthRight = 6;
        frameStyle.BorderWidthBottom = 6;
        _goldFrame.AddThemeStyleboxOverride("panel", frameStyle);
        _goldFrame.MouseFilter = Control.MouseFilterEnum.Ignore;
        _goldFrame.Visible = false;
        AddChild(_goldFrame);

        _vacuum = new Label();
        _vacuum.Position = new Vector2(12, 112);
        _vacuum.AddThemeFontSizeOverride("font_size", 16);
        _vacuum.Modulate = new Color(1f, 0.85f, 0.2f);
        _vacuum.Visible = false;
        AddChild(_vacuum);

        if (_player != null)
            _player.Connect(Player.SignalName.HealthChanged, Callable.From<int, int>(OnHealth));

        if (RuleManager.Instance != null)
        {
            RuleManager.Instance.Connect(RuleManager.SignalName.VacuumStarted, Callable.From(OnVacuumStart));
            RuleManager.Instance.Connect(RuleManager.SignalName.VacuumEnded, Callable.From(OnVacuumEnd));
        }

        Update();
    }

    private ProgressBar MakeBar(Color color, float max)
    {
        var bar = new ProgressBar();
        bar.MaxValue = max;
        bar.Value = max;
        bar.ShowPercentage = false;
        // 简单着色：用 StyleBoxFlat 填充
        var fg = new StyleBoxFlat();
        fg.BgColor = color;
        bar.AddThemeStyleboxOverride("fill", fg);
        AddChild(bar);
        return bar;
    }

    private void OnHealth(int hp, int max) => Update();
    private void OnBoss(int hp, int max) { _bossBar.MaxValue = max; _bossBar.Value = hp; }

    /// <summary>Boss 由 LevelDirector 运行时动态生成，这里懒加载其引用并绑定血条/名称。</summary>
    private void FindBoss()
    {
        if (_boss != null && IsInstanceValid(_boss)) return;
        _boss = GetTree().GetFirstNodeInGroup("boss") as Boss;
        if (_boss == null) return;
        int st = RunState.Instance != null ? RunState.Instance.CurrentStage + 1 : 1;
        _bossLabel.Text = $"第{st}关 {_boss.BossName}";
        _bossBar.MaxValue = _boss.MaxHp;
        _bossBar.Value = _boss.Hp;
        _boss.Connect(Boss.SignalName.HealthChanged, Callable.From<int, int>(OnBoss));
    }

    private void OnVacuumStart() { _goldFrame.Visible = true; _vacuum.Visible = true; }
    private void OnVacuumEnd() { _goldFrame.Visible = false; _vacuum.Visible = false; }

    // ---------- 技能 UI（技能点 + 三个技能槽：Q 划除 / 1 毁灭直线 / 2 八向射线） ----------
    // 统一尺寸、半透明底与描边风格；仅以「强调色」区分按键与图标，做到颜色区分。
    private void BuildSkillUi()
    {
        _skillLabel = new Label();
        _skillLabel.Position = new Vector2(12f, 36f);
        _skillLabel.AddThemeFontSizeOverride("font_size", 18);
        _skillLabel.Modulate = new Color(1f, 0.9f, 0.4f); // 金：技能点
        AddChild(_skillLabel);

        // 槽定义：按键、图标种类、强调色（Q 青 / 1 橙红 / 2 蓝 / 3 青护盾 / 4 绿治愈）
        var slots = new (string key, int icon, Color accent)[]
        {
            ("Q", 3, new Color(0.40f, 0.95f, 0.90f)),
            ("1", 1, new Color(1.00f, 0.45f, 0.35f)),
            ("2", 2, new Color(0.50f, 0.90f, 1.00f)),
            ("3", 4, new Color(0.30f, 1.00f, 0.95f)),
            ("4", 5, new Color(0.45f, 1.00f, 0.55f)),
        };
        for (int i = 0; i < slots.Length; i++)
        {
            var def = slots[i];
            var slot = new Control();
            slot.Position = new Vector2(12f + i * 52f, 60f);
            slot.Size = new Vector2(46f, 46f);

            var bg = new Panel();
            bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var bgStyle = new StyleBoxFlat();
            bgStyle.BgColor = new Color(0.1f, 0.1f, 0.15f, 0.35f); // 半透明底
            bgStyle.BorderColor = def.accent;                        // 强调色描边
            bgStyle.BorderWidthLeft = bgStyle.BorderWidthTop =
                bgStyle.BorderWidthRight = bgStyle.BorderWidthBottom = 2;
            bg.AddThemeStyleboxOverride("panel", bgStyle);
            slot.AddChild(bg);

            var icon = new TextureRect();
            icon.Texture = MakeSkillIcon(def.icon);
            icon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            icon.Position = new Vector2(4f, 2f);
            icon.Size = new Vector2(38f, 34f);
            slot.AddChild(icon);

            var key = new Label();
            key.Text = def.key;
            key.AddThemeFontSizeOverride("font_size", 13);
            key.Position = new Vector2(2f, 30f);
            key.Modulate = def.accent;
            slot.AddChild(key);

            var cd = new Label();
            cd.AddThemeFontSizeOverride("font_size", 14);
            cd.HorizontalAlignment = HorizontalAlignment.Right;
            cd.Position = new Vector2(20f, 28f);
            cd.Size = new Vector2(24f, 18f);
            cd.Modulate = new Color(1f, 0.25f, 0.25f); // 红色 CD 数字
            cd.Visible = false;
            slot.AddChild(cd);

            AddChild(slot);
            _skillIcons[i] = icon;
            _skillCdLabels[i] = cd;
        }
    }

    private void UpdateSkills()
    {
        if (_player == null) return;
        int n = _player.SkillPoints;
        _skillLabel.Text = $"技能 ✨ {n}/10";   // 技能点上限 10，显示 (x/10)
        // 槽 0 = Q（5s CD），槽 1~4 = 技能 1~4
        UpdateSlot(0, _player.IsQReady, _player.QSkillCd, _player.QSkillCdMax);
        UpdateSlot(1, _player.IsSkillReady(1), _player.SkillCd(1), _player.SkillCdMax(1));
        UpdateSlot(2, _player.IsSkillReady(2), _player.SkillCd(2), _player.SkillCdMax(2));
        UpdateSlot(3, _player.IsSkillReady(3), _player.SkillCd(3), _player.SkillCdMax(3));
        UpdateSlot(4, _player.IsSkillReady(4), _player.SkillCd(4), _player.SkillCdMax(4));
    }

    private void UpdateSlot(int idx, bool ready, float cd, float max)
    {
        _skillIcons[idx].Modulate = ready ? Colors.White : new Color(0.5f, 0.5f, 0.5f);
        _skillCdLabels[idx].Visible = !ready;
        if (!ready) _skillCdLabels[idx].Text = Mathf.Ceil(cd).ToString();
    }

    private ImageTexture MakeSkillIcon(int kind)
    {
        int s = 32;
        var img = Image.CreateEmpty(s, s, false, Image.Format.Rgba8);
        img.Fill(new Color(0f, 0f, 0f, 0f)); // 透明底
        Color col = kind switch
        {
            1 => new Color(1f, 0.4f, 0.3f),    // 毁灭直线：橙红
            2 => new Color(0.5f, 0.9f, 1f),    // 八向射线：蓝
            4 => new Color(0.3f, 1f, 0.95f),   // 青色护盾
            5 => new Color(0.45f, 1f, 0.55f),  // 自我治愈：绿
            _ => new Color(0.4f, 0.95f, 0.9f)  // 划除(Q)：青
        };
        if (kind == 1)
        {
            img.FillRect(new Rect2I(4, 13, 24, 6), col); // 中央粗横线
        }
        else if (kind == 2)
        {
            Vector2 c = new Vector2(16f, 16f);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.Pi / 4f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                for (int t = 0; t <= 12; t++)
                {
                    Vector2 p = c + dir * t;
                    int x = (int)Mathf.Round(p.X), y = (int)Mathf.Round(p.Y);
                    if (x >= 0 && x < s && y >= 0 && y < s) img.SetPixel(x, y, col);
                }
            }
            img.SetPixel(16, 16, col);
        }
        else if (kind == 4)  // 青色护盾：空心方框
        {
            for (int i = 6; i <= 26; i++) { img.SetPixel(i, 6, col); img.SetPixel(i, 26, col); }
            for (int j = 6; j <= 26; j++) { img.SetPixel(6, j, col); img.SetPixel(26, j, col); }
        }
        else if (kind == 5)  // 自我治愈：十字
        {
            for (int i = 10; i <= 22; i++) { img.SetPixel(i, 15, col); img.SetPixel(i, 16, col); }
            for (int j = 10; j <= 22; j++) { img.SetPixel(15, j, col); img.SetPixel(16, j, col); }
        }
        else // kind == 3：划除 = 对角斜杠
        {
            for (int t = 0; t <= 26; t++)
            {
                int x = (int)Mathf.Round(4f + t * 24f / 26f);
                int y = (int)Mathf.Round(4f + t * 24f / 26f);
                if (x >= 0 && x < s && y >= 0 && y < s) img.SetPixel(x, y, col);
            }
        }
        return ImageTexture.CreateFromImage(img);
    }

    public override void _Process(double delta)
    {
        if (_boss == null || !IsInstanceValid(_boss)) FindBoss();
        if (RuleManager.Instance != null && RuleManager.Instance.IsVacuum)
            _vacuum.Text = $"真空期 {RuleManager.Instance.VacuumRemaining:F1}s · 攻击×3 移速×2";
        UpdateSkills();
    }

    private void Update()
    {
        int hp = _player != null ? _player.Hp : 0;
        string hearts = hp > 0 ? new string('♥', hp) : "—";
        _status.Text = $"生命 {hearts}";
    }
}
