using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using AvaloniaEdit.CodeCompletion;
using LightSharp.Models;
using LightSharp.Views;
using Microsoft.CodeAnalysis.CSharp;

namespace LightSharp.Services;

public sealed class EditorWorkspaceController : IDisposable
{
    private readonly EditorWorkspaceView _view;
    private readonly ObservableCollection<EditorDocument> _documents;
    private readonly ObservableCollection<FileNode> _projects;
    private readonly OutputPanelView _output;
    private readonly IMessageBus _messageBus;
    private readonly CSharpCompletionService _completionService = new();
    private readonly Action<EditorDocument> _activateDocument;
    private readonly Func<EditorDocument, Task> _closeDocument;
    private readonly List<IDisposable> _subscriptions = [];
    private readonly DispatcherTimer _syntaxDiagnosticsTimer;
    private readonly DispatcherTimer _quickInfoTimer;
    private EditorDocument? _activeDocument;
    private CancellationTokenSource? _completionRequestCts;
    private CancellationTokenSource? _semanticDiagnosticsCts;
    private CancellationTokenSource? _quickInfoCts;
    private CompletionWindow? _completionWindow;
    private string? _lastSemanticDiagnosticsFailure;
    private int _hoverOffset = -1;
    private bool _suppressNextCompletionNewline;
    private bool _suppressNextCompletionPeriod;

    public EditorWorkspaceController(
        EditorWorkspaceView view,
        ObservableCollection<EditorDocument> documents,
        ObservableCollection<FileNode> projects,
        OutputPanelView output,
        IMessageBus messageBus,
        Action<EditorDocument> activateDocument,
        Func<EditorDocument, Task> closeDocument)
    {
        _view = view;
        _documents = documents;
        _projects = projects;
        _output = output;
        _messageBus = messageBus;
        _activateDocument = activateDocument;
        _closeDocument = closeDocument;

        _view.SetDocuments(_documents);
        _subscriptions.Add(messageBus.Observe<EditorTextChanged>().Subscribe(_ => Editor_TextChanged()));
        _subscriptions.Add(messageBus.Observe<EditorKeyPressed>().Subscribe(message => Editor_KeyDown(message.Args)));
        _subscriptions.Add(messageBus.Observe<EditorTextEntered>().Subscribe(message => Editor_TextEntered(message.Args)));
        _subscriptions.Add(messageBus.Observe<EditorTextEntering>().Subscribe(message => Editor_TextEntering(message.Args)));
        _subscriptions.Add(messageBus.Observe<EditorPointerMoved>().Subscribe(message => EditorPointerMoved(message.Offset)));
        _subscriptions.Add(messageBus.Observe<EditorPointerExited>().Subscribe(_ => ClearQuickInfo()));
        _subscriptions.Add(messageBus.Observe<EditorCaretMoved>().Subscribe(_ => UpdateCursorStatus()));
        _subscriptions.Add(messageBus.Observe<ActivateDocumentRequested>().Subscribe(message => _activateDocument(message.Document)));
        _subscriptions.Add(messageBus.Observe<CloseDocumentRequested>().Subscribe(message => { _ = _closeDocument(message.Document); }));

        _syntaxDiagnosticsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _syntaxDiagnosticsTimer.Tick += (_, _) =>
        {
            _syntaxDiagnosticsTimer.Stop();
            UpdateSyntaxDiagnostics();
        };
        _quickInfoTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _quickInfoTimer.Tick += async (_, _) => await ShowQuickInfoAsync();
    }

    public EditorDocument? ActiveDocument => _activeDocument;

    public void SetDocuments(IEnumerable<EditorDocument> documents) => _view.SetDocuments(documents);

    private void Editor_TextChanged()
    {
        if (_activeDocument is not null) _activeDocument.Text = _view.Text;
        _syntaxDiagnosticsTimer.Stop();
        _syntaxDiagnosticsTimer.Start();
    }

    public void SetActiveDocument(EditorDocument? document)
    {
        _completionRequestCts?.Cancel();
        ClearQuickInfo();
        _completionWindow?.Hide();
        _completionWindow = null;
        _activeDocument = document;
        _view.SetActiveDocument(document);
        UpdateSyntaxDiagnostics();
        UpdateCursorStatus();
        if (document is not null) _view.FocusEditor();
    }

