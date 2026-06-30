Imports System.Drawing.Drawing2D

Public Class ProfitChartCanvas
    Inherits Control

    Private ReadOnly points As New List(Of MaturityProjectionPoint)()
    Private hoveredIndex As Integer = -1

    Public Sub New()
        DoubleBuffered = True
        BackColor = Color.White
        SetStyle(ControlStyles.AllPaintingInWmPaint Or ControlStyles.UserPaint Or ControlStyles.ResizeRedraw, True)
    End Sub

    Public Sub SetPoints(items As IEnumerable(Of MaturityProjectionPoint))
        points.Clear()
        points.AddRange(items)
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        Dim plot = GetPlotRect()
        If points.Count = 0 OrElse e.X < plot.Left OrElse e.X > plot.Right Then
            hoveredIndex = -1
        ElseIf points.Count = 1 Then
            hoveredIndex = 0
        Else
            Dim ratio = (e.X - plot.Left) / CDbl(Math.Max(1, plot.Width))
            hoveredIndex = Math.Max(0, Math.Min(points.Count - 1, CInt(Math.Round(ratio * (points.Count - 1)))))
        End If
        Invalidate()
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        MyBase.OnMouseLeave(e)
        hoveredIndex = -1
        Invalidate()
    End Sub

    Protected Overrides Sub OnPaint(e As PaintEventArgs)
        MyBase.OnPaint(e)
        If ClientSize.Width <= 0 OrElse ClientSize.Height <= 0 Then Return
        Using bitmap As New Bitmap(ClientSize.Width, ClientSize.Height)
            Using g = Graphics.FromImage(bitmap)
                g.SmoothingMode = SmoothingMode.AntiAlias
                g.Clear(BackColor)
                DrawChart(g)
            End Using
            e.Graphics.DrawImageUnscaled(bitmap, 0, 0)
        End Using
    End Sub

    Private Sub DrawChart(g As Graphics)
        Dim plot = GetPlotRect()
        Using titleFont As New Font("Segoe UI Semibold", 10.5F),
              font As New Font("Segoe UI", 9.0F),
              textBrush As New SolidBrush(Color.FromArgb(51, 65, 85)),
              mutedBrush As New SolidBrush(Color.FromArgb(100, 116, 139)),
              gridPen As New Pen(Color.FromArgb(226, 232, 240)),
              linePen As New Pen(Color.FromArgb(37, 99, 235), 2.6F),
              fillBrush As New SolidBrush(Color.FromArgb(28, 37, 99, 235))

            g.DrawString("YT 未來累積收益（到期後 YT = 0）", titleFont, textBrush, 14, 10)
            If points.Count = 0 Then
                g.DrawString("等待到期日與 APY 資料", font, mutedBrush, plot.Left, plot.Top + 20)
                Return
            End If

            Dim minValue = points.Min(Function(item) item.EarnedAmount)
            Dim maxValue = points.Max(Function(item) item.EarnedAmount)
            Dim span = maxValue - minValue
            If span < 0.01 Then span = Math.Max(1, maxValue * 0.01)
            minValue -= span * 0.15
            maxValue += span * 0.15
            span = maxValue - minValue

            For i = 0 To 4
                Dim y = CSng(plot.Top + plot.Height * i / 4.0)
                g.DrawLine(gridPen, plot.Left, y, plot.Right, y)
                g.DrawString(FormatUsd(maxValue - span * i / 4.0), font, mutedBrush, 5, y - 8)
            Next

            Dim chartPoints As New List(Of PointF)()
            For i = 0 To points.Count - 1
                Dim x = If(points.Count = 1, plot.Left + plot.Width / 2.0F, CSng(plot.Left + plot.Width * i / CDbl(points.Count - 1)))
                Dim y = CSng(plot.Top + (maxValue - points(i).EarnedAmount) / span * plot.Height)
                chartPoints.Add(New PointF(x, y))
            Next
            If chartPoints.Count > 1 Then
                Dim area = chartPoints.Concat({New PointF(chartPoints.Last().X, plot.Bottom), New PointF(chartPoints.First().X, plot.Bottom)}).ToArray()
                g.FillPolygon(fillBrush, area)
                g.DrawLines(linePen, chartPoints.ToArray())
            End If

            g.DrawString(points.First().Date.ToString("MM/dd"), font, mutedBrush, plot.Left, plot.Bottom + 8)
            Dim lastText = points.Last().Date.ToString("yyyy/MM/dd")
            Dim lastSize = g.MeasureString(lastText, font)
            g.DrawString(lastText, font, mutedBrush, plot.Right - lastSize.Width, plot.Bottom + 8)

            If hoveredIndex >= 0 AndAlso hoveredIndex < chartPoints.Count Then
                DrawHover(g, chartPoints(hoveredIndex), points(hoveredIndex), plot, font)
            End If
        End Using
    End Sub

    Private Sub DrawHover(g As Graphics, point As PointF, item As MaturityProjectionPoint, plot As Rectangle, font As Font)
        Using crossPen As New Pen(Color.FromArgb(90, 15, 23, 42)),
              markerBrush As New SolidBrush(Color.FromArgb(37, 99, 235)),
              boxBrush As New SolidBrush(Color.FromArgb(248, 255, 255, 255)),
              borderPen As New Pen(Color.FromArgb(148, 163, 184)),
              textBrush As New SolidBrush(Color.FromArgb(51, 65, 85))
            g.DrawLine(crossPen, point.X, plot.Top, point.X, plot.Bottom)
            g.FillEllipse(markerBrush, point.X - 5, point.Y - 5, 10, 10)
            Dim box As New RectangleF(If(point.X + 170 < Width, point.X + 10, point.X - 160), Math.Max(38, point.Y - 70), 150, 62)
            g.FillRectangle(boxBrush, box)
            g.DrawRectangle(borderPen, Rectangle.Round(box))
            g.DrawString(item.Date.ToString("yyyy/MM/dd"), font, textBrush, box.Left + 7, box.Top + 6)
            g.DrawString($"收益 {FormatUsd(item.EarnedAmount)}", font, textBrush, box.Left + 7, box.Top + 24)
            g.DrawString($"預估可領 {FormatUsd(item.EarnedAmount)}", font, textBrush, box.Left + 7, box.Top + 42)
        End Using
    End Sub

    Private Function GetPlotRect() As Rectangle
        Return New Rectangle(82, 38, Math.Max(1, ClientSize.Width - 102), Math.Max(1, ClientSize.Height - 76))
    End Function

    Private Shared Function FormatUsd(value As Double) As String
        Return "$" & value.ToString("N2")
    End Function
End Class
