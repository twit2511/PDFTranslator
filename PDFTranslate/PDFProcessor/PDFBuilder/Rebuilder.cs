using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using iText.Layout.Element;
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
using iText.IO.Image;
using System.Reflection;

namespace PDFTranslate.PDFProcessor.PDFBuilder
{
    public static class Rebuilder
    {
        static PdfFont _cjkFont; // 用于中文文本 (如 STSong-Light)
        private static PdfFont _defaultLatinFont; // 用于西文文本 (如 Helvetica)
        static Rebuilder()
        {
            _cjkFont = LoadEmbeddedFont("PDFTranslate.Fonts.NotoSansSC-6.ttf");

            if (_cjkFont == null)
            {
                try
                {
                    _cjkFont = PdfFontFactory.CreateFont("STSong-Light",
                        PdfEncodings.IDENTITY_H,
                        PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED);
                    Console.WriteLine("成功加载 iText 内置 CJK 字体 'STSong-Light' (依赖 itext7.font-asian)。");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"警告: 无法加载 iText 内置 CJK 字体 'STSong-Light'。中文可能无法正确显示。");
                    Console.WriteLine($"这通常意味着 'itext7.font-asian' NuGet 包未被引用，或者字体资源不可用。错误: {ex.Message}");
                    _cjkFont = null;
                }
            }

