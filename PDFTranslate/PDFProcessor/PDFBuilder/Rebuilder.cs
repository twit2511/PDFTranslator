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
using System.Windows;
using iText.Layout.Renderer;
using System.Windows.Media;

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


        /// <summary>
        /// 实现PDF的重建
        /// </summary>
        /// <param name="elements"> pdf的所有相关元素 </param>
        /// <param name="outputPath">文件存放路径</param>
        /// <param name="sourcePdfPath">源pdf文件路径</param>
        public static void RebuildPdf(List<IPDFElement> elements, string outputPath, string sourcePdfPath)
        {
            if (elements == null || !elements.Any())
            {
                Console.WriteLine("没有元素可用于重建 PDF。");
                return;
            }
            if (!File.Exists(sourcePdfPath))
            {
                Console.WriteLine($"错误：源 PDF 文件未找到 {sourcePdfPath}");
                return;
            }

            PdfWriter pdfWriter = null;
            PdfDocument pdfDoc = null;
            PdfDocument sourceDoc = null;

            Console.WriteLine($"开始重建 PDF 到: {outputPath}");
            try
            {
                pdfWriter = new PdfWriter(outputPath);
                pdfDoc = new PdfDocument(pdfWriter);
                sourceDoc = new PdfDocument(new PdfReader(sourcePdfPath));

                var groupedElementsByPage = elements.GroupBy( e => e.PageNum).OrderBy(g => g.Key);

                foreach (var pageGroup in groupedElementsByPage)
                {
                    int pageNum = pageGroup.Key;
                    Console.WriteLine($"  重建第 {pageNum} 页...");
                    var originalPage = sourceDoc.GetPage(pageNum);
                    if (originalPage == null) {
                        Console.WriteLine($"    警告: 无法从源PDF获取第 {pageNum} 页。跳过此页。");
                        continue;
                    }

                    var RectanglePageSize = originalPage.GetPageSizeWithRotation();
                    PageSize pageSize = new PageSize(RectanglePageSize);
                    var newPage = pdfDoc.AddNewPage(pageSize);
                    var canvas = new PdfCanvas(newPage);

                    foreach (var element in pageGroup)
                    {
                        // 保存状态对于隔离元素绘制非常重要
                        canvas.SaveState();
                        try
                        {
                            switch (element.ElementType) {
                                case PDFElementType.Text:
                                case PDFElementType.Formula:
                                case PDFElementType.TableCell:
                                    DrawTextElement(canvas, (TextElement)element);
                                    break;
                                case PDFElementType.Image:
                                    DrawImageElement(canvas, (ImageElement)element);
                                    break;
                                case PDFElementType.PathLine:       
                                    DrawLineElement(canvas,(LineElement)element); 
                                    break;
                                default:
                                    Console.WriteLine($"    信息: 跳过未知元素类型 {element.ElementType}");
                                    break;
                            }
                        }
                        catch(Exception ex)
                        {
                            Console.WriteLine($"    错误: 绘制元素 (页 {element.PageNum}, 类型 {element.ElementType}) 时发生内部错误: {ex.Message}");
                        }
                        finally
                        {
                            canvas.RestoreState();
                        }
                    }
                }
                Console.WriteLine("PDF 重建完成。");
            }
            catch( Exception ex ) 
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nPDF 重建时发生严重错误: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Console.ResetColor();
            }
            finally
            {
                pdfDoc?.Close(); // writer 会被 pdfDoc 关闭
                sourceDoc?.Close();
            }
        }

        private static void DrawTextElement(PdfCanvas canvas, TextElement text)
        {
            //无论是否翻译都继续进行绘制
            string textString = string.IsNullOrEmpty(text.TranslatedText)? text.Text : text.TranslatedText;
            if (string.IsNullOrEmpty(textString)) return;

            PdfFont font = GetFont(text,textString);

            if (font == null)
            {
                Console.WriteLine($"    严重警告: 无法为文本找到任何可用字体 (页 {text.PageNum}, '{textString.Substring(0, Math.Min(10, textString.Length))}...')。文本将不会被绘制。");
                return;
            }

            canvas.BeginText()
                .SetFontAndSize(font, text.FontSize)
                .SetFillColor(text.FontColor ?? ColorConstants.BLACK)
                .SetCharacterSpacing(text.CharacterSpacing)
                .SetWordSpacing(text.WordSpacing)
                .SetHorizontalScaling(text.HorizontalScaling)
                .MoveText(text.StartPoint.Get(iText.Kernel.Geom.Vector.I1), text.StartPoint.Get(iText.Kernel.Geom.Vector.I2))
                .ShowText(textString)
                .EndText();

        }

        private static void DrawImageElement(PdfCanvas canvas, ImageElement image) { 
            if (image.ImageObject != null && image.Matrix != null)
            {
                canvas.AddXObjectWithTransformationMatrix(
                image.ImageObject,
                    image.Matrix.Get(iText.Kernel.Geom.Matrix.I11), // a
                    image.Matrix.Get(iText.Kernel.Geom.Matrix.I12), // b
                    image.Matrix.Get(iText.Kernel.Geom.Matrix.I21), // c
                    image.Matrix.Get(iText.Kernel.Geom.Matrix.I22), // d
                    image.Matrix.Get(iText.Kernel.Geom.Matrix.I31), // e
                    image.Matrix.Get(iText.Kernel.Geom.Matrix.I32));// f

            }
            else{
                Console.WriteLine($"    警告: 图像元素数据不完整，无法绘制 (页 {image.PageNum})");
            }
        }

        private static void DrawLineElement(PdfCanvas canvas, LineElement line) {

            canvas.SetStrokeColor(line.StrokeColor ?? ColorConstants.BLACK)
                .SetLineWidth(line.LineWidth > 0 ? line.LineWidth : 0.5f)
                .MoveTo(line.StartPoint.Get(iText.Kernel.Geom.Vector.I1), line.StartPoint.Get(iText.Kernel.Geom.Vector.I2))
                .LineTo(line.EndPoint.Get(iText.Kernel.Geom.Vector.I1), line.EndPoint.Get(iText.Kernel.Geom.Vector.I2))
                .Stroke();
        }
    }
}
