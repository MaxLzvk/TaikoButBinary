using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Raylib_cs;
using NativeFileDialogNET;
using static Raylib_cs.Raylib;

namespace TaikoButBinary;

public class Editor : IDisposable
{
    private readonly SongEntry song;
    private readonly Chart chart;
    public Music Music { get; private set; }

    // sound effects for note placement feedback
    private readonly Sound[] hitSounds;
    private int hitSoundIndex = 0;
    
    // for playing hit sounds when cursor passes notes during playback
    private float lastCursor = 0f;

    // timeline
    private float cursor = 0f;              // seconds
    private float viewStart = 0f;           // left edge time (sec)
    private float secPerPixel = 0.02f;      // zoom: smaller = zoom in
    private const float MinSecPerPx = 0.002f;
    private const float MaxSecPerPx = 0.2f;

    // BPM/time signature (persisted to chart)
    private float bpm = 120f;
    private int beatsPerBar = 4;      // time signature numerator
    private float gridOffsetSec = 0f; // shift the grid to line up with music
    private bool snapEnabled = false;
    private int snapDivisor = 4;      // beats per bar subdivision (e.g., 4=quarter, 8=eighth)
    private bool autoResnapOnBpmChange = true;

    // note picking/dragging
    private int selectedIndex = -1;
    private bool draggingNote = false;
    private float dragStartMouseTime = 0f;
    private float dragStartNoteTime = 0f;
    private readonly Dictionary<Note, float> multiDragOffsets = new();

    // cursor drag and multi-select
    private bool draggingCursor = false;
    private bool multiSelecting = false;
    private Vector2 selStart, selEnd;
    private readonly HashSet<Note> selectedNotes = new();

    // lanes
    private const float laneMidY = 360f;
    private const float laneSep = 70f;

    // playtest
    private bool playtest = false;
    private float playSongTime = 0f;
    private float playStartTime = 0f;
    private bool seekFailedMuteGate = false;

    // save toast
    private float saveToastTimer = 0f;

    // overlays
    private bool showSettings = false;
    private bool showHelp = false;

    // editor keybinds
    private EditorBindings binds = EditorBindings.Default();

    // key-capture state (for settings overlay)
    private bool capturing = false;
    private string capturingId = "";
    private bool capturingAlt = false;

    // right-click pan
    private bool panning = false;
    private float panStartX = 0f;
    private float panStartViewStart = 0f;

    // settings overlay scroll
    private float settingsScroll = 0f;

    // scrollbar state
    private bool draggingScroll = false;
    private float scrollDragOffsetX = 0f;

    // rename dialog state (in-editor)
    private bool renameOpen = false;
    private string renameText = "";

    // change-audio dialog
    private bool changeAudioOpen = false;
    private string changeAudioText = "";

    // note context menu
    private bool noteMenuOpen = false;
    private int noteMenuForIndex = -1;
    private Rectangle noteMenuRect;

    // clipboard (copy/paste/duplicate)
    private List<Note> clipboard = new();

    // Add these fields to the Editor class
    private float changeAudioScroll = 0f;
    private List<AudioEntry> audioEntries = new();

    // Struct to represent audio files and their metadata
    private struct AudioEntry
    {
        public string Name;
        public string FullPath;
        public float DurationSec;
    }

    public Editor(SongEntry s, Chart c, Music m)
    {
        song = s; chart = c;

        // adopt persisted timing if present
        if (chart.Bpm > 0f) bpm = chart.Bpm;
        if (chart.BeatsPerBar > 0) beatsPerBar = chart.BeatsPerBar;
        gridOffsetSec = chart.GridOffsetSec;

        Music = m;
        try { StopMusicStream(Music); } catch { /* ignore */ }
        SetMusicVolume(Music, Program.Settings.MasterVolume * Program.Settings.MusicVolume);

        // Load hit sounds for note placement feedback
        hitSounds = new Sound[4]; // Multiple instances for overlapping
        for (int i = 0; i < hitSounds.Length; i++)
        {
            hitSounds[i] = LoadSound("resources/sfx/hit_sound.mp3");
            SetSoundVolume(hitSounds[i], Program.Settings.MasterVolume * Program.Settings.SfxVolume);
        }

        SortNotes();
        RefreshAudioList();
    }

    public bool CanExitToSelect() => !playtest && !showSettings && !showHelp && !capturing && !renameOpen && !changeAudioOpen;

