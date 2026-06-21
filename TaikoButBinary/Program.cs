using System;
using System.IO;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace TaikoButBinary;

// ======================= PROGRAM / APP SHELL =======================
internal class Program
{
    public const bool ENABLE_WAVEFORM = false;

    static AppMode mode = AppMode.MainMenu;

    // Speed mod submenu state
    static bool showSpeedSubmenu = false;
    static Rectangle speedSubmenuRect = new Rectangle();
    static bool submenuJustOpened = false;

    // Global settings (persist in-memory) - will be loaded from file on startup
    public static readonly Settings Settings = new Settings();

    // User settings class for JSON serialization
    public class UserSettings
    {
        public float ScrollSpeed { get; set; } = 500f;
        public float MasterVolume { get; set; } = 0.8f;
        public float MusicVolume { get; set; } = 0.7f;
        public float SfxVolume { get; set; } = 0.8f;
        public int JudgementOffsetMs { get; set; } = 0;

        // Display Settings
        public int ResolutionWidth { get; set; } = 1280;
        public int ResolutionHeight { get; set; } = 720;
        public int TargetFPS { get; set; } = 120;
        public bool VSync { get; set; } = false;
        public bool Fullscreen { get; set; } = false;

        // Gameplay Keybinds - using strings for JSON serialization
        public string HitOnePrimary { get; set; } = KeyboardKey.One.ToString();
        public string HitOneAlt { get; set; } = KeyboardKey.Kp1.ToString();
        public string HitZeroPrimary { get; set; } = KeyboardKey.Zero.ToString();
        public string HitZeroAlt { get; set; } = KeyboardKey.Kp0.ToString();
        public string HitSpacePrimary { get; set; } = KeyboardKey.Space.ToString();
        public string HitSpaceAlt { get; set; } = KeyboardKey.Space.ToString();
        public string RestartHoldPrimary { get; set; } = KeyboardKey.R.ToString();
        public string RestartHoldAlt { get; set; } = KeyboardKey.Null.ToString();
        public string PauseTogglePrimary { get; set; } = KeyboardKey.Escape.ToString();
        public string PauseToggleAlt { get; set; } = KeyboardKey.Null.ToString();

        // Editor Keybinds - using strings for JSON serialization
        public string EditorKeyLeftPrimary { get; set; } = KeyboardKey.One.ToString();
        public string EditorKeyLeftAlt { get; set; } = KeyboardKey.Null.ToString();
        public string EditorKeyRightPrimary { get; set; } = KeyboardKey.Zero.ToString();
        public string EditorKeyRightAlt { get; set; } = KeyboardKey.Null.ToString();
        public string EditorKeyBigPrimary { get; set; } = KeyboardKey.Space.ToString();
        public string EditorKeyBigAlt { get; set; } = KeyboardKey.Null.ToString();

        public float RestartHoldDelaySec { get; set; } = 0.75f;
        public float UnpauseDelaySec { get; set; } = 0.5f;
    }

    // ---- Song Select persistent UI state ----
    static bool renamePopupOpen = false;
    static string renameText = "";
    static int renameIndex = -1;
    static int contextOpenIndex = -1;
    static bool contextMenuOpen = false;
    static Rectangle openMenuRect = new Rectangle(0, 0, 0, 0);

    // --- Song list persistent scroll state (must persist across frames) ---
    static float songListScrollY = 0f;
    static bool songDraggingScrollbar = false;
    static float songScrollbarDragOffset = 0f;
    static int songRowHeight = 40;

    // --- Settings menu scroll state ---
    static float settingsScrollY = 0f;
    static bool settingsDraggingScrollbar = false;
    static float settingsScrollbarDragOffset = 0f;

    // --- Settings tab state ---
    static int currentSettingsTab = 0; // 0=Audio, 1=Display, 2=Controls, 3=Visuals

    // --- Display settings state ---
    static string[] resolutionOptions = { "1280x720", "1366x768", "1920x1080", "2560x1440", "3840x2160" };
    static int[] resolutionWidths = { 1280, 1366, 1920, 2560, 3840 };
    static int[] resolutionHeights = { 720, 768, 1080, 1440, 2160 };
    static bool displaySettingsChanged = false;

