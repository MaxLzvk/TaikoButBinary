using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Numerics;
using Raylib_cs;
using static Raylib_cs.Raylib;

namespace TaikoButBinary;

public class Game : IDisposable
{
    private readonly Chart chart;
    public Music Music { get; private set; }
    private readonly Judge judge;

    public GameState State => state;
    public bool ShouldQuit { get; private set; } = false;
    private GameState state = GameState.Title;

    // Timing
    private float songTime = 0f;                 // seconds; driven by audio
    private float musicLength = 0f;              // total song length
    private bool musicEnded = false;             // audio has finished

    // Note processing
    private int currentNoteIndex = 0;            // current note being checked for hits
    private float inputOffsetSec = 0f;           // user's input offset

    // Timing windows
    public const float GoodWindow = 0.15f;       // ±0.15 seconds
    public const float PerfectWindow = 0.05f;    // ±0.05 seconds for perfect

    // UI and graphics
    private readonly float hitX = 200f;          // X position of the hit line
    private readonly List<ComboPopup> popups = new(); // Combo display popups

    // Accuracy tracking
    private struct AccuracyHit
    {
        public float TimingOffset;  // Negative = early, Positive = late
        public bool WasPerfect;
        public bool WasGood;
        public float Age;          // For fade effect
    }
    private readonly List<AccuracyHit> recentHits = new();
    private const int MaxRecentHits = 50;
    private const float AccuracyBarFadeTime = 2.0f; // Seconds before hit fades away

    // Pause system
    private bool paused = false;
    private float unpauseDelayTimer = 0f;

    // Restart hold mechanic
    private float restartHoldTimer = 0f;
    private bool restartRequested = false;

    // Health system (osu!-like)
    private float health = 1f;
    private const float HealthDrainRate   = 0.04f;  // HP lost per second passively
    private const float HealthGainPerfect = 0.05f;  // HP gained on perfect hit
    private const float HealthGainGood    = 0.02f;  // HP gained on good hit
    private const float HealthLossMiss    = 0.10f;  // HP lost on miss

    // Sound effects
    private Sound[] hitSounds;
    private Sound[] missSounds;
    private int hitSoundIndex = 0;
    private int missSoundIndex = 0;

    public Game(Chart chart, Music music, Judge judge, float scrollSpeed, float judgementOffsetMs)
    {
        this.chart = chart;
        this.judge = judge;
        this.Music = music;
        
        musicLength = GetMusicTimeLength(Music);

        // Load sound effects (multiple instances for overlapping)
        hitSounds = new Sound[4];
        missSounds = new Sound[4];
        
        string resourcesDir = Path.Join(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "resources", "sfx");
        
        for (int i = 0; i < 4; i++)
        {
            hitSounds[i] = LoadSound(Path.Join(resourcesDir, "hit_sound.mp3"));
            missSounds[i] = LoadSound(Path.Join(resourcesDir, "miss_sound.mp3"));
            
            // Set volume for sound effects (with master volume multiplier)
            SetSoundVolume(hitSounds[i], Program.Settings.MasterVolume * Program.Settings.SfxVolume);
            SetSoundVolume(missSounds[i], Program.Settings.MasterVolume * Program.Settings.SfxVolume);
        }
        
        // Set music volume (with master volume multiplier)
        SetMusicVolume(Music, Program.Settings.MasterVolume * Program.Settings.MusicVolume);
    }

    public void Start()
    {
        // Reset everything for a fresh start
        ResetJudge();
        ResetNotes(); // Reset all notes to unjudged state
        currentNoteIndex = 0;
        songTime = 0f;
        musicEnded = false;
        health = 1f;
        inputOffsetSec = Program.Settings.JudgementOffsetMs / 1000f;
        restartHoldTimer = 0f;
        restartRequested = false;
        paused = false;
        unpauseDelayTimer = 0f;
        popups.Clear(); // Clear any lingering combo popups

        StopMusicStream(Music);
        SeekMusicStream(Music, 0f);
        
        // Apply speed/pitch modifier for DT
        float speedMultiplier = GetSpeedMultiplier();
        SetMusicPitch(Music, speedMultiplier);
        
        PlayMusicStream(Music);
        state = GameState.Play;
    }

