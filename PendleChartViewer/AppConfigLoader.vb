Imports System.IO
Imports System.Text.Json

Public NotInheritable Class AppConfigLoader
    Private Sub New()
    End Sub

    Public Shared Function Load(configPath As String) As AppConfig
        Dim config As New AppConfig()
        If Not File.Exists(configPath) Then
            ApplyEnvironmentOverrides(config)
            Return config
        End If

        Using document = JsonDocument.Parse(File.ReadAllText(configPath))
            Dim pendle As JsonElement
            If document.RootElement.TryGetProperty("pendle", pendle) Then
                config.ChainId = CInt(GetDoubleValue(pendle, "chainId", config.ChainId))
                config.MarketAddress = GetStringValue(pendle, "market", config.MarketAddress)
            End If

            Dim telegram As JsonElement
            If document.RootElement.TryGetProperty("telegram", telegram) Then
                config.TelegramBotToken = GetStringValue(telegram, "botToken", config.TelegramBotToken)
                config.TelegramChatId = GetStringValue(telegram, "chatId", config.TelegramChatId)
            End If

            Dim walletMonitor As JsonElement
            If document.RootElement.TryGetProperty("walletMonitor", walletMonitor) Then
                config.WalletAddress = GetStringValue(walletMonitor, "address", config.WalletAddress)
                config.MinUsdChange = GetDoubleValue(walletMonitor, "minUsdChange", config.MinUsdChange)
                config.MinFixApyChangePercentPoints = GetDoubleValue(walletMonitor, "minFixApyChangePercentPoints", config.MinFixApyChangePercentPoints)
                config.AdvisorSafetyMarginPercentPoints = GetDoubleValue(walletMonitor, "advisorSafetyMarginPercentPoints", config.AdvisorSafetyMarginPercentPoints)
                config.HistoryPoints = CInt(GetDoubleValue(walletMonitor, "historyPoints", config.HistoryPoints))
            End If

            Dim monitor As JsonElement
            If document.RootElement.TryGetProperty("monitor", monitor) Then
                config.PollSeconds = CInt(GetDoubleValue(monitor, "pollSeconds", config.PollSeconds))
                config.RemoteStateUrl = GetStringValue(monitor, "remoteStateUrl", config.RemoteStateUrl)
            End If
        End Using

        ApplyEnvironmentOverrides(config)
        Return config
    End Function

    Private Shared Sub ApplyEnvironmentOverrides(config As AppConfig)
        Dim token = Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN")
        If Not String.IsNullOrWhiteSpace(token) Then
            config.TelegramBotToken = token
        End If

        Dim chatId = Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID")
        If Not String.IsNullOrWhiteSpace(chatId) Then
            config.TelegramChatId = chatId
        End If
    End Sub

    Private Shared Function GetStringValue(parent As JsonElement, name As String, fallback As String) As String
        Dim value As JsonElement
        If parent.TryGetProperty(name, value) AndAlso value.ValueKind = JsonValueKind.String Then
            Return value.GetString()
        End If
        Return fallback
    End Function

    Private Shared Function GetDoubleValue(parent As JsonElement, name As String, fallback As Double) As Double
        Dim value As JsonElement
        If parent.TryGetProperty(name, value) AndAlso value.ValueKind = JsonValueKind.Number Then
            Return value.GetDouble()
        End If
        Return fallback
    End Function
End Class
