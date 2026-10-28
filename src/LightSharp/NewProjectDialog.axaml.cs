using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace LightSharp;

public sealed partial class NewProjectDialog : Window
{
    private static readonly Dictionary<string, (string Title, string Description, string Icon)> TemplateDetails = new()
    {
        ["console"] = ("Console App", "A simple command-line application for .NET.", "C#"),
        ["classlib"] = ("Class Library", "A reusable .NET library that can be referenced by other projects.", "LIB"),
        ["webapi"] = ("ASP.NET Core Web API", "An HTTP API project with modern ASP.NET Core defaults.", "API"),
        ["webapp"] = ("ASP.NET Core Web App", "A server-rendered web application using ASP.NET Core.", "WEB"),
        ["xunit"] = ("xUnit Test Project", "A test project using the xUnit testing framework.", "TEST")
    };

    public NewProjectDialog()
        : this(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), false)
    {
    }

    public NewProjectDialog(string defaultLocation, bool canAddToSolution)
    {
        InitializeComponent();
        LocationBox.Text = defaultLocation;
        AddToSolutionCheck.IsVisible = canAddToSolution;
        AddToSolutionCheck.IsChecked = canAddToSolution;
        TemplateList.SelectedIndex = 0;
    }

    public string ProjectName => ProjectNameBox.Text?.Trim() ?? string.Empty;
    public string Location => LocationBox.Text?.Trim() ?? string.Empty;
    public string TemplateShortName => (TemplateList.SelectedItem as ListBoxItem)?.Tag?.ToString() ?? "console";
    public bool AddToSolution => AddToSolutionCheck.IsChecked == true;

    private void TemplateList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var template = TemplateShortName;
        if (!TemplateDetails.TryGetValue(template, out var details)) return;
        TemplateTitle.Text = details.Title;
        TemplateDescription.Text = details.Description;
        TemplateIcon.Text = details.Icon;
    }

    private void TemplateSearch_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var query = TemplateSearchBox.Text?.Trim() ?? string.Empty;
        foreach (var item in TemplateList.Items.OfType<ListBoxItem>())
        {
            var title = item.Content?.ToString() ?? string.Empty;
            item.IsVisible = title.Contains(query, StringComparison.OrdinalIgnoreCase);
        }

        if (TemplateList.SelectedItem is not ListBoxItem { IsVisible: true })
            TemplateList.SelectedItem = TemplateList.Items.OfType<ListBoxItem>().FirstOrDefault(item => item.IsVisible);
    }

    private async void BrowseLocation_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select project location",
            AllowMultiple = false
        });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null) LocationBox.Text = path;
    }

    private void Create_Click(object? sender, RoutedEventArgs e)
    {
        var invalidName = string.IsNullOrWhiteSpace(ProjectName) || ProjectName is "." or ".." ||
                          ProjectName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0;
        if (invalidName)
        {
            ShowValidation("Enter a valid project name.");
            return;
        }

        if (string.IsNullOrWhiteSpace(Location) || !Directory.Exists(Location))
        {
            ShowValidation("Choose an existing project location.");
            return;
        }

        if (Directory.Exists(Path.Combine(Location, ProjectName)) || File.Exists(Path.Combine(Location, ProjectName)))
        {
            ShowValidation("A folder or file with this project name already exists here.");
            return;
        }

        Close(true);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    private void ShowValidation(string message)
    {
        ValidationText.Text = message;
        ValidationText.IsVisible = true;
    }
}
