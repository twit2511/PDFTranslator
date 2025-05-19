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

namespace PDFTranslate.PDFProcessor.PDFExtractors
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
                HorizontalScaling = renderInfo.GetHorizontalScaling(),
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
                Console.WriteLine($"警告: 在第 {_pageNumber} 页检测到图像事件，但无法获取图像对象。");
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

        public List<IPDFElement> GetAndMergeExtractedElements()
        {
            // _elements 包含了原始提取的所有 IPdfElement
            List<IPDFElement> mergedElements = new List<IPDFElement>();
            List<TextElement> currentLineTextChunks = new List<TextElement>();

            // 先按页码，再按 Y 坐标（大致行），再按 X 坐标排序，有助于处理
            // 注意：这里用 ApproximateBoundingBox.GetY()，更精确的是 StartPoint.Get(Vector.I2)
            var sortedElements = _elements
                .OrderBy(e => e.PageNum)
                .ThenByDescending(e => e.ApproximateBoundingBox.GetY()) // PDF Y轴向上，所以同一行Y值大的在上面
                .ThenBy(e => e.ApproximateBoundingBox.GetX())
                .ToList();

            IPDFElement lastElement = null;

            foreach (var element in sortedElements)
            {
                if (element is TextElement currentText)
                {
                    if (currentLineTextChunks.Count == 0)
                    {
                        currentLineTextChunks.Add(currentText);
                    }
                    else
                    {
                        TextElement prevText = currentLineTextChunks.Last();
                        // --- 合并条件检查 ---
                        if (AreChunksMergeable(prevText, currentText))
                        {
                            currentLineTextChunks.Add(currentText);
                        }
                        else
                        {
                            // 不能合并，先处理之前收集的行内块
                            if (currentLineTextChunks.Any())
                            {
                                mergedElements.Add(CombineTextChunks(currentLineTextChunks));
                                currentLineTextChunks.Clear();
                            }
                            currentLineTextChunks.Add(currentText); // 开始新的收集
                        }
                    }
                }
                else // 非文本元素 (Image, Line)
                {
                    // 先处理之前收集的行内文本块
                    if (currentLineTextChunks.Any())
                    {
                        mergedElements.Add(CombineTextChunks(currentLineTextChunks));
                        currentLineTextChunks.Clear();
                    }
                    mergedElements.Add(element); // 直接添加非文本元素
                }
                lastElement = element;
            }

            // 处理最后一批收集的文本块
            if (currentLineTextChunks.Any())
            {
                mergedElements.Add(CombineTextChunks(currentLineTextChunks));
            }

            return mergedElements;
        }

        // 辅助方法：判断两个文本块是否可以合并
        private bool AreChunksMergeable(TextElement prev, TextElement current, float yTolerance = 2.0f, float xToleranceFactor = 1.5f)
        {
            if (prev == null || current == null) return false;

            // 1. 必须在同一页
            if (prev.PageNum != current.PageNum) return false;

            // 2. 字体、字号、颜色、水平缩放必须基本一致
            if (prev.FontName != current.FontName ||
                Math.Abs(prev.FontSize - current.FontSize) > 0.1f || // 允许极小字体差异
                !AreColorsSimilar(prev.FontColor, current.FontColor) ||
                Math.Abs(prev.HorizontalScaling - current.HorizontalScaling) > 0.1f) // 水平缩放也要一致
            {
                return false;
            }

            // 3. Y 坐标 (基线) 必须非常接近 (同一行)
            // 使用 StartPoint 的 Y 值进行比较
            float prevY = prev.StartPoint.Get(Vector.I2);
            float currentY = current.StartPoint.Get(Vector.I2);
            if (Math.Abs(prevY - currentY) > yTolerance)
            {
                return false;
            }

            // 4. X 坐标必须大致连续
            // prev 的结束点 X 应该接近 current 的起始点 X
            float prevEndX = prev.EndPoint.Get(Vector.I1);
            float currentStartX = current.StartPoint.Get(Vector.I1);

            // 计算预期间隙（基于前一个块的平均字符宽度或空格宽度）
            float expectedSpace = prev.FontSize * 0.3f * xToleranceFactor; // 粗略估算空格宽度容差
                                                                           // 或者使用 prev.CharacterSpacing, prev.WordSpacing (如果可靠)

            if (currentStartX < prevEndX - expectedSpace || // current 在 prev 左边太多（重叠或顺序错）
                currentStartX > prevEndX + expectedSpace * 3)    // current 在 prev 右边太远 (间隙过大)
            {
                return false;
            }

            return true;
        }

        // 辅助方法：判断颜色是否相似 (iText Color 没有直接的 Equals)
        private bool AreColorsSimilar(Color c1, Color c2, float tolerance = 0.01f)
        {
            if (c1 == null && c2 == null) return true;
            if (c1 == null || c2 == null) return false;

            // 比较颜色分量 (假设是 RGB 或 CMYK，更复杂的颜色空间比较更麻烦)
            var comp1 = c1.GetColorValue();
            var comp2 = c2.GetColorValue();
            if (comp1.Length != comp2.Length) return false;

            for (int i = 0; i < comp1.Length; i++)
            {
                if (Math.Abs(comp1[i] - comp2[i]) > tolerance) return false;
            }
            return true;
        }


        // 辅助方法：合并一组 TextElement 为一个
        private TextElement CombineTextChunks(List<TextElement> chunks)
        {
            if (chunks == null || !chunks.Any()) return null;
            if (chunks.Count == 1) return chunks[0]; // 如果只有一个，直接返回

            // 以第一个块为基础，合并文本内容
            TextElement firstChunk = chunks[0];
            string combinedText = string.Join("", chunks.Select(c => c.Text));

            // 计算合并后的边界框 (取所有块的最小外接矩形)
            float minX = chunks.Min(c => c.ApproximateBoundingBox.GetLeft());
            float minY = chunks.Min(c => c.ApproximateBoundingBox.GetBottom());
            float maxX = chunks.Max(c => c.ApproximateBoundingBox.GetRight());
            float maxY = chunks.Max(c => c.ApproximateBoundingBox.GetTop());
            Rectangle combinedBBox = new Rectangle(minX, minY, maxX - minX, maxY - minY);

            // 合并后的 StartPoint 是第一个块的 StartPoint
            // 合并后的 EndPoint 是最后一个块的 EndPoint
            Vector combinedStartPoint = firstChunk.StartPoint;
            Vector combinedEndPoint = chunks.Last().EndPoint;

            // 其他属性（字体、大小、颜色等）在 AreChunksMergeable 中已确保一致，可直接取第一个的
            return new TextElement
            {
                PageNum = firstChunk.PageNum,
                Text = combinedText,
                ApproximateBoundingBox = combinedBBox,
                StartPoint = combinedStartPoint,
                EndPoint = combinedEndPoint,
                FontName = firstChunk.FontName,
                FontSize = firstChunk.FontSize,
                FontColor = firstChunk.FontColor,
                CharacterSpacing = firstChunk.CharacterSpacing, // 平均值或第一个？这里取第一个
                WordSpacing = firstChunk.WordSpacing,         // 同上
                HorizontalScaling = firstChunk.HorizontalScaling, // 同上
                OriginalFont = firstChunk.OriginalFont,       // 同上
                ElementType = firstChunk.ElementType // 合并后的类型通常与第一个块的初步判断一致，后续结构分析会再调整
            };
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
            return GetAndMergeExtractedElements();
        }
    }
}
