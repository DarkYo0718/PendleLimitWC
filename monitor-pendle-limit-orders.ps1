param(
    [string]$ConfigPath = ".\config.json",
    [switch]$Once
)

$ErrorActionPreference = "Stop"

function Read-Config {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Config file not found: $Path. Copy config.example.json to config.json first."
    }

    Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Convert-ApyValue {
    param($Value)

    if ($null -eq $Value) {
        return $null
    }

    if ($Value -is [string]) {
        $text = $Value.Trim()
        if ($text.EndsWith("%")) {
            return [double]($text.TrimEnd("%")) / 100.0
        }
        return [double]$text
    }

    $number = [double]$Value
    if ([math]::Abs($number) -gt 1) {
        return $number / 100.0
    }
    return $number
}

function Format-Apy {
    param([double]$Value)
    "{0:N3}%" -f ($Value * 100.0)
}

function Format-Size {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return "0"
    }

    $size = [decimal]$Value
    if ($size -ge 1000000000000) {
        return "{0:N2}T" -f ($size / 1000000000000)
    }
    if ($size -ge 1000000000) {
        return "{0:N2}B" -f ($size / 1000000000)
    }
    if ($size -ge 1000000) {
        return "{0:N2}M" -f ($size / 1000000)
    }
    return "{0:N0}" -f $size
}

function Get-PendleOrderBook {
    param($Config)

    $pendle = $Config.pendle
    $baseUrl = "https://api-v2.pendle.finance/core/v2/limit-orders/book/$($pendle.chainId)"
    $query = @{
        market = $pendle.market
        precisionDecimal = $pendle.precisionDecimal
        limit = $pendle.limit
        includeAmm = [bool]$pendle.includeAmm
    }

    $queryString = ($query.GetEnumerator() | ForEach-Object {
        "{0}={1}" -f [uri]::EscapeDataString($_.Key), [uri]::EscapeDataString([string]$_.Value).ToLowerInvariant()
    }) -join "&"

    Invoke-RestMethod -Uri "$baseUrl`?$queryString" -Method Get -Headers @{
        Accept = "application/json"
        "User-Agent" = "PendleLimitWC/1.0"
    }
}

function Get-WalletYtStatus {
    param($Config)

    $wallet = $Config.walletMonitor
    if ($null -eq $wallet -or -not [bool]$wallet.enabled) {
        return $null
    }

    $address = [string]$wallet.address
    if ($address -notmatch "^0x[0-9a-fA-F]{40}$") {
        throw "walletMonitor.address is not a valid wallet address."
    }

    $chainId = [int]$Config.pendle.chainId
    $market = ([string]$Config.pendle.market).ToLowerInvariant()
    $marketId = "$chainId-$market"
    $uri = "https://api-v2.pendle.finance/core/v1/dashboard/positions/database/$address`?filterUsd=0"
    $response = Invoke-RestMethod -Uri $uri -Method Get -Headers @{
        Accept = "application/json"
        "User-Agent" = "PendleLimitWC/1.0"
    }

    $chainPositions = @($response.positions | Where-Object { [int]$_.chainId -eq $chainId })
    $position = @($chainPositions.openPositions | Where-Object {
        ([string]$_.marketId).ToLowerInvariant() -eq $marketId
    } | Select-Object -First 1)

    $valueUsd = 0.0
    $balance = "0"
    if ($position.Count -gt 0 -and $null -ne $position[0].yt) {
        $valueUsd = [double]$position[0].yt.valuation
        $balance = [string]$position[0].yt.balance
    }

    [pscustomobject]@{
        Address = $address
        MarketId = $marketId
        ValueUsd = $valueUsd
        Balance = $balance
    }
}

function Format-Usd {
    param([double]$Value)

    '$' + ('{0:N2}' -f $Value)
}

