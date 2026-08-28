param(
    [switch]$SkipAiTest,
    [int]$DockerTimeoutSeconds = 180,
    [int]$DifyTimeoutSeconds = 180,
    [int]$AsrTimeoutSeconds = 120,
    [int]$TtsTimeoutSeconds = 45
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$logRoot = Join-Path $scriptRoot '启动日志'
$runStamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$runLog = Join-Path $logRoot "startup_$runStamp.log"
$statusFile = Join-Path $logRoot 'latest_status.json'
$asrOutLog = Join-Path $logRoot 'funasr_stdout.log'
$asrErrLog = Join-Path $logRoot 'funasr_stderr.log'
$dockerDesktop = 'C:\Program Files\Docker\Docker\Docker Desktop.exe'
$composeFile = Join-Path $scriptRoot 'dify-1.14.2\docker\docker-compose.yaml'
$asrServer = Join-Path $scriptRoot 'server.py'
$ttsServer = Join-Path $scriptRoot 'tts_server.py'
$funAsrPythonCandidates = @(
    $env:VIRTUAL_CAMPUS_PYTHON,
    (Join-Path $scriptRoot '.venv\Scripts\python.exe'),
    'C:\ProgramData\anaconda3\envs\vr\python.exe'
)
$funAsrPython = $funAsrPythonCandidates | Where-Object {
    -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_)
} | Select-Object -First 1
$unityNetworkConfig = Join-Path $scriptRoot 'project\Assets\Resources\CampusNetworkConfig.json'
$repoDifyConfig = Join-Path $scriptRoot '本机配置\dify_config.json'
$unityConfigCandidates = @(
    $repoDifyConfig,
    (Join-Path $env:USERPROFILE 'AppData\LocalLow\DefaultCompany\Test\dify_config.json'),
    (Join-Path $env:USERPROFILE 'AppData\LocalLow\DefaultCompany\苏州大学未来校区VR导览\dify_config.json')
)
$unityConfig = $unityConfigCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($unityConfig)) { $unityConfig = $repoDifyConfig }
$requiredModel = 'qwen2.5:7b'
$results = [ordered]@{}

New-Item -ItemType Directory -Path $logRoot -Force | Out-Null

function Write-RunLog {
    param([string]$Message, [string]$Level = 'INFO')
    $line = "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') [$Level] $Message"
    $line | Tee-Object -FilePath $runLog -Append
}

function Set-Result {
    param([string]$Name, [bool]$Success, [string]$Detail)
    $results[$Name] = [ordered]@{
        success = $Success
        detail = $Detail
        checked_at = (Get-Date).ToString('o')
    }
    Write-RunLog "$Name：$Detail" $(if ($Success) { 'OK' } else { 'ERROR' })
}

function Wait-Until {
    param([scriptblock]$Probe, [int]$TimeoutSeconds, [int]$IntervalSeconds = 2)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        try {
            if (& $Probe) { return $true }
        } catch {
            # Expected while a service is still starting.
        }
        Start-Sleep -Seconds $IntervalSeconds
    } while ((Get-Date) -lt $deadline)
    return $false
}

function Test-Http {
    param([string]$Uri, [int]$TimeoutSeconds = 5)
    try {
        $response = Invoke-WebRequest -UseBasicParsing -Uri $Uri -TimeoutSec $TimeoutSeconds
        return $response.StatusCode -ge 200 -and $response.StatusCode -lt 500
    } catch {
        return $false
    }
}

function Test-DockerEngine {
    try {
        # Bound every probe because Docker CLI can hang on a stale Desktop socket.
        $probe = Start-Process -FilePath 'docker' -ArgumentList @('version', '--format', '{{.Server.Version}}') `
            -PassThru -WindowStyle Hidden
        if (-not $probe.WaitForExit(5000)) {
            Stop-Process -Id $probe.Id -Force -ErrorAction SilentlyContinue
            return $false
        }
        return $probe.ExitCode -eq 0
    } catch {
        return $false
    }
}

function Test-DockerInferenceSocketFailure {
    param([datetime]$Since)
    try {
        $backendLog = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Docker\log\host') `
            -Filter 'com.docker.backend.exe.log*' -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($null -eq $backendLog -or $backendLog.LastWriteTime -lt $Since) { return $false }
        $recent = Get-Content -LiteralPath $backendLog.FullName -Tail 180 -ErrorAction SilentlyContinue | Out-String
        return $recent -match 'dockerInference' -and $recent -match 'file cannot be accessed|filename, directory name, or volume label syntax'
    } catch {
        return $false
    }
}

