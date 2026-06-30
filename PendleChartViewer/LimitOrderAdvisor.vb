Public NotInheritable Class LimitOrderAdvisor
    Private Sub New()
    End Sub

    Public Shared Function Evaluate(config As AppConfig, incentive As IncentiveConfig, orders As List(Of MakerOrderInfo)) As AdvisorResult
        Dim result As New AdvisorResult()
        If incentive Is Nothing Then
            result.Status = "找不到獎勵區間"
            Return result
        End If

        result.HasData = True
        result.RangeText = $"{FormatApy(incentive.MinApy)} - {FormatApy(incentive.MaxApy)}"
        result.SuggestedApy = (incentive.MinApy + incentive.MaxApy) / 2.0

        Dim activeOrders = orders.Where(Function(o) String.Equals(o.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase)).ToList()
        result.ActiveOrderCount = activeOrders.Count

        If activeOrders.Count = 0 Then
            result.Status = "沒有 active 掛單"
            result.ActiveOrderText = "無"
            result.ShouldNotify = True
            result.NotificationKey = $"NO_ACTIVE|{result.RangeText}"
            result.NotificationText = $"目前沒有 active 掛單；獎勵區間 {result.RangeText}，建議 APY {FormatApy(result.SuggestedApy)}"
            Return result
        End If

        Dim first = activeOrders(0)
        Dim orderApy = ConvertLnImpliedRateToApy(first.LnImpliedRate)
        result.ActiveOrderText = $"{first.OrderType} {FormatApy(orderApy)} ({first.Status})"

        Dim margin = config.AdvisorSafetyMarginPercentPoints / 100.0
        Dim safeMin = incentive.MinApy + margin
        Dim safeMax = incentive.MaxApy - margin
        Dim outsideRange = orderApy < incentive.MinApy OrElse orderApy > incentive.MaxApy
        Dim nearEdge = orderApy < safeMin OrElse orderApy > safeMax

        If outsideRange Then
            result.Status = "掛單已離開獎勵區間"
            result.ShouldNotify = True
        ElseIf nearEdge Then
            result.Status = "掛單接近獎勵邊界"
            result.ShouldNotify = True
        Else
            result.Status = "掛單在安全區間"
        End If

        If result.ShouldNotify Then
            result.NotificationKey = $"{result.Status}|{FormatApy(orderApy)}|{result.RangeText}"
            result.NotificationText = $"{result.Status}；目前 {FormatApy(orderApy)}，獎勵區間 {result.RangeText}，建議 {FormatApy(result.SuggestedApy)}"
        End If

        Return result
    End Function

    Private Shared Function ConvertLnImpliedRateToApy(lnImpliedRateRaw As Double) As Double
        If lnImpliedRateRaw <= 0 Then
            Return 0
        End If

        Return Math.Exp(lnImpliedRateRaw / 1000000000000000000.0) - 1.0
    End Function

    Private Shared Function FormatApy(value As Double) As String
        Return $"{value * 100.0:N3}%"
    End Function
End Class