function Get-WalletYtChange {
    param(
        $Config,
        $WalletStatus,
        $State
    )

    if ($null -eq $WalletStatus -or -not [bool]$State.hasWalletYtBaseline) {
        return $null
    }

    $previous = [double]$State.lastWalletYtValueUsd
    $current = [double]$WalletStatus.ValueUsd
    $change = $current - $previous
    $absoluteChange = [math]::Abs($change)
    $percentChange = if ([math]::Abs($previous) -gt 0.0000001) {
        ($change / [math]::Abs($previous)) * 100.0
    }
    elseif ($absoluteChange -gt 0) {
        [double]::PositiveInfinity
    }
    else {
        0.0
    }

    $minUsdChange = if ($null -ne $Config.walletMonitor.minUsdChange) {
        [double]$Config.walletMonitor.minUsdChange
    }
    else {
        1.0
    }
    $minPercentChange = if ($null -ne $Config.walletMonitor.minPercentChange) {
        [double]$Config.walletMonitor.minPercentChange
    }
    else {
        1.0
    }

    [pscustomobject]@{
        PreviousUsd = $previous
        CurrentUsd = $current
        ChangeUsd = $change
        PercentChange = $percentChange
        ShouldNotify = $absoluteChange -ge $minUsdChange -or [math]::Abs($percentChange) -ge $minPercentChange
    }
}

function Test-Range {
    param(
        [string]$Name,
        [double[]]$Values,
        $Rule
    )

    if ($null -eq $Rule -or -not [bool]$Rule.enabled -or $Values.Count -eq 0) {
        return @()
    }

    $min = Convert-ApyValue $Rule.min
    $max = Convert-ApyValue $Rule.max
    $issues = @()

    foreach ($value in $Values) {
        if ($null -ne $min -and $value -lt $min) {
            $issues += [pscustomobject]@{
                Name = $Name
                Value = $value
                Message = "$Name below min: $(Format-Apy $value) < $(Format-Apy $min)"
            }
        }
        elseif ($null -ne $max -and $value -gt $max) {
            $issues += [pscustomobject]@{
                Name = $Name
                Value = $value
                Message = "$Name above max: $(Format-Apy $value) > $(Format-Apy $max)"
            }
        }
    }

    $issues
}

function Get-IncentiveQualifiedApyValues {
    param($Entries)

    @($Entries | Where-Object {
        $null -ne $_.incentiveQualifiedPySize -and [decimal]$_.incentiveQualifiedPySize -gt 0
    } | ForEach-Object {
        [double]$_.impliedApy
    })
}

function Format-ApyRange {
    param([double[]]$Values)

    if ($Values.Count -eq 0) {
        return "none"
    }

    $min = ($Values | Measure-Object -Minimum).Minimum
    $max = ($Values | Measure-Object -Maximum).Maximum
    if ($min -eq $max) {
        return Format-Apy $min
    }

    "$(Format-Apy $min) - $(Format-Apy $max)"
}

function Get-ConfiguredRangeText {
    param($Rule)

    "$(Format-Apy (Convert-ApyValue $Rule.min)) - $(Format-Apy (Convert-ApyValue $Rule.max))"
}

function Get-RewardBuyRangeStatus {
    param(
        $Config,
        $OrderBook
    )

    $rule = $Config.monitor.checks.incentiveLongYieldApy
    $values = Get-IncentiveQualifiedApyValues @($OrderBook.longYieldEntries)
    $rangeText = Format-ApyRange $values
    $targetText = Get-ConfiguredRangeText $rule
    $alerts = @()
    $coversTarget = $false

    if ($values.Count -eq 0) {
        $alerts += [pscustomobject]@{
            Name = "incentiveLongYieldApy"
            Value = $null
            Message = "Reward Buy YT no reward range"
        }
    }
    else {
        $rewardMin = ($values | Measure-Object -Minimum).Minimum
        $rewardMax = ($values | Measure-Object -Maximum).Maximum
        $targetMin = Convert-ApyValue $rule.min
        $targetMax = Convert-ApyValue $rule.max
        $coversTarget = $rewardMin -le $targetMin -and $rewardMax -ge $targetMax

        if (-not $coversTarget) {
            $alerts += [pscustomobject]@{
                Name = "incentiveLongYieldApy"
                Value = $null
                Message = "Reward Buy YT does not cover target range: $rangeText / target $targetText"
            }
        }
    }

    [pscustomobject]@{
        Values = $values
        RangeText = $rangeText
        RangeKey = $rangeText
        TargetText = $targetText
        CoversTarget = $coversTarget
        Alerts = @($alerts)
    }
}

