/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System.Collections.Generic;
using System.Linq;
using ArcGIS.Core.Geometry;
using HamlProAppModule.haml.geometry;

namespace HamlProAppModule.haml.util;

public struct FeatureClassEntry
{
    public Dictionary<string, double> DoubleFields { get; set; }
    public Dictionary<string, int> IntegerFields { get; set;}
    public Dictionary<string, string> StringFields { get; set;}
    
    public Profile P { get; set; }
    public Geometry Shape { get; set; }

    public List<string> GetFields()
    {
        var ret = DoubleFields.Keys.ToList();
        ret.AddRange(IntegerFields.Keys.ToList());
        ret.AddRange(StringFields.Keys.ToList());
        return ret;
    }
}