    public void Update()
    {
        float dt = GetFrameTime();

        // overlays take priority
        if (IsKeyPressed(KeyboardKey.Escape))
        {
            if (capturing) capturing = false;
            else if (renameOpen) renameOpen = false;
            else if (changeAudioOpen) changeAudioOpen = false;
            else if (showSettings) showSettings = false;
            else if (showHelp) showHelp = false;
        }

        // capture next key for rebinding
        if (capturing)
        {
            KeyboardKey pressed = GetAnyPressedKey();
            if (pressed != KeyboardKey.Null)
            {
                ApplyCapturedKey(capturingId, pressed, capturingAlt);
                capturing = false;
            }
            return; // block editor while capturing
        }

        // Ctrl+S save
        if (!showSettings && !showHelp && !renameOpen && !changeAudioOpen)
        {
            if (IsKeyDown(KeyboardKey.LeftControl) || IsKeyDown(KeyboardKey.RightControl))
                if (IsKeyPressed(KeyboardKey.S)) Save();
        }

        // toggle help via bind
        if (!showSettings && binds.ShowHelp.IsPressed()) showHelp = !showHelp;

        // pause editor input while an overlay is open
        if (showSettings || showHelp || renameOpen || changeAudioOpen)
        {
            // still update toast fade
            if (saveToastTimer > 0f) saveToastTimer -= dt;
            return;
        }

        if (!playtest)
        {
            // layout needed for scrollbar interactions
            int w = GetScreenWidth();
            int h = GetScreenHeight();
            int barH = 64;
            int top = Math.Max(110, barH + 16);
            int bottom = h - 56; // reserve space for scrollbar

            // compute total duration from music length (fallback to last note)
            float totalDuration = GetSafeMusicLength();
            float lastNote = chart.Notes.Count > 0 ? chart.Notes[^1].Time : 0f;
            if (totalDuration <= 0f) totalDuration = MathF.Max(lastNote + 2f, 10f);

            // Scrollbar between timeline and bottom
            Rectangle scrollRect = new Rectangle(20, bottom + 12, w - 40, 12);
            float visibleSec = w * secPerPixel;
            float maxViewStart = MathF.Max(0f, totalDuration - visibleSec);
            if (viewStart > maxViewStart) viewStart = maxViewStart;

            // thumb
            float thumbW = MathF.Max(6f, (visibleSec / MathF.Max(visibleSec, totalDuration)) * scrollRect.Width);
            float thumbX = scrollRect.X + (viewStart / MathF.Max(0.0001f, totalDuration)) * scrollRect.Width;
            thumbX = MathF.Min(scrollRect.X + scrollRect.Width - thumbW, MathF.Max(scrollRect.X, thumbX));
            Rectangle thumbRect = new Rectangle(thumbX, scrollRect.Y, thumbW, scrollRect.Height);

            // handle scrollbar mouse
            Vector2 mp = GetMousePosition();
            bool mpInScroll = CheckCollisionPointRec(mp, scrollRect);

            if (IsMouseButtonPressed(MouseButton.Left))
            {
                if (mpInScroll)
                {
                    if (CheckCollisionPointRec(mp, thumbRect))
                    {
                        draggingScroll = true;
                        scrollDragOffsetX = mp.X - thumbRect.X;
                    }
                    else
                    {
                        // click on track -> center around click
                        float clickT = (mp.X - scrollRect.X) / MathF.Max(1f, scrollRect.Width);
                        clickT = MathF.Min(1f, MathF.Max(0f, clickT));
                        float target = clickT * totalDuration - visibleSec * 0.5f;
                        viewStart = MathF.Min(maxViewStart, MathF.Max(0f, target));

                        // near end -> snap to end and move cursor to end of audio
                        if (mp.X >= scrollRect.X + scrollRect.Width - 6)
                        {
                            viewStart = maxViewStart;
                            cursor = totalDuration;
                        }
                    }
                }
            }

            if (IsMouseButtonDown(MouseButton.Left) && draggingScroll)
            {
                float newThumbX = mp.X - scrollDragOffsetX;
                float t = (newThumbX - scrollRect.X) / MathF.Max(1f, scrollRect.Width - thumbW);
                t = MathF.Min(1f, MathF.Max(0f, t));
                viewStart = t * MathF.Max(0f, totalDuration - visibleSec);
                viewStart = MathF.Min(maxViewStart, MathF.Max(0f, viewStart));
            }

            if (IsMouseButtonReleased(MouseButton.Left) && draggingScroll)
            {
                draggingScroll = false;
                if (viewStart >= maxViewStart - 0.0001f) cursor = totalDuration; // scroll end -> cursor to end
            }

            // step size for cursor nudge
            float step = 0.05f;
            if (IsKeyDown(KeyboardKey.LeftShift) || IsKeyDown(KeyboardKey.RightShift)) step = 0.005f;
            if (IsKeyDown(KeyboardKey.LeftControl) || IsKeyDown(KeyboardKey.RightControl)) step = 0.25f;

            // cursor L/R (no A/D panning per request)
            if (!snapEnabled)
            {
                if (binds.CursorLeft.IsDown()) cursor = MathF.Max(0f, cursor - step);
                if (binds.CursorRight.IsDown()) cursor = MathF.Max(0f, cursor + step);
            }
            else
            {
                if (binds.CursorLeft.IsPressed()) cursor = PrevGrid(cursor);
                if (binds.CursorRight.IsPressed()) cursor = NextGrid(cursor);
            }

            // snapping
            if (binds.ToggleSnap.IsPressed()) snapEnabled = !snapEnabled;
            if (binds.SnapDivDown.IsPressed()) snapDivisor = Math.Max(1, snapDivisor / 2);
            if (binds.SnapDivUp.IsPressed()) snapDivisor = Math.Min(64, snapDivisor * 2);

            // zoom (keyboard)
            if (binds.ZoomIn.IsPressed()) ZoomAt(cursor, 0.85f);
            if (binds.ZoomOut.IsPressed()) ZoomAt(cursor, 1f / 0.85f);

            // RMB pan start or note context
            if (IsMouseButtonPressed(MouseButton.Right))
            {
                // if over a note -> open context; else start panning
                int at = FindNoteAt(mp, out _);
                if (at >= 0)
                {
                    noteMenuOpen = true;
                    noteMenuForIndex = at;
                    noteMenuRect = new Rectangle(mp.X, mp.Y, 180, 96);
                }
                else
                {
                    panning = true;
                    panStartX = mp.X;
                    panStartViewStart = viewStart;

                    // cancel other interactions
                    draggingNote = false;
                    draggingCursor = false;
                    multiSelecting = false;
                    noteMenuOpen = false;
                }
            }
            // RMB pan move
            if (IsMouseButtonDown(MouseButton.Right) && panning)
            {
                float dx = GetMousePosition().X - panStartX; // pixels
                viewStart = MathF.Max(0f, panStartViewStart - dx * secPerPixel);
            }
            // RMB pan end
            if (IsMouseButtonReleased(MouseButton.Right)) panning = false;

            // mouse-wheel zoom at pointer
            float wheel = GetMouseWheelMove();
            if (wheel != 0)
            {
                float mouseTime = XToTime(GetMousePosition().X);
                float factor = MathF.Pow(0.85f, wheel); // >0 => zoom in
                ZoomAt(mouseTime, factor);
            }

            // mouse → time
            var mousePos = GetMousePosition();
            float pointedTime = XToTime(mousePos.X);

            // add notes with editor-specific binds
            if (Program.Settings.EditorKeys.EditorKeyLeft.IsPressed()) AddNoteAt(GetPlaceTime(), 1);
            if (Program.Settings.EditorKeys.EditorKeyRight.IsPressed()) AddNoteAt(GetPlaceTime(), 0);
            if (Program.Settings.EditorKeys.EditorKeyBig.IsPressed()) AddNoteAt(GetPlaceTime(), 2);

            bool shiftDown = IsKeyDown(KeyboardKey.LeftShift) || IsKeyDown(KeyboardKey.RightShift);
            bool ctrlDown = IsKeyDown(KeyboardKey.LeftControl) || IsKeyDown(KeyboardKey.RightControl);

            // copy/paste/duplicate/select all
            if (ctrlDown && IsKeyPressed(KeyboardKey.A))
            {
                selectedNotes.Clear();
                foreach (var n in chart.Notes) selectedNotes.Add(n);
            }
            if (ctrlDown && IsKeyPressed(KeyboardKey.C))
            {
                clipboard = new List<Note>();
                foreach (var n in chart.Notes) if (selectedNotes.Contains(n)) clipboard.Add(new Note { Time = n.Time, Type = n.Type });
            }
            if (ctrlDown && IsKeyPressed(KeyboardKey.V) && clipboard.Count > 0)
            {
                float baseTime = float.PositiveInfinity;
                foreach (var n in clipboard) baseTime = MathF.Min(baseTime, n.Time);
                float offset = GetPlaceTime() - baseTime;
                selectedNotes.Clear();
                foreach (var n in clipboard)
                {
                    float t = n.Time + offset;
                    if (snapEnabled) t = SnapTime(t);
                    var nn = new Note { Time = MathF.Max(0, (float)Math.Round(t, 3)), Type = n.Type };
                    chart.Notes.Add(nn);
                    selectedNotes.Add(nn);
                }
                SortNotes();
            }
            if (ctrlDown && IsKeyPressed(KeyboardKey.D))
            {
                // duplicate selection by +1 beat
                if (selectedNotes.Count > 0)
                {
                    float spb = 60f / MathF.Max(1f, bpm);
                    var dup = new List<Note>();
                    foreach (var n in chart.Notes) if (selectedNotes.Contains(n)) dup.Add(n);
                    selectedNotes.Clear();
                    foreach (var n in dup)
                    {
                        float t = n.Time + spb;
                        if (snapEnabled) t = SnapTime(t);
                        var nn = new Note { Time = MathF.Max(0, (float)Math.Round(t, 3)), Type = n.Type };
                        chart.Notes.Add(nn);
                        selectedNotes.Add(nn);
                    }
                    SortNotes();
                }
            }

            // Define the editor content zone where dragging should work
            // This excludes the top bar (UI buttons) and bottom scrollbar area
            Rectangle editorZone = new Rectangle(0, top, w, bottom - top);

            // interactions (disabled while panning)
            if (!panning)
            {
                if (IsMouseButtonPressed(MouseButton.Left))
                {
                    Vector2 mp2 = GetMousePosition();
                    selectedIndex = FindNoteAt(mp2, out _);

                    if (selectedIndex >= 0)
                    {
                        var note = chart.Notes[selectedIndex];
                        if (shiftDown)
                        {
                            selectedNotes.Add(note);
                            draggingNote = false;
                        }
                        else
                        {
                            // start multi-drag; if note not in selection, make it the only one
                            if (!selectedNotes.Contains(note))
                            {
                                selectedNotes.Clear();
                                selectedNotes.Add(note);
                            }
                            draggingNote = true;
                            dragStartMouseTime = XToTime(mp2.X);
                            multiDragOffsets.Clear();
                            foreach (var n in selectedNotes) multiDragOffsets[n] = n.Time;
                            dragStartNoteTime = note.Time;
                        }
                        // close note menu if open
                        noteMenuOpen = false;
                    }
                    else
                    {
                        if (shiftDown)
                        {
                            multiSelecting = true;
                            selStart = mp2;
                            selEnd = mp2;
                            draggingNote = false;
                            draggingCursor = false;
                        }
                        else
                        {
                            // Only allow cursor dragging if mouse is within the editor zone
                            if (CheckCollisionPointRec(mp2, editorZone))
                            {
                                selectedNotes.Clear();
                                draggingCursor = true;
                                cursor = MathF.Max(0f, pointedTime);
                                if (snapEnabled) cursor = SnapTime(cursor);
                            }
                        }
                    }
                }

                if (IsMouseButtonDown(MouseButton.Left))
                {
                    var mp2 = GetMousePosition();
                    float mt = XToTime(mp2.X);

                    if (draggingNote && selectedIndex >= 0)
                    {
                        float delta = mt - dragStartMouseTime;
                        foreach (var pair in multiDragOffsets)
                        {
                            float t = MathF.Max(0f, pair.Value + delta);
                            if (snapEnabled) t = SnapTime(t);
                            pair.Key.Time = t;
                        }
                        SortNotes();
                    }
                    else if (draggingCursor)
                    {
                        // Only continue cursor dragging if mouse is still within the editor zone
                        if (CheckCollisionPointRec(mp2, editorZone))
                        {
                            cursor = MathF.Max(0f, mt);
                            if (snapEnabled) cursor = SnapTime(cursor);
                        }
                    }
                    else if (multiSelecting)
                    {
                        selEnd = mp2;
                        UpdateMarqueeSelection(selStart, selEnd);
                    }
                }

                if (IsMouseButtonReleased(MouseButton.Left))
                {
                    draggingNote = false;
                    draggingCursor = false;
                    multiSelecting = false;
                }
            }

            // note context menu actions
            if (noteMenuOpen)
            {
                // draw handled in Draw(); here only handle click-outside close
                if (IsMouseButtonPressed(MouseButton.Left))
                {
                    if (!CheckCollisionPointRec(GetMousePosition(), noteMenuRect))
                        noteMenuOpen = false;
                }
            }

            // delete
            if (binds.DeleteSelected.IsPressed())
            {
                if (selectedNotes.Count > 0)
                {
                    chart.Notes.RemoveAll(n => selectedNotes.Contains(n));
                    selectedNotes.Clear();
                    selectedIndex = -1;
                }
                else if (selectedIndex >= 0)
                {
                    chart.Notes.RemoveAt(selectedIndex);
                    selectedIndex = -1;
                }
            }

            // play
            if (binds.PlayToggle.IsPressed()) StartPlaytestAt(cursor);

            // BPM/time-signature/offset controls with optional auto-resnap
            if (IsKeyDown(KeyboardKey.LeftControl) || IsKeyDown(KeyboardKey.RightControl))
            {
                bool changed = false;
                if (IsKeyPressed(KeyboardKey.B)) { bpm = MathF.Min(300f, bpm + 1f); changed = true; }
                if (IsKeyPressed(KeyboardKey.N)) { bpm = MathF.Max(10f, bpm - 1f); changed = true; }
                if (IsKeyPressed(KeyboardKey.RightBracket)) { beatsPerBar = Math.Min(16, beatsPerBar + 1); changed = true; }
                if (IsKeyPressed(KeyboardKey.LeftBracket)) { beatsPerBar = Math.Max(1, beatsPerBar - 1); changed = true; }
                if (IsKeyPressed(KeyboardKey.G)) { gridOffsetSec += 0.01f; changed = true; }
                if (IsKeyPressed(KeyboardKey.F)) { gridOffsetSec -= 0.01f; changed = true; }

                if (changed)
                {
                    if (autoResnapOnBpmChange)
                    {
                        ResnapNotesToGrid(selectedNotes.Count > 0 ? selectedNotes : null);
                    }
                    // persist timing immediately
                    PersistTiming();
                }
            }
        }
        else
        {
            // PLAYTEST running
            playSongTime += dt;
            UpdateMusicStream(Music);

            if (seekFailedMuteGate && playSongTime >= playStartTime)
            {
                SetMusicVolume(Music, 1f);
                seekFailedMuteGate = false;
            }

            if (binds.PlayToggle.IsPressed())
            {
                StopMusicStream(Music);
                SetMusicVolume(Music, Program.Settings.MasterVolume * Program.Settings.MusicVolume);
                playtest = false;
            }

            // Enable navigation controls during playtest
            Vector2 mp = GetMousePosition();
            
            // step size for cursor nudge
            float step = 0.05f;
            if (IsKeyDown(KeyboardKey.LeftShift) || IsKeyDown(KeyboardKey.RightShift)) step = 0.005f;
            if (IsKeyDown(KeyboardKey.LeftControl) || IsKeyDown(KeyboardKey.RightControl)) step = 0.25f;

            // cursor L/R during playtest
            if (!snapEnabled)
            {
                if (binds.CursorLeft.IsDown()) cursor = MathF.Max(0f, cursor - step);
                if (binds.CursorRight.IsDown()) cursor = MathF.Max(0f, cursor + step);
            }
            else
            {
                if (binds.CursorLeft.IsPressed()) cursor = PrevGrid(cursor);
                if (binds.CursorRight.IsPressed()) cursor = NextGrid(cursor);
            }

            // zoom (keyboard) during playtest
            if (binds.ZoomIn.IsPressed()) ZoomAt(cursor, 0.85f);
            if (binds.ZoomOut.IsPressed()) ZoomAt(cursor, 1f / 0.85f);

            // RMB pan during playtest
            if (IsMouseButtonPressed(MouseButton.Right))
            {
                panning = true;
                panStartX = mp.X;
                panStartViewStart = viewStart;
            }
            if (IsMouseButtonDown(MouseButton.Right) && panning)
            {
                float dx = GetMousePosition().X - panStartX;
                viewStart = MathF.Max(0f, panStartViewStart - dx * secPerPixel);
            }
            if (IsMouseButtonReleased(MouseButton.Right)) panning = false;

            // mouse-wheel zoom during playtest
            float wheel = GetMouseWheelMove();
            if (wheel != 0)
            {
                float mouseTime = XToTime(GetMousePosition().X);
                float factor = MathF.Pow(0.85f, wheel);
                ZoomAt(mouseTime, factor);
            }
            
            // Only during playtest: check if playhead moved forward and play hit sounds
            if (playSongTime > lastCursor)
            {
                CheckForNotesToPlay(lastCursor, playSongTime);
            }
            lastCursor = playSongTime;
        }
        
        // Update lastCursor for non-playtest mode (but don't play sounds)
        if (!playtest)
        {
            UpdateMusicStream(Music);
            lastCursor = cursor;
        }

        // toast timer
        if (saveToastTimer > 0f) saveToastTimer -= dt;
    }

