Public Class ProfitCalculatorForm
    Inherits Form

    Private ReadOnly positionLabel As Label
    Private ReadOnly apyLabel As Label
    Private ReadOnly expiryLabel As Label
    Private ReadOnly resultLabel As Label
    Private ReadOnly projectionList As ListView
    Private ReadOnly projectionChart As ProfitChartCanvas
     Private ReadOnly projectionSplit As SplitContainer
     Private currentPoint As ChartPoint

     Public Sub New(point As ChartPoint)
          Text = "YT 到期收益試算"
          MinimumSize = New Size(960, 620)
          Size = New Size(1120, 720)
          BackColor = Color.FromArgb(248, 250, 252)

          Dim summary As New TableLayoutPanel With {
            .Dock = DockStyle.Top,
            .Height = 98,
            .BackColor = Color.White,
            .Padding = New Padding(14, 10, 14, 8),
            .ColumnCount = 2,
            .RowCount = 2
        }
          summary.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 48))
          summary.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 52))
          summary.RowStyles.Add(New RowStyle(SizeType.Percent, 50))
          summary.RowStyles.Add(New RowStyle(SizeType.Percent, 50))
          positionLabel = CreateSummaryLabel(True)
          apyLabel = CreateSummaryLabel(False)
          expiryLabel = CreateSummaryLabel(False)
          resultLabel = CreateSummaryLabel(True)
          resultLabel.ForeColor = Color.FromArgb(21, 128, 61)
          summary.Controls.Add(positionLabel, 0, 0)
          summary.Controls.Add(apyLabel, 0, 1)
          summary.Controls.Add(expiryLabel, 1, 0)
          summary.Controls.Add(resultLabel, 1, 1)
          Controls.Add(summary)

          projectionSplit = New SplitContainer With {
            .Dock = DockStyle.Fill,
            .FixedPanel = FixedPanel.Panel2
        }
          projectionChart = New ProfitChartCanvas With {.Dock = DockStyle.Fill}
          projectionSplit.Panel1.Controls.Add(projectionChart)
          projectionList = New ListView With {.Dock = DockStyle.Fill, .View = View.Details, .FullRowSelect = True, .GridLines = True, .HideSelection = False}
          projectionList.Columns.Add("日期")
          projectionList.Columns.Add("AUSD 收益")
          projectionList.Columns.Add("預估 USD")
          projectionSplit.Panel2.Controls.Add(projectionList)
          Controls.Add(projectionSplit)
          projectionSplit.BringToFront()
          AddHandler projectionList.Resize, Sub() ResizeProjectionColumns()
          AddHandler projectionSplit.Resize, Sub() AdjustSplitterDistance()

          AdjustSplitterDistance()
          UpdateLiveValues(point)
     End Sub

     Public Sub UpdateLiveValues(point As ChartPoint)
          If point Is Nothing Then Return
          currentPoint = point
          Recalculate()
     End Sub

     Private Sub Recalculate()
          Dim now = DateTime.Now
          Dim expiry = currentPoint.MarketExpiry
          Dim remainingDays = If(expiry = DateTime.MinValue, 0, Math.Max(0, CInt(Math.Ceiling((expiry - now).TotalDays))))
          Dim asset = If(String.IsNullOrWhiteSpace(currentPoint.AccountingAssetSymbol), "accounting asset", currentPoint.AccountingAssetSymbol)
          positionLabel.Text = $"持倉：{currentPoint.YtBalance:N6} {currentPoint.YtSymbol}  ｜ 現值 {FormatUsd(currentPoint.ValueUsd)}"
          apyLabel.Text = $"Underlying APY + Rewards：{currentPoint.UnderlyingApyWithRewards * 100.0:N3}%"
          expiryLabel.Text = If(expiry = DateTime.MinValue, "到期日：等待 API 資料", $"到期日：{expiry:yyyy/MM/dd}（剩餘 {remainingDays} 天，YT 歸零）")

          If expiry = DateTime.MinValue OrElse expiry <= now OrElse currentPoint.YtBalance <= 0 Then
               resultLabel.Text = "到期前預估可領：--"
               projectionList.Items.Clear()
               projectionChart.SetPoints(Array.Empty(Of MaturityProjectionPoint)())
               Return
          End If

          Dim points = BuildProjection(currentPoint, now, expiry)
          Dim finalPoint = points.Last()
          resultLabel.Text = $"到期前預估可領：{finalPoint.EarnedAssetAmount:N2} {asset}（約 {FormatUsd(finalPoint.EarnedAmount)}）"
          projectionList.BeginUpdate()
          projectionList.Items.Clear()
          For Each item In points
               Dim row As New ListViewItem(item.Date.ToString("yyyy/MM/dd"))
               row.SubItems.Add(item.EarnedAssetAmount.ToString("N2"))
               row.SubItems.Add(FormatUsd(item.EarnedAmount))
               projectionList.Items.Add(row)
          Next
          projectionList.EndUpdate()
          ResizeProjectionColumns()
          projectionChart.SetPoints(points)
     End Sub

     Private Shared Function BuildProjection(point As ChartPoint, startDate As DateTime, expiry As DateTime) As List(Of MaturityProjectionPoint)
          Dim result As New List(Of MaturityProjectionPoint)()
          Dim totalDays = Math.Max(1, CInt(Math.Ceiling((expiry - startDate).TotalDays)))
          Dim intervals = Math.Min(12, totalDays)
          Dim assetPrice = If(point.AccountingAssetPriceUsd > 0, point.AccountingAssetPriceUsd, 1.0)
          For i = 0 To intervals
               Dim elapsed = CInt(Math.Round(totalDays * i / CDbl(intervals)))
               Dim earnedAsset = point.YtBalance * point.UnderlyingApyWithRewards * elapsed / 365.0
               result.Add(New MaturityProjectionPoint With {
                .Date = If(i = intervals, expiry, startDate.AddDays(elapsed)),
                .DaysElapsed = elapsed,
                .EarnedAssetAmount = earnedAsset,
                .EarnedAmount = earnedAsset * assetPrice,
                .TotalAmount = earnedAsset * assetPrice
            })
          Next
          Return result
     End Function

     Private Sub ResizeProjectionColumns()
          If projectionList.ClientSize.Width <= 20 OrElse projectionList.Columns.Count <> 3 Then Return
          Dim available = projectionList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4
          projectionList.Columns(0).Width = CInt(available * 0.32)
          projectionList.Columns(1).Width = CInt(available * 0.34)
          projectionList.Columns(2).Width = available - projectionList.Columns(0).Width - projectionList.Columns(1).Width
     End Sub

     Private Sub AdjustSplitterDistance()
          If projectionSplit.ClientSize.Width <= 0 Then Return

          Dim minimum = Math.Max(1, projectionSplit.Panel1MinSize)
          Dim maximum = projectionSplit.ClientSize.Width - Math.Max(1, projectionSplit.Panel2MinSize) - projectionSplit.SplitterWidth
          If maximum < minimum Then Return

          Dim desired = projectionSplit.ClientSize.Width - 320 - projectionSplit.SplitterWidth
          Dim safeDistance = Math.Max(minimum, Math.Min(maximum, desired))
          If projectionSplit.SplitterDistance <> safeDistance Then
               projectionSplit.SplitterDistance = safeDistance
          End If
     End Sub

     Private Shared Function CreateSummaryLabel(bold As Boolean) As Label
          Return New Label With {
            .Dock = DockStyle.Fill,
            .AutoEllipsis = True,
            .TextAlign = ContentAlignment.MiddleLeft,
            .Font = New Font("Segoe UI" & If(bold, " Semibold", ""), If(bold, 10.5F, 9.5F))
        }
     End Function

     Private Shared Function FormatUsd(value As Double) As String
        Return "$" & value.ToString("N2")
    End Function
End Class
