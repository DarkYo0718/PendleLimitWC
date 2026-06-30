Public Class AppConfig
    Public Property TelegramBotToken As String = ""
    Public Property TelegramChatId As String = ""
    Public Property ChainId As Integer = 143
    Public Property MarketAddress As String = ""
    Public Property WalletAddress As String = ""
    Public Property MinUsdChange As Double = 1.0
    Public Property MinFixApyChangePercentPoints As Double = 0.01
    Public Property AdvisorSafetyMarginPercentPoints As Double = 0.02
    Public Property PollSeconds As Integer = 60
    Public Property HistoryPoints As Integer = 720
    Public Property RemoteStateUrl As String = ""

    Public ReadOnly Property HasTelegram As Boolean
        Get
            Return Not String.IsNullOrWhiteSpace(TelegramBotToken) AndAlso Not String.IsNullOrWhiteSpace(TelegramChatId)
        End Get
    End Property
End Class