    public void Draw()
    {
        int w = GetScreenWidth();
        int h = GetScreenHeight();

        // ===== Top bar =====
        int barH = 64;
        DrawRectangle(0, 0, w, barH, new Color(18, 22, 30, 255));
        string title = string.IsNullOrWhiteSpace(chart.Title)
            ? Path.GetFileNameWithoutExtension(song.ChartPath)
            : chart.Title;
        DrawText($"Editor — {title}", 20, 18, 28, Color.White);

        // small info line under title
        string info = $"BPM: {bpm:0.##}    TimeSig: {beatsPerBar}/4    Grid Offset: {gridOffsetSec:0.000}s    Snap: {(snapEnabled ? $"ON 1/{snapDivisor}" : "OFF")}";
        DrawText(info, 20, 18 + 28, 16, Color.Gray);

        // Buttons anchored to the RIGHT
        int btnH = 28;
        int btnGap = 8;
        string lblRename = "Rename";
        string lblSettings = "Settings";
        string lblHelp = "Help";
        string lblChange = "Change Audio";
        int rw = MeasureText(lblRename, 18) + 28;
        int sw = MeasureText(lblSettings, 18) + 28;
        int hw = MeasureText(lblHelp, 18) + 28;
        int cw = MeasureText(lblChange, 18) + 28;

        int totalW = rw + btnGap + cw + btnGap + sw + btnGap + hw;
        int rightMargin = 18;
        int bx = w - rightMargin - totalW;
        int by = barH - btnH - 8;

        Rectangle btnRename = new Rectangle(bx, by, rw, btnH);
        Rectangle btnChange = new Rectangle(bx + rw + btnGap, by, cw, btnH);
        Rectangle btnSettings = new Rectangle(bx + rw + btnGap + cw + btnGap, by, sw, btnH);
        Rectangle btnHelp = new Rectangle(bx + rw + btnGap + cw + btnGap + sw + btnGap, by, hw, btnH);

        bool renameClicked = DrawButton(btnRename, lblRename);
        bool changeClicked = DrawButton(btnChange, lblChange);
        bool settingsClicked = DrawButton(btnSettings, lblSettings);
        bool helpClicked = DrawButton(btnHelp, lblHelp);

        // ===== Timeline =====
        int top = Math.Max(110, barH + 16);
        int bottom = h - 56; // reserve for scrollbar area
        DrawRectangle(0, top, w, bottom - top, new Color(20, 24, 34, 255));

        // Lanes
        int yUp = (int)(laneMidY - laneSep);
        int yMid = (int)(laneMidY);
        int yDown = (int)(laneMidY + laneSep);
        DrawLine(0, yUp, w, yUp, new Color(60, 90, 120, 180));
        DrawLine(0, yMid, w, yMid, new Color(200, 180, 80, 180));
        DrawLine(0, yDown, w, yDown, new Color(120, 80, 70, 180));
        DrawText("Type 1", 20, yUp - 24, 18, Color.Gray);
        DrawText("Space", 20, yMid - 24, 18, Color.Gray);
        DrawText("Type 0", 20, yDown + 6, 18, Color.Gray);

        // Grid
        DrawBeatGrid(top, bottom, w);

        // Notes
        for (int i = 0; i < chart.Notes.Count; i++)
        {
            var n = chart.Notes[i];
            float x = TimeToX(n.Time);
            if (x < -50 || x > w + 50) continue;

            float y = n.Type switch { 1 => yUp, 0 => yDown, 2 => yMid, _ => yMid };
            Rectangle rect = new Rectangle(x - 18, y - 18, 36, 36);
            Color baseCol = n.Type switch
            {
                1 => new Color(120, 200, 255, 255),
                0 => new Color(255, 140, 120, 255),
                2 => new Color(255, 230, 120, 255),
                _ => new Color(200, 200, 200, 255)
            };

            if (i == selectedIndex || selectedNotes.Contains(n))
            {
                Rectangle glow = new Rectangle(rect.X - 4, rect.Y - 4, rect.Width + 8, rect.Height + 8);
                DrawRectangleRounded(glow, 0.3f, 8, new Color(255, 255, 255, 100));
            }

            DrawRectangleRounded(rect, 0.3f, 8, baseCol);
            string label = n.Type == 2 ? "⎵" : (n.Type == 1 ? "1" : "0");
            DrawText(label, (int)(x - 8), (int)(y - 12), 24, new Color(20, 20, 24, 255));
        }

        // Cursor
        int cx = (int)TimeToX(cursor);
        DrawLine(cx, top, cx, bottom, new Color(240, 240, 240, 220));
        DrawText($"Cursor: {cursor:0.000}s", cx + 6, top - 26, 18, Color.White);

        // Playhead
        if (playtest)
        {
            int px = (int)TimeToX(playSongTime);
            DrawLine(px, top, px, bottom, new Color(160, 255, 160, 200));
            DrawText($"Play: {playSongTime:0.000}s", px + 6, top - 26, 18, new Color(120, 220, 120, 255));
        }

        // Audio start/end markers
        float totalDuration = GetSafeMusicLength();
        float lastNote = chart.Notes.Count > 0 ? chart.Notes[^1].Time : 0f;
        if (totalDuration <= 0f) totalDuration = MathF.Max(lastNote + 2f, 10f);
        float audioStart = MathF.Max(0f, chart.Offset_sec);
        float audioEnd = totalDuration;

        float xs = TimeToX(audioStart);
        float xe = TimeToX(audioEnd);
        if (xs > -1000 && xs < w + 1000)
        {
            DrawLine((int)xs, top, (int)xs, bottom, new Color(160, 220, 160, 200));
            DrawText("Audio Start", (int)xs + 6, top + 6, 16, new Color(160, 220, 160, 220));
        }
        if (xe > -1000 && xe < w + 1000)
        {
            DrawLine((int)xe, top, (int)xe, bottom, new Color(220, 120, 120, 200));
            DrawText("Audio End", (int)xe - 80, top + 6, 16, new Color(220, 120, 120, 220));
        }

        // Marquee
        if (multiSelecting)
        {
            Rectangle sel = MakeRect(selStart, selEnd);
            DrawRectangleRec(sel, new Color(100, 140, 200, 60));
            DrawRectangleLines((int)sel.X, (int)sel.Y, (int)sel.Width, (int)sel.Height, new Color(160, 200, 255, 200));
        }

        // --- Scrollbar (between timeline and bottom) ---
        Rectangle scrollRect = new Rectangle(20, bottom + 12, w - 40, 12);
        float visibleSec = w * secPerPixel;
        float maxViewStart = MathF.Max(0f, totalDuration - visibleSec);

        float thumbW = MathF.Max(6f, (visibleSec / MathF.Max(visibleSec, totalDuration)) * scrollRect.Width);
        float thumbX = scrollRect.X + (viewStart / MathF.Max(0.0001f, totalDuration)) * scrollRect.Width;
        thumbX = MathF.Min(scrollRect.X + scrollRect.Width - thumbW, MathF.Max(scrollRect.X, thumbX));
        Rectangle thumbRect = new Rectangle(thumbX, scrollRect.Y, thumbW, scrollRect.Height);

        DrawRectangleRec(scrollRect, new Color(28, 32, 40, 255));
        DrawRectangleLines((int)scrollRect.X, (int)scrollRect.Y, (int)scrollRect.Width, (int)scrollRect.Height, new Color(80, 90, 100, 255));
        DrawRectangleRec(thumbRect, new Color(120, 160, 220, 220));
        DrawRectangleLines((int)thumbRect.X, (int)thumbRect.Y, (int)thumbRect.Width, (int)thumbRect.Height, new Color(200, 210, 230, 200));

        // Save toast
        if (saveToastTimer > 0f)
        {
            string msg = "Saved!";
            int tw = MeasureText(msg, 24);
            DrawRectangle(20, h - 54, tw + 24, 36, new Color(30, 40, 60, 220));
            DrawText(msg, 32, h - 50, 24, Color.White);
        }

        // Clicks after draw
        if (renameClicked)
        {
            renameOpen = true;
            renameText = string.IsNullOrWhiteSpace(chart.Title)
                ? Path.GetFileNameWithoutExtension(song.ChartPath)
                : chart.Title;
        }
        if (changeClicked)
        {
            changeAudioOpen = true;
            // prefill with current audio field (relative or absolute)
            changeAudioText = chart.Audio ?? "";
        }
        if (settingsClicked) { showSettings = !showSettings; showHelp = false; }
        if (helpClicked) { showHelp = !showHelp; showSettings = false; }

        // Overlays
        if (showSettings) DrawSettingsOverlay();
        if (showHelp) DrawHelpOverlay();
        if (renameOpen) DrawRenameOverlay();
        if (changeAudioOpen) DrawChangeAudioOverlay();

        // Note context menu (drawn last to overlay)
        if (noteMenuOpen && noteMenuForIndex >= 0 && noteMenuForIndex < chart.Notes.Count)
        {
            DrawRectangleRec(noteMenuRect, new Color(30, 36, 48, 240));
            DrawRectangleLines((int)noteMenuRect.X, (int)noteMenuRect.Y, (int)noteMenuRect.Width, (int)noteMenuRect.Height, new Color(200, 210, 230, 200));

            int itemH = 28;
            Rectangle m1 = new Rectangle(noteMenuRect.X + 6, noteMenuRect.Y + 6, noteMenuRect.Width - 12, itemH);
            Rectangle m2 = new Rectangle(noteMenuRect.X + 6, noteMenuRect.Y + 6 + itemH + 4, noteMenuRect.Width - 12, itemH);
            Rectangle m3 = new Rectangle(noteMenuRect.X + 6, noteMenuRect.Y + 6 + (itemH + 4) * 2, noteMenuRect.Width - 12, itemH);

            DrawText("Resnap Selection to Grid", (int)m1.X + 6, (int)m1.Y + 4, 18, Color.White);
            DrawText($"Auto-Resnap on BPM Change: {(autoResnapOnBpmChange ? "On" : "Off")}", (int)m2.X + 6, (int)m2.Y + 4, 18, Color.White);
            DrawText("Delete", (int)m3.X + 6, (int)m3.Y + 4, 18, new Color(255, 180, 180, 255));

            Vector2 mp = GetMousePosition();
            if (IsMouseButtonPressed(MouseButton.Left))
            {
                if (CheckCollisionPointRec(mp, m1))
                {
                    ResnapNotesToGrid(selectedNotes.Count > 0 ? selectedNotes : null);
                    noteMenuOpen = false;
                }
                else if (CheckCollisionPointRec(mp, m2))
                {
                    autoResnapOnBpmChange = !autoResnapOnBpmChange;
                    noteMenuOpen = false;
                }
                else if (CheckCollisionPointRec(mp, m3))
                {
                    if (selectedNotes.Count > 0)
                    {
                        chart.Notes.RemoveAll(n => selectedNotes.Contains(n));
                        selectedNotes.Clear();
                        selectedIndex = -1;
                    }
                    else if (noteMenuForIndex >= 0 && noteMenuForIndex < chart.Notes.Count)
                    {
                        chart.Notes.RemoveAt(noteMenuForIndex);
                        selectedIndex = -1;
                    }
                    noteMenuOpen = false;
                }
            }
        }
    }

