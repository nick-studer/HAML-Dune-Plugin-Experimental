/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ArcGIS.Core.Data;
using ArcGIS.Desktop.Catalog;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using HamlProAppModule.haml.events;
using HamlProAppModule.haml.logging;
using Serilog;

namespace HamlProAppModule.haml.ui.options
{
    /// <summary>
    /// Interaction logic for PixelDepth.xaml
    /// </summary>
    public partial class OptionsWindow : INotifyPropertyChanged
    {
        protected ILogger Log => LogManager.GetLogger(GetType());
        
        private string _gdbPath;
        public string GDBPath
        {
            get { return _gdbPath; }
            set
            {
                _gdbPath = value;
                OnPropertyChanged(nameof(GDBPath));
            }
        }

        private int _selectedBitDepth;
        public int SelectedBitDepth
        {
            get { return _selectedBitDepth; }
            set
            {
                _selectedBitDepth = value;
                OnPropertyChanged(nameof(SelectedBitDepth));
            }
        }

        private bool _validBitDepth = true;
        public bool ValidBitDepth
        {
            get { return _validBitDepth; }
            set
            {
                _validBitDepth = value;
                OnPropertyChanged(nameof(ValidBitDepth));
            }
        }
        
        private int _transectLandwardLength = 200;
        public int TransectLandwardLength
        {
            get { return _transectLandwardLength; }
            set
            {
                Log.Here().Information("Setting landward length to {@LandwardLength}.", _transectLandwardLength);
                _transectLandwardLength = value;
                OnPropertyChanged(nameof(TransectLandwardLength));
            }
        }
        
        private int _transectSeawardLength = 50;
        public int TransectSeawardLength
        {
            get { return _transectSeawardLength; }
            set
            {
                Log.Here().Information("Setting seaward length to {@SeawardLength}.", _transectSeawardLength);
                _transectSeawardLength = value;
                OnPropertyChanged(nameof(TransectSeawardLength));
            }
        }

        private double _selectedMeanHighWaterPoint;

        public double SelectedMeanHighWaterPoint
        {
            get { return _selectedMeanHighWaterPoint; }
            set
            {
                _selectedMeanHighWaterPoint = value;
                OnPropertyChanged(nameof(SelectedMeanHighWaterPoint));
            }
        }
        
        private double _insertThresh;

        public double InsertThresh
        {
            get { return _insertThresh; }
            set
            {
                _insertThresh = value;
                OnPropertyChanged(nameof(_insertThresh));
            }
        }
        
        private double _crestThresh;

        public double CrestThresh
        {
            get { return _crestThresh; }
            set
            {
                _crestThresh = value;
                OnPropertyChanged(nameof(_crestThresh));
            }
        }
        
        private double _toeThresh;

        public double ToeThresh
        {
            get { return _toeThresh; }
            set
            {
                _toeThresh = value;
                OnPropertyChanged(nameof(_toeThresh));
            }
        }

        private bool _validMeanHighWaterPoint = true;
        
        public bool ValidMeanHighWaterPoint
        {
            get { return _validMeanHighWaterPoint; }
            set
            {
                _validMeanHighWaterPoint = value;
                OnPropertyChanged(nameof(ValidMeanHighWaterPoint));
            }
        }
        
        private bool _validTransectLengths = true;
        
        public bool ValidTransectLengths
        {
            get { return _validTransectLengths; }
            set
            {
                _validTransectLengths = value;
                OnPropertyChanged(nameof(ValidTransectLengths));
            }
        }

        private double _selectedProfileSpacing;

        public double SelectedProfileSpacing
        {
            get { return _selectedProfileSpacing; }
            set
            {
                _selectedProfileSpacing = value;
                OnPropertyChanged(nameof(SelectedProfileSpacing));
            }
        }

        private bool _validProfileSpacing = true;

        public bool ValidProfileSpacing
        {
            get { return _validProfileSpacing; }
            set
            {
                _validProfileSpacing = value;
                OnPropertyChanged(nameof(ValidProfileSpacing));
            }
        }

        private bool _validGDB = true;
        public bool ValidGDB
        {
            get { return _validGDB; }
            set
            {
                _validGDB = value;
                OnPropertyChanged(nameof(ValidGDB));
            }
        }

        private List<Layer> _layerList;
        public List<Layer> LayerList
        {
            get { return _layerList; }
            set
            {
                _layerList = value;
                OnPropertyChanged(nameof(LayerList));
            }
        }