    public void BackToTitle()
    {
        StopMusicStream(Music);
        SeekMusicStream(Music, 0f);
        ResetNotes(); // Reset notes when going back to title
        songTime = 0f;
        musicEnded = false;
        restartHoldTimer = 0f;
        restartRequested = false;
        paused = false;
        unpauseDelayTimer = 0f;
        ShouldQuit = true;
    }

    /// <summary>
    /// Triggers a restart by simulating the hold-to-restart mechanism
    /// </summary>
    private void TriggerRestart()
    {
        // Set the restart timer to trigger an immediate restart on the next frame
        restartHoldTimer = Program.Settings.RestartHoldDelaySec;
    }

    private void ImmediateRestart()
    {
        // Ensure we're not in any weird state
        paused = false;
        unpauseDelayTimer = 0f;
        restartHoldTimer = 0f;
        
        // Completely stop and reset music
        StopMusicStream(Music);
        
        // Reset all game state
        ResetJudge();
        ResetNotes();
        currentNoteIndex = 0;
        songTime = 0f;
        musicEnded = false;
        health = 1f;
        inputOffsetSec = Program.Settings.JudgementOffsetMs / 1000f;
        popups.Clear();
        recentHits.Clear(); // Clear accuracy tracking
        
        // Reset music position and apply speed modifier
        SeekMusicStream(Music, 0f);
        float speedMultiplier = GetSpeedMultiplier();
        SetMusicPitch(Music, speedMultiplier);
        
        // Start playing and set state
        PlayMusicStream(Music);
        state = GameState.Play;
    }

    private void RecordHitTiming(float timingOffset, bool wasPerfect, bool wasGood)
    {
        recentHits.Add(new AccuracyHit
        {
            TimingOffset = timingOffset,
            WasPerfect = wasPerfect,
            WasGood = wasGood,
            Age = 0f
        });

        // Remove old hits
        if (recentHits.Count > MaxRecentHits)
        {
            recentHits.RemoveAt(0);
        }
    }

