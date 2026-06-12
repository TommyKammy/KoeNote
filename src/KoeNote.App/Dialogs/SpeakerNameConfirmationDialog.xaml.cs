using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KoeNote.App.Services.Dialogs;

namespace KoeNote.App.Dialogs;

public partial class SpeakerNameConfirmationDialog : Window
{
    private SpeakerNameConfirmationDialogViewModel ViewModel => (SpeakerNameConfirmationDialogViewModel)DataContext;

    public SpeakerNameConfirmationDialog(SpeakerNameConfirmationRequest request)
    {
        InitializeComponent();
        DataContext = new SpeakerNameConfirmationDialogViewModel(request);
    }

    public SpeakerNameConfirmationResult? Result { get; private set; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        ViewModel.RefreshValidation();
        if (!ViewModel.CanConfirm)
        {
            return;
        }

        Result = ViewModel.CreateResult();
        DialogResult = true;
    }

    private void OnDisplayNameTextChanged(object sender, TextChangedEventArgs e)
    {
        ViewModel.RefreshValidation();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OnConfirmClick(sender, e);
            e.Handled = true;
        }
    }
}

internal sealed class SpeakerNameConfirmationDialogViewModel : INotifyPropertyChanged
{
    public SpeakerNameConfirmationDialogViewModel(SpeakerNameConfirmationRequest request)
    {
        LeadText = $"{request.JobTitle} の話者名を確認してから整文を開始します。";
        Items = new ObservableCollection<SpeakerNameConfirmationDialogItem>(
            request.Speakers.Select(speaker => new SpeakerNameConfirmationDialogItem(speaker)));
        RefreshValidation();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string LeadText { get; }

    public ObservableCollection<SpeakerNameConfirmationDialogItem> Items { get; }

    public bool CanConfirm => Items.Count > 0 && Items.All(static item => !item.HasError);

    public string ValidationSummary => CanConfirm
        ? string.Empty
        : "空の話者名は保存できません。80文字以内で入力してください。";

    public void RefreshValidation()
    {
        foreach (var item in Items)
        {
            item.RefreshValidation();
        }

        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(ValidationSummary));
    }

    public SpeakerNameConfirmationResult CreateResult()
    {
        return new SpeakerNameConfirmationResult(
            Items.ToDictionary(
                static item => item.SpeakerId,
                static item => item.DisplayName.Trim(),
                StringComparer.OrdinalIgnoreCase));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

internal sealed class SpeakerNameConfirmationDialogItem : INotifyPropertyChanged
{
    private string _displayName;
    private string _errorText = string.Empty;

    public SpeakerNameConfirmationDialogItem(SpeakerNameConfirmationItem item)
    {
        SpeakerId = item.SpeakerId;
        _displayName = item.EffectiveDisplayName;
        OriginalLabel = string.Equals(item.EffectiveDisplayName, item.SpeakerId, StringComparison.OrdinalIgnoreCase)
            ? item.SpeakerId
            : $"{item.EffectiveDisplayName} / {item.SpeakerId}";
        SegmentCountText = $"{item.SegmentCount}件の発話";
        PreviewText = item.PreviewTexts.Count == 0
            ? "代表発話はありません。"
            : string.Join(Environment.NewLine, item.PreviewTexts.Take(3));
        RefreshValidation();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string SpeakerId { get; }

    public string OriginalLabel { get; }

    public string SegmentCountText { get; }

    public string PreviewText { get; }

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (string.Equals(_displayName, value, StringComparison.Ordinal))
            {
                return;
            }

            _displayName = value;
            RefreshValidation();
            OnPropertyChanged();
        }
    }

    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (string.Equals(_errorText, value, StringComparison.Ordinal))
            {
                return;
            }

            _errorText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    public void RefreshValidation()
    {
        ErrorText = string.IsNullOrWhiteSpace(DisplayName)
            ? "話者名を入力してください。"
            : DisplayName.Trim().Length > 80
                ? "話者名は80文字以内で入力してください。"
                : string.Empty;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
