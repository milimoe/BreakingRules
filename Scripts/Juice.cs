using Godot;
using System;

namespace BreakingRules;

/// <summary>
/// 统一「多巴胺」反馈中枢（Autoload，ProcessMode.Always）。
/// 收口四类反馈，避免各处散落硬编码：
///   1. 顿帧 HitStop —— 通过 GetTree().Paused 冻结整棵树极短时间，
///      恢复计时由本节点 _Process（Always）驱动，故能在暂停期间照常推进，
///      不会因 SceneTreeTimer 也被暂停而死锁。
///   2. 有方向屏幕震动 —— trauma 模型（trauma 线性衰减、offset = trauma * 强度），
///      支持水平/垂直/双向独立控制，满足乱刀斩横向高频、闪现斩先纵后横等需求。
///   3. 屏幕边缘红/绿闪烁 —— 全屏边框 Panel 脉冲（违令而行红、时间怀表回血绿）。
///   4. 夸张受击裂痕 —— 大数字伤害时屏幕剧烈震动 + 边缘闪 + 随机裂痕线。
/// 另含大招释放瞬间的残影（撕裂感 AfterImage）与投技视角聚焦 FocusPunch。
///
/// 关键约束：顿帧用 GetTree().Paused，会与 Win/Lose/ESC暂停/返回标题/开局选牌 的
/// 永久暂停冲突；因此这些永久暂停在设置 Paused 前必须调用 CancelHitStop()，
/// 使进行中的顿帧计时器失效、不会误把永久暂停解除。
/// </summary>
public partial class Juice : Node
{
    public static Juice Instance { get; private set; }

    /// <summary>震动轴：双向 / 仅水平 / 仅垂直。满足「有方向的抖动」需求。</summary>
    public enum ShakeAxis { Both, Horizontal, Vertical }

    private Camera2D _cam;
    private float _traumaX, _traumaY;   // 当前震动能量 [0..1]
    private float _ampX, _ampY;         // 各轴最大像素振幅
    private const float ShakeDecay = 2.2f;   // trauma 每秒衰减
    private const float MaxAmp = 16f;        // 单轴振幅上限（防止抖飞）

    // ---- 顿帧状态 ----
    private bool _hitStopActive;
    private float _hitStopRemaining;
    private int _hitStopToken;   // 使 Cancel 能精准作废进行中的顿帧

