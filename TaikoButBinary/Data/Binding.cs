using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Numerics;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace TaikoButBinary;

public struct Binding
{
    public KeyboardKey Primary;
    public KeyboardKey Alt;

    public Binding(KeyboardKey primary, KeyboardKey alt)
    {
        Primary = primary; Alt = alt;
    }
    public bool IsPressed() => (Primary != KeyboardKey.Null && IsKeyPressed(Primary)) || (Alt != KeyboardKey.Null && IsKeyPressed(Alt));
    public bool IsDown() => (Primary != KeyboardKey.Null && IsKeyDown(Primary)) || (Alt != KeyboardKey.Null && IsKeyDown(Alt));
}
