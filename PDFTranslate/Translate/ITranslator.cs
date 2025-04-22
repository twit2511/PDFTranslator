// PdfGoogleTranslatorApp/Interfaces/ITranslator.cs
using System.Threading.Tasks;
namespace PDFTranslate.Interfaces
{
    public interface ITranslator
    {
        string Name { get; }
        Task<string> TranslateAsync(string textToTranslate, string sourceLanguage, string targetLanguage);
    }
}

