/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using HamlProAppModule.haml.events;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace HamlProAppModule.haml.ui.options;

public class ColorSettings
{
    private static string? _fileLocation;

    public static string FileLocation => _fileLocation ??= GetFilePath();

    [JsonConverter(typeof(StringEnumConverter))]
    public enum GuiElement
    {
        ShorelineAnnotation,
        Contour,
        HighAnnotation,
        LowAnnotation,
        SelectedProfile,
        SavedProfile,
        SearchSpace
    }
    
    private static readonly Regex HexRegex = new(@"^#([A-Fa-f0-9]{8}|[A-Fa-f0-9]{6}|[A-Fa-f0-9]{3})$");
    
    public const string DefaultYellow = "#F5D833";
    public const string DefaultRed = "#F53531";
    public const string DefaultGreen = "#47D143";
    public const string DefaultMagenta = "#C305FF";
    public const string DefaultBlack = "#23282D";
    public const string DefaultBlue = "#33b1f5";    
    public const string DefaultGrey = "#AEAEAE";
    public const string ClassificationBlue = "#90F0FF";
    public const string ClassificationYellow = "#E9FF90";
    public const string ClassificationPurple = "#AE7DCE";
    
    static readonly Dictionary<GuiElement, string> DefaultColors = new()
    {
        {GuiElement.ShorelineAnnotation, DefaultYellow},
        {GuiElement.LowAnnotation, DefaultRed},
        {GuiElement.Contour, DefaultRed},
        {GuiElement.HighAnnotation, DefaultGreen},
        {GuiElement.SelectedProfile, DefaultMagenta},
        {GuiElement.SearchSpace, DefaultYellow},
        {GuiElement.SavedProfile, DefaultBlack}
    };

    private static string GetFilePath()
    {
        return Path.Combine(Module1.AssemblyPath ?? "", @"..\..\HamlColorSettings.json");
    }

    private Dictionary<GuiElement, string> _guiElementColors;

    public ColorSettings()
    {
        _guiElementColors = DefaultColors;
    }

    [JsonConstructor]
    private ColorSettings(Dictionary<GuiElement, string> guiElementColors)
    {
        GuiElementColors = guiElementColors;
    }

    [JsonProperty]
    private Dictionary<GuiElement, string> GuiElementColors
    {
        get => _guiElementColors;
        set => _guiElementColors = Sanitize(value);
    }

    // Need to check that every GuiElement is accounted for and has a valid color. If not we set default
    private Dictionary<GuiElement, string> Sanitize(Dictionary<GuiElement, string> elementColors)
    {
        foreach (var kvp in DefaultColors)
        {
            if (elementColors.ContainsKey(kvp.Key))
            {
                if (!HexRegex.IsMatch(elementColors[kvp.Key]))
                {
                    elementColors[kvp.Key] = kvp.Value;
                }
            }
            else
            {
                elementColors.Add(kvp.Key, kvp.Value);
            }
        }

        return elementColors;
    }

    public string GetColor(GuiElement element)
    {
        return GuiElementColors[element];
    }

    public static string GetDefaultColor(GuiElement element)
    {
        return DefaultColors[element];
    }

    public void Update(Dictionary<GuiElement, string> elementColors)
    {
        var colorHasChanged = false;
        foreach (var kvp in elementColors)
        {
            if (!HexRegex.IsMatch(kvp.Value) || GuiElementColors[kvp.Key].Equals(kvp.Value)) continue;
            GuiElementColors[kvp.Key] = kvp.Value;
            colorHasChanged = true;
        }

        if (colorHasChanged)
        {
            SettingsChangedEvent.Publish(NoArgs.Instance);
        }
    }
}
