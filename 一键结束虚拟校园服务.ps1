[CmdletBinding()]
param(
    [switch]$StopDockerDesktop
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$logRoot = Join-Path $scriptRoot '启动日志'
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$logFile = Join-Path $logRoot "shutdown_$stamp.log"
$projectPython = 'C:\ProgramData\anaconda3\envs\vr\python.exe'

function Write-Log {
    param([string]$Level, [string]$Message)
    $line = '{0} [{1}] {2}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Level, $Message
    Write-Host $line
    Add-Content -LiteralPath $logFile -Value $line -Encoding UTF8
}

function Get-MemoryPercent {
    $os = Get-CimInstance Win32_OperatingSystem
    [math]::Round((1 - ($os.FreePhysicalMemory / $os.TotalVisibleMemorySize)) * 100, 1)
}

$before = Get-MemoryPercent
Write-Log INFO "开始关闭虚拟校园 AI 服务，当前内存占用 $before%"

# 只关闭由本项目 server.py 启动的 FunASR，避免影响其他 Python 程序。
$funAsrProcesses = Get-CimInstance Win32_Process | Where-Object {
    $_.Name -eq 'python.exe' -and $_.CommandLine -match '(^|[\s"])server\.py([\s"]|$)' -and
    ($_.ExecutablePath -eq $projectPython -or $_.ExecutablePath -match '\\envs\\bdc2026\\')
}
foreach ($process in $funAsrProcesses) {
    Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
    Write-Log OK "FunASR 已关闭（PID $($process.ProcessId)）"
}
if (-not $funAsrProcesses) { Write-Log INFO 'FunASR 未运行' }

# Close only this project's on-demand Edge TTS service.
$ttsProcesses = Get-CimInstance Win32_Process | Where-Object {
    $_.Name -eq 'python.exe' -and $_.CommandLine -match '(^|[\s"])tts_server\.py([\s"]|$)' -and
    ($_.ExecutablePath -eq $projectPython -or $_.ExecutablePath -match '\\envs\\bdc2026\\')
}
foreach ($process in $ttsProcesses) {
    Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue
    Write-Log OK "Edge TTS 已关闭（PID $($process.ProcessId)）"
}
if (-not $ttsProcesses) { Write-Log INFO 'Edge TTS 未运行' }

if (Get-Command ollama -ErrorAction SilentlyContinue) {
    $ErrorActionPreference = 'Continue'
    & ollama stop qwen2.5:3b 2>&1 | ForEach-Object { Add-Content -LiteralPath $logFile -Value $_ -Encoding UTF8 }
    & ollama stop qwen2.5:7b 2>&1 | ForEach-Object { Add-Content -LiteralPath $logFile -Value $_ -Encoding UTF8 }
    & ollama stop qwen3.5:9b 2>&1 | ForEach-Object { Add-Content -LiteralPath $logFile -Value $_ -Encoding UTF8 }
    $ErrorActionPreference = 'Stop'
    Write-Log OK '虚拟校园使用的 Ollama 模型已卸载'
} else {
    Write-Log INFO '未找到 Ollama 命令'
}

$composeFile = Join-Path $scriptRoot 'dify-1.14.2\docker\docker-compose.yaml'
if ((Get-Command docker -ErrorAction SilentlyContinue) -and (Test-Path -LiteralPath $composeFile)) {
    $ErrorActionPreference = 'Continue'
    & docker compose -f $composeFile stop 2>&1 | ForEach-Object { Add-Content -LiteralPath $logFile -Value $_ -Encoding UTF8 }
    $composeExit = $LASTEXITCODE
    if ($composeExit -eq 0) { Write-Log OK 'Dify 容器已停止，知识库和数据卷已保留' }
    else { Write-Log INFO 'Docker 未运行或 Dify 容器已经停止' }
    if ($StopDockerDesktop) {
        & docker desktop stop 2>&1 | ForEach-Object { Add-Content -LiteralPath $logFile -Value $_ -Encoding UTF8 }
        Write-Log OK 'Docker Desktop 关闭命令已执行'
    } else {
        Write-Log INFO 'Docker Desktop 保持待机，避免下次启动触发本机套接字故障；如需完全释放请加 -StopDockerDesktop'
    }
    $ErrorActionPreference = 'Stop'
} else {
    Write-Log INFO 'Docker 或 Dify Compose 文件不可用，跳过'
}

Start-Sleep -Seconds 3
$after = Get-MemoryPercent
Write-Log OK "结束完成，当前内存占用 $after%（开始时 $before%）"
Write-Log INFO "结束日志：$logFile"
exit 0
