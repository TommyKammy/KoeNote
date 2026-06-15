using KoeNote.App.Dialogs;
using KoeNote.App.Models;
using KoeNote.App.Services.Audio;
using KoeNote.App.Services.Dialogs;
using KoeNote.App.Services.Review;

namespace KoeNote.App.UiIntegrationTests;

public sealed class ReviewCandidateConfirmationDialogViewModelTests
{
    [Fact]
    public void Decisions_UpdateCountsHistoryAndAllowContinueAfterAllCandidatesAreResolved()
    {
        var operations = new FakeReviewCandidateOperations();
        var recordedDecisions = new List<string>();
        var viewModel = new ReviewCandidateConfirmationDialogViewModel(new ReviewCandidateConfirmationRequest(
            "meeting.wav",
            [
                CreateCandidate("draft-001", "segment-001", "raw one", "fixed one"),
                CreateCandidate("draft-002", "segment-002", "raw two", "fixed two"),
                CreateCandidate("draft-003", "segment-003", "raw three", "fixed three")
            ],
            operations)
        {
            RecordDecision = (draft, result, selectedText) =>
                recordedDecisions.Add($"{draft.DraftId}:{result.Action}:{selectedText}")
        });

        Assert.Equal(3, viewModel.PendingCount);
        Assert.Equal(0, viewModel.DecidedCount);
        Assert.False(viewModel.CanContinue);
        Assert.Equal("fixed one", viewModel.ManualEditText);

        Assert.True(viewModel.AcceptSelected());

        Assert.Equal(1, viewModel.AcceptedCount);
        Assert.Equal(2, viewModel.PendingCount);
        Assert.Equal(1, viewModel.DecidedCount);
        Assert.Equal("draft-002", viewModel.SelectedItem?.DraftId);
        Assert.False(viewModel.CanOperate);
        Assert.Equal(["accept:draft-001"], operations.Decisions);
        Assert.Equal("採用済み", viewModel.DecidedItems[0].DecisionStatusText);
        Assert.Equal("accepted text", viewModel.DecidedItems[0].FinalText);

        viewModel.EndDecisionInputCooldown();
        viewModel.ManualEditText = " manual two ";
        Assert.True(viewModel.ApplyManualEdit());

        Assert.Equal(1, viewModel.EditedCount);
        Assert.Equal(1, viewModel.PendingCount);
        Assert.Equal(2, viewModel.DecidedCount);
        Assert.Equal("draft-003", viewModel.SelectedItem?.DraftId);
        Assert.Equal(" manual two ", operations.ManualTextByDraft["draft-002"]);

        viewModel.EndDecisionInputCooldown();
        Assert.True(viewModel.RejectSelected());

        Assert.Equal(1, viewModel.RejectedCount);
        Assert.Equal(0, viewModel.PendingCount);
        Assert.Equal(3, viewModel.DecidedCount);
        Assert.True(viewModel.CanContinue);
        Assert.Null(viewModel.SelectedItem);
        Assert.Equal(ReviewCandidateConfirmationOutcome.Continue, viewModel.CreateResult(
            ReviewCandidateConfirmationOutcome.Continue).Outcome);
        Assert.Equal(
            [
                "draft-001:accepted:fixed one",
                "draft-002:manual_edit: manual two ",
                "draft-003:rejected:fixed three"
            ],
            recordedDecisions);

        viewModel.SetFilter(ReviewCandidateConfirmationFilter.Decided);

        Assert.Equal(3, viewModel.DisplayItems.Count);
        Assert.Equal("draft-001", viewModel.SelectedItem?.DraftId);
        Assert.Equal("採用済み", viewModel.SelectedItem?.DecisionStatusText);
        Assert.Equal("accepted text", viewModel.SelectedItem?.FinalText);
    }

