/*
!#######################################################################

THIS SOURCE CODE IS PROPERTY OF THE GOVERNMENT OF THE UNITED STATES OF AMERICA. BY USING, MODIFYING, OR DISSEMINATING THIS SOURCE CODE, YOU ACCEPT THE TERMS AND CONDITIONS IN THE NRL OPEN LICENSE AGREEMENT. USE, MODIFICATION, AND DISSEMINATION ARE PERMITTED ONLY IN ACCORDANCE WITH THE TERMS AND CONDITIONS OF THE NRL OPEN LICENSE AGREEMENT. NO OTHER RIGHTS OR LICENSES ARE GRANTED. UNAUTHORIZED USE, SALE, CONVEYANCE, DISPOSITION, OR MODIFICATION OF THIS SOURCE CODE MAY RESULT IN CIVIL PENALTIES AND/OR CRIMINAL PENALTIES UNDER 18 U.S.C. Â§ 641.

!########################################################################
*/


using System;
using ArcGIS.Core.Geometry;

namespace HamlProAppModule.haml.geometry {
    //Represents a vertex bound by a geometric constraint.
    public class Vertex {
        private MapPoint _mp;
        private IConstraint _constraint;

        public Vertex(MapPoint mp, IConstraint constraint = null) {
            if (constraint == null)
            {
                _constraint = new FreeConstraint(mp);
                _mp = mp;
            }
            else
            {
                _constraint = constraint;
                _mp = constraint.GetNearestPoint(mp.X, mp.Y);
            }
            
            Id = CreateVertexID();
        }

        public double GetX() {

            return _mp.X;
        }

        public double GetY() {

            return _mp.Y;
        }

        public MapPoint GetPoint() {

            return _mp;
        }

        public MapPoint Set(double x, double y)
        {
            MapPoint req = MapPointBuilderEx.CreateMapPoint(x, y);
            return Set(req);
        }

        public MapPoint Set(MapPoint req)
        {
            return Set(req, true);
        }
        
        public MapPoint Set(MapPoint req, bool getNearestPoint)
        {
            if (getNearestPoint)
            {
                _mp = _constraint.GetNearestPoint(req);    
            }
            else
            {
                _mp = req;
            }

            return _mp;
        }

        public IConstraint GetConstraint() {

            return _constraint;
        }
        public double DistanceSqTo(Vertex other) {

            return DistanceSqTo(other._mp);
        }

        public double DistanceSqTo(MapPoint other) {

            return Math.Pow(_mp.X - other.X, 2) + Math.Pow(_mp.Y - other.Y, 2);
        }

        public override bool Equals(object obj)
        {
            return obj is Vertex vertex && Id.Equals( vertex.Id);
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        public void SetConstraint(IConstraint constraint)
        {
            _constraint = constraint;
        }

        public string Id { get; }

        private string CreateVertexID()
        {
            String prefix = "vertex_";
            String suffix = _mp.GetHashCode().ToString();
            String ret = prefix + suffix;
            return ret;
        }
        
        public double DistAlongBaseline { get; set; }
        
        public int ProfileIdx { get; set; }
        
        public int BaselineOid { get; set; }
        
        public int BaselineSegIdx { get; set; }
    }
    
}