        private Layer _selectedLayer;
        public Layer SelectedLayer
        {
            get { return _selectedLayer; }
            set
            {
                _selectedLayer = value;
                OnPropertyChanged(nameof(SelectedLayer));
            }
        }
        
        private string[] _elements;
        public string[] Elements
        {
            get => _elements;
            set
            {
                _elements = value;
                OnPropertyChanged(nameof(Elements));
            }
        }
        
        private string _selectedElement;
        public string SelectedElement
        {
            get => _selectedElement;
            set
            {
                _selectedElement = value;
                OnPropertyChanged(nameof(SelectedElement));
            }
        }
        
        private Color _selectedColor;
        public Color SelectedColor
        {
            get => _selectedColor;
            set
            {
                _selectedColor = value;
                OnPropertyChanged(nameof(SelectedColor));
            }
        }
        
        private double _opacitySliderValue;
        public double OpacitySliderValue
        {
            get => _opacitySliderValue;
            set
            {
                _opacitySliderValue = value;
                OnPropertyChanged(nameof(OpacitySliderValue));  
            } 
        }
        
        private Dictionary<ColorSettings.GuiElement, string> _colorChanges;
        private Dictionary<string, ColorSettings.GuiElement> _formattedElements;

        internal bool Cancel { get; private set; } = true;
        internal Dictionary<Layer, RasterLayerSettings> RasterLayerSettings { get; private set; }

        internal List<string> instructions = new List<string>();
        private Dictionary<Layer, RasterLayerSettings> OriginalRasterLayerSettings { get; }

        internal OptionsWindow(Dictionary<Layer, RasterLayerSettings> rasterLayerSettings)
        {
            InitializeComponent();
            DataContext = this;
            _colorChanges = new Dictionary<ColorSettings.GuiElement, string>();
            _formattedElements = new Dictionary<string, ColorSettings.GuiElement>();
            FormatGuiElements();
            RasterLayerSettings = rasterLayerSettings;
            OriginalRasterLayerSettings = new Dictionary<Layer, RasterLayerSettings>(RasterLayerSettings);
            _opacitySliderValue = Module1.Opacity;
            if (RasterLayerSettings.Count() == 0)
            {
                BitDepthSP.Visibility = Visibility.Collapsed;
                InstructionsSP.Visibility = Visibility.Collapsed;
            }
            LayerList = RasterLayerSettings.Keys.ToList();
            SelectedLayer = LayerList.FirstOrDefault();
            DefaultValues();

            GDBPathTB.Focus();
            GDBPathTB.Select(GDBPathTB.Text.Length, 0);

            Closing += PixelDepth_Closing;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void PixelDepth_Closing(object sender, CancelEventArgs e)
        {
            if (Cancel)
            {
                DefaultValues();
            }
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var browseDialog = new OpenItemDialog
            {
                Title = "Browse for a Geodatabase",
                InitialLocation = Project.Current.HomeFolderPath,
                MultiSelect = false,
                Filter = ItemFilters.Geodatabases
            };

            var result = browseDialog.ShowDialog();
            if (result.GetValueOrDefault())
            {
                var item = browseDialog.Items.FirstOrDefault();
                if (item != null)
                {
                    GDBPath = item.Path;
                }
            }
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            Cancel = false;
            Module1.UpdateColorSettings(_colorChanges);
            Module1.UpdateMeanHighWaterPoint(_selectedMeanHighWaterPoint);
            Module1.UpdateProfileSpacing(_selectedProfileSpacing);
            Module1.UpdateTransectLandwardLength(_transectLandwardLength);
            Module1.UpdateTransectSeawardLength(_transectSeawardLength);
            Module1.UpdateInsertThresh(_insertThresh);
            Module1.UpdateCrestThresh(_crestThresh);
            Module1.UpdateToeThresh(_toeThresh);
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Cancel = true;
            Close();
        }

        private void Reset_Click(object sender, RoutedEventArgs args)
        {
            var element = _formattedElements[_selectedElement];
            SelectedColor = (Color)ColorConverter.ConvertFromString(Module1.GetDefaultColor(element));
            if (_colorChanges.ContainsKey(element))
            {
                _colorChanges[element] = _selectedColor.ToString();
            }
            else
            {
                _colorChanges.Add(element, _selectedColor.ToString());
            }
        }

        private void DefaultValues()
        {
            SelectedMeanHighWaterPoint = Module1.MeanHighWaterVal;
            SelectedProfileSpacing = Module1.ProfileSpacing;
            TransectLandwardLength = Module1.TransectLandwardLength;
            TransectSeawardLength = Module1.TransectSeawardLength;
            InsertThresh = Module1.smartInsertionTolerance;
            CrestThresh = Module1.highTolerance;
            ToeThresh = Module1.lowTolerance;
            
            RasterLayerSettings = OriginalRasterLayerSettings;
            if (SelectedLayer != null)
            {
                SelectedBitDepth = RasterLayerSettings[SelectedLayer].UserDefinedBitDepth;
            }

            if (string.IsNullOrWhiteSpace(OptionsVM.gdbPath))
            {
                GDBPath = Module1.DefaultGDBPath;
            }
            else
            {
                GDBPath = OptionsVM.gdbPath;
            }
        }

        // TODO: this is overkill
        private static readonly Regex _validInt = new Regex("^[0-9]+$");
        private bool IsInputValidNumber(string text, Regex valueCheck)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }
            return valueCheck.IsMatch(text);
        }

