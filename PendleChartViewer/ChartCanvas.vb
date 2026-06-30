Imports System.Drawing.Drawing2D
Imports System.ComponentModel

Public Enum ChartMetric
    YtValue
    FixApy
    UnderlyingApyWithRewards
End Enum

Public Class ChartCanvas
    Inherits Control

    Private ReadOnly points As New List(Of ChartPoint)()
    Private viewStart As Integer = 0
    Private viewCount As Integer = 240
    Private followLatest As Boolean = True
    Private isDragging As Boolean = False
    Private lastMouseX As Integer = 0
    Private hoveredIndex As Integer = -1

    <DefaultValue(ChartMetric.YtValue)>
    <DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)>
    Public Property Metric As ChartMetric = ChartMetric.YtValue
    Public Event ViewChanged(summary As String)

    Public Sub New()
        DoubleBuffered = True
        BackColor = Color.FromArgb(248, 250, 252)
        SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.UserPaint Or ControlStyles.ResizeRedraw, True)
    End Sub

    Public Sub SetPoints(newPoints As IEnumerable(Of ChartPoint))
        points.Clear()
        points.AddRange(newPoints)

        If points.Count = 0 Then
            viewStart = 0
            viewCount = 240
        Else
            viewCount = Math.Max(10, Math.Min(viewCount, points.Count))
            If followLatest Then
                viewStart = Math.Max(0, points.Count - viewCount)
            Else
                ClampView()
            End If
        End If

        RaiseViewChanged()
        Invalidate()
    End Sub

    Public Sub ResetToLatest()
        followLatest = True
        If points.Count > 0 Then
            viewCount = Math.Min(240, points.Count)
            viewStart = Math.Max(0, points.Count - viewCount)
        End If
        RaiseViewChanged()
        Invalidate()
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)

        If ClientSize.Width <= 0 OrElse ClientSize.Height <= 0 Then
            Return
        End If

        Using bitmap As New Bitmap(ClientSize.Width, ClientSize.Height)
            Using g = Graphics.FromImage(bitmap)
                g.SmoothingMode = SmoothingMode.AntiAlias
                g.Clear(BackColor)
                DrawChart(g)
            End Using
            e.Graphics.DrawImageUnscaled(bitmap, 0, 0)
        End Using
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        MyBase.OnMouseDown(e)
        If e.Button = MouseButtons.Left Then
            isDragging = True
            lastMouseX = e.X
            Cursor = Cursors.SizeWE
        End If
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)

        If isDragging AndAlso points.Count > viewCount Then
            Dim plot = GetPlotRect()
            If plot.Width > 0 Then
                Dim dx = e.X - lastMouseX
                Dim pointsPerPixel = viewCount / CDbl(plot.Width)
                Dim deltaIndex = CInt(Math.Round(-dx * pointsPerPixel))
                If deltaIndex <> 0 Then
                    followLatest = False
                    viewStart += deltaIndex
                    ClampView()
                    lastMouseX = e.X
                    RaiseViewChanged()
                    Invalidate()
                End If
            End If
        Else
            hoveredIndex = HitTestPoint(e.X)
            Invalidate()
        End If
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        MyBase.OnMouseUp(e)
        isDragging = False
        Cursor = Cursors.Default
        followLatest = points.Count > 0 AndAlso viewStart + viewCount >= points.Count
        RaiseViewChanged()
    End Sub

    Protected Overrides Sub OnMouseWheel(e As MouseEventArgs)
        MyBase.OnMouseWheel(e)
        If points.Count <= 1 Then
            Return
        End If

        Dim oldCount = viewCount
        Dim factor = If(e.Delta > 0, 0.8, 1.25)
        Dim newCount = CInt(Math.Round(viewCount * factor))
        newCount = Math.Max(10, Math.Min(points.Count, newCount))
        If newCount = oldCount Then
            Return
        End If

        Dim plot = GetPlotRect()
        Dim anchorRatio As Double = 0.5
        If plot.Width > 0 Then
            anchorRatio = Math.Max(0, Math.Min(1, (e.X - plot.Left) / CDbl(plot.Width)))
        End If

        Dim anchorIndex = viewStart + CInt(Math.Round(oldCount * anchorRatio))
        viewCount = newCount
        viewStart = anchorIndex - CInt(Math.Round(viewCount * anchorRatio))
        followLatest = False
        ClampView()
        RaiseViewChanged()
        Invalidate()
    End Sub

    Protected Overrides Sub OnDoubleClick(e As EventArgs)
        MyBase.OnDoubleClick(e)
        ResetToLatest()
    End Sub

    Private Sub DrawChart(g As Graphics)
        Dim plot = GetPlotRect()
        Using titleFont As New Font("Segoe UI Semibold", 10.5F),
              font As New Font("Segoe UI", 9.0F),
              textBrush As New SolidBrush(Color.FromArgb(51, 65, 85)),
              mutedBrush As New SolidBrush(Color.FromArgb(100, 116, 139)),
              gridPen As New Pen(Color.FromArgb(226, 232, 240)),
              axisPen As New Pen(Color.FromArgb(148, 163, 184)),
              linePen As New Pen(GetLineColor(), 2.4F),
              pointBrush As New SolidBrush(GetLineColor())

            g.DrawString(GetTitle(), titleFont, textBrush, 14, 10)

            If points.Count = 0 Then
                g.DrawString("等待資料檔...", font, mutedBrush, plot.Left, plot.Top + 24)
                Return
            End If

            Dim visible = GetVisiblePoints().Where(Function(p) HasMetricValue(p)).ToList()
            If visible.Count = 0 Then
                g.DrawString("等待新版 APY 資料...", font, mutedBrush, plot.Left, plot.Top + 24)
                Return
            End If
            Dim values = visible.Select(Function(p) GetMetricValue(p)).ToList()
            Dim minValue = values.Min()
            Dim maxValue = values.Max()
            Dim span = maxValue - minValue
            Dim minimumSpan = GetMinimumSpan(maxValue)
            If span < minimumSpan Then
                span = minimumSpan
                minValue -= span / 2.0
                maxValue += span / 2.0
            Else
                Dim padding = span * 0.15
                minValue -= padding
                maxValue += padding
            End If
            span = maxValue - minValue

            For i = 0 To 4
                Dim y = CSng(plot.Top + plot.Height * i / 4.0)
                g.DrawLine(gridPen, plot.Left, y, plot.Right, y)
                Dim labelValue = maxValue - span * i / 4.0
                g.DrawString(FormatAxisValue(labelValue), font, mutedBrush, 6, y - 8)
            Next

            g.DrawLine(axisPen, plot.Left, plot.Top, plot.Left, plot.Bottom)
            g.DrawLine(axisPen, plot.Left, plot.Bottom, plot.Right, plot.Bottom)

            Dim chartPoints As New List(Of PointF)()
            For i = 0 To visible.Count - 1
                Dim x = If(visible.Count = 1,
                           plot.Left + plot.Width / 2.0F,
                           CSng(plot.Left + plot.Width * i / CDbl(visible.Count - 1)))
                Dim y = CSng(plot.Top + (maxValue - values(i)) / span * plot.Height)
                chartPoints.Add(New PointF(x, y))
            Next

            If chartPoints.Count > 1 Then
                g.DrawLines(linePen, chartPoints.ToArray())
            End If

            Dim lastPoint = chartPoints(chartPoints.Count - 1)
            g.FillEllipse(pointBrush, lastPoint.X - 4, lastPoint.Y - 4, 8, 8)

            Dim firstTime = visible(0).Timestamp.ToString("MM/dd HH:mm")
            Dim lastTime = visible(visible.Count - 1).Timestamp.ToString("MM/dd HH:mm")
            g.DrawString(firstTime, font, mutedBrush, plot.Left, plot.Bottom + 8)
            Dim lastTimeSize = g.MeasureString(lastTime, font)
            g.DrawString(lastTime, font, mutedBrush, plot.Right - lastTimeSize.Width, plot.Bottom + 8)

            Dim latest = visible(visible.Count - 1)
            Dim latestText = $"最新: {FormatMetricValue(GetMetricValue(latest))}"
            Dim latestSize = g.MeasureString(latestText, titleFont)
            g.DrawString(latestText, titleFont, textBrush, ClientSize.Width - 20 - latestSize.Width, 10)

            Dim modeText = If(followLatest, "最新", "手動")
            g.DrawString($"Bitmap zoom: {modeText} {visible.Count} / {points.Count} 點", font, mutedBrush, 190, 12)

            DrawHover(g, plot, visible, chartPoints, font, textBrush, mutedBrush)
        End Using
    End Sub

    Private Sub DrawHover(g As Graphics, plot As Rectangle, visible As List(Of ChartPoint), chartPoints As List(Of PointF), font As Font, textBrush As Brush, mutedBrush As Brush)
        If hoveredIndex < 0 OrElse hoveredIndex >= visible.Count OrElse hoveredIndex >= chartPoints.Count Then
            Return
        End If

        Dim pt = chartPoints(hoveredIndex)
        Using crossPen As New Pen(Color.FromArgb(80, 15, 23, 42)),
              fillBrush As New SolidBrush(Color.FromArgb(245, 255, 255, 255)),
              borderPen As New Pen(Color.FromArgb(148, 163, 184)),
              markerBrush As New SolidBrush(GetLineColor())

            g.DrawLine(crossPen, pt.X, plot.Top, pt.X, plot.Bottom)
            g.FillEllipse(markerBrush, pt.X - 5, pt.Y - 5, 10, 10)

            Dim item = visible(hoveredIndex)
            Dim lines = {
                item.Timestamp.ToString("MM/dd HH:mm:ss"),
                $"YT {FormatUsd(item.ValueUsd)}",
                $"FIX {FormatPercent(item.FixApy)}",
                $"U+R {FormatPercent(item.UnderlyingApyWithRewards)}"
            }
            Dim boxWidth As Single = 136
            Dim boxHeight As Single = 80
            Dim boxX = If(pt.X + boxWidth + 14 < ClientSize.Width, pt.X + 12, pt.X - boxWidth - 12)
            Dim boxY = Math.Max(8, Math.Min(ClientSize.Height - boxHeight - 8, pt.Y - boxHeight - 10))
            Dim boxRect As New RectangleF(boxX, CSng(boxY), boxWidth, boxHeight)
            g.FillRectangle(fillBrush, boxRect)
            g.DrawRectangle(borderPen, Rectangle.Round(boxRect))

            g.DrawString(lines(0), font, mutedBrush, boxRect.Left + 8, boxRect.Top + 7)
            g.DrawString(lines(1), font, textBrush, boxRect.Left + 8, boxRect.Top + 25)
            g.DrawString(lines(2), font, textBrush, boxRect.Left + 8, boxRect.Top + 43)
            g.DrawString(lines(3), font, textBrush, boxRect.Left + 8, boxRect.Top + 61)
        End Using
    End Sub

    Private Function GetVisiblePoints() As List(Of ChartPoint)
        ClampView()
        Return points.Skip(viewStart).Take(viewCount).ToList()
    End Function

    Private Function HitTestPoint(mouseX As Integer) As Integer
        If points.Count = 0 Then
            Return -1
        End If

        Dim plot = GetPlotRect()
        If mouseX < plot.Left OrElse mouseX > plot.Right OrElse plot.Width <= 0 Then
            Return -1
        End If

        Dim visibleCount = Math.Min(viewCount, points.Count - viewStart)
        If visibleCount <= 1 Then
            Return 0
        End If

        Dim ratio = (mouseX - plot.Left) / CDbl(plot.Width)
        Return Math.Max(0, Math.Min(visibleCount - 1, CInt(Math.Round(ratio * (visibleCount - 1)))))
    End Function

    Private Function GetPlotRect() As Rectangle
        Return New Rectangle(72, 38, Math.Max(1, ClientSize.Width - 96), Math.Max(1, ClientSize.Height - 76))
    End Function

    Private Sub ClampView()
        If points.Count = 0 Then
            viewStart = 0
            viewCount = 0
            Return
        End If

        viewCount = Math.Max(1, Math.Min(viewCount, points.Count))
        viewStart = Math.Max(0, Math.Min(viewStart, points.Count - viewCount))
        If viewStart + viewCount >= points.Count Then
            followLatest = True
        End If
    End Sub

    Private Sub RaiseViewChanged()
        If points.Count = 0 Then
            RaiseEvent ViewChanged("無資料")
            Return
        End If

        Dim endIndex = Math.Min(points.Count, viewStart + viewCount)
        Dim modeText = If(followLatest, "最新", "手動")
        RaiseEvent ViewChanged($"{modeText} {viewStart + 1}-{endIndex} / {points.Count}")
    End Sub

    Private Function GetTitle() As String
        If Metric = ChartMetric.FixApy Then
            Return "FIX APY 走勢"
        End If
        If Metric = ChartMetric.UnderlyingApyWithRewards Then
            Return "Underlying APY + Rewards"
        End If
        Return "YT 價值走勢 (USD)"
    End Function

    Private Function GetMetricValue(point As ChartPoint) As Double
        If Metric = ChartMetric.FixApy Then
            Return point.FixApy * 100.0
        End If
        If Metric = ChartMetric.UnderlyingApyWithRewards Then
            Return point.UnderlyingApyWithRewards * 100.0
        End If
        Return point.ValueUsd
    End Function

    Private Function FormatMetricValue(value As Double) As String
        If Metric = ChartMetric.FixApy OrElse Metric = ChartMetric.UnderlyingApyWithRewards Then
            Return $"{value:N3}%"
        End If
        Return FormatUsd(value)
    End Function

    Private Function FormatAxisValue(value As Double) As String
        If Metric = ChartMetric.FixApy OrElse Metric = ChartMetric.UnderlyingApyWithRewards Then
            Return $"{value:N3}%"
        End If
        Return value.ToString("N2")
    End Function

    Private Function GetMinimumSpan(maxValue As Double) As Double
        If Metric = ChartMetric.FixApy OrElse Metric = ChartMetric.UnderlyingApyWithRewards Then
            Return 0.001
        End If
        Return Math.Max(0.02, Math.Abs(maxValue) * 0.00002)
    End Function

    Private Function GetLineColor() As Color
        If Metric = ChartMetric.FixApy Then
            Return Color.FromArgb(194, 65, 12)
        End If
        If Metric = ChartMetric.UnderlyingApyWithRewards Then
            Return Color.FromArgb(37, 99, 235)
        End If
        Return Color.FromArgb(13, 148, 136)
    End Function

    Private Function HasMetricValue(point As ChartPoint) As Boolean
        If Metric = ChartMetric.UnderlyingApyWithRewards Then
            Return point.UnderlyingApyWithRewards > 0
        End If
        Return True
    End Function

    Private Shared Function FormatUsd(value As Double) As String
        Return "$" & value.ToString("N2")
    End Function

    Private Shared Function FormatPercent(value As Double) As String
        Return $"{value * 100.0:N3}%"
    End Function
End Class
