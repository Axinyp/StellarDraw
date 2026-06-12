namespace WhiteBoard.Contracts.Models;

public static class HostTopics
{
    public const string MeetingJoined        = "host.meeting.joined";
    public const string MeetingLeft          = "host.meeting.left";
    public const string CollaborationEnable  = "host.collab.enable";
    public const string CollaborationDisable = "host.collab.disable";
    public const string SaveRequest          = "host.save.request";
    public const string LanguageChanged      = "host.language.changed";
    public const string ThemeChanged         = "host.theme.changed";
}

public static class WhiteboardTopics
{
    public const string Ready          = "whiteboard.ready";
    public const string Closed         = "whiteboard.closed";
    public const string UnsavedChanges = "whiteboard.unsaved";
    public const string SaveCompleted  = "whiteboard.save.done";
    public const string SaveFailed     = "whiteboard.save.failed";
    public const string ExportReady    = "whiteboard.export.ready";
}
