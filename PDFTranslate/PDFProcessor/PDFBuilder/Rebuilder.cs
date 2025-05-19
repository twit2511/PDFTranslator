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
using iText.Kernel.Pdf.Xobject;

namespace PDFTranslate.PDFProcessor.PDFBuilder
{
    public static class Rebuilder
    {
        private static Dictionary<string, PdfFont> _fontCache = new Dictionary<string, PdfFont>();
        private static PdfFont _globalFallbackFont; // 使用我们下载的字体作为主要的备用字体
        private const string CustomFontsDirectory = "Fonts"; // 假设字体放在项目输出目录下的 "Fonts" 文件夹

        static Rebuilder()
        {
            try
            {
                
                // ---- 使用下载的字体作为全局备用字体 ----
                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                // 例如，你下载了一个名为 "NotoSansCJKsc-Regular.otf" 的字体来支持中文
                string fallbackFontFileName = "Roboto_Condensed - Black.ttf"; // 修改为你下载的字体文件名
                // 或者，如果你用的是 Arial Unicode MS
                // string fallbackFontFileName = "arialuni.ttf";

                string fallbackFontPath = System.IO.Path.Combine(baseDirectory, CustomFontsDirectory, fallbackFontFileName);

                if (File.Exists(fallbackFontPath))
                {
                    _globalFallbackFont = PdfFontFactory.CreateFont(fallbackFontPath, PdfEncodings.IDENTITY_H, PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED);
                    Console.WriteLine($"全局备用字体 '{fallbackFontFileName}' 加载成功。");
                }
                else
                {
                    Console.WriteLine($"警告: 未找到全局备用字体文件 '{fallbackFontPath}'。将尝试使用 Helvetica。");
                    LoadHelveticaAsFallback();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"警告: 加载全局备用字体时出错: {ex.Message}。将尝试使用 Helvetica。");
                LoadHelveticaAsFallback();
            }
        }

        private static void LoadHelveticaAsFallback()
        {
            try
            {
                _globalFallbackFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA, PdfEncodings.WINANSI, PdfFontFactory.EmbeddingStrategy.PREFER_NOT_EMBEDDED);
                Console.WriteLine("已加载 Helvetica 作为最终备用字体。");
            }
            catch (Exception helvEx)
            {
                Console.WriteLine($"严重错误: 无法加载 Helvetica作为备用字体: {helvEx.Message}");
                // _globalFallbackFont 将为 null，GetFont 方法需要处理这种情况
            }
        }

        /// <summary>
        /// 获取用于绘制文本的字体。
        /// 优先使用 TextElement.OriginalPdfFont，其次尝试从自定义字体目录加载，最后使用全局备用字体。
        /// </summary>
        private static PdfFont GetFont(string textToDraw)
        {
            

            if (_globalFallbackFont != null && CanFontDisplay(_globalFallbackFont, textToDraw))
            {
                return _globalFallbackFont;
            }

           
            if (_fontCache.TryGetValue("HELVETICA_FINAL", out var helvetica))
            {
                if (CanFontDisplay(helvetica, textToDraw)) return helvetica;
            }
            try
            {
                var finalHelvetica = PdfFontFactory.CreateFont(StandardFonts.HELVETICA, PdfEncodings.WINANSI, PdfFontFactory.EmbeddingStrategy.PREFER_NOT_EMBEDDED);
                _fontCache["HELVETICA_FINAL"] = finalHelvetica;
                // 对于Helvetica，CanFontDisplay 对于非WinAnsi字符会返回false，这是正常的
                return finalHelvetica;
            }
            catch (IOException ex)
            {
                throw new Exception("无法加载任何可用字体 (包括Helvetica) 来显示文本。", ex);
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
        /// <param name="originalDocumentForElements">源pdf文件</param>
        public static void RebuildPdf(List<IPDFElement> elements, string outputPath, PdfDocument originalDocumentForElements)
        {
            if (elements == null || !elements.Any())
            {
                Console.WriteLine("没有元素可用于重建 PDF。");
                return;
            }
            if (originalDocumentForElements == null || originalDocumentForElements.IsClosed())
            {
                Console.WriteLine($"错误：提供的原始文档为 null 或已关闭。");
                return;
            }

            PdfWriter pdfWriter = null;
            PdfDocument pdfDoc = null;
            

            Console.WriteLine($"开始重建 PDF 到: {outputPath}");
            try
            {
                pdfWriter = new PdfWriter(outputPath);
                pdfDoc = new PdfDocument(pdfWriter);

                var groupedElementsByPage = elements.GroupBy( e => e.PageNum).OrderBy(g => g.Key);

                foreach (var pageGroup in groupedElementsByPage)
                {
                    int pageNum = pageGroup.Key;
                    Console.WriteLine($"  重建第 {pageNum} 页...");

                    PdfPage originalPage = null;
                    if (pageNum <= originalDocumentForElements.GetNumberOfPages())
                    {
                        originalPage = originalDocumentForElements.GetPage(pageNum);
                    }

                    if (originalPage == null)
                    {
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
                pdfDoc?.Close();
            }
        }

        private static void DrawTextElement(PdfCanvas canvas, TextElement text)
        {
            //无论是否翻译都继续进行绘制
            string textString = string.IsNullOrEmpty(text.TranslatedText)? text.Text : text.TranslatedText;
            if (string.IsNullOrEmpty(textString)) return;

            PdfFont font = GetFont(textString);

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
                
                PdfImageXObject copiedImage = image.ImageObject.CopyTo(canvas.GetDocument());
                canvas.AddXObjectWithTransformationMatrix(
                copiedImage,
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