    public void Update()
    {
        switch (state)
        {
            case GameState.Title:
                break;

            case GameState.Play:
                {
                    // Pause toggle
                    if (!paused && Program.Settings.PauseToggle.IsPressed())
                    {
                        paused = true;
                        if (!musicEnded) PauseMusicStream(Music);
                    }
                    else if (paused)
                    {
                        // Click/keys on pause menu handled in DrawPlay() overlay.
                        // If countdown not running and pause key pressed, start resume countdown
                        if (unpauseDelayTimer <= 0f && Program.Settings.PauseToggle.IsPressed())
                        {
                            unpauseDelayTimer = MathF.Max(0.05f, Program.Settings.UnpauseDelaySec);
                        }

                        // Handle resume countdown
                        if (unpauseDelayTimer > 0f)
                        {
                            unpauseDelayTimer -= GetFrameTime();
                            if (unpauseDelayTimer <= 0f)
                            {
                                paused = false;
                                if (!musicEnded) ResumeMusicStream(Music);
                            }
                        }

                        // Handle restart request from pause menu
                        if (restartRequested)
                        {
                            restartRequested = false;
                            state = GameState.Restarting; // Transition to restart state
                            return;
                        }

                        // While paused or unpausing, do not progress gameplay
                        break;
                    }

                    // Handle restart request from pause menu (can happen while paused)
                    if (restartRequested)
                    {
                        restartRequested = false;
                        state = GameState.Restarting; // Transition to restart state
                        return;
                    }

                    // Handle the in-game hold-to-restart mechanic (only when not paused)
                    if (Program.Settings.RestartHold.IsDown())
                    {
                        restartHoldTimer += GetFrameTime();
                        if (restartHoldTimer >= Program.Settings.RestartHoldDelaySec)
                        {
                            Start(); // Full restart
                            return;
                        }
                    }
                    else
                    {
                        restartHoldTimer = 0f;
                    }

                    // Advance time; if audio finished, keep advancing so remaining notes can be judged/missed.
                    UpdateMusicStream(Music);
                    float played = GetMusicTimePlayed(Music);

                    if (!musicEnded)
                    {
                        // Apply judgment offset to the music timing instead of hit detection
                        songTime = played + inputOffsetSec;
                        // Detect end (guard small epsilon)
                        if (musicLength > 0f && songTime >= musicLength - 0.005f)
                        {
                            musicEnded = true;
                            songTime = musicLength;
                            StopMusicStream(Music);
                        }
                    }
                    else
                    {
                        // After audio ends, advance using frame time so trailing notes can resolve as misses
                        songTime += GetFrameTime();
                    }

                    // Handle input - autoplay, relax, or normal user input
                    bool isAutoplay = ModManager.IsModActive(ModType.Autoplay);
                    bool isRelax = ModManager.IsModActive(ModType.Relax);
                    int input = -1;
                    bool anyInput = false;
                    
                    if (!isAutoplay)
                    {
                        // User input detection
                        if (Program.Settings.HitOne.IsPressed())
                        {
                            input = 1;
                            anyInput = true;
                        }
                        else if (Program.Settings.HitZero.IsPressed())
                        {
                            input = 0;
                            anyInput = true;
                        }
                        else if (Program.Settings.HitSecial.IsPressed())
                        {
                            input = 2;
                            anyInput = true;
                        }
                    }

                    for (int i = currentNoteIndex; i < chart.Notes.Count; i++)
                    {
                        var n = chart.Notes[i];
                        if (n.Judged) { currentNoteIndex = i + 1; continue; }

                        float ndt = n.Time - songTime;

                        if (ndt < -GoodWindow)
                        {
                            n.Judged = true; n.Hit = false;
                            judge.Miss++; judge.Combo = 0;
                            health = Math.Clamp(health - HealthLossMiss, 0f, 1f);
                            
                            // Record miss timing for accuracy bar (show as red)
                            RecordHitTiming(ndt, false, false);
                            
                            // Show red X for missed note
                            popups.Add(new ComboPopup { Text = "X", Pos = new Vector2(hitX - 10, GetScreenHeight() / 2f - 150), Col = Color.Red });
                            
                            // Play miss sound (rotating instances for overlapping)
                            PlaySound(missSounds[missSoundIndex]);
                            missSoundIndex = (missSoundIndex + 1) % missSounds.Length;
                            
                            currentNoteIndex = i + 1;
                            continue;
                        }

                        // Autoplay: automatically hit notes when they reach the hit line (ndt ≈ 0)
                        if (isAutoplay && ndt <= 0.01f && ndt >= -0.01f)
                        {
                            n.Judged = true; n.Hit = true;
                            judge.Perfect++; judge.Score += 1000;
                            judge.Combo++; judge.MaxCombo = Math.Max(judge.MaxCombo, judge.Combo);
                            health = Math.Clamp(health + HealthGainPerfect, 0f, 1f);
                            popups.Add(new ComboPopup { Text = $"{judge.Combo}", Pos = new Vector2(hitX - 10, GetScreenHeight() / 2f - 150), Col = Color.White });
                            
                            // Record perfect autoplay timing
                            RecordHitTiming(ndt, true, false);
                            
                            // Play hit sound (rotating instances for overlapping)
                            PlaySound(hitSounds[hitSoundIndex]);
                            hitSoundIndex = (hitSoundIndex + 1) % hitSounds.Length;
                            
                            currentNoteIndex = i + 1;
                            // Continue to next note in autoplay to handle rapid notes
                            continue;
                        }
                        // Manual input handling
                        else if (anyInput)
                        {
                            if (MathF.Abs(ndt) <= GoodWindow)
                            {
                                // In Relax mode, any input hits any note type
                                // In Normal mode, input type must match note type
                                if (isRelax || input == n.Type)
                                {
                                    n.Judged = true; n.Hit = true;
                                    bool isPerfect = MathF.Abs(ndt) <= PerfectWindow;
                                    if (isPerfect) { judge.Perfect++; judge.Score += 1000; health = Math.Clamp(health + HealthGainPerfect, 0f, 1f); }
                                    else { judge.Good++; judge.Score += 500; health = Math.Clamp(health + HealthGainGood, 0f, 1f); }
                                    judge.Combo++; judge.MaxCombo = Math.Max(judge.MaxCombo, judge.Combo);
                                    popups.Add(new ComboPopup { Text = $"{judge.Combo}", Pos = new Vector2(hitX - 10, GetScreenHeight() / 2f - 150), Col = Color.White });
                                    
                                    // Record hit timing
                                    RecordHitTiming(ndt, isPerfect, !isPerfect);
                                    
                                    // Play hit sound (rotating instances for overlapping)
                                    PlaySound(hitSounds[hitSoundIndex]);
                                    hitSoundIndex = (hitSoundIndex + 1) % hitSounds.Length;
                                    
                                    currentNoteIndex = i + 1;
                                }
                                else
                                {
                                    // Normal mode: wrong input type
                                    n.Judged = true; n.Hit = false;
                                    judge.Miss++; judge.Combo = 0;
                                    health = Math.Clamp(health - HealthLossMiss, 0f, 1f);
                                    
                                    // Record miss timing for accuracy bar (show as red)
                                    RecordHitTiming(ndt, false, false);
                                    
                                    // Show red X in combo popup to indicate wrong button
                                    popups.Add(new ComboPopup { Text = "X", Pos = new Vector2(hitX - 10, GetScreenHeight() / 2f - 150), Col = Color.Red });
                                    
                                    // Play miss sound (rotating instances for overlapping)
                                    PlaySound(missSounds[missSoundIndex]);
                                    missSoundIndex = (missSoundIndex + 1) % missSounds.Length;
                                    
                                    currentNoteIndex = i + 1;
                                }
                            }
                            break; // Break only for manual input
                        }
                        else
                        {
                            // No input and not autoplay timing - stop processing
                            break;
                        }
                    }

                    if (currentNoteIndex >= chart.Notes.Count)
                    {
                        float last = chart.Notes.Count > 0 ? chart.Notes[^1].Time : 0f;
                        if (songTime > last + 2.0f) state = GameState.Results;
                    }

                    // Passive health drain (only when not in autoplay)
                    if (!isAutoplay && !paused)
                    {
                        health -= HealthDrainRate * GetFrameTime();
                        if (health <= 0f)
                        {
                            health = 0f;
                            StopMusicStream(Music);
                            state = GameState.Failed;
                            return;
                        }
                    }

                    float dtPop = GetFrameTime();
                    for (int p = popups.Count - 1; p >= 0; p--)
                    {
                        popups[p].Update(dtPop);
                        if (popups[p].Dead) popups.RemoveAt(p);
                    }

                    // Update accuracy hit ages and remove faded ones
                    for (int h = recentHits.Count - 1; h >= 0; h--)
                    {
                        var hit = recentHits[h];
                        hit.Age += dtPop;
                        recentHits[h] = hit;
                        
                        if (hit.Age > AccuracyBarFadeTime)
                        {
                            recentHits.RemoveAt(h);
                        }
                    }
                    break;
                }

            case GameState.Restarting:
                {
                    // Clean restart transition - perform full restart
                    Start();
                    // Start() already sets state to Play, so we'll just break and let the next frame handle Play state
                    break;
                }

            case GameState.Failed:
                break;

            case GameState.Results:
                break;
        }
    }

