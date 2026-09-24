using Asura.App.ViewModels;
using Asura.App.Views.Components;
using Asura.Application;
using Asura.Core;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

[Collection(AvaloniaUiCollection.Name)]
public sealed class DatabaseRowFieldEditorHeadlessTests
{
    [Theory]
    [InlineData(DatabaseValueKind.Json)]
    [InlineData(DatabaseValueKind.Boolean)]
    public async Task InspectorTemplateShowsTheCorrectEditorAndAppliesItsDraft(DatabaseValueKind kind)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var headless = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        try
        {
            Assert.True(await headless.Dispatch(async () =>
            {
                using var panel = new DatabaseRuntimePanelViewModel(PanelInstanceId.New(), "Database",
                    new DatabaseRuntimePanelViewModelTests.FakeDatabasePanelClient(), "sqlite", "Data Source=demo.db");
                await panel.Initialization;
                await panel.PreviewTableAsync(panel.Tables[0]);
                panel.SelectRow(panel.ResultRows[0]);
                var view = new DatabaseWorkspaceView { DataContext = panel };
                var window = new Window { Content = view, Width = 1000, Height = 700 };
                window.Show();
                try
                {
                    window.UpdateLayout();
                    var inspector = Assert.Single(view.GetVisualDescendants().OfType<ItemsControl>(),
                        control => ReferenceEquals(control.ItemsSource, panel.SelectedRowFields));
                    var column = new DatabaseColumnDescriptor("value", kind == DatabaseValueKind.Json ? "jsonb" : "boolean", kind, IsNullable: true);
                    object original = kind == DatabaseValueKind.Json ? "{}" : true;
                    var cell = new DatabaseResultCellViewModel(new DatabaseValue(original, kind, original.ToString()!), column, 200, canEdit: true);
                    using var field = new DatabaseRowFieldViewModel(new DatabaseResultColumnViewModel(column, 200), cell);
                    var content = inspector.ItemTemplate!.Build(field)!;
                    content.DataContext = field;
                    window.Content = content;
                    window.UpdateLayout();
                    var buttons = content.GetVisualDescendants().OfType<Button>().ToArray();
                    Assert.True(Assert.Single(buttons, button => string.Equals(AutomationProperties.GetName(button), "Edit value", StringComparison.Ordinal)).IsVisible);
                    field.BeginEdit();
                    window.UpdateLayout();
                    var editor = Assert.Single(content.GetVisualDescendants().OfType<CodeEditBox>());
                    var selector = Assert.Single(content.GetVisualDescendants().OfType<ComboBox>());
                    Assert.Equal(kind == DatabaseValueKind.Boolean, selector.IsVisible);
                    if (kind == DatabaseValueKind.Boolean)
                    {
                        Assert.Equal("true", selector.SelectedItem);
                        selector.SelectedItem = "false";
                        Assert.Equal("false", field.Draft);
                    }
                    else
                    {
                        Assert.Equal("{}", editor.Text);
                        editor.Text = """{"enabled":false}""";
                        Assert.Equal(editor.Text, field.Draft);
                    }
                    Assert.False(cell.IsDirty);
                    Assert.Single(buttons, button => string.Equals(AutomationProperties.GetName(button), "Apply value", StringComparison.Ordinal))
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Assert.False(field.IsEditing);
                    Assert.True(cell.TryBuildEdit(out var edit));
                    Assert.Equal(kind == DatabaseValueKind.Json ? (object)"""{"enabled":false}""" : false, edit.Value);
                    return true;
                }
                finally { window.Close(); }
            }, timeout.Token));
        }
        finally { await headless.DisposeAsync(); }
    }
}