    // ---- 边缘闪烁 / 裂痕层 ----
    private Panel _edgePanel;
    private StyleBoxFlat _edgeSb;
    private CanvasLayer _crackLayer;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;   // 暂停期间仍要推进顿帧恢复与震动
        BuildEdgeLayer();
        BuildCrackLayer();
    }

    // ------------------------------------------------------------------
    // 相机注册：Player._Ready 把自身 Camera2D 交给 Juice 统一抖动。
    // 场景重载后 Player 重新注册即可；旧相机失效由 _Process 中的 IsInstanceValid 兜底。
    // ------------------------------------------------------------------
    public void RegisterCamera(Camera2D cam) => _cam = cam;

    public override void _Process(double delta)
    {
        float d = (float)delta;

        // 顿帧恢复：本节点 Always，暂停时仍推进；到点且未被 Cancel 才解除暂停。
        if (_hitStopActive && _hitStopRemaining > 0f)
        {
            _hitStopRemaining -= d;
            if (_hitStopRemaining <= 0f)
            {
                _hitStopActive = false;
                if (GetTree() != null) GetTree().Paused = false;
            }
        }

        // 震动：trauma 线性衰减，offset = trauma * 振幅 * 随机方向。
        // 暂停期间（顿帧中）本节点仍运行，故冻结画面下也能看到高频抖动。
        if (_cam != null && IsInstanceValid(_cam))
        {
            _traumaX = Mathf.Max(0f, _traumaX - ShakeDecay * d);
            _traumaY = Mathf.Max(0f, _traumaY - ShakeDecay * d);
            float ox = _traumaX * _ampX * (GD.Randf() * 2f - 1f);
            float oy = _traumaY * _ampY * (GD.Randf() * 2f - 1f);
            _cam.Offset = new Vector2(ox, oy);
        }
    }

    // ------------------------------------------------------------------
    // 顿帧 HitStop
    // ------------------------------------------------------------------
    /// <summary>冻结整棵树 dur 秒（顿帧）。若已处于永久暂停（胜负/ESC/选牌），
    /// 则不叠加、直接返回，避免覆盖永久暂停。</summary>
    public void HitStop(float dur)
    {
        if (GetTree() == null || GetTree().Paused) return;
        _hitStopActive = true;
        _hitStopRemaining = Mathf.Max(_hitStopRemaining, dur);
        _hitStopToken++;
        GetTree().Paused = true;
    }

    /// <summary>作废进行中的顿帧（不解除暂停，交给随后设置的永久暂停）。
    /// 必须在 Win/Lose/ESC暂停/返回标题/开局选牌 改 Paused 前调用。</summary>
    public void CancelHitStop()
    {
        _hitStopActive = false;
        _hitStopRemaining = 0f;
        _hitStopToken++;
    }

    // ------------------------------------------------------------------
    // 有方向屏幕震动
    // ------------------------------------------------------------------
    /// <summary>触发一次有方向震动。intensity=最大像素振幅，duration=持续秒（映射到 trauma）。</summary>
    public void Shake(float intensity, float duration, ShakeAxis axis = ShakeAxis.Both)
    {
        float trauma = Mathf.Clamp(duration * ShakeDecay, 0f, 1f);
        float amp = Mathf.Min(MaxAmp, intensity);
        if (axis == ShakeAxis.Horizontal || axis == ShakeAxis.Both)
        {
            _traumaX = Mathf.Max(_traumaX, trauma);
            _ampX = Mathf.Max(_ampX, amp);
        }
        if (axis == ShakeAxis.Vertical || axis == ShakeAxis.Both)
        {
            _traumaY = Mathf.Max(_traumaY, trauma);
            _ampY = Mathf.Max(_ampY, amp);
        }
    }

    /// <summary>投技/大招视角聚焦：相机短暂放大再回弹（与震动用不同属性，互不打架）。</summary>
    public void FocusPunch(float to = 1.18f, float back = 1.0f, float time = 0.18f)
    {
        if (_cam == null || !IsInstanceValid(_cam)) return;
        var tw = CreateTween();
        tw.TweenProperty(_cam, "zoom", new Vector2(to, to), time);
        tw.TweenProperty(_cam, "zoom", new Vector2(back, back), time * 1.2f);
    }

    // ------------------------------------------------------------------
    // 边缘闪烁（红=违规/受击，绿=回血，金=消除规则）
    // ------------------------------------------------------------------
    private void BuildEdgeLayer()
    {
        var layer = new CanvasLayer();
        layer.Layer = 16;
        layer.ProcessMode = ProcessModeEnum.Always;
        _edgePanel = new Panel();
        _edgePanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _edgeSb = new StyleBoxFlat();
        _edgeSb.BgColor = new Color(0f, 0f, 0f, 0f);   // 透明底，只留边框
        _edgeSb.BorderWidthLeft = _edgeSb.BorderWidthTop = _edgeSb.BorderWidthRight = _edgeSb.BorderWidthBottom = 14;
        _edgeSb.BorderColor = Colors.Red;
        _edgePanel.AddThemeStyleboxOverride("panel", _edgeSb);
        _edgePanel.Modulate = new Color(1f, 1f, 1f, 0f);   // 初始不可见
        _edgePanel.MouseFilter = Control.MouseFilterEnum.Ignore;
        layer.AddChild(_edgePanel);
        AddChild(layer);
    }

    /// <summary>屏幕边缘闪烁一次。color=边框色，duration=总时长，peak=最高不透明度。</summary>
    public void FlashEdge(Color color, float duration = 0.3f, float peak = 0.9f)
    {
        if (_edgePanel == null || _edgeSb == null) return;
        _edgeSb.BorderColor = color;
        var tw = CreateTween();
        tw.TweenProperty(_edgePanel, "modulate:a", peak, duration * 0.3f);
        tw.TweenProperty(_edgePanel, "modulate:a", 0f, duration * 0.7f);
    }

    // ------------------------------------------------------------------
    // 夸张受击裂痕
    // ------------------------------------------------------------------
    private void BuildCrackLayer()
    {
        _crackLayer = new CanvasLayer();
        _crackLayer.Layer = 14;
        _crackLayer.ProcessMode = ProcessModeEnum.Always;   // 暂停（顿帧）时裂痕也照常淡出
        AddChild(_crackLayer);
    }

    /// <summary>大数字伤害反馈：剧烈震动 + 边缘闪 + 随机裂痕线。severity 越大裂痕越多越亮。</summary>
    public void BigHit(float severity = 1f, Color? edgeColor = null)
    {
        Shake(16f, 0.45f, ShakeAxis.Both);
        FlashEdge(edgeColor ?? new Color(1f, 0.2f, 0.2f, 1f), 0.4f, 0.95f);
        SpawnCracks(severity);
    }

    private void SpawnCracks(float severity)
    {
        if (_crackLayer == null) return;
        int n = 3;
        var rnd = new Random();
        Vector2 center = new Vector2(480f, 270f);   // CanvasLayer 空间 = 屏幕空间（不随相机偏移）
        for (int i = 0; i < n; i++)
        {
            var line = new Line2D();
            float ang = (float)(rnd.NextDouble() * Math.PI * 2f);
            float len = 280f + (float)rnd.NextDouble() * 220f;
            Vector2 end = center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * len;
            Vector2 mid = center.Lerp(end, 0.5f) +
                          new Vector2((float)(rnd.NextDouble() - 0.5) * 90f, (float)(rnd.NextDouble() - 0.5) * 90f);
            line.Points = new Vector2[] { center, mid, end };
            line.Width = 3f + severity;
            line.DefaultColor = new Color(1f, 1f, 1f, 0.9f);
            _crackLayer.AddChild(line);
            var tw = CreateTween();
            tw.TweenProperty(line, "modulate:a", 0f, 0.4f + 0.2f * severity);
            tw.TweenCallback(Callable.From(() => line.QueueFree()));
        }
    }

    // ------------------------------------------------------------------
    // 残影 / 撕裂（大招释放、BOSS 大伤）
    // ------------------------------------------------------------------
    /// <summary>在 source 当前位置生成若干半透明残影，模拟动态模糊/撕裂。
    /// tex 取 source 当前帧纹理；tint 决定残影色调。</summary>
    public void AfterImage(Node2D source, Texture2D tex, Color tint, int copies = 3, float life = 0.3f)
    {
        if (source == null) return;
        if (tex == null) tex = Util.Square(40, 40, Colors.White);   // 帧纹理缺失时回退占位方块，杜绝「随机不显示」
        var scene = GetTree()?.CurrentScene;
        if (scene == null) return;
        for (int i = 0; i < copies; i++)
        {
            var sp = new Sprite2D();
            sp.Texture = tex;
            sp.GlobalPosition = source.GlobalPosition;
            sp.Rotation = source.Rotation;
            sp.Scale = source.Scale;
            sp.Modulate = new Color(tint.R, tint.G, tint.B, 0.5f);
            sp.ZIndex = -5;
            scene.AddChild(sp);
            var tw = CreateTween();
            if (i > 0) tw.TweenInterval(i * 0.04f);
            tw.TweenProperty(sp, "modulate:a", 0f, life);
            tw.TweenCallback(Callable.From(() => sp.QueueFree()));
        }
    }

    // ------------------------------------------------------------------
    // 大招斩击特效：在目标（BOSS）身上生成多道斩痕 + 最后一刀红色落下
    // ------------------------------------------------------------------
    /// <summary>在 BOSS 身上生成 count 道斩痕（围绕中心随机散布），最后一刀巨大红色落下。
    /// 进程 Always，故在顿帧冻结期间也能逐刀浮现，强化「乱刀斩 / 闪现斩」的画面感。</summary>
    public void SlashOnBoss(Boss boss, Color color, int count = 5, float spread = 46f)
    {
        if (boss == null || !IsInstanceValid(boss)) return;
        var scene = GetTree()?.CurrentScene;
        if (scene == null) return;
        var rnd = new Random();
        for (int i = 0; i < count; i++)
        {
            float ox = (float)(rnd.NextDouble() * 2 - 1) * spread;
            float oy = (float)(rnd.NextDouble() * 2 - 1) * spread;
            float rot = (float)(rnd.NextDouble() * Mathf.Pi);
            SpawnSlash(scene, boss.GlobalPosition + new Vector2(ox, oy), rot, color, 1f, i * 0.05f);
        }
        // 最后一刀：巨大红色斩击（略延迟，收尾落下）
        SpawnSlash(scene, boss.GlobalPosition, (float)(rnd.NextDouble() * 0.6 - 0.3), Colors.Red, 1.7f, count * 0.05f + 0.06f);
    }

    private void SpawnSlash(Node scene, Vector2 pos, float rot, Color color, float scale, float delay)
    {
        var poly = new Polygon2D();
        poly.Polygon = new Vector2[]
        {
            new Vector2(-55f, -6f), new Vector2(0f, -14f),
            new Vector2(55f, -6f), new Vector2(0f, 6f)
        };
        poly.Color = color;
        poly.Position = pos;
        poly.Rotation = rot;
        poly.Scale = new Vector2(scale * 0.4f, scale * 0.4f);
        poly.ZIndex = 50;
        poly.Modulate = new Color(1f, 1f, 1f, 0.95f);
        scene.AddChild(poly);
        var tw = CreateTween();
        if (delay > 0f) tw.TweenInterval(delay);
        tw.TweenProperty(poly, "scale", new Vector2(scale, scale), 0.10f);
        tw.TweenProperty(poly, "modulate:a", 0f, 0.20f);
        tw.TweenCallback(Callable.From(() => poly.QueueFree()));
    }
}
