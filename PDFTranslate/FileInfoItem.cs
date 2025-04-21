// FileInfoItem.cs 或 MainWindow.xaml.cs 内
namespace PDFTranslate
{
    public class FileInfoItem
    {
        // 用于显示的 文件名 (例如 "我的文档.pdf")
        public string FileName { get; set; }

        // 用于操作的 完整路径 (例如 "C:\Users\You\Documents\我的文档.pdf")
        public string FullPath { get; set; }

        // 之后可以添加其他属性，例如状态
        // public string Status { get; set; } = "等待处理";
    }
}