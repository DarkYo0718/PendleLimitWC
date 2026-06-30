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
$script:timelineFollowLatest = $true
$script:timelineStartIndex = $null
$script:isUpdatingTimeline = $false

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

function Get-ChartWindowSize {
    param(
        [int]$Count,
        [int]$PlotWidth
    )

    if ($Count -le 0) {
        return 0
    }

    [Math]::Max(1, [Math]::Min(240, [Math]::Min($Count, [int]($PlotWidth * 0.9))))
}

function Set-TimelineControls {
    param(
        [int]$Count,
        [int]$WindowSize,
        [int]$StartIndex
    )

    if ($null -eq $script:timelineBar) {
        return
    }

    $script:isUpdatingTimeline = $true
    try {
        $maxStart = [Math]::Max(0, $Count - $WindowSize)
        $clampedValue = [Math]::Max(0, [Math]::Min($StartIndex, $maxStart))
        $script:timelineBar.Enabled = $maxStart -gt 0
        $script:timelineBar.Minimum = 0
        if ($script:timelineBar.Maximum -gt $maxStart -and $script:timelineBar.Value -gt $maxStart) {
            $script:timelineBar.Value = $maxStart
        }
        $script:timelineBar.Maximum = $maxStart
        $script:timelineBar.SmallChange = 1
        $script:timelineBar.LargeChange = [Math]::Max(1, [int]($WindowSize / 3))
        $script:timelineBar.TickFrequency = [Math]::Max(1, [int]($maxStart / 8))
        $script:timelineBar.Value = $clampedValue

        if ($null -ne $script:timelineLabel) {
            if ($Count -eq 0) {
                $script:timelineLabel.Text = "時間線 --"
            }
            elseif ($maxStart -eq 0) {
                $script:timelineLabel.Text = "時間線 全部"
            }
            elseif ($script:timelineFollowLatest) {
                $script:timelineLabel.Text = "時間線 最新"
            }
            else {
                $endIndex = [Math]::Min($Count, $StartIndex + $WindowSize)
                $script:timelineLabel.Text = "時間線 $($StartIndex + 1)-$endIndex / $Count"
            }
        }
    }
    finally {
        $script:isUpdatingTimeline = $false
    }
}

function Select-ChartItems {
    param(
        $History,
        [int]$PlotWidth
    )

    $items = @($History | Where-Object {
        $null -ne $_ -and $null -ne $_.valueUsd
    })
    if ($items.Count -le 1) {
        Set-TimelineControls -Count $items.Count -WindowSize $items.Count -StartIndex 0
        return $items
    }

    $windowSize = Get-ChartWindowSize -Count $items.Count -PlotWidth $PlotWidth
    $maxStart = [Math]::Max(0, $items.Count - $windowSize)
    if ($script:timelineFollowLatest -or $null -eq $script:timelineStartIndex) {
        $startIndex = $maxStart
    }
    else {
        $startIndex = [Math]::Max(0, [Math]::Min([int]$script:timelineStartIndex, $maxStart))
    }

    $script:timelineStartIndex = $startIndex
    Set-TimelineControls -Count $items.Count -WindowSize $windowSize -StartIndex $startIndex

    if ($items.Count -le $windowSize) {
        return $items
    }

    @($items | Select-Object -Skip $startIndex -First $windowSize)
}

