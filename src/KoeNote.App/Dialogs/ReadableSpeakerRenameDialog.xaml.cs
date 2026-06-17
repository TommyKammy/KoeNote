using System.Windows;
using System.Windows.Input;

namespace KoeNote.App.Dialogs;

public partial class ReadableSpeakerRenameDialog : Window
{
    public ReadableSpeakerRenameDialog(string currentSpeaker)
    {
        InitializeComponent();
        CurrentSpeakerText.Text = currentSpeaker;
        SpeakerNameTextBox.Text = currentSpeaker;
        SpeakerNameTextBox.SelectAll();
        SpeakerNameTextBox.Focus();
    }

    public string SpeakerName => SpeakerNameTextBox.Text.Trim();

    private void OnApplyClick(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SpeakerName))
        {
            MessageBox.Show(
                this,
                "話者名を入力してください。",
                "話者名を変更",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            SpeakerNameTextBox.Focus();
            return;
        }

        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
        }
        else if (e.Key == Key.Enter)
        {
            OnApplyClick(sender, e);
        }
    }
}
