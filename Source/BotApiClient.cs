using System;
using System.Collections;
using System.Globalization;
using System.Text;
using UnityEngine.Networking;

namespace PraetorisClient
{
    internal static class BotApiClient
    {
        public static IEnumerator PostLinkRoutine(LinkRequest link, Action<long, string, bool, string> sendResult)
        {
            string linkApiUrl = PraetorisClientPlugin.GetLinkApiUrl();
            string botApiKey = PraetorisClientPlugin.GetBotApiKey();

            if (string.IsNullOrWhiteSpace(linkApiUrl))
            {
                sendResult(link.Sender, link.RequestId, false, "Link API URL is not configured on the server.");
                yield break;
            }

            if (string.IsNullOrWhiteSpace(botApiKey))
            {
                sendResult(link.Sender, link.RequestId, false, "Bot API key is not configured on the server.");
                yield break;
            }

            string body = JsonLinkRequest(link);
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            using UnityWebRequest request = new(linkApiUrl, "POST")
            {
                uploadHandler = new UploadHandlerRaw(bytes),
                downloadHandler = new DownloadHandlerBuffer()
            };
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("X-API-Key", botApiKey);
            request.SetRequestHeader("User-Agent", "PraetorisClient/0.1");

            yield return request.SendWebRequest();

            string responseText = request.downloadHandler != null ? request.downloadHandler.text : "";
            if (request.result != UnityWebRequest.Result.Success || request.responseCode < 200 || request.responseCode >= 300)
            {
                string message = string.IsNullOrWhiteSpace(responseText)
                    ? (string.IsNullOrWhiteSpace(request.error) ? "HTTP " + request.responseCode : request.error + " (HTTP " + request.responseCode + ")")
                    : responseText;
                PraetorisClientPlugin.Log.LogWarning("Discord link API failed for " + link.PlayerId + ": " + message);
                sendResult(link.Sender, link.RequestId, false, message);
            }
            else
            {
                string message = string.IsNullOrWhiteSpace(responseText)
                    ? "Discord link complete."
                    : responseText;
                PraetorisClientPlugin.Log.LogInfo("Discord link API accepted " + link.PlayerId + ".");
                sendResult(link.Sender, link.RequestId, true, message);
            }
        }

        private static string JsonLinkRequest(LinkRequest link)
        {
            return "{" +
                   "\"requestId\":\"" + EscapeJson(link.RequestId) + "\"," +
                   "\"code\":\"" + EscapeJson(link.Code) + "\"," +
                   "\"playerId\":\"" + EscapeJson(link.PlayerId) + "\"," +
                   "\"playerName\":\"" + EscapeJson(link.PlayerName) + "\"," +
                   "\"endpoint\":\"" + EscapeJson(link.Endpoint) + "\"," +
                   "\"platformDisplayName\":\"" + EscapeJson(link.PlatformDisplayName) + "\"," +
                   "\"receivedAtUtc\":\"" + EscapeJson(link.ReceivedAtUtc.ToString("O", CultureInfo.InvariantCulture)) + "\"" +
                   "}";
        }

        private static string EscapeJson(string value)
        {
            return (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }
}
