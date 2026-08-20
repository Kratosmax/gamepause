namespace GamePause.App;

internal enum UpdatePromptSelection { Later, Update, Skip }

public partial class UpdatePromptWindow : System.Windows.Window
{
    internal UpdatePromptWindow(string currentVersion, string newVersion, string? releaseNotes)
    {
        InitializeComponent();
        VersionText.Text = $"当前版本 {currentVersion}  ·  新版本 {newVersion}";
        NotesText.Text = string.IsNullOrWhiteSpace(releaseNotes) ? "此版本没有提供更新说明。" : releaseNotes.Trim();
    }

    internal UpdatePromptSelection Selection { get; private set; } = UpdatePromptSelection.Later;

    private void Update_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Selection = UpdatePromptSelection.Update;
        DialogResult = true;
    }

    private void Later_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Selection = UpdatePromptSelection.Later;
        DialogResult = false;
    }

    private void Skip_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        Selection = UpdatePromptSelection.Skip;
        DialogResult = true;
    }
}
