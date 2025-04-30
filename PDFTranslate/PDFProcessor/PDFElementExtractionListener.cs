using iText.Kernel.Colors;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Xobject;
using PDFTranslate.PDFProcessor.PDFElements;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using iText.Kernel.Geom;

namespace PDFTranslate.PDFProcessor
{
    /// <summary>
    /// 实现 iText 7 事件监听器，用于提取页面元素。
    /// </summary>
    public class PdfElementExtractionListener : IEventListener
    {
        private readonly int _pageNumber;
        private readonly List<IPDFElement> _elements = new List<IPDFElement>();

        // 定义已知数学字体名称的子串 (用于启发式判断公式)
        private static readonly HashSet<string> MathFontNameSubstrings = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Math", "Symbol", "MT Extra", "Euclid", "Mathematical", "CambriaMath", "CMSY", "CMEX", "AMS"
        // 可根据需要扩展此列表
    };

        // 检查文本是否包含常用 Unicode 数学符号 (用于启发式判断公式)
        private static bool ContainsMathChars(string text)
        {
            // 简单的检查，可以根据需要扩展范围或提高复杂度
            return text.Any(c =>
                (c >= '\u2200' && c <= '\u22FF') || // Mathematical Operators
                (c >= '\u2190' && c <= '\u21FF') || // Arrows
                (c >= '\u0370' && c <= '\u03FF') || // Greek and Coptic
                (c >= '\u2A00' && c <= '\u2AFF') || // Supplemental Mathematical Operators
                (c >= '\u27C0' && c <= '\u27EF')    // Miscellaneous Mathematical Symbols-A
            );
        }

        // 路径处理常量
        private const float MIN_LINE_LENGTH = 5.0f; // 忽略过短的线条
        private const float LINE_DETECTION_TOLERANCE = 1.5f; // 水平/垂直判断容差

        public PdfElementExtractionListener(int pageNumber)
        {
            _pageNumber = pageNumber;
        }

        /// <summary>
        /// 处理 iText 解析器触发的事件。
        /// </summary>
        public void EventOccurred(IEventData data, EventType type)
        {
            try // 添加整体异常捕获，防止单个元素解析失败导致整个页面失败
            {
                switch (type)
                {
                    case EventType.RENDER_TEXT:
                        ExtractTextElement((TextRenderInfo)data);
                        break;
                    case EventType.RENDER_IMAGE:
                        ExtractImageElement((ImageRenderInfo)data);
                        break;
                    case EventType.RENDER_PATH:
                        ExtractPathElement((PathRenderInfo)data);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"!! 错误: 在处理第 {_pageNumber} 页的 {type} 事件时发生异常: {ex.Message}");
                // 可以选择记录更详细的日志或采取其他错误处理措施
            }
        }

