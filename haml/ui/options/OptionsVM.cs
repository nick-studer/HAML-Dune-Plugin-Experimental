/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System.Collections.Generic;
using System.Linq;
using ArcGIS.Core.Data.Raster;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;

namespace HamlProAppModule.haml.ui.options
{
    internal class OptionsVM
    {
        public static string gdbPath = "";
        public static Dictionary<Layer, RasterLayerSettings> rasterLayerSettings = new Dictionary<Layer, RasterLayerSettings>();

        private static OptionsWindow optionsWindow = null;
        internal async static void OpenOptionsWindow()
        {
            if (optionsWindow != null)
                return;
            
            var layerList = MapView.Active?.Map?.Layers;
            List<Layer> rasterLayers = new List<Layer>();
            if (layerList != null)
            {
                var compositeLayers = layerList.Where(lyr => lyr is CompositeLayer);
                foreach (var compositeLyr in compositeLayers)
                {
                    rasterLayers.AddRange(((CompositeLayer)compositeLyr).Layers.Where(lyr => lyr is BasicRasterLayer));
                }
                rasterLayers.AddRange(layerList.Where(lyr => lyr is BasicRasterLayer));
            }
            if (rasterLayers.Count() == 0)
            {
                rasterLayerSettings.Clear();
            }
            await QueuedTask.Run(() => {
                foreach (var lyr in rasterLayers)
                {
                    if (!rasterLayerSettings.ContainsKey(lyr))
                    {
                        BasicRasterLayer derivativeRasterLayer = lyr as BasicRasterLayer;
                        Raster derivativeRaster = derivativeRasterLayer.GetRaster();
                        RasterDataset rasterDataset = derivativeRaster.GetRasterDataset() as RasterDataset;
                        Raster sourceRaster = rasterDataset.CreateFullRaster();
                        var pixelType = sourceRaster.GetPixelType();
                        var bitDepth = GetBitDepthFromPixelType(pixelType);
                        rasterLayerSettings[lyr] = new RasterLayerSettings(lyr, bitDepth, bitDepth);
                    }
                }
            });

            optionsWindow = new OptionsWindow(rasterLayerSettings)
            {
                Owner = FrameworkApplication.Current.MainWindow
            };

            optionsWindow.Closed += (o, e) =>
            {
                if (!optionsWindow.Cancel)
                {
                    gdbPath = optionsWindow.GDBPath;
                    rasterLayerSettings = optionsWindow.RasterLayerSettings;
                }
                optionsWindow = null;
            };

            optionsWindow.Show();
        }

        private static int GetBitDepthFromPixelType(RasterPixelType pixelType)
        {
            switch (pixelType)
            {
                case RasterPixelType.U1:
                    return 1;
                case RasterPixelType.U2:
                    return 2;
                case RasterPixelType.U4:
                    return 4;
                case RasterPixelType.CHAR:
                case RasterPixelType.UCHAR:
                    return 8;
                case RasterPixelType.SHORT:
                case RasterPixelType.USHORT:
                case RasterPixelType.CSHORT:
                    return 16;
                case RasterPixelType.LONG:
                case RasterPixelType.ULONG:
                case RasterPixelType.CLONG:
                case RasterPixelType.FLOAT:
                    return 32;
                case RasterPixelType.DOUBLE:
                case RasterPixelType.DCOMPLEX:
                    return 64;
                case RasterPixelType.UNKNOWN:
                case RasterPixelType.COMPLEX:
                default:
                    return 0;
            }
        }
    }

    class OptionsWindowOpen : ArcGIS.Desktop.Framework.Contracts.Button
    {
        protected override void OnClick()
        {
            OptionsVM.OpenOptionsWindow();
        }
    }
}
