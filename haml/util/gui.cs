/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System;
using System.Windows;
using ArcGIS.Desktop.Framework;
using MessageBox = ArcGIS.Desktop.Framework.Dialogs.MessageBox;

namespace HamlProAppModule.haml.util
{
    public static class GUI
    {
        public static void ShowToast(string message)
        {
            Notification notification = new Notification
            {
                Title = FrameworkApplication.Title,
                Message = message,
                ImageUrl = @"pack://application:,,,/ArcGIS.Desktop.Resources;component/Images/ToastLicensing32.png"
            };
            
            FrameworkApplication.AddNotification(notification);
        }
        
        public static bool ShowMessageBox(string message, string caption, bool cancelable)
        {
            bool ret = false;

            MessageBoxResult dialogResult;
            if (cancelable)
            {
                dialogResult = MessageBox.Show(
                    message,
                    caption,
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Exclamation);
            }
            else
            {
                dialogResult = MessageBox.Show(
                    message,
                    caption,
                    MessageBoxButton.OK,
                    MessageBoxImage.Exclamation);
            }
            
            if (dialogResult == MessageBoxResult.OK)
            {
                ret = true;
            }

            return ret;
        }
        
        // Leaving this method in the event of us running into a situ where QueuedTask does not solve a different thread
        // issue
        internal static void RunOnUiThread(Action action)
        {
            try
            {
                if (IsOnUiThread)
                    action();
                else
                    Application.Current.Dispatcher.Invoke(action);
            }
            catch (Exception ex)
            {
                MessageBox.Show($@"Unable to run on UI thread; {ex.Message}");
            }
        }

        public static bool IsOnUiThread => FrameworkApplication.TestMode || Application.Current.Dispatcher.CheckAccess();
    }
}
