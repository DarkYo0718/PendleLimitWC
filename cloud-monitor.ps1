param(
    [Parameter(Mandatory = $true)]
    [string]$StatePath
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Get-RequiredEnvironmentValue([string]$Name) {
    $value = [Environment]::GetEnvironmentVariable($Name)
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "$Name is not configured."
    }
    return $value
}

function Get-Number($Object, [string]$Name, [double]$Fallback = 0) {
    if ($null -eq $Object -or $null -eq $Object.PSObject.Properties[$Name] -or $null -eq $Object.$Name) {
        return $Fallback
    }
    return [double]$Object.$Name
}

function Read-State([string]$Path) {
    if (Test-Path -LiteralPath $Path) {
        $state = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    }
    else {
        $state = [pscustomobject]@{}
    }

    $defaults = [ordered]@{
        hasWalletYtBaseline = $false
        lastWalletYtValueUsd = 0.0
        hasFixApyBaseline = $false
        lastFixApy = 0.0
        hasUnderlyingApyBaseline = $false
        lastUnderlyingApyWithRewards = 0.0
        walletYtHistory = @()
    }
    foreach ($entry in $defaults.GetEnumerator()) {
        if ($null -eq $state.PSObject.Properties[$entry.Key]) {
            $state | Add-Member -NotePropertyName $entry.Key -NotePropertyValue $entry.Value
        }
    }
    return $state
}

function Set-StateProperty($State, [string]$Name, $Value) {
    if ($null -eq $State.PSObject.Properties[$Name]) {
        $State | Add-Member -NotePropertyName $Name -NotePropertyValue $Value
    }
    else {
        $State.$Name = $Value
    }
}

function Send-Telegram([string]$Token, [string]$ChatId, [string]$Text) {
    $uri = "https://api.telegram.org/bot$Token/sendMessage"
    Invoke-RestMethod -Uri $uri -Method Post -ContentType "application/json; charset=utf-8" -Body (@{
        chat_id = $ChatId
        text = $Text
    } | ConvertTo-Json) | Out-Null
}

$chainId = [int](Get-RequiredEnvironmentValue "PENDLE_CHAIN_ID")
$marketAddress = (Get-RequiredEnvironmentValue "PENDLE_MARKET").ToLowerInvariant()
$walletAddress = (Get-RequiredEnvironmentValue "PENDLE_WALLET").ToLowerInvariant()
$telegramToken = Get-RequiredEnvironmentValue "TELEGRAM_BOT_TOKEN"
$telegramChatId = Get-RequiredEnvironmentValue "TELEGRAM_CHAT_ID"
$minimumUsdChange = [double](Get-RequiredEnvironmentValue "MIN_USD_CHANGE")
$minimumApyChange = [double](Get-RequiredEnvironmentValue "MIN_APY_CHANGE_PP")

$headers = @{ Accept = "application/json"; "User-Agent" = "PendleLimitWC-GitHub/1.0" }
$market = Invoke-RestMethod -Uri "https://api-v2.pendle.finance/core/v1/$chainId/markets/$marketAddress" -Headers $headers
$dashboard = Invoke-RestMethod -Uri "https://api-v2.pendle.finance/core/v1/dashboard/positions/database/$walletAddress`?filterUsd=0" -Headers $headers
$marketId = "$chainId-$marketAddress"
$position = @($dashboard.positions | Where-Object { [int]$_.chainId -eq $chainId } | ForEach-Object { $_.openPositions } | Where-Object { ([string]$_.marketId).ToLowerInvariant() -eq $marketId } | Select-Object -First 1)

$valueUsd = 0.0
$rawBalance = 0.0
if ($position.Count -gt 0 -and $null -ne $position[0].yt) {
    $valueUsd = [double]$position[0].yt.valuation
    $rawBalance = [double]$position[0].yt.balance
}

$ytDecimals = [int]$market.yt.decimals
$ytBalance = $rawBalance / [Math]::Pow(10, $ytDecimals)
$ytExclusiveRewardApy = [double](($market.underlyingRewardApyBreakdown | Where-Object { [bool]$_.ytExclusive } | Measure-Object -Property absoluteApy -Sum).Sum)
$underlyingApy = Get-Number $market "underlyingApy"
$underlyingApyWithRewards = $underlyingApy + $ytExclusiveRewardApy
$fixApy = Get-Number $market "impliedApy"
$now = Get-Date
$state = Read-State $StatePath

