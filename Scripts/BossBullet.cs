using Godot;

namespace BreakingRules;

/// <summary>
/// BOSS 火枪弹幕：方块子弹，朝固定方向匀速飞行。
/// 命中玩家时调用 TakeBossDamage（受防御完全抵挡），命中或飞出场地后释放。
/// </summary>
public partial class BossBullet : Node2D
{
    public Vector2 Vel;
    public float Damage;
    public float Life = 3f;

    public override void _Ready()
    {
        var spr = new Sprite2D();
        spr.Texture = Util.Square(14, 14, new Color(1f, 0.6f, 0.2f));
        AddChild(spr);
    }

    public override void _PhysicsProcess(double delta)
    {
        float d = (float)delta;
        Position += Vel * d;
        Life -= d;

        var p = GetTree().GetFirstNodeInGroup("player") as Player;
        if (p != null && IsInstanceValid(p) && p.GlobalPosition.DistanceTo(GlobalPosition) < 18f)
        {
            p.TakeBossDamage((int)Mathf.Round(Damage));
            QueueFree();
            return;
        }

        if (Life <= 0f ||
            GlobalPosition.X < -60f || GlobalPosition.X > 1020f ||
            GlobalPosition.Y < -60f || GlobalPosition.Y > 600f)
            QueueFree();
    }
}
