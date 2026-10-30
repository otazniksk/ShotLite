namespace ShotLite;

/// <summary>All user-facing texts; the property name is the key in languages\&lt;code&gt;.json.</summary>
internal static class Strings
{
    public const string AppName = "ShotLite";

    // Overlay
    public static string Hint => Localization.Get(nameof(Hint));
    public static string ToolPencil => Localization.Get(nameof(ToolPencil));
    public static string ToolLine => Localization.Get(nameof(ToolLine));
    public static string ToolArrow => Localization.Get(nameof(ToolArrow));
    public static string ToolRectangle => Localization.Get(nameof(ToolRectangle));
    public static string ToolRedact => Localization.Get(nameof(ToolRedact));
    public static string RedactBlack => Localization.Get(nameof(RedactBlack));
    public static string RedactFrosted => Localization.Get(nameof(RedactFrosted));
    public static string RedactPixelate => Localization.Get(nameof(RedactPixelate));
    public static string ToolMarker => Localization.Get(nameof(ToolMarker));
    public static string ToolText => Localization.Get(nameof(ToolText));
    public static string ToolColor => Localization.Get(nameof(ToolColor));
    public static string ToolUndo => Localization.Get(nameof(ToolUndo));
    public static string ActionUpload => Localization.Get(nameof(ActionUpload));
    public static string ActionPrint => Localization.Get(nameof(ActionPrint));
    public static string ActionCopy => Localization.Get(nameof(ActionCopy));
    public static string ActionSave => Localization.Get(nameof(ActionSave));
    public static string ActionClose => Localization.Get(nameof(ActionClose));
    public static string ActionRecord => Localization.Get(nameof(ActionRecord));
    public static string RecordHint => Localization.Get(nameof(RecordHint));
    public static string RecordWholeMonitor => Localization.Get(nameof(RecordWholeMonitor));
    public static string RecordCursor => Localization.Get(nameof(RecordCursor));
    public static string ActionAspect => Localization.Get(nameof(ActionAspect));
    public static string AspectFree => Localization.Get(nameof(AspectFree));

    // Notifications
    public static string Copied => Localization.Get(nameof(Copied));
    public static string CopyFailed => Localization.Get(nameof(CopyFailed));
    public static string Saved => Localization.Get(nameof(Saved));
    public static string SaveFailed => Localization.Get(nameof(SaveFailed));
    public static string PrintFailed => Localization.Get(nameof(PrintFailed));
    public static string Uploading => Localization.Get(nameof(Uploading));
    public static string Uploaded => Localization.Get(nameof(Uploaded));
    public static string UploadFailed => Localization.Get(nameof(UploadFailed));
    public static string FirstRun => Localization.Get(nameof(FirstRun));
    public static string HotkeyFailed => Localization.Get(nameof(HotkeyFailed));
    public static string SnippingWarning => Localization.Get(nameof(SnippingWarning));
    public static string RecordLimitReached => Localization.Get(nameof(RecordLimitReached));
    public static string RecordFailed => Localization.Get(nameof(RecordFailed));
    public static string RecordInterrupted => Localization.Get(nameof(RecordInterrupted));

    // Tray menu
    public static string MenuCapture => Localization.Get(nameof(MenuCapture));
    public static string MenuRecord => Localization.Get(nameof(MenuRecord));
    public static string MenuStopRecording => Localization.Get(nameof(MenuStopRecording));
    public static string MenuSaveFull => Localization.Get(nameof(MenuSaveFull));
    public static string MenuCopyFull => Localization.Get(nameof(MenuCopyFull));
    public static string MenuOpenFolder => Localization.Get(nameof(MenuOpenFolder));
    public static string MenuSettings => Localization.Get(nameof(MenuSettings));
    public static string MenuAbout => Localization.Get(nameof(MenuAbout));
    public static string MenuUpdateAvailable => Localization.Get(nameof(MenuUpdateAvailable));
    public static string MenuInstallUpdate => Localization.Get(nameof(MenuInstallUpdate));
    public static string UpdateNotification => Localization.Get(nameof(UpdateNotification));
    public static string UpdateNotificationInstall => Localization.Get(nameof(UpdateNotificationInstall));
    public static string UpdateDownloading => Localization.Get(nameof(UpdateDownloading));
    public static string UpdateFailed => Localization.Get(nameof(UpdateFailed));
    public static string UpdateBadSignature => Localization.Get(nameof(UpdateBadSignature));
    public static string MenuExit => Localization.Get(nameof(MenuExit));

