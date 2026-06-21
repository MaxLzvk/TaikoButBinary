using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Numerics;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace TaikoButBinary;

public record SongEntry(string Title, string ChartPath)
{
    public static List<SongEntry> ScanCharts(string chartsRoot)
    {
        var list = new List<SongEntry>();
        if (!Directory.Exists(chartsRoot)) return list;
        foreach (var file in Directory.GetFiles(chartsRoot, "*.chart.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var chart = ChartLoader.Load(file);
                list.Add(new SongEntry(chart.Title ?? Path.GetFileNameWithoutExtension(file), file));
            }
            catch { }
        }
        list.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
        return list;
    }
}

