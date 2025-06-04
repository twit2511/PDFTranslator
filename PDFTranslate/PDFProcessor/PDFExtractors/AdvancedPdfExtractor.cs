using System; 
using System.IO; 
using System.Collections.Generic; 
using System.Linq;
using PDFTranslate.PDFProcessor.PDFElements;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Crypto.Securityhandler;
using iText.Bouncycastle.Crypto;
using PDFTranslate.Translate;

namespace PDFTranslate.PDFProcessor.PDFExtractors
{
    /// <summary>
    /// 封装了PDF提取和分析的主要流程。
    /// </summary>
    public class AdvancedPdfProcessor
    {
        /// <summary>
        /// 处理指定的PDF文件，提取并分析其结构。
        /// </summary>
        /// <param name="sourcePdfPath">源PDF文件路径。</param>
        /// <param name="handleProcessedElements">处理完所有页面后，用于接收最终元素列表的回调函数。</param>
        public static List<IPDFElement> ProcessPdf(PdfDocument pdfDoc)
        {

            var allElements = new List<IPDFElement>();

            Console.WriteLine($"开始处理PDF文件");
            try
            {
                int numberOfPages = pdfDoc.GetNumberOfPages();
                Console.WriteLine($"PDF 已加载，共 {numberOfPages} 页。开始逐页提取和分析...");

                // 遍历所有页面
                for (int i = 1; i <= numberOfPages; i++)
                {
                    Console.WriteLine($"  处理第 {i} 页...");
                    PdfPage page = pdfDoc.GetPage(i);

                    // 使用监听器提取原始元素
                    var listener = new PdfElementExtractionListener(i);
                    var processor = new PdfCanvasProcessor(listener);
                    processor.ProcessPageContent(page); // 解析页面内容流
                    var rawPageElements = listener.GetExtractedElements();
                    Console.WriteLine($"    第 {i} 页：提取到 {rawPageElements.Count} 个原始元素。");

                    // 对当前页的元素进行结构分析（表格检测）
                    // Console.WriteLine($"    开始对第 {i} 页进行结构分析..."); // 调试信息
                    var processedPageElements = StructureAnalyzer.AnalyzePageStructure(rawPageElements);
                    allElements.AddRange(processedPageElements); // 将处理后的元素添加到总列表
                }
            }
            catch (iText.Kernel.Exceptions.PdfException pdfEx) // 捕获 iText 特定异常
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n处理 PDF 时发生 iText 错误: {pdfEx.Message}");
                if (pdfEx.Message.Contains("Bad user password")) Console.WriteLine("提示：文件可能已加密或密码错误。");
                Console.WriteLine(pdfEx.StackTrace);
                Console.ResetColor();
            }
            catch (Exception ex) // 捕获其他一般异常
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n处理 PDF 时发生未知错误: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                Console.ResetColor();
            }

            Console.WriteLine($"\n提取与分析完成。共处理 {allElements.Count} 个元素。");

            Console.WriteLine($"开始翻译...");

            ITranslator translator = new Translator();
            foreach (var element in allElements)
            {
                if (element is TextElement textElement)
                {
                    break;
                    if (textElement.NeedsTranslated)
                    {
                        textElement.TranslatedText =  translator.TranslateAsync(textElement.Text, "en", "zh");
                        Console.WriteLine($"    第 {textElement.PageNum} 页：翻译文本 \"{textElement.Text}\" 为 \"{textElement.TranslatedText}\"。");
                    }
                }
            }
            Console.WriteLine($"翻译完成。");

            return allElements;


        }
    }

}
