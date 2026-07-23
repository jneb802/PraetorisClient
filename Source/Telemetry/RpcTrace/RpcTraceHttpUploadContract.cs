using System.Collections.Generic;

namespace PraetorisClient
{
    internal static class RpcTraceHttpUploadContract
    {
        internal const string ContentType = "application/gzip";
        internal const string UserAgentSuffix = "ValheimTracerHttpUpload";

        internal static Dictionary<string, string> BuildHeaders(
            string token,
            string batchId,
            string runtimeId,
            string fileId,
            int batchIndex,
            bool finalBatch,
            string flushReason,
            string modVersion)
        {
            Dictionary<string, string> headers = new()
            {
                { "Authorization", "Bearer " + token },
                { "Content-Type", ContentType },
                { "X-Trace-Batch-Id", batchId },
                { "X-Trace-Runtime-Id", runtimeId },
                { "X-Trace-File-Id", fileId },
                { "X-Trace-Batch-Index", batchIndex.ToString() },
                { "X-Trace-Final-Batch", finalBatch ? "true" : "false" },
                { "X-Trace-Flush-Reason", flushReason ?? "" },
                { "User-Agent", BuildUserAgent(modVersion) },
            };
            return headers;
        }

        private static string BuildUserAgent(string modVersion)
        {
            string version = string.IsNullOrWhiteSpace(modVersion) ? "unknown" : modVersion;
            return "PraetorisClient/" + version + " " + UserAgentSuffix;
        }
    }
}