    private void DrawRenameOverlay()
    {
        int w = GetScreenWidth();
        int h = GetScreenHeight();

        // type chars
        for (int ch = GetCharPressed(); ch > 0; ch = GetCharPressed())
        {
            if (ch >= 32 && ch < 127 && renameText.Length < 60)
                renameText += (char)ch;
        }
        if (IsKeyPressed(KeyboardKey.Backspace) && renameText.Length > 0)
            renameText = renameText.Substring(0, renameText.Length - 1);

        bool ok = IsKeyPressed(KeyboardKey.Enter);
        bool cancel = IsKeyPressed(KeyboardKey.Escape);

        int boxW = 600, boxH = 180;
        int bx = (w - boxW) / 2;
        int by = (h - boxH) / 2;

        DrawRectangle(0, 0, w, h, new Color(0, 0, 0, 160));
        DrawRectangle(bx, by, boxW, boxH, new Color(24, 28, 38, 255));
        DrawRectangleLines(bx, by, boxW, boxH, new Color(200, 210, 230, 255));
        DrawText("Rename Chart", bx + 16, by + 12, 26, Color.White);

        DrawRectangle(bx + 20, by + 60, boxW - 40, 40, new Color(18, 20, 28, 255));
        DrawText(string.IsNullOrEmpty(renameText) ? "" : renameText, bx + 28, by + 68, 24, Color.White);

        Rectangle btnSave = new Rectangle(bx + boxW - 200, by + boxH - 52, 80, 32);
        Rectangle btnCancel = new Rectangle(bx + boxW - 110, by + boxH - 52, 80, 32);
        if (DrawButton(btnSave, "Save")) ok = true;
        if (DrawButton(btnCancel, "Cancel")) cancel = true;

        if (ok && !string.IsNullOrWhiteSpace(renameText))
        {
            chart.Title = renameText.Trim();
            Save(); // persist title to file
            renameOpen = false;
        }
        if (cancel) renameOpen = false;
    }

