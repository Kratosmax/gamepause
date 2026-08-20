namespace GamePause.App;

public partial class ElevationPromptWindow : System.Windows.Window
{
    internal ElevationPromptWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WpfBackdrop.TryApply(this);
    }

    private void Restart_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
