using System.Windows;

namespace PlcRecipe.WpfApp.Services;

/// <summary>主题切换（亮/暗），基于资源字典热替换。持久化由调用方负责（主窗切换按钮 / 系统管理页）。</summary>
public interface IThemeService
{
    /// <summary>当前主题："light" / "dark"</summary>
    string Current { get; }
    void Apply(string theme);
}

public sealed class ThemeService : IThemeService
{
    private const string SkinLight = "pack://application:,,,/HandyControl;component/Themes/SkinDefault.xaml";
    private const string SkinDark = "pack://application:,,,/HandyControl;component/Themes/SkinDark.xaml";
    private const string HcTheme = "pack://application:,,,/HandyControl;component/Themes/Theme.xaml";
    private const string MyLight = "pack://application:,,,/PlcRecipeStudio;component/Themes/Light.xaml";
    private const string MyDark = "pack://application:,,,/PlcRecipeStudio;component/Themes/Dark.xaml";

    public string Current { get; private set; } = "light";

    /// <summary>App.xaml 初始字典顺序：[0]Skin [1]HC Theme [2]我的主题字典 [3]Shared（不参与切换）。</summary>
    public void Apply(string theme)
    {
        var skin = theme == "dark" ? SkinDark : SkinLight;
        var mine = theme == "dark" ? MyDark : MyLight;
        var dicts = Application.Current.Resources.MergedDictionaries;

        dicts[0] = new ResourceDictionary { Source = new Uri(skin) };
        if (dicts.Count < 3)
        {
            dicts.Add(new ResourceDictionary { Source = new Uri(HcTheme) });
            dicts.Add(new ResourceDictionary { Source = new Uri(mine) });
        }
        else
        {
            if (dicts[1].Source?.OriginalString != HcTheme)
                dicts[1] = new ResourceDictionary { Source = new Uri(HcTheme) };
            dicts[2] = new ResourceDictionary { Source = new Uri(mine) };
        }
        Current = theme == "dark" ? "dark" : "light";
    }
}
