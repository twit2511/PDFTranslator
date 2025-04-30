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
    internal class ImageElement:IPDFElement
    {
        public int PageNum { get; set; }
        public PDFElementType ElementType => PDFElementType.Image;
        public Rectangle ApproximateBoundingBox { get; set; }
        public bool NeedsTranslated => false;
        public PdfImageXObject ImageObject { get; set; }
        public Matrix Matrix { get; set; }

        public ImageElement(int page, PdfImageXObject img, Matrix mat) 
        { 
            PageNum = page; 
            ImageObject = img; 
            Matrix = mat; 
            float width = img.GetWidth(); 
            float height = img.GetHeight(); 
            ApproximateBoundingBox = CalculateBoundingBox(mat, width, height); 
        }

        private static Rectangle CalculateBoundingBox(Matrix matrix, float width, float height) 
        { 
            Vector p1 = new Vector(0, 0, 1).Cross(matrix); 
            Vector p2 = new Vector(width, 0, 1).Cross(matrix); 
            Vector p3 = new Vector(0, height, 1).Cross(matrix); 
            Vector p4 = new Vector(width, height, 1).Cross(matrix); 
            float minX = Math.Min(Math.Min(p1.Get(0), p2.Get(0)), Math.Min(p3.Get(0), p4.Get(0))); 
            float minY = Math.Min(Math.Min(p1.Get(1), p2.Get(1)), Math.Min(p3.Get(1), p4.Get(1))); 
            float maxX = Math.Max(Math.Max(p1.Get(0), p2.Get(0)), Math.Max(p3.Get(0), p4.Get(0))); 
            float maxY = Math.Max(Math.Max(p1.Get(1), p2.Get(1)), Math.Max(p3.Get(1), p4.Get(1))); 
            return new Rectangle(minX, minY, maxX - minX, maxY - minY); 
        }
    }
}
