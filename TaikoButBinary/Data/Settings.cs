using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Numerics;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace TaikoButBinary;

public class Settings
{
    public float ScrollSpeed { get; set; }
    public float MasterVolume { get; set; } = 0.8f;
    public float MusicVolume { get; set; } = 0.7f;
    public float SfxVolume { get; set; } = 0.8f;
    public int JudgementOffsetMs { get; set; } // +/- in ms
    public bool ShowAccuracyBar { get; set; } = true;

    // Display settings
    public int ResolutionWidth { get; set; } = 1280;
    public int ResolutionHeight { get; set; } = 720;
    public int TargetFPS { get; set; } = 60;
    public bool VSync { get; set; } = false;
    public bool Fullscreen { get; set; } = false;

    public Binding HitOne { get; set; }
    public Binding HitZero { get; set; }
    public Binding HitSecial { get; set; }

    // Hold-to-restart binding and delay (seconds)
    public Binding RestartHold { get; set; }
    public float RestartHoldDelaySec { get; set; }

    // Pause toggle and unpause delay (seconds) — new
    public Binding PauseToggle { get; set; }
    public float UnpauseDelaySec { get; set; }

    // Editor-specific keybinds for note placement
    public EditorKeybinds EditorKeys { get; set; } = new EditorKeybinds();
}

public class EditorKeybinds
{
    public Binding EditorKeyLeft { get; set; } = new Binding(KeyboardKey.One, KeyboardKey.Null);
    public Binding EditorKeyRight { get; set; } = new Binding(KeyboardKey.Zero, KeyboardKey.Null);
    public Binding EditorKeyBig { get; set; } = new Binding(KeyboardKey.Space, KeyboardKey.Null);
}
