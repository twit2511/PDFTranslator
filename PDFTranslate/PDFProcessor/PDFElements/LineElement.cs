using System;
using System.Collections.Generic;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Geom;
using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Pdf.Xobject;
using System.Text.RegularExpressions;

namespace PDFTranslate.PDFProcessor.PDFElements
{
    internal class LineElement : IPDFElement
    {
        public int PageNum { get; set; }
        public PDFElementType ElementType => PDFElementType.PathLine;
        public Rectangle ApproximateBoundingBox { get; set; }
        public bool NeedsTranslated => false;
        public Vector StartPoint { get; set; }
        public Vector EndPoint { get; set; }
        public float LineWidth { get; set; }
        public Color StrokeColor { get; set; }
        public bool IsText => false; 
        public bool IsImage => false; 
        public bool IsPath => true; 
        public bool NeedsTranslation => false;
        public LineElement(int page, Vector start, Vector end, float width, Color color) 
        { 
            PageNum = page; 
            StartPoint = start; 
            EndPoint = end; 
            LineWidth = width; 
            StrokeColor = color ?? ColorConstants.BLACK; 
            float minX = Math.Min(start.Get(Vector.I1), end.Get(Vector.I1)); 
            float minY = Math.Min(start.Get(Vector.I2), end.Get(Vector.I2)); 
            float maxX = Math.Max(start.Get(Vector.I1), end.Get(Vector.I1)); 
            float maxY = Math.Max(start.Get(Vector.I2), end.Get(Vector.I2)); 
            ApproximateBoundingBox = new Rectangle(minX, minY, maxX - minX, maxY - minY); 
        }
        public bool IsHorizontal(float tolerance = 1.0f) 
        { 
            return Math.Abs(StartPoint.Get(Vector.I2) - EndPoint.Get(Vector.I2)) < tolerance; 
        }
        public bool IsVertical(float tolerance = 1.0f) 
        { 
            return Math.Abs(StartPoint.Get(Vector.I1) - EndPoint.Get(Vector.I1)) < tolerance; 
        }
        public float Length() 
        { 
            return StartPoint.Subtract(EndPoint).Length(); 
        }
    }
}
