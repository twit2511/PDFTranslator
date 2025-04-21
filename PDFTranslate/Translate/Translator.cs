using PDFTranslate.Interfaces;
using System;
using System.Net; 
using System.Threading.Tasks;
using TencentCloud.Common;
using TencentCloud.Common.Profile;
using TencentCloud.Tmt.V20180321; // 确认 TMT SDK 版本
using TencentCloud.Tmt.V20180321.Models;
// ----------------------------------


namespace PDFTranslate.translators
{
    public class Translator : ITranslator
    {
        //这里别动
        private const string HardcodedSecretId = "";//"AKIDxX0FG4f3D9sQl4PeS9i5IOJ60oUzISo4";
        private const string HardcodedSecretKey = "";//"pW2b4KOVUJBuUt6DLK6g5IH3f4FE0bz9";
        private const string HardcodedRegion = "";//"ap-guangzhou";
        // --- 硬编码结束 ---

        public string Name => "腾讯云翻译 (硬编码凭据 - 不安全)";

        /// <summary>
        /// 无参构造Translator实例
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        // 使用无参数构造函数
        public Translator()
        {
            // 检查硬编码的值是否为空
            if (string.IsNullOrWhiteSpace(HardcodedSecretId) || string.IsNullOrWhiteSpace(HardcodedSecretKey) || string.IsNullOrWhiteSpace(HardcodedRegion))
            {
                throw new InvalidOperationException("硬编码的腾讯云凭据包含空值，请检查代码。");
            }
        }

        /// <summary>
        /// 翻译函数
        /// </summary>
        /// <param name="textToTranslate">需要翻译的文本</param>
        /// <param name="sourceLanguage">译前语言</param>
        /// <param name="targetLanguage">译后语言</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="Exception"></exception>
        public async Task<string> TranslateAsync(string textToTranslate, string sourceLanguage, string targetLanguage)
        {
            if (string.IsNullOrWhiteSpace(textToTranslate)) return string.Empty;
            if (string.IsNullOrWhiteSpace(sourceLanguage) || string.IsNullOrWhiteSpace(targetLanguage))
                throw new ArgumentException("源语言和目标语言代码不能为空。");

            try
            {
                // 使用硬编码的凭据
                Credential cred = new Credential
                {
                    SecretId = HardcodedSecretId,
                    SecretKey = HardcodedSecretKey
                };

                ClientProfile clientProfile = new ClientProfile();
                HttpProfile httpProfile = new HttpProfile { Endpoint = "tmt.tencentcloudapi.com" };
                clientProfile.HttpProfile = httpProfile;

                // 使用硬编码的区域
                TmtClient client = new TmtClient(cred, HardcodedRegion, clientProfile);

                TextTranslateRequest req = new TextTranslateRequest
                {
                    SourceText = textToTranslate,
                    Source = sourceLanguage,
                    Target = targetLanguage,
                    ProjectId = 0
                };

                TextTranslateResponse resp = await client.TextTranslate(req);
                return resp.TargetText ?? string.Empty;
            }
            catch (TencentCloudSDKException e) // 捕获腾讯云特定的异常
            {
                Console.Error.WriteLine($"腾讯云翻译 API 错误: Code={e.ErrorCode}, Msg={e.Message}, RequestId={e.RequestId}");
                string errorMsg = $"腾讯云翻译失败: {e.Message}";
                // 根据错误码提供更具体信息的逻辑
                switch (e.ErrorCode)
                {
                    case "AuthFailure.SignatureFailure":
                    case "AuthFailure.SecretIdNotFound":
                        errorMsg = "腾讯云认证失败，请检查硬编码的 SecretId 和 SecretKey。"; break;
                    // ... (其他错误码处理保持不变) ...
                    case "LimitExceeded": case "RequestLimitExceeded": errorMsg = "腾讯云调用超限..."; break;
                    case "UnsupportedOperation.UnsupportedLanguage": errorMsg = "腾讯云不支持语言..."; break;
                    case "FailedOperation.NoFreeAmount": errorMsg = "腾讯云免费额度用完..."; break;
                    case "FailedOperation.ServiceIsolate": errorMsg = "腾讯云账户欠费..."; break;
                }
                throw new Exception(errorMsg, e);
            }
            catch (Exception e) // 捕获其他通用异常
            {
                Console.Error.WriteLine($"调用腾讯云翻译时发生意外错误: {e.ToString()}");
                throw new Exception("调用腾讯云翻译时发生意外错误。", e);
            }
        }
    }
}