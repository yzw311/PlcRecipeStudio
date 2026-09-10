using System.Globalization;
using PlcRecipe.Core.Models;

namespace PlcRecipe.Core;

/// <summary>
/// 参数容差检查：按数据行的上下限核对（上传/读回的）实际值，产出越告警清单。
/// 数值型变量按数值比较；Bool/String 不参与限值检查。
/// </summary>
public static class RecipeLimits
{
    /// <summary>返回越界告警清单（如 "温度=105 超上限 100"），无越界返回空列表。</summary>
    public static List<string> Check(IReadOnlyList<RecipeItem> items, IReadOnlyDictionary<string, string> valuesByName)
    {
        var violations = new List<string>();
        foreach (var item in items)
        {
            if (!item.LowerLimit.HasValue && !item.UpperLimit.HasValue) continue;
            if (item.DataType is PlcDataType.Bool or PlcDataType.String) continue;
            if (!valuesByName.TryGetValue(item.Name, out var raw)) continue;
            if (string.IsNullOrWhiteSpace(raw)) continue;
            // 空串按默认 0 处理的约定与 ValueCodec 一致；这里只对可解析为数值的值做检查
            if (!double.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) continue;

            if (item.UpperLimit.HasValue && value > item.UpperLimit.Value)
                violations.Add($"{item.Name}={Format(value)} 超上限 {Format(item.UpperLimit.Value)}");
            if (item.LowerLimit.HasValue && value < item.LowerLimit.Value)
                violations.Add($"{item.Name}={Format(value)} 低于下限 {Format(item.LowerLimit.Value)}");
        }
        return violations;
    }

    private static string Format(double v) =>
        v.ToString("0.###", CultureInfo.InvariantCulture);
}
