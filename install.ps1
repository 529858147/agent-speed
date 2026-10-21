<#
Agent Speed installer for Windows.

    irm https://raw.githubusercontent.com/zoefix/agent-speed/main/install.ps1 | iex

With options:

    & ([scriptblock]::Create((irm https://raw.githubusercontent.com/zoefix/agent-speed/main/install.ps1))) -Uninstall

Options: -Version vX.Y.Z, -NoLaunch, -NoAutostart, -Uninstall, -Purge
Environment: AGENT_SPEED_VERSION, AGENT_SPEED_DOWNLOAD_BASE (mirrors, testing)
#>
[CmdletBinding()]
param(
    [string]$Version = $env:AGENT_SPEED_VERSION,
    [switch]$NoLaunch,
    [switch]$NoAutostart,
    [switch]$Uninstall,
    [switch]$Purge
)

# Run in a child scope so preferences and helpers do not leak into the caller's session.
& {
    Set-StrictMode -Version 3.0
    $ErrorActionPreference = 'Stop'
    $ProgressPreference = 'SilentlyContinue'

    $Repo = 'zoefix/agent-speed'
    $AppName = 'Agent Speed'
    $ExeName = 'AgentSpeed.exe'
    $InstallDir = Join-Path $env:LOCALAPPDATA 'Programs\AgentSpeed'
    $StartMenuLink = Join-Path ([Environment]::GetFolderPath('Programs')) "$AppName.lnk"
    $UninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\AgentSpeed'
    $RunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    $SettingsDir = Join-Path $env:APPDATA 'AgentSpeed'
    $LogsDir = Join-Path $env:LOCALAPPDATA 'AgentSpeed'

    function Get-Text {
        $culture = [System.Globalization.CultureInfo]::CurrentUICulture.Name
        $t = @{
            Downloading = 'Downloading the latest release'; Verifying = 'Verifying integrity'; Installing = 'Installing'
            Done = 'Installed'; Launched = 'Agent Speed is running — look at your taskbar.'
            UpdateHint = 'Run the same command again any time to update.'; Uninstalling = 'Uninstalling Agent Speed'
            Uninstalled = 'Removed'; NotInstalled = 'Agent Speed is not installed'; OldWindows = 'Windows 10 (1809) or later is required'
            DownloadFailed = 'Download failed'; NoChecksum = 'Checksum not found'; BadChecksum = 'Checksum mismatch, installation aborted'
            BadArchive = 'Unexpected package contents'
        }
        if ($culture -match '^zh-(TW|HK|MO)|^zh-Hant') {
            $t = @{
                Downloading = '下載最新版本'; Verifying = '驗證檔案完整性'; Installing = '安裝'
                Done = '安裝完成'; Launched = 'Agent Speed 已啟動，請看工作列。'
                UpdateHint = '之後重新執行同一行指令即可更新。'; Uninstalling = '移除 Agent Speed'
                Uninstalled = '已移除'; NotInstalled = '沒有找到已安裝的 Agent Speed'; OldWindows = '需要 Windows 10 (1809) 或更新版本'
                DownloadFailed = '下載失敗'; NoChecksum = '找不到校驗碼'; BadChecksum = '校驗碼不符，已中止安裝'
                BadArchive = '安裝包內容不正確'
            }
        }
        elseif ($culture -match '^zh') {
            $t = @{
                Downloading = '下载最新版本'; Verifying = '验证文件完整性'; Installing = '安装'
                Done = '安装完成'; Launched = 'Agent Speed 已启动，请查看任务栏。'
                UpdateHint = '之后重新运行同一行命令即可更新。'; Uninstalling = '卸载 Agent Speed'
                Uninstalled = '已卸载'; NotInstalled = '没有找到已安装的 Agent Speed'; OldWindows = '需要 Windows 10 (1809) 或更高版本'
                DownloadFailed = '下载失败'; NoChecksum = '找不到校验码'; BadChecksum = '校验码不匹配，已中止安装'
                BadArchive = '安装包内容不正确'
            }
        }
        elseif ($culture -match '^ja') {
            $t = @{
                Downloading = '最新バージョンをダウンロード'; Verifying = 'ファイルの整合性を確認'; Installing = 'インストール'
                Done = 'インストール完了'; Launched = 'Agent Speed を起動しました。タスクバーを確認してください。'
                UpdateHint = '同じコマンドを再実行すると更新できます。'; Uninstalling = 'Agent Speed をアンインストール'
                Uninstalled = '削除しました'; NotInstalled = 'インストールされた Agent Speed が見つかりません'
                OldWindows = 'Windows 10 (1809) 以降が必要です'; DownloadFailed = 'ダウンロードに失敗しました'
                NoChecksum = 'チェックサムが見つかりません'; BadChecksum = 'チェックサムが一致しないため中止しました'
                BadArchive = 'パッケージの内容が正しくありません'
            }
        }
        return $t
    }

    $T = Get-Text

    function Write-Title { Write-Host ''; Write-Host '◉ Agent Speed' -ForegroundColor Cyan; Write-Host '' }
    function Write-Step([string]$Text) { Write-Host '  › ' -ForegroundColor Cyan -NoNewline; Write-Host $Text }
    function Write-Done([string]$Text) { Write-Host ''; Write-Host '  ✓ ' -ForegroundColor Green -NoNewline; Write-Host $Text }
    function Write-Hint([string]$Text) { Write-Host "    $Text" -ForegroundColor DarkGray }
    function Stop-WithError([string]$Text) {
        Write-Host ''
        Write-Host "  ✗ $Text" -ForegroundColor Red
        Write-Host ''
        throw $Text
    }

    function Get-Architecture {
        $arch = $env:PROCESSOR_ARCHITEW6432
        if (-not $arch) { $arch = $env:PROCESSOR_ARCHITECTURE }
        if ($arch -eq 'ARM64') { return 'arm64' }
        return 'x64'
    }

    function Stop-AgentSpeed {
        $running = Get-Process -Name 'AgentSpeed' -ErrorAction SilentlyContinue
        if (-not $running) { return }
        foreach ($process in $running) {
            try { [void]$process.CloseMainWindow() } catch { }
        }
        $deadline = (Get-Date).AddSeconds(4)
        while ((Get-Process -Name 'AgentSpeed' -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 200
        }
        Get-Process -Name 'AgentSpeed' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 300
    }

    function Invoke-Download([string]$Url, [string]$Path) {
        for ($attempt = 1; $attempt -le 3; $attempt++) {
            try {
                Invoke-WebRequest -Uri $Url -OutFile $Path -UseBasicParsing -TimeoutSec 120
                return
            }
            catch {
                if ($attempt -eq 3) { Stop-WithError "$($T.DownloadFailed): $Url" }
                Start-Sleep -Seconds (2 * $attempt)
            }
        }
    }

    function Remove-AgentSpeed {
        Write-Title
        Write-Step $T.Uninstalling
        Stop-AgentSpeed
        $found = Test-Path $InstallDir
        Remove-ItemProperty -Path $RunKey -Name 'AgentSpeed' -ErrorAction SilentlyContinue
        Remove-Item -Path $StartMenuLink -Force -ErrorAction SilentlyContinue
        Remove-Item -Path $UninstallKey -Recurse -Force -ErrorAction SilentlyContinue
        if ($found) {
            Remove-Item -Path $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
        }
        if ($Purge) {
            Remove-Item -Path $SettingsDir -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -Path $LogsDir -Recurse -Force -ErrorAction SilentlyContinue
        }
        if ($found) { Write-Done $T.Uninstalled } else { Write-Done $T.NotInstalled }
        Write-Host ''
    }

    function Install-AgentSpeed {
        if ([Environment]::OSVersion.Version.Build -lt 17763) { Stop-WithError $T.OldWindows }
        try {
            [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        }
        catch { }

        $arch = Get-Architecture
        $asset = "AgentSpeed-Windows-$arch.zip"
        $base = $env:AGENT_SPEED_DOWNLOAD_BASE
        if (-not $base) {
            if ($Version) {
                if ($Version -notmatch '^v') { $Version = "v$Version" }
                if ($Version -notmatch '^v[0-9A-Za-z.\-]+$') { throw 'Invalid -Version' }
                $base = "https://github.com/$Repo/releases/download/$Version"
            }
            else {
                $base = "https://github.com/$Repo/releases/latest/download"
            }
        }

        $temp = Join-Path ([IO.Path]::GetTempPath()) ("agent-speed-" + [Guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $temp | Out-Null
        try {
            Write-Title
            Write-Step $T.Downloading
            $zip = Join-Path $temp $asset
            $sums = Join-Path $temp 'SHA256SUMS.txt'
            Invoke-Download "$base/$asset" $zip
            Invoke-Download "$base/SHA256SUMS.txt" $sums

            Write-Step $T.Verifying
            $expected = $null
            foreach ($line in Get-Content -Path $sums) {
                $parts = $line.Trim() -split '\s+', 2
                if ($parts.Count -eq 2 -and $parts[1].TrimStart('*') -eq $asset) { $expected = $parts[0].ToLowerInvariant(); break }
            }
            if (-not $expected) { Stop-WithError $T.NoChecksum }
            $actual = (Get-FileHash -Path $zip -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($expected -ne $actual) { Stop-WithError $T.BadChecksum }

            $unpacked = Join-Path $temp 'unpacked'
            Expand-Archive -Path $zip -DestinationPath $unpacked -Force
            $exe = Join-Path $unpacked $ExeName
            if (-not (Test-Path $exe)) { Stop-WithError $T.BadArchive }

            Write-Step $T.Installing
            Stop-AgentSpeed
            New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
            Copy-Item -Path (Join-Path $unpacked '*') -Destination $InstallDir -Recurse -Force
            $target = Join-Path $InstallDir $ExeName
            Get-ChildItem -Path $InstallDir -Recurse -File | Unblock-File -ErrorAction SilentlyContinue

            $uninstaller = Join-Path $InstallDir 'uninstall.ps1'
            $uninstallBody = @"
`$ErrorActionPreference = 'SilentlyContinue'
Get-Process -Name 'AgentSpeed' | Stop-Process -Force
Start-Sleep -Milliseconds 500
Remove-ItemProperty -Path '$RunKey' -Name 'AgentSpeed'
Remove-Item -Path '$StartMenuLink' -Force
Remove-Item -Path '$UninstallKey' -Recurse -Force
Set-Location `$env:TEMP
Remove-Item -Path '$InstallDir' -Recurse -Force
"@
            Set-Content -Path $uninstaller -Value $uninstallBody -Encoding UTF8

            $shell = New-Object -ComObject WScript.Shell
            $link = $shell.CreateShortcut($StartMenuLink)
            $link.TargetPath = $target
            $link.WorkingDirectory = $InstallDir
            $link.IconLocation = "$target,0"
            $link.Description = $AppName
            $link.Save()

            $fileVersion = (Get-Item $target).VersionInfo.ProductVersion
            if (-not $fileVersion) { $fileVersion = '1.0.0' }
            $fileVersion = ($fileVersion -split '\+')[0]
            New-Item -Path $UninstallKey -Force | Out-Null
            $props = @{
                DisplayName = $AppName
                DisplayVersion = $fileVersion
                Publisher = 'zoefix'
                DisplayIcon = "$target,0"
                InstallLocation = $InstallDir
                URLInfoAbout = "https://github.com/$Repo"
                UninstallString = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstaller`""
                NoModify = 1
                NoRepair = 1
            }
            foreach ($key in $props.Keys) {
                $type = if ($props[$key] -is [int]) { 'DWord' } else { 'String' }
                New-ItemProperty -Path $UninstallKey -Name $key -Value $props[$key] -PropertyType $type -Force | Out-Null
            }

            if (-not $NoAutostart) {
                New-ItemProperty -Path $RunKey -Name 'AgentSpeed' -Value "`"$target`"" -PropertyType String -Force | Out-Null
            }

            Write-Done "$($T.Done) $fileVersion → $InstallDir"
            if (-not $NoLaunch) {
                Start-Process -FilePath $target -WorkingDirectory $InstallDir
                Write-Hint $T.Launched
            }
            Write-Hint $T.UpdateHint
            Write-Host ''
        }
        finally {
            Remove-Item -Path $temp -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    if ($Uninstall) { Remove-AgentSpeed } else { Install-AgentSpeed }
}
