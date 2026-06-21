using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Numerics;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace TaikoButBinary;

public class Chart
{
    public string Title { get; set; } = "Untitled";
    public string Audio { get; set; } = "";
    /// <summary>
    /// Optional preroll (in seconds). Also used as the "Audio Start" marker in the editor.
    /// </summary>
    public float Offset_sec { get; set; } = 0f;

    // NEW: Persist editor timing settings
    public float Bpm { get; set; } = 120f;
    public int BeatsPerBar { get; set; } = 4;
    public float GridOffsetSec { get; set; } = 0f;

    public List<Note> Notes { get; set; } = new();

    // Runtime-only serialization skips if your Note has JsonIgnore bits inside
    [JsonIgnore] public bool Dirty { get; set; } = false;
}

public static class ChartLoader
{
    public static Chart Load(string chartPath)
    {
        string json = File.ReadAllText(chartPath);
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        Chart? c = JsonSerializer.Deserialize<Chart>(json, opts);
        if (c == null) throw new Exception("Failed to parse chart");
        return c;
    }

    public static void Save(string chartPath, Chart chart)
    {
        var opts = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
        };
        string json = JsonSerializer.Serialize(chart, opts);
        File.WriteAllText(chartPath, json);
    }
}
