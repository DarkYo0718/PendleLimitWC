# Pendle YT Limit Order Monitor

Monitors the public Pendle Core API for this Monad market:

`0x6f99cf00ee7290ae78a072bb6910ef72d1129fe7`

The script reads:

`https://api-v2.pendle.finance/core/v2/limit-orders/book/143`

and checks the configured YT limit-order implied APY ranges. Telegram is used only for notifications.

## Setup

1. Copy the example config:

```powershell
Copy-Item .\config.example.json .\config.json
```

2. Edit `config.json` and set the APY ranges you want to monitor.

`0.08`, `8`, and `"8%"` are all accepted as 8%.

3. Set Telegram secrets in the current PowerShell session, or put them in `config.json` under `telegram.botToken` and `telegram.chatId`.

```powershell
$env:TELEGRAM_BOT_TOKEN = "your-bot-token"
$env:TELEGRAM_CHAT_ID = "your-chat-id"
```

Environment variables take priority over `config.json`.

## Get Telegram Chat ID

Send any message to your bot first, then run:

```powershell
Invoke-RestMethod "https://api.telegram.org/bot$env:TELEGRAM_BOT_TOKEN/getUpdates"
```

Look for `message.chat.id`.

## Run

Test once:

```powershell
.\monitor-pendle-limit-orders.ps1 -Once
```

Run continuously:

```powershell
.\monitor-pendle-limit-orders.ps1
```

Run as a tray monitor:

```powershell
.\Start-PendleTrayMonitor.ps1
```

The tray monitor keeps running in the Windows notification area. Double-click the icon to open the refresh log, click `Hide to tray` or minimize the window to hide it again, and use the tray right-click menu to refresh or exit.

Run tray monitor without a visible PowerShell console:

```powershell
.\start-tray-monitor.vbs
```

## Checks

`incentiveLongYieldApy` checks the Reward Buy YT range, using Buy YT order-book levels where `incentiveQualifiedPySize` is greater than zero. This is the default enabled monitor.

`incentiveShortYieldApy` checks the Reward Sell YT range. It is disabled by default.

When the Reward Buy YT range changes, the monitor sends a short Chinese Telegram notification. It no longer sends notifications for out-of-range or recovery states.

`bestLongYieldApy` and `bestShortYieldApy` check the first visible order-book level only. They are disabled by default because reward-range monitoring should use the incentive checks above.

`anyLongYieldApy` and `anyShortYieldApy` can be enabled if you want alerts when any returned level is outside the configured range.

## Wallet YT value monitor

Set `walletMonitor.enabled` to `true` and enter the wallet address in
`walletMonitor.address`. The monitor reads the YT USD valuation for the configured
Monad market from Pendle's user positions API.

`minUsdChange` and `minPercentChange` are notification thresholds. A Telegram
message is sent when either threshold is reached. The first successful refresh
only saves a baseline and does not send a notification.
