param(
    [string]$ApkPath = (Join-Path $PSScriptRoot '交付\苏州大学未来校区VR导览-PICO.apk')
)

$ErrorActionPreference = 'Stop'
$packageName = 'com.suda.futurecampusvr'
$launchActivity = "$packageName/com.unity3d.player.UnityPlayerActivity"
$adbCandidates = @(
    'C:\D\project\VR+AI\test\PICO\PICO Developer Center\resources\resources\adb\win32\platform-tools\adb.exe',
    'C:\Program Files\Unity\Hub\Editor\2022.3.62f3c1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe'
)
$adb = $adbCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($adb)) { throw '未找到 adb。请确认 PICO Developer Center 或 Unity Android SDK 已安装。' }
if (-not (Test-Path -LiteralPath $ApkPath)) { throw "未找到 APK：$ApkPath" }

& $adb start-server | Out-Null
$devices = & $adb devices
$connected = $devices | Where-Object { $_ -match "\tdevice$" }
if (-not $connected) { throw '未检测到 PICO。请连接 USB、戴上头显允许调试，并重新运行。' }

Write-Host '正在安装 PICO 成品 APK...'
# Some Android command-line tools cannot read Chinese paths reliably. Install
# from a short ASCII-only temporary path while keeping the delivered filename.
$tempApk = Join-Path ([System.IO.Path]::GetTempPath()) 'FutureCampusVR-PICO.apk'
Copy-Item -LiteralPath $ApkPath -Destination $tempApk -Force
try {
    & $adb install -r $tempApk
    $installExitCode = $LASTEXITCODE
} finally {
    Remove-Item -LiteralPath $tempApk -Force -ErrorAction SilentlyContinue
}
if ($installExitCode -ne 0) { throw 'APK 安装失败。' }

# Launch once so Unity creates Application.persistentDataPath, then install the
# existing local Dify client configuration without embedding its API key in APK.
& $adb shell am start -W -n $launchActivity | Out-Null
Start-Sleep -Seconds 4
& $adb shell am force-stop $packageName | Out-Null

$configCandidates = @(
    (Join-Path $PSScriptRoot '本机配置\dify_config.json'),
    (Join-Path $env:USERPROFILE 'AppData\LocalLow\DefaultCompany\Test\dify_config.json'),
    (Join-Path $env:USERPROFILE 'AppData\LocalLow\DefaultCompany\苏州大学未来校区VR导览\dify_config.json')
)
$sourceConfig = $configCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if ($sourceConfig) {
    $config = Get-Content -Raw -LiteralPath $sourceConfig | ConvertFrom-Json
    $config.enabled = $true
    $config.base_url = 'http://192.168.137.1/v1'
    $config.timeout_seconds = 30
    $tempConfig = Join-Path ([System.IO.Path]::GetTempPath()) 'future_campus_dify_config.json'
    $config | ConvertTo-Json | Set-Content -LiteralPath $tempConfig -Encoding UTF8
    $deviceFolder = "/sdcard/Android/data/$packageName/files"
    & $adb shell mkdir -p $deviceFolder | Out-Null
    & $adb push $tempConfig "$deviceFolder/dify_config.json" | Out-Null
    $pushExitCode = $LASTEXITCODE
    Remove-Item -LiteralPath $tempConfig -Force
    if ($pushExitCode -ne 0) { throw 'Dify 配置写入 PICO 失败。' }
    Write-Host 'Dify 校园知识库配置已写入头显。'
} else {
    Write-Warning '电脑上未找到 Dify 配置；APK 将使用本地 Ollama 备用回答。'
}

& $adb shell am start -W -n $launchActivity | Out-Null
Write-Host '安装完成。现场演示时：先开启电脑移动热点，再运行“一键启动虚拟校园服务.ps1”，最后启动头显中的“苏州大学未来校区VR导览”。'