function Repair-DockerInferenceSockets {
    # Docker Desktop 4.77 can leave Unix socket placeholders that Windows cannot
    # remove. Only stop Docker's own processes and unlink the four exact Docker
    # socket paths through the existing Ubuntu WSL filesystem view.
    Write-RunLog '检测到 Docker Desktop 本地套接字残留，正在执行限定范围自修复...' 'WARN'
    $dockerProcessNames = @('Docker Desktop', 'com.docker.backend', 'com.docker.build', 'com.docker.proxy', 'vpnkit')
    Get-Process -ErrorAction SilentlyContinue | Where-Object {
        $_.ProcessName -in $dockerProcessNames
    } | Stop-Process -Force -ErrorAction SilentlyContinue

    $previousErrorPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & wsl.exe --terminate docker-desktop 2>&1 | Add-Content -LiteralPath $runLog
    & wsl.exe -d Ubuntu -- sh -lc `
        'rm -f /mnt/c/Users/XUER_WANG/AppData/Local/Docker/run/dockerEthernetVfkit /mnt/c/Users/XUER_WANG/AppData/Local/Docker/run/dockerInference /mnt/c/Users/XUER_WANG/AppData/Local/Docker/run/userAnalyticsOtlpHttp.sock /mnt/c/Users/XUER_WANG/AppData/Local/docker-secrets-engine/engine.sock' `
        2>&1 | Add-Content -LiteralPath $runLog
    $repairExitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorPreference
    if ($repairExitCode -ne 0) { throw 'Docker 本地套接字自修复失败，请查看启动日志' }
    Write-RunLog 'Docker Desktop 本地套接字残留已清理' 'OK'
}

function Get-LanIPv4 {
    # The delivered APK always uses the Windows Mobile Hotspot gateway. Never
    # write a venue/WLAN address into the next build by accident.
    return '192.168.137.1'
}

function Test-PicoHotspot {
    $hotspotAddress = Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object {
            $_.AddressState -eq 'Preferred' -and $_.IPAddress -eq '192.168.137.1'
        } | Select-Object -First 1 -ExpandProperty IPAddress
    return -not [string]::IsNullOrWhiteSpace($hotspotAddress)
}

