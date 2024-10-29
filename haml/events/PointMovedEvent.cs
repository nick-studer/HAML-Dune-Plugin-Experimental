/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System;
using ArcGIS.Core.Events;
using ArcGIS.Desktop.Framework;

namespace HamlProAppModule.haml.events;

public class PointMovedEvent : CompositePresentationEvent<PointMovedEventArgs>
{
    private PointMovedEvent() 
    {
    }

    public static SubscriptionToken Subscribe(
        Action<PointMovedEventArgs> action,
        bool keepSubscriberAlive = false)
    {
        return FrameworkApplication.EventAggregator.GetEvent<PointMovedEvent>()
            .Register(action, keepSubscriberAlive);
    }
        
    public static void Unsubscribe(Action<PointMovedEventArgs> action) => FrameworkApplication.EventAggregator.GetEvent<PointMovedEvent>().Unregister(action);
        
    public static void Unsubscribe(SubscriptionToken token) => FrameworkApplication.EventAggregator.GetEvent<PointMovedEvent>().Unregister(token);

    internal static void Publish(PointMovedEventArgs eventArgs) => FrameworkApplication.EventAggregator.GetEvent<PointMovedEvent>().Broadcast(eventArgs);
}