    public void SelectAllIfFocused()
    {
        if (_view.IsEditorFocused) _view.SelectAll();
    }

    public void NavigateTo(int line, int column) => _view.NavigateTo(line, column);

    public void ToggleLineComment(bool uncomment)
    {
        if (!_view.HasDocument) return;
        var selectionStart = _view.SelectionStart;
        var selectionLength = _view.SelectionLength;
        var startLine = _view.GetLineNumberByOffset(selectionLength == 0 ? _view.CaretOffset : selectionStart);
        var endOffset = selectionLength == 0 ? _view.CaretOffset : Math.Max(selectionStart, selectionStart + selectionLength - 1);
        var endLine = _view.GetLineNumberByOffset(endOffset);
        foreach (var number in Enumerable.Range(startLine, endLine - startLine + 1).Reverse())
        {
            var lineOffset = _view.GetLineOffset(number);
            var lineText = _view.GetText(lineOffset, _view.GetLineEndOffset(number) - lineOffset);
            var indentation = lineText.Length - lineText.TrimStart().Length;
            var commentOffset = lineOffset + indentation;
            if (uncomment)
            {
                if (lineText.AsSpan(indentation).StartsWith("//", StringComparison.Ordinal))
                    _view.ReplaceRange(commentOffset, 2, string.Empty);
            }
            else if (!lineText.AsSpan(indentation).StartsWith("//", StringComparison.Ordinal))
                _view.ReplaceRange(commentOffset, 0, "//");
        }
    }

    public async Task FormatActiveDocumentAsync(bool selectionOnly)
    {
        if (_activeDocument is null) return;
        var activeDocument = _activeDocument;
        var selectionStart = _view.SelectionStart;
        var selectionLength = _view.SelectionLength;
        if (selectionOnly && selectionLength == 0)
        {
            _output.StatusMessage = "Select code to format";
            return;
        }

        var source = _view.Text;
        var caretOffset = _view.CaretOffset;
        var projectPath = FindProjectForFile(activeDocument.FullPath);
        var span = selectionOnly
            ? new Microsoft.CodeAnalysis.Text.TextSpan(selectionStart, selectionLength)
            : (Microsoft.CodeAnalysis.Text.TextSpan?)null;
        try
        {
            var formatted = await _completionService.FormatAsync(projectPath, activeDocument.FullPath, source, span, CancellationToken.None);
            if (!ReferenceEquals(_activeDocument, activeDocument) || !string.Equals(_view.Text, source, StringComparison.Ordinal)) return;
            _view.ReplaceAllText(formatted);
            _view.CaretOffset = Math.Min(caretOffset, _view.TextLength);
            _output.StatusMessage = "Formatting complete";
        }
        catch (Exception ex)
        {
            _output.AppendOutput($"Formatting failed: {ex.Message}");
            _output.StatusMessage = "Formatting failed";
        }
    }

    public void Dispose()
    {
        _completionRequestCts?.Cancel();
        _completionRequestCts?.Dispose();
        _semanticDiagnosticsCts?.Cancel();
        _semanticDiagnosticsCts?.Dispose();
        _quickInfoCts?.Cancel();
        _quickInfoCts?.Dispose();
        _syntaxDiagnosticsTimer.Stop();
        _quickInfoTimer.Stop();
        _completionWindow?.Hide();
        _completionService.Dispose();
        SubscriptionDisposal.DisposeAll(_subscriptions);
    }

    private void ScheduleSyntaxDiagnostics()
    {
        _syntaxDiagnosticsTimer.Stop();
        _syntaxDiagnosticsTimer.Start();
    }

