using PlcRecipe.Core.Models;
using ClosedXML.Excel;
using PlcRecipe.Core;
using PlcRecipe.Core.Interfaces;

namespace PlcRecipe.Infrastructure.Services;

/// <summary>配方 Excel 导入导出（ClosedXML，MIT）。</summary>
public class ExcelService(IRecipeService recipes) : IExcelService
{
    private const int MaxItems = 10_000;
    private const int MaxTextLength = 4_096;
    private static bool IsFormula(string value) => value.Length > 0 && "=+-@".Contains(value[0]);
    private static string SafeText(string? value)
    {
        var text = value ?? "";
        if (text.Length > MaxTextLength) throw new InvalidOperationException("Excel 文本字段超出长度限制");
        return IsFormula(text) ? "'" + text : text;
    }

    public async Task ExportRecipeAsync(Recipe recipe, string filePath, CancellationToken ct = default)
    {
        var fresh = await recipes.GetRecipeAsync(recipe.Id, ct).ConfigureAwait(false);
        if (fresh.Items.Count > MaxItems) throw new InvalidOperationException("配方条目数超出 Excel 导出限制");
        await Task.Run(() =>
        {
            using var wb = new XLWorkbook(); var ws = wb.AddWorksheet("配方");
            string[] headers = ["变量名", "PLC地址", "数据类型", "值", "单位", "下限", "上限", "备注"];
            for (var c = 0; c < headers.Length; c++) ws.Cell(1, c + 1).Value = headers[c];
            ws.Range(1, 1, 1, 8).Style.Font.Bold = true;
            var row = 2;
            foreach (var i in fresh.Items)
            {
                ws.Cell(row, 1).Value = SafeText(i.Name); ws.Cell(row, 2).Value = SafeText(i.Address);
                ws.Cell(row, 3).Value = SafeText(i.DataType.ToString()); ws.Cell(row, 4).Value = SafeText(i.Value);
                ws.Cell(row, 5).Value = SafeText(i.Unit); ws.Cell(row, 6).Value = SafeText(i.LowerLimit?.ToString(System.Globalization.CultureInfo.InvariantCulture));
                ws.Cell(row, 7).Value = SafeText(i.UpperLimit?.ToString(System.Globalization.CultureInfo.InvariantCulture)); ws.Cell(row, 8).Value = SafeText(i.Remark); row++;
            }
            ws.Columns().AdjustToContents(); wb.SaveAs(filePath);
        }, ct).ConfigureAwait(false);
    }
    public async Task<(int matched, List<string> unmatched, List<string> invalid)> ImportRecipeAsync(int recipeId, string filePath, string? user, CancellationToken ct = default)
    {
        var recipe = await recipes.GetRecipeAsync(recipeId, ct).ConfigureAwait(false);
        var byName = recipe.Items.ToDictionary(i => i.Name, i => i, StringComparer.OrdinalIgnoreCase);

        var (newValues, unmatched, invalid) = await Task.Run(() =>
        {
            using var wb = new XLWorkbook(filePath);
            var ws = wb.Worksheets.First();
            var headerRow = ws.FirstRowUsed() ?? throw new InvalidOperationException("Excel 文件为空");
            int nameCol = -1, valueCol = -1;
            int lastCol = ws.LastColumnUsed().ColumnNumber();
            for (int c = 1; c <= lastCol; c++)
            {
                var h = ws.Cell(headerRow.RowNumber(), c).GetString().Trim();
                if (h is "变量名" or "变量") nameCol = c;
                if (h is "值" or "配方值") valueCol = c;
            }
            if (nameCol < 0 || valueCol < 0)
                throw new InvalidOperationException("Excel 表头需包含「变量名」与「值」列");

            var values = new Dictionary<string, string>();
            var unmatched = new List<string>();
            var invalid = new List<string>();
            int row = headerRow.RowNumber() + 1;
            int lastRow = ws.LastRowUsed().RowNumber();
            while (row <= lastRow)
            {
                var name = ws.Cell(row, nameCol).GetString().Trim();
                if (name.Length > 0)
                {
                    if (byName.TryGetValue(name, out var item))
                    {
                        var cellValue = ws.Cell(row, valueCol).GetString().Trim();
                        if (cellValue.Length == 0)
                        {
                            // 空单元格视为不修改该变量
                        }
                        // 导入前按数据行类型校验，坏值不允许入库（否则要等到下载时才报错）
                        else if (!ValueCodec.TryValidate(item, cellValue, out var err))
                            invalid.Add($"{name}：{err}");
                        else
                            values[item.Name] = cellValue;
                    }
                    else
                        unmatched.Add(name);
                }
                row++;
            }
            return (values, unmatched, invalid);
        }, ct).ConfigureAwait(false);

        if (newValues.Count > 0)
            await recipes.ApplyReadValuesAsync(recipeId, newValues, user, ct).ConfigureAwait(false);
        return (newValues.Count, unmatched, invalid);
    }
}
