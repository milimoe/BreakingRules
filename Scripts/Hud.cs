using Godot;

namespace BreakingRules;

/// <summary>
/// HUD：玩家 HP、墨条、Boss 血条、真空期倒计时 + 金框。
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
    private TextureRect[] _skillIcons = new TextureRect[2];
    private Label[] _skillCdLabels = new Label[2];

    public override void _Ready()
    {
        _player = GetTree().GetFirstNodeInGroup("player") as Player;
        _boss = GetTree().GetFirstNodeInGroup("boss") as Boss;

        _status = new Label();
        _status.Position = new Vector2(12, 10);
        _status.AddThemeFontSizeOverride("font_size", 18);
        AddChild(_status);

        BuildSkillUi();

        // Boss 血条：顶部居中（稍往下一点），上方用名称「初审官」标记
        _bossLabel = new Label();
        _bossLabel.Text = "初审官";
        _bossLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _bossLabel.VerticalAlignment = VerticalAlignment.Center;
        _bossLabel.AddThemeFontSizeOverride("font_size", 16);
        _bossLabel.Modulate = new Color(0.85f, 0.7f, 1f); // 浅紫，呼应 BOSS 配色
        _bossLabel.Position = new Vector2(360f, 8f);
        _bossLabel.Size = new Vector2(240f, 26f);
        AddChild(_bossLabel);

        _bossBar = MakeBar(new Vector2(360, 40), new Color(0.55f, 0.2f, 0.85f), 24f);
        _bossBar.Size = new Vector2(240f, 18f);

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
        if (_boss != null)
            _boss.Connect(Boss.SignalName.HealthChanged, Callable.From<int, int>(OnBoss));

        if (RuleManager.Instance != null)
        {
            RuleManager.Instance.Connect(RuleManager.SignalName.VacuumStarted, Callable.From(OnVacuumStart));
            RuleManager.Instance.Connect(RuleManager.SignalName.VacuumEnded, Callable.From(OnVacuumEnd));
        }

        Update();
    }

    private ProgressBar MakeBar(Vector2 pos, Color color, float max)
    {
        var bar = new ProgressBar();
        bar.Position = pos;
        bar.Size = new Vector2(200, 18);
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

    private void OnVacuumStart() { _goldFrame.Visible = true; _vacuum.Visible = true; }
    private void OnVacuumEnd() { _goldFrame.Visible = false; _vacuum.Visible = false; }

    // ---------- 技能 UI（技能点 + 两个技能图标槽） ----------
    private void BuildSkillUi()
    {
        _skillLabel = new Label();
        _skillLabel.Position = new Vector2(12f, 36f);
        _skillLabel.AddThemeFontSizeOverride("font_size", 16);
        _skillLabel.Modulate = new Color(1f, 0.9f, 0.4f);
        AddChild(_skillLabel);

        for (int i = 0; i < 2; i++)
        {
            var slot = new Control();
            slot.Position = new Vector2(12f + i * 52f, 60f);
            slot.Size = new Vector2(46f, 46f);

            var bg = new Panel();
            bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            var bgStyle = new StyleBoxFlat();
            bgStyle.BgColor = new Color(0.1f, 0.1f, 0.15f, 0.35f); // 半透明底 -> “透明图标”观感
            bgStyle.BorderColor = new Color(0.6f, 0.6f, 0.7f, 0.85f);
            bgStyle.BorderWidthLeft = 1; bgStyle.BorderWidthTop = 1;
            bgStyle.BorderWidthRight = 1; bgStyle.BorderWidthBottom = 1;
            bg.AddThemeStyleboxOverride("panel", bgStyle);
            slot.AddChild(bg);

            var icon = new TextureRect();
            icon.Texture = MakeSkillIcon(i + 1);
            icon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            icon.Position = new Vector2(4f, 2f);
            icon.Size = new Vector2(38f, 34f);
            slot.AddChild(icon);

            var key = new Label();
            key.Text = (i + 1).ToString();
            key.AddThemeFontSizeOverride("font_size", 13);
            key.Position = new Vector2(2f, 30f);
            key.Modulate = new Color(0.92f, 0.92f, 0.96f);
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
        int shown = Mathf.Min(n, 10);
        _skillLabel.Text = n == 0 ? "技能 ✨ 0" : "技能 " + new string('✨', shown) + $" {n}";
        for (int i = 1; i <= 2; i++)
        {
            int idx = i - 1;
            bool ready = _player.IsSkillReady(i);
            _skillIcons[idx].Modulate = ready ? Colors.White : new Color(0.5f, 0.5f, 0.5f);
            _skillCdLabels[idx].Visible = !ready;
            if (!ready) _skillCdLabels[idx].Text = Mathf.Ceil(_player.SkillCd(i)).ToString();
        }
    }

    private ImageTexture MakeSkillIcon(int kind)
    {
        int s = 32;
        var img = Image.CreateEmpty(s, s, false, Image.Format.Rgba8);
        img.Fill(new Color(0f, 0f, 0f, 0f)); // 透明底
        Color col = kind == 1 ? new Color(1f, 0.4f, 0.3f) : new Color(0.5f, 0.9f, 1f);
        if (kind == 1)
        {
            img.FillRect(new Rect2I(4, 13, 24, 6), col); // 毁灭直线：中央粗横线
        }
        else
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
        return ImageTexture.CreateFromImage(img);
    }

    public override void _Process(double delta)
    {
        if (RuleManager.Instance != null && RuleManager.Instance.IsVacuum)
            _vacuum.Text = $"真空期 {RuleManager.Instance.VacuumRemaining:F1}s · 攻击×3 移速×2 墨×2";
        UpdateSkills();
    }

    private void Update()
    {
        int hp = _player != null ? _player.Hp : 0;
        string hearts = hp > 0 ? new string('♥', hp) : "—";
        _status.Text = $"生命 {hearts}";
    }
}
