using System;
using System.Collections.Generic;
using System.Linq;

namespace TaikoButBinary;

public enum ModType
{
    Autoplay,
    Relax,
    Speed
}

public class Mod
{
    public ModType Type { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string ShortName { get; set; } = ""; // For display in results
    
    public static readonly Dictionary<ModType, Mod> AllMods = new()
    {
        {
            ModType.Autoplay, new Mod
            {
                Type = ModType.Autoplay,
                Name = "Autoplay",
                Description = "Watch the game play itself",
                ShortName = "AUTO"
            }
        },
        {
            ModType.Relax, new Mod
            {
                Type = ModType.Relax,
                Name = "Relax",
                Description = "Hit any key to hit notes, regardless of color",
                ShortName = "RELAX"
            }
        },
        {
            ModType.Speed, new Mod
            {
                Type = ModType.Speed,
                Name = "Speed",
                Description = "Adjust playback speed from 0.5x to 1.5x",
                ShortName = "SPEED"
            }
        }
    };
}

public static class ModManager
{
    private static readonly HashSet<ModType> activeMods = new();
    private static float speedModValue = 1.25f; // Default speed - 1.25x
    
    public static IReadOnlyCollection<ModType> ActiveMods => activeMods;
    
    public static float SpeedModValue
    {
        get => speedModValue;
        set
        {
            // Round to nearest 0.05
            float rounded = MathF.Round(value * 20f) / 20f;
            // Clamp between 0.5 and 1.5
            rounded = Math.Clamp(rounded, 0.5f, 1.5f);
            
            // Skip 1.0x - if between 0.975 and 1.075, snap to either 0.95 or 1.1
            if (rounded >= 0.975f && rounded <= 1.075f)
            {
                // Determine which side to snap to based on which is closer
                if (rounded < 1.025f)
                    speedModValue = 0.95f; // Snap down to 0.95
                else
                    speedModValue = 1.1f;  // Snap up to 1.1
            }
            else
            {
                speedModValue = rounded;
            }
        }
    }
    
    public static void ToggleMod(ModType modType)
    {
        if (activeMods.Contains(modType))
        {
            activeMods.Remove(modType);
        }
        else
        {
            // Handle mutual exclusions
            if (modType == ModType.Autoplay && activeMods.Contains(ModType.Relax))
            {
                activeMods.Remove(ModType.Relax); // Remove Relax when enabling Autoplay
            }
            else if (modType == ModType.Relax && activeMods.Contains(ModType.Autoplay))
            {
                activeMods.Remove(ModType.Autoplay); // Remove Autoplay when enabling Relax
            }
            
            activeMods.Add(modType);
        }
    }
    
    public static bool IsModActive(ModType modType)
    {
        return activeMods.Contains(modType);
    }
    
    public static void ClearMods()
    {
        activeMods.Clear();
    }
    
    public static string GetModsString()
    {
        if (activeMods.Count == 0) return "";
        
        var modNames = new List<string>();
        
        foreach (var modType in activeMods)
        {
            if (modType == ModType.Speed)
            {
                // Show speed value for Speed mod
                modNames.Add($"{SpeedModValue:0.0#}x");
            }
            else
            {
                // Use standard short name for other mods
                modNames.Add(Mod.AllMods[modType].ShortName);
            }
        }
        
        return string.Join(", ", modNames);
    }
}
