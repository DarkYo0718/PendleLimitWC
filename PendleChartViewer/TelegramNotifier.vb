Imports System.Net.Http
Imports System.Threading.Tasks

Public NotInheritable Class TelegramNotifier
    Private Shared ReadOnly Client As New HttpClient()

    Private Sub New()
    End Sub

    Public Shared Async Function SendAsync(config As AppConfig, message As String) As Task
        If config Is Nothing OrElse Not config.HasTelegram Then
            Return
        End If

        Dim url = $"https://api.telegram.org/bot{config.TelegramBotToken}/sendMessage"
        Using content As New FormUrlEncodedContent(New Dictionary(Of String, String) From {
            {"chat_id", config.TelegramChatId},
            {"text", message}
        })
            Dim response = Await Client.PostAsync(url, content)
            response.EnsureSuccessStatusCode()
        End Using
    End Function
End Class