    private void DrawChangeAudioOverlay()
    {
        int screenWidth = GetScreenWidth();
        int screenHeight = GetScreenHeight();

        // Semi-transparent fullscreen overlay
        DrawRectangle(0, 0, screenWidth, screenHeight, new Color(0, 0, 0, 160));

        // Centered panel
        int panelWidth = Math.Min(800, screenWidth - 80);
        int panelHeight = Math.Min(600, screenHeight - 80);
        int panelX = (screenWidth - panelWidth) / 2;
        int panelY = (screenHeight - panelHeight) / 2;

        DrawRectangle(panelX, panelY, panelWidth, panelHeight, new Color(24, 28, 38, 255));
        DrawRectangleLines(panelX, panelY, panelWidth, panelHeight, new Color(200, 210, 230, 255));
        DrawText("Change Audio", panelX + 16, panelY + 12, 26, Color.White);

        // Content area
        int contentX = panelX + 20;
        int contentY = panelY + 60;
        int contentWidth = panelWidth - 40;
        int contentHeight = panelHeight - 120;

        // Scrollable area
        BeginScissorMode(contentX, contentY, contentWidth, contentHeight);

        int yOffset = contentY - (int)changeAudioScroll;
        foreach (var entry in audioEntries)
        {
            // Draw each audio entry
            Rectangle entryRect = new Rectangle(contentX, yOffset, contentWidth - 20, 40);
            Vector2 mousePos = GetMousePosition();
            bool hover = CheckCollisionPointRec(mousePos, entryRect);

            Color bgColor = hover ? new Color(70, 90, 130, 220) : new Color(50, 64, 92, 200);
            DrawRectangleRec(entryRect, bgColor);
            DrawRectangleLines((int)entryRect.X, (int)entryRect.Y, (int)entryRect.Width, (int)entryRect.Height, new Color(180, 200, 240, 180));

            // Display file name and duration
            DrawText(entry.Name, (int)entryRect.X + 10, (int)entryRect.Y + 10, 18, Color.White);
            DrawText(FormatMmSs(entry.DurationSec), (int)entryRect.X + contentWidth - 100, (int)entryRect.Y + 10, 18, Color.Gray);

            // Handle click
            if (hover && IsMouseButtonPressed(MouseButton.Left))
            {
                SelectAudioFromFullPath(entry.FullPath);
                changeAudioOpen = false;
            }

            yOffset += 50;
        }

        EndScissorMode();

        // Scrollbar
        int scrollbarX = contentX + contentWidth - 10;
        int scrollbarHeight = contentHeight;
        int contentTotalHeight = audioEntries.Count * 50;
        DrawScrollbar(scrollbarX, contentY, 8, scrollbarHeight, ref changeAudioScroll, contentTotalHeight, contentHeight);

        // Add Song button
        Rectangle addSongBtn = new Rectangle(panelX + 20, panelY + panelHeight - 52, 170, 34);
        if (DrawButton(addSongBtn, "Add Song (.mp3 file)"))
        {
            string? selectedFile = OpenFilePicker();
            if (!string.IsNullOrEmpty(selectedFile))
            {
                string assetsDir = GetAssetsDir();
                Directory.CreateDirectory(assetsDir);
                string destPath = GetUniqueFilePath(Path.Combine(assetsDir, Path.GetFileName(selectedFile)));
                File.Copy(selectedFile, destPath, overwrite: true);
                SelectAudioFromFullPath(destPath);
                RefreshAudioList(); // Refresh the list to show the newly added file
                changeAudioOpen = false;
            }
        }

        // Open Folder button (next to Add Song button)
        Rectangle openFolderBtn = new Rectangle(panelX + 20 + 170 + 10, panelY + panelHeight - 52, 115, 34);
        if (DrawButton(openFolderBtn, "Open Folder"))
        {
            try
            {
                string assetsDir = GetAssetsDir();
                Directory.CreateDirectory(assetsDir); // Ensure the directory exists
                Process.Start("explorer", assetsDir);
            }
            catch
            {
                // Silently ignore errors (e.g., if explorer can't open the folder)
            }
        }

        // Close button
        Rectangle closeBtn = new Rectangle(panelX + panelWidth - 120, panelY + panelHeight - 52, 100, 32);
        if (DrawButton(closeBtn, "Close")) changeAudioOpen = false;
    }

