Public Class IncentiveConfig
    Public Property MinApy As Double
    Public Property MaxApy As Double
    Public Property ImpliedApy As Double
    Public Property BuyYtApr As Double
    Public Property SellYtApr As Double
End Class

Public Class MakerOrderInfo
    Public Property Id As String = ""
    Public Property Status As String = ""
    Public Property OrderType As String = ""
    Public Property LnImpliedRate As Double
    Public Property CreatedAt As DateTime
    Public Property CurrentMakingAmount As String = ""
    Public Property NotionalVolumeUsd As Double
End Class

Public Class AdvisorResult
    Public Property HasData As Boolean
    Public Property Status As String = ""
    Public Property RangeText As String = ""
    Public Property SuggestedApy As Double
    Public Property ActiveOrderCount As Integer
    Public Property ActiveOrderText As String = ""
    Public Property ShouldNotify As Boolean
    Public Property NotificationKey As String = ""
    Public Property NotificationText As String = ""
End Class

