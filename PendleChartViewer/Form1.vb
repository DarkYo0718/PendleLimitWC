Imports System.IO
Imports System.Net.Http

Public Class Form1
    Private ReadOnly ytChart As ChartCanvas
    Private ReadOnly apyChart As ChartCanvas
     Private ReadOnly underlyingChart As ChartCanvas
     Private ReadOnly statusLabel As Label
     Private ReadOnly rangeLabel As Label
     Private ReadOnly reloadButton As Button
     Private ReadOnly calculatorButton As Button
     Private ReadOnly eventList As ListView
     Private ReadOnly advisorLabel As Label
     Private ReadOnly refreshTimer As Timer
     Private ReadOnly notifyIcon As NotifyIcon
     Private ReadOnly statePath As String
     Private ReadOnly configPath As String
     Private appConfig As AppConfig
     Private exitRequested As Boolean = False
     Private lastEventKey As String = ""
     Private hasTelegramBaseline As Boolean = False
     Private lastTelegramValueUsd As Double = 0
     Private lastTelegramFixApy As Double = 0
     Private lastTelegramUnderlyingApyWithRewards As Double = 0
     Private lastAdvisorNotificationKey As String = ""
     Private isLoading As Boolean = False
     Private hasLoadedOnce As Boolean = False
     Private liveFetchInProgress As Boolean = False
     Private calculatorForm As ProfitCalculatorForm
     Private Shared ReadOnly RemoteClient As New HttpClient()

     Public Sub New()
          InitializeComponent()

          statePath = Path.GetFullPath(Path.Combine(Application.StartupPath, "..\..\..\..\.pendle-monitor-state.json"))
          If Not File.Exists(statePath) Then
               statePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, ".pendle-monitor-state.json"))
          End If
          configPath = Path.GetFullPath(Path.Combine(Application.StartupPath, "..\..\..\..\config.json"))
          If Not File.Exists(configPath) Then
               configPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "config.json"))
          End If
          appConfig = AppConfigLoader.Load(configPath)

          Text = "Pendle YT Bitmap Chart"
          MinimumSize = New Size(1000, 620)
          Size = New Size(1180, 760)

          Dim topPanel As New Panel With {
            .Dock = DockStyle.Top,
            .Height = 46,
            .BackColor = Color.White
        }
          Controls.Add(topPanel)

          statusLabel = New Label With {
            .AutoSize = False,
            .Dock = DockStyle.Fill,
            .Padding = New Padding(12, 13, 8, 0),
            .Text = "讀取中...",
            .ForeColor = Color.FromArgb(51, 65, 85)
        }
          topPanel.Controls.Add(statusLabel)

          reloadButton = New Button With {
            .Dock = DockStyle.Right,
            .Width = 92,
            .Text = "重新載入"
        }
          topPanel.Controls.Add(reloadButton)

          calculatorButton = New Button With {
            .Dock = DockStyle.Right,
            .Width = 96,
            .Text = "到期試算"
        }
          topPanel.Controls.Add(calculatorButton)

          rangeLabel = New Label With {
            .AutoSize = False,
            .Dock = DockStyle.Right,
            .Width = 220,
            .Padding = New Padding(0, 13, 12, 0),
            .TextAlign = ContentAlignment.TopRight,
            .ForeColor = Color.FromArgb(100, 116, 139)
        }
          topPanel.Controls.Add(rangeLabel)

          Dim rightPanel As New Panel With {
            .Dock = DockStyle.Right,
            .Width = 330,
            .BackColor = Color.White,
            .Padding = New Padding(8)
        }
          Controls.Add(rightPanel)

          Dim eventTitle As New Label With {
            .Dock = DockStyle.Top,
            .Height = 28,
            .Text = "交易 / 變化列表",
            .Font = New Font("Segoe UI Semibold", 10.0F),
            .ForeColor = Color.FromArgb(51, 65, 85)
        }
          rightPanel.Controls.Add(eventTitle)

          advisorLabel = New Label With {
            .Dock = DockStyle.Bottom,
            .Height = 132,
            .Padding = New Padding(8),
            .BackColor = Color.FromArgb(248, 250, 252),
            .ForeColor = Color.FromArgb(51, 65, 85),
            .Text = "掛單建議讀取中..."
        }
          rightPanel.Controls.Add(advisorLabel)

          eventList = New ListView With {
            .Dock = DockStyle.Fill,
            .View = View.Details,
            .FullRowSelect = True,
            .GridLines = False,
            .HideSelection = False
        }
          eventList.Columns.Add("時間", 82)
          eventList.Columns.Add("YT", 82)
          eventList.Columns.Add("FIX", 64)
          eventList.Columns.Add("U+R", 64)
          eventList.Columns.Add("變化", 74)
          AddHandler eventList.Resize, Sub() ResizeEventColumns()
          rightPanel.Controls.Add(eventList)
          eventList.BringToFront()

          Dim chartPanel As New TableLayoutPanel With {
            .Dock = DockStyle.Fill,
            .ColumnCount = 1,
            .RowCount = 3,
            .BackColor = Color.FromArgb(248, 250, 252)
        }
          chartPanel.RowStyles.Add(New RowStyle(SizeType.Percent, 42.0F))
          chartPanel.RowStyles.Add(New RowStyle(SizeType.Percent, 29.0F))
          chartPanel.RowStyles.Add(New RowStyle(SizeType.Percent, 29.0F))
          Controls.Add(chartPanel)
          chartPanel.BringToFront()

          ytChart = New ChartCanvas With {
            .Dock = DockStyle.Fill,
            .Metric = ChartMetric.YtValue
        }
          apyChart = New ChartCanvas With {
            .Dock = DockStyle.Fill,
            .Metric = ChartMetric.FixApy
        }
          underlyingChart = New ChartCanvas With {
            .Dock = DockStyle.Fill,
            .Metric = ChartMetric.UnderlyingApyWithRewards
        }
          chartPanel.Controls.Add(ytChart, 0, 0)
          chartPanel.Controls.Add(apyChart, 0, 1)
          chartPanel.Controls.Add(underlyingChart, 0, 2)

          Dim trayMenu As New ContextMenuStrip()
          Dim openItem = trayMenu.Items.Add("Open")
          Dim reloadItem = trayMenu.Items.Add("Reload")
          trayMenu.Items.Add("-")
          Dim exitItem = trayMenu.Items.Add("Exit")

          notifyIcon = New NotifyIcon With {
            .Icon = SystemIcons.Information,
            .Text = "Pendle chart viewer",
            .Visible = True,
            .ContextMenuStrip = trayMenu
        }

          AddHandler openItem.Click, Sub() ShowWindow()
          AddHandler reloadItem.Click, Async Sub() Await LoadDataAsync()
          AddHandler exitItem.Click, Sub() ExitApp()
          AddHandler notifyIcon.DoubleClick, Sub() ShowWindow()

          AddHandler reloadButton.Click, Async Sub() Await LoadDataAsync()
          AddHandler calculatorButton.Click, Sub() ShowProfitCalculator()
          AddHandler ytChart.ViewChanged, Sub(summary) rangeLabel.Text = summary

          refreshTimer = New Timer With {
            .Interval = 5000
        }
          AddHandler refreshTimer.Tick, Async Sub() Await LoadDataAsync(False)
          refreshTimer.Start()
     End Sub

     Protected Overrides Async Sub OnShown(e As EventArgs)
          MyBase.OnShown(e)
          Await LoadDataAsync()
     End Sub

     Protected Overrides Sub OnResize(e As EventArgs)
          MyBase.OnResize(e)
          If WindowState = FormWindowState.Minimized Then
               Hide()
               notifyIcon.ShowBalloonTip(1500, "Pendle 圖表", "已縮小到右下角。", ToolTipIcon.Info)
          End If
     End Sub

     Protected Overrides Sub OnFormClosing(e As FormClosingEventArgs)
          If Not exitRequested Then
               e.Cancel = True
               Hide()
               notifyIcon.ShowBalloonTip(1500, "Pendle 圖表", "仍在右下角執行，右鍵 Exit 才會關閉。", ToolTipIcon.Info)
               Return
          End If

          notifyIcon.Visible = False
          notifyIcon.Dispose()
          MyBase.OnFormClosing(e)
     End Sub

     Private Sub ShowWindow()
          Show()
          WindowState = FormWindowState.Normal
          Activate()
     End Sub

     Private Sub ExitApp()
          exitRequested = True
          refreshTimer.Stop()
          Close()
     End Sub

     Private Async Function LoadDataAsync(Optional showStatus As Boolean = True) As Task
          If isLoading Then
               Return
          End If

          isLoading = True
          Try
               appConfig = AppConfigLoader.Load(configPath)
               Await SyncRemoteStateAsync()
               Dim history = PendleStateLoader.LoadHistory(statePath)
               If String.IsNullOrWhiteSpace(appConfig.RemoteStateUrl) Then
                    Await FetchLivePointIfDueAsync(history)
               End If
               history = PendleStateLoader.LoadHistory(statePath)
               ytChart.SetPoints(history)
               apyChart.SetPoints(history)
               underlyingChart.SetPoints(history)
               UpdateEventList(history)
               Await UpdateAdvisorAsync()

               If history.Count = 0 Then
                    statusLabel.Text = $"找不到歷史資料: {statePath}"
                    notifyIcon.Text = "Pendle chart: no data"
               Else
                    Dim latest = history(history.Count - 1)
                    statusLabel.Text = $"最後資料 {latest.Timestamp:MM/dd HH:mm:ss} | YT {FormatUsd(latest.ValueUsd)} | FIX {FormatPercent(latest.FixApy)} | Underlying+R {FormatPercent(latest.UnderlyingApyWithRewards)}"
                    notifyIcon.Text = TruncateTrayText($"YT {FormatUsd(latest.ValueUsd)} | FIX {FormatPercent(latest.FixApy)}")
                    If calculatorForm IsNot Nothing AndAlso Not calculatorForm.IsDisposed Then
                         calculatorForm.UpdateLiveValues(latest)
                    End If

                    Dim currentEventKey = $"{latest.Timestamp:O}|{latest.ValueUsd:N6}|{latest.FixApy:N9}"
                    If lastEventKey <> "" AndAlso currentEventKey <> lastEventKey Then
                         'notifyIcon.ShowBalloonTip(2500, "Pendle 新資料", $"YT {FormatUsd(latest.ValueUsd)} | FIX {FormatPercent(latest.FixApy)}", ToolTipIcon.Info)
                         If String.IsNullOrWhiteSpace(appConfig.RemoteStateUrl) Then
                              Await NotifyTelegramIfNeededAsync(latest)
                         End If
                    End If
                    If lastEventKey = "" Then
                         SetTelegramBaseline(latest)
                    End If
                    lastEventKey = currentEventKey
               End If
               hasLoadedOnce = True
          Catch ex As Exception
               If showStatus Then
                    MessageBox.Show(ex.Message, "讀取失敗", MessageBoxButtons.OK, MessageBoxIcon.Error)
               End If
               statusLabel.Text = "讀取失敗: " & ex.Message
          Finally
               isLoading = False
          End Try
     End Function

     Private Async Function SyncRemoteStateAsync() As Task
          If String.IsNullOrWhiteSpace(appConfig.RemoteStateUrl) Then Return

          Dim separator = If(appConfig.RemoteStateUrl.Contains("?"), "&", "?")
          Dim requestUri = appConfig.RemoteStateUrl & separator & "t=" & DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()
          Using response = Await RemoteClient.GetAsync(requestUri)
               response.EnsureSuccessStatusCode()
               Dim json = Await response.Content.ReadAsStringAsync()
               Using document = System.Text.Json.JsonDocument.Parse(json)
                    Dim historyElement As System.Text.Json.JsonElement
                    If Not document.RootElement.TryGetProperty("walletYtHistory", historyElement) Then
                         Throw New InvalidDataException("GitHub state 尚未建立完整歷史資料。")
                    End If
               End Using
               File.WriteAllText(statePath, json)
          End Using
     End Function

     Private Sub ShowProfitCalculator()
          Dim history = PendleStateLoader.LoadHistory(statePath)
          Dim latest As ChartPoint = Nothing
          If history.Count > 0 Then
               latest = history(history.Count - 1)
          End If

          If calculatorForm Is Nothing OrElse calculatorForm.IsDisposed Then
               calculatorForm = New ProfitCalculatorForm(latest)
          ElseIf latest IsNot Nothing Then
               calculatorForm.UpdateLiveValues(latest)
          End If
          calculatorForm.Show()
          calculatorForm.WindowState = FormWindowState.Normal
          calculatorForm.Activate()
     End Sub

     Private Sub ResizeEventColumns()
          If eventList.ClientSize.Width <= 20 OrElse eventList.Columns.Count <> 5 Then Return
          Dim available = eventList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4
          Dim widths = {0.22, 0.22, 0.17, 0.17}
          Dim used = 0
          For i = 0 To widths.Length - 1
               eventList.Columns(i).Width = CInt(available * widths(i))
               used += eventList.Columns(i).Width
          Next
          eventList.Columns(4).Width = Math.Max(60, available - used)
     End Sub

     Private Async Function FetchLivePointIfDueAsync(history As List(Of ChartPoint)) As Task
          If liveFetchInProgress Then
               Return
          End If
          If String.IsNullOrWhiteSpace(appConfig.MarketAddress) OrElse String.IsNullOrWhiteSpace(appConfig.WalletAddress) Then
               Return
          End If

          Dim pollSeconds = Math.Max(10, appConfig.PollSeconds)
          If history.Count > 0 Then
               Dim latest = history(history.Count - 1)
               If (DateTime.Now - latest.Timestamp).TotalSeconds < pollSeconds Then
                    Return
               End If
          End If

          liveFetchInProgress = True
          Try
               Dim point = Await PendleApiClient.GetLiveChartPointAsync(appConfig)
               PendleStateStore.AppendHistoryPoint(statePath, point, appConfig.HistoryPoints)
          Finally
               liveFetchInProgress = False
          End Try
     End Function

     Private Sub UpdateEventList(history As List(Of ChartPoint))
          eventList.BeginUpdate()
          Try
               eventList.Items.Clear()

               Dim startIndex = Math.Max(0, history.Count - 80)
               For i = history.Count - 1 To startIndex Step -1
                    Dim point = history(i)
                    Dim previous As ChartPoint = If(i > 0, history(i - 1), Nothing)
                    Dim ytChange As Double = If(previous Is Nothing, 0, point.ValueUsd - previous.ValueUsd)
                    Dim apyChange As Double = If(previous Is Nothing, 0, (point.FixApy - previous.FixApy) * 100.0)

                    If previous IsNot Nothing AndAlso Math.Abs(ytChange) < 0.0001 AndAlso Math.Abs(apyChange) < 0.000001 Then
                         Continue For
                    End If

                    Dim item As New ListViewItem(point.Timestamp.ToString("HH:mm:ss"))
                    item.SubItems.Add(FormatUsd(point.ValueUsd))
                    item.SubItems.Add(FormatPercent(point.FixApy))
                    item.SubItems.Add(If(point.UnderlyingApyWithRewards > 0, FormatPercent(point.UnderlyingApyWithRewards), "--"))
                    item.SubItems.Add(FormatChange(ytChange, apyChange))
                    If ytChange > 0 Then
                         item.ForeColor = Color.FromArgb(21, 128, 61)
                    ElseIf ytChange < 0 Then
                         item.ForeColor = Color.FromArgb(185, 28, 28)
                    End If
                    eventList.Items.Add(item)
               Next
          Finally
               eventList.EndUpdate()
          End Try
     End Sub

     Private Async Function UpdateAdvisorAsync() As Task
          Try
               If String.IsNullOrWhiteSpace(appConfig.MarketAddress) OrElse String.IsNullOrWhiteSpace(appConfig.WalletAddress) Then
                    advisorLabel.Text = "掛單建議：config 缺 market 或 wallet"
                    Return
               End If

               Dim incentiveTask = PendleApiClient.GetIncentiveConfigAsync(appConfig)
               Dim ordersTask = PendleApiClient.GetMakerOrdersAsync(appConfig)
               Await Task.WhenAll(incentiveTask, ordersTask)

               Dim advisor = LimitOrderAdvisor.Evaluate(appConfig, incentiveTask.Result, ordersTask.Result)
               If Not advisor.HasData Then
                    advisorLabel.Text = "掛單建議：" & advisor.Status
                    Return
               End If

               advisorLabel.Text = String.Join(Environment.NewLine, {
                "掛單建議",
                $"Range {advisor.RangeText}",
                $"建議 APY {advisor.SuggestedApy * 100.0:N3}%",
                $"目前 {advisor.ActiveOrderText}",
                advisor.Status
            })

               If advisor.ShouldNotify AndAlso hasLoadedOnce AndAlso advisor.NotificationKey <> lastAdvisorNotificationKey Then
                    lastAdvisorNotificationKey = advisor.NotificationKey
                    notifyIcon.ShowBalloonTip(3000, "Pendle 掛單建議", advisor.Status, ToolTipIcon.Warning)
                    Await SendAdvisorTelegramAsync(advisor)
               ElseIf Not advisor.ShouldNotify Then
                    lastAdvisorNotificationKey = ""
               End If
          Catch ex As Exception
               advisorLabel.Text = "掛單建議失敗: " & ex.Message
          End Try
     End Function

     Private Async Function SendAdvisorTelegramAsync(advisor As AdvisorResult) As Task
          If Not appConfig.HasTelegram Then
               Return
          End If

          Dim message = String.Join(Environment.NewLine, {
            "Pendle 掛單建議",
            advisor.NotificationText
        })

          Try
               Await TelegramNotifier.SendAsync(appConfig, message)
          Catch ex As Exception
               statusLabel.Text = statusLabel.Text & " | 掛單TG失敗: " & ex.Message
          End Try
     End Function

     Private Shared Function FormatChange(ytChange As Double, apyChange As Double) As String
          Dim ytText = If(ytChange >= 0, "+", "") & "$" & ytChange.ToString("N2")
          Dim apyText = If(apyChange >= 0, "+", "") & apyChange.ToString("N3") & "pp"
          Return $"{ytText} / {apyText}"
     End Function

     Private Sub SetTelegramBaseline(point As ChartPoint)
          If hasTelegramBaseline Then
               Return
          End If

          hasTelegramBaseline = True
          lastTelegramValueUsd = point.ValueUsd
          lastTelegramFixApy = point.FixApy
          lastTelegramUnderlyingApyWithRewards = point.UnderlyingApyWithRewards
     End Sub

     Private Async Function NotifyTelegramIfNeededAsync(latest As ChartPoint) As Task
          If Not hasTelegramBaseline Then
               SetTelegramBaseline(latest)
               Return
          End If

          Dim ytChange = latest.ValueUsd - lastTelegramValueUsd
          Dim apyChangePercentPoints = (latest.FixApy - lastTelegramFixApy) * 100.0
          Dim underlyingChangePercentPoints = (latest.UnderlyingApyWithRewards - lastTelegramUnderlyingApyWithRewards) * 100.0
          Dim lines As New List(Of String)()

          If Math.Abs(ytChange) >= appConfig.MinUsdChange Then
               Dim direction = If(ytChange >= 0, "增加", "減少")
               lines.Add($"YT 價值 {FormatUsd(latest.ValueUsd)}")
               lines.Add($"{direction} {FormatUsd(Math.Abs(ytChange))}")
               lastTelegramValueUsd = latest.ValueUsd
          End If

          Dim minimumVisibleApyChange = Math.Max(appConfig.MinFixApyChangePercentPoints, 0.01)
          If Math.Abs(apyChangePercentPoints) >= minimumVisibleApyChange Then
               lines.Add($"FIX APY {FormatPercent(latest.FixApy)}")
               lines.Add($"變化 {FormatSignedPercentPoints(apyChangePercentPoints)}")
               lastTelegramFixApy = latest.FixApy
          End If

          If latest.UnderlyingApyWithRewards > 0 AndAlso Math.Abs(underlyingChangePercentPoints) >= minimumVisibleApyChange Then
               lines.Add($"Underlying+Reward {FormatPercent(latest.UnderlyingApyWithRewards)}")
               lines.Add($"變化 {FormatSignedPercentPoints(underlyingChangePercentPoints)}")
               lastTelegramUnderlyingApyWithRewards = latest.UnderlyingApyWithRewards
          End If

          If lines.Count = 0 Then
               Return
          End If

          If Not appConfig.HasTelegram Then
            statusLabel.Text = statusLabel.Text & " | TG 未設定"
            Return
        End If

        Dim messageLines As New List(Of String) From {
            "Pendle 變化通知"
        }
        messageLines.AddRange(lines)
        messageLines.Add($"時間 {latest.Timestamp:HH:mm:ss}")

        Try
            Await TelegramNotifier.SendAsync(appConfig, String.Join(Environment.NewLine, messageLines))
            statusLabel.Text = statusLabel.Text & " | TG 已送出"
        Catch ex As Exception
            statusLabel.Text = statusLabel.Text & " | TG 失敗: " & ex.Message
        End Try
    End Function

    Private Shared Function FormatUsd(value As Double) As String
        Return "$" & value.ToString("N2")
    End Function

    Private Shared Function FormatPercent(value As Double) As String
        Return $"{value * 100.0:N3}%"
    End Function

    Private Shared Function FormatSignedPercentPoints(value As Double) As String
        Return If(value >= 0, "+", "") & value.ToString("N3") & "pp"
    End Function

    Private Shared Function TruncateTrayText(text As String) As String
        If text.Length > 63 Then
            Return text.Substring(0, 63)
        End If
        Return text
    End Function
End Class
