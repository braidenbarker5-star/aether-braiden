# Aether Desktop

Standalone Windows desktop workspace with Venice’s web agent, Aether Bench, and App Builder.

## App Builder

1. Choose **App Builder → Attach project folder**.
2. Select a source file to inspect or edit. Saving creates a timestamped backup in `.aether-backup` before replacing the file.
3. Choose **Copy selected file and task for Venice**, describe the task, open Venice, and paste the supplied context. Venice’s web agent cannot directly access the local project; review its proposed edits before saving them in Aether.
4. For Android projects, select **Build Android debug APK**. Aether invokes the attached project’s Gradle wrapper with the fixed `assembleDebug` task and displays its output.

Project scanning skips dependency/build folders and indexes supported source/configuration files. Files larger than the editor size limit cannot be opened for editing. Project contents are not uploaded automatically.

## Aether Bench

USB inventory, standard `adb devices -l` / `fastboot devices` lists, validated wireless `adb connect`, and a local SHA-256 ledger. These device operations do not flash, unlock, bypass protections, or write partitions.

## Venice

**Venice AI agent** opens Venice’s own web app. Sign in on Venice.ai and choose its available model there. Aether does not request a Venice API key or call the direct inference API.

## Requirements

- Windows
- Microsoft Edge WebView2 Runtime
- For Android builds: a project containing its Gradle wrapper and the necessary Android build environment

Run `AetherStandalone.exe` to start the app. Rebuild it with `powershell -ExecutionPolicy Bypass -File .\build-windows.ps1`.
