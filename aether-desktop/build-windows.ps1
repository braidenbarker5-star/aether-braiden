$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$desktop = Split-Path -Parent $MyInvocation.MyCommand.Path
$tauri = Join-Path $desktop 'src-tauri'
$output = Join-Path $tauri 'build-output'
$temp = Join-Path $env:TEMP 'aether-windows-build'
$webViewPackage = Join-Path $temp 'webview2.nupkg'
$webViewUrl = 'https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/1.0.3650.58/microsoft.web.webview2.1.0.3650.58.nupkg'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $framework 'csc.exe'

if (-not (Test-Path -LiteralPath $csc)) { throw 'The Windows .NET Framework C# compiler is unavailable.' }
if (-not (Test-Path -LiteralPath (Join-Path ${env:ProgramFiles(x86)} 'Microsoft\EdgeWebView\Application'))) {
    throw 'Install the Microsoft Edge WebView2 Runtime before using this desktop application.'
}
New-Item -ItemType Directory -Force -Path $temp, $output | Out-Null
if (-not (Test-Path -LiteralPath $webViewPackage)) {
    Invoke-WebRequest -Uri $webViewUrl -OutFile $webViewPackage -TimeoutSec 180
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$extract = Join-Path $temp 'webview2'
$net462 = Join-Path $extract 'lib\net462'
if (-not (Test-Path -LiteralPath (Join-Path $net462 'Microsoft.Web.WebView2.WinForms.dll'))) {
    if (Test-Path -LiteralPath $extract) { Remove-Item -LiteralPath $extract -Recurse -Force }
    [IO.Compression.ZipFile]::ExtractToDirectory($webViewPackage, $extract)
}

$assets = Join-Path $output 'assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null
foreach ($name in @('index.html', 'app.css', 'app.js')) {
    Copy-Item -LiteralPath (Join-Path $desktop "public\$name") -Destination (Join-Path $assets $name) -Force
}

$core = Join-Path $net462 'Microsoft.Web.WebView2.Core.dll'
$forms = Join-Path $net462 'Microsoft.Web.WebView2.WinForms.dll'
$loader = Join-Path $extract 'build\native\x64\WebView2Loader.dll'
$management = Join-Path $framework 'System.Management.dll'
$webExtensions = Join-Path $framework 'System.Web.Extensions.dll'
$assembly = Join-Path $output 'AetherStandalone.exe'
$resourceArguments = @(
    "/resource:$(Join-Path $assets 'index.html'),AetherStandalone.assets.index.html",
    "/resource:$(Join-Path $assets 'app.css'),AetherStandalone.assets.app.css",
    "/resource:$(Join-Path $assets 'app.js'),AetherStandalone.assets.app.js",
    "/resource:$core,AetherStandalone.deps.Microsoft.Web.WebView2.Core.dll",
    "/resource:$forms,AetherStandalone.deps.Microsoft.Web.WebView2.WinForms.dll",
    "/resource:$loader,AetherStandalone.deps.WebView2Loader.dll",
    "/resource:$management,AetherStandalone.deps.System.Management.dll",
    "/resource:$webExtensions,AetherStandalone.deps.System.Web.Extensions.dll"
)
$icon = Join-Path $tauri 'icons\icon.ico'
if (Test-Path -LiteralPath $icon) { $resourceArguments += "/win32icon:$icon" }
$source = Join-Path $tauri 'src\windows-host.cs'
& $csc /nologo /target:winexe /platform:anycpu /optimize+ /out:$assembly `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll `
    "/reference:$management" "/reference:$webExtensions" "/reference:$core" "/reference:$forms" @resourceArguments $source
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $assembly)) { throw 'Windows single-file desktop executable compilation failed.' }

Remove-Item -LiteralPath $assets -Recurse -Force
Remove-Item -LiteralPath (Join-Path $output 'Microsoft.Web.WebView2.Core.dll'), (Join-Path $output 'Microsoft.Web.WebView2.WinForms.dll'), (Join-Path $output 'WebView2Loader.dll') -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $desktop 'README.md') -Destination (Join-Path $output 'README.txt') -Force
Get-ChildItem -LiteralPath $output -File | Select-Object Name,Length
Write-Output "Standalone Venice chat executable built: $assembly"
Write-Output 'Venice chat UI, WebView2 controls, and native loader are embedded in the executable.'
Write-Output 'Microsoft Edge WebView2 Runtime is a prerequisite.'
