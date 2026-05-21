namespace CustomController;

public class WorkerOptions
{
    public string db_config { get; set; } = string.Empty;        // ← оставляем
    public int FormatCardUid { get; set; } = 2;
    public bool RunNow { get; set; } = true;
    public string TimeStart { get; set; } = "9:10:0";
    public string Timeout { get; set; } = "0:02:00";
    public int TimeWaitHttp { get; set; } = 8;
    public bool ClearLog { get; set; } = false;
}