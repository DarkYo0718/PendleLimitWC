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

Open the VB.NET Bitmap chart viewer:

```powershell
.\Start-PendleChartViewer.vbs
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

Each refresh stores the wallet YT USD value and the market FIX APY
(`impliedApy`). The tray window draws the saved YT values as a Bitmap line chart
with automatic zoom on the visible data. Use the timeline slider below the chart
to inspect older points; drag it back to the far right to follow the latest data.

The `PendleChartViewer` VB.NET WinForms project reads the same
`.pendle-monitor-state.json` history file and draws the chart with Bitmap. Drag
left/right to pan, use the mouse wheel to zoom, move the mouse to inspect a point,
and double-click to return to the latest data. It includes separate YT value,
FIX APY, and Underlying APY + Rewards charts, a right-side change list, and a
system tray icon. Minimize or close the window to keep it running in the
lower-right tray; right-click the tray icon and choose `Exit` to stop it.

The VB.NET chart viewer also reads Telegram settings and thresholds from
`config.json`. The first loaded point only becomes the baseline; later updates
send a short Chinese Telegram notification when YT value changes by at least
`walletMonitor.minUsdChange` or FIX APY changes by at least
`walletMonitor.minFixApyChangePercentPoints`. FIX APY notifications are also
guarded so changes below `0.01pp` are ignored.

The VB.NET chart viewer also updates `.pendle-monitor-state.json` itself. It uses
`monitor.pollSeconds` as the live API polling interval, so it can refresh once per
minute without the PowerShell tray monitor running separately.

Underlying APY + Rewards is calculated from Pendle market data as
`underlyingApy + ytExclusive underlyingRewardApyBreakdown`. The raw state stores
`underlyingApy`, `underlyingInterestApy`, `underlyingRewardApy`, and
`ytIncentiveRewardApy` for each new point.

It also has a read-only limit-order advisor. The advisor reads Pendle's
incentivized range and your maker orders, then shows whether your active order is
inside the reward range. If there is no active order, or the order leaves/gets too
close to the range edge, it sends a Telegram suggestion. It does not sign,
cancel, or create orders.

Telegram notifications are now limited to YT value and FIX APY changes. Reward
Buy YT range checks are disabled. `minUsdChange` controls the YT value change
threshold, while `minFixApyChangePercentPoints` controls the FIX APY change
threshold in percentage points. The current default is `$1` for YT and `0.01pp`
for FIX APY. The first successful refresh only saves the baseline and does not
send a notification.

## GitHub cloud monitoring

`.github/workflows/pendle-monitor.yml` runs `cloud-monitor.ps1` every five
minutes and can also be started manually from the Actions page. The workflow:

- reads the wallet YT quantity and value from Pendle;
- records FIX APY and Underlying APY + Rewards;
- sends Telegram only when YT value changes by at least `$1`, or either APY
  changes by at least `0.01pp`;
- force-updates `state.json` on the dedicated `monitor-data` branch so monitor
  history does not create permanent commits on the source branch.

Repository Actions secrets required:

- `TELEGRAM_BOT_TOKEN`
- `TELEGRAM_CHAT_ID`

Set `monitor.remoteStateUrl` in local `config.json` to the Raw GitHub URL. When
this value is present, the WinForms viewer downloads cloud history and disables
its own API polling and Telegram sending, preventing duplicate notifications.
