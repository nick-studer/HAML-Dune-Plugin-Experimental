/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System;
using System.Collections.Generic;
using System.Linq;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Events;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using HamlProAppModule.haml.events;

namespace HamlProAppModule.haml.ui;

public class OverlayController : IDisposable
{
    // I would have passed the maptool object but its add and
    // update overlay functions are protected
    // This was a way around that.
    private Func<Geometry, CIMSymbolReference, double, IDisposable> _addFunc;
    private Func<IDisposable, Geometry, CIMSymbolReference, double, bool> _updateFunc;
    // Used to prevent certain overlay types from being added or updated
    // Used for toggling but we may be able to get rid of that as we're currently only
    // toggling for one layer.
    private HashSet<HamlGraphicType> _exemptSet;

    private readonly Dictionary<HamlGraphicType, IDisposable> _overlays;
    private SubscriptionToken _token;

    public OverlayController(Func<Geometry, CIMSymbolReference, double, IDisposable> addOverlayFunc, 
        Func<IDisposable, Geometry, CIMSymbolReference, double, bool> updateOverlayFunc)
    {
        _addFunc = addOverlayFunc;
        _updateFunc = updateOverlayFunc;
        _exemptSet = new HashSet<HamlGraphicType>();
        _overlays = new Dictionary<HamlGraphicType, IDisposable>();
        _token = GeometryChangedEvent.Subscribe(OnGeometryChanged);
    }

    public void AddOverlay(Geometry geometry, CIMSymbolReference reference, HamlGraphicType type)
    {
        if (IsExempt(type)) return;
        Remove(type);

        var overlay = new Overlay{
            Disposable = _addFunc.Invoke(geometry, reference, -1), 
            Reference = reference
        };
        _overlays.Add(type, overlay);
    }

    public void AddOverlay(CIMGraphic graphic, HamlGraphicType type)
    {
        if (IsExempt(type)) return;
        Remove(type);
        _overlays.Add(type, MapView.Active.AddOverlay(graphic));
    }

    // Not currently being used but could be used to update an overlay manually
    public void UpdateOverlay(Geometry geometry, HamlGraphicType type)
    {
        if (IsExempt(type)) return;
        
        if (Contains(type) && _overlays[type] is Overlay overlay)
        {
            UpdateOverlay(overlay.Disposable, geometry, overlay.Reference);
        }
    }

    public bool Contains(HamlGraphicType type)
    {
        return _overlays.ContainsKey(type);
    }

    // Adds a haml graphic type to the exempt list so that type
    // of overlay may not be added. Will remove that overlay if it exists
    // Used for overlay toggling.
    public void AddExemption(HamlGraphicType type)
    {
        _exemptSet.Add(type);
        Remove(type);
    }
    
    // Removes a haml graphic type to the exempt list so that type
    // of overlay may be added.
    // Used for overlay toggling.
    public void RemoveExemption(HamlGraphicType type)
    {
        _exemptSet.Remove(type);
    }

    public void Remove(HamlGraphicType type)
    {
        if (!Contains(type)) return;
        _overlays[type]?.Dispose();
        _overlays.Remove(type);
    }

    public void Remove(List<HamlGraphicType> types)
    {
        types.ForEach(Remove);
    }

    public void RemoveAll()
    {
        _overlays.Values.ToList().ForEach(o => o.Dispose());
        _overlays.Clear();
    }
    
    public void Dispose()
    {
        GeometryChangedEvent.Unsubscribe(_token);
        _token = null;
        RemoveAll();
    }

    // Currently all of the CIMGraphic overlays are temporary so
    // we only need to worry about auto updating the geometry overlays
    private void OnGeometryChanged(GeometryChangedEventArgs args)
    {
        var type = args.Type;
        var geometry = args.Geometry;
        
        if (IsExempt(type) || !Contains(type) || geometry is null) return;

        if (_overlays[type] is not Overlay overlay) return;
        
        if (args.Reference is not null)
        {
            overlay.Reference = args.Reference;    
        }
        
        UpdateOverlay(overlay.Disposable, geometry, overlay.Reference);
    }
    
    private void UpdateOverlay(IDisposable disposable, Geometry geometry, CIMSymbolReference reference)
    {
        QueuedTask.Run(() => _updateFunc(disposable, geometry, reference, -1));
    }

    private bool IsExempt(HamlGraphicType type)
    {
        return _exemptSet.Contains(type);
    }
}
