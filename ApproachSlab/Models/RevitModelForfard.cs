using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.ObjectModel;
using ApproachSlab.Models;
using System.IO;
using System.Text;

namespace ApproachSlab
{
    public class RevitModelForfard
    {
        private UIApplication Uiapp { get; set; } = null;
        private Application App { get; set; } = null;
        private UIDocument Uidoc { get; set; } = null;
        private Document Doc { get; set; } = null;

        public RevitModelForfard(UIApplication uiapp)
        {
            Uiapp = uiapp;
            App = uiapp.Application;
            Uidoc = uiapp.ActiveUIDocument;
            Doc = uiapp.ActiveUIDocument.Document;
        }

        #region Ось трассы
        public PolyCurve RoadAxis { get; set; }

        private string _roadAxisElemIds;
        public string RoadAxisElemIds
        {
            get => _roadAxisElemIds;
            set => _roadAxisElemIds = value;
        }

        public void GetPolyCurve()
        {
            var curves = RevitGeometryUtils.GetCurvesByDirectShapeAndModelLine(Uiapp, out _roadAxisElemIds);
            RoadAxis = new PolyCurve(curves);
        }
        #endregion

        #region Линия на поверхности 1
        public List<Line> RoadLines1 { get; set; }

        private string _roadLineElemIds1;
        public string RoadLineElemIds1
        {
            get => _roadLineElemIds1;
            set => _roadLineElemIds1 = value;
        }

        public void GetRoadLine1()
        {
            RoadLines1 = RevitGeometryUtils.GetRoadLines(Uiapp, out _roadLineElemIds1);
        }
        #endregion

        #region Линия на поверхности 2
        public List<Line> RoadLines2 { get; set; }

        private string _roadLineElemIds2;
        public string RoadLineElemIds2
        {
            get => _roadLineElemIds2;
            set => _roadLineElemIds2 = value;
        }

        public void GetRoadLine2()
        {
            RoadLines2 = RevitGeometryUtils.GetRoadLines(Uiapp, out _roadLineElemIds2);
        }
        #endregion

        #region Линии для размещения профилей
        public List<Line> ProfileLines { get; set; }

        private string _profileLineIds;
        public string ProfileLineIds
        {
            get => _profileLineIds;
            set => _profileLineIds = value;
        }

        public void GetProfileLines()
        {
            ProfileLines = RevitGeometryUtils.GetCurvesByLines(Uiapp, out _profileLineIds);
        }
        #endregion

        #region Список названий типоразмеров семейств
        public ObservableCollection<FamilySymbolSelector> GetFamilySymbolNames()
        {
            var familySymbolNames = new ObservableCollection<FamilySymbolSelector>();
            var allFamilies = new FilteredElementCollector(Doc).OfClass(typeof(Family)).OfType<Family>();
            var genericModelFamilies = allFamilies.Where(f => f.FamilyCategory.Id.IntegerValue == (int)BuiltInCategory.OST_GenericModel);
            if (genericModelFamilies.Count() == 0)
                return familySymbolNames;

            foreach (var family in genericModelFamilies)
            {
                foreach (var symbolId in family.GetFamilySymbolIds())
                {
                    var familySymbol = Doc.GetElement(symbolId);
                    familySymbolNames.Add(new FamilySymbolSelector(family.Name, familySymbol.Name));
                }
            }

            return familySymbolNames;
        }
        #endregion

        #region Получение количества точек ручек формы
        public int GetCountShapeHandlePoints(FamilySymbolSelector familyAndSymbolName)
        {
            Family family = GetFamilyByName(familyAndSymbolName);
            int countShapeHandlePoints = AdaptiveComponentFamilyUtils.GetNumberOfShapeHandlePoints(family);

            return countShapeHandlePoints;
        }
        #endregion