    public void DrawTitle()
    {
        DrawText("Binary Taiko", 60, 60, 60, Color.White);
        DrawText("Keys: configurable in Settings", 60, 140, 24, Color.Gray);
        DrawText("Click START or press ENTER", 60, 180, 24, Color.Gray);
        DrawText("BACKSPACE: return to Song Select", 60, 210, 24, Color.Gray);
    }

    public void DrawPlay()
    {
        int screenWidth = GetScreenWidth();
        int screenHeight = GetScreenHeight();

        // --- Health bar (very top of screen, osu!-style) ---
        DrawHealthBar();

        // --- Real-time accuracy percent display (top left) ---
        float acc = judge.AccuracyPercent;
        string accText = $"Acc: {acc:0.00}%";
        DrawText(accText, 24, 18, 32, Color.White);

        // --- Mod indicators (top right) ---
        int currentX = screenWidth - 24;
        
        if (ModManager.IsModActive(ModType.Speed))
        {
            string speedText = $"{ModManager.SpeedModValue:0.0#}x";
            int speedTextWidth = MeasureText(speedText, 32);
            currentX -= speedTextWidth;
            DrawText(speedText, currentX, 18, 32, new Color(147, 112, 219, 255)); // Purple
            currentX -= 10; // Spacing between mods
        }
        
        if (ModManager.IsModActive(ModType.Relax))
        {
            string relaxText = "RELAX";
            int relaxTextWidth = MeasureText(relaxText, 32);
            currentX -= relaxTextWidth;
            DrawText(relaxText, currentX, 18, 32, new Color(135, 206, 250, 255)); // Light blue
            currentX -= 10;
        }
        
        if (ModManager.IsModActive(ModType.Autoplay))
        {
            string autoText = "AUTO";
            int autoTextWidth = MeasureText(autoText, 32);
            currentX -= autoTextWidth;
            DrawText(autoText, currentX, 18, 32, Color.Gold);
        }

        DrawRectangle(0, screenHeight / 2 - 100, screenWidth, 200, new Color(30, 30, 38, 255));
        DrawLineV(new Vector2(hitX, screenHeight / 2 - 110), new Vector2(hitX, screenHeight / 2 + 110), new Color(220, 220, 220, 255));
        DrawText("HIT", (int)(hitX - 20), screenHeight / 2 - 140, 20, Color.White);

        for (int i = 0; i < chart.Notes.Count; i++)
        {
            var n = chart.Notes[i];
            if (n.Judged) continue; // Note disappears when judged, regardless of hit/miss

            float dt = n.Time - songTime;
            float x = hitX + dt * Program.Settings.ScrollSpeed;
            float y = screenHeight / 2f;
            if (x < -100 || x > screenWidth + 200) continue;

            Color c = n.Type switch
            {
                1 => new Color(120, 200, 255, 255),
                0 => new Color(255, 140, 120, 255),
                2 => new Color(255, 230, 120, 255),
                _ => new Color(200, 200, 200, 255)
            };
            string label = n.Type == 2 ? "⎵" : (n.Type == 1 ? "1" : "0");

            DrawRectangleRounded(new Rectangle(x - 30, y - 30, 60, 60), 0.3f, 8, c);
            DrawText(label, (int)(x - (n.Type == 2 ? 12 : 10)), (int)(y - 18), 36, new Color(20, 20, 24, 255));
        }

        DrawText($"Time: {songTime:F2}s", 980, 20, 20, Color.Gray);
        DrawText($"Score: {judge.Score}", 980, 50, 24, Color.White);
        DrawText($"Combo: {judge.Combo}", 980, 80, 24, Color.White);

        // Draw combo popups
        foreach (var popup in popups)
            popup.Draw();

        // Draw accuracy bar
        DrawAccuracyBar();

        // Restart hold indicator
        if (restartHoldTimer > 0f)
        {
            float progress = restartHoldTimer / Program.Settings.RestartHoldDelaySec;
            int barWidth = 200;
            int barHeight = 10;
            int barX = screenWidth / 2 - barWidth / 2;
            int barY = 50;
            DrawRectangle(barX, barY, barWidth, barHeight, Color.DarkGray);
            DrawRectangle(barX, barY, (int)(barWidth * progress), barHeight, Color.Orange);
            DrawText("Hold to Restart", barX, barY - 25, 20, Color.White);
        }

        // Pause overlay
        if (paused)
        {
            DrawRectangle(0, 0, screenWidth, screenHeight, new Color(0, 0, 0, 180));
            
            string pauseText = unpauseDelayTimer > 0f ? $"Resuming in {unpauseDelayTimer:0.0}..." : "PAUSED";
            int textWidth = MeasureText(pauseText, 60);
            DrawText(pauseText, screenWidth / 2 - textWidth / 2, screenHeight / 2 - 120, 60, Color.White);

            if (unpauseDelayTimer <= 0f)
            {
                // Pause menu buttons
                Rectangle resumeBtn = new Rectangle(screenWidth / 2f - 100, screenHeight / 2f - 30, 200, 50);
                Rectangle restartBtn = new Rectangle(screenWidth / 2f - 100, screenHeight / 2f + 40, 200, 50);
                Rectangle quitBtn = new Rectangle(screenWidth / 2f - 100, screenHeight / 2f + 110, 200, 50);

                if (DrawPauseButton(resumeBtn, "Resume (ESC)"))
                {
                    unpauseDelayTimer = MathF.Max(0.05f, Program.Settings.UnpauseDelaySec);
                }
                if (DrawPauseButton(restartBtn, "Restart (R)") || IsKeyPressed(KeyboardKey.R))
                {
                    restartRequested = true; // Request restart to be processed in Update()
                }
                if (DrawPauseButton(quitBtn, "Quit (Q)") || IsKeyPressed(KeyboardKey.Q))
                {
                    BackToTitle();
                }
            }
        }
    }