function Render-YtValueChart {
    param($History)

    $width = $script:chartBox.ClientSize.Width
    $height = $script:chartBox.ClientSize.Height
    if ($width -lt 200 -or $height -lt 160) {
        return
    }

    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::FromArgb(248, 250, 252))

    $axisPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(148, 163, 184), 1)
    $gridPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(226, 232, 240), 1)
    $linePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(13, 148, 136), 2.5)
    $pointBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(15, 118, 110))
    $textBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(51, 65, 85))
    $mutedBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(100, 116, 139))
    $apyBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(194, 65, 12))
    $font = New-Object System.Drawing.Font("Segoe UI", 9)
    $titleFont = New-Object System.Drawing.Font("Segoe UI Semibold", 11)

    try {
        $left = 72
        $top = 42
        $right = 24
        $bottom = 42
        $plotWidth = [Math]::Max(1, $width - $left - $right)
        $plotHeight = [Math]::Max(1, $height - $top - $bottom)
        $rawItems = @($History)
        $items = @(Select-ChartItems -History $rawItems -PlotWidth $plotWidth)

        $graphics.DrawString("YT 價值走勢 (USD)", $titleFont, $textBrush, 14, 12)

        if ($items.Count -eq 0) {
            $graphics.DrawString("等待第一次刷新資料...", $font, $mutedBrush, $left, $top + 30)
        }
        else {
            $values = @($items | ForEach-Object { [double]$_.valueUsd })
            $minValue = [double](($values | Measure-Object -Minimum).Minimum)
            $maxValue = [double](($values | Measure-Object -Maximum).Maximum)
            $span = $maxValue - $minValue
            $minimumSpan = [Math]::Max(0.02, [Math]::Abs($maxValue) * 0.00002)
            if ($span -lt $minimumSpan) {
                $span = $minimumSpan
                $minValue -= $span / 2.0
                $maxValue += $span / 2.0
            }
            else {
                $padding = $span * 0.15
                $minValue -= $padding
                $maxValue += $padding
            }
            $span = $maxValue - $minValue

            for ($i = 0; $i -le 4; $i++) {
                $y = $top + ($plotHeight * $i / 4.0)
                $graphics.DrawLine($gridPen, $left, $y, $left + $plotWidth, $y)
                $labelValue = $maxValue - ($span * $i / 4.0)
                $label = "{0:N2}" -f $labelValue
                $graphics.DrawString($label, $font, $mutedBrush, 6, $y - 8)
            }

            $graphics.DrawLine($axisPen, $left, $top, $left, $top + $plotHeight)
            $graphics.DrawLine($axisPen, $left, $top + $plotHeight, $left + $plotWidth, $top + $plotHeight)

            $points = New-Object System.Collections.Generic.List[System.Drawing.PointF]
            for ($i = 0; $i -lt $items.Count; $i++) {
                $x = if ($items.Count -eq 1) {
                    $left + ($plotWidth / 2.0)
                }
                else {
                    $left + ($plotWidth * $i / ($items.Count - 1.0))
                }
                $value = [double]$items[$i].valueUsd
                $y = $top + (($maxValue - $value) / $span * $plotHeight)
                $points.Add((New-Object System.Drawing.PointF([single]$x, [single]$y)))
            }

            if ($points.Count -gt 1) {
                $graphics.DrawLines($linePen, $points.ToArray())
            }
            $lastPoint = $points[$points.Count - 1]
            $graphics.FillEllipse($pointBrush, $lastPoint.X - 4, $lastPoint.Y - 4, 8, 8)

            $firstTime = ([datetime]$items[0].timestamp).ToLocalTime().ToString("MM/dd HH:mm")
            $lastTime = ([datetime]$items[-1].timestamp).ToLocalTime().ToString("MM/dd HH:mm")
            $graphics.DrawString($firstTime, $font, $mutedBrush, $left, $top + $plotHeight + 12)
            $lastSize = $graphics.MeasureString($lastTime, $font)
            $graphics.DrawString($lastTime, $font, $mutedBrush, $left + $plotWidth - $lastSize.Width, $top + $plotHeight + 12)

            $lastValueText = "最新: $(Format-Usd ([double]$items[-1].valueUsd))"
            $valueSize = $graphics.MeasureString($lastValueText, $titleFont)
            $graphics.DrawString($lastValueText, $titleFont, $textBrush, $width - $right - $valueSize.Width, 12)

            $fixText = "FIX APY $(Format-Apy ([double]$items[-1].fixApy))"
            $fixSize = $graphics.MeasureString($fixText, $font)
            $graphics.DrawString($fixText, $font, $apyBrush, $width - $right - $fixSize.Width, $top + 5)

            if ($rawItems.Count -gt $items.Count) {
                $modeText = if ($script:timelineFollowLatest) { "最新" } else { "手動" }
                $zoomText = "Auto zoom: $modeText $($items.Count) / $($rawItems.Count) 點"
                $graphics.DrawString($zoomText, $font, $mutedBrush, 190, 14)
            }
        }
    }
    finally {
        $graphics.Dispose()
        $axisPen.Dispose()
        $gridPen.Dispose()
        $linePen.Dispose()
        $pointBrush.Dispose()
        $textBrush.Dispose()
        $mutedBrush.Dispose()
        $apyBrush.Dispose()
        $font.Dispose()
        $titleFont.Dispose()
    }

    $oldImage = $script:chartBox.Image
    $script:chartBox.Image = $bitmap
    if ($null -ne $oldImage) {
        $oldImage.Dispose()
    }
}