        #region Построение экземпляров семейства адаптивного сечения
        public void CreateAdaptivePointsFamilyInstanse(FamilySymbolSelector familyAndSymbolName,
                                                       int countShapeHandlePoints,
                                                       bool rotateFamilyInstanse,
                                                       bool isVertical,
                                                       bool isRotateByAngle,
                                                       double rotateAngle)
        {
            FamilySymbol fSymbol = GetFamilySymbolByName(familyAndSymbolName);

            var pointParameters = new List<double>();
            foreach(var line in ProfileLines)
            {
                double profileParameter;
                RoadAxis.Intersect(line, out _, out profileParameter);
                pointParameters.Add(profileParameter);
            }

            var creationDataList = new List<Autodesk.Revit.Creation.FamilyInstanceCreationData>();

            foreach (var parameter in pointParameters)
            {
                var familyInstancePoints = new List<XYZ>();

                Plane plane = RoadAxis.GetPlaneOnPolycurve(parameter);
                if (plane.XVec.Z == -1 || plane.XVec.Z == 1)
                {
                    plane = Plane.CreateByOriginAndBasis(plane.Origin, plane.YVec, plane.XVec);
                }
                Line lineOnRoad1 = RevitGeometryUtils.GetIntersectCurve(RoadLines1, plane);
                Line lineOnRoad2 = RevitGeometryUtils.GetIntersectCurve(RoadLines2, plane);

                XYZ v1 = lineOnRoad1.GetEndPoint(0) - lineOnRoad1.GetEndPoint(1);

                double lineParam1;
                double lineParam2;
                XYZ point1 = RevitGeometryUtils.LinePlaneIntersection(lineOnRoad1, plane, out lineParam1);
                XYZ point2 = RevitGeometryUtils.LinePlaneIntersection(lineOnRoad2, plane, out lineParam2);
                XYZ lineDirection = point1 - point2;
                Line projectionLine = Line.CreateUnbound(point1, lineDirection);
                Line verticalLine = Line.CreateUnbound(plane.Origin, XYZ.BasisZ);

                IntersectionResultArray interResult;
                var compResult = projectionLine.Intersect(verticalLine, out interResult);
                XYZ firstPoint = null;
                if (compResult == SetComparisonResult.Overlap)
                {
                    foreach (var elem in interResult)
                    {
                        if (elem is IntersectionResult result)
                        {
                            firstPoint = result.XYZPoint;
                            familyInstancePoints.Add(firstPoint);
                        }
                    }
                }

                double distanceBetweenPoints = UnitUtils.ConvertToInternalUnits(1, UnitTypeId.Meters);
                XYZ orthVector = plane.XVec.Normalize() * distanceBetweenPoints;

                XYZ upVector = orthVector.CrossProduct(v1).Normalize() * distanceBetweenPoints;
                bool isPlaneZNegative = upVector.Z < 0;

                if (isPlaneZNegative)
                {
                    upVector = upVector.Negate();
                    orthVector = orthVector.Negate();
                }

                if (isVertical)
                {
                    upVector = XYZ.BasisZ * distanceBetweenPoints;
                }

                if (rotateFamilyInstanse)
                {
                    orthVector = orthVector.Negate();
                }

                XYZ secondPoint;

                if(isRotateByAngle)
                {
                    var rotationAngle = UnitUtils.ConvertToInternalUnits(90 - rotateAngle, UnitTypeId.Degrees);
                    Transform transformRotation = Transform.CreateRotation(upVector, rotationAngle);
                    XYZ rotatedVectorPoint2 = transformRotation.OfPoint(orthVector);
                    secondPoint = firstPoint + rotatedVectorPoint2;
                }
                else
                {
                    secondPoint = firstPoint + orthVector;
                }

                familyInstancePoints.Add(secondPoint);

                XYZ thirdPoint = firstPoint + upVector;
                familyInstancePoints.Add(thirdPoint);

                // Добавляем нулевые точки вместо точек ручек формы
                if (countShapeHandlePoints != 0)
                {
                    for (int i = 0; i < countShapeHandlePoints; i++)
                    {
                        familyInstancePoints.Add(XYZ.Zero);
                    }
                }
                creationDataList.Add(new Autodesk.Revit.Creation.FamilyInstanceCreationData(fSymbol, familyInstancePoints));
            }

            using (Transaction trans = new Transaction(Doc, "Create Family Instances"))
            {
                trans.Start();
                if (!fSymbol.IsActive)
                {
                    fSymbol.Activate();
                }
                if (Doc.IsFamilyDocument)
                {
                    Doc.FamilyCreate.NewFamilyInstances2(creationDataList);
                }
                else
                {
                    Doc.Create.NewFamilyInstances2(creationDataList);
                }
                trans.Commit();
            }

        }

        #endregion

        #region Получение типоразмера по имени
        private FamilySymbol GetFamilySymbolByName(FamilySymbolSelector familyAndSymbolName)
        {
            var familyName = familyAndSymbolName.FamilyName;
            var symbolName = familyAndSymbolName.SymbolName;

            Family family = new FilteredElementCollector(Doc).OfClass(typeof(Family)).Where(f => f.Name == familyName).First() as Family;
            var symbolIds = family.GetFamilySymbolIds();
            foreach (var symbolId in symbolIds)
            {
                FamilySymbol fSymbol = (FamilySymbol)Doc.GetElement(symbolId);
                if (fSymbol.get_Parameter(BuiltInParameter.SYMBOL_NAME_PARAM).AsString() == symbolName)
                {
                    return fSymbol;
                }
            }
            return null;
        }
        #endregion

        #region Получение семейства по имени
        private Family GetFamilyByName(FamilySymbolSelector familyAndSymbolName)
        {
            var familyName = familyAndSymbolName.FamilyName;
            Family family = new FilteredElementCollector(Doc).OfClass(typeof(Family)).Where(f => f.Name == familyName).First() as Family;

            return family;
        }
        #endregion

    }
}
