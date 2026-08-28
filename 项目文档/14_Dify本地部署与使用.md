# Dify 本地部署与使用

## 当前状态

- Dify 版本：`1.14.2`
- 部署目录：`C:\D\project\VR+AI\打包\dify-1.14.2\docker`
- 本机入口：<http://localhost>
- 首次初始化：<http://localhost/install>
- 部署方式：Docker Compose
- 2026-07-18 已验证核心容器正常运行，Dify 尚未创建初始管理员。

## 首次使用

1. 确保 Docker Desktop 正在运行。
2. 打开 <http://localhost/install>。
3. 设置本机 Dify 的管理员邮箱、用户名和强密码。
4. 登录后配置模型供应商，或导入他人提供的应用 DSL。

管理员密码由使用者自行设置并保管，不应写入项目文件或本文档。

## 启动与停止

在 PowerShell 中进入部署目录：

```powershell
cd "C:\D\project\VR+AI\打包\dify-1.14.2\docker"
```

启动：

```powershell
docker compose up -d
```

查看状态：

```powershell
docker compose ps
```

停止但保留全部数据：

```powershell
docker compose stop
```

停止并移除容器但保留挂载数据：

```powershell
docker compose down
```

## 与他人 Dify 工作空间的关系

本机部署是一套独立 Dify 实例，数据库位于本机部署目录的数据卷中。它不会自动包含其他人的工作空间、知识库、工作流和邀请记录。

他人 Dify 生成的 `http://localhost/activate?...` 邀请链接，只有访问同一套 Dify 数据库时才有效。本机新实例无法验证另一台电脑数据库里的邀请 token。

如果只需要在 Unity 中使用对方完成的 AI 应用，应优先获取：

- 可从本机及 Pico 4 访问的 API Base URL
- 应用 API Key
- 应用类型（Chatflow、Workflow 或 Agent）
- 输入变量与返回格式
- WebApp 测试链接

## 安全注意事项

- 不要公开邀请 token、API Key、模型密钥或管理员密码。
- 已公开的邀请 token 应由工作空间管理员撤销并重新生成。
- 不要把生产 API Key 直接提交到 Git 仓库。
- `localhost` 只代表当前设备；Pico 4 无法用它访问电脑上的 Dify。