    // Helper methods for persistent user settings
    private static string GetUserSettingsPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "resources", "userSettings.json");
    }

    private static void LoadUserSettings()
    {
        try
        {
            string path = GetUserSettingsPath();
            if (!File.Exists(path))
            {
                // Use default values and create the file
                ApplyDefaultSettings();
                SaveUserSettings();
                return;
            }

            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                ApplyDefaultSettings();
                return;
            }

            var userSettings = JsonSerializer.Deserialize<UserSettings>(json);
            if (userSettings != null)
            {
                ApplyUserSettings(userSettings);
            }
            else
            {
                ApplyDefaultSettings();
            }
        }
        catch
        {
            // If anything goes wrong, use defaults
            ApplyDefaultSettings();
        }
    }

    public static void SaveUserSettings()
    {
        try
        {
            string path = GetUserSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var userSettings = new UserSettings
            {
                ScrollSpeed = Settings.ScrollSpeed,
                MasterVolume = Settings.MasterVolume,
                MusicVolume = Settings.MusicVolume,
                SfxVolume = Settings.SfxVolume,
                JudgementOffsetMs = Settings.JudgementOffsetMs,
                ResolutionWidth = Settings.ResolutionWidth,
                ResolutionHeight = Settings.ResolutionHeight,
                TargetFPS = Settings.TargetFPS,
                VSync = Settings.VSync,
                Fullscreen = Settings.Fullscreen,
                HitOnePrimary = Settings.HitOne.Primary.ToString(),
                HitOneAlt = Settings.HitOne.Alt.ToString(),
                HitZeroPrimary = Settings.HitZero.Primary.ToString(),
                HitZeroAlt = Settings.HitZero.Alt.ToString(),
                HitSpacePrimary = Settings.HitSecial.Primary.ToString(),
                HitSpaceAlt = Settings.HitSecial.Alt.ToString(),
                RestartHoldPrimary = Settings.RestartHold.Primary.ToString(),
                RestartHoldAlt = Settings.RestartHold.Alt.ToString(),
                PauseTogglePrimary = Settings.PauseToggle.Primary.ToString(),
                PauseToggleAlt = Settings.PauseToggle.Alt.ToString(),
                EditorKeyLeftPrimary = Settings.EditorKeys.EditorKeyLeft.Primary.ToString(),
                EditorKeyLeftAlt = Settings.EditorKeys.EditorKeyLeft.Alt.ToString(),
                EditorKeyRightPrimary = Settings.EditorKeys.EditorKeyRight.Primary.ToString(),
                EditorKeyRightAlt = Settings.EditorKeys.EditorKeyRight.Alt.ToString(),
                EditorKeyBigPrimary = Settings.EditorKeys.EditorKeyBig.Primary.ToString(),
                EditorKeyBigAlt = Settings.EditorKeys.EditorKeyBig.Alt.ToString(),
                RestartHoldDelaySec = Settings.RestartHoldDelaySec,
                UnpauseDelaySec = Settings.UnpauseDelaySec
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(userSettings, options);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Silently ignore save errors to not break the game
        }
    }

    private static void ApplyDefaultSettings()
    {
        Settings.ScrollSpeed = 500f;
        Settings.MasterVolume = 0.8f;
        Settings.MusicVolume = 0.7f;
        Settings.SfxVolume = 0.8f;
        Settings.JudgementOffsetMs = 0;
        Settings.ResolutionWidth = 1280;
        Settings.ResolutionHeight = 720;
        Settings.TargetFPS = 60;
        Settings.VSync = false;
        Settings.Fullscreen = false;
        Settings.HitOne = new Binding(KeyboardKey.One, KeyboardKey.Kp1);
        Settings.HitZero = new Binding(KeyboardKey.Zero, KeyboardKey.Kp0);
        Settings.HitSecial = new Binding(KeyboardKey.Space, KeyboardKey.Space);
        Settings.RestartHold = new Binding(KeyboardKey.R, KeyboardKey.Null);
        Settings.RestartHoldDelaySec = 0.75f;
        Settings.PauseToggle = new Binding(KeyboardKey.Escape, KeyboardKey.Null);
        Settings.UnpauseDelaySec = 0.5f;

        // Initialize editor keybinds with defaults
        Settings.EditorKeys = new EditorKeybinds
        {
            EditorKeyLeft = new Binding(KeyboardKey.One, KeyboardKey.Null),
            EditorKeyRight = new Binding(KeyboardKey.Zero, KeyboardKey.Null),
            EditorKeyBig = new Binding(KeyboardKey.Space, KeyboardKey.Null)
        };
    }

    private static void ApplyUserSettings(UserSettings userSettings)
    {
        Settings.ScrollSpeed = userSettings.ScrollSpeed;
        Settings.MasterVolume = userSettings.MasterVolume;
        Settings.MusicVolume = userSettings.MusicVolume;
        Settings.SfxVolume = userSettings.SfxVolume;
        Settings.JudgementOffsetMs = userSettings.JudgementOffsetMs;

        // Apply resolution settings with safety limits
        Settings.ResolutionWidth = Math.Max(800, Math.Min(3840, userSettings.ResolutionWidth));
        Settings.ResolutionHeight = Math.Max(600, Math.Min(2160, userSettings.ResolutionHeight));
        Settings.TargetFPS = Math.Max(30, Math.Min(240, userSettings.TargetFPS));
        Settings.VSync = userSettings.VSync;
        Settings.Fullscreen = userSettings.Fullscreen;

        // Parse keybinds with fallback to defaults
        Settings.HitOne = new Binding(
            ParseKeyboardKey(userSettings.HitOnePrimary, KeyboardKey.One),
            ParseKeyboardKey(userSettings.HitOneAlt, KeyboardKey.Kp1));
        Settings.HitZero = new Binding(
            ParseKeyboardKey(userSettings.HitZeroPrimary, KeyboardKey.Zero),
            ParseKeyboardKey(userSettings.HitZeroAlt, KeyboardKey.Kp0));
        Settings.HitSecial = new Binding(
            ParseKeyboardKey(userSettings.HitSpacePrimary, KeyboardKey.Space),
            ParseKeyboardKey(userSettings.HitSpaceAlt, KeyboardKey.Space));
        Settings.RestartHold = new Binding(
            ParseKeyboardKey(userSettings.RestartHoldPrimary, KeyboardKey.R),
            ParseKeyboardKey(userSettings.RestartHoldAlt, KeyboardKey.Null));
        Settings.PauseToggle = new Binding(
            ParseKeyboardKey(userSettings.PauseTogglePrimary, KeyboardKey.Escape),
            ParseKeyboardKey(userSettings.PauseToggleAlt, KeyboardKey.Null));

        // Load editor keybinds
        Settings.EditorKeys = new EditorKeybinds
        {
            EditorKeyLeft = new Binding(
                ParseKeyboardKey(userSettings.EditorKeyLeftPrimary, KeyboardKey.One),
                ParseKeyboardKey(userSettings.EditorKeyLeftAlt, KeyboardKey.Null)),
            EditorKeyRight = new Binding(
                ParseKeyboardKey(userSettings.EditorKeyRightPrimary, KeyboardKey.Zero),
                ParseKeyboardKey(userSettings.EditorKeyRightAlt, KeyboardKey.Null)),
            EditorKeyBig = new Binding(
                ParseKeyboardKey(userSettings.EditorKeyBigPrimary, KeyboardKey.Space),
                ParseKeyboardKey(userSettings.EditorKeyBigAlt, KeyboardKey.Null))
        };

        Settings.RestartHoldDelaySec = userSettings.RestartHoldDelaySec;
        Settings.UnpauseDelaySec = userSettings.UnpauseDelaySec;
    }

    private static KeyboardKey ParseKeyboardKey(string keyString, KeyboardKey defaultKey)
    {
        if (string.IsNullOrWhiteSpace(keyString))
            return defaultKey;

        if (Enum.TryParse<KeyboardKey>(keyString, out var key))
            return key;

        return defaultKey;
    }

    // Helper method to get current resolution index
    private static int GetCurrentResolutionIndex()
    {
        for (int i = 0; i < resolutionWidths.Length; i++)
        {
            if (resolutionWidths[i] == Settings.ResolutionWidth && resolutionHeights[i] == Settings.ResolutionHeight)
                return i;
        }
        return 0; // Default to first resolution if not found
    }

    // Helper method to draw sliders with labels positioned above them
    static void DrawSliderWithTopLabel(Rectangle rect, string label, float value, float min, float max, string? unit, int labelX, int labelY)
    {
        // Draw label above the slider
        DrawText(label, labelX, labelY, 18, Color.LightGray);

        // Draw slider background
        DrawRectangleRec(rect, new Color(30, 36, 50, 255));

        // Draw slider track and fill
        float t = (value - min) / (max - min);
        float knobX = rect.X + t * rect.Width;
        DrawRectangle((int)rect.X, (int)rect.Y + 8, (int)rect.Width, 8, new Color(60, 72, 100, 255));
        DrawRectangle((int)(rect.X), (int)rect.Y + 8, (int)(t * rect.Width), 8, new Color(120, 160, 220, 255));
        DrawCircle((int)knobX, (int)rect.Y + 12, 10, new Color(220, 230, 255, 255));

        // Draw value text to the right of the slider
        string val = unit == null ? $"{value:0.##}" : $"{value:0.##} {unit}";
        DrawText(val, (int)(rect.X + rect.Width + 12), (int)rect.Y - 2, 20, Color.Gray);
    }

    static void Main(string[] args)
    {
        // Load persistent user settings first
        LoadUserSettings();

        // Safety check: limit window size to reasonable bounds
        int safeWidth = Math.Max(800, Math.Min(1920, Settings.ResolutionWidth));
        int safeHeight = Math.Max(600, Math.Min(1080, Settings.ResolutionHeight));

        InitWindow(safeWidth, safeHeight, "Bainari");
        // Disable ESC auto-close
        SetExitKey(KeyboardKey.Null);

        // Apply display settings
        if (Settings.VSync)
            SetTargetFPS(0); // 0 enables VSync
        else
            SetTargetFPS(Settings.TargetFPS);

        InitAudioDevice();

        // Detect charts folder
        string baseDir = AppContext.BaseDirectory;
        string chartsDirExe = Path.Combine(baseDir, "charts");
        string chartsDirProject = Path.Combine("charts");
        string chartsRoot = Directory.Exists(chartsDirExe) ? chartsDirExe : chartsDirProject;

        // Build song list
        List<SongEntry> songs = SongEntry.ScanCharts(chartsRoot);
        if (songs.Count == 0)
        {
            while (!WindowShouldClose())
            {
                BeginDrawing();
                ClearBackground(Color.Black);
                DrawText("No charts found in ./charts", 60, 60, 40, Color.White);
                DrawText("Add *.chart.json files to the charts/ folder.", 60, 120, 24, Color.Gray);
                DrawText("Click anywhere to exit.", 60, 160, 24, Color.Gray);
                EndDrawing();
                if (IsMouseButtonReleased(MouseButton.Left)) break;
            }
            CloseAudioDevice();
            CloseWindow();
            return;
        }

        // App state
        int selected = 0;
        Game? game = null;
        Editor? editor = null;
        Rectangle songsPanel = new Rectangle(50, 150, 820, 420);

        // Main loop
        while (!WindowShouldClose())
        {
            switch (mode)
            {
                // -------------------- MAIN MENU --------------------
                case AppMode.MainMenu:
                    {
                        int screenW = GetScreenWidth();
                        int screenH = GetScreenHeight();
                        int btnW = Math.Min(320, screenW - 120); // Responsive button width
                        int btnH = 56;
                        int startY = Math.Max(220, screenH / 4); // Start buttons at 1/4 screen height or 220, whichever is smaller

                        Rectangle btnPlay = new Rectangle(60, startY, btnW, btnH);
                        Rectangle btnSettings = new Rectangle(60, startY + 70, btnW, btnH);
                        Rectangle btnQuit = new Rectangle(60, startY + 140, btnW, btnH);

                        bool goPlay = UI.Button(btnPlay, "Play");
                        bool goSettings = UI.Button(btnSettings, "Settings");
                        bool goQuit = UI.Button(btnQuit, "Quit");

                        if (goPlay) mode = AppMode.SongSelect;
                        if (goSettings) mode = AppMode.Settings;
                        if (goQuit) { game?.Dispose(); editor?.Dispose(); CloseAudioDevice(); CloseWindow(); return; }

                        BeginDrawing();
                        ClearBackground(new Color(10, 12, 18, 255));
                        int titleSize = Math.Min(64, screenW / 20); // Responsive title size
                        int subtitleSize = Math.Min(22, screenW / 58); // Responsive subtitle size
                        DrawText("Binary Taiko", 60, 60, titleSize, Color.White);
                        DrawText("Click buttons to navigate. Keyboard still works if you prefer.", 60, 60 + titleSize + 10, subtitleSize, Color.Gray);
                        UI.DrawButton(btnPlay, "Play");
                        UI.DrawButton(btnSettings, "Settings");
                        UI.DrawButton(btnQuit, "Quit");
                        EndDrawing();
                        break;
                    }

                // -------------------- SETTINGS --------------------
                case AppMode.Settings:
                    {
                        // Settings panel layout
                        int screenW = GetScreenWidth();
                        int screenH = GetScreenHeight();
                        int panelX = 60;
                        int panelY = 120;
                        int panelW = screenW - 120;
                        int panelH = screenH - 200;

                        // Tab layout
                        int tabHeight = 50;
                        int tabY = panelY - tabHeight;
                        string[] tabNames = { "Audio", "Gameplay", "Display", "Controls", "Visuals" };
                        int tabWidth = panelW / tabNames.Length;

                        // Draw tab buttons
                        for (int i = 0; i < tabNames.Length; i++)
                        {
                            int tabX = panelX + (i * tabWidth);
                            Rectangle tabRect = new Rectangle(tabX, tabY, tabWidth, tabHeight);

                            bool isActive = currentSettingsTab == i;
                            bool isHovered = CheckCollisionPointRec(GetMousePosition(), tabRect);
                            bool wasClicked = isHovered && IsMouseButtonPressed(MouseButton.Left);

                            if (wasClicked)
                            {
                                currentSettingsTab = i;
                                settingsScrollY = 0f; // Reset scroll when switching tabs
                            }

                            // Tab colors
                            Color tabColor = isActive ? new Color(70, 80, 100, 255) :
                                           isHovered ? new Color(60, 70, 90, 255) :
                                           new Color(50, 60, 80, 255);
                            Color borderColor = isActive ? new Color(120, 140, 180, 255) : new Color(80, 100, 140, 255);

                            // Draw tab
                            DrawRectangleRounded(tabRect, 0.1f, 8, tabColor);
                            if (isActive)
                            {
                                // Active tab gets a bottom border connecting to panel
                                DrawRectangle(tabX, tabY + tabHeight - 3, tabWidth, 3, tabColor);
                            }
                            DrawRectangleRoundedLinesEx(tabRect, 0.1f, 8, 2, borderColor);

                            // Tab text
                            int textWidth = MeasureText(tabNames[i], 20);
                            Color textColor = isActive ? Color.White : new Color(200, 200, 200, 255);
                            DrawText(tabNames[i], tabX + tabWidth / 2 - textWidth / 2, tabY + tabHeight / 2 - 10, 20, textColor);
                        }

                        // Content layout parameters
                        int contentX = panelX + 20;
                        int currentY = panelY + 20;

                        // Content padding and scrollable area layout
                        int contentPadding = 30; // Left/right padding inside scrollable area
                        int topPadding = 25;     // Top padding inside scrollable area
                        int scrollableX = panelX + contentPadding;
                        int scrollableY = panelY + topPadding;
                        int scrollableW = panelW - (contentPadding * 2) - 20; // Account for scrollbar
                        int scrollableH = panelH - (topPadding * 2);

                        // Calculate total content height for scrolling based on current tab
                        int totalContentHeight = 0;
                        int sliderSpacing = 50;
                        int headerHeight = 50;

                        switch (currentSettingsTab)
                        {
                            case 0: // Audio
                                int audioSliderCount = 3; // Master Volume, Music Volume, SFX Volume
                                totalContentHeight = topPadding + headerHeight + (audioSliderCount * sliderSpacing) + 40;
                                break;
                            case 1: // Gameplay
                                int gameplaySliderCount = 4; // Scroll Speed, Judgement Offset, Restart Hold Delay, Unpause Delay
                                totalContentHeight = topPadding + headerHeight + (gameplaySliderCount * sliderSpacing) + 40;
                                break;
                            case 2: // Display
                                int displayControlCount = 5;
                                totalContentHeight = topPadding + headerHeight + (displayControlCount * 50) + 60;
                                break;
                            case 3: // Controls
                                int keybindCount = 5; // Hit 1, Hit 0, Hit Special, Restart, Pause
                                totalContentHeight = topPadding + headerHeight + (keybindCount * 50) + 40;
                                break;
                            case 4: // Visuals
                                int visualsCount = 1; // Show Accuracy Bar checkbox
                                totalContentHeight = topPadding + headerHeight + (visualsCount * 50) + 40;
                                break;
                        }

                        // Handle mouse wheel scrolling when over the scrollable area
                        Vector2 mousePos = GetMousePosition();
                        bool mouseOverPanel = mousePos.X >= scrollableX && mousePos.X <= scrollableX + scrollableW &&
                                            mousePos.Y >= scrollableY && mousePos.Y <= scrollableY + scrollableH;
                        // Only process mouse wheel if not dragging scrollbar
                        if (mouseOverPanel && !settingsDraggingScrollbar)
                        {
                            float wheel = GetMouseWheelMove();
                            settingsScrollY -= wheel * 40f; // Scroll speed
                        }

                        // Clamp scroll to new scrollable area
                        float maxScroll = Math.Max(0f, totalContentHeight - scrollableH);

                        // Handle scrollbar mouse interaction (only if content exceeds container)
                        if (totalContentHeight > scrollableH)
                        {
                            int scrollbarX = scrollableX + scrollableW + 5;
                            int scrollbarY = scrollableY;
                            int scrollbarW = 12;
                            int scrollbarH = scrollableH;

                            // Calculate thumb properties
                            float thumbHeight = ((float)scrollableH / totalContentHeight) * scrollbarH;
                            float thumbY = scrollbarY + (settingsScrollY / maxScroll) * (scrollbarH - thumbHeight);

                            Rectangle thumbRect = new Rectangle(scrollbarX, thumbY, scrollbarW, thumbHeight);
                            Rectangle trackRect = new Rectangle(scrollbarX, scrollbarY, scrollbarW, scrollbarH);

                            bool mouseOverThumb = CheckCollisionPointRec(mousePos, thumbRect);
                            bool mouseOverTrack = CheckCollisionPointRec(mousePos, trackRect);

                            // Handle thumb dragging
                            if (IsMouseButtonPressed(MouseButton.Left) && mouseOverThumb)
                            {
                                settingsDraggingScrollbar = true;
                                settingsScrollbarDragOffset = mousePos.Y - thumbY;
                            }

                            if (settingsDraggingScrollbar)
                            {
                                if (IsMouseButtonDown(MouseButton.Left))
                                {
                                    // Calculate new scroll position based on mouse movement
                                    float newThumbY = mousePos.Y - settingsScrollbarDragOffset;
                                    float thumbRange = scrollbarH - thumbHeight;
                                    float scrollPercent = (newThumbY - scrollbarY) / thumbRange;
                                    settingsScrollY = scrollPercent * maxScroll;
                                }
                                else
                                {
                                    settingsDraggingScrollbar = false;
                                }
                            }

                            // Handle track clicking (page scroll)
                            if (IsMouseButtonPressed(MouseButton.Left) && mouseOverTrack && !mouseOverThumb && !settingsDraggingScrollbar)
                            {
                                float pageSize = scrollableH * 0.8f; // 80% of visible area
                                if (mousePos.Y < thumbY)
                                {
                                    settingsScrollY -= pageSize; // Page up
                                }
                                else if (mousePos.Y > thumbY + thumbHeight)
                                {
                                    settingsScrollY += pageSize; // Page down
                                }
                            }
                        }

                        // Clamp scroll after all interactions
                        if (settingsScrollY < 0f) settingsScrollY = 0f;
                        if (settingsScrollY > maxScroll) settingsScrollY = maxScroll;

                        // Calculate positions with new scrollable area coordinates
                        int scrolledY = scrollableY + 20 - (int)settingsScrollY; // Start position with scroll offset

                        // Slider positions (centered horizontally with reduced width for better layout)
                        int sliderWidth = (int)(scrollableW * 0.75f); // 75% of available width
                        int sliderStartX = scrollableX + (scrollableW - sliderWidth) / 2; // Center horizontally
                        int sliderVerticalSpacing = 50; // Increased spacing to accommodate labels above

                        // Tab 0 – Audio: vol, musicVol, sfxVol (slots 0-2)
                        Rectangle volRect      = new Rectangle(sliderStartX, scrolledY + 55,                            sliderWidth, 24);
                        Rectangle musicVolRect = new Rectangle(sliderStartX, scrolledY + 55 + sliderVerticalSpacing,     sliderWidth, 24);
                        Rectangle sfxVolRect   = new Rectangle(sliderStartX, scrolledY + 55 + sliderVerticalSpacing * 2, sliderWidth, 24);

                        // Tab 1 – Gameplay: spd, off, rst, unp (slots 0-3, same start offset)
                        Rectangle spdRect = new Rectangle(sliderStartX, scrolledY + 55,                            sliderWidth, 24);
                        Rectangle offRect = new Rectangle(sliderStartX, scrolledY + 55 + sliderVerticalSpacing,     sliderWidth, 24);
                        Rectangle rstRect = new Rectangle(sliderStartX, scrolledY + 55 + sliderVerticalSpacing * 2, sliderWidth, 24);
                        Rectangle unpRect = new Rectangle(sliderStartX, scrolledY + 55 + sliderVerticalSpacing * 3, sliderWidth, 24);

                        Rectangle backBtn = new Rectangle(panelX, screenH - 70, 200, 50);

                        // Tab-conditional slider interactions (prevents ghost input on hidden sliders)
                        if (currentSettingsTab == 0) // Audio
                        {
                            float vol = Settings.MasterVolume;
                            UI.Slider(volRect, "Master Volume", ref vol, 0f, 1f, null);
                            if (Settings.MasterVolume != vol)
                            {
                                Settings.MasterVolume = vol;
                                SaveUserSettings();
                                if (game != null)
                                {
                                    SetMusicVolume(game.Music, Settings.MasterVolume * Settings.MusicVolume);
                                    game.UpdateSoundVolumes(Settings.MasterVolume * Settings.SfxVolume, Settings.MasterVolume * Settings.MusicVolume);
                                }
                                if (editor != null)
                                {
                                    SetMusicVolume(editor.Music, Settings.MasterVolume * Settings.MusicVolume);
                                    editor.UpdateSoundVolumes(Settings.MasterVolume * Settings.SfxVolume, Settings.MasterVolume * Settings.MusicVolume);
                                }
                            }

                            float musicVol = Settings.MusicVolume;
                            UI.Slider(musicVolRect, "Music Volume", ref musicVol, 0f, 1f, null);
                            if (Settings.MusicVolume != musicVol)
                            {
                                Settings.MusicVolume = musicVol;
                                SaveUserSettings();
                                if (game != null) SetMusicVolume(game.Music, Settings.MasterVolume * Settings.MusicVolume);
                                if (editor != null)
                                {
                                    SetMusicVolume(editor.Music, Settings.MasterVolume * Settings.MusicVolume);
                                    editor.UpdateSoundVolumes(Settings.MasterVolume * Settings.SfxVolume, Settings.MasterVolume * Settings.MusicVolume);
                                }
                            }

                            float sfxVol = Settings.SfxVolume;
                            UI.Slider(sfxVolRect, "SFX Volume", ref sfxVol, 0f, 1f, null);
                            if (Settings.SfxVolume != sfxVol)
                            {
                                Settings.SfxVolume = sfxVol;
                                SaveUserSettings();
                                if (game != null) game.UpdateSoundVolumes(Settings.MasterVolume * Settings.SfxVolume, Settings.MasterVolume * Settings.MusicVolume);
                                if (editor != null) editor.UpdateSoundVolumes(Settings.MasterVolume * Settings.SfxVolume, Settings.MasterVolume * Settings.MusicVolume);
                            }
                        }
                        else if (currentSettingsTab == 1) // Gameplay
                        {
                            float spd = Settings.ScrollSpeed;
                            UI.Slider(spdRect, "Scroll Speed", ref spd, 200f, 1200f, "px/sec");
                            if (Settings.ScrollSpeed != spd) { Settings.ScrollSpeed = spd; SaveUserSettings(); }

                            float offsetSec = Settings.JudgementOffsetMs / 1000f;
                            UI.Slider(offRect, "Judgement Offset", ref offsetSec, -0.150f, 0.150f, "sec");
                            int newOffset = (int)MathF.Round(offsetSec * 1000f);
                            if (Settings.JudgementOffsetMs != newOffset) { Settings.JudgementOffsetMs = newOffset; SaveUserSettings(); }

                            float rstDelay = Settings.RestartHoldDelaySec;
                            UI.Slider(rstRect, "Restart Hold Delay", ref rstDelay, 0.2f, 2.0f, "sec");
                            if (Settings.RestartHoldDelaySec != rstDelay) { Settings.RestartHoldDelaySec = rstDelay; SaveUserSettings(); }

                            float unpDelay = Settings.UnpauseDelaySec;
                            UI.Slider(unpRect, "Unpause Delay", ref unpDelay, 0.1f, 2.0f, "sec");
                            if (Settings.UnpauseDelaySec != unpDelay) { Settings.UnpauseDelaySec = unpDelay; SaveUserSettings(); }
                        }

                        // Keybinding interaction will be handled in the drawing section

                        if (UI.Button(backBtn, "Back")) mode = AppMode.MainMenu;

                        // Draw
                        BeginDrawing();
                        ClearBackground(new Color(12, 12, 18, 255));

                        // Draw settings panel background
                        DrawRectangle(panelX, panelY, panelW, panelH, new Color(20, 24, 32, 180));
                        DrawRectangleLines(panelX, panelY, panelW, panelH, new Color(60, 70, 90, 255));

                        // Clip content to scrollable area with padding
                        BeginScissorMode(scrollableX, scrollableY, scrollableW, scrollableH);

                        // === SETTINGS CONTENT BASED ON CURRENT TAB ===
                        int contentLeftPadding = 15; // Consistent left padding for all content
                        int sectionHeaderX = scrollableX + contentLeftPadding;
                        int currentDrawY = scrollableY + 10 - (int)settingsScrollY; // Start position with scroll offset (reduced from 25 to 10)

                        if (currentSettingsTab == 0) // Audio
                        {
                            DrawSliderWithTopLabel(volRect, "Master Volume", Settings.MasterVolume, 0f, 1f, null,
                                                sliderStartX, (int)volRect.Y - 22);
                            DrawSliderWithTopLabel(musicVolRect, "Music Volume", Settings.MusicVolume, 0f, 1f, null,
                                                sliderStartX, (int)musicVolRect.Y - 22);
                            DrawSliderWithTopLabel(sfxVolRect, "SFX Volume", Settings.SfxVolume, 0f, 1f, null,
                                                sliderStartX, (int)sfxVolRect.Y - 22);
                        }
                        else if (currentSettingsTab == 1) // Gameplay
                        {
                            DrawSliderWithTopLabel(spdRect, "Scroll Speed", Settings.ScrollSpeed, 200f, 1200f, "px/sec",
                                            sliderStartX, (int)spdRect.Y - 22);
                            DrawSliderWithTopLabel(offRect, "Judgement Offset", Settings.JudgementOffsetMs / 1000f, -0.150f, 0.150f, "sec",
                                        sliderStartX, (int)offRect.Y - 22);
                            DrawSliderWithTopLabel(rstRect, "Restart Hold Delay", Settings.RestartHoldDelaySec, 0.2f, 2.0f, "sec",
                                                sliderStartX, (int)rstRect.Y - 22);
                            DrawSliderWithTopLabel(unpRect, "Unpause Delay", Settings.UnpauseDelaySec, 0.1f, 2.0f, "sec",
                                            sliderStartX, (int)unpRect.Y - 22);
                        }
                        else if (currentSettingsTab == 2) // Display Settings
                        {

                            // Display settings controls positioning
                            int displayControlY = currentDrawY;
                            int displayControlSpacing = 50;
                            int displayLabelX = scrollableX + contentLeftPadding;
                            int displayControlX = scrollableX + 200; // Position controls to the right of labels
                            int displayControlW = 200;
                            int displayControlH = 30;

                            // Resolution selector
                            Rectangle resolutionRect = new Rectangle(displayControlX, displayControlY, displayControlW, displayControlH);
                            DrawText("Resolution", displayLabelX, displayControlY + 5, 20, Color.White);

                            int currentResIndex = GetCurrentResolutionIndex();
                            int newResIndex = UI.OptionSelector(resolutionRect, resolutionOptions, currentResIndex);
                            UI.DrawOptionSelector(resolutionRect, resolutionOptions, currentResIndex);

                            if (newResIndex != currentResIndex)
                            {
                                Settings.ResolutionWidth = resolutionWidths[newResIndex];
                                Settings.ResolutionHeight = resolutionHeights[newResIndex];
                                displaySettingsChanged = true;
                                SaveUserSettings();
                            }
                            displayControlY += displayControlSpacing;

                            // FPS slider
                            Rectangle fpsRect = new Rectangle(displayControlX, displayControlY, displayControlW, 24);
                            DrawText("Target FPS", displayLabelX, displayControlY - 18, 18, Color.LightGray);
                            float fps = Settings.TargetFPS;
                            UI.Slider(fpsRect, "Target FPS", ref fps, 30f, 240f, "fps");
                            if (Settings.TargetFPS != (int)fps)
                            {
                                Settings.TargetFPS = (int)fps;
                                displaySettingsChanged = true;
                                SaveUserSettings();
                            }

                            // Draw FPS slider
                            DrawRectangleRec(fpsRect, new Color(30, 36, 50, 255));
                            float fpsT = (Settings.TargetFPS - 30f) / (240f - 30f);
                            float fpsKnobX = fpsRect.X + fpsT * fpsRect.Width;
                            DrawRectangle((int)fpsRect.X, (int)fpsRect.Y + 8, (int)fpsRect.Width, 8, new Color(60, 72, 100, 255));
                            DrawRectangle((int)(fpsRect.X), (int)fpsRect.Y + 8, (int)(fpsT * fpsRect.Width), 8, new Color(120, 160, 220, 255));
                            DrawCircle((int)fpsKnobX, (int)fpsRect.Y + 12, 10, new Color(220, 230, 255, 255));
                            string fpsVal = $"{Settings.TargetFPS} fps";
                            DrawText(fpsVal, (int)(fpsRect.X + fpsRect.Width + 12), (int)fpsRect.Y - 2, 20, Color.Gray);

                            displayControlY += displayControlSpacing;

                            // VSync checkbox
                            Rectangle vsyncRect = new Rectangle(displayControlX, displayControlY, 30, 30);
                            DrawText("VSync", displayLabelX, displayControlY + 5, 20, Color.White);

                            bool newVSync = UI.Checkbox(vsyncRect, Settings.VSync);
                            UI.DrawCheckbox(vsyncRect, Settings.VSync);

                            if (newVSync != Settings.VSync)
                            {
                                Settings.VSync = newVSync;
                                displaySettingsChanged = true;
                                SaveUserSettings();
                            }

                            displayControlY += displayControlSpacing;

                            // Fullscreen checkbox
                            Rectangle fullscreenRect = new Rectangle(displayControlX, displayControlY, 30, 30);
                            DrawText("Fullscreen", displayLabelX, displayControlY + 5, 20, Color.White);

                            bool newFullscreen = UI.Checkbox(fullscreenRect, Settings.Fullscreen);
                            UI.DrawCheckbox(fullscreenRect, Settings.Fullscreen);

                            if (newFullscreen != Settings.Fullscreen)
                            {
                                Settings.Fullscreen = newFullscreen;
                                displaySettingsChanged = true;
                                SaveUserSettings();
                            }

                            displayControlY += displayControlSpacing;

                            // Apply display settings changes immediately
                            if (displaySettingsChanged)
                            {
                                // Apply fullscreen change
                                if (Settings.Fullscreen && !IsWindowFullscreen())
                                {
                                    ToggleFullscreen();
                                }
                                else if (!Settings.Fullscreen && IsWindowFullscreen())
                                {
                                    ToggleFullscreen();
                                }

                                // Apply resolution change (only in windowed mode)
                                if (!Settings.Fullscreen)
                                {
                                    SetWindowSize(Settings.ResolutionWidth, Settings.ResolutionHeight);
                                }

                                // Apply FPS change
                                SetTargetFPS(Settings.TargetFPS);

                                displaySettingsChanged = false; // Reset the flag
                                DrawText("Display settings applied! VSync requires restart.", displayLabelX, displayControlY, 16, new Color(100, 255, 100, 255));
                            }

                        }
                        else if (currentSettingsTab == 3) // Controls
                        {

                            // Keybinding layout parameters
                            int keybindRowSpacing = 50; // Equal vertical spacing
                            int keybindLabelX = scrollableX + contentLeftPadding; // Left-aligned labels
                            int keybindButtonW = 120; // Button width
                            int keybindButtonH = 36;
                            int keybindButtonSpacing = 12; // Space between primary/alt buttons
                            int keybindRightMargin = 30; // Margin from right edge of scrollable area
                            int keybindButtonX = scrollableX + scrollableW - keybindRightMargin - (keybindButtonW * 2) - keybindButtonSpacing; // Right-aligned buttons

                            // Create properly aligned button rectangles
                            Rectangle b1p = new Rectangle(keybindButtonX, currentDrawY, keybindButtonW, keybindButtonH);
                            Rectangle b1a = new Rectangle(keybindButtonX + keybindButtonW + keybindButtonSpacing, currentDrawY, keybindButtonW, keybindButtonH);
                            Rectangle b0p = new Rectangle(keybindButtonX, currentDrawY + keybindRowSpacing, keybindButtonW, keybindButtonH);
                            Rectangle b0a = new Rectangle(keybindButtonX + keybindButtonW + keybindButtonSpacing, currentDrawY + keybindRowSpacing, keybindButtonW, keybindButtonH);
                            Rectangle bsp = new Rectangle(keybindButtonX, currentDrawY + keybindRowSpacing * 2, keybindButtonW, keybindButtonH);
                            Rectangle bsa = new Rectangle(keybindButtonX + keybindButtonW + keybindButtonSpacing, currentDrawY + keybindRowSpacing * 2, keybindButtonW, keybindButtonH);
                            Rectangle brp = new Rectangle(keybindButtonX, currentDrawY + keybindRowSpacing * 3, keybindButtonW, keybindButtonH);
                            Rectangle bra = new Rectangle(keybindButtonX + keybindButtonW + keybindButtonSpacing, currentDrawY + keybindRowSpacing * 3, keybindButtonW, keybindButtonH);
                            Rectangle bpp = new Rectangle(keybindButtonX, currentDrawY + keybindRowSpacing * 4, keybindButtonW, keybindButtonH);
                            Rectangle bpa = new Rectangle(keybindButtonX + keybindButtonW + keybindButtonSpacing, currentDrawY + keybindRowSpacing * 4, keybindButtonW, keybindButtonH);

                            // Interactive keybinding UI with aligned labels and buttons
                            int keybindY = currentDrawY;

                            // Hit 1
                            UI.DrawKeybindLabel(keybindLabelX, keybindY + 8, "Hit 1");
                            var bind1 = Settings.HitOne;
                            var oldBind1Primary = bind1.Primary;
                            var oldBind1Alt = bind1.Alt;
                            bind1.Primary = UI.KeyButton(b1p, "one_primary", bind1.Primary);
                            bind1.Alt = UI.KeyButton(b1a, "one_alt", bind1.Alt);
                            if (oldBind1Primary != bind1.Primary || oldBind1Alt != bind1.Alt)
                            {
                                Settings.HitOne = bind1;
                                SaveUserSettings();
                            }
                            keybindY += keybindRowSpacing;

                            // Hit 0  
                            UI.DrawKeybindLabel(keybindLabelX, keybindY + 8, "Hit 0");
                            var bind0 = Settings.HitZero;
                            var oldBind0Primary = bind0.Primary;
                            var oldBind0Alt = bind0.Alt;
                            bind0.Primary = UI.KeyButton(b0p, "zero_primary", bind0.Primary);
                            bind0.Alt = UI.KeyButton(b0a, "zero_alt", bind0.Alt);
                            if (oldBind0Primary != bind0.Primary || oldBind0Alt != bind0.Alt)
                            {
                                Settings.HitZero = bind0;
                                SaveUserSettings();
                            }
                            keybindY += keybindRowSpacing;

                            // Hit Special
                            UI.DrawKeybindLabel(keybindLabelX, keybindY + 8, "Hit Special");
                            var bindS = Settings.HitSecial;
                            var oldBindSPrimary = bindS.Primary;
                            var oldBindSAlt = bindS.Alt;
                            bindS.Primary = UI.KeyButton(bsp, "space_primary", bindS.Primary);
                            bindS.Alt = UI.KeyButton(bsa, "space_alt", bindS.Alt);
                            if (oldBindSPrimary != bindS.Primary || oldBindSAlt != bindS.Alt)
                            {
                                Settings.HitSecial = bindS;
                                SaveUserSettings();
                            }
                            keybindY += keybindRowSpacing;

                            // Restart
                            UI.DrawKeybindLabel(keybindLabelX, keybindY + 8, "Restart");
                            var bindR = Settings.RestartHold;
                            var oldBindRPrimary = bindR.Primary;
                            var oldBindRAlt = bindR.Alt;
                            bindR.Primary = UI.KeyButton(brp, "restart_primary", bindR.Primary);
                            bindR.Alt = UI.KeyButton(bra, "restart_alt", bindR.Alt);
                            if (oldBindRPrimary != bindR.Primary || oldBindRAlt != bindR.Alt)
                            {
                                Settings.RestartHold = bindR;
                                SaveUserSettings();
                            }
                            keybindY += keybindRowSpacing;

                            // Pause
                            UI.DrawKeybindLabel(keybindLabelX, keybindY + 8, "Pause");
                            var bindP = Settings.PauseToggle;
                            var oldBindPPrimary = bindP.Primary;
                            var oldBindPAlt = bindP.Alt;
                            bindP.Primary = UI.KeyButton(bpp, "pause_primary", bindP.Primary);
                            bindP.Alt = UI.KeyButton(bpa, "pause_alt", bindP.Alt);
                            if (oldBindPPrimary != bindP.Primary || oldBindPAlt != bindP.Alt)
                            {
                                Settings.PauseToggle = bindP;
                                SaveUserSettings();
                            }

                        }
                        else if (currentSettingsTab == 4) // Visuals
                        {

                            // Accuracy Bar checkbox - using same positioning and size as Display Settings
                            int visualsLabelX = scrollableX + contentLeftPadding;
                            int visualsControlX = scrollableX + 250; // Moved further right to avoid overlap with text
                            Rectangle accuracyBarRect = new Rectangle(visualsControlX, currentDrawY, 30, 30); // Same size as VSync checkbox
                            DrawText("Show Accuracy Bar", visualsLabelX, currentDrawY + 5, 20, Color.White); // Same vertical offset as VSync

                            bool newShowAccuracyBar = UI.Checkbox(accuracyBarRect, Settings.ShowAccuracyBar);
                            UI.DrawCheckbox(accuracyBarRect, Settings.ShowAccuracyBar);

                            if (newShowAccuracyBar != Settings.ShowAccuracyBar)
                            {
                                Settings.ShowAccuracyBar = newShowAccuracyBar;
                                SaveUserSettings();
                            }
                            currentDrawY += 50; // Same spacing as other sections
                        }

                        EndScissorMode();

                        // Draw scrollbar if content exceeds scrollable area height
                        if (totalContentHeight > scrollableH)
                        {
                            int scrollbarX = scrollableX + scrollableW + 5; // Position at right edge of scrollable area
                            int scrollbarY = scrollableY;
                            int scrollbarW = 12;
                            int scrollbarH = scrollableH;

                            // Calculate thumb properties for visual feedback
                            float thumbHeight = ((float)scrollableH / totalContentHeight) * scrollbarH;
                            float thumbY = scrollbarY + (settingsScrollY / maxScroll) * (scrollbarH - thumbHeight);
                            Rectangle thumbRect = new Rectangle(scrollbarX, thumbY, scrollbarW, thumbHeight);

                            bool mouseOverThumb = CheckCollisionPointRec(mousePos, thumbRect);

                            // Track
                            DrawRectangle(scrollbarX, scrollbarY, scrollbarW, scrollbarH, new Color(40, 50, 65, 180));
                            DrawRectangleLines(scrollbarX, scrollbarY, scrollbarW, scrollbarH, new Color(80, 100, 140, 200));

                            // Thumb with hover/drag visual feedback
                            Color thumbColor;
                            if (settingsDraggingScrollbar)
                            {
                                thumbColor = new Color(160, 180, 220, 255); // Brighter when dragging
                            }
                            else if (mouseOverThumb)
                            {
                                thumbColor = new Color(140, 160, 200, 240); // Slightly brighter when hovering
                            }
                            else
                            {
                                thumbColor = new Color(120, 140, 180, 220); // Default color
                            }

                            DrawRectangle(scrollbarX + 2, (int)thumbY, scrollbarW - 4, (int)thumbHeight, thumbColor);
                        }

                        // Back button and footer (outside of scrolled content)
                        UI.DrawButton(backBtn, "Back");

                        // Instructional hint text positioned in bottom-right corner
                        string hintText = "Notes use audio-driven timing. Adjust offset if consistently early/late.";
                        int hintTextWidth = MeasureText(hintText, 16);
                        int hintTextX = screenW - hintTextWidth - 20; // Bottom-right with padding
                        int hintTextY = screenH - 25;  // Bottom edge with small margin
                        DrawText(hintText, hintTextX, hintTextY, 16, Color.Gray);

                        EndDrawing();
                        break;
                    }

                // -------------------- SONG SELECT --------------------
                case AppMode.SongSelect:
                    {
                        // row height
                        int rowH = songRowHeight;

                        // Song list layout inside songsPanel
                        float innerTop = songsPanel.Y + 10f;
                        float innerHeight = songsPanel.Height - 20f;
                        float contentHeight = songs.Count * rowH;
                        float maxScroll = MathF.Max(0f, contentHeight - innerHeight);

                        // Mouse + keyboard
                        Vector2 mp = GetMousePosition();

                        // --- Mouse wheel scrolls list vertically (no selection change) ---
                        if (!renamePopupOpen)
                        {
                            float wheel = GetMouseWheelMove();
                            if (wheel != 0)
                            {
                                songListScrollY -= wheel * rowH; // scroll by rows
                                songListScrollY = MathF.Max(0f, MathF.Min(maxScroll, songListScrollY));
                            }
                        }
                        // --- Keyboard navigation (preserve) ---
                        if (!renamePopupOpen)
                        {
                            if (IsKeyPressed(KeyboardKey.Down))
                            {
                                if (songs.Count > 0)
                                {
                                    selected = Math.Min(songs.Count - 1, selected + 1);
                                    float itemTop = innerTop + selected * rowH - songListScrollY;
                                    if (itemTop + rowH > innerTop + innerHeight - 2f)
                                        songListScrollY = MathF.Min(maxScroll, (selected + 1) * rowH - innerHeight + 2f);
                                }
                            }
                            if (IsKeyPressed(KeyboardKey.Up))
                            {
                                if (songs.Count > 0)
                                {
                                    selected = Math.Max(0, selected - 1);
                                    float itemTop = innerTop + selected * rowH - songListScrollY;
                                    if (itemTop < innerTop + 2f) songListScrollY = MathF.Max(0f, selected * rowH - 2f);
                                }
                            }
                        }

                        // --- Scrollbar geometry ---
                        float barX = songsPanel.X + songsPanel.Width - 14f;
                        float barW = 10f;
                        float barH;
                        if (contentHeight <= 0f) barH = innerHeight;
                        else
                        {
                            // proportional scrollbar height, clamped to a minimum
                            barH = (innerHeight * innerHeight) / MathF.Max(1f, contentHeight);
                            barH = MathF.Max(20f, MathF.Min(innerHeight, barH));
                        }

                        float barY;
                        if (maxScroll <= 0f) barY = innerTop;
                        else
                        {
                            float ratio = songListScrollY / maxScroll;
                            barY = innerTop + ratio * (innerHeight - barH);
                        }
                        Rectangle scrollbarRect = new Rectangle(barX, barY, barW, barH);

                        // --- Scrollbar dragging ---
                        if (IsMouseButtonDown(MouseButton.Left) && CheckCollisionPointRec(mp, scrollbarRect))
                        {
                            songDraggingScrollbar = true;
                            songScrollbarDragOffset = mp.Y - barY;
                        }
                        if (songDraggingScrollbar)
                        {
                            if (IsMouseButtonDown(MouseButton.Left))
                            {
                                float newBarY = mp.Y - songScrollbarDragOffset;
                                newBarY = MathF.Max(innerTop, MathF.Min(innerTop + innerHeight - barH, newBarY));
                                float ratio = (newBarY - innerTop) / MathF.Max(1f, (innerHeight - barH));
                                songListScrollY = ratio * maxScroll;
                            }
                            else
                            {
                                songDraggingScrollbar = false;
                            }
                        }

                        // --- Buttons on the right (play/edit/new/mods/back) ---
                        Rectangle playBtn = new Rectangle(900, 150, 240, 50);
                        Rectangle editBtn = new Rectangle(900, 210, 240, 50);
                        Rectangle newBtn = new Rectangle(900, 270, 240, 50);
                        Rectangle modsBtn = new Rectangle(900, 330, 240, 50);
                        Rectangle backBtn = new Rectangle(900, 390, 240, 50);

                        bool clickedPlay = false, clickedEdit = false, clickedNew = false, clickedMods = false, clickedBack = false;
                        if (!renamePopupOpen)
                        {
                            clickedPlay = UI.Button(playBtn, "Play");
                            clickedEdit = UI.Button(editBtn, "Open in Editor");
                            clickedNew = UI.Button(newBtn, "New Chart");
                            clickedMods = UI.Button(modsBtn, "Mods");
                            clickedBack = UI.Button(backBtn, "Back");
                            if (IsKeyPressed(KeyboardKey.Enter)) clickedPlay = true;
                        }

                        if (clickedBack)
                        {
                            contextMenuOpen = false;
                            mode = AppMode.MainMenu;
                            break;
                        }

                        if (clickedMods)
                        {
                            mode = AppMode.ModSelect;
                            break;
                        }

                        if (clickedNew)
                        {
                            try
                            {
                                string chartsDir = Path.Combine(chartsRoot);
                                Directory.CreateDirectory(chartsDir);
                                string fileName = $"new_chart_{DateTime.Now.Ticks}.chart.json";
                                string newPath = Path.Combine(chartsDir, fileName);
                                var newChart = new Chart { Title = "New Chart", Notes = new() };
                                ChartLoader.Save(newPath, newChart);
                                songs = SongEntry.ScanCharts(chartsRoot);
                                selected = Math.Max(0, songs.FindIndex(s => s.ChartPath == newPath));
                            }
                            catch { }
                        }

                        if (clickedEdit && songs.Count > 0)
                        {
                            var song = songs[selected];
                            var chart = ChartLoader.Load(song.ChartPath);
                            string audioPath = PathUtil.ResolveAudioPath(song.ChartPath, chart.Audio, out _);
                            var music = LoadMusicStream(audioPath);
                            StopMusicStream(music);
                            SetMusicVolume(music, Settings.MasterVolume * Settings.MusicVolume);

                            editor = new Editor(song, chart, music);
                            mode = AppMode.Editor;
                            break;
                        }

                        if (clickedPlay && songs.Count > 0)
                        {
                            var song = songs[selected];
                            var chart = ChartLoader.Load(song.ChartPath);
                            string audioPath = PathUtil.ResolveAudioPath(song.ChartPath, chart.Audio, out _);
                            var music = LoadMusicStream(audioPath);
                            StopMusicStream(music);
                            SetMusicVolume(music, Settings.MasterVolume * Settings.MusicVolume);

                            var judge = new Judge();
                            game = new Game(chart, music, judge, Settings.ScrollSpeed, Settings.JudgementOffsetMs);
                            game.Start(); // Start immediately, skip title screen
                            mode = AppMode.Game;
                            break;
                        }

                        // --- Row clicks to select ---
                        if (!renamePopupOpen && CheckCollisionPointRec(mp, songsPanel) && IsMouseButtonReleased(MouseButton.Left))
                        {
                            // check if clicked on scrollbar first -> handled above by dragging; ignore
                            if (!CheckCollisionPointRec(mp, scrollbarRect))
                            {
                                // compute clicked index (account for scrollY)
                                int idx = (int)((mp.Y - innerTop + songListScrollY) / rowH);
                                if (idx >= 0 && idx < songs.Count)
                                {
                                    selected = idx;
                                    // ensure visible
                                    float itemTop = innerTop + selected * rowH - songListScrollY;
                                    if (itemTop < innerTop + 2f) songListScrollY = MathF.Max(0f, selected * rowH - 2f);
                                    else if (itemTop + rowH > innerTop + innerHeight - 2f)
                                        songListScrollY = MathF.Min(maxScroll, (selected + 1) * rowH - innerHeight + 2f);
                                }
                            }
                        }

                        // --- Draw UI ---
                        BeginDrawing();
                        ClearBackground(new Color(10, 12, 18, 255));
                        DrawText("Select Song", 60, 40, 48, Color.White);
                        DrawText("Mouse wheel scroll * Drag scrollbar * Click rows * Three-dots for options", 60, 100, 20, Color.Gray);

                        // Panel background
                        DrawRectangleRec(songsPanel, new Color(18, 20, 28, 255));

                        // Clip to songsPanel
                        BeginScissorMode((int)songsPanel.X, (int)songsPanel.Y, (int)songsPanel.Width, (int)songsPanel.Height);

                        // Draw rows (apply scrollY)
                        int startY = (int)innerTop;
                        for (int i = 0; i < songs.Count; i++)
                        {
                            float yf = startY + i * rowH - songListScrollY;
                            if (yf < songsPanel.Y - rowH || yf > songsPanel.Y + songsPanel.Height) continue; // skip completely offscreen

                            bool isSel = (i == selected);
                            string line = $"{songs[i].Title}";
                            int fontSize = isSel ? 30 : 24;
                            var col = isSel ? new Color(230, 240, 255, 255) : Color.Gray;

                            if (isSel)
                                DrawRectangle((int)songsPanel.X + 6, (int)yf - 6, (int)songsPanel.Width - 12, rowH, new Color(40, 50, 70, 180));

                            DrawText(line, (int)songsPanel.X + 14, (int)yf, fontSize, col);

                            // --- three-dots icon (clickable on mouse release), shifted left from scrollbar ---
                            const float dotsPadRight = 36f; // move away from scrollbar to avoid accidental clicks
                            float cx = songsPanel.X + songsPanel.Width - dotsPadRight;
                            float cy = yf + 10f;
                            Rectangle dotsRect = new Rectangle(cx - 10f, cy - 10f, 20f, 20f);
                            DrawCircle((int)(cx - 6f), (int)cy, 2, Color.White);
                            DrawCircle((int)cx, (int)cy, 2, Color.White);
                            DrawCircle((int)(cx + 6f), (int)cy, 2, Color.White);

                            // open context menu only when left mouse is released over the dots (and not dragging scrollbar)
                            if (!renamePopupOpen && !songDraggingScrollbar && CheckCollisionPointRec(mp, dotsRect) && IsMouseButtonReleased(MouseButton.Left))
                            {
                                contextOpenIndex = i;
                                contextMenuOpen = true;
                            }
                        }

                        EndScissorMode();

                        // Draw scrollbar (outside scissor so it remains visible)
                        DrawRectangleRec(scrollbarRect, new Color(120, 140, 180, 200));
                        if (songDraggingScrollbar) DrawRectangleRec(scrollbarRect, new Color(160, 180, 200, 230));

                        // Right-side buttons draw
                        UI.DrawButton(playBtn, "Play");
                        UI.DrawButton(editBtn, "Open in Editor");
                        UI.DrawButton(newBtn, "New Chart");
                        UI.DrawButton(modsBtn, "Mods");
                        UI.DrawButton(backBtn, "Back");

                        // --- Per-row context menu (if open) ---
                        if (contextMenuOpen && contextOpenIndex >= 0 && contextOpenIndex < songs.Count)
                        {
                            // compute menu anchor near the dots for that row (use same math as draw)
                            float menuRowY = startY + contextOpenIndex * rowH - songListScrollY;
                            const float dotsPadRight = 40f;
                            float cx = songsPanel.X + songsPanel.Width - dotsPadRight;
                            float cy = menuRowY + 10f;
                            // Anchor menu to the left of the dots, not mouse
                            Vector2 menuAnchor = new Vector2(cx, cy);

                            // Menu width/height
                            int menuW = 160, menuH = 106;
                            // Offset menu so its right edge is at the dots
                            Rectangle menu = new Rectangle(menuAnchor.X - menuW, menuRowY, menuW, menuH);
                            openMenuRect = menu;

                            DrawRectangleRec(menu, new Color(30, 36, 48, 240));
                            DrawRectangleLines((int)menu.X, (int)menu.Y, (int)menu.Width, (int)menu.Height, new Color(200, 210, 230, 200));

                            Rectangle r1 = new Rectangle(menu.X + 6, menu.Y + 6, menu.Width - 12, 30);
                            Rectangle r2 = new Rectangle(menu.X + 6, menu.Y + 40, menu.Width - 12, 30);
                            Rectangle r3 = new Rectangle(menu.X + 6, menu.Y + 74, menu.Width - 12, 30);

                            DrawText("Rename", (int)r1.X + 6, (int)r1.Y + 6, 20, Color.White);
                            DrawText("Clone", (int)r2.X + 6, (int)r2.Y + 6, 20, Color.White);
                            DrawText("Delete", (int)r3.X + 6, (int)r3.Y + 6, 20, Color.White);

                            // handle clicks on the menu (trigger on mouse release, not press)
                            if (!renamePopupOpen && IsMouseButtonReleased(MouseButton.Left))
                            {

                                if (CheckCollisionPointRec(mp, r1))
                                {
                                    // open rename popup
                                    renamePopupOpen = true;
                                    renameIndex = contextOpenIndex;
                                    try
                                    {
                                        var ch = ChartLoader.Load(songs[renameIndex].ChartPath);
                                        renameText = string.IsNullOrWhiteSpace(ch.Title)
                                            ? Path.GetFileNameWithoutExtension(songs[renameIndex].ChartPath)
                                            : ch.Title;
                                    }
                                    catch { renameText = songs[renameIndex].Title; }
                                    contextMenuOpen = false;
                                }
                                else if (CheckCollisionPointRec(mp, r2))
                                {
                                    try
                                    {
                                        string src = songs[contextOpenIndex].ChartPath;
                                        string dir = Path.GetDirectoryName(src)!;
                                        string name = Path.GetFileNameWithoutExtension(src);
                                        string dest = Path.Combine(dir, $"{name}_copy.chart.json");
                                        File.Copy(src, dest, overwrite: false);
                                        songs = SongEntry.ScanCharts(chartsRoot);
                                    }
                                    catch { }
                                    contextMenuOpen = false;
                                }
                                else if (CheckCollisionPointRec(mp, r3))
                                {
                                    try
                                    {
                                        File.Delete(songs[contextOpenIndex].ChartPath);
                                        songs = SongEntry.ScanCharts(chartsRoot);
                                        selected = Math.Max(0, Math.Min(selected, songs.Count - 1));
                                    }
                                    catch { }
                                    contextMenuOpen = false;
                                }
                                // Close menu if clicked outside
                                else if (!(mp.X >= menu.X && mp.X <= menu.X + menu.Width &&
                                           mp.Y >= menu.Y && mp.Y <= menu.Y + menu.Height))
                                {
                                    contextMenuOpen = false;
                                }
                            }
                        }
                        else
                        {
                            // close context menu by clicking outside the menu/dots (but only when not renaming)
                            if (!renamePopupOpen && contextMenuOpen && IsMouseButtonReleased(MouseButton.Left))
                            {

                                bool clickedOnAnyDots = false;
                                for (int i = 0; i < songs.Count; i++)
                                {
                                    float y = startY + i * rowH - songListScrollY;
                                    const float dotsPadRight = 36f;
                                    float cx2 = songsPanel.X + songsPanel.Width - dotsPadRight;
                                    float cy2 = y + 10f;
                                    Rectangle dotsRect2 = new Rectangle(cx2 - 10f, cy2 - 10f, 20f, 20f);
                                    if (CheckCollisionPointRec(mp, dotsRect2)) { clickedOnAnyDots = true; break; }
                                }
                                // also if clicked inside the openMenuRect we keep it
                                if (!clickedOnAnyDots)
                                {
                                    if (!(mp.X >= openMenuRect.X && mp.X <= openMenuRect.X + openMenuRect.Width &&
                                          mp.Y >= openMenuRect.Y && mp.Y <= openMenuRect.Y + openMenuRect.Height))
                                    {
                                        contextMenuOpen = false;
                                    }
                                }
                            }
                        }

                        // Draw rename popup on top if open (same as before)
                        if (renamePopupOpen && renameIndex >= 0 && renameIndex < songs.Count)
                        {
                            // Darken entire window
                            DrawRectangle(0, 0, GetScreenWidth(), GetScreenHeight(), new Color(0, 0, 0, 160));

                            int boxW = 560, boxH = 180;
                            int bx = GetScreenWidth() / 2 - boxW / 2;
                            int by = GetScreenHeight() / 2 - boxH / 2;

                            DrawRectangle(bx, by, boxW, boxH, new Color(24, 28, 38, 255));
                            DrawRectangleLines(bx, by, boxW, boxH, new Color(200, 210, 230, 255));
                            DrawText("Rename Chart", bx + 16, by + 12, 26, Color.White);

                            // Input box
                            DrawRectangle(bx + 20, by + 60, boxW - 40, 40, new Color(18, 20, 28, 255));
                            DrawText(string.IsNullOrEmpty(renameText) ? "" : renameText, bx + 28, by + 68, 24, Color.White);

                            // Type chars
                            for (int ch = GetCharPressed(); ch > 0; ch = GetCharPressed())
                            {
                                if (ch >= 32 && ch < 127 && renameText.Length < 60) renameText += (char)ch;
                            }
                            if (IsKeyPressed(KeyboardKey.Backspace) && renameText.Length > 0)
                                renameText = renameText.Substring(0, renameText.Length - 1);

                            bool ok = false, cancel = false;
                            Rectangle okBtn = new Rectangle(bx + boxW - 200, by + boxH - 52, 80, 32);
                            Rectangle cancelBtn = new Rectangle(bx + boxW - 110, by + boxH - 52, 80, 32);
                            UI.DrawButton(okBtn, "Save");
                            UI.DrawButton(cancelBtn, "Cancel");

                            Vector2 mpp = GetMousePosition();
                            if (CheckCollisionPointRec(mpp, okBtn) && IsMouseButtonReleased(MouseButton.Left)) ok = true;
                            if (CheckCollisionPointRec(mpp, cancelBtn) && IsMouseButtonReleased(MouseButton.Left)) cancel = true;
                            if (IsKeyPressed(KeyboardKey.Enter)) ok = true;
                            if (IsKeyPressed(KeyboardKey.Escape)) cancel = true;

                            if (ok && !string.IsNullOrWhiteSpace(renameText))
                            {
                                try
                                {
                                    var ch = ChartLoader.Load(songs[renameIndex].ChartPath);
                                    ch.Title = renameText.Trim();
                                    ChartLoader.Save(songs[renameIndex].ChartPath, ch);
                                    songs = SongEntry.ScanCharts(chartsRoot);
                                }
                                catch { /* ignore */ }
                                renamePopupOpen = false; renameIndex = -1;
                            }
                            if (cancel)
                            {
                                renamePopupOpen = false; renameIndex = -1;
                            }
                        }

                        EndDrawing();
                        break;
                    }

                // -------------------- GAME --------------------
                case AppMode.Game:
                    {
                        if (game!.ShouldQuit)
                        {
                            game.Dispose();
                            game = null;
                            mode = AppMode.SongSelect;
                            break;
                        }

                        if (game.State == GameState.Title)
                        {
                            Rectangle startBtn = new Rectangle(60, 300, 200, 46);
                            Rectangle backBtn = new Rectangle(60, 360, 200, 46);

                            if (UI.Button(startBtn, "Start")) game.Start();
                            if (UI.Button(backBtn, "Back"))
                            {
                                game.Dispose();
                                game = null;
                                mode = AppMode.SongSelect;
                                break;
                            }
                            if (IsKeyPressed(KeyboardKey.Enter)) game.Start();
                            if (IsKeyPressed(KeyboardKey.Backspace))
                            {
                                game.Dispose();
                                game = null;
                                mode = AppMode.SongSelect;
                                break;
                            }

                            BeginDrawing();
                            ClearBackground(new Color(18, 18, 22, 255));
                            game.DrawTitle();
                            UI.DrawButton(startBtn, "Start");
                            UI.DrawButton(backBtn, "Back");
                            EndDrawing();
                            break;
                        }

                        if (game.State == GameState.Failed)
                        {
                            Rectangle retryBtn = new Rectangle(60, 520, 260, 46);
                            Rectangle backBtn  = new Rectangle(60, 580, 260, 46);

                            if (UI.Button(retryBtn, "Retry")) game.Start();
                            if (UI.Button(backBtn, "Back to Song Select"))
                            {
                                game.Dispose();
                                game = null;
                                mode = AppMode.SongSelect;
                                break;
                            }
                            if (IsKeyPressed(KeyboardKey.R)) game.Start();
                            if (IsKeyPressed(KeyboardKey.Backspace))
                            {
                                game.Dispose();
                                game = null;
                                mode = AppMode.SongSelect;
                                break;
                            }

                            BeginDrawing();
                            ClearBackground(new Color(18, 18, 22, 255));
                            game.DrawFailed();
                            UI.DrawButton(retryBtn, "Retry (R)");
                            UI.DrawButton(backBtn, "Back to Song Select");
                            EndDrawing();
                            break;
                        }

                        if (game.State == GameState.Results)
                        {
                            Rectangle toTitleBtn = new Rectangle(60, 520, 260, 46);
                            Rectangle backBtn = new Rectangle(60, 580, 260, 46);

                            if (UI.Button(toTitleBtn, "Back to Title")) game.BackToTitle();
                            if (UI.Button(backBtn, "Back to Song Select"))
                            {
                                game.Dispose();
                                game = null;
                                mode = AppMode.SongSelect;
                                break;
                            }
                            if (IsKeyPressed(KeyboardKey.Enter)) game.BackToTitle();
                            if (IsKeyPressed(KeyboardKey.Backspace))
                            {
                                game.Dispose();
                                game = null;
                                mode = AppMode.SongSelect;
                                break;
                            }

                            BeginDrawing();
                            ClearBackground(new Color(18, 18, 22, 255));
                            game.DrawResults();
                            UI.DrawButton(toTitleBtn, "Back to Title");
                            UI.DrawButton(backBtn, "Back to Song Select");
                            EndDrawing();
                            break;
                        }

                        // Handle Restarting state - just continue to normal play drawing
                        // The restart happens in Update() and immediately transitions to Play state

                        // Normal play
                        game.Update();
                        BeginDrawing();
                        ClearBackground(new Color(18, 18, 22, 255));
                        game.DrawPlay();
                        EndDrawing();
                        break;
                    }

                // -------------------- EDITOR --------------------
                case AppMode.Editor:
                    {
                        // Backspace to exit when safe (no on-screen button)
                        if (IsKeyPressed(KeyboardKey.Backspace) && editor!.CanExitToSelect())
                        {
                            editor.Dispose();
                            editor = null;
                            mode = AppMode.SongSelect;
                            break;
                        }

                        editor!.Update();
                        BeginDrawing();
                        ClearBackground(new Color(12, 14, 20, 255));
                        editor.Draw();
                        EndDrawing();
                        break;
                    }

                // -------------------- MOD SELECT --------------------
                case AppMode.ModSelect:
                    {
                        int screenW = GetScreenWidth();
                        int screenH = GetScreenHeight();

                        // Panel layout
                        int panelW = Math.Min(800, screenW - 100);
                        int panelH = Math.Min(600, screenH - 100);
                        int panelX = (screenW - panelW) / 2;
                        int panelY = (screenH - panelH) / 2;

                        // Mod icon layout
                        int modIconSize = 120;
                        int modIconSpacing = 20;
                        int modsPerRow = 4;
                        int startX = panelX + 50;
                        int startY = panelY + 80;

                        // Back button
                        Rectangle backBtn = new Rectangle(panelX + panelW - 120, panelY + panelH - 60, 100, 40);
                        bool clickedBack = UI.Button(backBtn, "Back");

                        if (clickedBack || IsKeyPressed(KeyboardKey.Escape))
                        {
                            mode = AppMode.SongSelect;
                            break;
                        }

                        // Handle mod clicks
                        int modIndex = 0;
                        foreach (var kvp in Mod.AllMods)
                        {
                            var modType = kvp.Key;
                            var mod = kvp.Value;

                            int row = modIndex / modsPerRow;
                            int col = modIndex % modsPerRow;
                            int x = startX + col * (modIconSize + modIconSpacing);
                            int y = startY + row * (modIconSize + modIconSpacing);

                            Rectangle modRect = new Rectangle(x, y, modIconSize, modIconSize);
                            Vector2 mousePos = GetMousePosition();
                            bool hover = CheckCollisionPointRec(mousePos, modRect);
                            bool isActive = ModManager.IsModActive(modType);

                            if (hover && IsMouseButtonPressed(MouseButton.Left))
                            {
                                // Check if clicking on dots for Speed mod
                                if (modType == ModType.Speed)
                                {
                                    Rectangle dotsRect = new Rectangle(x + modIconSize - 30, y + 5, 25, 15);
                                    bool clickedDots = CheckCollisionPointRec(mousePos, dotsRect);

                                    if (clickedDots)
                                    {
                                        // Toggle speed submenu
                                        showSpeedSubmenu = !showSpeedSubmenu;
                                        submenuJustOpened = showSpeedSubmenu;
                                        if (showSpeedSubmenu)
                                        {
                                            speedSubmenuRect = new Rectangle(x + modIconSize + 10, y, 200, 100);
                                        }
                                    }
                                    else
                                    {
                                        // Normal mod toggle
                                        ModManager.ToggleMod(modType);
                                        showSpeedSubmenu = false; // Close submenu when toggling mod
                                    }
                                }
                                else
                                {
                                    ModManager.ToggleMod(modType);
                                }
                            }

                            modIndex++;
                        }

                        // Draw
                        BeginDrawing();
                        ClearBackground(new Color(10, 12, 18, 255));

                        // Title
                        DrawText("Mod Selection", 60, 60, 48, Color.White);
                        string activeModsText = ModManager.GetModsString();
                        if (!string.IsNullOrEmpty(activeModsText))
                        {
                            DrawText($"Active: {activeModsText}", 60, 120, 24, Color.Yellow);
                        }
                        else
                        {
                            DrawText("No mods selected", 60, 120, 24, Color.Gray);
                        }

                        // Panel background
                        DrawRectangle(panelX, panelY, panelW, panelH, new Color(20, 24, 32, 180));
                        DrawRectangleLines(panelX, panelY, panelW, panelH, new Color(60, 70, 90, 255));

                        // Draw mod icons
                        modIndex = 0;
                        foreach (var kvp in Mod.AllMods)
                        {
                            var modType = kvp.Key;
                            var mod = kvp.Value;

                            int row = modIndex / modsPerRow;
                            int col = modIndex % modsPerRow;
                            int x = startX + col * (modIconSize + modIconSpacing);
                            int y = startY + row * (modIconSize + modIconSpacing);

                            Rectangle modRect = new Rectangle(x, y, modIconSize, modIconSize);
                            Vector2 mousePos = GetMousePosition();
                            bool hover = CheckCollisionPointRec(mousePos, modRect);
                            bool isActive = ModManager.IsModActive(modType);

                            // Draw mod icon with specific colors for each mod
                            Color bgColor;
                            Color textColor;
                            if (isActive)
                            {
                                // Active state - use mod-specific colors
                                if (modType == ModType.Relax)
                                {
                                    bgColor = new Color(135, 206, 250, 255); // Light blue for Relax
                                    textColor = new Color(20, 20, 20, 255);
                                }
                                else if (modType == ModType.Speed)
                                {
                                    bgColor = new Color(147, 112, 219, 255); // Purple for Speed
                                    textColor = new Color(255, 255, 255, 255);
                                }
                                else
                                {
                                    bgColor = new Color(255, 200, 100, 255); // Golden for other mods (Autoplay)
                                    textColor = new Color(20, 20, 20, 255);
                                }
                            }
                            else if (hover)
                            {
                                bgColor = new Color(120, 120, 120, 255); // Light gray on hover
                                textColor = Color.White;
                            }
                            else
                            {
                                bgColor = new Color(80, 80, 80, 255); // Dark gray default
                                textColor = Color.White;
                            }

                            // Draw rounded rectangle (note-like shape)
                            DrawRectangleRounded(modRect, 0.3f, 8, bgColor);

                            // Draw outline - white for active mods, textColor for others
                            Color outlineColor = isActive ? Color.White : textColor;
                            DrawRectangleRoundedLinesEx(modRect, 0.3f, 8, 2, outlineColor);

                            // Draw mod text in center
                            int textWidth = MeasureText(mod.ShortName, 20);
                            int textX = (int)(modRect.X + modRect.Width / 2 - textWidth / 2);
                            int textY = (int)(modRect.Y + modRect.Height / 2 - 10);
                            DrawText(mod.ShortName, textX, textY, 20, textColor);

                            // Draw three dots for Speed mod (horizontal)
                            if (modType == ModType.Speed)
                            {
                                int dotsY = (int)(modRect.Y + 8);
                                int dotsStartX = (int)(modRect.X + modRect.Width - 25);
                                for (int dot = 0; dot < 3; dot++)
                                {
                                    DrawCircle(dotsStartX + dot * 6, dotsY, 2, Color.White);
                                }
                            }

                            // Draw mod name below icon
                            int nameWidth = MeasureText(mod.Name, 16);
                            int nameX = (int)(modRect.X + modRect.Width / 2 - nameWidth / 2);
                            int nameY = (int)(modRect.Y + modRect.Height + 5);
                            DrawText(mod.Name, nameX, nameY, 16, Color.Gray);

                            modIndex++;
                        }

                        // Back button
                        UI.DrawButton(backBtn, "Back");

                        // Handle speed submenu
                        if (showSpeedSubmenu)
                        {
                            // Check if clicked outside submenu to close it (but not on the same frame it was opened)
                            Vector2 mousePos = GetMousePosition();
                            if (IsMouseButtonPressed(MouseButton.Left) && !CheckCollisionPointRec(mousePos, speedSubmenuRect) && !submenuJustOpened)
                            {
                                showSpeedSubmenu = false;
                            }
                        }

                        // Reset the flag for next frame
                        if (submenuJustOpened)
                        {
                            submenuJustOpened = false;
                        }

                        // Draw speed submenu if visible
                        if (showSpeedSubmenu)
                        {
                            Vector2 mousePos = GetMousePosition();

                            // Draw submenu background
                            DrawRectangleRounded(speedSubmenuRect, 0.1f, 8, new Color(40, 44, 52, 240));
                            DrawRectangleRoundedLinesEx(speedSubmenuRect, 0.1f, 8, 2, Color.White);

                            // Speed slider
                            Rectangle sliderRect = new Rectangle(speedSubmenuRect.X + 10, speedSubmenuRect.Y + 30, speedSubmenuRect.Width - 20, 20);
                            float currentSpeed = ModManager.SpeedModValue;

                            // Simple slider implementation with gap around 1.0x
                            bool mouseOverSlider = CheckCollisionPointRec(mousePos, sliderRect);
                            if (mouseOverSlider && IsMouseButtonDown(MouseButton.Left))
                            {
                                float sliderPercent = (mousePos.X - sliderRect.X) / sliderRect.Width;

                                // Map slider to speed range, skipping 1.0x
                                // 0.0 - 0.5 maps to 0.5 - 0.95
                                // 0.5 - 1.0 maps to 1.1 - 1.5
                                float newSpeed;
                                if (sliderPercent <= 0.5f)
                                {
                                    // Lower half: 0.5 to 0.95
                                    newSpeed = 0.5f + (sliderPercent * 2.0f) * 0.45f;
                                }
                                else
                                {
                                    // Upper half: 1.1 to 1.5
                                    newSpeed = 1.1f + ((sliderPercent - 0.5f) * 2.0f) * 0.4f;
                                }
                                ModManager.SpeedModValue = newSpeed;
                            }

                            // Draw slider track
                            DrawRectangleRounded(sliderRect, 0.5f, 8, new Color(60, 60, 60, 255));

                            // Draw slider thumb (position based on current speed)
                            float thumbPercent;
                            if (currentSpeed <= 0.95f)
                            {
                                // Lower range: 0.5 to 0.95 maps to 0.0 to 0.5
                                thumbPercent = (currentSpeed - 0.5f) / 0.45f * 0.5f;
                            }
                            else
                            {
                                // Upper range: 1.1 to 1.5 maps to 0.5 to 1.0
                                thumbPercent = 0.5f + (currentSpeed - 1.1f) / 0.4f * 0.5f;
                            }
                            int thumbX = (int)(sliderRect.X + thumbPercent * sliderRect.Width);
                            DrawCircle(thumbX, (int)(sliderRect.Y + sliderRect.Height / 2), 8, Color.White);

                            // Draw speed value
                            string speedText = $"Speed: {ModManager.SpeedModValue:0.0#}x";
                            DrawText(speedText, (int)(speedSubmenuRect.X + 10), (int)(speedSubmenuRect.Y + 10), 16, Color.White);

                            // Draw range labels
                            DrawText("0.5x", (int)(sliderRect.X), (int)(sliderRect.Y + 25), 12, Color.Gray);
                            DrawText("1.5x", (int)(sliderRect.X + sliderRect.Width - 20), (int)(sliderRect.Y + 25), 12, Color.Gray);
                        }

                        EndDrawing();
                        break;
                    }
            }
        }

        game?.Dispose();
        editor?.Dispose();
        CloseAudioDevice();
        CloseWindow();
    }
}
