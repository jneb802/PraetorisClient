using System;

namespace PraetorisClient
{
    internal static class RpcTraceUploadRetryPolicy
    {
        internal static bool ShouldRetryUpload(long responseCode, string responseText)
        {
            return !IsPermanentReceiverConfigurationFailure(responseCode, responseText);
        }

        internal static bool IsPermanentReceiverConfigurationFailure(long responseCode, string responseText)
        {
            string message = responseText ?? "";
            return responseCode == 403
                || message.IndexOf("missing relay key", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("missing token signing secret", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("unauthorized relay", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("invalid token", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
