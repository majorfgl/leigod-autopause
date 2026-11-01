#requires -Version 7.2
param([string]$Destination, [switch]$Console)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'signing-key.ps1')
if (-not $Destination) { $Destination = Join-Path ([Environment]::GetFolderPath('MyDocuments')) ('LeigodAutoPause-Backups/release-key-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.p8') }
if ($Console) {
    $first = Read-Host 'Backup password (at least 16 characters; keep it separately)' -AsSecureString
    $second = Read-Host 'Repeat password' -AsSecureString
    $password = [Net.NetworkCredential]::new('', $first).Password
    try {
        if ($password -cne [Net.NetworkCredential]::new('', $second).Password) { throw 'Passwords do not match.' }
        Export-EncryptedReleaseKey -Path $Destination -Password $password
    } finally { $password = $null; $first.Dispose(); $second.Dispose() }
    exit
}
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
$form = [Windows.Forms.Form]::new(); $form.Text = '发布签名密钥 · 加密备份'; $form.ClientSize = [Drawing.Size]::new(660,330); $form.StartPosition = 'CenterScreen'; $form.FormBorderStyle = 'FixedDialog'; $form.MaximizeBox = $false
$form.Font = [Drawing.Font]::new('Microsoft YaHei UI',10)
$description = [Windows.Forms.Label]::new(); $description.SetBounds(20,15,620,62); $description.Text = "请设置至少 16 个字符的独立备份密码。密码不会发送到聊天或服务器。`n备份可在新电脑恢复；请把密码与备份文件分开保存。"; $form.Controls.Add($description)
$pathLabel = [Windows.Forms.Label]::new(); $pathLabel.SetBounds(20,86,110,25); $pathLabel.Text = '备份文件'; $form.Controls.Add($pathLabel)
$pathBox = [Windows.Forms.TextBox]::new(); $pathBox.SetBounds(130,82,500,30); $pathBox.Text = $Destination; $form.Controls.Add($pathBox)
$passwordLabel = [Windows.Forms.Label]::new(); $passwordLabel.SetBounds(20,130,110,25); $passwordLabel.Text = '备份密码'; $form.Controls.Add($passwordLabel)
$passwordBox = [Windows.Forms.TextBox]::new(); $passwordBox.SetBounds(130,126,500,30); $passwordBox.UseSystemPasswordChar = $true; $form.Controls.Add($passwordBox)
$repeatLabel = [Windows.Forms.Label]::new(); $repeatLabel.SetBounds(20,174,110,25); $repeatLabel.Text = '重复密码'; $form.Controls.Add($repeatLabel)
$repeatBox = [Windows.Forms.TextBox]::new(); $repeatBox.SetBounds(130,170,500,30); $repeatBox.UseSystemPasswordChar = $true; $form.Controls.Add($repeatBox)
$status = [Windows.Forms.Label]::new(); $status.SetBounds(20,258,620,56); $status.Text = '尚未生成备份。'; $form.Controls.Add($status)
$button = [Windows.Forms.Button]::new(); $button.SetBounds(130,215,240,35); $button.Text = '生成备份并验证恢复'; $form.Controls.Add($button)
$button.Add_Click({
    try {
        $button.Enabled = $false; $form.UseWaitCursor = $true
        if ($passwordBox.Text -cne $repeatBox.Text) { throw '两次密码不一致。' }
        $result = Export-EncryptedReleaseKey -Path $pathBox.Text -Password $passwordBox.Text
        $passwordBox.Clear(); $repeatBox.Clear(); $passwordBox.Enabled = $repeatBox.Enabled = $pathBox.Enabled = $false
        $status.Text = "备份及恢复验签通过。请转存 .p8 与 .p8.json 文件。`n" + $result.path
        $button.Text = '备份已验证'; $form.Tag = 'complete'
    } catch { $status.Text = $_.Exception.Message; $button.Enabled = $true }
    finally { $form.UseWaitCursor = $false }
})
$form.AcceptButton = $button
[void]$form.ShowDialog(); $passwordBox.Clear(); $repeatBox.Clear(); $form.Dispose()