function Get-Alerts {
    param(
        $Config,
        $OrderBook
    )

    $longEntries = @($OrderBook.longYieldEntries)
    $shortEntries = @($OrderBook.shortYieldEntries)
    $checks = $Config.monitor.checks
    $alerts = @()

    if ($longEntries.Count -gt 0) {
        $alerts += Test-Range "bestLongYieldApy" @([double]$longEntries[0].impliedApy) $checks.bestLongYieldApy
        $alerts += Test-Range "anyLongYieldApy" @($longEntries | ForEach-Object { [double]$_.impliedApy }) $checks.anyLongYieldApy
        if ($null -ne $checks.incentiveLongYieldApy -and [bool]$checks.incentiveLongYieldApy.enabled) {
            $rewardStatus = Get-RewardBuyRangeStatus -Config $Config -OrderBook $OrderBook
            $alerts += $rewardStatus.Alerts
        }
    }

    if ($shortEntries.Count -gt 0) {
        $alerts += Test-Range "bestShortYieldApy" @([double]$shortEntries[0].impliedApy) $checks.bestShortYieldApy
        $alerts += Test-Range "anyShortYieldApy" @($shortEntries | ForEach-Object { [double]$_.impliedApy }) $checks.anyShortYieldApy
        $alerts += Test-Range "incentiveShortYieldApy" (Get-IncentiveQualifiedApyValues $shortEntries) $checks.incentiveShortYieldApy
    }

    $alerts
}

function Get-SummaryText {
    param(
        $OrderBook,
        $WalletStatus
    )

    $longEntries = @($OrderBook.longYieldEntries)
    $lines = @()

    $longIncentiveValues = Get-IncentiveQualifiedApyValues $longEntries
    $lines += "Reward Buy YT range: $(Format-ApyRange $longIncentiveValues)"
    if ($null -ne $WalletStatus) {
        $lines += "Wallet YT value: $(Format-Usd $WalletStatus.ValueUsd)"
    }

    $lines -join "`n"
}

function Send-TelegramMessage {
    param(
        [string]$BotToken,
        [string]$ChatId,
        [string]$Text
    )

    if ([string]::IsNullOrWhiteSpace($BotToken)) {
        throw "TELEGRAM_BOT_TOKEN is not set."
    }
    if ([string]::IsNullOrWhiteSpace($ChatId) -or $ChatId -eq "PUT_YOUR_CHAT_ID_HERE") {
        throw "Telegram chatId is not configured."
    }

    $uri = "https://api.telegram.org/bot$BotToken/sendMessage"
    $json = @{
        chat_id = $ChatId
        text = $Text
        disable_web_page_preview = $true
    } | ConvertTo-Json -Compress
    $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    Invoke-RestMethod -Uri $uri -Method Post -ContentType "application/json; charset=utf-8" -Body $bodyBytes | Out-Null
}

function Get-TelegramBotToken {
    param($Config)

    if (-not [string]::IsNullOrWhiteSpace($env:TELEGRAM_BOT_TOKEN)) {
        return $env:TELEGRAM_BOT_TOKEN
    }

    if ($null -ne $Config.telegram -and -not [string]::IsNullOrWhiteSpace([string]$Config.telegram.botToken)) {
        return [string]$Config.telegram.botToken
    }

    return ""
}

function Get-TelegramChatId {
    param($Config)

    if (-not [string]::IsNullOrWhiteSpace($env:TELEGRAM_CHAT_ID)) {
        return $env:TELEGRAM_CHAT_ID
    }

    if ($null -ne $Config.telegram -and -not [string]::IsNullOrWhiteSpace([string]$Config.telegram.chatId)) {
        return [string]$Config.telegram.chatId
    }

    return ""
}

function Read-State {
    param([string]$Path)

    if (Test-Path -LiteralPath $Path) {
        $state = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
        if ($null -eq $state.PSObject.Properties["lastAlertKey"]) {
            $state | Add-Member -NotePropertyName lastAlertKey -NotePropertyValue ""
        }
        if ($null -eq $state.PSObject.Properties["lastAlertAt"]) {
            $state | Add-Member -NotePropertyName lastAlertAt -NotePropertyValue "1970-01-01T00:00:00Z"
        }
        if ($null -eq $state.PSObject.Properties["wasOutOfRange"]) {
            $state | Add-Member -NotePropertyName wasOutOfRange -NotePropertyValue $false
        }
        if ($null -eq $state.PSObject.Properties["lastRewardBuyRangeKey"]) {
            $state | Add-Member -NotePropertyName lastRewardBuyRangeKey -NotePropertyValue ""
        }
        if ($null -eq $state.PSObject.Properties["hasWalletYtBaseline"]) {
            $state | Add-Member -NotePropertyName hasWalletYtBaseline -NotePropertyValue $false
        }
        if ($null -eq $state.PSObject.Properties["lastWalletYtValueUsd"]) {
            $state | Add-Member -NotePropertyName lastWalletYtValueUsd -NotePropertyValue 0.0
        }
        return $state
    }

    [pscustomobject]@{
        lastAlertKey = ""
        lastAlertAt = "1970-01-01T00:00:00Z"
        wasOutOfRange = $false
        lastRewardBuyRangeKey = ""
        hasWalletYtBaseline = $false
        lastWalletYtValueUsd = 0.0
    }
}

