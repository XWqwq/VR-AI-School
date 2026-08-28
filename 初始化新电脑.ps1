param(
    [switch]$SkipPythonPackages,
    [switch]$SkipModelDownload
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$venvPython = Join-Path $scriptRoot '.venv\Scripts\python.exe'
$requirements = Join-Path $scriptRoot 'backend-requirements.txt'
$difyDocker = Join-Path $scriptRoot 'dify-1.14.2\docker'
$difyEnv = Join-Path $difyDocker '.env'
$difyEnvExample = Join-Path $difyDocker '.env.example'
$localConfigRoot = Join-Path $scriptRoot '本机配置'
$localDifyConfig = Join-Path $localConfigRoot 'dify_config.json'
$exampleDifyConfig = Join-Path $localConfigRoot 'dify_config.example.json'

function Find-Python {
    foreach ($candidate in @('py', 'python')) {
        $command = Get-Command $candidate -ErrorAction SilentlyContinue
        if ($command) { return $command.Source }
    }
    return $null
}

Write-Host '正在初始化虚拟校园的新电脑运行环境...'

foreach ($commandName in @('git', 'docker', 'ollama')) {
    if (-not (Get-Command $commandName -ErrorAction SilentlyContinue)) {
        throw "缺少 $commandName。请先安装 Git for Windows、Docker Desktop 和 Ollama，再重新运行。"
    }
}

if (-not (Test-Path -LiteralPath $venvPython)) {
    $pythonLauncher = Find-Python
    if ([string]::IsNullOrWhiteSpace($pythonLauncher)) {
        throw '未找到 Python。请安装 Python 3.10 或 3.11，并勾选 Add Python to PATH。'
    }
    Write-Host '正在创建项目独立 Python 环境...'
    & $pythonLauncher -m venv (Join-Path $scriptRoot '.venv')
}

if (-not $SkipPythonPackages) {
    Write-Host '正在安装语音识别与语音合成依赖（首次耗时较长）...'
    & $venvPython -m pip install --upgrade pip
    & $venvPython -m pip install -r $requirements
}

if (-not (Test-Path -LiteralPath $difyEnv)) {
    Copy-Item -LiteralPath $difyEnvExample -Destination $difyEnv
    Write-Host '已从官方示例创建 Dify 本机 .env。'
}

New-Item -ItemType Directory -Path $localConfigRoot -Force | Out-Null
if (-not (Test-Path -LiteralPath $localDifyConfig)) {
    Copy-Item -LiteralPath $exampleDifyConfig -Destination $localDifyConfig
    Write-Host "已创建本机 Dify 客户端配置：$localDifyConfig"
}

if (-not $SkipModelDownload) {
    Write-Host '正在下载/确认 Ollama qwen2.5:7b 模型...'
    & ollama pull qwen2.5:7b
}

Write-Host ''
Write-Host '基础环境初始化完成。首次使用还需：'
Write-Host '1. 运行“一键启动虚拟校园服务.ps1”。'
Write-Host '2. 浏览器打开 http://localhost，创建 Dify 管理员。'
Write-Host '3. 导入“问题分类 + 知识库 + 聊天机器人.yml”及“知识库_转换工作区\知识库”中的资料。'
Write-Host '4. 在 Dify 应用 API 页面创建密钥，填入“本机配置\dify_config.json”。'
Write-Host '完成一次配置后，后续即可使用一键启动脚本。'