    private void DrawHealthBar()
    {
        int screenWidth = GetScreenWidth();
        const float barHeight = 14f;

        // Background
        DrawRectangle(0, 0, screenWidth, (int)barHeight, new Color(20, 20, 20, 200));

        // Determine bar color based on health level
        Color barColor;
        if (health > 0.5f)
        {
            // Green → yellow as health drops from 100% to 50%
            float t = (health - 0.5f) / 0.5f;
            barColor = new Color((byte)(255 * (1f - t * 0.6f)), (byte)220, (byte)50, (byte)255);
        }
        else if (health > 0.25f)
        {
            // Yellow → orange as health drops from 50% to 25%
            float t = (health - 0.25f) / 0.25f;
            barColor = new Color((byte)255, (byte)(int)(200 * t), (byte)20, (byte)255);
        }
        else
        {
            // Pulsing red when critically low
            float pulse = (MathF.Sin((float)GetTime() * 8f) + 1f) / 2f;
            barColor = new Color((byte)255, (byte)(int)(30 * pulse), (byte)10, (byte)255);
        }

        // Fill
        int fillWidth = (int)(screenWidth * health);
        if (fillWidth > 0)
        {
            DrawRectangle(0, 0, fillWidth, (int)barHeight, barColor);
            // Shine highlight on top
            DrawRectangle(0, 0, fillWidth, 3, new Color(255, 255, 255, 55));
        }
    }

