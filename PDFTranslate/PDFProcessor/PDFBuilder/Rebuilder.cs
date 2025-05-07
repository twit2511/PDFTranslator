using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using iText.Kernel.Colors;
using iText.Kernel.Font;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas;
using iText.IO.Font;
using iText.IO.Font.Constants;

using PDFTranslate.PDFProcessor.PDFElements;

namespace PDFTranslate.PDFProcessor.PDFBuilder
{
    public static class Rebulider
    {
        

        /// <summary>
        /// 获取用于绘制文本的字体。
        /// 优先使用 TextElement.OriginalPdfFont，其次尝试从自定义字体目录加载，最后使用全局备用字体。
        /// </summary>
        private static PdfFont GetFont(TextElement textElement, string textToDraw)
        {
            // 1. 优先使用 TextElement 中直接携带的原始 PdfFont 对象 (如果存在且有效)
            if (textElement.OriginalFont != null && CanFontDisplay(textElement.OriginalFont, textToDraw))
            {
                return textElement.OriginalFont;
            }
            else
            {
                Console.WriteLine("字体获取出错");
                return null;
            }
        }

        private static bool CanFontDisplay(PdfFont font, string text)
        {
            if (font == null) return false;
            if (string.IsNullOrEmpty(text)) return true;
            try
            {
                foreach (char c in text)
                {
                    if (!font.ContainsGlyph(c))
                    {
                        return false;
                    }
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        


    }
}
