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
    public enum PDFElementType
    {
        Unknown,
        Text,
        Formula,
        TableCell,
        Image,
        PathLine
    }

    public interface IPDFElement
    {
        int PageNum {  get; }
        PDFElementType ElementType { get; }
        Rectangle ApproximateBoundingBox { get; }
        bool NeedsTranslated { get; }
    }
}
