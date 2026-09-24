using Asura.App.ViewModels;
using Asura.Application;

namespace Asura.App.Tests;

public sealed class DatabaseRowFieldEditingTests
{
    [Theory]
    [InlineData("json")]
    [InlineData("jsonb")]
    public void JsonDraftsValidateAndStageWithoutChangingOriginalValues(string type)
    {
        var column = new DatabaseColumnDescriptor("configuration", type, DatabaseValueKind.Json, IsNullable: true);
        var cell = new DatabaseResultCellViewModel(new DatabaseValue("{}", DatabaseValueKind.Json, "{}"), column, 200, canEdit: true);
        var gridColumn = new DatabaseResultColumnViewModel(column, 200, canEdit: true);
        using var field = new DatabaseRowFieldViewModel(gridColumn, cell);
        Assert.True(gridColumn.IsEditable);
        Assert.True(field.ShowsEditAction);
        Assert.True(cell.UsesLargeTextEditor);
        field.BeginEdit();
        Assert.True(field.ShowsTextEditor);
        Assert.False(field.ShowsBooleanEditor);
        Assert.Equal(".json", field.GrammarExtension);
        field.Draft = "{invalid";
        Assert.False(cell.IsDirty);
        field.ApplyEdit();
        Assert.True(field.HasValidationError);
        Assert.False(cell.TryBuildEdit(out _));
        field.BeginEdit();
        field.Draft = """{"extras":{"headers":{}},"enabled":false}""";
        field.ApplyEdit();
        Assert.True(cell.TryBuildEdit(out var edit));
        Assert.Equal(field.Draft, edit.Value);
        Assert.Equal("{}", cell.BuildOriginalEdit().Value);
        Assert.True(cell.IsDirty);
        Assert.False(field.HasValidationError);
        field.BeginEdit();
        field.Draft = "[]";
        field.CancelEdit();
        Assert.Equal(edit.Value, cell.RawValue);
        cell.Reset();
        Assert.Equal("{}", field.Text);
        Assert.False(cell.IsDirty);
    }

    [Theory]
    [InlineData("\"quoted value\"")]
    [InlineData("null")]
    [InlineData("false")]
    public void ProviderJsonScalarsOpenAsCompleteJson(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var column = new DatabaseColumnDescriptor("configuration", "jsonb", DatabaseValueKind.Json);
        var cell = new DatabaseResultCellViewModel(new DatabaseValue(document.RootElement.Clone(), DatabaseValueKind.Json, json), column, 200, canEdit: true);
        using var field = new DatabaseRowFieldViewModel(new DatabaseResultColumnViewModel(column, 200), cell);
        field.BeginEdit();
        Assert.Equal(json, field.Draft);
        field.ApplyEdit();
        Assert.True(cell.TryBuildEdit(out var edit));
        Assert.Equal(DatabaseEditValueState.Value, edit.State);
        Assert.False(cell.IsDirty);
    }

    [Fact]
    public void JsonNullRemainsAJsonValueRatherThanSqlNull()
    {
        var column = new DatabaseColumnDescriptor("configuration", "jsonb", DatabaseValueKind.Json, IsNullable: false);
        var cell = new DatabaseResultCellViewModel(new DatabaseValue("{}", DatabaseValueKind.Json, "{}"), column, 200, canEdit: true);
        using var field = new DatabaseRowFieldViewModel(new DatabaseResultColumnViewModel(column, 200), cell);
        field.BeginEdit();
        field.Draft = "null";
        field.ApplyEdit();
        Assert.True(cell.TryBuildEdit(out var edit));
        Assert.Equal(DatabaseEditValueState.Value, edit.State);
        Assert.Equal("null", edit.Value);
        Assert.False(cell.IsNull);
    }

    [Fact]
    public void BooleanInspectorStagesTypedValuesAndSqlNullAndCanCancel()
    {
        var column = new DatabaseColumnDescriptor("is_enabled", "boolean", DatabaseValueKind.Boolean, IsNullable: true);
        var cell = new DatabaseResultCellViewModel(new DatabaseValue(true, DatabaseValueKind.Boolean, "true"), column, 200, canEdit: true);
        using var field = new DatabaseRowFieldViewModel(new DatabaseResultColumnViewModel(column, 200), cell);
        Assert.True(field.ShowsEditAction);
        field.BeginEdit();
        Assert.True(field.ShowsBooleanEditor);
        Assert.False(field.ShowsTextEditor);
        Assert.Equal("true", field.Draft);
        Assert.Equal(["true", "false", "NULL"], field.BooleanValues);
        field.Draft = "false";
        field.CancelEdit();
        Assert.True(Assert.IsType<bool>(cell.RawValue));
        Assert.False(cell.IsDirty);
        field.BeginEdit();
        Assert.Equal("true", field.Draft);
        field.Draft = "false";
        field.ApplyEdit();
        Assert.True(cell.TryBuildEdit(out var edit));
        Assert.False(Assert.IsType<bool>(edit.Value));
        Assert.True(Assert.IsType<bool>(cell.BuildOriginalEdit().Value));
        field.BeginEdit();
        field.Draft = "NULL";
        field.ApplyEdit();
        Assert.True(cell.TryBuildEdit(out var nullEdit));
        Assert.Equal(DatabaseEditValueState.Null, nullEdit.State);
        Assert.Null(nullEdit.Value);
        field.BeginEdit();
        Assert.Equal("NULL", field.Draft);
        field.Draft = "true";
        field.ApplyEdit();
        Assert.False(cell.IsDirty);
        Assert.True(Assert.IsType<bool>(cell.RawValue));
    }

    [Fact]
    public void RequiredBooleanDoesNotOfferNullAndRejectsInvalidDrafts()
    {
        var column = new DatabaseColumnDescriptor("is_enabled", "boolean", DatabaseValueKind.Boolean, IsNullable: false);
        var cell = new DatabaseResultCellViewModel(new DatabaseValue(false, DatabaseValueKind.Boolean, "false"), column, 200, canEdit: true);
        using var field = new DatabaseRowFieldViewModel(new DatabaseResultColumnViewModel(column, 200), cell);
        Assert.Equal(["true", "false"], field.BooleanValues);
        field.BeginEdit();
        field.Draft = "NULL";
        field.ApplyEdit();
        Assert.False(cell.TryBuildEdit(out _));
        Assert.True(field.HasValidationError);
    }

    [Theory]
    [InlineData(DatabaseValueKind.Json, true, true)]
    [InlineData(DatabaseValueKind.Json, false, false)]
    [InlineData(DatabaseValueKind.Boolean, true, true)]
    [InlineData(DatabaseValueKind.Boolean, false, false)]
    public void ReadOnlyColumnsAndResultsNeverExposeInspectorEditing(DatabaseValueKind kind, bool readOnly, bool canEdit)
    {
        var column = new DatabaseColumnDescriptor("value", "type", kind, IsReadOnly: readOnly);
        var cell = new DatabaseResultCellViewModel(new DatabaseValue(null, kind, "NULL"), column, 200, canEdit);
        using var field = new DatabaseRowFieldViewModel(new DatabaseResultColumnViewModel(column, 200, canEdit), cell);
        Assert.False(field.ShowsEditAction);
        field.BeginEdit();
        Assert.False(field.IsEditing);
        Assert.False(field.ShowsTextEditor);
        Assert.False(field.ShowsBooleanEditor);
    }
}
