Public Class ChartPoint
    Public Property Timestamp As DateTime
    Public Property ValueUsd As Double
    Public Property FixApy As Double
    Public Property UnderlyingApy As Double
    Public Property UnderlyingInterestApy As Double
    Public Property UnderlyingRewardApy As Double
    Public Property YtIncentiveRewardApy As Double
    Public Property MarketExpiry As DateTime
    Public Property YtBalance As Double
    Public Property YtSymbol As String = "YT"
    Public Property AccountingAssetSymbol As String = ""
    Public Property AccountingAssetPriceUsd As Double

    Public ReadOnly Property UnderlyingApyWithRewards As Double
        Get
            Return UnderlyingApy + YtIncentiveRewardApy
        End Get
    End Property
End Class