            // ---- 2. 加载 iText 标准的西文字体 (如 Helvetica) ----
            // PREFER_NOT_EMBEDDED 因为标准14字体通常不需要嵌入。
            try
            {
                _defaultLatinFont = PdfFontFactory.CreateFont(StandardFonts.HELVETICA, PdfEncodings.WINANSI, PdfFontFactory.EmbeddingStrategy.PREFER_NOT_EMBEDDED);
                Console.WriteLine("成功加载 iText 标准西文字体 'Helvetica'。");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"严重错误: 无法加载 iText 标准西文字体 'Helvetica'。错误: {ex.Message}");
                _defaultLatinFont = null;
            }
        }

        private static PdfFont LoadEmbeddedFont(string resourceName)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly(); // 或者 Assembly.GetCallingAssembly()，或 typeof(Rebuilder).Assembly
                // 资源名称通常是: <默认命名空间>.<文件夹名（如果存在）>.<文件名>
                // 例如，如果你的项目默认命名空间是 "PDFTranslate"，字体在 "Fonts" 文件夹下，文件名为 "NotoSansSC-Regular.otf"，
                // 那么资源名称就是 "PDFTranslate.Fonts.NotoSansSC-Regular.otf"

                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        Console.WriteLine($"警告: 无法找到嵌入的资源 '{resourceName}'。");
                        return null;
                    }

                    byte[] fontBytes = new byte[stream.Length];
                    stream.Read(fontBytes, 0, fontBytes.Length);

                    PdfFont font = PdfFontFactory.CreateFont(fontBytes,
                        PdfEncodings.IDENTITY_H,
                        PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED, // 嵌入字体到PDF中
                        true); // true for embedded fonts, important for some font types
                    Console.WriteLine($"成功加载嵌入的 CJK 字体 '{resourceName}'。");
                    return font;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"警告: 加载嵌入的 CJK 字体 '{resourceName}' 失败。错误: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 判断文本是否可能包含中日韩字符。
        /// 这是一个基础的启发式检查。
        /// </summary>
        private static bool ContainsCjkCharacters(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (char c in text)
            {
                // CJK Unicode 块通常从 U+2E80 开始 (包括部首、符号、主要表意文字区等)
                // 这是一个比较宽泛的检查，可以根据需要调整。
                if (c >= 0x2E80 && c <= 0x9FFF) // 常用CJK统一表意文字范围 U+4E00-U+9FFF
                {
                    return true;
                }
                // 可以添加更多CJK相关范围的检查，如扩展区等
            }
            return false;
        }
        private static PdfFont GetFont(string textToDraw)
        {
            bool isCjkText = ContainsCjkCharacters(textToDraw);

            // 1. 如果是CJK文本且CJK字体已加载
            if (isCjkText && _cjkFont != null)
            {
                if (CanFontDisplay(_cjkFont, textToDraw))
                {
                    return _cjkFont;
                }
                // 即使 STSong-Light 不能完全显示所有字符，对于中文文本，它仍是首选
                // Console.WriteLine($"警告: STSong-Light 可能无法显示文本中的所有字符: '{textToDraw.Substring(0, Math.Min(10,textToDraw.Length))}'");
                return _cjkFont;
            }

            // 2. 如果不是CJK文本，或者CJK字体加载失败，尝试使用默认西文字体
            if (_defaultLatinFont != null)
            {
                if (CanFontDisplay(_defaultLatinFont, textToDraw))
                {
                    return _defaultLatinFont;
                }
               
                return _defaultLatinFont;
            }

            
            if (_cjkFont != null)
            {
                // Console.WriteLine("警告: 西文字体加载失败，回退到CJK字体（如果存在）。");
                return _cjkFont;
            }

            // 4. 极端情况：所有尝试加载的字体都为null
            Console.WriteLine($"严重警告: 无法为文本找到任何 iText 提供的可用字体 (页码未知，文本片段: '{textToDraw.Substring(0, Math.Min(10, textToDraw.Length))}...')。文本将不会被正确绘制。");
            return null;
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

            // ---- BEGIN DEBUG LOGGING ----
            string fontNameToLog = "NULL FONT";
            if (font != null && font.GetFontProgram() != null && font.GetFontProgram().GetFontNames() != null)
            {
                fontNameToLog = font.GetFontProgram().GetFontNames().GetFontName();
            }
            else if (font != null)
            {
                fontNameToLog = "Font object exists, but name unavailable";
            }

            Console.WriteLine($"DEBUG: Page {text.PageNum}, Text: '{textString.Substring(0, Math.Min(20, textString.Length))}', " +
                              $"Font Chosen: '{fontNameToLog}', " +
                              $"FontSize: {text.FontSize}, " +
                              $"FontColor: {text.FontColor},"+
                              $"CharSpacing: {text.CharacterSpacing}, " +
                              $"WordSpacing: {text.WordSpacing}, " +
                              $"HorizScaling: {text.HorizontalScaling}");
            // ---- END DEBUG LOGGING ----

            
            

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
        private static void DrawImageElement(PdfCanvas canvas, ImageElement imageElement)
        {
            if (imageElement.ImageObject == null || imageElement.Matrix == null)
            {
                Console.WriteLine($"    警告: 图像元素数据不完整，无法绘制 (页 {imageElement.PageNum})");
                return;
            }

            PdfImageXObject originalImageXObject = imageElement.ImageObject;
            iText.Kernel.Geom.Matrix matrix = imageElement.Matrix;
            PdfDocument newDocument = canvas.GetDocument();

            try
            {
                

                byte[] imageBytes = null;
                PdfStream imageStream = originalImageXObject.GetPdfObject(); // PdfImageXObject 本身就是一个 PdfStream

                if (imageStream != null)
                {
                    // 尝试获取原始（可能已压缩）的字节
                    // 注意：对于某些复杂的图像，这可能不是直接的图像文件格式字节
                    imageBytes = imageStream.GetBytes(false); // false 表示不解码，获取原始流字节
                }

                if (imageBytes == null || imageBytes.Length == 0)
                {
                    Console.WriteLine($"    警告: 无法从 PdfImageXObject (页 {imageElement.PageNum}) 获取原始图像字节。尝试使用 CopyTo。");
                    // 回退到 CopyTo 方案
                    UseCopyToForImage(canvas, originalImageXObject, matrix);
                    return;
                }

                ImageData imageData = null;
                try
                {
                    imageData = ImageDataFactory.Create(imageBytes);
                }
                catch (Exception exFactory)
                {
                    Console.WriteLine($"    警告: 使用 ImageDataFactory 从提取的字节创建图像失败 (页 {imageElement.PageNum}): {exFactory.Message}。尝试使用 CopyTo。");
                    // 如果 ImageDataFactory 失败 (例如，字节不是它能识别的格式)，回退到 CopyTo
                    UseCopyToForImage(canvas, originalImageXObject, matrix);
                    return;
                }

                // 创建一个 iText.Layout.Element.Image 对象
                // 这个 Image 对象将负责将其 XObject 正确地添加到新文档中
                Image layoutImage = new Image(imageData);

                // 获取属于新文档的 PdfXObject
                // 当我们通过 iText.Layout.Element.Image 创建 XObject 时，它会自动属于正确的文档
                PdfImageXObject newPdfImageXObject = new PdfImageXObject(imageData);

                if (newPdfImageXObject != null)
                {
                    Console.WriteLine($"    信息: 通过 ImageDataFactory 重新创建图像成功 (页 {imageElement.PageNum})。");
                    canvas.AddXObjectWithTransformationMatrix(
                        newPdfImageXObject,
                        matrix.Get(iText.Kernel.Geom.Matrix.I11), // a
                        matrix.Get(iText.Kernel.Geom.Matrix.I12), // b
                        matrix.Get(iText.Kernel.Geom.Matrix.I21), // c
                        matrix.Get(iText.Kernel.Geom.Matrix.I22), // d
                        matrix.Get(iText.Kernel.Geom.Matrix.I31), // e
                        matrix.Get(iText.Kernel.Geom.Matrix.I32)  // f
                    );
                }
                else
                {
                    Console.WriteLine($"    警告: 从 ImageData 创建的 Layout.Image 未能生成 PdfXObject (页 {imageElement.PageNum})。尝试使用 CopyTo。");
                    UseCopyToForImage(canvas, originalImageXObject, matrix);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    错误: 从原始数据重新创建图像时发生意外错误 (页 {imageElement.PageNum}): {ex.Message}。尝试使用 CopyTo 作为最终回退。");
                try
                {
                    UseCopyToForImage(canvas, originalImageXObject, matrix);
                }
                catch (Exception exCopyToFallback)
                {
                    Console.WriteLine($"    严重错误: 最终回退到 CopyTo 方案也失败了 (页 {imageElement.PageNum}): {exCopyToFallback.Message}");
                }
            }
        }

        // 辅助方法，用于原始的 CopyTo 逻辑
        private static void UseCopyToForImage(PdfCanvas canvas, PdfImageXObject imageXObject, iText.Kernel.Geom.Matrix matrix)
        {
            Console.WriteLine($"    信息: 正在尝试使用 CopyTo 绘制图像 (页 {imageXObject.GetPdfObject().GetIndirectReference()?.GetDocument().GetPageNumber(imageXObject.GetPdfObject())})"); // 尝试获取原始页码
            PdfImageXObject copiedImage = imageXObject.CopyTo(canvas.GetDocument());
            canvas.AddXObjectWithTransformationMatrix(
                copiedImage,
                matrix.Get(iText.Kernel.Geom.Matrix.I11),
                matrix.Get(iText.Kernel.Geom.Matrix.I12),
                matrix.Get(iText.Kernel.Geom.Matrix.I21),
                matrix.Get(iText.Kernel.Geom.Matrix.I22),
                matrix.Get(iText.Kernel.Geom.Matrix.I31),
                matrix.Get(iText.Kernel.Geom.Matrix.I32)
            );
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