    public void DrawFailed()
    {
        int screenWidth = GetScreenWidth();
        int screenHeight = GetScreenHeight();

        // Draw the play field frozen in the background (just the lane)
        DrawRectangle(0, screenHeight / 2 - 100, screenWidth, 200, new Color(30, 30, 38, 255));
        DrawLineV(new Vector2(hitX, screenHeight / 2 - 110), new Vector2(hitX, screenHeight / 2 + 110), new Color(220, 220, 220, 255));

        // Dark red overlay
        DrawRectangle(0, 0, screenWidth, screenHeight, new Color(80, 0, 0, 140));

        // "FAILED" banner
        const int failFontSize = 110;
        int failW = MeasureText("FAILED", failFontSize);
        DrawText("FAILED", screenWidth / 2 - failW / 2, 55, failFontSize, Color.Red);

        // Stats — fixed Y positions so buttons at y=520/580 never overlap
        float acc = judge.AccuracyPercent;
        DrawText($"Score: {judge.Score}", 60, 210, 32, Color.White);
        DrawText($"Max Combo: {judge.MaxCombo}x", 60, 258, 32, Color.White);
        DrawText($"Accuracy: {acc:0.00}%", 60, 306, 32, Color.White);
        DrawText($"Perfect: {judge.Perfect}   Good: {judge.Good}   Miss: {judge.Miss}", 60, 354, 26, Color.LightGray);
    }

