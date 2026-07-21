using System.Text;

namespace ConsoleApp1;

static class Program
{
    private static readonly string LogPath = Path.Combine(
        Path.GetTempPath(), "ClaudeCodeNotify.log");

    /// <summary>
    /// Usage:
    ///   ConsoleApp1.exe --title "..." --msg "..." --type info --actions "查看,忽略" --timeout 15 --sound on
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        try
        {
            // Parse arguments
            var parsed = ParseArgs(args);

            // Log if debug requested
            if (parsed.Debug)
                Log($"Starting: title={parsed.Title} msg={parsed.Message} type={parsed.Type}");

            // Allow up to 3 concurrent notifications (Semaphore, not Mutex)
            using var instanceSlot = new Semaphore(3, 3, @"Local\ClaudeCodeNotifySlots");
            if (!instanceSlot.WaitOne(0))
            {
                if (parsed.Debug) Log("Too many notifications (max 3). Skipping.");
                return;
            }

            try
            {
                // Build the notification form
                var form = new NotificationForm(
                    title: parsed.Title,
                    message: parsed.Message,
                    type: parsed.Type,
                    timeoutSeconds: parsed.Timeout,
                    actions: parsed.Actions,
                    playSound: parsed.Sound,
                    resultFile: parsed.ResultFile
                );

                // Run the message loop
                Application.Run(form);
            }
            finally
            {
                // Release the semaphore slot so the next queued notification can show
                try { instanceSlot.Release(); } catch { /* best-effort */ }
            }
        }
        catch (Exception ex)
        {
            Log($"FATAL: {ex}");
            // Silent crash — no popup on error
        }
    }

    // ── Arguments ──
    private record ParsedArgs(
        string Title,
        string Message,
        NotificationType Type,
        int Timeout,
        string[]? Actions,
        bool Sound,
        bool Debug,
        string? ResultFile
    );

    private static ParsedArgs ParseArgs(string[] args)
    {
        string title = "Claude Code 提醒";
        string message = "";
        string typeStr = "info";
        int timeout = 15;
        string actionsStr = "查看,忽略";
        bool sound = true;
        bool debug = false;
        string? resultFile = null;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--title":
                case "-t":
                    if (i + 1 < args.Length) title = args[++i];
                    break;
                case "--msg":
                case "-m":
                    if (i + 1 < args.Length) message = args[++i];
                    break;
                case "--type":
                case "-T":
                    if (i + 1 < args.Length) typeStr = args[++i];
                    break;
                case "--timeout":
                case "-d":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var t))
                    {
                        timeout = Math.Max(0, Math.Min(t, 600));
                        i++;
                    }
                    break;
                case "--actions":
                case "-a":
                    if (i + 1 < args.Length) actionsStr = args[++i];
                    break;
                case "--sound":
                case "-s":
                    if (i + 1 < args.Length)
                    {
                        var val = args[i + 1].ToLowerInvariant();
                        sound = val is "on" or "true" or "1";
                        i++;
                    }
                    break;
                case "--result-file":
                case "-r":
                    if (i + 1 < args.Length) resultFile = args[++i];
                    break;
                case "--debug":
                    debug = true;
                    break;
            }
        }

        // Fix garbled Chinese text caused by UTF-8→ANSI encoding mismatch
        title = FixEncoding(title);
        message = FixEncoding(message);
        actionsStr = FixEncoding(actionsStr);

        // Fallback message
        if (string.IsNullOrWhiteSpace(message))
            message = "请查看终端并进行操作";

        // Parse type
        var type = typeStr.ToLowerInvariant() switch
        {
            "warning" or "warn" => NotificationType.Warning,
            "error" or "err" => NotificationType.Error,
            "permission" or "perm" => NotificationType.Permission,
            _ => NotificationType.Info,
        };

        // Parse actions
        string[]? actions = null;
        if (!string.IsNullOrWhiteSpace(actionsStr) && actionsStr != ",")
        {
            actions = actionsStr
                .Split("||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (actions.Length == 0) actions = null;
        }

        return new ParsedArgs(title, message, type, timeout, actions, sound, debug, resultFile);
    }

    // ── Encoding fix for garbled Chinese text ──
    /// <summary>
    /// When Git Bash passes UTF-8 Chinese text to a Windows .NET app,
    /// the UTF-8 bytes can be misinterpreted as the system's ANSI codepage (e.g. GBK).
    /// This recovers the original text by re-interpreting the bytes.
    /// </summary>
    private static string FixEncoding(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // If text already contains CJK or other non-Latin Unicode chars, it's fine
        foreach (char c in text)
        {
            if (c > 0x00FF)
                return text; // Already proper Unicode
        }

        // If no high bytes, nothing to fix
        bool hasHighBytes = false;
        foreach (char c in text)
        {
            if (c >= 0x0080)
            {
                hasHighBytes = true;
                break;
            }
        }
        if (!hasHighBytes)
            return text;

        // Try to recover: treat current string as ANSI bytes, re-decode as UTF-8
        try
        {
            byte[] ansiBytes = Encoding.Default.GetBytes(text);
            string recovered = Encoding.UTF8.GetString(ansiBytes);

            // Check if recovery produced meaningful CJK characters
            bool hasCJK = false;
            foreach (char c in recovered)
            {
                if (c >= 0x4E00 && c <= 0x9FFF) // CJK Unified Ideographs
                {
                    hasCJK = true;
                    break;
                }
            }

            if (hasCJK)
                return recovered;
        }
        catch { /* If recovery fails, return original */ }

        return text;
    }

    // ── Simple file logging ──
    private static readonly object LogLock = new();

    private static void Log(string message)
    {
        try
        {
            lock (LogLock)
            {
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
                File.AppendAllText(LogPath, line, Encoding.UTF8);
            }
        }
        catch { /* best-effort logging */ }
    }
}
