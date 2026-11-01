# 参与贡献

欢迎反馈问题、补充文档和提交 Pull Request。修复 Bug 时，请说明复现步骤和修改后的行为；涉及较大的功能调整，可以先开 Issue 讨论。

## 本地开发

Windows 环境需要 PowerShell 7.2+、.NET Framework 编译器和 Node.js 22+。在仓库根目录执行：

```powershell
node --test tests/*.test.cjs
pwsh -File build.ps1 -Development
pwsh -File export.ps1
pwsh -File scripts/test-manager.ps1
```

构建产物位于 `artifacts`，可以用来验证安装器和插件的修改。

## 代码从哪里看

| 位置 | 内容 |
| --- | --- |
| `plugin/core.cjs`、`config.cjs` | 等待规则、监控状态及设置 |
| `plugin/renderer.cjs` | 客户端内按钮、设置面板、状态读取及游戏资料 |
| `plugin/main.cjs`、`transaction.cjs` | 监控调度、暂停执行与结果复核 |
| `plugin/adapter.cjs`、`processes.cjs` | 客户端能力识别和 Windows 进程检测 |
| `launcher/Manager.cs`、`Adaptive.cs` | 安装、卸载、入口发现与恢复 |
| `launcher/Maintenance.cs` | 本地维护、更新下载及回退 |
| `tests`、`launcher/*SelfTests.cs` | 脚本测试和管理器测试 |

整体流程见 [技术说明](docs/architecture.md)。

## 验证修改

改等待逻辑时，重点运行 `tests/core.test.cjs`；改状态或游戏资料读取时，运行 `adapter.test.cjs` 和 `metadata.test.cjs`；改进程检测时，运行 `processes.test.cjs`。界面文案或布局调整，还需要实际查看设置面板是否正常显示。

安装、更新或回退相关改动，完成开发构建和打包后运行 `scripts/test-manager.ps1`。CI 会执行完整的检查。开发构建用于本地测试，请勿作为正式安装包分发。

## 提交修改

1. Fork 仓库，在新分支上修改。
2. 运行与改动相关的测试，说明实际验证的客户端和场景。
3. 提交 PR，写清楚解决的问题、修改内容和验证结果。

请勿提交客户端本体、个人配置、账号凭据、原始日志或本地备份。模拟测试与真实客户端测试请分别说明。