$notifications = [System.Collections.Generic.List[string]]::new()
if (-not [bool]$state.hasWalletYtBaseline) {
    Set-StateProperty $state "hasWalletYtBaseline" $true
    Set-StateProperty $state "lastWalletYtValueUsd" $valueUsd
}
else {
    $change = $valueUsd - [double]$state.lastWalletYtValueUsd
    if ([Math]::Abs($change) -ge $minimumUsdChange) {
        $notifications.Add("YT 價值 $('{0:N2}' -f $valueUsd) USD")
        $notifications.Add("變化 $('{0:+0.00;-0.00;0.00}' -f $change) USD")
        Set-StateProperty $state "lastWalletYtValueUsd" $valueUsd
    }
}

if (-not [bool]$state.hasFixApyBaseline) {
    Set-StateProperty $state "hasFixApyBaseline" $true
    Set-StateProperty $state "lastFixApy" $fixApy
}
else {
    $changePp = ($fixApy - [double]$state.lastFixApy) * 100
    if ([Math]::Abs($changePp) -ge $minimumApyChange) {
        $notifications.Add("FIX APY $('{0:N3}%' -f ($fixApy * 100))")
        $notifications.Add("變化 $('{0:+0.000;-0.000;0.000}pp' -f $changePp)")
        Set-StateProperty $state "lastFixApy" $fixApy
    }
}

if ($null -eq $state.PSObject.Properties["hasUnderlyingApyBaseline"] -or -not [bool]$state.hasUnderlyingApyBaseline) {
    Set-StateProperty $state "hasUnderlyingApyBaseline" $true
    Set-StateProperty $state "lastUnderlyingApyWithRewards" $underlyingApyWithRewards
}
else {
    $changePp = ($underlyingApyWithRewards - [double]$state.lastUnderlyingApyWithRewards) * 100
    if ([Math]::Abs($changePp) -ge $minimumApyChange) {
        $notifications.Add("Underlying+Rewards $('{0:N3}%' -f ($underlyingApyWithRewards * 100))")
        $notifications.Add("變化 $('{0:+0.000;-0.000;0.000}pp' -f $changePp)")
        Set-StateProperty $state "lastUnderlyingApyWithRewards" $underlyingApyWithRewards
    }
}

$history = @($state.walletYtHistory)
$history += [pscustomobject]@{
    timestamp = $now.ToUniversalTime().ToString("o")
    valueUsd = $valueUsd
    fixApy = $fixApy
    underlyingApy = $underlyingApy
    underlyingInterestApy = Get-Number $market "underlyingInterestApy"
    underlyingRewardApy = Get-Number $market "underlyingRewardApy"
    ytIncentiveRewardApy = $ytExclusiveRewardApy
    underlyingApyWithRewards = $underlyingApyWithRewards
    marketExpiry = ([datetime]$market.expiry).ToUniversalTime().ToString("o")
    ytBalance = $ytBalance
    ytSymbol = [string]$market.yt.symbol
    accountingAssetSymbol = [string]$market.accountingAsset.symbol
    accountingAssetPriceUsd = [double]$market.accountingAsset.price.usd
}
$state.walletYtHistory = @($history | Select-Object -Last 2016)
$state | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $StatePath -Encoding utf8

if ($notifications.Count -gt 0) {
    $lines = @("Pendle 變化通知") + $notifications + "時間 $($now.ToString('yyyy/MM/dd HH:mm:ss'))"
    Send-Telegram $telegramToken $telegramChatId ($lines -join "`n")
    Write-Host "Telegram notification sent."
}

Write-Host "YT $('{0:N6}' -f $ytBalance) | value $('{0:N2}' -f $valueUsd) USD | FIX $('{0:N3}%' -f ($fixApy * 100)) | U+R $('{0:N3}%' -f ($underlyingApyWithRewards * 100))"