function Write-State {
    param(
        [string]$Path,
        $State
    )

    $State | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Invoke-MonitorOnce {
    param(
        $Config,
        [string]$StatePath
    )

    $orderBook = Get-PendleOrderBook $Config
    $walletStatus = Get-WalletYtStatus $Config
    $alerts = @(Get-Alerts $Config $orderBook)
    $rewardBuyStatus = Get-RewardBuyRangeStatus -Config $Config -OrderBook $orderBook
    $summary = Get-SummaryText -OrderBook $orderBook -WalletStatus $walletStatus
    $now = Get-Date
    $state = Read-State $StatePath
    $walletChange = Get-WalletYtChange -Config $Config -WalletStatus $walletStatus -State $state

    Write-Host "[$($now.ToString("yyyy-MM-dd HH:mm:ss"))]"
    Write-Host $summary

    $botToken = Get-TelegramBotToken $Config
    $chatId = Get-TelegramChatId $Config

    $previousRangeKey = [string]$state.lastRewardBuyRangeKey
    $hasPreviousRange = -not [string]::IsNullOrWhiteSpace($previousRangeKey)
    $rangeChanged = $hasPreviousRange -and $rewardBuyStatus.RangeKey -ne $previousRangeKey

    if ($alerts.Count -gt 0) {
        $alertKey = ($alerts | ForEach-Object { $_.Message }) -join "|"

        if ($rangeChanged) {
            $message = @(
                "Pendle 掛單獎勵提醒",
                "Reward Buy YT: $($rewardBuyStatus.RangeText)",
                "目標: $($rewardBuyStatus.TargetText)",
                "狀態: 區間變更",
                "時間: $($now.ToString("HH:mm:ss"))"
            ) -join "`n"

            Send-TelegramMessage -BotToken $botToken -ChatId $chatId -Text $message
            Write-Host "Range change notification sent."
        }
        else {
            Write-Host "Range unchanged; notification skipped."
        }

        $state.lastAlertKey = $alertKey
        $state.lastAlertAt = $now.ToUniversalTime().ToString("o")
        $state.wasOutOfRange = $true
        $state.lastRewardBuyRangeKey = $rewardBuyStatus.RangeKey
    }
    else {
        if ($rangeChanged) {
            $message = @(
                "Pendle 掛單獎勵提醒",
                "Reward Buy YT: $($rewardBuyStatus.RangeText)",
                "目標: $($rewardBuyStatus.TargetText)",
                "狀態: 區間變更",
                "時間: $($now.ToString("HH:mm:ss"))"
            ) -join "`n"

            Send-TelegramMessage -BotToken $botToken -ChatId $chatId -Text $message
            Write-Host "Range change notification sent."
        }
        else {
            Write-Host "In range."
        }

        $state.lastAlertKey = ""
        $state.wasOutOfRange = $false
        $state.lastRewardBuyRangeKey = $rewardBuyStatus.RangeKey
    }

    if ($null -ne $walletStatus) {
        if (-not [bool]$state.hasWalletYtBaseline) {
            $state.hasWalletYtBaseline = $true
            $state.lastWalletYtValueUsd = $walletStatus.ValueUsd
            Write-Host "Wallet YT baseline saved: $(Format-Usd $walletStatus.ValueUsd)"
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
            Write-Host "Wallet YT value notification sent."
            $state.lastWalletYtValueUsd = $walletStatus.ValueUsd
        }
    }

    Write-State -Path $StatePath -State $state
}

if ($MyInvocation.InvocationName -ne ".") {
    $config = Read-Config $ConfigPath
    $statePath = Join-Path (Split-Path -Parent (Resolve-Path -LiteralPath $ConfigPath)) ".pendle-monitor-state.json"

    do {
        try {
            Invoke-MonitorOnce -Config $config -StatePath $statePath
        }
        catch {
            Write-Error $_
        }

        if ($Once) {
            break
        }

        Start-Sleep -Seconds ([int]$config.monitor.pollSeconds)
    } while ($true)
}
