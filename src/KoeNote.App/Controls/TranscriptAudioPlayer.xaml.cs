using System.Windows;
using System.Windows.Controls;

namespace KoeNote.App.Controls;

public partial class TranscriptAudioPlayer : UserControl
{
    public static readonly DependencyProperty IsSlimProperty = DependencyProperty.Register(
        nameof(IsSlim),
        typeof(bool),
        typeof(TranscriptAudioPlayer),
        new PropertyMetadata(false));

    public TranscriptAudioPlayer()
    {
        InitializeComponent();
    }

    public bool IsSlim
    {
        get => (bool)GetValue(IsSlimProperty);
        set => SetValue(IsSlimProperty, value);
    }
}
