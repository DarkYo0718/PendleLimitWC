Imports System.Globalization
Imports System.Net.Http
Imports System.Text.Json
Imports System.Threading.Tasks

Public NotInheritable Class PendleApiClient
    Private Shared ReadOnly Client As New HttpClient()

    Private Sub New()
    End Sub

    Public Shared Async Function GetIncentiveConfigAsync(config As AppConfig) As Task(Of IncentiveConfig)
        Dim uri = "https://api-v2.pendle.finance/core/v1/limit-orders/incentive/configs"
        Using response = Await Client.GetAsync(uri)
            response.EnsureSuccessStatusCode()
            Using document = JsonDocument.Parse(Await response.Content.ReadAsStringAsync())
                Dim configs As JsonElement
                If Not document.RootElement.TryGetProperty("configs", configs) Then
                    Return Nothing
                End If

                For Each item In configs.EnumerateArray()
                    Dim chainId = GetIntValue(item, "chainId", 0)
                    Dim market = GetStringValue(item, "marketAddress", "")
                    If chainId = config.ChainId AndAlso String.Equals(market, config.MarketAddress, StringComparison.OrdinalIgnoreCase) Then
                        Dim estimatedApr As JsonElement
                        item.TryGetProperty("estimatedApr", estimatedApr)
                        Return New IncentiveConfig With {
                            .MinApy = GetDoubleValue(item, "minApy", 0),
                            .MaxApy = GetDoubleValue(item, "maxApy", 0),
                            .ImpliedApy = GetDoubleValue(item, "impliedApy", 0),
                            .BuyYtApr = GetDoubleValue(estimatedApr, "buyYtApr", 0),
                            .SellYtApr = GetDoubleValue(estimatedApr, "sellYtApr", 0)
                        }
                    End If
                Next
            End Using
        End Using

        Return Nothing
    End Function

    Public Shared Async Function GetMakerOrdersAsync(config As AppConfig) As Task(Of List(Of MakerOrderInfo))
        Dim results As New List(Of MakerOrderInfo)()
        If String.IsNullOrWhiteSpace(config.WalletAddress) OrElse String.IsNullOrWhiteSpace(config.MarketAddress) Then
            Return results
        End If

        Dim requestUri = $"https://api-v2.pendle.finance/core/v1/limit-orders/makers/limit-orders?chainId={config.ChainId}&maker={System.Uri.EscapeDataString(config.WalletAddress)}&market={System.Uri.EscapeDataString(config.MarketAddress)}&limit=20"
        Using response = Await Client.GetAsync(requestUri)
            response.EnsureSuccessStatusCode()
            Using document = JsonDocument.Parse(Await response.Content.ReadAsStringAsync())
                Dim orders As JsonElement
                If Not document.RootElement.TryGetProperty("results", orders) OrElse orders.ValueKind <> JsonValueKind.Array Then
                    Return results
                End If

                For Each item In orders.EnumerateArray()
                    Dim state As JsonElement
                    item.TryGetProperty("orderState", state)
                    results.Add(New MakerOrderInfo With {
                        .Id = GetStringValue(item, "id", ""),
                        .Status = GetStringValue(item, "status", ""),
                        .OrderType = GetStringValue(state, "orderType", ""),
                        .LnImpliedRate = GetDoubleFromString(item, "lnImpliedRate", 0),
                        .CreatedAt = GetDateValue(item, "createdAt"),
                        .CurrentMakingAmount = GetStringValue(item, "currentMakingAmount", ""),
                        .NotionalVolumeUsd = GetDoubleValue(state, "notionalVolumeUSD", 0)
                    })
                Next
            End Using
        End Using

        Return results
    End Function

    Public Shared Async Function GetLiveChartPointAsync(config As AppConfig) As Task(Of ChartPoint)
        Dim walletTask = GetWalletYtSnapshotAsync(config)
        Dim marketTask = GetMarketApySnapshotAsync(config)
        Await Task.WhenAll(walletTask, marketTask)

        Dim market = marketTask.Result
        Dim wallet = walletTask.Result
        Dim ytBalance = wallet.RawBalance / Math.Pow(10, market.YtDecimals)
        Return New ChartPoint With {
            .Timestamp = DateTime.Now,
            .ValueUsd = wallet.ValueUsd,
            .FixApy = market.FixApy,
            .UnderlyingApy = market.UnderlyingApy,
            .UnderlyingInterestApy = market.UnderlyingInterestApy,
            .UnderlyingRewardApy = market.UnderlyingRewardApy,
            .YtIncentiveRewardApy = market.YtIncentiveRewardApy,
            .MarketExpiry = market.MarketExpiry,
            .YtBalance = ytBalance,
            .YtSymbol = market.YtSymbol,
            .AccountingAssetSymbol = market.AccountingAssetSymbol,
            .AccountingAssetPriceUsd = market.AccountingAssetPriceUsd
        }
    End Function

    Private Shared Async Function GetMarketApySnapshotAsync(config As AppConfig) As Task(Of MarketApySnapshot)
        Dim requestUri = $"https://api-v2.pendle.finance/core/v1/{config.ChainId}/markets/{config.MarketAddress}"
        Using response = Await Client.GetAsync(requestUri)
            response.EnsureSuccessStatusCode()
            Using document = JsonDocument.Parse(Await response.Content.ReadAsStringAsync())
                Dim ytIncentiveRewardApy As Double = 0
                Dim breakdown As JsonElement
                If document.RootElement.TryGetProperty("underlyingRewardApyBreakdown", breakdown) AndAlso breakdown.ValueKind = JsonValueKind.Array Then
                    For Each item In breakdown.EnumerateArray()
                        If GetBooleanValue(item, "ytExclusive", False) Then
                            ytIncentiveRewardApy += GetDoubleValue(item, "absoluteApy", 0)
                        End If
                    Next
                End If

                Return New MarketApySnapshot With {
                    .FixApy = GetDoubleValue(document.RootElement, "impliedApy", 0),
                    .UnderlyingApy = GetDoubleValue(document.RootElement, "underlyingApy", 0),
                    .UnderlyingInterestApy = GetDoubleValue(document.RootElement, "underlyingInterestApy", 0),
                    .UnderlyingRewardApy = GetDoubleValue(document.RootElement, "underlyingRewardApy", 0),
                    .YtIncentiveRewardApy = ytIncentiveRewardApy,
                    .MarketExpiry = GetDateValue(document.RootElement, "expiry"),
                    .YtDecimals = GetNestedIntValue(document.RootElement, "yt", "decimals", 18),
                    .YtSymbol = GetNestedStringValue(document.RootElement, "yt", "symbol", "YT"),
                    .AccountingAssetSymbol = GetNestedStringValue(document.RootElement, "accountingAsset", "symbol", ""),
                    .AccountingAssetPriceUsd = GetNestedDoubleValue(document.RootElement, "accountingAsset", "price", "usd", 0)
                }
            End Using
        End Using
    End Function

    Private Shared Async Function GetWalletYtSnapshotAsync(config As AppConfig) As Task(Of WalletYtSnapshot)
        Dim requestUri = $"https://api-v2.pendle.finance/core/v1/dashboard/positions/database/{config.WalletAddress}?filterUsd=0"
        Using response = Await Client.GetAsync(requestUri)
            response.EnsureSuccessStatusCode()
            Using document = JsonDocument.Parse(Await response.Content.ReadAsStringAsync())
                Dim positions As JsonElement
                If Not document.RootElement.TryGetProperty("positions", positions) OrElse positions.ValueKind <> JsonValueKind.Array Then
                    Return New WalletYtSnapshot()
                End If

                Dim marketId = $"{config.ChainId}-{config.MarketAddress}".ToLowerInvariant()
                For Each chain In positions.EnumerateArray()
                    If GetIntValue(chain, "chainId", 0) <> config.ChainId Then
                        Continue For
                    End If

                    Dim openPositions As JsonElement
                    If Not chain.TryGetProperty("openPositions", openPositions) OrElse openPositions.ValueKind <> JsonValueKind.Array Then
                        Continue For
                    End If

                    For Each position In openPositions.EnumerateArray()
                        If Not String.Equals(GetStringValue(position, "marketId", ""), marketId, StringComparison.OrdinalIgnoreCase) Then
                            Continue For
                        End If

                        Dim yt As JsonElement
                        If position.TryGetProperty("yt", yt) Then
                            Return New WalletYtSnapshot With {
                                .ValueUsd = GetDoubleValue(yt, "valuation", 0),
                                .RawBalance = GetDoubleValue(yt, "balance", 0)
                            }
                        End If
                    Next
                Next
            End Using
        End Using

        Return New WalletYtSnapshot()
    End Function

    Private Shared Function GetNestedStringValue(parent As JsonElement, objectName As String, name As String, fallback As String) As String
        Dim child As JsonElement
        If parent.TryGetProperty(objectName, child) Then Return GetStringValue(child, name, fallback)
        Return fallback
    End Function

    Private Shared Function GetNestedIntValue(parent As JsonElement, objectName As String, name As String, fallback As Integer) As Integer
        Dim child As JsonElement
        If parent.TryGetProperty(objectName, child) Then Return GetIntValue(child, name, fallback)
        Return fallback
    End Function

    Private Shared Function GetNestedDoubleValue(parent As JsonElement, objectName As String, nestedObjectName As String, name As String, fallback As Double) As Double
        Dim child As JsonElement
        Dim nested As JsonElement
        If parent.TryGetProperty(objectName, child) AndAlso child.TryGetProperty(nestedObjectName, nested) Then
            Return GetDoubleValue(nested, name, fallback)
        End If
        Return fallback
    End Function

    Private Shared Function GetStringValue(parent As JsonElement, name As String, fallback As String) As String
        If parent.ValueKind = JsonValueKind.Undefined Then
            Return fallback
        End If

        Dim value As JsonElement
        If parent.TryGetProperty(name, value) Then
            If value.ValueKind = JsonValueKind.String Then
                Return value.GetString()
            End If
            Return value.ToString()
        End If
        Return fallback
    End Function

    Private Shared Function GetDoubleValue(parent As JsonElement, name As String, fallback As Double) As Double
        If parent.ValueKind = JsonValueKind.Undefined Then
            Return fallback
        End If

        Dim value As JsonElement
        If parent.TryGetProperty(name, value) Then
            If value.ValueKind = JsonValueKind.Number Then
                Return value.GetDouble()
            End If
            If value.ValueKind = JsonValueKind.String Then
                Dim parsed As Double
                If Double.TryParse(value.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, parsed) Then
                    Return parsed
                End If
            End If
        End If
        Return fallback
    End Function

    Private Shared Function GetIntValue(parent As JsonElement, name As String, fallback As Integer) As Integer
        Return CInt(GetDoubleValue(parent, name, fallback))
    End Function

    Private Shared Function GetDoubleFromString(parent As JsonElement, name As String, fallback As Double) As Double
        Dim raw = GetStringValue(parent, name, "")
        Dim parsed As Double
        If Double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, parsed) Then
            Return parsed
        End If
        Return fallback
    End Function

    Private Shared Function GetBooleanValue(parent As JsonElement, name As String, fallback As Boolean) As Boolean
        If parent.ValueKind = JsonValueKind.Undefined Then
            Return fallback
        End If

        Dim value As JsonElement
        If parent.TryGetProperty(name, value) AndAlso (value.ValueKind = JsonValueKind.True OrElse value.ValueKind = JsonValueKind.False) Then
            Return value.GetBoolean()
        End If
        Return fallback
    End Function

    Private Shared Function GetDateValue(parent As JsonElement, name As String) As DateTime
        Dim raw = GetStringValue(parent, name, "")
        Dim parsed As DateTime
        If DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, parsed) Then
            Return parsed.ToLocalTime()
        End If
        Return DateTime.MinValue
    End Function

    Private Class MarketApySnapshot
        Public Property FixApy As Double
        Public Property UnderlyingApy As Double
        Public Property UnderlyingInterestApy As Double
        Public Property UnderlyingRewardApy As Double
        Public Property YtIncentiveRewardApy As Double
        Public Property MarketExpiry As DateTime
        Public Property YtDecimals As Integer
        Public Property YtSymbol As String
        Public Property AccountingAssetSymbol As String
        Public Property AccountingAssetPriceUsd As Double
    End Class

    Private Class WalletYtSnapshot
        Public Property ValueUsd As Double
        Public Property RawBalance As Double
    End Class
End Class