    // Refresh the list of audio files in the assets directory
    private void RefreshAudioList()
    {
        audioEntries.Clear();
        string assetsDir = GetAssetsDir();
        if (!Directory.Exists(assetsDir)) return;

        foreach (string file in Directory.EnumerateFiles(assetsDir, "*.*", SearchOption.TopDirectoryOnly))
        {
            if (file.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".ogg", StringComparison.OrdinalIgnoreCase))
            {
                float duration = GetAudioDurationSecSafe(file);
                if (duration > 0)
                {
                    audioEntries.Add(new AudioEntry
                    {
                        Name = Path.GetFileName(file),
                        FullPath = file,
                        DurationSec = duration
                    });
                }
            }
        }
    }

    // Get the assets directory path
    private string GetAssetsDir() => Path.Combine(AppContext.BaseDirectory, "charts", "assets");

    // Format seconds as mm:ss
    private string FormatMmSs(float seconds)
    {
        TimeSpan time = TimeSpan.FromSeconds(seconds);
        return $"{time.Minutes:D2}:{time.Seconds:D2}";
    }

    // Safely get the duration of an audio file
    private float GetAudioDurationSecSafe(string path)
    {
        try
        {
            Music music = LoadMusicStream(path);
            float duration = GetMusicTimeLength(music);
            UnloadMusicStream(music);
            return duration;
        }
        catch
        {
            return 0f;
        }
    }

    // Get a unique file path to avoid overwriting
    private string GetUniqueFilePath(string path)
    {
        string directory = Path.GetDirectoryName(path)!;
        string fileName = Path.GetFileNameWithoutExtension(path);
        string extension = Path.GetExtension(path);
        int counter = 1;

        while (File.Exists(path))
        {
            path = Path.Combine(directory, $"{fileName} ({counter++}){extension}");
        }

        return path;
    }

    // Select an audio file and update the chart
    private void SelectAudioFromFullPath(string fullPath)
    {
        string chartDir = Path.GetDirectoryName(song.ChartPath) ?? ".";
        string relativePath = Path.GetRelativePath(chartDir, fullPath).Replace('\\', '/');
        chart.Audio = relativePath;

        // Immediately load and play the new audio
        if (TryUpdateAudio(relativePath, out string error))
        {
            // Successfully loaded new audio - start playback at current cursor position
            try
            {
                StopMusicStream(Music);
                SeekMusicStream(Music, cursor);
                SetMusicVolume(Music, Program.Settings.MasterVolume * Program.Settings.MusicVolume);
                PlayMusicStream(Music);
            }
            catch
            {
                // If seeking fails, just play from the beginning
                try
                {
                    StopMusicStream(Music);
                    SeekMusicStream(Music, 0f);
                    PlayMusicStream(Music);
                }
                catch { /* ignore playback errors */ }
            }
        }

        Save();
    }

    // Draw a vertical scrollbar
    private void DrawScrollbar(int x, int y, int w, int h, ref float scroll, int contentHeight, int viewHeight)
    {
        DrawRectangle(x, y, w, h, new Color(30, 36, 52, 180));
        DrawRectangleLines(x, y, w, h, new Color(80, 100, 140, 200));

        if (contentHeight <= viewHeight) return;

        float scrollRatio = (float)viewHeight / contentHeight;
        int thumbHeight = Math.Max(24, (int)(h * scrollRatio));
        float maxScroll = contentHeight - viewHeight;
        float scrollPosition = (maxScroll <= 0f) ? 0f : scroll / maxScroll;
        int thumbY = y + (int)((h - thumbHeight) * scrollPosition);

        DrawRectangle(x + 1, thumbY + 1, w - 2, thumbHeight - 2, new Color(120, 160, 220, 230));

        // Handle mouse wheel scrolling
        Vector2 mousePos = GetMousePosition();
        if (mousePos.X >= x && mousePos.X <= x + w && mousePos.Y >= y && mousePos.Y <= y + h)
        {
            float wheel = GetMouseWheelMove();
            if (Math.Abs(wheel) > 0f) scroll -= wheel * 32f;
        }

        // Clamp scroll
        scroll = Math.Clamp(scroll, 0, maxScroll);
    }

    // Open a file picker using NativeFileDialog.Net
    private string? OpenFilePicker()
    {
        string? selectedFile = null;
        using (var dialog = new NativeFileDialog())
        {
            var result = dialog.AddFilter("Audio Files", "mp3")
                           .AddFilter("All Files", "*.*")
                           .Open(out selectedFile);
            if (result != DialogResult.Okay)
                selectedFile = null;
        }
        return selectedFile;
    }

    private void DrawSettingsOverlay()
    {
        int w = GetScreenWidth();
        int h = GetScreenHeight();

        DrawRectangle(0, 0, w, h, new Color(0, 0, 0, 160));
        int panelW = Math.Min(920, w - 60);
        int panelH = Math.Min(600, h - 60);
        int px = w / 2 - panelW / 2;
        int py = h / 2 - panelH / 2;

        DrawRectangle(px, py, panelW, panelH, new Color(24, 28, 38, 255));
        DrawRectangleLines(px, py, panelW, panelH, new Color(200, 210, 230, 255));
        DrawText("Editor Settings — Keybinds", px + 16, py + 12, 26, Color.White);

        // Content metrics
        int labelX = px + 20;
        int rowH = 44;
        int col1X = px + 320;
        int col2X = px + panelW - 220 - 20; // right column 220px wide, margin 20
        int contentTop = py + 56;
        int contentHeight;

        // Build rows to measure height (13 rows: 10 existing + 3 editor keybinds)
        int rows = 13;
        contentHeight = rows * rowH + 90; // + footer

        // Viewport area (scrollable)
        int vX = px + 10;
        int vY = py + 48;
        int vW = panelW - 20;
        int vH = panelH - 100; // leave room for footer+Close

        // Handle wheel scrolling when hovered
        Vector2 mp = GetMousePosition();
        bool overViewport = mp.X >= vX && mp.X <= vX + vW && mp.Y >= vY && mp.Y <= vY + vH;
        if (overViewport)
        {
            float wheel = GetMouseWheelMove();
            if (Math.Abs(wheel) > 0f) settingsScroll -= wheel * 32f; // scroll step
        }

        // clamp scroll
        float maxScroll = Math.Max(0, contentHeight - vH);
        if (settingsScroll < 0) settingsScroll = 0;
        if (settingsScroll > maxScroll) settingsScroll = maxScroll;

        // clip to viewport
        BeginScissorMode(vX, vY, vW, vH);

        // Draw rows with offset
        int yBase = contentTop - (int)settingsScroll;
        int row = 0;

        DrawRow("Play/Stop (toggle)", ref binds.PlayToggle, "ed_play", yBase + row * rowH); row++;
        DrawRow("Toggle Snap", ref binds.ToggleSnap, "ed_snap", yBase + row * rowH); row++;
        DrawRow("Snap Div -", ref binds.SnapDivDown, "ed_snapdec", yBase + row * rowH); row++;
        DrawRow("Snap Div +", ref binds.SnapDivUp, "ed_snapinc", yBase + row * rowH); row++;
        DrawRow("Zoom In", ref binds.ZoomIn, "ed_zoom_in", yBase + row * rowH); row++;
        DrawRow("Zoom Out", ref binds.ZoomOut, "ed_zoom_out", yBase + row * rowH); row++;
        DrawRow("Cursor Left", ref binds.CursorLeft, "ed_cur_left", yBase + row * rowH); row++;
        DrawRow("Cursor Right", ref binds.CursorRight, "ed_cur_right", yBase + row * rowH); row++;
        DrawRow("Delete Selected", ref binds.DeleteSelected, "ed_del", yBase + row * rowH); row++;
        DrawRow("Show Help", ref binds.ShowHelp, "ed_help", yBase + row * rowH); row++;

        // Section separator
        if (yBase + row * rowH > vY - rowH && yBase + row * rowH < vY + vH)
        {
            DrawText("--- Editor Note Keys ---", labelX, yBase + row * rowH + 8, 20, new Color(255, 200, 120, 255));
        }
        row++;

        // Editor keybind rows
        DrawEditorKeyRow("Place Note 1", "ed_key_1", yBase + row * rowH); row++;
        DrawEditorKeyRow("Place Note 0", "ed_key_0", yBase + row * rowH); row++;
        DrawEditorKeyRow("Place Special Note", "ed_key_big", yBase + row * rowH); row++;

        EndScissorMode();

        // Scrollbar
        DrawSettingsScrollbar(vX + vW - 10, vY, 8, vH, settingsScroll, contentHeight, vH);

        // Footer
        string note = "Notes: Save = Ctrl+S. BPM/grid: Ctrl+Uparrow/Downarrow, Ctrl+F/G, Ctrl+/[ and Ctrl+].";
        DrawText(note, px + 16, py + panelH - 60, 18, Color.Gray);
        string note2 = "Editor note keys are separate from gameplay keys and only work in the editor.";
        DrawText(note2, px + 16, py + panelH - 40, 18, Color.Gray);

        Rectangle closeBtn = new Rectangle(px + panelW - 120, py + panelH - 46, 100, 32);
        if (DrawButton(closeBtn, "Close")) showSettings = false;

        // local helper row
        void DrawRow(string title, ref KeyBind bind, string id, int y)
        {
            // skip if row is far outside viewport
            if (y > vY + vH || y + rowH < vY - rowH) return;

            DrawText(title, labelX, y + 8, 20, Color.White);

            Rectangle r1 = new Rectangle(col1X, y, 220, 32);
            if (DrawKeyButton(r1, $"{id}_p", bind.Primary, false))
            {
                capturing = true; capturingId = $"{id}_p"; capturingAlt = false;
            }

            Rectangle r2 = new Rectangle(col2X, y, 220, 32);
            if (DrawKeyButton(r2, $"{id}_a", bind.Alt, true))
            {
                capturing = true; capturingId = $"{id}_a"; capturingAlt = true;
            }

            // refresh bind after capture
            bind = GetBindById(id);
        }

        // helper for editor keybind rows
        void DrawEditorKeyRow(string title, string id, int y)
        {
            // skip if row is far outside viewport
            if (y > vY + vH || y + rowH < vY - rowH) return;

            DrawText(title, labelX, y + 8, 20, Color.White);

            var editorBind = GetEditorBindById(id);
            Rectangle r1 = new Rectangle(col1X, y, 220, 32);
            if (DrawKeyButton(r1, $"{id}_p", editorBind.Primary, false))
            {
                capturing = true; capturingId = $"{id}_p"; capturingAlt = false;
            }

            Rectangle r2 = new Rectangle(col2X, y, 220, 32);
            if (DrawKeyButton(r2, $"{id}_a", editorBind.Alt, true))
            {
                capturing = true; capturingId = $"{id}_a"; capturingAlt = true;
            }
        }
    }

    private void DrawSettingsScrollbar(int x, int y, int w, int h, float scroll, int contentH, int viewH)
    {
        // track
        DrawRectangle(x, y, w, h, new Color(30, 36, 52, 180));
        DrawRectangleLines(x, y, w, h, new Color(80, 100, 140, 200));

        if (contentH <= viewH) return;

        float frac = (float)viewH / contentH;
        int thumbH = Math.Max(24, (int)(h * frac));
        float maxScroll = contentH - viewH;
        float startFrac = (maxScroll <= 0f) ? 0f : scroll / maxScroll;
        int thumbY = y + (int)((h - thumbH) * startFrac);

        DrawRectangle(x + 1, thumbY + 1, w - 2, thumbH - 2, new Color(120, 160, 220, 230));
    }

    private void DrawHelpOverlay()
    {
        int w = GetScreenWidth();
        int h = GetScreenHeight();

        DrawRectangle(0, 0, w, h, new Color(0, 0, 0, 160));
        int panelW = Math.Min(920, w - 60);
        int panelH = Math.Min(560, h - 60);
        int px = w / 2 - panelW / 2;
        int py = h / 2 - panelH / 2;

        DrawRectangle(px, py, panelW, panelH, new Color(24, 28, 38, 255));
        DrawRectangleLines(px, py, panelW, panelH, new Color(200, 210, 230, 255));
        DrawText("Editor Help", px + 16, py + 12, 26, Color.White);

        int tx = px + 16;
        int ty = py + 56;
        int lh = 22;

        string[] lines =
        {
            "Basics",
            $"• Add notes: 1({Program.Settings.EditorKeys.EditorKeyLeft.Primary})  0({Program.Settings.EditorKeys.EditorKeyRight.Primary})  Big({Program.Settings.EditorKeys.EditorKeyBig.Primary})",
            $"• Play/Stop from cursor: {Format(binds.PlayToggle)}",
            $"• Move cursor: {Format(binds.CursorLeft)} (Left) / {Format(binds.CursorRight)} (Right)",
            $"• Toggle snapping: {Format(binds.ToggleSnap)}  |  Snap div: - {Format(binds.SnapDivDown)}  + {Format(binds.SnapDivUp)}",
            $"• Zoom: In {Format(binds.ZoomIn)}  Out {Format(binds.ZoomOut)}  |  RMB to pan. Mouse wheel zooms at pointer.",
            $"• Delete selected: {Format(binds.DeleteSelected)}",
            "",
            "Selection & Editing",
            "• Click a note to select. Shift-click to multi-select.",
            "• Drag to move all selected notes together.",
            "• Ctrl+A select all, Ctrl+C copy, Ctrl+V paste at cursor, Ctrl+D duplicate +1 beat.",
            "• Right-click a note: resnap / auto-resnap toggle / delete.",
            "",
            "Saving & Timing",
            "• Save: Ctrl+S",
            "• BPM: Ctrl+B (up), Ctrl+N (down)",
            "• Time Signature (beats per bar): Ctrl+[ (down), Ctrl+] (up)",
            "• Grid Offset: Ctrl+F (−), Ctrl+G (+)"
        };

        foreach (var ln in lines)
        {
            bool header = ln is "Basics" or "Selection & Editing" or "Saving & Timing";
            DrawText(ln, tx, ty, header ? 22 : 20, header ? Color.White : Color.Gray);
            ty += header ? (lh + 8) : (lh + 2);
        }

        Rectangle closeBtn = new Rectangle(px + panelW - 120, py + panelH - 46, 100, 32);
        if (DrawButton(closeBtn, "Close")) showHelp = false;
    }

    private static string Format(KeyBind b) =>
        b.Alt != KeyboardKey.Null ? $"{b.Primary}/{b.Alt}" : b.Primary.ToString();

    // ===== Helpers =====
    private float GetSafeMusicLength()
    {
        float len = 0f;
        try { len = GetMusicTimeLength(Music); } catch { len = 0f; }
        if (float.IsNaN(len) || len < 0f) len = 0f;
        return len;
    }

    private void DrawBeatGrid(int top, int bottom, int w)
    {
        float spb = 60f / MathF.Max(1f, bpm);
        float left = viewStart;
        float right = viewStart + w * secPerPixel;

        int kStart = (int)MathF.Floor((left - gridOffsetSec) / spb) - 1;
        int kEnd = (int)MathF.Ceiling((right - gridOffsetSec) / spb) + 1;

        for (int k = kStart; k <= kEnd; k++)
        {
            float t = gridOffsetSec + k * spb;
            int x = (int)TimeToX(t);
            bool isBar = (beatsPerBar > 0) && (k % beatsPerBar == 0);
            Color col = isBar ? new Color(255, 255, 255, 80) : new Color(255, 255, 255, 30);
            DrawLine(x, top, x, bottom, col);

            if (snapDivisor > 1)
            {
                float subSpb = spb / (snapDivisor / 1f);
                for (int s = 1; s < snapDivisor; s++)
                {
                    float ts = t + s * subSpb;
                    int xs = (int)TimeToX(ts);
                    DrawLine(xs, top, xs, bottom, new Color(255, 255, 255, 20));
                }
            }
        }
    }

    private void ZoomAt(float centerTime, float factor)
    {
        float x = TimeToX(centerTime);
        float newSecPerPx = Clamp(secPerPixel * factor, MinSecPerPx, MaxSecPerPx);
        if (Math.Abs(newSecPerPx - secPerPixel) < 1e-9f) return;
        secPerPixel = newSecPerPx;
        viewStart = centerTime - x * secPerPixel;
        if (viewStart < 0f) viewStart = 0f;
    }

    private static float Clamp(float v, float lo, float hi) => MathF.Min(hi, MathF.Max(lo, v));

    private float GetPlaceTime()
    {
        float t = cursor;
        if (snapEnabled) t = SnapTime(t);
        return t;
    }

    private float SnapTime(float t)
    {
        float spb = 60f / MathF.Max(1f, bpm);
        float sub = spb / MathF.Max(1, snapDivisor);
        float k = MathF.Round((t - gridOffsetSec) / sub);
        return MathF.Max(0f, gridOffsetSec + k * sub);
    }

    private float PrevGrid(float t)
    {
        float spb = 60f / MathF.Max(1f, bpm);
        float sub = spb / MathF.Max(1, snapDivisor);
        float k = MathF.Floor((t - gridOffsetSec) / sub) - 1f;
        return MathF.Max(0f, gridOffsetSec + k * sub);
    }

    private float NextGrid(float t)
    {
        float spb = 60f / MathF.Max(1f, bpm);
        float sub = spb / MathF.Max(1, snapDivisor);
        float k = MathF.Ceiling((t - gridOffsetSec) / sub) + 1f;
        return MathF.Max(0f, gridOffsetSec + k * sub);
    }

    private void ResnapNotesToGrid(HashSet<Note>? only = null)
    {
        if (only != null && only.Count == 0) return;
        foreach (var n in chart.Notes)
        {
            if (only == null || only.Contains(n))
            {
                n.Time = SnapTime(n.Time);
            }
        }
        SortNotes();
    }

    private void PersistTiming()
    {
        chart.Bpm = bpm;
        chart.BeatsPerBar = beatsPerBar;
        chart.GridOffsetSec = gridOffsetSec;
        // do not auto-save file here to avoid spamming disk; Save() is user-triggered
    }

    private void AddNoteAt(float t, int type)
    {
        t = MathF.Max(0f, (float)Math.Round(t, 3)); // snap to 1ms
        
        // Check if there's already a note at this exact time position
        var existingNote = chart.Notes.FirstOrDefault(n => MathF.Abs(n.Time - t) < 0.001f);
        if (existingNote != null)
        {
            // Replace the existing note with the new type instead of stacking
            existingNote.Type = type;
            selectedIndex = chart.Notes.IndexOf(existingNote);
            selectedNotes.Clear();
            selectedNotes.Add(existingNote);
            return;
        }
        
        var newNote = new Note { Time = t, Type = type };
        chart.Notes.Add(newNote);
        SortNotes();
        selectedIndex = chart.Notes.FindIndex(n => ReferenceEquals(n, newNote));
        selectedNotes.Clear();
        selectedNotes.Add(newNote);
    }

    private void CheckForNotesToPlay(float startTime, float endTime)
    {
        // Play hit sounds for notes between startTime and endTime
        foreach (var note in chart.Notes)
        {
            if (note.Time > startTime && note.Time <= endTime)
            {
                // Play hit sound for this note
                PlaySound(hitSounds[hitSoundIndex]);
                hitSoundIndex = (hitSoundIndex + 1) % hitSounds.Length;
            }
        }
    }

    private void SortNotes() => chart.Notes.Sort((a, b) => a.Time.CompareTo(b.Time));

    private int FindNoteAt(Vector2 mouse, out float noteTime)
    {
        noteTime = XToTime(mouse.X);
        float y1 = laneMidY - laneSep;
        float y0 = laneMidY + laneSep;
        float y2 = laneMidY;
        for (int i = 0; i < chart.Notes.Count; i++)
        {
            var n = chart.Notes[i];
            float x = TimeToX(n.Time);
            float y = n.Type switch { 1 => y1, 0 => y0, 2 => y2, _ => y2 };
            if (MathF.Abs(mouse.X - x) <= 22 && MathF.Abs(mouse.Y - y) <= 24)
                return i;
        }
        return -1;
    }

    private float TimeToX(float t) => (t - viewStart) / secPerPixel;
    private float XToTime(float x) => viewStart + x * secPerPixel;

    private void Save()
    {
        try
        {
            // persist timing first
            PersistTiming();
            SortNotes();
            ChartLoader.Save(song.ChartPath, chart);
            saveToastTimer = 1.5f;
        }
        catch { saveToastTimer = 1.5f; }
    }

    private void StartPlaytestAt(float startTimeSec)
    {
        playtest = true;
        playSongTime = startTimeSec;
        playStartTime = startTimeSec;

        try
        {
            StopMusicStream(Music);
            SeekMusicStream(Music, startTimeSec);
            SetMusicVolume(Music, 1f);
            PlayMusicStream(Music);
            seekFailedMuteGate = false;
        }
        catch
        {
            StopMusicStream(Music);
            SeekMusicStream(Music, 0f);
            PlayMusicStream(Music);
            SetMusicVolume(Music, 0f);
            seekFailedMuteGate = true;
        }
    }

    private static Rectangle MakeRect(Vector2 a, Vector2 b)
    {
        float x = MathF.Min(a.X, b.X);
        float y = MathF.Min(a.Y, b.Y);
        float w = MathF.Abs(a.X - b.X);
        float h = MathF.Abs(a.Y - b.Y);
        return new Rectangle(x, y, w, h);
    }

    private void UpdateMarqueeSelection(Vector2 a, Vector2 b)
    {
        Rectangle sel = MakeRect(a, b);
        selectedNotes.Clear();

        float y1 = laneMidY - laneSep;
        float y0 = laneMidY + laneSep;
        float y2 = laneMidY;

        for (int i = 0; i < chart.Notes.Count; i++)
        {
            var n = chart.Notes[i];
            float x = TimeToX(n.Time);
            float y = n.Type switch { 1 => y1, 0 => y0, 2 => y2, _ => y2 };
            Rectangle rect = new Rectangle(x - 18, y - 18, 36, 36);
            if (CheckCollisionRecs(sel, rect))
                selectedNotes.Add(n);
        }
    }

    private bool DrawButton(Rectangle r, string label)
    {
        Vector2 mp = GetMousePosition();
        bool hover = CheckCollisionPointRec(mp, r);
        Color bg = hover ? new Color(70, 90, 130, 220) : new Color(50, 64, 92, 200);
        DrawRectangleRounded(r, 0.25f, 6, bg);
        DrawRectangleLines((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height, new Color(180, 200, 240, 180));
        int tw = MeasureText(label, 18);
        DrawText(label, (int)(r.X + r.Width / 2 - tw / 2), (int)(r.Y + r.Height / 2 - 10), 18, Color.White);
        return hover && IsMouseButtonReleased(MouseButton.Left);
    }

    private bool DrawKeyButton(Rectangle r, string id, KeyboardKey key, bool isAlt)
    {
        Vector2 mp = GetMousePosition();
        bool hover = CheckCollisionPointRec(mp, r);
        bool isCapturingThis = capturing && capturingId == id && capturingAlt == isAlt;

        Color bg = isCapturingThis ? new Color(150, 110, 80, 240)
                : hover ? new Color(70, 90, 130, 220)
                : new Color(50, 64, 92, 200);

        DrawRectangleRounded(r, 0.25f, 6, bg);
        DrawRectangleLines((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height, new Color(180, 200, 240, 180));

        string text = isCapturingThis ? "Press a key..." : key.ToString();
        int tw = MeasureText(text, 18);
        DrawText(text, (int)(r.X + r.Width / 2 - tw / 2), (int)(r.Y + r.Height / 2 - 10), 18, Color.White);

        return !isCapturingThis && hover && IsMouseButtonPressed(MouseButton.Left);
    }

    private KeyboardKey GetAnyPressedKey()
    {
        // scan common enum range (simple approach)
        for (int k = (int)KeyboardKey.Space; k <= (int)KeyboardKey.KpEqual; k++)
        {
            var kk = (KeyboardKey)k;
            if (IsKeyPressed(kk)) return kk;
        }
        for (int k = (int)KeyboardKey.A; k <= (int)KeyboardKey.Z; k++) if (IsKeyPressed((KeyboardKey)k)) return (KeyboardKey)k;
        for (int k = (int)KeyboardKey.Zero; k <= (int)KeyboardKey.Nine; k++) if (IsKeyPressed((KeyboardKey)k)) return (KeyboardKey)k;
        return KeyboardKey.Null;
    }

    private void ApplyCapturedKey(string id, KeyboardKey k, bool alt)
    {
        // Extract base ID (remove _p or _a suffix)
        string baseId = id;
        if (id.EndsWith("_p") || id.EndsWith("_a"))
        {
            baseId = id.Substring(0, id.Length - 2);
        }

        if (baseId.StartsWith("ed_key_"))
        {
            // Handle editor keybinds
            var editorBind = GetEditorBindById(baseId);
            if (alt) editorBind.Alt = k; else editorBind.Primary = k;
            SetEditorBindById(baseId, editorBind);
            Program.SaveUserSettings(); // Save to JSON immediately
        }
        else
        {
            // Handle regular editor binds
            var b = GetBindById(baseId);
            if (alt) b.Alt = k; else b.Primary = k;
            SetBindById(baseId, b);
        }
    }

    private KeyBind GetBindById(string id) =>
        id switch
        {
            "ed_play" => binds.PlayToggle,
            "ed_snap" => binds.ToggleSnap,
            "ed_snapdec" => binds.SnapDivDown,
            "ed_snapinc" => binds.SnapDivUp,
            "ed_zoom_in" => binds.ZoomIn,
            "ed_zoom_out" => binds.ZoomOut,
            "ed_cur_left" => binds.CursorLeft,
            "ed_cur_right" => binds.CursorRight,
            "ed_del" => binds.DeleteSelected,
            "ed_help" => binds.ShowHelp,
            _ => new KeyBind(KeyboardKey.Null, KeyboardKey.Null),
        };

    private void SetBindById(string id, KeyBind value)
    {
        switch (id)
        {
            case "ed_play": binds.PlayToggle = value; break;
            case "ed_snap": binds.ToggleSnap = value; break;
            case "ed_snapdec": binds.SnapDivDown = value; break;
            case "ed_snapinc": binds.SnapDivUp = value; break;
            case "ed_zoom_in": binds.ZoomIn = value; break;
            case "ed_zoom_out": binds.ZoomOut = value; break;
            case "ed_cur_left": binds.CursorLeft = value; break;
            case "ed_cur_right": binds.CursorRight = value; break;
            case "ed_del": binds.DeleteSelected = value; break;
            case "ed_help": binds.ShowHelp = value; break;
        }
    }

    private Binding GetEditorBindById(string id) =>
        id switch
        {
            "ed_key_1" => Program.Settings.EditorKeys.EditorKeyLeft,
            "ed_key_0" => Program.Settings.EditorKeys.EditorKeyRight,
            "ed_key_big" => Program.Settings.EditorKeys.EditorKeyBig,
            _ => new Binding(KeyboardKey.Null, KeyboardKey.Null),
        };

    private void SetEditorBindById(string id, Binding value)
    {
        switch (id)
        {
            case "ed_key_1": Program.Settings.EditorKeys.EditorKeyLeft = value; break;
            case "ed_key_0": Program.Settings.EditorKeys.EditorKeyRight = value; break;
            case "ed_key_big": Program.Settings.EditorKeys.EditorKeyBig = value; break;
        }
    }

    private bool TryLoadAudio(string userPath, out string error)
    {
        try
        {
            string chartDir = Path.GetDirectoryName(song.ChartPath) ?? ".";
            string fullPath = Path.IsPathRooted(userPath) ? userPath : Path.Combine(chartDir, userPath);
            if (!File.Exists(fullPath))
            {
                error = "File not found.";
                return false;
            }

            // unload previous
            try
            {
                try { if (IsMusicStreamPlaying(Music)) StopMusicStream(Music); } catch { /* ignore */ }
                UnloadMusicStream(Music);
            }
            catch { /* ignore */ }

            // load new
            try
            {
                Music = LoadMusicStream(fullPath);
            }
            catch
            {
                error = "Failed to load audio (format not supported).";
                return false;
            }
            SetMusicVolume(Music, Program.Settings.MasterVolume * Program.Settings.MusicVolume);

            // reset play state and view
            playtest = false;
            playSongTime = 0f;
            cursor = 0f;

            error = "";
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to load: {ex.Message}";
            return false;
        }
    }

    // Load new audio without resetting cursor position or playtest state
    private bool TryUpdateAudio(string userPath, out string error)
    {
        try
        {
            string chartDir = Path.GetDirectoryName(song.ChartPath) ?? ".";
            string fullPath = Path.IsPathRooted(userPath) ? userPath : Path.Combine(chartDir, userPath);
            if (!File.Exists(fullPath))
            {
                error = "File not found.";
                return false;
            }

            // Store current playback state
            bool wasPlaying = false;
            try
            {
                wasPlaying = IsMusicStreamPlaying(Music);
            }
            catch { /* ignore */ }

            // unload previous
            try
            {
                try { if (IsMusicStreamPlaying(Music)) StopMusicStream(Music); } catch { /* ignore */ }
                UnloadMusicStream(Music);
            }
            catch { /* ignore */ }

            // load new
            try
            {
                Music = LoadMusicStream(fullPath);
            }
            catch
            {
                error = "Failed to load audio (format not supported).";
                return false;
            }
            SetMusicVolume(Music, Program.Settings.MasterVolume * Program.Settings.MusicVolume);

            // Don't reset cursor or playtest state when just updating audio

            error = "";
            return true;
        }
        catch (Exception ex)
        {
            error = $"Failed to load: {ex.Message}";
            return false;
        }
    }

    public void UpdateSoundVolumes(float sfxVolume, float musicVolume)
    {
        // Update SFX volumes
        for (int i = 0; i < hitSounds.Length; i++)
        {
            SetSoundVolume(hitSounds[i], sfxVolume);
        }
        
        // Update music volume
        SetMusicVolume(Music, musicVolume);
    }

    public void Dispose()
    {
        try { if (IsMusicStreamPlaying(Music)) StopMusicStream(Music); } catch { /* ignore */ }
        try { UnloadMusicStream(Music); } catch { /* ignore */ }
        
        // Unload hit sounds
        for (int i = 0; i < hitSounds.Length; i++)
        {
            try { UnloadSound(hitSounds[i]); } catch { /* ignore */ }
        }
    }
}

// ====== Editor keybinds ======
internal struct EditorBindings
{
    public KeyBind PlayToggle;
    public KeyBind ToggleSnap;
    public KeyBind SnapDivDown;
    public KeyBind SnapDivUp;
    public KeyBind ZoomIn;
    public KeyBind ZoomOut;
    public KeyBind CursorLeft;
    public KeyBind CursorRight;
    public KeyBind DeleteSelected;
    public KeyBind ShowHelp;

    public static EditorBindings Default() => new EditorBindings
    {
        PlayToggle = new KeyBind(KeyboardKey.P, KeyboardKey.Null),
        ToggleSnap = new KeyBind(KeyboardKey.Tab, KeyboardKey.Null),
        SnapDivDown = new KeyBind(KeyboardKey.Q, KeyboardKey.Null),
        SnapDivUp = new KeyBind(KeyboardKey.W, KeyboardKey.Null),
        ZoomIn = new KeyBind(KeyboardKey.Equal, KeyboardKey.KpAdd),
        ZoomOut = new KeyBind(KeyboardKey.Minus, KeyboardKey.KpSubtract),
        CursorLeft = new KeyBind(KeyboardKey.Left, KeyboardKey.Null),
        CursorRight = new KeyBind(KeyboardKey.Right, KeyboardKey.Null),
        DeleteSelected = new KeyBind(KeyboardKey.Delete, KeyboardKey.Null),
        ShowHelp = new KeyBind(KeyboardKey.H, KeyboardKey.Null),
    };
}

internal struct KeyBind
{
    public KeyboardKey Primary;
    public KeyboardKey Alt;

    public KeyBind(KeyboardKey primary, KeyboardKey alt) { Primary = primary; Alt = alt; }

    public bool IsDown()
    {
        return (Primary != KeyboardKey.Null && IsKeyDown(Primary)) ||
            (Alt != KeyboardKey.Null && IsKeyDown(Alt));
    }

    public bool IsPressed()
    {
        return (Primary != KeyboardKey.Null && IsKeyPressed(Primary)) ||
            (Alt != KeyboardKey.Null && IsKeyPressed(Alt));
    }
}
