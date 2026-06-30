Imports System.Globalization
Imports System.IO
Imports System.Text.Json

Public NotInheritable Class PendleStateLoader
    Private Sub New()
    End Sub

    Public Shared Function LoadHistory(statePath As String) As List(Of ChartPoint)
        Dim result As New List(Of ChartPoint)()
        If Not File.Exists(statePath) Then
            Return result
        End If

        Using document = JsonDocument.Parse(File.ReadAllText(statePath))
            Dim history As JsonElement
            If Not document.RootElement.TryGetProperty("walletYtHistory", history) OrElse history.ValueKind <> JsonValueKind.Array Then
                Return result
            End If

            For Each item In history.EnumerateArray()
                Dim timestampText As String = ""
                Dim valueUsd As Double = 0
                Dim fixApy As Double = 0
                Dim underlyingApy As Double = 0
                Dim underlyingInterestApy As Double = 0
                Dim underlyingRewardApy As Double = 0
                Dim ytIncentiveRewardApy As Double = 0
                Dim marketExpiry As DateTime = DateTime.MinValue
                Dim ytBalance As Double = 0
                Dim ytSymbol As String = "YT"
                Dim accountingAssetSymbol As String = ""
                Dim accountingAssetPriceUsd As Double = 0

                Dim timestampElement As JsonElement
                If item.TryGetProperty("timestamp", timestampElement) Then
                    timestampText = timestampElement.GetString()
                End If

                Dim valueElement As JsonElement
                If item.TryGetProperty("valueUsd", valueElement) Then
                    valueUsd = valueElement.GetDouble()
                End If

                Dim apyElement As JsonElement
                If item.TryGetProperty("fixApy", apyElement) Then
                    fixApy = apyElement.GetDouble()
                End If

                underlyingApy = GetDoubleValue(item, "underlyingApy", 0)
                underlyingInterestApy = GetDoubleValue(item, "underlyingInterestApy", 0)
                underlyingRewardApy = GetDoubleValue(item, "underlyingRewardApy", 0)
                ytIncentiveRewardApy = GetDoubleValue(item, "ytIncentiveRewardApy", 0)
                marketExpiry = GetDateValue(item, "marketExpiry")
                ytBalance = GetDoubleValue(item, "ytBalance", 0)
                ytSymbol = GetStringValue(item, "ytSymbol", "YT")
                accountingAssetSymbol = GetStringValue(item, "accountingAssetSymbol", "")
                accountingAssetPriceUsd = GetDoubleValue(item, "accountingAssetPriceUsd", 0)

                Dim timestamp As DateTime
                If DateTime.TryParse(timestampText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, timestamp) Then
                    result.Add(New ChartPoint With {
                        .Timestamp = timestamp.ToLocalTime(),
                        .ValueUsd = valueUsd,
                        .FixApy = fixApy,
                        .UnderlyingApy = underlyingApy,
                        .UnderlyingInterestApy = underlyingInterestApy,
                        .UnderlyingRewardApy = underlyingRewardApy,
                        .YtIncentiveRewardApy = ytIncentiveRewardApy,
                        .MarketExpiry = marketExpiry,
                        .YtBalance = ytBalance,
                        .YtSymbol = ytSymbol,
                        .AccountingAssetSymbol = accountingAssetSymbol,
                        .AccountingAssetPriceUsd = accountingAssetPriceUsd
                    })
                End If
            Next
        End Using

        Return result
    End Function

    Private Shared Function GetDoubleValue(parent As JsonElement, name As String, fallback As Double) As Double
        Dim value As JsonElement
        If parent.TryGetProperty(name, value) AndAlso value.ValueKind = JsonValueKind.Number Then
            Return value.GetDouble()
        End If
        Return fallback
    End Function

    Private Shared Function GetDateValue(parent As JsonElement, name As String) As DateTime
        Dim value As JsonElement
        If parent.TryGetProperty(name, value) AndAlso value.ValueKind = JsonValueKind.String Then
            Dim parsed As DateTime
            If DateTime.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, parsed) Then
                Return parsed.ToLocalTime()
            End If
        End If
        Return DateTime.MinValue
    End Function

    Private Shared Function GetStringValue(parent As JsonElement, name As String, fallback As String) As String
        Dim value As JsonElement
        If parent.TryGetProperty(name, value) AndAlso value.ValueKind = JsonValueKind.String Then Return value.GetString()
        Return fallback
    End Function
End Class
