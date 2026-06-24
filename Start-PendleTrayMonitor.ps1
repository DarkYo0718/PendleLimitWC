param(
    [string]$ConfigPath = ".\config.json"
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptDir "monitor-pendle-limit-orders.ps1")

if (-not [System.IO.Path]::IsPathRooted($ConfigPath)) {
    $ConfigPath = Join-Path $scriptDir $ConfigPath
}

$config = Read-Config $ConfigPath
$statePath = Join-Path (Split-Path -Parent (Resolve-Path -LiteralPath $ConfigPath)) ".pendle-monitor-state.json"
$script:isRefreshing = $false
$script:exitRequested = $false

function Add-LogLine {
    param([string]$Text)

    $line = "[{0}] {1}" -f (Get-Date).ToString("yyyy-MM-dd HH:mm:ss"), $Text
    $script:logBox.AppendText($line + [Environment]::NewLine)
    $script:logBox.SelectionStart = $script:logBox.TextLength
    $script:logBox.ScrollToCaret()
}

function Set-Status {
    param(
        [string]$Text,
        [System.Drawing.Color]$Color = [System.Drawing.Color]::FromArgb(30, 41, 59)
    )

    $script:statusLabel.Text = $Text
    $script:statusLabel.ForeColor = $Color
    $script:notifyIcon.Text = if ($Text.Length -gt 63) { $Text.Substring(0, 63) } else { $Text }
}

function Show-MonitorWindow {
    $script:form.Show()
    $script:form.WindowState = [System.Windows.Forms.FormWindowState]::Normal
    $script:form.Activate()
}