    // Settings
    public static string SettingsTitle => Localization.Get(nameof(SettingsTitle));
    public static string SectionHotkeys => Localization.Get(nameof(SectionHotkeys));
    public static string TabScreenshots => Localization.Get(nameof(TabScreenshots));
    public static string TabVideo => Localization.Get(nameof(TabVideo));
    public static string TabServer => Localization.Get(nameof(TabServer));
    public static string SectionSelection => Localization.Get(nameof(SectionSelection));
    public static string SectionGeneral => Localization.Get(nameof(SectionGeneral));
    public static string SectionUpload => Localization.Get(nameof(SectionUpload));
    public static string HotkeyCapture => Localization.Get(nameof(HotkeyCapture));
    public static string HotkeyRecord => Localization.Get(nameof(HotkeyRecord));
    public static string HotkeySaveFull => Localization.Get(nameof(HotkeySaveFull));
    public static string HotkeyCopyFull => Localization.Get(nameof(HotkeyCopyFull));
    public static string HotkeyNote => Localization.Get(nameof(HotkeyNote));
    public static string AspectRatios => Localization.Get(nameof(AspectRatios));
    public static string AspectNote => Localization.Get(nameof(AspectNote));
    public static string AspectDefault => Localization.Get(nameof(AspectDefault));
    public static string AspectInvalid => Localization.Get(nameof(AspectInvalid));
    public static string Format => Localization.Get(nameof(Format));
    public static string JpegQuality => Localization.Get(nameof(JpegQuality));
    public static string Folder => Localization.Get(nameof(Folder));
    public static string Browse => Localization.Get(nameof(Browse));
    public static string AutoSave => Localization.Get(nameof(AutoSave));
    public static string OpenFolderAfterRecording => Localization.Get(nameof(OpenFolderAfterRecording));
    public static string Notifications => Localization.Get(nameof(Notifications));
    public static string RunAtStartup => Localization.Get(nameof(RunAtStartup));
    public static string CheckForUpdates => Localization.Get(nameof(CheckForUpdates));
    public static string AutoInstallUpdates => Localization.Get(nameof(AutoInstallUpdates));
    public static string UploadEnabled => Localization.Get(nameof(UploadEnabled));
    public static string UploadUrl => Localization.Get(nameof(UploadUrl));
    public static string UploadField => Localization.Get(nameof(UploadField));
    public static string UploadAuth => Localization.Get(nameof(UploadAuth));
    public static string UploadJsonPath => Localization.Get(nameof(UploadJsonPath));
    public static string UploadNote => Localization.Get(nameof(UploadNote));
    public static string Cancel => Localization.Get(nameof(Cancel));
    public static string UrlInvalid => Localization.Get(nameof(UrlInvalid));
    public static string Language => Localization.Get(nameof(Language));
    public static string LanguageNote => Localization.Get(nameof(LanguageNote));
    public static string LanguageAuto => Localization.Get(nameof(LanguageAuto));

    // About
    public static string AboutVersion => Localization.Get(nameof(AboutVersion));
    public static string AboutTagline => Localization.Get(nameof(AboutTagline));
    public static string AboutLicense => Localization.Get(nameof(AboutLicense));
    public static string AboutTesters => Localization.Get(nameof(AboutTesters));
}
