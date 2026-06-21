# TaikoButBinary 🥁

[![C#](https://img.shields.io/badge/language-C%23-239120.svg)](https://learn.microsoft.com/en-us/dotnet/csharp/)
[![raylib](https://img.shields.io/badge/engine-raylib-FFC0CB.svg)](https://www.raylib.com/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Status](https://img.shields.io/badge/status-in%20development-yellow.svg)](#)

A Taiko-no-Tatsujin-inspired rhythm game with a binary twist: every note you hit is a bit. Hit notes in the right order at the right time to "type out" the song in binary as you play.

## Concept

Instead of the usual red/blue don/katsu drum hits, **TaikoButBinary** maps rhythm-game inputs directly onto binary data:

| Note color | Value | Meaning |
|---|---|---|
| 🔵 Blue | `1` | Binary one |
| 🔴 Red | `0` | Binary zero |
| 🟡 Yellow | `␣` | Space / separator note |

As notes scroll in and you hit them on beat, you're effectively building a binary string in real time — turning the classic drum-hit rhythm gameplay into a stream of bits.

## Built With

- **C#**
- **[raylib](https://www.raylib.com/)** (via [raylib-cs](https://github.com/ChrisDill/Raylib-cs)) — for rendering, input, and audio

## Getting Started

### Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) (6.0 or later recommended)
- A C# IDE such as Visual Studio, Visual Studio Code, or JetBrains Rider (optional, the project also builds from the CLI)

### Installation

1. Clone the repository
   ```bash
   git clone https://github.com/MaxLzvk/TaikoButBinary.git
   cd TaikoButBinary
   ```
2. Restore dependencies
   ```bash
   dotnet restore
   ```
3. Build the project
   ```bash
   dotnet build
   ```

### Running the Game

```bash
dotnet run --project TaikoButBinary
```

Or open `TaikoButBinary.sln` in Visual Studio / Rider and run from there.

## Controls

| Key | Action |
|---|---|
| `D` / `F` | Hit red note (0) |
| `J` / `K` | Hit blue note (1) |
| `Space` | Hit yellow note (space) |
| `Esc` | Pause / quit |

*(Update this table to match your actual key bindings.)*

## Roadmap

- [ ] Song/chart loading system
- [ ] Score & combo tracking
- [ ] Difficulty levels
- [ ] Custom chart editor
- [ ] Sound effects & hit feedback

## Contributing

This is currently a solo/school project, but suggestions and issues are welcome — feel free to open an issue or PR.

## License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.

## Author

Made by [MaxLzvk](https://github.com/MaxLzvk)