    [Fact]
    public void Decision_AutoSelectsNextCandidateAndBlocksImmediateRepeat()
    {
        var operations = new FakeReviewCandidateOperations();
        var viewModel = new ReviewCandidateConfirmationDialogViewModel(new ReviewCandidateConfirmationRequest(
            "meeting.wav",
            [
                CreateCandidate("draft-001", "segment-001", "raw one", "fixed one"),
                CreateCandidate("draft-002", "segment-002", "raw two", "fixed two")
            ],
            operations));

        Assert.True(viewModel.AcceptSelected());

        Assert.Equal("draft-002", viewModel.SelectedItem?.DraftId);
        Assert.False(viewModel.AcceptSelected());
        Assert.Equal(1, viewModel.AcceptedCount);
        Assert.Equal(1, viewModel.PendingCount);
        Assert.Equal(["accept:draft-001"], operations.Decisions);

        viewModel.EndDecisionInputCooldown();

        Assert.True(viewModel.AcceptSelected());
        Assert.Equal(2, viewModel.AcceptedCount);
    }

    [Fact]
    public void Decision_AutoSelectsFromCurrentPendingPosition()
    {
        var operations = new FakeReviewCandidateOperations();
        var viewModel = new ReviewCandidateConfirmationDialogViewModel(new ReviewCandidateConfirmationRequest(
            "meeting.wav",
            [
                CreateCandidate("draft-001", "segment-001", "raw one", "fixed one"),
                CreateCandidate("draft-002", "segment-002", "raw two", "fixed two"),
                CreateCandidate("draft-003", "segment-003", "raw three", "fixed three")
            ],
            operations));

        viewModel.SelectNext();

        Assert.Equal("draft-002", viewModel.SelectedItem?.DraftId);
        Assert.True(viewModel.AcceptSelected());

        Assert.Equal("draft-003", viewModel.SelectedItem?.DraftId);
        Assert.False(viewModel.CanOperate);
    }

    [Fact]
    public void Decision_SelectsPreviousPendingCandidateWhenLastItemIsResolved()
    {
        var operations = new FakeReviewCandidateOperations();
        var viewModel = new ReviewCandidateConfirmationDialogViewModel(new ReviewCandidateConfirmationRequest(
            "meeting.wav",
            [
                CreateCandidate("draft-001", "segment-001", "raw one", "fixed one"),
                CreateCandidate("draft-002", "segment-002", "raw two", "fixed two")
            ],
            operations));

        viewModel.SelectNext();

        Assert.Equal("draft-002", viewModel.SelectedItem?.DraftId);
        Assert.True(viewModel.AcceptSelected());

        Assert.Equal("draft-001", viewModel.SelectedItem?.DraftId);
        Assert.False(viewModel.CanOperate);
    }

    [Fact]
    public void Filter_CanShowPendingDecidedAndAllCandidates()
    {
        var operations = new FakeReviewCandidateOperations();
        var viewModel = new ReviewCandidateConfirmationDialogViewModel(new ReviewCandidateConfirmationRequest(
            "meeting.wav",
            [
                CreateCandidate("draft-001", "segment-001", "raw one", "fixed one"),
                CreateCandidate("draft-002", "segment-002", "raw two", "fixed two")
            ],
            operations));

        Assert.True(viewModel.AcceptSelected());

        viewModel.SetFilter(ReviewCandidateConfirmationFilter.Pending);
        Assert.Single(viewModel.DisplayItems);
        Assert.Equal("draft-002", viewModel.SelectedItem?.DraftId);

        viewModel.SetFilter(ReviewCandidateConfirmationFilter.Decided);
        Assert.Single(viewModel.DisplayItems);
        Assert.Equal("draft-001", viewModel.SelectedItem?.DraftId);
        viewModel.EndDecisionInputCooldown();
        Assert.True(viewModel.CanOperate);

        viewModel.SetFilter(ReviewCandidateConfirmationFilter.All);
        Assert.Equal(2, viewModel.DisplayItems.Count);
    }