function Invoke-TrayRefresh {
    if ($script:isRefreshing) {
        return
    }

    $script:isRefreshing = $true
    $script:refreshButton.Enabled = $false

    try {
        Add-LogLine "正在刷新 YT 價值與 FIX APY..."

        $walletStatus = Get-WalletYtStatus $config
        $marketStatus = Get-PendleMarketStatus $config
        $now = Get-Date
        $state = Read-State $statePath
        $walletChange = Get-WalletYtChange -Config $config -WalletStatus $walletStatus -State $state
        $fixApyChange = Get-FixApyChange -Config $config -MarketStatus $marketStatus -State $state

        $script:ytValueLabel.Text = "YT 價值  $(Format-Usd $walletStatus.ValueUsd)"
        $script:fixApyLabel.Text = "FIX APY  $(Format-Apy $marketStatus.ImpliedApy)"
        Set-Status "最後刷新: $($now.ToString("HH:mm:ss"))" ([System.Drawing.Color]::FromArgb(21, 128, 61))
        Add-LogLine "YT $(Format-Usd $walletStatus.ValueUsd) | FIX APY $(Format-Apy $marketStatus.ImpliedApy)"

        $botToken = Get-TelegramBotToken $config
        $chatId = Get-TelegramChatId $config
        $notificationLines = @()
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
                $notificationLines += "YT 價值: $(Format-Usd $walletChange.CurrentUsd)"
                $notificationLines += "$direction $(Format-Usd ([math]::Abs($walletChange.ChangeUsd))) ($percentText)"
                $state.lastWalletYtValueUsd = $walletStatus.ValueUsd
            }
        }

        if (-not [bool]$state.hasFixApyBaseline) {
            $state.hasFixApyBaseline = $true
            $state.lastFixApy = $marketStatus.ImpliedApy
            Add-LogLine "FIX APY 基準已記錄: $(Format-Apy $marketStatus.ImpliedApy)"
        }
        elseif ($null -ne $fixApyChange -and [bool]$fixApyChange.ShouldNotify) {
            $notificationLines += "FIX APY: $(Format-Apy $fixApyChange.Current)"
            $notificationLines += "變化: $('{0:+0.000;-0.000;0.000}' -f $fixApyChange.ChangePercentagePoints) 個百分點"
            $state.lastFixApy = $marketStatus.ImpliedApy
        }

        if ($notificationLines.Count -gt 0) {
            $message = @("Pendle 變化通知") + $notificationLines + "時間: $($now.ToString("HH:mm:ss"))"
            Send-TelegramMessage -BotToken $botToken -ChatId $chatId -Text ($message -join "`n")
            $script:notifyIcon.ShowBalloonTip(4000, "Pendle 變化通知", ($notificationLines -join " | "), [System.Windows.Forms.ToolTipIcon]::Info)
            Add-LogLine "YT / FIX APY 變化通知已送出。"
        }

        Add-WalletYtHistoryPoint -Config $config -State $state -WalletStatus $walletStatus -MarketStatus $marketStatus -Timestamp $now
        Write-State -Path $statePath -State $state
        Render-YtValueChart $state.walletYtHistory
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
$form.Text = "Pendle YT Value Monitor"
$form.Size = New-Object System.Drawing.Size(880, 620)
$form.StartPosition = "CenterScreen"
$form.MinimumSize = New-Object System.Drawing.Size(700, 480)
$form.ShowInTaskbar = $true

$statusLabel = New-Object System.Windows.Forms.Label
$statusLabel.AutoSize = $false
$statusLabel.Dock = [System.Windows.Forms.DockStyle]::Top
$statusLabel.Height = 34
$statusLabel.Padding = New-Object System.Windows.Forms.Padding(10, 9, 10, 0)
$statusLabel.Text = "Starting..."
$form.Controls.Add($statusLabel)

$metricsPanel = New-Object System.Windows.Forms.Panel
$metricsPanel.Dock = [System.Windows.Forms.DockStyle]::Top
$metricsPanel.Height = 54
$metricsPanel.BackColor = [System.Drawing.Color]::White
$form.Controls.Add($metricsPanel)

$ytValueLabel = New-Object System.Windows.Forms.Label
$ytValueLabel.AutoSize = $false
$ytValueLabel.Left = 12
$ytValueLabel.Top = 10
$ytValueLabel.Width = 330
$ytValueLabel.Height = 34
$ytValueLabel.Font = New-Object System.Drawing.Font("Segoe UI Semibold", 16)
$ytValueLabel.ForeColor = [System.Drawing.Color]::FromArgb(15, 118, 110)
$ytValueLabel.Text = "YT 價值  --"
$metricsPanel.Controls.Add($ytValueLabel)

