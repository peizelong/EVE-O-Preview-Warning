using System;
using System.IO;

namespace EveOPreview.Services.Detection
{
    /// <summary>
    /// 简单的文件日志，用于诊断模板检测问题。
    /// 输出到 输出目录/detection.log
    /// </summary>
    internal static class DetectionLog
    {
        private static readonly string _logPath;
        private static readonly object _lock = new();

        static DetectionLog()
        {
            try
            {
                _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "detection.log");
                // 启动时清空旧日志
                File.WriteAllText(_logPath, $"=== Detection Log Started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\r\n");
            }
            catch
            {
                _logPath = null;
            }
        }

        public static void Write(string message)
        {
            if (_logPath == null) return;
            lock (_lock)
            {
                try
                {
                    File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\r\n");
                }
                catch { }
            }
        }
    }
}
