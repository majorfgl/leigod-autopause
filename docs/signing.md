# 发布签名与灾难恢复

发布签名用于验证更新包来自同一个发布者。它不是 Windows Authenticode 代码签名证书，也不会因此消除 SmartScreen 提示。

客户端内置公钥，可以公开。发布私钥必须保密；仅保留 GitHub 账号不能替代私钥备份。

## 备份

使用 PowerShell 7.2 或更新版本运行：

```powershell
pwsh -File scripts/backup-signing-key.ps1
```

在本机窗口设置至少 16 个字符的独立密码。工具导出标准加密 PKCS#8 文件（`.p8`），使用 PBES2、AES-256-CBC 和 PBKDF2-HMAC-SHA256（300000 次迭代）。导出后重新读取文件，用恢复的私钥签署随机挑战，再以项目公钥验证。

备份默认保存在“文档/LeigodAutoPause-Backups”，不能放在源码仓库内。`.p8.json` 是公钥指纹、文件摘要及验签回执，不含私钥或密码。保留至少两份异地副本，密码单独保存在可恢复的密码管理器或离线介质中。加密备份也不应上传到公开仓库。

## 换电脑恢复

从可信来源取回项目源码、公钥和备份后运行：

```powershell
pwsh -File scripts/restore-signing-key.ps1 -Backup '备份文件.p8' -VerifyOnly
pwsh -File scripts/restore-signing-key.ps1 -Backup '备份文件.p8'
```

密码在本机交互输入，不传入命令行或聊天。工具核对私钥与项目公钥，并把恢复结果重新用当前 Windows 用户的 DPAPI 加密保存。已经存在但身份不同的本地密钥不会被覆盖。

仅复制旧电脑的 `signing-key.dpapi` 通常不足以跨电脑、跨 Windows 用户恢复。备份密码遗失时，本工具不能解密备份。

## 发布环境

本机构建使用 DPAPI 存储的私钥。自动发布任务可以从受保护的环境密钥 `LEIGOD_SIGNING_KEY_XML` 读取同一私钥，普通 PR 和测试任务不得获得此密钥。仓库只提交公钥。构建不会在缺失私钥时生成替代身份。

GitHub 账号的双重验证恢复码、私钥备份和备份密码应分别保存。GitHub Secrets 是运行时存储，不作为唯一灾难恢复副本。

如果所有私钥副本都丢失，可以发布新的工具，但已经安装并信任旧公钥的客户端通常需要用户手动安装新版本。密钥泄露时应停止旧密钥发布、公告并制定迁移方案；当前协议不支持预先承诺无缝轮换。
