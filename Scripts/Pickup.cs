using Godot;

namespace BreakingRules;

/// <summary>
/// 地图拾取物：玩家碰到即获得对应增益，随后隐藏并在 RespawnDelay 后重生（可再生资源）。
/// Kind 取值："Speed"（加速）/ "Jump"（跳跃↑）/ "Heal"（回血）。
/// 用 string 而非 enum 导出，便于在 .tscn 里直接写 Kind = "Speed" 等，避免枚举序列化歧义。
/// </summary>
[GlobalClass]
public partial class Pickup : Area2D
{
    [Export] public string Kind { get; set; } = "Heal";
    [Export] public float RespawnDelay { get; set; } = 15f;

    private Sprite2D _sprite;
    private float _t;
    private bool _active = true;

    public override void _Ready()
    {
        _sprite = new Sprite2D();
        // 用 Interface 图标集的 PNG（24px，Scale=1）；三种道具取不同色相，便于区分。
        // 换图标只改下面这行映射。
        string icon = Kind switch
        {
            "Speed" => "res://Assets/PNG/Interface/Tiles/tile_0034.png", // 蓝
            "Jump"  => "res://Assets/PNG/Interface/Tiles/tile_0019.png", // 橙黄
            _       => "res://Assets/PNG/Interface/Tiles/tile_0025.png", // 红（Heal）
        };
        _sprite.Texture = GD.Load<Texture2D>(icon);
        AddChild(_sprite);

        var shape = new CollisionShape2D();
        shape.Shape = new CircleShape2D { Radius = 18f };
        AddChild(shape);

        // 仅当进入的是玩家才触发；拾取后关闭监测，倒计时结束再打开。
        BodyEntered += OnBodyEntered;
    }

    public override void _Process(double delta)
    {
        _t += (float)delta;
        if (_sprite != null)
            _sprite.Position = new Vector2(0f, Mathf.Sin(_t * 3f) * 4f); // 轻微上下浮动，吸引注意
    }

    private void OnBodyEntered(Node body)
    {
        if (!_active || body is not Player p) return;
        p.ApplyPickup(Kind);
        _active = false;
        if (_sprite != null) _sprite.Visible = false;
        // 物理信号回调内不能直接改 monitoring（物理状态 locked），必须 deferred
        Variant off = false;
        SetDeferred("monitoring", off);
        var timer = GetTree().CreateTimer(RespawnDelay);
        timer.Timeout += Respawn;
    }

    private void Respawn()
    {
        _active = true;
        if (_sprite != null) _sprite.Visible = true;
        // 重新开启检测（deferred，与拾取时一致）
        Variant on = true;
        SetDeferred("monitoring", on);
    }
}