    private void DrawAccuracyBar()
    {
        if (!Program.Settings.ShowAccuracyBar) return;

        int screenWidth = GetScreenWidth();
        int screenHeight = GetScreenHeight();
        
        // Accuracy bar dimensions and position (bottom of screen)
        float barWidth = 400f;
        float barHeight = 8f;
        float barX = screenWidth / 2f - barWidth / 2f;
        float barY = screenHeight - 80f;
        
        // Draw background bar
        DrawRectangleRounded(new Rectangle(barX, barY, barWidth, barHeight), 0.5f, 4, new Color(40, 40, 40, 180));
        
        // Draw center line (perfect timing)
        float centerX = barX + barWidth / 2f;
        DrawLineEx(new Vector2(centerX, barY - 5), new Vector2(centerX, barY + barHeight + 5), 2f, new Color(255, 255, 255, 150));
        
        // Draw timing window indicators
        float maxOffset = 0.15f; // Same as GoodWindow
        float perfectWindowPixels = (PerfectWindow / maxOffset) * (barWidth / 2f);
        
        // Perfect timing zone (green)
        DrawRectangleRounded(new Rectangle(centerX - perfectWindowPixels, barY, perfectWindowPixels * 2, barHeight), 0.3f, 4, new Color(0, 255, 0, 60));
        
        // Draw recent hits
        foreach (var hit in recentHits)
        {
            // Calculate fade alpha based on age
            float fadeAlpha = 1f - (hit.Age / AccuracyBarFadeTime);
            if (fadeAlpha <= 0f) continue;
            
            // Calculate position on bar
            float normalizedOffset = MathF.Max(-1f, MathF.Min(1f, hit.TimingOffset / maxOffset)); // Clamp to [-1, 1]
            float hitBarX = centerX + normalizedOffset * (barWidth / 2f);
            
            // Choose color based on hit quality
            byte alpha = (byte)(255 * fadeAlpha);
            Color hitColor = hit.WasPerfect ? new Color((byte)0, (byte)255, (byte)0, alpha) :  // Green for perfect
                           hit.WasGood ? new Color((byte)255, (byte)255, (byte)0, alpha) :      // Yellow for good
                           new Color((byte)255, (byte)0, (byte)0, alpha);                      // Red for miss
            
            // Draw hit marker as vertical line
            float lineHeight = barHeight + 8f; // Make lines slightly taller than the bar
            DrawLineEx(new Vector2(hitBarX, barY - 4f), new Vector2(hitBarX, barY + lineHeight), 2f, hitColor);

        }
        
        // Draw labels
        DrawText("Late", (int)(barX - 50), (int)(barY - 2), 16, new Color(200, 200, 200, 180));
        DrawText("Early", (int)(barX + barWidth + 10), (int)(barY - 2), 16, new Color(200, 200, 200, 180));
        DrawText("Accuracy", (int)(barX), (int)(barY - 25), 20, Color.White);
    }

    private bool DrawPauseButton(Rectangle rect, string label)
    {
        bool hovered = CheckCollisionPointRec(GetMousePosition(), rect);
        bool clicked = hovered && IsMouseButtonPressed(MouseButton.Left);
        
        Color bgColor = hovered ? new Color(70, 70, 80, 255) : new Color(50, 50, 60, 255);
        DrawRectangleRounded(rect, 0.1f, 8, bgColor);
        DrawRectangleRoundedLinesEx(rect, 0.1f, 8, 2, Color.White);
        
        int textWidth = MeasureText(label, 20);
        DrawText(label, (int)(rect.X + rect.Width / 2 - textWidth / 2), (int)(rect.Y + rect.Height / 2 - 10), 20, Color.White);
        
        return clicked;
    }

