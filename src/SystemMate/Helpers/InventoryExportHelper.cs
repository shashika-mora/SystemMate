using Microsoft.UI.Xaml;
using System.Text;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace SystemMate.Helpers;

internal static class InventoryExportHelper
{
    public static async Task<string?> SaveCsvAsync(string suggestedName, IEnumerable<string> headers,
        IEnumerable<IEnumerable<string?>> rows, Window owner)
    {
        var picker = new FileSavePicker { SuggestedFileName = suggestedName, SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeChoices.Add("CSV report", new List<string> { ".csv" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return null;
        var csv = new StringBuilder();
        csv.AppendLine(string.Join(",", headers.Select(Escape)));
        foreach (var row in rows) csv.AppendLine(string.Join(",", row.Select(Escape)));
        await FileIO.WriteTextAsync(file, csv.ToString(), Windows.Storage.Streams.UnicodeEncoding.Utf8);
        return file.Path;
    }

    private static string Escape(string? value)
    {
        value ??= "";
        // Registry values are untrusted; prevent a spreadsheet from executing them as formulas.
        if (value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+')
            || value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