    private void Editor_TextEntered(TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text) || e.Text.Length != 1) return;
        var character = e.Text[0];
        var source = _view.Text;
        var afterAssignment = source[..Math.Clamp(_view.CaretOffset, 0, source.Length)].TrimEnd().EndsWith('=');
        if (afterAssignment && (character == '=' || char.IsWhiteSpace(character)))
        {
            if (_completionWindow is not null)
            {
                _completionWindow.Hide();
                _completionWindow = null;
            }
            _ = ShowCompletionAsync(null, explicitInvocation: true);
            return;
        }

        if (_completionWindow is not null) return;
        if (character == '.' || char.IsLetterOrDigit(character) || character == '_')
            _ = ShowCompletionAsync(character, explicitInvocation: false);
    }

    private void Editor_TextEntering(TextInputEventArgs e)
    {
        if (_suppressNextCompletionNewline && e.Text is "\r" or "\n" or "\r\n")
        {
            _suppressNextCompletionNewline = false;
            e.Handled = true;
        }
        else if (_suppressNextCompletionPeriod && e.Text == ".")
        {
            _suppressNextCompletionPeriod = false;
            e.Handled = true;
        }
    }

    private void Editor_KeyDown(KeyEventArgs e)
    {
        if (e.Key != Key.Enter) _suppressNextCompletionNewline = false;
        if (e.Key is not (Key.OemPeriod or Key.Decimal)) _suppressNextCompletionPeriod = false;

        if (e.Key == Key.Enter && (e.KeyModifiers & KeyModifiers.Alt) != 0)
        {
            e.Handled = true;
            _ = ShowAddUsingActionsAsync();
            return;
        }

        if (_completionWindow is not null && e.Key is Key.Enter or Key.Tab or Key.OemPeriod or Key.Decimal)
        {
            var list = _completionWindow.CompletionList;
            if (list.SelectedItem is null && list.CompletionData.Count > 0)
                list.SelectedItem = list.CompletionData[0];
            if (list.SelectedItem is not null)
            {
                PrepareCompletionCommit(e.Key);
                e.Handled = true;
                list.RequestInsertion(e);
                return;
            }
        }

        if (e.Key != Key.Space || (e.KeyModifiers & KeyModifiers.Control) == 0) return;
        e.Handled = true;
        _ = ShowCompletionAsync(null, explicitInvocation: true);
    }

    private char? PrepareCompletionCommit(EventArgs request)
    {
        return request is KeyEventArgs keyEvent ? PrepareCompletionCommit(keyEvent.Key) : null;
    }

    private char? PrepareCompletionCommit(Key key)
    {
        if (key == Key.Enter)
            _suppressNextCompletionNewline = true;
        else if (key is Key.OemPeriod or Key.Decimal)
        {
            _suppressNextCompletionPeriod = true;
            return "."[0];
        }
        return null;
    }

    private void CompleteTextItem(
        AvaloniaEdit.Editing.TextArea textArea,
        AvaloniaEdit.Document.ISegment segment,
        string completionText,
        EventArgs insertionRequest)
    {
        var commitCharacter = PrepareCompletionCommit(insertionRequest);
        textArea.Document.Replace(segment.Offset, segment.Length, completionText);
        textArea.Caret.Offset = segment.Offset + completionText.Length;
        if (commitCharacter is char character)
        {
            textArea.Document.Insert(textArea.Caret.Offset, character.ToString());
            textArea.Caret.Offset++;
        }
    }

    private void EditorPointerMoved(int? pointerOffset)
    {
        if (pointerOffset is null || !_view.HasDocument)
        {
            ClearQuickInfo();
            return;
        }

        var offset = pointerOffset.Value;
        var source = _view.Text;
        if (offset >= source.Length || !IsIdentifierPart(source[offset])) offset--;
        if (offset < 0 || !IsIdentifierPart(source[offset]))
        {
            ClearQuickInfo();
            return;
        }

        if (_hoverOffset == offset) return;
        _hoverOffset = offset;
        _view.SetQuickInfo(null);
        _quickInfoTimer.Stop();
        _quickInfoTimer.Start();
    }

    private void EditorTextView_PointerExited() => ClearQuickInfo();

    private void ClearQuickInfo()
    {
        _hoverOffset = -1;
        _quickInfoTimer.Stop();
        _quickInfoCts?.Cancel();
        _view.SetQuickInfo(null);
    }

    private async Task ShowQuickInfoAsync()
    {
        _quickInfoTimer.Stop();
        var document = _activeDocument;
        var offset = _hoverOffset;
        var projectPath = document is null ? null : FindProjectForFile(document.FullPath);
        if (document is null || projectPath is null || offset < 0) return;

        _quickInfoCts?.Cancel();
        var request = new CancellationTokenSource();
        _quickInfoCts = request;
        var source = _view.Text;
        try
        {
            var info = await _completionService.GetQuickInfoAsync(projectPath, document.FullPath, source, offset, request.Token);
            if (request.IsCancellationRequested || !ReferenceEquals(_activeDocument, document) ||
                !string.Equals(_view.Text, source, StringComparison.Ordinal) || _hoverOffset != offset)
                return;
            _view.SetQuickInfo(info);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"C# quick info unavailable: {ex.Message}"); }
        finally
        {
            if (ReferenceEquals(_quickInfoCts, request)) _quickInfoCts = null;
            request.Dispose();
        }
    }

    private async Task ShowAddUsingActionsAsync()
    {
        var document = _activeDocument;
        if (document is null) return;
        var projectPath = FindProjectForFile(document.FullPath);
        if (projectPath is null)
        {
            _output.StatusMessage = "Open this file in a C# project to find using suggestions";
            return;
        }

        var source = _view.Text;
        var caretOffset = _view.CaretOffset;
        try
        {
            var suggestions = await _completionService.GetMissingUsingSuggestionsAsync(
                projectPath, document.FullPath, source, caretOffset, CancellationToken.None);
            if (!ReferenceEquals(_activeDocument, document) || !string.Equals(_view.Text, source, StringComparison.Ordinal)) return;
            if (suggestions.Count == 0)
            {
                _output.StatusMessage = "No matching using directive found";
                return;
            }
            if (suggestions.Count == 1)
            {
                await ApplyUsingAsync(document, source, suggestions[0], caretOffset);
                return;
            }

            var menu = new ContextMenu { Placement = PlacementMode.Pointer };
            foreach (var suggestion in suggestions)
            {
                var item = new MenuItem { Header = $"using {suggestion};" };
                item.Click += async (_, _) => await ApplyUsingAsync(document, source, suggestion, caretOffset);
                menu.Items.Add(item);
            }
            _view.ShowQuickActionMenu(menu);
        }
        catch (Exception ex)
        {
        _output.AppendOutput($"Unable to find using suggestions: {ex.Message}");
            _output.StatusMessage = "Quick action failed";
        }
    }

    private async Task ApplyUsingAsync(EditorDocument document, string source, string namespaceName, int caretOffset)
    {
        var projectPath = FindProjectForFile(document.FullPath);
        if (projectPath is null) return;
        try
        {
            var updatedSource = await _completionService.AddUsingAsync(
                projectPath, document.FullPath, source, namespaceName, CancellationToken.None);
            if (updatedSource is null || !ReferenceEquals(_activeDocument, document) ||
                !string.Equals(_view.Text, source, StringComparison.Ordinal)) return;

            var offsetAdjustment = updatedSource.Length - source.Length;
            _view.ReplaceAllText(updatedSource);
            _view.CaretOffset = Math.Clamp(caretOffset + offsetAdjustment, 0, _view.TextLength);
            _view.FocusEditor();
            _output.StatusMessage = $"Added using {namespaceName}";
        }
        catch (Exception ex)
        {
            _output.AppendOutput($"Unable to add using {namespaceName}: {ex.Message}");
            _output.StatusMessage = "Quick action failed";
        }
    }

    private async Task ShowCompletionAsync(char? triggerCharacter, bool explicitInvocation)
    {
        if (_activeDocument is null || _completionWindow is not null ||
            !Path.GetExtension(_activeDocument.FullPath).Equals(".cs", StringComparison.OrdinalIgnoreCase))
            return;

        _completionRequestCts?.Cancel();
        _completionRequestCts?.Dispose();
        var requestCts = new CancellationTokenSource();
        _completionRequestCts = requestCts;

        var activeDocument = _activeDocument;
        var source = _view.Text;
        var caretOffset = _view.CaretOffset;
        var startOffset = triggerCharacter == '.' ? caretOffset : GetIdentifierStart(source, caretOffset);
        var projectPath = FindProjectForFile(activeDocument.FullPath);
        var semanticItems = Array.Empty<CSharpCompletionCandidate>();
        string? expectedTypeName = null;

        try
        {
            if (!explicitInvocation && triggerCharacter != '.')
                await Task.Delay(90, requestCts.Token);

            if (projectPath is not null)
            {
                semanticItems = (await _completionService.GetSuggestionsAsync(
                    projectPath,
                    activeDocument.FullPath,
                    source,
                    caretOffset,
                    triggerCharacter,
                    requestCts.Token)).ToArray();
                var leftOfCaret = source[..Math.Clamp(caretOffset, 0, source.Length)].TrimEnd();
                if (leftOfCaret.EndsWith('='))
                    expectedTypeName = await _completionService.GetExpectedTypeNameAsync(
                        projectPath,
                        activeDocument.FullPath,
                        source,
                        caretOffset,
                        requestCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _output.AppendOutput($"C# completion unavailable: {ex.Message}");
        }

        if (requestCts.IsCancellationRequested) return;
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
            if (!ReferenceEquals(_activeDocument, activeDocument) || _view.Text != source ||
                requestCts.IsCancellationRequested || _completionWindow is not null)
                return;

            var prefix = source[startOffset..caretOffset];
            var matchingSemanticItems = semanticItems
                .Where(item => MatchesCompletionPrefix(item.FilterText, prefix))
                .ToArray();
            var useWordFallback = matchingSemanticItems.Length == 0 &&
                                  (semanticItems.Length == 0 || explicitInvocation) &&
                                  (explicitInvocation || (triggerCharacter != '.' && prefix.Length > 0));
            if (matchingSemanticItems.Length == 0 && !useWordFallback) return;

            var completionWindow = _view.CreateCompletionWindow();
            completionWindow.StartOffset = startOffset;
            completionWindow.EndOffset = caretOffset;
            completionWindow.CloseAutomatically = true;
            completionWindow.CloseWhenCaretAtBeginning = triggerCharacter != '.';
            completionWindow.CompletionList.CompletionAcceptKeys = [Key.Enter, Key.Tab, Key.OemPeriod, Key.Decimal];

            if (matchingSemanticItems.Length > 0)
            {
                var insertionRequested = false;
                ICompletionData? preferredItem = null;
                foreach (var semanticItem in matchingSemanticItems)
                {
                    var item = semanticItem;
                    var completionItem = new EditorCompletionItem(
                        item.DisplayText,
                        item.Description,
                        (textArea, segment, insertionEventArgs) =>
                        {
                            if (insertionRequested) return;
                            insertionRequested = true;
                            var commitCharacter = PrepareCompletionCommit(insertionEventArgs);
                            _ = ApplySemanticCompletionAsync(item, textArea, segment, commitCharacter);
                        });
                    completionWindow.CompletionList.CompletionData.Add(completionItem);
                    if (expectedTypeName is not null &&
                        (string.Equals(item.FilterText, expectedTypeName, StringComparison.Ordinal) ||
                         string.Equals(item.DisplayText, expectedTypeName, StringComparison.Ordinal)))
                        preferredItem = completionItem;
                }
                if (expectedTypeName is not null && preferredItem is null)
                {
                    preferredItem = new EditorCompletionItem(
                        expectedTypeName,
                        "Expected type for this expression",
                        (textArea, segment, insertionEventArgs) =>
                        {
                            if (insertionRequested) return;
                            insertionRequested = true;
                            CompleteTextItem(textArea, segment, expectedTypeName, insertionEventArgs);
                        });
                    completionWindow.CompletionList.CompletionData.Add(preferredItem);
                }
                completionWindow.CompletionList.SelectedItem = preferredItem ?? completionWindow.CompletionList.CompletionData.FirstOrDefault();
            }
            else
            {
                var insertionRequested = false;
                foreach (var word in GetWordSuggestions(source, startOffset, caretOffset))
                {
                    var completionText = word;
                    completionWindow.CompletionList.CompletionData.Add(new EditorCompletionItem(
                        completionText,
                        "Word from open documents",
                        (textArea, segment, insertionEventArgs) =>
                        {
                            if (insertionRequested) return;
                            insertionRequested = true;
                            CompleteTextItem(textArea, segment, completionText, insertionEventArgs);
                        }));
                }
                if (expectedTypeName is not null)
                {
                    var expectedTypeItem = new EditorCompletionItem(
                        expectedTypeName,
                        "Expected type for this expression",
                        (textArea, segment, insertionEventArgs) =>
                        {
                            if (insertionRequested) return;
                            insertionRequested = true;
                            CompleteTextItem(textArea, segment, expectedTypeName, insertionEventArgs);
                        });
                    completionWindow.CompletionList.CompletionData.Insert(0, expectedTypeItem);
                    completionWindow.CompletionList.SelectedItem = expectedTypeItem;
                }
                else if (completionWindow.CompletionList.CompletionData.Count > 0)
                    completionWindow.CompletionList.SelectedItem = completionWindow.CompletionList.CompletionData[0];
            }

            if (completionWindow.CompletionList.CompletionData.Count == 0) return;
            _completionWindow = completionWindow;
            completionWindow.Closed += (_, _) =>
            {
                if (ReferenceEquals(_completionWindow, completionWindow)) _completionWindow = null;
            };
            completionWindow.Show();
            }
            catch (Exception ex)
            {
                _completionWindow?.Hide();
                _completionWindow = null;
                _output.AppendOutput($"Unable to show completion: {ex.Message}");
            }
        });
    }

    private async Task ApplySemanticCompletionAsync(
        CSharpCompletionCandidate candidate,
        AvaloniaEdit.Editing.TextArea textArea,
        AvaloniaEdit.Document.ISegment segment,
        char? commitCharacter)
    {
        try
        {
            var change = await candidate.CompletionService.GetChangeAsync(
                candidate.Document,
                candidate.Item,
                cancellationToken: CancellationToken.None);
            Dispatcher.UIThread.Post(() =>
            {
                if (_activeDocument?.FullPath != candidate.Document.FilePath ||
                    _view.Text != candidate.SourceSnapshot)
                    return;

                try
                {
                    foreach (var textChange in change.TextChanges.OrderByDescending(item => item.Span.Start))
                        textArea.Document.Replace(textChange.Span.Start, textChange.Span.Length, textChange.NewText ?? string.Empty);

                    textArea.Caret.Offset = change.NewPosition ?? (segment.Offset + candidate.DisplayText.Length);
                    if (commitCharacter is char character)
                    {
                        textArea.Document.Insert(textArea.Caret.Offset, character.ToString());
                        textArea.Caret.Offset++;
                    }
                }
                catch (Exception ex)
                {
                    _output.AppendOutput($"Unable to apply completion: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            _output.AppendOutput($"Unable to apply completion: {ex.Message}");
        }
    }

    private IEnumerable<string> GetWordSuggestions(string source, int prefixStart, int caretOffset)
    {
        var prefix = source[prefixStart..Math.Clamp(caretOffset, prefixStart, source.Length)];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var documentText in _documents.Select(document => document.Text).Append(source))
        {
            var position = 0;
            while (position < documentText.Length)
            {
                if (!IsIdentifierStart(documentText[position]))
                {
                    position++;
                    continue;
                }

                var start = position++;
                while (position < documentText.Length && IsIdentifierPart(documentText[position])) position++;
                var word = documentText[start..position];
                if (word.Length > prefix.Length && word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && seen.Add(word))
                    yield return word;
            }
        }
    }

    private static bool MatchesCompletionPrefix(string candidate, string prefix)
    {
        if (prefix.Length == 0 || candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return true;

        var initials = new string(candidate.Where(char.IsUpper).ToArray());
        return initials.Length > 0 && initials.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIdentifierPart(char character) => character == '_' || char.IsLetterOrDigit(character);
    private static bool IsIdentifierStart(char character) => character == '_' || char.IsLetter(character);

    private static int GetIdentifierStart(string source, int position)
    {
        var start = Math.Clamp(position, 0, source.Length);
        while (start > 0 && IsIdentifierPart(source[start - 1])) start--;
        return start;
    }

    private void UpdateSyntaxDiagnostics()
    {
        var document = _activeDocument;
        if (document is null || !Path.GetExtension(document.FullPath).Equals(".cs", StringComparison.OrdinalIgnoreCase))
        {
            _output.SetProblems([]);
            _view.SetSyntaxErrors([]);
            _view.SetSemanticHighlights([]);
            _messageBus.Publish(new EditorStatusChanged("✓  Ready"));
            return;
        }

        var source = _view.Text;
        var tree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.Preview),
            document.FullPath);
        var parseDiagnostics = tree.GetDiagnostics().ToArray();
        DisplayDiagnostics(parseDiagnostics);
        _ = UpdateSemanticDiagnosticsAsync(document, source, parseDiagnostics);
    }

    private void DisplayDiagnostics(IEnumerable<Microsoft.CodeAnalysis.Diagnostic> diagnostics)
    {
        var visibleDiagnostics = diagnostics
            .Where(item => item.Location.IsInSource &&
                           item.Severity is Microsoft.CodeAnalysis.DiagnosticSeverity.Error or Microsoft.CodeAnalysis.DiagnosticSeverity.Warning)
            .DistinctBy(item => (item.Id, item.Location.SourceSpan.Start, item.Location.SourceSpan.Length))
            .OrderBy(item => item.Location.SourceSpan.Start)
            .ToArray();
        var problems = visibleDiagnostics.Select(diagnostic =>
        {
            var span = diagnostic.Location.GetLineSpan();
            var line = span.StartLinePosition.Line + 1;
            var column = span.StartLinePosition.Character + 1;
            return new EditorProblem(
                diagnostic.Severity,
                line,
                column,
                diagnostic.GetMessage());
        }).ToArray();
        _output.SetProblems(problems);
        _view.SetSyntaxErrors(visibleDiagnostics
            .Where(item => item.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error && item.Location.IsInSource)
            .Select(item => (item.Location.SourceSpan.Start, item.Location.SourceSpan.Length)));

        var errors = problems.Count(problem => problem.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        var warnings = problems.Length - errors;
        _messageBus.Publish(new EditorStatusChanged(errors > 0
            ? $"✖  {errors} error(s), {warnings} warning(s)"
            : warnings > 0 ? $"⚠  {warnings} warning(s)" : "✓  No syntax errors"));
    }

    private async Task UpdateSemanticDiagnosticsAsync(
        EditorDocument document,
        string source,
        IReadOnlyList<Microsoft.CodeAnalysis.Diagnostic> parseDiagnostics)
    {
        _semanticDiagnosticsCts?.Cancel();
        _semanticDiagnosticsCts?.Dispose();
        var request = new CancellationTokenSource();
        _semanticDiagnosticsCts = request;

        var projectPath = FindProjectForFile(document.FullPath);
        if (projectPath is null)
        {
            _view.SetSemanticHighlights([]);
            return;
        }
        try
        {
            var diagnostics = await _completionService.GetDiagnosticsAsync(
                projectPath,
                document.FullPath,
                source,
                request.Token);
            var semanticHighlights = await _completionService.GetSemanticHighlightsAsync(
                projectPath,
                document.FullPath,
                source,
                request.Token);
            if (request.IsCancellationRequested || !ReferenceEquals(_activeDocument, document) ||
                !string.Equals(_view.Text, source, StringComparison.Ordinal)) return;

            _view.SetSemanticHighlights(semanticHighlights);
            _lastSemanticDiagnosticsFailure = null;
            DisplayDiagnostics(diagnostics.Count > 0 ? diagnostics : parseDiagnostics);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Live C# diagnostics unavailable: {ex.Message}");
            var failure = ex.GetBaseException().Message;
            if (!string.Equals(_lastSemanticDiagnosticsFailure, failure, StringComparison.Ordinal))
            {
                _lastSemanticDiagnosticsFailure = failure;
                _output.AppendOutput($"Live C# semantic highlighting unavailable: {failure}");
            }
        }
    }

    private string? FindProjectForFile(string filePath)
    {
        var candidates = _projects
            .Select(project => new { Project = project, Directory = Path.GetDirectoryName(project.FullPath) })
            .Where(item => item.Directory is not null && IsPathInside(item.Directory, filePath))
            .OrderByDescending(item => item.Directory!.Length);
        return candidates.FirstOrDefault()?.Project.FullPath;
    }

    private static bool IsPathInside(string directory, string filePath)
    {
        var relative = Path.GetRelativePath(directory, filePath);
        return !Path.IsPathRooted(relative) && relative != ".." &&
               !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
    }

    private void UpdateCursorStatus()
    {
        _messageBus.Publish(new EditorCursorChanged(_view.CaretLine, _view.CaretColumn));
    }

}
