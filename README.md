# VR-AI-School

苏州大学未来校区 PICO VR 导览与本地校园 AI 项目。仓库包含 Unity/PICO 客户端源工程、校园知识库源资料、Dify 工作流、Ollama/FunASR/Edge TTS 后端脚本，以及 Windows 一键启动和安装脚本。

## 仓库包含什么

- `project/Assets`、`project/Packages`、`project/ProjectSettings`：Unity 2022.3.62f3c1 项目源文件与嵌入式 PICO SDK。
- `server.py`、`tts_server.py`：FunASR WebSocket 和 Edge TTS 服务。
- `dify-1.14.2/docker`：Dify 1.14.2 的可重建 Docker Compose 定义；不含本机数据库卷和密钥。
- `问题分类 + 知识库 + 聊天机器人.yml`：Dify Chatflow 导出。
- `知识库_转换工作区/知识库`、`知识库补充`：可重新导入的知识库源资料。
- `初始化新电脑.ps1`：首次迁移时创建 Python 环境、Dify 本机配置并下载 Ollama 模型。
- `一键启动虚拟校园服务.ps1`：启动并检查热点、Ollama/GPU、Docker/Dify、FunASR、TTS 和端到端 AI。
- `安装成品到PICO.ps1`：通过 USB/ADB 安装生成的 APK，并写入头显本机配置。

## 新电脑恢复顺序

1. 安装 Git for Windows（包含 Git LFS）、Unity Hub + Unity `2022.3.62f3c1` Android Build Support、Docker Desktop、Ollama、Python 3.10/3.11 和 PICO Developer Center。
2. 克隆仓库。建议使用不含中文的短路径，例如 `C:\VR-AI-School`，以避免 Unity Android/Gradle 的路径限制。
3. 在 PowerShell 中运行 `git lfs pull`，确认大型 FBX、贴图和二进制插件已下载。
4. 运行 `初始化新电脑.ps1`。
5. 运行 `一键启动虚拟校园服务.ps1`，打开 `http://localhost`，创建 Dify 管理员。
6. 导入根目录的 Dify YAML；创建知识库并导入 `知识库_转换工作区\知识库` 和 `知识库补充\知识库`。
7. 在 Dify 应用 API 页面生成 `app-` 开头的密钥，写入被 Git 忽略的 `本机配置\dify_config.json`。
8. 再次运行一键启动脚本进行完整端到端检查。
9. 用 Unity 打开 `project`。Android 构建可调用 `PicoReleaseBuilder.Build`；生成 APK 后运行 `安装成品到PICO.ps1`。

## 一键运行的边界

账号密码、Dify API Key、Docker 数据库卷、Ollama 模型文件、Python 虚拟环境、Unity `Library/Temp` 和构建产物不会进入 Git。它们含敏感信息、与单机绑定，或可由源文件重建。新电脑首次配置完成后，日常启动只需运行 `一键启动虚拟校园服务.ps1`。

## 安全与备份

- 不要提交 `key.txt`、`.env`、`本机配置/dify_config.json` 或 Dify `docker/volumes`。
- Git LFS 是完整恢复 Unity 大型资源的必要部分；克隆后务必执行 `git lfs pull`。
- 删除旧工作目录前，应在另一目录完成一次克隆、Unity 打开/构建及后端端到端测试。

