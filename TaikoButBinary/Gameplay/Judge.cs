using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Numerics;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace TaikoButBinary;

public class Judge
{
    public int Score;
    public int Combo;
    public int MaxCombo;
    public int Perfect;
    public int Good;
    public int Miss;

    public int TotalJudged => Perfect + Good + Miss;
    public float AccuracyPercent
        => TotalJudged == 0 ? 0f : (Perfect + 0.5f * Good) / TotalJudged * 100f;

    public string Grade
    {
        get
        {
            float acc = AccuracyPercent;
            if (TotalJudged > 0 && Math.Abs(acc - 100f) < 0.0001f) return "SS";
            if (acc >= 95f) return "S";
            if (acc >= 90f) return "A";
            if (acc >= 85f) return "B";
            if (acc >= 80f) return "C";
            if (acc < 70f) return "D";
            return "C"; // 70–80% defaults to C
        }
    }
}