using Godot;

namespace BreakingRules;

[GlobalClass]
public partial class LevelDirector : Node2D
{
    private Player _player;
    private Boss _boss;
    private CanvasLayer _overlay;
    private Label _overlayLabel;
    private bool _ended;

    public override void _Ready()
    {
        // 暂停游戏时仍需接收 R 键重开，故本节点设为 Always（其子树无游戏逻辑，不受影响）。
        ProcessMode = Node.ProcessModeEnum.Always;

        // 先于其余节点复位 Autoload（Boss/玩家随后 _Ready 才会登记）
        RuleManager.Instance?.Reset();
        RunState.Instance?.Reset();

        // LevelDirector 是 World 的首个子节点，其 _Ready 先于 Player/Boss（它们随后才入组），
        // 因此用节点路径取引用，而非依赖 group。
        _player = GetParent()?.GetNode<Player>("Player");
        _boss = GetParent()?.GetNode<Boss>("Boss");

        if (_player != null)
            _player.Connect(Player.SignalName.Died, Callable.From(OnPlayerDied));
        if (_boss != null)
            _boss.Connect(Boss.SignalName.Died, Callable.From(OnBossDied));

        // 关键修复：原在 _Ready 内同步构建地形会因父节点(World)仍在 setup 子节点而触发
        // "Parent node is busy setting up children"（TileMap 加不进场景 → 看不到平台）。
        // 把整段 Build 延迟到场景树空闲时执行——此时所有节点已就位，add_child 不再被拒。
        CallDeferred(nameof(BuildTerrain));
        BuildOverlay();
    }

    private void BuildTerrain()
    {
        TerrainBuilder.Build(this);
    }

    private void OnPlayerDied()
    {
        if (_ended) return;
        Lose();
    }

    private void OnBossDied()
    {
        if (_ended) return;
        Win();
    }

    private void Win()
    {
        _ended = true;
        RuleManager.Instance?.PlaySFX("win");
        GetTree().Paused = true; // 冻结全场：玩家/Boss/条文均停止，不再能移动
        ShowOverlay("规则崩坏 · 初审官倒下！\n按 R 重新开始");
    }

    private void Lose()
    {
        _ended = true;
        GetTree().Paused = true; // 冻结全场
        ShowOverlay("被终审 · 失败\n按 R 重新开始");
    }

    private void BuildOverlay()
    {
        _overlay = new CanvasLayer();
        _overlay.Layer = 10;
        AddChild(_overlay);
        var dim = new ColorRect();
        dim.Color = new Color(0f, 0f, 0f, 0.55f);
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _overlay.AddChild(dim);
        _overlayLabel = new Label();
        _overlayLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _overlayLabel.VerticalAlignment = VerticalAlignment.Center;
        _overlayLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _overlayLabel.AddThemeFontSizeOverride("font_size", 30);
        _overlay.AddChild(_overlayLabel);
        _overlay.Visible = false;
    }

    private void ShowOverlay(string text)
    {
        _overlayLabel.Text = text;
        _overlay.Visible = true;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_ended && @event is InputEventKey key && key.Pressed && key.Keycode == Key.R)
        {
            GetTree().Paused = false; // 重开前必须解除暂停，否则新场景也会处于暂停态
            GetTree().ReloadCurrentScene();
        }
    }
}