    public void DrawResults()
    {
        int screenWidth = GetScreenWidth();
        int screenHeight = GetScreenHeight();

        DrawText("Results", 60, 60, 60, Color.White);

        // Map title
        string mapTitle = chart.Title ?? "Unknown Map";
        DrawText($"Map: {mapTitle}", 60, 120, 28, Color.LightGray);

        float acc = judge.AccuracyPercent;
        string grade = judge.Grade;

        DrawText($"Score: {judge.Score}", 60, 170, 36, Color.White);
        DrawText($"Max Combo: {judge.MaxCombo}", 60, 220, 36, Color.White);
        DrawText($"Perfect: {judge.Perfect}", 60, 270, 36, Color.White);
        DrawText($"Good: {judge.Good}", 60, 320, 36, Color.White);
        DrawText($"Miss: {judge.Miss}", 60, 370, 36, Color.White);
        DrawText($"Accuracy: {acc:0.00}%", 60, 420, 36, Color.White);

        // Full Combo check
        bool fullCombo = judge.Miss == 0 && chart.Notes.Count > 0;
        if (fullCombo)
        {
            DrawText("FULL COMBO!", 60, 470, 40, Color.Gold);
        }

        // Grade letter, big and on the right
        int gradeFontSize = 240;
        int gradeX = screenWidth - 300;
        int gradeY = 180;
        Color gradeColor = Color.White;
        if (Math.Abs(acc - 100f) < 0.0001f)
            gradeColor = Color.RayWhite;
        else if (acc >= 95f)
            gradeColor = Color.Gold;
        else if (acc >= 90f)
            gradeColor = Color.Green;
        else if (acc >= 85f)
            gradeColor = Color.Blue;
        else if (acc >= 70f)
            gradeColor = Color.Purple;
        else if (acc < 70f)
            gradeColor = Color.Red;

        // Only the first character of the grade (the letter)
        string gradeLetter = !string.IsNullOrEmpty(grade) ? grade.Substring(0, 1) : "?";
        int gradeTextWidth = MeasureText(gradeLetter, gradeFontSize);
        DrawText(gradeLetter, gradeX - gradeTextWidth / 2, gradeY, gradeFontSize, gradeColor);

        // Display active mods underneath the grade
        string modsText = ModManager.GetModsString();
        if (!string.IsNullOrEmpty(modsText))
        {
            int modsFontSize = 32;
            int modsTextWidth = MeasureText(modsText, modsFontSize);
            DrawText(modsText, gradeX - modsTextWidth / 2, gradeY + gradeFontSize + 20, modsFontSize, Color.LightGray);
        }
    }

    private void ResetJudge()
    {
        judge.Score = 0;
        judge.Combo = 0;
        judge.MaxCombo = 0;
        judge.Perfect = 0;
        judge.Good = 0;
        judge.Miss = 0;
        recentHits.Clear(); // Clear accuracy tracking
    }

    private void ResetNotes()
    {
        // Reset all notes to unjudged state for replay
        foreach (var note in chart.Notes)
        {
            note.Judged = false;
            note.Hit = false;
        }
    }

    public void UpdateSoundVolumes(float sfxVolume, float musicVolume)
    {
        // Update SFX volumes
        for (int i = 0; i < hitSounds.Length; i++)
        {
            SetSoundVolume(hitSounds[i], sfxVolume);
            SetSoundVolume(missSounds[i], sfxVolume);
        }
        
        // Update music volume
        SetMusicVolume(Music, musicVolume);
    }

    private float GetSpeedMultiplier()
    {
        return ModManager.IsModActive(ModType.Speed) ? ModManager.SpeedModValue : 1.0f;
    }

    public void Dispose()
    {
        UnloadMusicStream(Music);
        
        for (int i = 0; i < hitSounds.Length; i++)
        {
            UnloadSound(hitSounds[i]);
            UnloadSound(missSounds[i]);
        }
    }
}