        /// <summary>
        /// 提取文本元素信息。
        /// </summary>
        private void ExtractTextElement(TextRenderInfo renderInfo)
        {
            string text = renderInfo.GetText();
            if (string.IsNullOrWhiteSpace(text)) return; // 忽略空白文本块

            var font = renderInfo.GetFont();
            string fontName = font?.GetFontProgram()?.GetFontNames()?.GetFontName() ?? "Unknown";
            var fillColor = renderInfo.GetFillColor() ?? ColorConstants.BLACK; // 默认黑色

            // 计算位置和边界框
            var startPoint = renderInfo.GetBaseline().GetStartPoint();
            var endPoint = renderInfo.GetBaseline().GetEndPoint();
            var ascentLine = renderInfo.GetAscentLine();
            var descentLine = renderInfo.GetDescentLine();
            float height = ascentLine.GetStartPoint().Get(Vector.I2) - descentLine.GetStartPoint().Get(Vector.I2);
            if (height <= 0 && renderInfo.GetFontSize() > 0) 
                height = renderInfo.GetFontSize() * 1.2f; // 估算高度
            float width = endPoint.Get(Vector.I1) - startPoint.Get(Vector.I1);
            if (Math.Abs(width) < 0.01f && text.Length > 0) 
                width = renderInfo.GetFontSize() * text.Length * 0.6f; // 估算宽度
            Rectangle bbox = new Rectangle(startPoint.Get(Vector.I1), descentLine.GetStartPoint().Get(Vector.I2), width, height);

            // 初步类型判断 (公式 vs 普通)
            PDFElementType elementType = PDFElementType.Text;
            bool isMathFont = fontName != "Unknown" && MathFontNameSubstrings.Any(s => fontName.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
            bool hasMathChars = ContainsMathChars(text);
            // 规则: 数学字体 或 (短文本 且 含数学符号) -> 认为是公式
            if (isMathFont || (hasMathChars && text.Length < 20))
            {
                elementType = PDFElementType.Formula;
            }

            // 创建 TextElement 对象
            var textElement = new TextElement
            {
                PageNum = _pageNumber,
                Text = text,
                ApproximateBoundingBox = bbox,
                StartPoint = startPoint,
                EndPoint = endPoint,
                FontName = fontName,
                FontSize = renderInfo.GetFontSize(),
                FontColor = fillColor,
                CharacterSpacing = renderInfo.GetCharSpacing(),
                WordSpacing = renderInfo.GetWordSpacing(),
                HorizontalScaling = renderInfo.GetHorizontalScaling() * 100, // 转为百分比
                OriginalFont = font,
                ElementType = elementType // 设置初步判断的类型
            };
            _elements.Add(textElement);
        }

        /// <summary>
        /// 提取图像元素信息。
        /// </summary>
        private void ExtractImageElement(ImageRenderInfo renderInfo)
        {
            PdfImageXObject imageObject = renderInfo.GetImage();
            if (imageObject == null)
            {
                // Console.WriteLine($"警告: 在第 {_pageNumber} 页检测到图像事件，但无法获取图像对象。");
                return;
            }
            Matrix matrix = renderInfo.GetImageCtm(); // 获取变换矩阵，保证位置正确
            _elements.Add(new ImageElement(_pageNumber, imageObject, matrix));
        }

        /// <summary>
        /// 提取路径元素信息，并筛选出直线用于表格检测。
        /// </summary>
        private void ExtractPathElement(PathRenderInfo renderInfo)
        {
            // 只关心描边操作 (Stroke)，因为表格线通常是画出来的
            if (renderInfo.GetOperation() != PathRenderInfo.STROKE) return;

            Path path = renderInfo.GetPath();
            if (path == null) return;

            Matrix ctm = renderInfo.GetCtm(); // 获取当前变换矩阵

            // 遍历路径的所有子路径
            foreach (var subpath in path.GetSubpaths())
            {
                // 获取子路径的所有段 (Segment)
                var segments = subpath.GetSegments();

                // --- 重点处理由单条直线构成的子路径 ---
                // 这是表格线最常见的情况
                if (segments != null && segments.Count == 1 && segments[0] is Line lineSegment)
                {
                    // 从 Line Segment 获取其基点 (base points)
                    // 注意: GetStartPoint() 和 GetEndPoint() 返回的是 Point 对象
                    Point start = lineSegment.GetBasePoints()[0];
                    Point end = lineSegment.GetBasePoints()[1];

                    // 将路径点转换为 Vector 并应用 CTM 得到页面坐标
                    Vector finalStart = new Vector((float)start.GetX(), (float)start.GetY(), 1).Cross(ctm);
                    Vector finalEnd = new Vector((float)end.GetX(), (float)end.GetY(), 1).Cross(ctm);

                    var lineElement = new LineElement(
                        _pageNumber, finalStart, finalEnd,
                        renderInfo.GetLineWidth(),
                        renderInfo.GetStrokeColor()
                    );

                    // 过滤：只保留足够长且近似水平或垂直的线
                    if (lineElement.Length() >= MIN_LINE_LENGTH &&
                        (lineElement.IsHorizontal(LINE_DETECTION_TOLERANCE) || lineElement.IsVertical(LINE_DETECTION_TOLERANCE)))
                    {
                        _elements.Add(lineElement);
                    }
                }
                // else: 可以添加逻辑处理更复杂的子路径，比如矩形框等
            }
        }

        /// <summary>
        /// 声明此监听器关心的事件类型。
        /// </summary>
        public ICollection<EventType> GetSupportedEvents()
        {
            // 需要监听文本、图像和路径事件
            return new List<EventType> { EventType.RENDER_TEXT, EventType.RENDER_IMAGE, EventType.RENDER_PATH };
        }

        /// <summary>
        /// 获取此监听器收集到的所有元素。
        /// </summary>
        public List<IPDFElement> GetExtractedElements()
        {
            return _elements;
        }
    }
}
