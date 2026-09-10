using PlcRecipe.Core;
using PlcRecipe.Core.Interfaces;
using PlcRecipe.Core.Models;

namespace PlcRecipe.Infrastructure.Services;

/// <summary>配方对比：读 PLC 当前值与配方值逐项比较。</summary>
public class CompareService(
    ITransferService transfers) : ICompareService
{
    public async Task<CompareResult> CompareAsync(PlcDevice device, Recipe recipe, CancellationToken ct = default)
    {
        var result = new CompareResult
        {
            DeviceId = device.Id,
            DeviceName = device.Name,
            RecipeId = recipe.Id,
            RecipeName = recipe.Name
        };
        var recipeValues = recipe.Items.ToDictionary(i => i.Name, i => i.Value);

        Dictionary<string, string> plcValues;
        try
        {
            plcValues = await transfers.ReadPlanValuesAsync(device, device.Brand, recipe.Items, null, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = ex.Message;
            return result;
        }

        foreach (var item in recipe.Items)
        {
            var row = new CompareRow { Item = item, RecipeValue = recipeValues.GetValueOrDefault(item.Name, "") };
            if (plcValues.TryGetValue(item.Name, out var plcValue))
            {
                row.PlcValue = plcValue;
                row.State = ValueCodec.ValuesEqual(item.DataType, plcValue, row.RecipeValue ?? "")
                    ? CompareState.Same : CompareState.Different;
            }
            else
            {
                row.PlcValue = null;
                row.State = CompareState.ReadFailed;
            }
            row.Selected = row.State == CompareState.Different && item.Access == VariableAccess.ReadWrite;
            result.Rows.Add(row);
        }
        return result;
    }
}