function Invoke-TrayRefresh {
    if ($script:isRefreshing) {
        return
    }

    $script:isRefreshing = $true
    $script:refreshButton.Enabled = $false

    try {
        Add-LogLine "Refreshing Pendle order book..."

        $orderBook = Get-PendleOrderBook $config
        $walletStatus = Get-WalletYtStatus $config
        $alerts = @(Get-Alerts $config $orderBook)
        $rewardBuyStatus = Get-RewardBuyRangeStatus -Config $config -OrderBook $orderBook
        $summary = Get-SummaryText -OrderBook $orderBook -WalletStatus $walletStatus
        $now = Get-Date
        $state = Read-State $statePath
        $walletChange = Get-WalletYtChange -Config $config -WalletStatus $walletStatus -State $state

        Add-LogLine ($summary -replace "`n", " | ")

        $botToken = Get-TelegramBotToken $config
        $chatId = Get-TelegramChatId $config

        $previousRangeKey = [string]$state.lastRewardBuyRangeKey
        $hasPreviousRange = -not [string]::IsNullOrWhiteSpace($previousRangeKey)
        $rangeChanged = $hasPreviousRange -and $rewardBuyStatus.RangeKey -ne $previousRangeKey

        if ($alerts.Count -gt 0) {
            $alertKey = ($alerts | ForEach-Object { $_.Message }) -join "|"
            $alertText = "Reward Buy YT $($rewardBuyStatus.RangeText) 未涵蓋 $($rewardBuyStatus.TargetText)"

            Set-Status "Not covered: $alertText" ([System.Drawing.Color]::FromArgb(185, 28, 28))
            Add-LogLine "Not covered: $alertText"

            if ($rangeChanged) {
                $message = @(
                    "Pendle 掛單獎勵提醒",
                    "Reward Buy YT: $($rewardBuyStatus.RangeText)",
                    "目標: $($rewardBuyStatus.TargetText)",
                    "狀態: 區間變更",
                    "時間: $($now.ToString("HH:mm:ss"))"
                ) -join "`n"

                Send-TelegramMessage -BotToken $botToken -ChatId $chatId -Text $message
                $script:notifyIcon.ShowBalloonTip(5000, "Pendle 掛單獎勵", $alertText, [System.Windows.Forms.ToolTipIcon]::Warning)
                Add-LogLine "Range change notification sent."
            }
            else {
                Add-LogLine "Range unchanged; notification skipped."
            }

            $state.lastAlertKey = $alertKey
            $state.lastAlertAt = $now.ToUniversalTime().ToString("o")
            $state.wasOutOfRange = $true
            $state.lastRewardBuyRangeKey = $rewardBuyStatus.RangeKey
        }
        else {
            Set-Status "In range. Last refresh: $($now.ToString("HH:mm:ss"))" ([System.Drawing.Color]::FromArgb(21, 128, 61))

            if ($rangeChanged) {
                $message = @(
                    "Pendle 掛單獎勵提醒",
                    "Reward Buy YT: $($rewardBuyStatus.RangeText)",
                    "目標: $($rewardBuyStatus.TargetText)",
                    "狀態: 區間變更",
                    "時間: $($now.ToString("HH:mm:ss"))"
                ) -join "`n"

                Send-TelegramMessage -BotToken $botToken -ChatId $chatId -Text $message
                $script:notifyIcon.ShowBalloonTip(5000, "Pendle 掛單獎勵", "Reward Buy YT 區間變更", [System.Windows.Forms.ToolTipIcon]::Info)
                Add-LogLine "Range change notification sent."
            }
            else {
                Add-LogLine "In range."
            }

            $state.lastAlertKey = ""
            $state.wasOutOfRange = $false
            $state.lastRewardBuyRangeKey = $rewardBuyStatus.RangeKey
        }

        if ($null -ne $walletStatus) {
            if (-not [bool]$state.hasWalletYtBaseline) {
                $state.hasWalletYtBaseline = $true
                $state.lastWalletYtValueUsd = $walletStatus.ValueUsd
                Add-LogLine "YT 價值基準已記錄: $(Format-Usd $walletStatus.ValueUsd)"
            }
            elseif ($null -ne $walletChange -and [bool]$walletChange.ShouldNotify) {
                $direction = if ($walletChange.ChangeUsd -ge 0) { "增加" } else { "減少" }
                $percentText = if ([double]::IsInfinity($walletChange.PercentChange)) {
                    "新持倉"
                }
                else {
                    "{0:N2}%" -f [math]::Abs($walletChange.PercentChange)
                }
                $message = @(
                    "Pendle YT 價值變動",
                    "目前: $(Format-Usd $walletChange.CurrentUsd)",
                    "$direction`: $(Format-Usd ([math]::Abs($walletChange.ChangeUsd))) ($percentText)",
                    "時間: $($now.ToString("HH:mm:ss"))"
                ) -join "`n"

                Send-TelegramMessage -BotToken $botToken -ChatId $chatId -Text $message
                $script:notifyIcon.ShowBalloonTip(5000, "Pendle YT 價值", "$direction $(Format-Usd ([math]::Abs($walletChange.ChangeUsd)))", [System.Windows.Forms.ToolTipIcon]::Info)
                Add-LogLine "YT 價值變動通知已送出。"
                $state.lastWalletYtValueUsd = $walletStatus.ValueUsd
            }
        }

        Write-State -Path $statePath -State $state
    }
    catch {
        Set-Status "Refresh failed: $($_.Exception.Message)" ([System.Drawing.Color]::FromArgb(185, 28, 28))
        Add-LogLine "ERROR: $($_.Exception.Message)"
        $script:notifyIcon.ShowBalloonTip(5000, "Pendle monitor error", $_.Exception.Message, [System.Windows.Forms.ToolTipIcon]::Error)
    }
    finally {
        $script:refreshButton.Enabled = $true
        $script:isRefreshing = $false
    }
}

$form = New-Object System.Windows.Forms.Form
$form.Text = "Pendle YT Limit Order Monitor"
$form.Size = New-Object System.Drawing.Size(760, 460)
$form.StartPosition = "CenterScreen"
$form.MinimumSize = New-Object System.Drawing.Size(620, 320)
$form.ShowInTaskbar = $true

$statusLabel = New-Object System.Windows.Forms.Label
$statusLabel.AutoSize = $false
$statusLabel.Dock = [System.Windows.Forms.DockStyle]::Top
$statusLabel.Height = 34
$statusLabel.Padding = New-Object System.Windows.Forms.Padding(10, 9, 10, 0)
$statusLabel.Text = "Starting..."
$form.Controls.Add($statusLabel)

