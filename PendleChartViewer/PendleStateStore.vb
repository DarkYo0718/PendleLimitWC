Imports System.IO
Imports System.Text.Json
Imports System.Text.Json.Nodes

Public NotInheritable Class PendleStateStore
    Private Sub New()
    End Sub

    Public Shared Sub AppendHistoryPoint(statePath As String, point As ChartPoint, maxPoints As Integer)
        Dim root As JsonObject
        If File.Exists(statePath) Then
            root = JsonNode.Parse(File.ReadAllText(statePath)).AsObject()
        Else
            root = New JsonObject()
        End If

        Dim history = TryCast(root("walletYtHistory"), JsonArray)
        If history Is Nothing Then
            history = New JsonArray()
            root("walletYtHistory") = history
        End If

        history.Add(New JsonObject From {
            {"timestamp", point.Timestamp.ToUniversalTime().ToString("o")},
            {"valueUsd", point.ValueUsd},
            {"fixApy", point.FixApy},
            {"underlyingApy", point.UnderlyingApy},
            {"underlyingInterestApy", point.UnderlyingInterestApy},
            {"underlyingRewardApy", point.UnderlyingRewardApy},
            {"ytIncentiveRewardApy", point.YtIncentiveRewardApy},
            {"underlyingApyWithRewards", point.UnderlyingApyWithRewards},
            {"marketExpiry", If(point.MarketExpiry = DateTime.MinValue, Nothing, point.MarketExpiry.ToUniversalTime().ToString("o"))},
            {"ytBalance", point.YtBalance},
            {"ytSymbol", point.YtSymbol},
            {"accountingAssetSymbol", point.AccountingAssetSymbol},
            {"accountingAssetPriceUsd", point.AccountingAssetPriceUsd}
        })

        Dim keepCount = Math.Max(10, maxPoints)
        While history.Count > keepCount
            history.RemoveAt(0)
        End While

        root("hasWalletYtBaseline") = True
        root("lastWalletYtValueUsd") = point.ValueUsd
        root("hasFixApyBaseline") = True
        root("lastFixApy") = point.FixApy
        root("lastUnderlyingApyWithRewards") = point.UnderlyingApyWithRewards

        Dim options As New JsonSerializerOptions With {
            .WriteIndented = True
        }
        File.WriteAllText(statePath, root.ToJsonString(options))
    End Sub
End Class
