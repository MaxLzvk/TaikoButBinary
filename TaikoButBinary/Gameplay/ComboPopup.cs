using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Numerics;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace TaikoButBinary;

public class ComboPopup
{
    public string Text = "1";
    public Vector2 Pos;
    public float Age = 0f;
    public float Life = 0.6f;
    public Color Col = Color.White;
    public bool Dead => Age >= Life;
    public void Update(float dt) => Age += dt;
    public void Draw()
    {
        float t = MathF.Min(Age / Life, 1f);
        float scale = 1.0f + (1.3f - 1.0f) * (1f - t) * 0.6f;
        float opacity = 1f - t;
        var p = new Vector2(Pos.X, Pos.Y - t * 40f);
        int fontSize = (int)(28 * scale);
        Color c = Fade(Col, opacity);
        DrawText(Text, (int)p.X, (int)p.Y, fontSize, c);
    }
}