$logBox = New-Object System.Windows.Forms.TextBox
$logBox.Multiline = $true
$logBox.ReadOnly = $true
$logBox.ScrollBars = [System.Windows.Forms.ScrollBars]::Vertical
$logBox.Dock = [System.Windows.Forms.DockStyle]::Fill
$logBox.Font = New-Object System.Drawing.Font("Consolas", 10)
$form.Controls.Add($logBox)

$buttonPanel = New-Object System.Windows.Forms.Panel
$buttonPanel.Dock = [System.Windows.Forms.DockStyle]::Bottom
$buttonPanel.Height = 46
$form.Controls.Add($buttonPanel)

$refreshButton = New-Object System.Windows.Forms.Button
$refreshButton.Text = "Refresh now"
$refreshButton.Width = 110
$refreshButton.Height = 28
$refreshButton.Left = 10
$refreshButton.Top = 9
$refreshButton.Add_Click({ Invoke-TrayRefresh })
$buttonPanel.Controls.Add($refreshButton)

$hideButton = New-Object System.Windows.Forms.Button
$hideButton.Text = "Hide to tray"
$hideButton.Width = 100
$hideButton.Height = 28
$hideButton.Left = 130
$hideButton.Top = 9
$hideButton.Add_Click({ $script:form.Hide() })
$buttonPanel.Controls.Add($hideButton)

$exitButton = New-Object System.Windows.Forms.Button
$exitButton.Text = "Exit"
$exitButton.Width = 80
$exitButton.Height = 28
$exitButton.Left = 240
$exitButton.Top = 9
$exitButton.Add_Click({
    $script:exitRequested = $true
    $script:timer.Stop()
    $script:notifyIcon.Visible = $false
    $script:form.Close()
})
$buttonPanel.Controls.Add($exitButton)

$contextMenu = New-Object System.Windows.Forms.ContextMenuStrip
$showItem = $contextMenu.Items.Add("Open")
$refreshItem = $contextMenu.Items.Add("Refresh now")
$contextMenu.Items.Add("-") | Out-Null
$exitItem = $contextMenu.Items.Add("Exit")
$showItem.Add_Click({ Show-MonitorWindow })
$refreshItem.Add_Click({ Invoke-TrayRefresh })
$exitItem.Add_Click({
    $script:exitRequested = $true
    $script:timer.Stop()
    $script:notifyIcon.Visible = $false
    $script:form.Close()
})

$notifyIcon = New-Object System.Windows.Forms.NotifyIcon
$notifyIcon.Icon = [System.Drawing.SystemIcons]::Information
$notifyIcon.Text = "Pendle monitor starting..."
$notifyIcon.Visible = $true
$notifyIcon.ContextMenuStrip = $contextMenu
$notifyIcon.Add_DoubleClick({ Show-MonitorWindow })

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = [Math]::Max(5, [int]$config.monitor.pollSeconds) * 1000
$timer.Add_Tick({ Invoke-TrayRefresh })

$form.Add_Resize({
    if ($script:form.WindowState -eq [System.Windows.Forms.FormWindowState]::Minimized) {
        $script:form.Hide()
        $script:notifyIcon.ShowBalloonTip(1500, "Pendle monitor", "Still running in the system tray.", [System.Windows.Forms.ToolTipIcon]::Info)
    }
})

$form.Add_FormClosing({
    param($sender, $eventArgs)

    if (-not $script:exitRequested) {
        $eventArgs.Cancel = $true
        $script:form.Hide()
        $script:notifyIcon.ShowBalloonTip(1500, "Pendle monitor", "Still running in the system tray. Right-click the icon and choose Exit to stop.", [System.Windows.Forms.ToolTipIcon]::Info)
    }
})

$script:form = $form
$script:statusLabel = $statusLabel
$script:logBox = $logBox
$script:refreshButton = $refreshButton
$script:notifyIcon = $notifyIcon
$script:timer = $timer

Add-LogLine "Monitor started. Poll interval: $($config.monitor.pollSeconds)s"
$timer.Start()
Invoke-TrayRefresh

[System.Windows.Forms.Application]::Run($form)
$notifyIcon.Dispose()