    [Fact]
    public void DecidedCandidate_CanChangeDecisionToManualEdit()
    {
        var operations = new FakeReviewCandidateOperations();
        var viewModel = new ReviewCandidateConfirmationDialogViewModel(new ReviewCandidateConfirmationRequest(
            "meeting.wav",
            [
                CreateCandidate("draft-001", "segment-001", "raw one", "fixed one"),
                CreateCandidate("draft-002", "segment-002", "raw two", "fixed two")
            ],
            operations));

        Assert.True(viewModel.AcceptSelected());
        viewModel.EndDecisionInputCooldown();
        viewModel.SetFilter(ReviewCandidateConfirmationFilter.Decided);

        Assert.Equal("draft-001", viewModel.SelectedItem?.DraftId);
        Assert.Equal("accepted text", viewModel.ManualEditText);
        viewModel.ManualEditText = "changed one";
        Assert.True(viewModel.ApplyManualEdit());

        Assert.Equal(0, viewModel.AcceptedCount);
        Assert.Equal(0, viewModel.RejectedCount);
        Assert.Equal(1, viewModel.EditedCount);
        Assert.Equal(1, viewModel.PendingCount);
        Assert.Equal(1, viewModel.DecidedCount);
        Assert.Equal("draft-001", viewModel.SelectedItem?.DraftId);
        Assert.Equal("手修正済み", viewModel.SelectedItem?.DecisionStatusText);
        Assert.Equal("changed one", viewModel.SelectedItem?.FinalText);
        Assert.Equal("changed one", viewModel.ManualEditText);
        Assert.False(viewModel.ApplyManualEdit());
        viewModel.EndDecisionInputCooldown();
        Assert.True(viewModel.CanApplyManualEdit);
        Assert.Equal(["accept:draft-001", "change-manual:draft-001"], operations.Decisions);
    }

    [Fact]
    public void DecidedCandidate_RejectionRefreshesPendingSameSegmentCurrentText()
    {
        var operations = new FakeReviewCandidateOperations();
        var viewModel = new ReviewCandidateConfirmationDialogViewModel(new ReviewCandidateConfirmationRequest(
            "meeting.wav",
            [
                CreateCandidate("draft-001", "segment-001", "raw segment", "fixed one"),
                CreateCandidate("draft-002", "segment-001", "raw segment", "fixed two")
            ],
            operations));

        Assert.True(viewModel.AcceptSelected());
        Assert.Equal("accepted text", viewModel.SelectedItem?.CurrentText);
        viewModel.EndDecisionInputCooldown();
        viewModel.SetFilter(ReviewCandidateConfirmationFilter.Decided);

        Assert.True(viewModel.RejectSelected());
        viewModel.SetFilter(ReviewCandidateConfirmationFilter.Pending);

        Assert.Equal("draft-002", viewModel.SelectedItem?.DraftId);
        Assert.Equal("raw segment", viewModel.SelectedItem?.CurrentText);
    }

    [Fact]
    public void DecidedCandidate_RejectionRefreshesManualEditText()
    {
        var operations = new FakeReviewCandidateOperations();
        var viewModel = new ReviewCandidateConfirmationDialogViewModel(new ReviewCandidateConfirmationRequest(
            "meeting.wav",
            [CreateCandidate("draft-001", "segment-001", "raw one", "fixed one")],
            operations));

        Assert.True(viewModel.AcceptSelected());
        viewModel.EndDecisionInputCooldown();
        viewModel.SetFilter(ReviewCandidateConfirmationFilter.Decided);

        Assert.Equal("accepted text", viewModel.ManualEditText);
        Assert.True(viewModel.RejectSelected());

        Assert.Equal("raw one", viewModel.SelectedItem?.FinalText);
        Assert.Equal("raw one", viewModel.ManualEditText);
        Assert.False(viewModel.AcceptSelected());
    }

