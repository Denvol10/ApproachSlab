using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace ApproachSlab.Models
{
    internal class RevitGeometryUtils
    {
        public static List<Curve> GetCurvesByRectangle(UIApplication uiapp, out string elementIds)
        {
            Selection sel = uiapp.ActiveUIDocument.Selection;
            var selectedElements = sel.PickElementsByRectangle("Select Road Axis");
            var directshapeRoadAxis = selectedElements.OfType<DirectShape>();
            elementIds = ElementIdToString(directshapeRoadAxis);
            var curvesRoadAxis = GetCurvesByDirectShapes(directshapeRoadAxis);

            return curvesRoadAxis;
        }

        // Получение линий оси трассы на основе не только directshape линий но и линий модели
        public static List<Curve> GetCurvesByDirectShapeAndModelLine(UIApplication uiapp, out string elementIds)
        {
            Selection sel = uiapp.ActiveUIDocument.Selection;
            var selectedElements = sel.PickObjects(ObjectType.Element,
                                                   new DirectShapeAndModelLineFilter(),
                                                   "Select Road Axis or Approach Slab Axis Line");
            var elements = selectedElements.Select(r => uiapp.ActiveUIDocument.Document.GetElement(r));
            bool isContainsModelLine = elements.Any(e => e is ModelCurve);

            Options options = new Options();
            if (isContainsModelLine)
            {
                var modelCurvesElems = elements.OfType<ModelCurve>();
                elementIds = ElementIdToString(modelCurvesElems);
                var modelCurves = modelCurvesElems.Select(e => e.get_Geometry(options).First()).OfType<Curve>().ToList();
                return modelCurves;
            }
            else
            {
                var directShapeELems = elements.OfType<DirectShape>();
                elementIds = ElementIdToString(directShapeELems);
                var directshapeCurves = GetCurvesByDirectShapes(directShapeELems);
                return directshapeCurves;
            }
        }

        // Метод получения списка линий на поверхности дороги
        public static List<Line> GetRoadLines(UIApplication uiapp, out string elementIds)
        {
            Selection sel = uiapp.ActiveUIDocument.Selection;
            var selectedOnRoadSurface = sel.PickObjects(ObjectType.Element, "Select Road Lines");
            var directShapesRoadSurface = selectedOnRoadSurface.Select(r => uiapp.ActiveUIDocument.Document.GetElement(r))
                                                               .OfType<DirectShape>();
            elementIds = ElementIdToString(directShapesRoadSurface);
            var curvesRoadSurface = GetCurvesByDirectShapes(directShapesRoadSurface);
            var linesRoadSurface = curvesRoadSurface.OfType<Line>().ToList();

            return linesRoadSurface;
        }

        public static List<Line> GetCurvesByLines(UIApplication uiapp, out string elementIds)
        {
            Selection sel = uiapp.ActiveUIDocument.Selection;
            var curvesPicked = sel.PickObjects(ObjectType.Element, new ModelLineClassFilter(), "Select lines for placement profiles");
            Options options = new Options();
            var elements = curvesPicked.Select(r => uiapp.ActiveUIDocument.Document.GetElement(r));
            elementIds = ElementIdToString(elements);
            var lines = elements.Select(e => e.get_Geometry(options).First()).OfType<Line>().ToList();

            return lines;
        }

        // Получение линии из списка, которая пересекается с плоскостью
        public static Line GetIntersectCurve(IEnumerable<Line> lines, Plane plane)
        {
            XYZ originPlane = plane.Origin;
            XYZ directionLine = plane.XVec;

            var lineByPlane = Line.CreateUnbound(originPlane, directionLine);

            foreach (var line in lines)
            {
                XYZ startPoint = line.GetEndPoint(0);
                XYZ finishPoint = line.GetEndPoint(1);

                XYZ startPointOnBase = new XYZ(startPoint.X, startPoint.Y, 0);
                XYZ finishPointOnBase = new XYZ(finishPoint.X, finishPoint.Y, 0);

                var baseLine = Line.CreateBound(startPointOnBase, finishPointOnBase);

                var result = new IntersectionResultArray();
                var compResult = lineByPlane.Intersect(baseLine, out result);
                if (compResult == SetComparisonResult.Overlap)
                {
                    return line;
                }
            }

            return null;
        }

        /* Пересечение линии и плоскости
         * (преобразует линию в вектор, поэтому пересекает любую линию не параллельную плоскости)
         */
        public static XYZ LinePlaneIntersection(Line line, Plane plane, out double lineParameter)
        {
            XYZ planePoint = plane.Origin;
            XYZ planeNormal = plane.Normal;
            XYZ linePoint = line.GetEndPoint(0);

            XYZ lineDirection = (line.GetEndPoint(1) - linePoint).Normalize();

            // Проверка на параллельность линии и плоскости
            if ((planeNormal.DotProduct(lineDirection)) == 0)
            {
                lineParameter = double.NaN;
                return null;
            }

            lineParameter = (planeNormal.DotProduct(planePoint)
              - planeNormal.DotProduct(linePoint))
                / planeNormal.DotProduct(lineDirection);

            return linePoint + lineParameter * lineDirection;
        }

        // Получение id элементов на основе списка в виде строки
        public static List<int> GetIdsByString(string elems)
        {
            if (string.IsNullOrEmpty(elems))
            {
                return null;
            }

            var elemIds = elems.Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(s => int.Parse(s.Remove(0, 2)))
                         .ToList();

            return elemIds;
        }

        // Проверка на то существуют ли элементы с данным Id в модели
        public static bool IsElemsExistInModel(Document doc, IEnumerable<int> elems, Type type)
        {
            if (elems is null)
            {
                return false;
            }

            foreach (var elem in elems)
            {
                ElementId id = new ElementId(elem);
                Element curElem = doc.GetElement(id);
                if (curElem is null || !(curElem.GetType() == type))
                {
                    return false;
                }
            }

            return true;
        }

        // Проверка на то существуют ли элементы оси в модели (элементы оси для переходной плиты могут быть DirectShape или ModelCurve)
        public static bool IsAxisElemsExistInModel(Document doc, IEnumerable<int> elems)
        {
            if (elems is null)
            {
                return false;
            }

            foreach (var elem in elems)
            {
                ElementId id = new ElementId(elem);
                Element curElem = doc.GetElement(id);
                if (curElem is null || !(curElem is DirectShape || curElem is ModelCurve))
                {
                    return false;
                }
            }

            return true;
        }

        // Получение линий по их id
        public static List<Curve> GetCurvesById(Document doc, IEnumerable<int> ids)
        {
            var directShapeLines = new List<DirectShape>();
            foreach (var id in ids)
            {
                ElementId elemId = new ElementId(id);
                DirectShape line = doc.GetElement(elemId) as DirectShape;
                directShapeLines.Add(line);
            }

            var lines = GetCurvesByDirectShapes(directShapeLines).OfType<Curve>().ToList();

            return lines;
        }

        // Получение линий по их id (в случае если за ось принимается ось трассы или линия модели)
        public static List<Curve> GetDirectShapeAndModelLinesCurvesById(Document doc, IEnumerable<int> ids)
        {
            var elementsInSettings = new List<Element>();
            foreach(var id in ids)
            {
                ElementId elemId = new ElementId(id);
                Element elem = doc.GetElement(elemId);
                elementsInSettings.Add(elem);
            }

            bool isContainsModelLine = elementsInSettings.Any(e => e is ModelCurve);

            Options options = new Options();
            if (isContainsModelLine)
            {
                var modelCurvesElems = elementsInSettings.OfType<ModelCurve>();
                var modelCurves = modelCurvesElems.Select(e => e.get_Geometry(options).First()).OfType<Curve>().ToList();
                return modelCurves;
            }
            else
            {
                var directShapeELems = elementsInSettings.OfType<DirectShape>();
                var directshapeCurves = GetCurvesByDirectShapes(directShapeELems);
                return directshapeCurves;
            }
        }

        // Получение линии по Id
        public static Curve GetBoundCurveById(Document doc, string elemIdInSettings)
        {
            var elemId = GetIdsByString(elemIdInSettings).First();
            ElementId modelLineId = new ElementId(elemId);
            Element modelLine = doc.GetElement(modelLineId);
            Options options = new Options();
            Curve line = modelLine.get_Geometry(options).First() as Curve;

            return line;
        }

        // Получение линий для построения профилей по их id
        public static List<Line> GetProfileLinesById(Document doc, IEnumerable<int> ids)
        {
            var elementsInSettings = new List<Element>();
            foreach (var id in ids)
            {
                ElementId elemId = new ElementId(id);
                Element elem = doc.GetElement(elemId);
                elementsInSettings.Add(elem);
            }

            Options options = new Options();
            var lines = elementsInSettings.Select(e => e.get_Geometry(options).First()).OfType<Line>().ToList();

            return lines;
        }

        // Получение линий на основе элементов DirectShape
        private static List<Curve> GetCurvesByDirectShapes(IEnumerable<DirectShape> directShapes)
        {
            var curves = new List<Curve>();

            Options options = new Options();
            var geometries = directShapes.Select(d => d.get_Geometry(options)).SelectMany(g => g);

            foreach (var geom in geometries)
            {
                if (geom is PolyLine polyLine)
                {
                    var polyCurve = GetCurvesByPolyline(polyLine);
                    curves.AddRange(polyCurve);
                }
                else
                {
                    curves.Add(geom as Curve);
                }
            }

            return curves;
        }

        // Метод получения списка линий на основе полилинии
        private static IEnumerable<Curve> GetCurvesByPolyline(PolyLine polyLine)
        {
            var curves = new List<Curve>();

            for (int i = 0; i < polyLine.NumberOfCoordinates - 1; i++)
            {
                var line = Line.CreateBound(polyLine.GetCoordinate(i), polyLine.GetCoordinate(i + 1));
                curves.Add(line);
            }

            return curves;
        }

        // Метод получения строки с ElementId
        private static string ElementIdToString(IEnumerable<Element> elements)
        {
            var stringArr = elements.Select(e => "Id" + e.Id.IntegerValue.ToString()).ToArray();
            string resultString = string.Join(", ", stringArr);

            return resultString;
        }
    }
}