        string originalBitDepth = null;
        private void BitDepth_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var tb = (TextBox)sender;
            originalBitDepth = tb.Text;
        }

        private void BitDepth_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            var tb = (TextBox)sender;
            originalBitDepth = tb.Text;
        }

        private void BitDepth_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (BitDepthSP.Visibility == Visibility.Collapsed)
            {
                ValidBitDepth = true;
                return;
            }

            var tb = (TextBox)sender;

            int.TryParse(tb.Text, out int bitDepth);
            var isTextAllowed = true;
            if (e.Changes.FirstOrDefault().AddedLength > 0)
            {
                isTextAllowed = IsInputValidNumber(tb.Text, _validInt);
            }
            
            if ((SelectedLayer != null && RasterLayerSettings.ContainsKey(SelectedLayer) && bitDepth > RasterLayerSettings[SelectedLayer].MaxBitDepth) || !isTextAllowed)
            {
                tb.Text = originalBitDepth;
                ValidBitDepth = true;
            }
            else if (bitDepth == 0)
            {
                ValidBitDepth = false;
            }
            else
            {
                SelectedBitDepth = bitDepth;
                RasterLayerSettings[SelectedLayer] = new RasterLayerSettings(SelectedLayer, RasterLayerSettings[SelectedLayer].MaxBitDepth, SelectedBitDepth);
                ValidBitDepth = true;
            }

            tb.Select(tb.Text.Length, 0);
            Validation.ClearInvalid(tb.GetBindingExpression(TextBox.TextProperty));
        }
        
        string originalMeanHighWaterPoint = null;
        private void MeanHighWaterPoint_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var tb = (TextBox)sender;
            originalMeanHighWaterPoint = tb.Text;
        }

        private void MeanHighWaterPoint_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            var tb = (TextBox)sender;
            originalMeanHighWaterPoint = tb.Text;
        }

        private void MeanHighWaterPoint_TextChanged(object sender, TextChangedEventArgs e)
        {
            double.TryParse(((TextBox)sender).Text, out double selectedMeanHighWaterPoint);
            _selectedMeanHighWaterPoint = selectedMeanHighWaterPoint;
        }
        
        string originalProfileSpacing = "10";

        private void ProfileSpacing_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            var tb = (TextBox)sender;
            originalProfileSpacing = tb.Text;
        }

        private void ProfileSpacing_TextChanged(object sender, TextChangedEventArgs e)
        {
            double.TryParse(((TextBox)sender).Text, out double selectedProfileSpacing);
            //if the user puts in complete nonsense, correct the value to 10 meters to prevent crashing when attempting
            // to place profiles
            _selectedProfileSpacing = selectedProfileSpacing == 0 ? 10 : selectedProfileSpacing;
        }
        
        private bool _isFixedTransect = true; 
        public bool IsFixedTransect
        {
            get { return _isFixedTransect; }
            set
            {
                _isFixedTransect = value;
                OnPropertyChanged(nameof(IsFixedTransect));
            }
        }
        private void HandleFixedOrMapview(object sender, RoutedEventArgs routedEventArgs)
        {
            RadioButton rb = sender as RadioButton ?? throw new InvalidOperationException();
            IsFixedTransect = rb.Name.Equals("cbFixed");
            Module1.UpdateTransectConstraint(_isFixedTransect);
        }
        
        private void TransectLen_TextChanged(object sender, TextChangedEventArgs e)
        {
            var tb = (TextBox)sender;
            var tbName = tb.Name;
            string text = tb.Text;

            bool isLandward = tbName.Equals("landwardTextBox");

            int minVal = isLandward ? 10 : 0;
            
            bool valid = true;
            if (text.Length > 0)
            {
                if (IsInputValidNumber(text, _validInt))
                {
                    int candLen = Int32.Parse(text);

                    if (text.Length == 1)
                    {
                        if (isLandward)
                        {
                            ValidTransectLengths = false;

                            // will automatically backspace in the event of starting with 0 and landward value
                            if (candLen == 0)
                            {
                                valid = false;
                            }
                        }
                        else
                        {
                            ValidTransectLengths = true;
                        }
                    } 
                    else if (text.Length > 1 && candLen >= minVal)
                    {
                        ValidTransectLengths = true;
                        if (isLandward)
                        {
                            TransectLandwardLength = candLen;
                        }
                        else
                        {
                            TransectSeawardLength = candLen;
                        }
                    }
                }
                else
                {
                    valid = false;
                }
                
                if (!valid)
                {
                    tb.Text = tb.Text.Remove(tb.Text.Length - 1);
                    tb.SelectionStart = tb.Text.Length;
                }
            }
            else
            {
                ValidTransectLengths = false;
            }
        }

        // TODO: value checking/sanitization
        private void InsertThresh_TextChanged(object sender, TextChangedEventArgs e)
        {
            var tb = (TextBox)sender;
            string text = tb.Text;

            Module1.smartInsertionTolerance = Double.Parse(text);
        }
        
        // TODO: value checking/sanitization
        private void CrestThresh_TextChanged(object sender, TextChangedEventArgs e)
        {
            var tb = (TextBox)sender;
            string text = tb.Text;

            Module1.highTolerance = Double.Parse(text);
        }
        
        // TODO: value checking/sanitization
        private void ToeThresh_TextChanged(object sender, TextChangedEventArgs e)
        {
            var tb = (TextBox)sender;
            string text = tb.Text;

            Module1.lowTolerance = Double.Parse(text);
        }

        private bool CheckGDBValid(string gdbPath)
        {
            if (string.IsNullOrWhiteSpace(gdbPath))
            {
                return false;
            }

            if (!Directory.Exists(gdbPath))
            {
                return false;
            }

            return QueuedTask.Run(() =>
            {
                try
                {
                    using (var geodatabase = new Geodatabase(new FileGeodatabaseConnectionPath(new Uri(gdbPath))))
                    {
                        return true;
                    }
                }
                catch
                {
                    return false;
                }
            }).Result;
        }

        private void GDBPath_TextChanged(object sender, TextChangedEventArgs e)
        {
            var tb = (TextBox)sender;
            var gdbPath = tb.Text;
            ValidGDB = CheckGDBValid(gdbPath);
        }

        private void GDBPath_LostFocus(object sender, RoutedEventArgs e)
        {
            var tb = (TextBox)sender;
            tb.Select(tb.Text.Length, 0);
        }

        private void LayerListCB_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedLayer = (Layer)e.AddedItems[0];
            if (RasterLayerSettings.ContainsKey(selectedLayer))
            {
                SelectedBitDepth = RasterLayerSettings[selectedLayer].UserDefinedBitDepth;
            }
        }
        
        public void OnSelectionChanged(object sender, SelectionChangedEventArgs args)
        {
            SelectedColor = (Color)ColorConverter.ConvertFromString(
                    Module1.GetElementHexColor(_formattedElements[_selectedElement])
                );
        }

        private void OnSelectedColorChanged(object sender, RoutedPropertyChangedEventArgs<Color?> e)
        {
            var element = _formattedElements[_selectedElement];
            if (_colorChanges.ContainsKey(element))
            {
                _colorChanges[element] = _selectedColor.ToString();
            }
            else
            {
                _colorChanges.Add(element, _selectedColor.ToString());
            }
        }

        private void OnOpacitySliderChanged(object sender, RoutedEventArgs e)
        {
            Module1.Opacity = (float) OpacitySliderValue;
            SettingsChangedEvent.Publish(NoArgs.Instance);
        }

        // Reformatting enum names by creating a space between each word
        private void FormatGuiElements()
        {
            var r = new Regex(@"(?<!^)(?=[A-Z])");
            foreach (ColorSettings.GuiElement element in Enum.GetValues(typeof(ColorSettings.GuiElement)))
            {
                var split = r.Split(element.ToString());
                _formattedElements[string.Join(" ", split)] = element;
            }

            Elements = _formattedElements.Keys.ToArray();
            SelectedElement = _elements[0];
        }
    }

    public class BooleanAndConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            return values.All(value => value is bool && (bool)value);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