$fixApyLabel = New-Object System.Windows.Forms.Label
$fixApyLabel.AutoSize = $false
$fixApyLabel.Left = 360
$fixApyLabel.Top = 10
$fixApyLabel.Width = 300
$fixApyLabel.Height = 34
$fixApyLabel.Font = New-Object System.Drawing.Font("Segoe UI Semibold", 16)
$fixApyLabel.ForeColor = [System.Drawing.Color]::FromArgb(194, 65, 12)
$fixApyLabel.Text = "FIX APY  --"
$metricsPanel.Controls.Add($fixApyLabel)

$contentPanel = New-Object System.Windows.Forms.Panel
$contentPanel.Dock = [System.Windows.Forms.DockStyle]::Fill
$form.Controls.Add($contentPanel)

$chartPanel = New-Object System.Windows.Forms.Panel
$chartPanel.Dock = [System.Windows.Forms.DockStyle]::Fill
$contentPanel.Controls.Add($chartPanel)

$chartBox = New-Object System.Windows.Forms.PictureBox
$chartBox.Dock = [System.Windows.Forms.DockStyle]::Fill
$chartBox.BackColor = [System.Drawing.Color]::FromArgb(248, 250, 252)
$chartPanel.Controls.Add($chartBox)

$timelinePanel = New-Object System.Windows.Forms.Panel
$timelinePanel.Dock = [System.Windows.Forms.DockStyle]::Bottom
$timelinePanel.Height = 42
$timelinePanel.BackColor = [System.Drawing.Color]::White
$chartPanel.Controls.Add($timelinePanel)

$timelineLabel = New-Object System.Windows.Forms.Label
$timelineLabel.AutoSize = $false
$timelineLabel.Dock = [System.Windows.Forms.DockStyle]::Right
$timelineLabel.Width = 170
$timelineLabel.Padding = New-Object System.Windows.Forms.Padding(0, 12, 10, 0)
$timelineLabel.TextAlign = [System.Drawing.ContentAlignment]::TopRight
$timelineLabel.ForeColor = [System.Drawing.Color]::FromArgb(100, 116, 139)
$timelineLabel.Text = "時間線 --"
$timelinePanel.Controls.Add($timelineLabel)

$timelineBar = New-Object System.Windows.Forms.TrackBar
$timelineBar.Dock = [System.Windows.Forms.DockStyle]::Fill
$timelineBar.Minimum = 0
$timelineBar.Maximum = 0
$timelineBar.TickStyle = [System.Windows.Forms.TickStyle]::None
$timelineBar.Enabled = $false
$timelinePanel.Controls.Add($timelineBar)

$logBox = New-Object System.Windows.Forms.TextBox
$logBox.Multiline = $true
$logBox.ReadOnly = $true
$logBox.ScrollBars = [System.Windows.Forms.ScrollBars]::Vertical
$logBox.Dock = [System.Windows.Forms.DockStyle]::Bottom
$logBox.Height = 125
$logBox.Font = New-Object System.Drawing.Font("Consolas", 10)
$contentPanel.Controls.Add($logBox)

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
$script:ytValueLabel = $ytValueLabel
$script:fixApyLabel = $fixApyLabel
$script:chartBox = $chartBox
$script:timelineBar = $timelineBar
$script:timelineLabel = $timelineLabel
$script:logBox = $logBox
$script:refreshButton = $refreshButton
$script:notifyIcon = $notifyIcon
$script:timer = $timer

$timelineBar.Add_Scroll({
    if ($script:isUpdatingTimeline) {
        return
    }

    $script:timelineStartIndex = $script:timelineBar.Value
    $script:timelineFollowLatest = $script:timelineBar.Value -ge $script:timelineBar.Maximum
    try {
        $timelineState = Read-State $statePath
        Render-YtValueChart $timelineState.walletYtHistory
    }
    catch {
        Add-LogLine "ERROR: $($_.Exception.Message)"
    }
})

$chartBox.Add_SizeChanged({
    try {
        $resizeState = Read-State $statePath
        Render-YtValueChart $resizeState.walletYtHistory
    }
    catch {
    }
})

Add-LogLine "Monitor started. Poll interval: $($config.monitor.pollSeconds)s"
$timer.Start()
Invoke-TrayRefresh

[System.Windows.Forms.Application]::Run($form)
$notifyIcon.Dispose()
