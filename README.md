# Voxlet

Voxlet is a local-first Windows dictation app by CeskynoobProductions. This repository contains the WPF application source and Inno Setup installer script. It is licensed under GPL-3.0-or-later; see [LICENSE](LICENSE).

## Build the Windows app

Requirements: Windows, the .NET 10 SDK, and the Whisper runtime files described below. Restore and publish with:

```powershell
dotnet restore Voxlet.csproj
dotnet publish Voxlet.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
```

The speech engine and model are not stored in Git because they are large third-party binaries. To build a runnable package, place the English model at `Resources/Whisper/ggml-small.en.bin`, the CPU Whisper executable and its required DLLs in `Resources/Whisper/cli/Release/`, and optional GPU runtime files in `Resources/Whisper/cuda/`. See the item paths in `Voxlet.csproj` and the upstream whisper.cpp project for those assets. The app can download optional language models from its settings.

## Build the installer

After publishing the app to `publish/win-x64`, open `setup/Voxlet.iss` with Inno Setup Compiler and build it. The installer output is written to `dist/`.

## Contributing and attribution

Keep the GPL license and attribution notices with redistributions. Voxlet and its logo identify the original project by CeskynoobProductions. If a modified version keeps the Voxlet name or logo, clearly label it as an unofficial modification and link to this original repository.
