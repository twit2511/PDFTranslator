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
    internal class TextElement:IPDFElement
    {
        public int PageNum { get; set; }
        public PDFElementType ElementType { get; set; }
        public Rectangle ApproximateBoundingBox { get; set; }
        public bool NeedsTranslated { get; set; }
        public string Text {  get; set; }
        public Vector StartPoint { get; set; }
        public Vector EndPoint { get; set; }
        public string FontName { get; set; }
        public float FontSize { get; set; }
        public Color FontColor { get; set; }
        public float CharacterSpacing { get; set; }
        public float WordSpacing { get; set; }
        public float HorizontalScaling { get; set; }
        public PdfFont OriginalFont { get; set; }
        public string TranslatedText { get; set; }
    }
}
