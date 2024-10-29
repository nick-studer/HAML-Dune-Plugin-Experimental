/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System;
using ArcGIS.Core.Events;
using ArcGIS.Desktop.Framework;

namespace HamlProAppModule.haml.events;

public class GeometryChangedEvent : CompositePresentationEvent<GeometryChangedEventArgs>
{
    private GeometryChangedEvent()
    {
    }

    public static SubscriptionToken Subscribe(
        Action<GeometryChangedEventArgs> action,
        bool keepSubscriberAlive = false)
    {
        return FrameworkApplication.EventAggregator.GetEvent<GeometryChangedEvent>()
            .Register(action, keepSubscriberAlive);
    }
        
    public static void Unsubscribe(Action<GeometryChangedEventArgs> action) => FrameworkApplication.EventAggregator.GetEvent<GeometryChangedEvent>().Unregister(action);
        
    public static void Unsubscribe(SubscriptionToken token) => FrameworkApplication.EventAggregator.GetEvent<GeometryChangedEvent>().Unregister(token);

    internal static void Publish(GeometryChangedEventArgs eventArgs) => FrameworkApplication.EventAggregator.GetEvent<GeometryChangedEvent>().Broadcast(eventArgs);
}
