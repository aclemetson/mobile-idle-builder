using System;
using UnityEngine;
using UnityEditor;

[InitializeOnLoad]
internal static class UnityRemoteWarningFilter
{
    static UnityRemoteWarningFilter()
    {
        Debug.unityLogger.logHandler = new RemoteErrorReclassifier(Debug.unityLogger.logHandler);
    }

    private sealed class RemoteErrorReclassifier : ILogHandler
    {
        private readonly ILogHandler _inner;

        internal RemoteErrorReclassifier(ILogHandler inner) => _inner = inner;

        public void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
        {
            if (logType == LogType.Error && IsRemoteNoDeviceError(format, args))
            {
                _inner.LogFormat(LogType.Warning, context,
                    "[Unity Remote] No Android device connected — connect a device with USB debugging enabled to use Unity Remote.",
                    Array.Empty<object>());
                return;
            }
            _inner.LogFormat(logType, context, format, args);
        }

        public void LogException(Exception exception, UnityEngine.Object context)
        {
            if (IsRemoteNoDeviceException(exception))
            {
                _inner.LogFormat(LogType.Warning, context,
                    "[Unity Remote] No Android device connected — connect a device with USB debugging enabled to use Unity Remote.",
                    Array.Empty<object>());
                return;
            }
            _inner.LogException(exception, context);
        }

        private static bool IsRemoteNoDeviceError(string format, object[] args)
        {
            if (MatchesRemoteNoDevice(format)) return true;
            if (args is { Length: > 0 })
            {
                try { return MatchesRemoteNoDevice(string.Format(format, args)); }
                catch { /* ignore malformed format strings */ }
            }
            return false;
        }

        private static bool IsRemoteNoDeviceException(Exception ex) =>
            ex?.Message != null && MatchesRemoteNoDevice(ex.Message);

        private static bool MatchesRemoteNoDevice(string msg) =>
            msg != null &&
            msg.Contains("Unity Remote requirements check failed") &&
            msg.Contains("no devices");
    }
}
