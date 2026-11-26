# Aether Desktop

Standalone Windows app with Venice's web agent and a local Aether Bench.

## Run

Open `AetherStandalone.exe`. Choose **Venice AI agent** to open Venice.ai in its own app window and sign in there. Aether does not call Venice's API or ask for an API key.

Choose **Aether Bench** to scan Windows USB devices and any locally installed ADB/fastboot executables. Hash a file to append its SHA-256 digest to `%LOCALAPPDATA%\Aether\bench\HASHES.csv`.

Bench operations are read-only: device scanning lists devices only, and hashing reads the selected file. No firmware is downloaded and no device data is written or erased.

## Requirements

- Windows
- Microsoft Edge WebView2 Runtime

## Rebuild

Run `powershell -ExecutionPolicy Bypass -File .\build-windows.ps1` from this directory. The script embeds the Venice/Bench UI, WebView2 libraries, and Windows device-inventory libraries into `src-tauri\build-output\AetherStandalone.exe`.
