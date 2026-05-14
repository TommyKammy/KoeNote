using KoeNote.App.Models;
using KoeNote.App.Services;
using KoeNote.App.Services.Asr;
using KoeNote.App.Services.Export;
using KoeNote.App.Services.Jobs;
using KoeNote.App.Services.Review;
using KoeNote.App.Services.Transcript;
using System.IO.Compression;
using System.Xml.Linq;

namespace KoeNote.App.Tests;

public sealed class TranscriptExportServiceTests
{
    [Fact]
    public void ExportJob_WritesFilesUsingFallbackTextAndSpeakerAliases()
    {
        var paths = TestDatabase.CreateReadyPaths();
        TestDatabase.InsertReviewReadyJob(paths, "job-001", "meeting");
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("seg-001", "job-001", 1, 2.5, "spk-1", "raw one", "normalized one"),
            new TranscriptSegment("seg-002", "job-001", 3, 4.25, "spk-2", "raw two")
        ]);
        SetFinalText(paths, "job-001", "seg-001", "final one");
        SetSpeakerAlias(paths, "job-001", "spk-1", "Alice");
        new CorrectionDraftRepository(paths).SaveDrafts([
            new CorrectionDraft("draft-001", "job-001", "seg-002", "wording", "raw", "fixed", "reason", 0.8)
        ]);
        var output = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "exports");

        var result = new TranscriptExportService(paths).ExportJob(
            "job-001",
            output,
            null,
            new TranscriptExportOptions(Source: TranscriptExportSource.Polished));

        Assert.True(result.HasUnresolvedDrafts);
        Assert.Equal(1, result.PendingDraftCount);
        Assert.Equal(5, result.FilePaths.Count);

        var text = File.ReadAllText(Path.Combine(output, "meeting.txt"));
        Assert.Contains("Alice: final one", text, StringComparison.Ordinal);
        Assert.Contains("spk-2: raw two", text, StringComparison.Ordinal);

        var json = File.ReadAllText(Path.Combine(output, "meeting.json"));
        Assert.Contains("\"PendingDraftCount\": 1", json, StringComparison.Ordinal);
        Assert.Contains("\"Text\": \"final one\"", json, StringComparison.Ordinal);

        var markdown = File.ReadAllText(Path.Combine(output, "meeting.md"));
        Assert.Contains("1件の未処理のレビュー候補が残っています。", markdown, StringComparison.Ordinal);

        var srt = File.ReadAllText(Path.Combine(output, "meeting.srt"));
        Assert.Contains("00:00:01,000 --> 00:00:02,500", srt, StringComparison.Ordinal);

        var vtt = File.ReadAllText(Path.Combine(output, "meeting.vtt"));
        Assert.StartsWith("WEBVTT", vtt, StringComparison.Ordinal);
        Assert.Contains("00:00:03.000 --> 00:00:04.250", vtt, StringComparison.Ordinal);

        var logs = new JobLogRepository(paths).ReadLatest("job-001");
        Assert.Contains(logs, entry => entry.Stage == "export" && entry.Level == "warning");
    }

    [Fact]
    public void ExportJob_DefaultSourceWritesReadableDocumentFormats()
    {
        var paths = TestDatabase.CreateReadyPaths();
        TestDatabase.InsertReviewReadyJob(paths, "job-001", "meeting");
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("seg-001", "job-001", 0, 1, "spk-1", "raw")
        ]);
        var derivativeRepository = new TranscriptDerivativeRepository(paths);
        derivativeRepository.Save(new TranscriptDerivativeSaveRequest(
            "job-001",
            TranscriptDerivativeKinds.Polished,
            TranscriptDerivativeFormats.PlainText,
            "[00:00 - 00:01] Alice: 読める本文です。",
            TranscriptDerivativeSourceKinds.Raw,
            derivativeRepository.ComputeCurrentRawTranscriptHash("job-001"),
            "seg-001..seg-001",
            null,
            "model",
            "prompt",
            "profile"));
        var output = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "exports");

        var result = new TranscriptExportService(paths).ExportJob("job-001", output);

        Assert.Equal(4, result.FilePaths.Count);
        Assert.True(File.Exists(Path.Combine(output, "meeting.txt")));
        Assert.True(File.Exists(Path.Combine(output, "meeting.md")));
        Assert.True(File.Exists(Path.Combine(output, "meeting.docx")));
        Assert.True(File.Exists(Path.Combine(output, "meeting.xlsx")));
        Assert.False(File.Exists(Path.Combine(output, "meeting.json")));
        Assert.False(File.Exists(Path.Combine(output, "meeting.srt")));
        Assert.Contains("読める本文です。", File.ReadAllText(Path.Combine(output, "meeting.txt")), StringComparison.Ordinal);
    }

    [Fact]
    public void ExportJob_CanUseRawTranscriptSource()
    {
        var paths = TestDatabase.CreateReadyPaths();
        TestDatabase.InsertReviewReadyJob(paths, "job-001", "meeting");
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("seg-001", "job-001", 1, 2.5, "spk-1", "raw one", "normalized one")
        ]);
        SetFinalText(paths, "job-001", "seg-001", "final one");
        var output = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "exports");

        new TranscriptExportService(paths).ExportJob(
            "job-001",
            output,
            [TranscriptExportFormat.Text],
            new TranscriptExportOptions(IncludeTimestamps: false, Source: TranscriptExportSource.Raw));

        var text = File.ReadAllText(Path.Combine(output, "meeting.txt"));
        Assert.Contains("raw one", text, StringComparison.Ordinal);
        Assert.DoesNotContain("final one", text, StringComparison.Ordinal);
        Assert.DoesNotContain("normalized one", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportJob_CanWriteSelectedFormatsOnly()
    {
        var paths = TestDatabase.CreateReadyPaths();
        TestDatabase.InsertReviewReadyJob(paths, "job-001", "selected");
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("seg-001", "job-001", 0, 1, "spk-1", "raw")
        ]);
        var output = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "exports");

        var result = new TranscriptExportService(paths).ExportJob(
            "job-001",
            output,
            [TranscriptExportFormat.Text],
            new TranscriptExportOptions(Source: TranscriptExportSource.Polished));

        var file = Assert.Single(result.FilePaths);
        Assert.Equal(Path.Combine(output, "selected.txt"), file);
        Assert.True(File.Exists(file));
        Assert.False(File.Exists(Path.Combine(output, "selected.json")));
    }

    [Fact]
    public void ExportJob_UsesCustomBaseFileNameAndCanHideDisplayTimestamps()
    {
        var paths = TestDatabase.CreateReadyPaths();
        TestDatabase.InsertReviewReadyJob(paths, "job-001", "selected");
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("seg-001", "job-001", 0, 9.8, "", "hello")
        ]);
        var output = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "exports");

        var result = new TranscriptExportService(paths).ExportJob(
            "job-001",
            output,
            [TranscriptExportFormat.Text, TranscriptExportFormat.Docx],
            new TranscriptExportOptions("custom name", IncludeTimestamps: false, Source: TranscriptExportSource.Polished));

        Assert.Contains(Path.Combine(output, "custom name.txt"), result.FilePaths);
        Assert.Contains(Path.Combine(output, "custom name.docx"), result.FilePaths);
        var text = File.ReadAllText(Path.Combine(output, "custom name.txt"));
        Assert.Equal($"hello{Environment.NewLine}", text);
        Assert.DoesNotContain("[00:00:00.000 - 00:00:09.800]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportJobToFile_WritesRequestedFormatToExactPath()
    {
        var paths = TestDatabase.CreateReadyPaths();
        TestDatabase.InsertReviewReadyJob(paths, "job-001", "selected");
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("seg-001", "job-001", 0, 9.8, "", "hello")
        ]);
        var output = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "exports", "custom-title.txt");

        var result = new TranscriptExportService(paths).ExportJobToFile(
            "job-001",
            output,
            TranscriptExportFormat.Text,
            new TranscriptExportOptions(IncludeTimestamps: false, Source: TranscriptExportSource.Polished));

        var file = Assert.Single(result.FilePaths);
        Assert.Equal(output, file);
        Assert.Equal($"hello{Environment.NewLine}", File.ReadAllText(output));
    }

    [Fact]
    public void ExportJob_CanWriteDocx()
    {
        var paths = TestDatabase.CreateReadyPaths();
        TestDatabase.InsertReviewReadyJob(paths, "job-001", "docx-export");
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("seg-001", "job-001", 0, 1, "spk-1", "hello docx")
        ]);
        var output = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "exports");

        var result = new TranscriptExportService(paths).ExportJob(
            "job-001",
            output,
            [TranscriptExportFormat.Docx],
            new TranscriptExportOptions(Source: TranscriptExportSource.Polished));

        var file = Assert.Single(result.FilePaths);
        Assert.Equal(Path.Combine(output, "docx-export.docx"), file);
        using var archive = ZipFile.OpenRead(file);
        Assert.NotNull(archive.GetEntry("[Content_Types].xml"));
        var document = archive.GetEntry("word/document.xml");
        Assert.NotNull(document);
        using var reader = new StreamReader(document.Open());
        var xml = reader.ReadToEnd();
        Assert.Contains("hello docx", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportJob_CanWriteReadablePolishedDerivative()
    {
        var paths = TestDatabase.CreateReadyPaths();
        TestDatabase.InsertReviewReadyJob(paths, "job-001", "meeting");
        var derivativeRepository = new TranscriptDerivativeRepository(paths);
        derivativeRepository.Save(new TranscriptDerivativeSaveRequest(
            "job-001",
            TranscriptDerivativeKinds.Polished,
            TranscriptDerivativeFormats.PlainText,
            """
            # After

            。

            [00:00:01 - 00:00:02] Speaker_0: 入力側の混入です。
            Output:
            [00:00:01 - 00:00:02] Speaker_0: 読みやすい整文です。
            [00:00:02 - 00:00:03] Speaker_1: 次の発話です。
            """,
            TranscriptDerivativeSourceKinds.Raw,
            "hash",
            "seg-001..seg-002",
            null,
            "model",
            "prompt",
            "profile"));
        var output = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "exports");

        var result = new TranscriptExportService(paths).ExportJob(
            "job-001",
            output,
            [TranscriptExportFormat.Text, TranscriptExportFormat.Markdown, TranscriptExportFormat.Docx],
            new TranscriptExportOptions(Source: TranscriptExportSource.ReadablePolished));

        Assert.Equal(3, result.FilePaths.Count);
        var text = File.ReadAllText(Path.Combine(output, "meeting.txt"));
        Assert.Contains("読みやすい整文です。", text, StringComparison.Ordinal);
        Assert.Contains($"読みやすい整文です。{Environment.NewLine}{Environment.NewLine}[00:00:02", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Output:", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("入力側", text, StringComparison.Ordinal);
        Assert.DoesNotContain("# After", text, StringComparison.Ordinal);
        Assert.DoesNotContain("[00:00:00.000 - 00:00:00.000]", text, StringComparison.Ordinal);

        var markdown = File.ReadAllText(Path.Combine(output, "meeting.md"));
        Assert.StartsWith("[00:00:01 - 00:00:02] Speaker_0: 読みやすい整文です。", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("# meeting", markdown, StringComparison.Ordinal);
        Assert.Contains("Speaker_1: 次の発話です。", markdown, StringComparison.Ordinal);

        var docxXml = ReadDocxDocumentXml(Path.Combine(output, "meeting.docx"));
        Assert.Contains("読みやすい整文です。", docxXml, StringComparison.Ordinal);
        Assert.DoesNotContain("meeting", docxXml, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportJob_RejectsBrokenReadablePolishedDerivative()
    {
        var paths = TestDatabase.CreateReadyPaths();
        TestDatabase.InsertReviewReadyJob(paths, "job-001", "meeting");
        new TranscriptDerivativeRepository(paths).Save(new TranscriptDerivativeSaveRequest(
            "job-001",
            TranscriptDerivativeKinds.Polished,
            TranscriptDerivativeFormats.PlainText,
            """
            破損した読みやすく整文です。
            �
            同じ行です。
            同じ行です。
            同じ行です。
            同じ行です。
            """,
            TranscriptDerivativeSourceKinds.Raw,
            "hash",
            "seg-001..seg-002",
            null,
            "model",
            "prompt",
            "profile"));
        var output = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "exports");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            new TranscriptExportService(paths).ExportJob(
                "job-001",
                output,
                [TranscriptExportFormat.Text],
                new TranscriptExportOptions(Source: TranscriptExportSource.ReadablePolished)));

        Assert.Contains("not usable", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportJob_CanWriteRawXlsx()
    {
        var paths = TestDatabase.CreateReadyPaths();
        TestDatabase.InsertReviewReadyJob(paths, "job-001", "xlsx-export");
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("seg-001", "job-001", 1.25, 2.5, "spk-1", "raw <one>\nsecond & line"),
            new TranscriptSegment("seg-002", "job-001", 3, 4.125, "spk-2", "raw two")
        ]);
        SetSpeakerAlias(paths, "job-001", "spk-1", "Alice");
        var output = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "exports");

        var result = new TranscriptExportService(paths).ExportJob(
            "job-001",
            output,
            [TranscriptExportFormat.Xlsx],
            new TranscriptExportOptions(Source: TranscriptExportSource.Raw));

        var file = Assert.Single(result.FilePaths);
        Assert.Equal(Path.Combine(output, "xlsx-export.xlsx"), file);
        Assert.True(File.Exists(file));

        using var archive = ZipFile.OpenRead(file);
        Assert.NotNull(archive.GetEntry("[Content_Types].xml"));
        Assert.NotNull(archive.GetEntry("xl/styles.xml"));

        var workbook = ReadZipXml(archive, "xl/workbook.xml");
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        Assert.Equal("Transcript", workbook.Descendants(main + "sheet").Single().Attribute("name")?.Value);

        var worksheet = ReadZipXml(archive, "xl/worksheets/sheet1.xml");
        var cells = ReadInlineStringCells(worksheet);
        Assert.Equal("開始時刻", cells["A1"]);
        Assert.Equal("終了時刻", cells["B1"]);
        Assert.Equal("話者", cells["C1"]);
        Assert.Equal("素起こし", cells["D1"]);
        Assert.Equal("00:00:01.250", cells["A2"]);
        Assert.Equal("00:00:02.500", cells["B2"]);
        Assert.Equal("Alice", cells["C2"]);
        Assert.Equal("raw <one>\nsecond & line", cells["D2"]);
        Assert.Equal("00:00:03.000", cells["A3"]);
        Assert.Equal("00:00:04.125", cells["B3"]);
        Assert.Equal("spk-2", cells["C3"]);
        Assert.Equal("raw two", cells["D3"]);
        Assert.False(cells.ContainsKey("E1"));
    }

    [Fact]
    public void ExportJob_CanWritePolishedXlsx()
    {
        var paths = TestDatabase.CreateReadyPaths();
        TestDatabase.InsertReviewReadyJob(paths, "job-001", "polished-xlsx");
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("seg-001", "job-001", 1.25, 2.5, "spk-1", "raw <one>\nsecond & line")
        ]);
        SetFinalText(paths, "job-001", "seg-001", "polished <one>\nsecond & line");
        SetSpeakerAlias(paths, "job-001", "spk-1", "Alice");
        var output = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "exports");

        var result = new TranscriptExportService(paths).ExportJob(
            "job-001",
            output,
            [TranscriptExportFormat.Xlsx],
            new TranscriptExportOptions(Source: TranscriptExportSource.Polished));

        var file = Assert.Single(result.FilePaths);
        Assert.Equal(Path.Combine(output, "polished-xlsx.xlsx"), file);
        using var archive = ZipFile.OpenRead(file);
        var worksheet = ReadZipXml(archive, "xl/worksheets/sheet1.xml");
        var cells = ReadInlineStringCells(worksheet);
        Assert.Equal("開始時刻", cells["A1"]);
        Assert.Equal("終了時刻", cells["B1"]);
        Assert.Equal("話者", cells["C1"]);
        Assert.Equal("レビュー候補", cells["D1"]);
        Assert.Equal("00:00:01.250", cells["A2"]);
        Assert.Equal("00:00:02.500", cells["B2"]);
        Assert.Equal("Alice", cells["C2"]);
        Assert.Equal("polished <one>\nsecond & line", cells["D2"]);
        Assert.False(cells.ContainsKey("E1"));
    }

    [Fact]
    public void ExportJob_CanWriteReadablePolishedXlsx()
    {
        var paths = TestDatabase.CreateReadyPaths();
        TestDatabase.InsertReviewReadyJob(paths, "job-001", "readable-xlsx");
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("seg-001", "job-001", 0, 1, "spk-1", "raw")
        ]);
        var derivativeRepository = new TranscriptDerivativeRepository(paths);
        derivativeRepository.Save(new TranscriptDerivativeSaveRequest(
            "job-001",
            TranscriptDerivativeKinds.Polished,
            TranscriptDerivativeFormats.PlainText,
            "[00:00 - 00:01] Alice: 読みやすい本文です。",
            TranscriptDerivativeSourceKinds.Raw,
            derivativeRepository.ComputeCurrentRawTranscriptHash("job-001"),
            "seg-001..seg-001",
            null,
            "model",
            "prompt",
            "profile"));
        var output = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "exports");

        var result = new TranscriptExportService(paths).ExportJob(
            "job-001",
            output,
            [TranscriptExportFormat.Xlsx],
            new TranscriptExportOptions(Source: TranscriptExportSource.ReadablePolished));

        var file = Assert.Single(result.FilePaths);
        Assert.Equal(Path.Combine(output, "readable-xlsx.xlsx"), file);
        using var archive = ZipFile.OpenRead(file);
        var worksheet = ReadZipXml(archive, "xl/worksheets/sheet1.xml");
        var cells = ReadInlineStringCells(worksheet);
        Assert.Equal("整文", cells["A1"]);
        Assert.Equal("[00:00 - 00:01] Alice: 読みやすい本文です。", cells["A2"]);
        Assert.False(cells.ContainsKey("B1"));
    }

    [Fact]
    public void ExportJob_CanMergeConsecutiveSpeakersForReadableFormats()
    {
        var paths = TestDatabase.CreateReadyPaths();
        TestDatabase.InsertReviewReadyJob(paths, "job-001", "merge-export");
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("seg-001", "job-001", 0, 1, "spk-1", "raw one"),
            new TranscriptSegment("seg-002", "job-001", 1.5, 2.5, "spk-1", "raw two"),
            new TranscriptSegment("seg-003", "job-001", 3, 4, "spk-2", "raw three")
        ]);
        SetFinalText(paths, "job-001", "seg-001", "final one");
        SetFinalText(paths, "job-001", "seg-002", "final two");
        SetFinalText(paths, "job-001", "seg-003", "final three");
        SetSpeakerAlias(paths, "job-001", "spk-1", "Alice");
        SetSpeakerAlias(paths, "job-001", "spk-2", "Bob");
        var output = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "exports");

        new TranscriptExportService(paths).ExportJob(
            "job-001",
            output,
            [TranscriptExportFormat.Xlsx, TranscriptExportFormat.Text, TranscriptExportFormat.Markdown, TranscriptExportFormat.Docx],
            new TranscriptExportOptions(
                IncludeTimestamps: false,
                Source: TranscriptExportSource.Polished,
                MergeConsecutiveSpeakers: true));

        using (var archive = ZipFile.OpenRead(Path.Combine(output, "merge-export.xlsx")))
        {
            var cells = ReadInlineStringCells(ReadZipXml(archive, "xl/worksheets/sheet1.xml"));
            Assert.Equal("00:00:00.000", cells["A2"]);
            Assert.Equal("00:00:02.500", cells["B2"]);
            Assert.Equal("Alice", cells["C2"]);
            Assert.Equal("final one\nfinal two", cells["D2"]);
            Assert.Equal("Bob", cells["C3"]);
            Assert.Equal("final three", cells["D3"]);
            Assert.False(cells.ContainsKey("A4"));
        }

        var text = File.ReadAllText(Path.Combine(output, "merge-export.txt"));
        Assert.Contains($"Alice: final one{Environment.NewLine}final two", text, StringComparison.Ordinal);
        Assert.Contains($"final two{Environment.NewLine}{Environment.NewLine}Bob: final three", text, StringComparison.Ordinal);
        Assert.DoesNotContain($"Alice: final one{Environment.NewLine}Alice: final two", text, StringComparison.Ordinal);

        var markdown = File.ReadAllText(Path.Combine(output, "merge-export.md"));
        Assert.Contains($"**Alice**: final one{Environment.NewLine}final two", markdown, StringComparison.Ordinal);
        Assert.Contains($"final two{Environment.NewLine}{Environment.NewLine}- **Bob**: final three", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain($"**Alice**: final one{Environment.NewLine}- **Alice**: final two", markdown, StringComparison.Ordinal);

        using var docx = ZipFile.OpenRead(Path.Combine(output, "merge-export.docx"));
        var document = docx.GetEntry("word/document.xml");
        Assert.NotNull(document);
        using var reader = new StreamReader(document.Open());
        var xml = reader.ReadToEnd();
        Assert.Contains("Alice: final one", xml, StringComparison.Ordinal);
        Assert.Contains("final two", xml, StringComparison.Ordinal);
        Assert.Contains($"final two</w:t></w:r></w:p>{Environment.NewLine}<w:p><w:r><w:t></w:t></w:r></w:p>{Environment.NewLine}<w:p><w:r><w:t>Bob: final three", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("Alice: final two", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportJob_MergedPolishedXlsxUsesRawTextWhenNoPolishedTextExists()
    {
        var paths = TestDatabase.CreateReadyPaths();
        TestDatabase.InsertReviewReadyJob(paths, "job-001", "blank-polished");
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("seg-001", "job-001", 0, 1, "spk-1", "raw one"),
            new TranscriptSegment("seg-002", "job-001", 1.5, 2.5, "spk-1", "raw two")
        ]);
        var output = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "exports");

        new TranscriptExportService(paths).ExportJob(
            "job-001",
            output,
            [TranscriptExportFormat.Xlsx],
            new TranscriptExportOptions(MergeConsecutiveSpeakers: true, Source: TranscriptExportSource.Polished));

        using var archive = ZipFile.OpenRead(Path.Combine(output, "blank-polished.xlsx"));
        var cells = ReadInlineStringCells(ReadZipXml(archive, "xl/worksheets/sheet1.xml"));
        Assert.Equal("raw one\nraw two", cells["D2"]);
        Assert.False(cells.ContainsKey("A3"));
    }

    [Fact]
    public void ExportJob_DoesNotMergeWhenOptionDisabled()
    {
        var paths = TestDatabase.CreateReadyPaths();
        TestDatabase.InsertReviewReadyJob(paths, "job-001", "no-merge");
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("seg-001", "job-001", 0, 1, "spk-1", "raw one"),
            new TranscriptSegment("seg-002", "job-001", 1.5, 2.5, "spk-1", "raw two")
        ]);
        var output = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "exports");

        new TranscriptExportService(paths).ExportJob(
            "job-001",
            output,
            [TranscriptExportFormat.Xlsx],
            new TranscriptExportOptions(Source: TranscriptExportSource.Polished));

        using var archive = ZipFile.OpenRead(Path.Combine(output, "no-merge.xlsx"));
        var cells = ReadInlineStringCells(ReadZipXml(archive, "xl/worksheets/sheet1.xml"));
        Assert.Equal("raw one", cells["D2"]);
        Assert.Equal("raw two", cells["D3"]);
    }

    [Fact]
    public void ExportJob_DoesNotInsertSpeakerBlockBlankLinesWhenMergeDisabled()
    {
        var paths = TestDatabase.CreateReadyPaths();
        TestDatabase.InsertReviewReadyJob(paths, "job-001", "no-blank-lines");
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("seg-001", "job-001", 0, 1, "spk-1", "raw one"),
            new TranscriptSegment("seg-002", "job-001", 1.5, 2.5, "spk-2", "raw two")
        ]);
        var output = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "exports");

        new TranscriptExportService(paths).ExportJob(
            "job-001",
            output,
            [TranscriptExportFormat.Text, TranscriptExportFormat.Markdown, TranscriptExportFormat.Docx],
            new TranscriptExportOptions(IncludeTimestamps: false, Source: TranscriptExportSource.Raw));

        var text = File.ReadAllText(Path.Combine(output, "no-blank-lines.txt"));
        Assert.Contains($"spk-1: raw one{Environment.NewLine}spk-2: raw two", text, StringComparison.Ordinal);
        Assert.DoesNotContain($"spk-1: raw one{Environment.NewLine}{Environment.NewLine}spk-2: raw two", text, StringComparison.Ordinal);

        var markdown = File.ReadAllText(Path.Combine(output, "no-blank-lines.md"));
        Assert.Contains($"**spk-1**: raw one{Environment.NewLine}- **spk-2**: raw two", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain($"**spk-1**: raw one{Environment.NewLine}{Environment.NewLine}- **spk-2**: raw two", markdown, StringComparison.Ordinal);

        using var docx = ZipFile.OpenRead(Path.Combine(output, "no-blank-lines.docx"));
        var document = docx.GetEntry("word/document.xml");
        Assert.NotNull(document);
        using var reader = new StreamReader(document.Open());
        var xml = reader.ReadToEnd();
        Assert.DoesNotContain("<w:p><w:r><w:t></w:t></w:r></w:p>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportJob_DoesNotMergeJsonSrtOrVtt()
    {
        var paths = TestDatabase.CreateReadyPaths();
        TestDatabase.InsertReviewReadyJob(paths, "job-001", "non-readable");
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("seg-001", "job-001", 0, 1, "spk-1", "raw one"),
            new TranscriptSegment("seg-002", "job-001", 1.5, 2.5, "spk-1", "raw two")
        ]);
        var output = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "exports");

        new TranscriptExportService(paths).ExportJob(
            "job-001",
            output,
            [TranscriptExportFormat.Json, TranscriptExportFormat.Srt, TranscriptExportFormat.Vtt],
            new TranscriptExportOptions(Source: TranscriptExportSource.Polished, MergeConsecutiveSpeakers: true));

        var json = File.ReadAllText(Path.Combine(output, "non-readable.json"));
        Assert.Contains("\"SegmentId\": \"seg-001\"", json, StringComparison.Ordinal);
        Assert.Contains("\"SegmentId\": \"seg-002\"", json, StringComparison.Ordinal);

        var srt = File.ReadAllText(Path.Combine(output, "non-readable.srt"));
        Assert.Contains("00:00:00,000 --> 00:00:01,000", srt, StringComparison.Ordinal);
        Assert.Contains("00:00:01,500 --> 00:00:02,500", srt, StringComparison.Ordinal);

        var vtt = File.ReadAllText(Path.Combine(output, "non-readable.vtt"));
        Assert.Contains("00:00:00.000 --> 00:00:01.000", vtt, StringComparison.Ordinal);
        Assert.Contains("00:00:01.500 --> 00:00:02.500", vtt, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportJob_NormalizesBlankLinesInsideSubtitleCues()
    {
        var paths = TestDatabase.CreateReadyPaths();
        TestDatabase.InsertReviewReadyJob(paths, "job-001", "subtitle");
        new TranscriptSegmentRepository(paths).SaveSegments([
            new TranscriptSegment("seg-001", "job-001", 0, 1, "spk-1", "first line\r\n\r\nsecond line")
        ]);
        var output = Path.Combine(Path.GetTempPath(), "KoeNote.Tests", Guid.NewGuid().ToString("N"), "exports");

        new TranscriptExportService(paths).ExportJob(
            "job-001",
            output,
            [TranscriptExportFormat.Srt, TranscriptExportFormat.Vtt],
            new TranscriptExportOptions(Source: TranscriptExportSource.Raw));

        var srt = File.ReadAllText(Path.Combine(output, "subtitle.srt"));
        Assert.Contains($"spk-1: first line{Environment.NewLine}second line", srt, StringComparison.Ordinal);
        Assert.DoesNotContain($"first line{Environment.NewLine}{Environment.NewLine}second line", srt, StringComparison.Ordinal);

        var vtt = File.ReadAllText(Path.Combine(output, "subtitle.vtt"));
        Assert.Contains($"spk-1: first line{Environment.NewLine}second line", vtt, StringComparison.Ordinal);
        Assert.DoesNotContain($"first line{Environment.NewLine}{Environment.NewLine}second line", vtt, StringComparison.Ordinal);
    }

    private static XDocument ReadZipXml(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName);
        Assert.NotNull(entry);
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static Dictionary<string, string> ReadInlineStringCells(XDocument worksheet)
    {
        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return worksheet
            .Descendants(main + "c")
            .Where(cell => cell.Attribute("r") is not null)
            .ToDictionary(
                cell => cell.Attribute("r")!.Value,
                cell => string.Concat(cell.Descendants(main + "t").Select(static text => text.Value)));
    }

    private static string ReadDocxDocumentXml(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var document = archive.GetEntry("word/document.xml");
        Assert.NotNull(document);
        using var reader = new StreamReader(document.Open());
        return reader.ReadToEnd();
    }

    private static void SetFinalText(AppPaths paths, string jobId, string segmentId, string finalText)
    {
        using var connection = TestDatabase.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE transcript_segments
            SET final_text = $final_text
            WHERE job_id = $job_id AND segment_id = $segment_id;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$segment_id", segmentId);
        command.Parameters.AddWithValue("$final_text", finalText);
        command.ExecuteNonQuery();
    }

    private static void SetSpeakerAlias(AppPaths paths, string jobId, string speakerId, string displayName)
    {
        using var connection = TestDatabase.Open(paths);
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO speaker_aliases (job_id, speaker_id, display_name, updated_at)
            VALUES ($job_id, $speaker_id, $display_name, $updated_at);
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        command.Parameters.AddWithValue("$speaker_id", speakerId);
        command.Parameters.AddWithValue("$display_name", displayName);
        command.Parameters.AddWithValue("$updated_at", DateTimeOffset.Now.ToString("o"));
        command.ExecuteNonQuery();
    }

}
