using Godot;
using System;

namespace BreakingRules;

/// <summary>
/// 玩家设置（持久化到 user://settings.cfg）。
///   · 分辨率：预设 16:9 窗口尺寸（仅在窗口模式下生效，拉伸仍按 960×540 视口 letterbox）。
///   · 主音量：映射到 Master 总线 dB，覆盖 BGM 与全部音效。
/// 静态单例，启动时 Load()，改动后 Save()。
/// </summary>
public static class Settings
{
    private const string CfgPath = "user://settings.cfg";

    /// <summary>预设 16:9 分辨率（标签 / 宽 / 高）。</summary>
    public static readonly (string label, int w, int h)[] Resolutions =
    {
        ("720p", 1280, 720),
        ("900p", 1600, 900),
        ("1080p", 1920, 1080),
        ("1440p", 2560, 1440),
        ("4K", 3840, 2160),
    };

    public static int ResolutionIndex { get; set; } = 0;   // 默认 720p
    public static float MasterVolume { get; set; } = 1f;   // 0..1

    public static void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(CfgPath) == Error.Ok)
        {
            ResolutionIndex = cfg.GetValue("video", "resolution_index").AsInt32();
            MasterVolume = cfg.GetValue("audio", "master_volume").AsSingle();
        }
        Clamp();
    }

    public static void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("video", "resolution_index", ResolutionIndex);
        cfg.SetValue("audio", "master_volume", MasterVolume);
        cfg.Save(CfgPath);
    }

    public static void Clamp()
    {
        ResolutionIndex = Mathf.Clamp(ResolutionIndex, 0, Resolutions.Length - 1);
        MasterVolume = Mathf.Clamp(MasterVolume, 0f, 1f);
    }

    /// <summary>把 Master 音量（0..1）映射为 dB 并写入音频总线。0 视为静音（-80dB）。</summary>
    public static void ApplyVolume()
    {
        float v = MasterVolume;
        float db = v <= 0.001f ? -80f : 20f * (float)Math.Log10(v);
        AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex("Master"), db);
    }

    /// <summary>把预设分辨率写入窗口尺寸。</summary>
    public static void ApplyResolution()
    {
        var r = Resolutions[ResolutionIndex];
        DisplayServer.WindowSetSize(new Vector2I(r.w, r.h));
    }

    public static string ResolutionLabel()
    {
        var r = Resolutions[ResolutionIndex];
        return $"{r.w}×{r.h} ({r.label})";
    }

    public static string VolumeLabel() => $"{Mathf.RoundToInt(MasterVolume * 100f)}%";
}
