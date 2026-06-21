using System.Text.Json.Serialization;

namespace TaikoButBinary;

public class Note
{
    public float Time { get; set; } // seconds into track
    // Type: 0 = '0' key (red), 1 = '1' key (blue), 2 = 'Space' (yellow)
    public int Type { get; set; }

    // runtime only
    [JsonIgnore] public bool Judged { get; set; }
    [JsonIgnore] public bool Hit { get; set; }
}
