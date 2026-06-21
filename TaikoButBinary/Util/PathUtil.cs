using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Numerics;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace TaikoButBinary;

static class PathUtil
{
    public static string ResolveAudioPath(string chartPath, string audioField, out List<string> attemptedPaths)
    {
        attemptedPaths = new List<string>();

        if (Path.IsPathRooted(audioField))
        {
            attemptedPaths.Add(audioField);
            return audioField;
        }

        string audioRel = audioField.Replace('/', Path.DirectorySeparatorChar)
                                    .Replace('\\', Path.DirectorySeparatorChar);

        string chartsDir = Path.GetDirectoryName(chartPath)!;
        string chartsParent = Path.GetDirectoryName(chartsDir)!;
        string exeDir = AppContext.BaseDirectory;
        string projectRootGuess = Path.GetFullPath(Path.Combine(exeDir, "..", "..", ".."));

        var candidates = new[]
        {
            Path.Combine(chartsDir, audioRel),
            Path.Combine(chartsParent, audioRel),
            Path.Combine(exeDir, audioRel),
            Path.Combine(projectRootGuess, audioRel),
            Path.Combine(Path.GetDirectoryName(projectRootGuess) ?? projectRootGuess, audioRel)
        };

        foreach (var p in candidates)
        {
            attemptedPaths.Add(p);
            if (File.Exists(p)) return p;
        }

        return candidates.Length > 1 ? candidates[1] : audioRel;
    }
}
