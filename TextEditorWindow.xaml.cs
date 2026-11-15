using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace StickIt;

public partial class TextEditorWindow : Window
{
    private readonly string _filePath;
    private byte[] _originalBytes;
    private Encoding _encoding = new UTF8Encoding(false);
    private byte[] _preamble = [];
    private string _lineEnding = Environment.NewLine;
    private string _baselineEditorText = "";
    private bool _saved;

    public TextEditorWindow(string filePath)
    {
        InitializeComponent();
        _filePath = Path.GetFullPath(filePath);
        FileNameText.Text = Path.GetFileName(_filePath);
        Title = $"Edit {Path.GetFileName(_filePath)} — StickIt";

        var sourceBytes = File.ReadAllBytes(_filePath);
        using var reader = new StreamReader(new MemoryStream(sourceBytes), new UTF8Encoding(false),
            detectEncodingFromByteOrderMarks: true);
        var originalContent = reader.ReadToEnd();
        _encoding = reader.CurrentEncoding;
        _originalBytes = sourceBytes;
        var encodingPreamble = _encoding.GetPreamble();
        _preamble = encodingPreamble.Length > 0 && sourceBytes.AsSpan().StartsWith(encodingPreamble)
            ? encodingPreamble
            : [];
        _lineEnding = DetectLineEnding(originalContent);
        _baselineEditorText = NormalizeForEditor(originalContent);
        EditorTextBox.Text = _baselineEditorText;
        EditorTextBox.CaretIndex = 0;
        UpdateEditorState();
    }

    private void EditorTextBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateEditorState();

    private void UpdateEditorState()
    {
        var isDirty = !string.Equals(EditorTextBox.Text, _baselineEditorText, StringComparison.Ordinal);
        SaveButton.IsEnabled = isDirty;
        SaveStatusText.Text = isDirty ? "Unsaved changes" : "No unsaved changes";
        SaveStatusText.Foreground = (System.Windows.Media.Brush)Application.Current.Resources[
            isDirty ? "WarmAccentForeground" : "TextMuted"];
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_filePath))
        {
            MessageBox.Show(this, "This file was removed from disk. Save was cancelled.",
                "File no longer exists", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            var currentBytes = File.ReadAllBytes(_filePath);
            if (!currentBytes.AsSpan().SequenceEqual(_originalBytes))
            {
                var overwrite = MessageBox.Show(this,
                    "This file changed on disk after you opened it. Overwrite those external changes with your edits?",
                    "File changed externally", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (overwrite != MessageBoxResult.Yes)
                {
                    SaveStatusText.Text = "Not saved; external changes were kept.";
                    return;
                }
            }

            var normalizedContent = NormalizeLineEndings(EditorTextBox.Text, _lineEnding);
            var textBytes = _encoding.GetBytes(normalizedContent);
            var outputBytes = new byte[_preamble.Length + textBytes.Length];
            _preamble.CopyTo(outputBytes, 0);
            textBytes.CopyTo(outputBytes, _preamble.Length);
            File.WriteAllBytes(_filePath, outputBytes);

            _originalBytes = outputBytes;
            _baselineEditorText = EditorTextBox.Text;
            _saved = true;
            DialogResult = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(this, $"Could not save this file.\n\n{ex.Message}",
                "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

    private void TextEditorWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_saved || string.Equals(EditorTextBox.Text, _baselineEditorText, StringComparison.Ordinal))
            return;

        var discard = MessageBox.Show(this, "Discard your unsaved changes?",
            "Unsaved changes", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (discard != MessageBoxResult.Yes)
            e.Cancel = true;
    }

    private static string NormalizeForEditor(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", Environment.NewLine);

    private static string NormalizeLineEndings(string text, string lineEnding) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", lineEnding);

    private static string DetectLineEnding(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\r')
                return index + 1 < text.Length && text[index + 1] == '\n' ? "\r\n" : "\r";
            if (text[index] == '\n')
                return "\n";
        }

        return Environment.NewLine;
    }
}