function Enable-VirtualCampusFirewallPorts {
    try {
        $ruleName = 'Virtual Campus LAN AI Services'
        if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
            New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Action Allow -Profile Private `
                -Protocol TCP -LocalPort 80,8765,8766,11434 -RemoteAddress LocalSubnet | Out-Null
        }
        Write-RunLog '已确认防火墙允许局域网访问 Dify、FunASR 和 Edge TTS'
    } catch {
        Write-RunLog '未能自动添加防火墙规则；请以管理员身份运行一次启动脚本，或手动放行 TCP 80、8765、8766、11434（专用网络）' 'WARN'
    }
}

Write-RunLog '========== 虚拟校园服务启动开始 =========='
Write-RunLog "脚本目录：$scriptRoot"

try {
    $lanIp = Get-LanIPv4
    Write-RunLog "PICO 局域网服务地址：http://$lanIp"
    $hotspotReady = Test-PicoHotspot
    Set-Result 'PICO热点' $hotspotReady $(if ($hotspotReady) {
        '电脑移动热点已开启，头显可访问固定地址 192.168.137.1'
    } else {
        '电脑移动热点未开启；后台会继续启动，但头显暂时无法访问。请开启 Windows 移动热点并让 PICO 连接后再启动游戏'
    })
    Enable-VirtualCampusFirewallPorts
    $networkConfig = [ordered]@{
        asr_url = "ws://${lanIp}:8765"
        dify_base_url = "http://${lanIp}/v1"
        ollama_base_url = "http://${lanIp}:11434"
        edge_tts_url = "http://${lanIp}:8766/synthesize"
        edge_tts_voice = 'zh-CN-XiaoxiaoNeural'
    }
    $networkConfig | ConvertTo-Json | Set-Content -LiteralPath $unityNetworkConfig -Encoding UTF8

    # 1. Ollama
    if (-not (Test-Http 'http://localhost:11434/api/tags')) {
        Write-RunLog 'Ollama 未响应，正在启动 Ollama App...'
        $ollamaApp = Join-Path $env:LOCALAPPDATA 'Programs\Ollama\ollama app.exe'
        if (Test-Path -LiteralPath $ollamaApp) {
            Start-Process -FilePath $ollamaApp -WindowStyle Hidden
        } else {
            Start-Process -FilePath 'ollama' -ArgumentList 'serve' -WindowStyle Hidden `
                -RedirectStandardOutput (Join-Path $logRoot 'ollama_stdout.log') `
                -RedirectStandardError (Join-Path $logRoot 'ollama_stderr.log')
        }
    }
    $ollamaReady = Wait-Until { Test-Http 'http://localhost:11434/api/tags' } 60
    if (-not $ollamaReady) { throw 'Ollama 在 60 秒内未就绪' }
    $modelList = & ollama list 2>&1 | Out-String
    $modelReady = $modelList -match [regex]::Escape($requiredModel)
    Set-Result 'Ollama' $modelReady $(if ($modelReady) { "服务正常，模型 $requiredModel 已安装" } else { "服务正常，但缺少模型 $requiredModel" })
    if (-not $modelReady) { throw "缺少 Ollama 模型 $requiredModel" }
    # The live demo uses one model only. Remove old fallback/test models from
    # VRAM, then preload 7B with the same 8K context used by the Dify workflow.
    foreach ($unusedModel in @('qwen2.5:3b', 'qwen3.5:9b')) {
        try {
            # `ollama stop` returns a non-zero native exit code when the model
            # isn't loaded. That's already the desired state, so don't abort
            # the entire startup sequence for it.
            & ollama stop $unusedModel 2>$null | Out-Null
        } catch {
            Write-RunLog "Ollama 旧模型未驻留，跳过：$unusedModel"
        }
    }
    $warmupBody = @{
        model = $requiredModel
        prompt = '只回复：就绪'
        stream = $false
        keep_alive = '10m'
        options = @{ num_ctx = 8192; num_predict = 8; temperature = 0 }
    } | ConvertTo-Json -Depth 4
    $warmupResult = Invoke-RestMethod -Uri 'http://localhost:11434/api/generate' -Method Post `
        -ContentType 'application/json' -Body $warmupBody -TimeoutSec 120
    if ([string]::IsNullOrWhiteSpace($warmupResult.response)) { throw "模型 $requiredModel 预热失败" }
    Write-RunLog "Ollama 已统一并预热 $requiredModel（8K 上下文）"

    # Verify the actual loaded runner rather than relying on Task Manager's
    # default 3D graph, which often makes CUDA inference appear idle.
    $ollamaProcessText = & ollama ps 2>&1 | Out-String
    $gpuRunnerReady = $ollamaProcessText -match [regex]::Escape($requiredModel) -and
                      $ollamaProcessText -match '100%\s+GPU' -and
                      $ollamaProcessText -match '\b8192\b'
    $gpuMemory = try {
        (& nvidia-smi --query-gpu=memory.used,memory.total --format=csv,noheader 2>$null | Select-Object -First 1).Trim()
    } catch { '无法读取显存' }
    Set-Result 'GPU推理' $gpuRunnerReady $(if ($gpuRunnerReady) {
        "$requiredModel 已全量加载到 NVIDIA GPU，8K 上下文；显存 $gpuMemory"
    } else {
        "模型未按预期以 100% GPU / 8K 上下文运行。ollama ps：$($ollamaProcessText.Trim())"
    })
    if (-not $gpuRunnerReady) { throw 'Ollama GPU 推理状态异常' }

    # 2. Docker Desktop and Dify
    $dockerReady = Test-DockerEngine
    if (-not $dockerReady) {
        if (-not (Test-Path -LiteralPath $dockerDesktop)) { throw "找不到 Docker Desktop：$dockerDesktop" }
        Write-RunLog 'Docker 引擎未就绪，正在启动 Docker Desktop...'
        $dockerLaunchTime = Get-Date
        Start-Process -FilePath $dockerDesktop -WindowStyle Hidden
        Start-Sleep -Seconds 8
        if (-not (Test-DockerEngine) -and (Test-DockerInferenceSocketFailure $dockerLaunchTime)) {
            Repair-DockerInferenceSockets
            $dockerLaunchTime = Get-Date
            Start-Process -FilePath $dockerDesktop -WindowStyle Hidden
        }
        $dockerReady = Wait-Until { Test-DockerEngine } $DockerTimeoutSeconds
    }
    Set-Result 'Docker' $dockerReady $(if ($dockerReady) { 'Docker 引擎正常' } else { "Docker 在 $DockerTimeoutSeconds 秒内未就绪" })
    if (-not $dockerReady) { throw 'Docker 启动失败' }

    if (-not (Test-Path -LiteralPath $composeFile)) { throw "找不到 Dify Compose 文件：$composeFile" }
    $difyEnv = Join-Path (Split-Path -Parent $composeFile) '.env'
    $difyEnvExample = Join-Path (Split-Path -Parent $composeFile) '.env.example'
    if (-not (Test-Path -LiteralPath $difyEnv)) {
        if (-not (Test-Path -LiteralPath $difyEnvExample)) { throw '缺少 Dify .env 和 .env.example' }
        Copy-Item -LiteralPath $difyEnvExample -Destination $difyEnv
        Write-RunLog '已从官方示例创建 Dify 本机 .env'
    }
    Write-RunLog '正在启动/恢复 Dify 容器...'
    # Docker Compose writes normal progress messages to stderr. PowerShell 5.1
    # otherwise promotes them to terminating ErrorRecord objects.
    $previousErrorPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $composeOutput = (& docker compose -f $composeFile up -d 2>&1 | ForEach-Object { $_.ToString() }) -join "`n"
    $composeExitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorPreference
    $composeOutput | Add-Content -LiteralPath $runLog
    if ($composeExitCode -ne 0) { throw "docker compose up 失败：$composeOutput" }
    $difyReady = Wait-Until { Test-Http 'http://localhost/console/api/setup' } $DifyTimeoutSeconds
    Set-Result 'Dify' $difyReady $(if ($difyReady) { 'Web/API 已响应 http://localhost' } else { "Dify 在 $DifyTimeoutSeconds 秒内未就绪" })
    if (-not $difyReady) { throw 'Dify 启动失败' }

    $containerSummary = & docker ps --format '{{.Names}}|{{.Status}}' 2>&1 | Out-String
    Write-RunLog "Dify 容器状态：`n$containerSummary"

    # 3. FunASR
    $asrReady = Test-NetConnection -ComputerName localhost -Port 8765 -InformationLevel Quiet -WarningAction SilentlyContinue
    if (-not $asrReady) {
        if (-not (Test-Path -LiteralPath $asrServer)) { throw "找不到 FunASR 服务脚本：$asrServer" }
        if ([string]::IsNullOrWhiteSpace($funAsrPython) -or -not (Test-Path -LiteralPath $funAsrPython)) {
            throw '找不到 FunASR Python 环境。请先运行“初始化新电脑.ps1”'
        }
        & $funAsrPython -c 'import funasr, torch, websockets' 2>$null
        if ($LASTEXITCODE -ne 0) { throw "FunASR Python 环境缺少依赖：$funAsrPython" }
        Write-RunLog 'FunASR 端口未监听，正在启动语音识别服务...'
        Start-Process -FilePath $funAsrPython -ArgumentList @('server.py') -WorkingDirectory $scriptRoot `
            -WindowStyle Hidden -RedirectStandardOutput $asrOutLog -RedirectStandardError $asrErrLog
        $asrReady = Wait-Until {
            Test-NetConnection -ComputerName localhost -Port 8765 -InformationLevel Quiet -WarningAction SilentlyContinue
        } $AsrTimeoutSeconds 3
    }
    $asrDetail = if ($asrReady) { "WebSocket 已监听 ws://${lanIp}:8765（PICO 可访问）" } else { "启动失败，请查看 $asrErrLog" }
    Set-Result 'FunASR' $asrReady $asrDetail
    if (-not $asrReady) { throw 'FunASR 启动失败' }

    # 4. Edge TTS (small on-demand service; no model stays resident in RAM).
    if (-not (Test-Path -LiteralPath $ttsServer)) { throw "找不到 Edge TTS 服务脚本：$ttsServer" }
    & $funAsrPython -c 'import edge_tts' 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-RunLog '正在安装 Edge TTS Python 依赖（首次仅需一次）...'
        & $funAsrPython -m pip install edge-tts 2>&1 | Add-Content -LiteralPath $runLog
        if ($LASTEXITCODE -ne 0) { throw 'Edge TTS 依赖安装失败，请检查网络或 Python 环境' }
    }
    $ttsReady = Test-Http "http://localhost:8766/synthesize?text=%E6%B5%8B%E8%AF%95"
    if (-not $ttsReady) {
        Start-Process -FilePath $funAsrPython -ArgumentList @('tts_server.py') -WorkingDirectory $scriptRoot `
            -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logRoot 'edge_tts_stdout.log') `
            -RedirectStandardError (Join-Path $logRoot 'edge_tts_stderr.log')
        $ttsReady = Wait-Until { Test-Http "http://localhost:8766/synthesize?text=%E6%B5%8B%E8%AF%95" 30 } $TtsTimeoutSeconds 2
    }
    Set-Result 'Edge TTS' $ttsReady $(if ($ttsReady) { "服务正常：http://${lanIp}:8766/synthesize" } else { '启动失败，请查看 edge_tts_stderr.log' })
    if (-not $ttsReady) { throw 'Edge TTS 启动失败' }

    # 5. End-to-end Dify -> knowledge base -> Ollama check.
    if ($SkipAiTest) {
        Set-Result 'AI端到端测试' $true '已按参数跳过'
    } elseif (-not (Test-Path -LiteralPath $unityConfig)) {
        Set-Result 'AI端到端测试' $false "找不到 Unity Dify 配置：$unityConfig"
        throw '缺少 Unity Dify 配置'
    } else {
        $config = Get-Content -Raw -LiteralPath $unityConfig | ConvertFrom-Json
        if (-not $config.enabled -or [string]::IsNullOrWhiteSpace($config.api_key)) {
            throw 'Unity dify_config.json 未启用或缺少 API Key'
        }
        Write-RunLog '正在执行 Dify → 知识库 → Ollama 端到端测试，首次模型加载可能需要 1～3 分钟...'
        $body = @{
            inputs = @{
                scene_name = 'StartupCheck'
                location_id = ''
                location_name = ''
                location_description = ''
                interaction_type = 'startup_check'
                user_interest = '人工智能'
            }
            query = '你好，请只回复：虚拟校园AI服务正常'
            response_mode = 'blocking'
            conversation_id = ''
            user = 'virtual-campus-startup-check'
        } | ConvertTo-Json -Depth 6
        try {
            # This is a host-side health check. Always use localhost so it
            # remains valid before the PICO joins the Windows hotspot.
            $aiResponse = Invoke-RestMethod -Method Post `
                -Uri 'http://localhost/v1/chat-messages' `
                -Headers @{ Authorization = "Bearer $($config.api_key)" } `
                -ContentType 'application/json; charset=utf-8' `
                -Body ([Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 240
            $cleanAnswer = ($aiResponse.answer -replace '<think>[\s\S]*?</think>', '').Trim()
            $aiReady = -not [string]::IsNullOrWhiteSpace($cleanAnswer)
            Set-Result 'AI端到端测试' $aiReady "回答成功，长度 $($cleanAnswer.Length)，conversation_id=$($aiResponse.conversation_id)"
            Write-RunLog "AI 测试回答：$cleanAnswer"
            if (-not $aiReady) { throw 'Dify 返回空答案' }
        } catch {
            Set-Result 'AI端到端测试' $false $_.Exception.Message
            throw
        }
    }
} catch {
    Write-RunLog $_.Exception.Message 'ERROR'
} finally {
    $requiredChecks = @('PICO热点', 'Ollama', 'GPU推理', 'Docker', 'Dify', 'FunASR', 'Edge TTS', 'AI端到端测试')
    $completedAllChecks = -not ($requiredChecks | Where-Object { -not $results.Contains($_) })
    $allPassed = $completedAllChecks -and -not ($results.Values | Where-Object { -not $_.success })
    $summary = [ordered]@{
        run_at = (Get-Date).ToString('o')
        success = $allPassed
        log_file = $runLog
        services = $results
    }
    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $statusFile -Encoding UTF8
    Write-RunLog "总结果：$(if ($allPassed) { '全部服务启动成功' } else { '存在启动失败项' })" $(if ($allPassed) { 'OK' } else { 'ERROR' })
    Write-RunLog "状态摘要：$statusFile"
    Write-RunLog '========== 虚拟校园服务启动结束 =========='
    Write-Host ''
    Write-Host $(if ($allPassed) { '全部服务启动成功，可以打开 Unity/PDC。' } else { '有服务启动失败，请查看上方错误和启动日志。' }) `
        -ForegroundColor $(if ($allPassed) { 'Green' } else { 'Red' })
    Write-Host "启动日志：$runLog"
    if (-not $allPassed) { exit 1 }
}