    [Fact]
    public void Filter_ReassertsActiveStateWhenCurrentFilterIsSelectedAgain()
    {
        var operations = new FakeReviewCandidateOperations();
        var viewModel = new ReviewCandidateConfirmationDialogViewModel(new ReviewCandidateConfirmationRequest(
            "meeting.wav",
            [CreateCandidate("draft-001", "segment-001", "raw one", "fixed one")],
            operations));
        var propertyNames = new List<string>();
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is not null)
            {
                propertyNames.Add(args.PropertyName);
            }
        };

        viewModel.SetFilter(ReviewCandidateConfirmationFilter.Pending);

        Assert.True(viewModel.IsPendingFilterActive);
        Assert.Contains(nameof(viewModel.IsPendingFilterActive), propertyNames);
        Assert.Contains(nameof(viewModel.IsDecidedFilterActive), propertyNames);
        Assert.Contains(nameof(viewModel.IsAllFilterActive), propertyNames);
    }

    [Fact]
    public void PostCommitRecordDecisionFailure_DoesNotLeaveCommittedDraftPending()
    {
        var operations = new FakeReviewCandidateOperations();
        var viewModel = new ReviewCandidateConfirmationDialogViewModel(new ReviewCandidateConfirmationRequest(
            "meeting.wav",
            [CreateCandidate("draft-001", "segment-001", "raw one", "fixed one")],
            operations)
        {
            RecordDecision = (_, _, _) => throw new InvalidOperationException("memory unavailable")
        });

        Assert.True(viewModel.AcceptSelected());

        Assert.Equal(1, viewModel.AcceptedCount);
        Assert.Equal(0, viewModel.PendingCount);
        Assert.Equal(1, viewModel.DecidedCount);
        Assert.True(viewModel.CanContinue);
        Assert.Contains("補正メモリの更新に失敗しました", viewModel.OperationErrorText, StringComparison.Ordinal);
        Assert.Equal(["accept:draft-001"], operations.Decisions);
    }

    [Fact]
    public void SameSegmentRemainingCandidate_UsesFinalTextFromPreviousDecision()
    {
        var operations = new FakeReviewCandidateOperations();
        var viewModel = new ReviewCandidateConfirmationDialogViewModel(new ReviewCandidateConfirmationRequest(
            "meeting.wav",
            [
                CreateCandidate("draft-001", "segment-001", "raw one", "fixed one"),
                CreateCandidate("draft-002", "segment-001", "raw two", "fixed two")
            ],
            operations));

        Assert.True(viewModel.AcceptSelected());

        Assert.Equal("draft-002", viewModel.SelectedItem?.DraftId);
        Assert.Equal("accepted text", viewModel.SelectedItem?.CurrentText);
    }

    [Fact]
    public async Task Playback_CanPlaySelectedCandidateAndStopsWhenSelectionChanges()
    {
        var audioPath = CreateAudioFile();
        var playback = new FakeAudioPlaybackService();
        var viewModel = new ReviewCandidateConfirmationDialogViewModel(new ReviewCandidateConfirmationRequest(
            "meeting.wav",
            [
                CreateCandidate("draft-001", "segment-001", "raw one", "fixed one"),
                CreateCandidate("draft-002", "segment-002", "raw two", "fixed two")
            ],
            new FakeReviewCandidateOperations())
        {
            AudioPath = audioPath
        }, playback);

        Assert.True(viewModel.CanPlaySelectedPreview);

        await viewModel.TogglePreviewAsync();

        Assert.True(viewModel.IsPreviewPlaying);
        Assert.Equal(TimeSpan.FromSeconds(1), playback.SeekPosition);
        Assert.Equal("再生中", viewModel.PreviewPlaybackStatusText);

        viewModel.SelectNext();

        Assert.Equal("draft-002", viewModel.SelectedItem?.DraftId);
        Assert.False(viewModel.IsPreviewPlaying);
        Assert.False(playback.IsPlaying);
        Assert.True(playback.StopCount >= 1);
    }

    [Fact]
    public async Task Playback_StopsWhenCandidateDecisionIsApplied()
    {
        var audioPath = CreateAudioFile();
        var playback = new FakeAudioPlaybackService();
        var operations = new FakeReviewCandidateOperations();
        var viewModel = new ReviewCandidateConfirmationDialogViewModel(new ReviewCandidateConfirmationRequest(
            "meeting.wav",
            [
                CreateCandidate("draft-001", "segment-001", "raw one", "fixed one"),
                CreateCandidate("draft-002", "segment-002", "raw two", "fixed two")
            ],
            operations)
        {
            AudioPath = audioPath
        }, playback);

        await viewModel.TogglePreviewAsync();
        Assert.True(viewModel.IsPreviewPlaying);

        Assert.True(viewModel.AcceptSelected());

        Assert.False(viewModel.IsPreviewPlaying);
        Assert.False(playback.IsPlaying);
        Assert.Equal("draft-002", viewModel.SelectedItem?.DraftId);
        Assert.False(viewModel.CanOperate);
    }

    [Fact]
    public void Playback_IsDisabledWithoutAudioPathOrCandidateRange()
    {
        var operations = new FakeReviewCandidateOperations();
        var withoutAudioPath = new ReviewCandidateConfirmationDialogViewModel(new ReviewCandidateConfirmationRequest(
            "meeting.wav",
            [CreateCandidate("draft-001", "segment-001", "raw one", "fixed one")],
            operations));

        Assert.False(withoutAudioPath.CanPlaySelectedPreview);
        Assert.Equal("音声なし", withoutAudioPath.PreviewPlaybackStatusText);

        var withoutRange = new ReviewCandidateConfirmationDialogViewModel(new ReviewCandidateConfirmationRequest(
            "meeting.wav",
            [CreateCandidate("draft-001", "segment-001", "raw one", "fixed one", null, null)],
            operations)
        {
            AudioPath = CreateAudioFile()
        }, new FakeAudioPlaybackService());

        Assert.False(withoutRange.CanPlaySelectedPreview);
    }

    [Fact]
    public async Task Playback_CanPlayDecidedCandidateAfterReturningToHistory()
    {
        var audioPath = CreateAudioFile();
        var playback = new FakeAudioPlaybackService();
        var operations = new FakeReviewCandidateOperations();
        var viewModel = new ReviewCandidateConfirmationDialogViewModel(new ReviewCandidateConfirmationRequest(
            "meeting.wav",
            [
                CreateCandidate("draft-001", "segment-001", "raw one", "fixed one"),
                CreateCandidate("draft-002", "segment-002", "raw two", "fixed two", 4, 6)
            ],
            operations)
        {
            AudioPath = audioPath
        }, playback);

        Assert.True(viewModel.AcceptSelected());
        viewModel.EndDecisionInputCooldown();
        viewModel.SetFilter(ReviewCandidateConfirmationFilter.Decided);

        Assert.Equal("draft-001", viewModel.SelectedItem?.DraftId);
        Assert.True(viewModel.CanPlaySelectedPreview);

        await viewModel.TogglePreviewAsync();

        Assert.True(viewModel.IsPreviewPlaying);
        Assert.Equal(TimeSpan.FromSeconds(1), playback.SeekPosition);
    }

    [Fact]
    public async Task Playback_StopsWhenFilterChangeSelectsAnotherCandidate()
    {
        var audioPath = CreateAudioFile();
        var playback = new FakeAudioPlaybackService();
        var operations = new FakeReviewCandidateOperations();
        var viewModel = new ReviewCandidateConfirmationDialogViewModel(new ReviewCandidateConfirmationRequest(
            "meeting.wav",
            [
                CreateCandidate("draft-001", "segment-001", "raw one", "fixed one"),
                CreateCandidate("draft-002", "segment-002", "raw two", "fixed two", 4, 6)
            ],
            operations)
        {
            AudioPath = audioPath
        }, playback);

        Assert.True(viewModel.AcceptSelected());
        viewModel.EndDecisionInputCooldown();
        Assert.Equal("draft-002", viewModel.SelectedItem?.DraftId);

        await viewModel.TogglePreviewAsync();
        Assert.True(viewModel.IsPreviewPlaying);

        viewModel.SetFilter(ReviewCandidateConfirmationFilter.Decided);

        Assert.Equal("draft-001", viewModel.SelectedItem?.DraftId);
        Assert.False(viewModel.IsPreviewPlaying);
        Assert.False(playback.IsPlaying);
        Assert.True(playback.StopCount >= 1);
    }

    [Fact]
    public void Playback_CommandIsDisabledWhenAudioFileIsMissing()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), "KoeNote.UiIntegrationTests", Guid.NewGuid().ToString("N"), "missing.wav");
        var viewModel = new ReviewCandidateConfirmationDialogViewModel(new ReviewCandidateConfirmationRequest(
            "meeting.wav",
            [CreateCandidate("draft-001", "segment-001", "raw one", "fixed one")],
            new FakeReviewCandidateOperations())
        {
            AudioPath = missingPath
        }, new FakeAudioPlaybackService());

        Assert.False(viewModel.CanPlaySelectedPreview);
        Assert.False(viewModel.TogglePreviewCommand.CanExecute(null));
    }

    private static ReviewCandidateConfirmationItem CreateCandidate(
        string draftId,
        string segmentId,
        string originalText,
        string suggestedText,
        double? startSeconds = 1,
        double? endSeconds = 2)
    {
        return new ReviewCandidateConfirmationItem(
            new CorrectionDraft(
                draftId,
                "job-001",
                segmentId,
                "表記ゆれ",
                originalText,
                suggestedText,
                "候補理由",
                0.8),
            StartSeconds: startSeconds,
            EndSeconds: endSeconds,
            SpeakerName: "Speaker_0",
            CurrentText: originalText);
    }

    private static string CreateAudioFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "KoeNote.UiIntegrationTests", Guid.NewGuid().ToString("N"), "meeting.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "audio");
        return path;
    }

    private sealed class FakeReviewCandidateOperations : IReviewCandidateConfirmationOperations
    {
        public List<string> Decisions { get; } = [];

        public Dictionary<string, string> ManualTextByDraft { get; } = new(StringComparer.Ordinal);

        public ReviewOperationResult AcceptDraft(string draftId)
        {
            Decisions.Add($"accept:{draftId}");
            return CreateResult(draftId, "accepted", "accepted text");
        }

        public ReviewOperationResult RejectDraft(string draftId)
        {
            Decisions.Add($"reject:{draftId}");
            return CreateResult(draftId, "rejected", null);
        }

        public ReviewOperationResult ApplyManualEdit(string draftId, string finalText, string? manualNote = null)
        {
            Decisions.Add($"manual:{draftId}");
            ManualTextByDraft[draftId] = finalText;
            return CreateResult(draftId, "manual_edit", finalText);
        }

        public ReviewOperationResult ChangeToAccepted(string draftId)
        {
            Decisions.Add($"change-accept:{draftId}");
            return CreateResult(draftId, "accepted", "changed accepted text");
        }

        public ReviewOperationResult ChangeToRejected(string draftId)
        {
            Decisions.Add($"change-reject:{draftId}");
            return CreateResult(draftId, "rejected", null);
        }

        public ReviewOperationResult ChangeToManualEdit(string draftId, string finalText, string? manualNote = null)
        {
            Decisions.Add($"change-manual:{draftId}");
            ManualTextByDraft[draftId] = finalText;
            return CreateResult(draftId, "manual_edit", finalText);
        }

        private static ReviewOperationResult CreateResult(string draftId, string action, string? finalText)
        {
            return new ReviewOperationResult("job-001", "segment-001", draftId, action, finalText, 0);
        }
    }

    private sealed class FakeAudioPlaybackService : IAudioPlaybackService
    {
        public event EventHandler? PlaybackStateChanged;

        public bool IsPlaying { get; private set; }

        public string? CurrentPath { get; private set; }

        public TimeSpan Position { get; set; }

        public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(60);

        public string? OpenedPath { get; private set; }

        public TimeSpan SeekPosition { get; private set; }

        public int StopCount { get; private set; }

        public bool Toggle(string audioPath)
        {
            CurrentPath = audioPath;
            IsPlaying = !IsPlaying;
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            return IsPlaying;
        }

        public bool Open(string audioPath)
        {
            OpenedPath = audioPath;
            CurrentPath = audioPath;
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public void Seek(TimeSpan position)
        {
            SeekPosition = position;
            Position = position;
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SetPlaybackRate(double rate)
        {
        }

        public void SetVolume(double volume)
        {
        }

        public void Stop()
        {
            StopCount++;
            IsPlaying = false;
            CurrentPath = null;
            PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
