using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Numerics;
using Raylib_cs;
using static Raylib_cs.Raylib;
using System.Diagnostics.Contracts;

namespace TaikoButBinary;

static class UI
{
    // -------- Buttons --------
    public static bool Button(Rectangle rect, string label)
    {
        Vector2 mp = GetMousePosition();
        bool hover = CheckCollisionPointRec(mp, rect);
        DrawButton(rect, label, hover);
        if (hover && IsMouseButtonReleased(MouseButton.Left)) return true;
        return false;
    }
    
    public static void DrawButton(Rectangle rect, string label, bool hover = false)
    {
        Color bg = hover ? new Color(70, 90, 130, 255) : new Color(40, 50, 70, 220);
        DrawRectangleRec(rect, bg);
        DrawRectangleLines((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height, new Color(200, 210, 230, 255));
        int tw = MeasureText(label, 24);
        DrawText(label, (int)(rect.X + rect.Width / 2 - tw / 2), (int)(rect.Y + rect.Height / 2 - 12), 24, Color.White);
    }

    // -------- Slider (float) --------
    public static void Slider(Rectangle rect, string label, ref float value, float min, float max, string? unit)
    {
        value = MathF.Min(max, MathF.Max(min, value));
        Vector2 mp = GetMousePosition();
        if (CheckCollisionPointRec(mp, rect) && IsMouseButtonDown(MouseButton.Left))
        {
            float t = (mp.X - rect.X) / rect.Width;
            t = MathF.Min(1f, MathF.Max(0f, t));
            value = min + t * (max - min);
        }
    }

    public static void DrawSlider(Rectangle rect, string label, float value, float min, float max, string? unit)
    {
        DrawText(label, (int)rect.X - 300, (int)rect.Y - 2, 22, Color.White);
        DrawRectangleRec(rect, new Color(30, 36, 50, 255));
        float t = (value - min) / (max - min);
        float knobX = rect.X + t * rect.Width;
        DrawRectangle((int)rect.X, (int)rect.Y + 8, (int)rect.Width, 8, new Color(60, 72, 100, 255));
        DrawRectangle((int)(rect.X), (int)rect.Y + 8, (int)(t * rect.Width), 8, new Color(120, 160, 220, 255));
        DrawCircle((int)knobX, (int)rect.Y + 12, 10, new Color(220, 230, 255, 255));

        string val = unit == null ? $"{value:0.##}" : $"{value:0.##} {unit}";
        DrawText(val, (int)(rect.X + rect.Width + 12), (int)rect.Y - 2, 20, Color.Gray);
    }

    // -------- Keybinds (simple capture) --------
    private static string? _capturingId = null;

    public static KeyboardKey KeyButton(Rectangle rect, string id, KeyboardKey current)
    {
        Vector2 mp = GetMousePosition();
        bool hover = CheckCollisionPointRec(mp, rect);
        bool clicked = hover && IsMouseButtonPressed(MouseButton.Left);

        if (clicked) _capturingId = id;

        // If we are capturing for this id, take the next key pressed
        if (_capturingId == id)
        {
            int k = GetKeyPressed();
            if (k != 0)
            {
                _capturingId = null;
                current = (KeyboardKey)k;
            }
        }

        // Draw
        Color bg = hover ? new Color(70, 90, 130, 255) : new Color(40, 50, 70, 220);
        DrawRectangleRec(rect, bg);
        DrawRectangleLines((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height, new Color(200, 210, 230, 255));

        string label = (_capturingId == id) ? "Press a key..." : current.ToString();
        int tw = MeasureText(label, 20);
        DrawText(label, (int)(rect.X + rect.Width / 2 - tw / 2), (int)(rect.Y + rect.Height / 2 - 10), 20, Color.White);

        return current;
    }

    public static void DrawKeyButton(Rectangle rect, KeyboardKey bind, string id)
    {
        Vector2 mp = GetMousePosition();
        bool hover = CheckCollisionPointRec(mp, rect);
        Color bg = hover ? new Color(70, 90, 130, 255) : new Color(40, 50, 70, 220);
        DrawRectangleRec(rect, bg);
        DrawRectangleLines((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height, new Color(200, 210, 230, 255));

        string label = (_capturingId == id) ? "Press a key..." : bind.ToString();
        int tw = MeasureText(label, 20);
        DrawText(label, (int)(rect.X + rect.Width / 2 - tw / 2), (int)(rect.Y + rect.Height / 2 - 10), 20, Color.White);
    }

    public static void DrawKeybindLabel(int x, int y, string text)
    {
        DrawText(text, x, y + 6, 22, Color.White);
    }

    // -------- Dropdown/Option Selection --------
    public static int OptionSelector(Rectangle rect, string[] options, int currentIndex)
    {
        Vector2 mp = GetMousePosition();
        bool hover = CheckCollisionPointRec(mp, rect);
        
        // Left arrow button
        Rectangle leftArrow = new Rectangle(rect.X, rect.Y, 30, rect.Height);
        bool clickedLeft = CheckCollisionPointRec(mp, leftArrow) && IsMouseButtonReleased(MouseButton.Left);
        
        // Right arrow button
        Rectangle rightArrow = new Rectangle(rect.X + rect.Width - 30, rect.Y, 30, rect.Height);
        bool clickedRight = CheckCollisionPointRec(mp, rightArrow) && IsMouseButtonReleased(MouseButton.Left);
        
        // Handle navigation
        if (clickedLeft)
        {
            currentIndex = (currentIndex - 1 + options.Length) % options.Length;
        }
        else if (clickedRight)
        {
            currentIndex = (currentIndex + 1) % options.Length;
        }
        
        return currentIndex;
    }

    public static void DrawOptionSelector(Rectangle rect, string[] options, int currentIndex)
    {
        Vector2 mp = GetMousePosition();
        
        // Main background
        DrawRectangleRec(rect, new Color(30, 36, 50, 255));
        DrawRectangleLines((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height, new Color(200, 210, 230, 255));
        
        // Left arrow
        Rectangle leftArrow = new Rectangle(rect.X, rect.Y, 30, rect.Height);
        bool hoverLeft = CheckCollisionPointRec(mp, leftArrow);
        Color leftBg = hoverLeft ? new Color(70, 90, 130, 255) : new Color(50, 60, 80, 255);
        DrawRectangleRec(leftArrow, leftBg);
        DrawText("<", (int)(leftArrow.X + leftArrow.Width/2 - 6), (int)(leftArrow.Y + leftArrow.Height/2 - 10), 20, Color.White);
        
        // Right arrow
        Rectangle rightArrow = new Rectangle(rect.X + rect.Width - 30, rect.Y, 30, rect.Height);
        bool hoverRight = CheckCollisionPointRec(mp, rightArrow);
        Color rightBg = hoverRight ? new Color(70, 90, 130, 255) : new Color(50, 60, 80, 255);
        DrawRectangleRec(rightArrow, rightBg);
        DrawText(">", (int)(rightArrow.X + rightArrow.Width/2 - 6), (int)(rightArrow.Y + rightArrow.Height/2 - 10), 20, Color.White);
        
        // Current option text
        if (currentIndex >= 0 && currentIndex < options.Length)
        {
            string currentOption = options[currentIndex];
            int textWidth = MeasureText(currentOption, 20);
            int textX = (int)(rect.X + rect.Width/2 - textWidth/2);
            int textY = (int)(rect.Y + rect.Height/2 - 10);
            DrawText(currentOption, textX, textY, 20, Color.White);
        }
    }

    // -------- Checkbox --------
    public static bool Checkbox(Rectangle rect, bool isChecked)
    {
        Vector2 mp = GetMousePosition();
        bool hover = CheckCollisionPointRec(mp, rect);
        bool clicked = hover && IsMouseButtonReleased(MouseButton.Left);
        
        if (clicked) isChecked = !isChecked;
        
        return isChecked;
    }

    public static void DrawCheckbox(Rectangle rect, bool isChecked)
    {
        Vector2 mp = GetMousePosition();
        bool hover = CheckCollisionPointRec(mp, rect);
        
        Color bg = hover ? new Color(70, 90, 130, 255) : new Color(40, 50, 70, 220);
        DrawRectangleRec(rect, bg);
        DrawRectangleLines((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height, new Color(200, 210, 230, 255));
        
        if (isChecked)
        {
            // Draw an X mark using two lines instead of a unicode glyph to avoid font/encoding issues
            // inset the lines slightly so they don't touch the rectangle border
            float inset = MathF.Max(2f, MathF.Min(rect.Width, rect.Height) * 0.18f);
            Vector2 a1 = new Vector2(rect.X + inset, rect.Y + inset);
            Vector2 a2 = new Vector2(rect.X + rect.Width - inset, rect.Y + rect.Height - inset);
            Vector2 b1 = new Vector2(rect.X + rect.Width - inset, rect.Y + inset);
            Vector2 b2 = new Vector2(rect.X + inset, rect.Y + rect.Height - inset);

            // Use a solid color with good contrast
            Color xCol = new Color(230, 230, 230, 255);

            // Draw thicker lines for the X
            DrawLineEx(a1, a2, 3f, xCol);
            DrawLineEx(b1, b2, 3f, xCol);
        }
    }
}

