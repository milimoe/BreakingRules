using Godot;

namespace BreakingRules;

/// <summary>
/// 随机掉落的「技能宝珠」：玩家碰到即获得 1 点技能点，随后消失（一次性掉落，不刷新）。
/// 由 RuleManager 定时在地图随机位置生成。
/// </summary>
[GlobalClass]
public partial class SkillDrop : Area2D
{
    private Sprite2D _sprite;
    private float _t;
    private float _ttl = 16f;   // 未被捡起则自动消失，避免场上堆积

    public override void _Ready()
    {
        _sprite = new Sprite2D();
        _sprite.Texture = Util.Square(22, new Color(1f, 0.85f, 0.2f)); // 金色宝珠
        _sprite.Scale = new Vector2(1f, 1f);
        AddChild(_sprite);

        var shape = new CollisionShape2D();
        shape.Shape = new CircleShape2D { Radius = 16f };
        AddChild(shape);

        BodyEntered += OnBodyEntered;
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (_sprite != null)
            _sprite.Position = new Vector2(0f, Mathf.Sin(_t * 3f) * 4f); // 轻微浮动
        _ttl -= (float)delta;
        if (_ttl <= 0f) QueueFree();
    }

    private void OnBodyEntered(Node body)
    {
        if (body is Player p)
        {
            p.AddSkillPoint();
            RuleManager.Instance?.PlaySFX("vacuum_start"); // 复用音效作反馈
            QueueFree();
        }
    }
}
