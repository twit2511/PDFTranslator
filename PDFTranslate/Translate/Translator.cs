using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using TencentCloud.Common;
using TencentCloud.Common.Profile;
using TencentCloud.Tmt.V20180321; // 确认 TMT SDK 版本
using TencentCloud.Tmt.V20180321.Models;
// ----------------------------------


namespace PDFTranslate.Translate
{
    public class Translator : ITranslator
    {
        //这里别动
        private const string HardcodedSecretId = "AKIDxX0FG4f3D9sQl4PeS9i5IOJ60oUzISo4";
        private const string HardcodedSecretKey = "pW2b4KOVUJBuUt6DLK6g5IH3f4FE0bz9";
        private const string HardcodedRegion = "ap-guangzhou";
        // --- 硬编码结束 ---

        //如果依然超时增加最大重试次数和延迟时间
        private const int MaxRetryAttempts = 5;                   // 最大重试次数
        private const int InitialRetryDelayMs = 1000;             // 初始重试延迟(毫秒)
        private const int MaxRetryDelayMs = 10000;                // 最大重试延迟(毫秒)
        private const int RequestTimeoutMs = 30000;               // 请求超时时间(毫秒)
        private const int ReadWriteTimeoutMs = 60000;             // 读写超时时间(毫秒)
        private const int MaxTextLength = 1000;                   // 单次请求最大文本长度
        private const int MaxConcurrentRequests = 5;              // 最大并发请求数
        private const int MinRequestIntervalMs = 100;             // 最小请求间隔(毫秒)

        public string Name => "腾讯云翻译";

        // 控制变量
        private static readonly SemaphoreSlim _throttler = new SemaphoreSlim(MaxConcurrentRequests, MaxConcurrentRequests);
        private static readonly object _timingLock = new object();
        private static DateTime _lastRequestTime = DateTime.MinValue;





        /// <summary>
        /// 翻译函数
        /// </summary>
        /// <param name="textToTranslate">需要翻译的文本</param>
        /// <param name="sourceLanguage">译前语言</param>
        /// <param name="targetLanguage">译后语言</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="Exception"></exception>
        public string TranslateAsync(string textToTranslate, string sourceLanguage, string targetLanguage)
        {
            if (string.IsNullOrWhiteSpace(textToTranslate)) return string.Empty;
            if (string.IsNullOrWhiteSpace(sourceLanguage) || string.IsNullOrWhiteSpace(targetLanguage))
                throw new ArgumentException("源语言和目标语言代码不能为空。");

            // 长文本自动分块处理
            if (textToTranslate.Length > MaxTextLength)
            {
                return ProcessLongText(textToTranslate, sourceLanguage, targetLanguage);
            }

            EnforceRateLimit();

            // 应用并发控制
            _throttler.Wait();

            try
            {
                return TranslateWithRetry(textToTranslate, sourceLanguage, targetLanguage);
            }
            finally
            {
                _throttler.Release();
            }
        }

        private string TranslateWithRetry(string text, string source, string target)
        {
            int retryCount = 0;
            int delayMs = InitialRetryDelayMs;

            while (true)
            {
                try
                {
                    return ExecuteTranslation(text, source, target);
                }
                catch (Exception ex) when (IsTransientError(ex) && retryCount < MaxRetryAttempts)
                {
                    retryCount++;
                    Console.WriteLine($"第 {retryCount} 次重试，延迟 {delayMs}ms... 错误: {ex.Message}");

                    // 指数退避
                    Thread.Sleep(delayMs);
                    delayMs = Math.Min(delayMs * 2, MaxRetryDelayMs);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"翻译失败: {ex}");
                    throw new Exception(FormatErrorMessage(ex), ex);
                }
            }
        }

        //请求服务函数
        private string ExecuteTranslation(string textToTranslate, string sourceLanguage, string targetLanguage)
        {
            try
            {
                Credential cred = new Credential
                {
                    SecretId = HardcodedSecretId,
                    SecretKey = HardcodedSecretKey
                };

                ClientProfile clientProfile = new ClientProfile();
                HttpProfile httpProfile = new HttpProfile
                {
                    Endpoint = "tmt.tencentcloudapi.com",
                    Timeout = RequestTimeoutMs // 设置请求超时
                };
                clientProfile.HttpProfile = httpProfile;

                TmtClient client = new TmtClient(cred, HardcodedRegion, clientProfile);

                TextTranslateRequest req = new TextTranslateRequest
                {
                    SourceText = textToTranslate,
                    Source = sourceLanguage,
                    Target = targetLanguage,
                    ProjectId = 0
                };

                // 同步调用
                TextTranslateResponse resp = client.TextTranslate(req).Result;
                return resp.TargetText ?? string.Empty;
            }
            catch (TencentCloudSDKException e)
            {
                Console.Error.WriteLine($"腾讯云翻译 API 错误: Code={e.ErrorCode}, Msg={e.Message}, RequestId={e.RequestId}");
                string errorMsg = $"腾讯云翻译失败: {e.Message}";

                switch (e.ErrorCode)
                {
                    case "AuthFailure.SignatureFailure":
                    case "AuthFailure.SecretIdNotFound":
                        errorMsg = "腾讯云认证失败，请检查硬编码的 SecretId 和 SecretKey。"; break;
                    case "LimitExceeded":
                    case "RequestLimitExceeded":
                        errorMsg = "腾讯云调用超限，请稍后再试或增加重试间隔。"; break;
                    case "UnsupportedOperation.UnsupportedLanguage":
                        errorMsg = "腾讯云不支持该语言对。"; break;
                    case "FailedOperation.NoFreeAmount":
                        errorMsg = "腾讯云免费额度已用完。"; break;
                    case "FailedOperation.ServiceIsolate":
                        errorMsg = "腾讯云账户欠费。"; break;
                    case "RequestTimeout":
                        errorMsg = "腾讯云请求超时。"; break;
                }
                throw new Exception(errorMsg, e);
            }
        }

        private HttpProfile CreateHttpProfile()
        {
            return new HttpProfile
            {
                Endpoint = "tmt.tencentcloudapi.com",
                Timeout = RequestTimeoutMs,
                Protocol = "https",
                WebProxy = null,
                
            };
        }

        private string ProcessLongText(string text, string source, string target)
        {
            // 简单分句逻辑（可按需改进）
            var sentences = text.Split(new[] { '.', '。', '!', '！', '?', '？', '\n', '\r' },
                                     StringSplitOptions.RemoveEmptyEntries);

            var results = new List<string>();
            foreach (var sentence in sentences)
            {
                if (!string.IsNullOrWhiteSpace(sentence))
                {
                    results.Add(TranslateAsync(sentence.Trim(), source, target));
                }
            }

            return string.Join(" ", results);
        }

        private void EnforceRateLimit()
        {
            lock (_timingLock)
            {
                var elapsed = DateTime.Now - _lastRequestTime;
                if (elapsed.TotalMilliseconds < MinRequestIntervalMs)
                {
                    Thread.Sleep(MinRequestIntervalMs - (int)elapsed.TotalMilliseconds);
                }
                _lastRequestTime = DateTime.Now;
            }
        }

        //判断是否是暂时性错误
        private bool IsTransientError(Exception ex)
        {
            if (ex is TencentCloudSDKException sdkEx)
            {
                // 这些错误码表示可能通过重试解决的问题
                return sdkEx.ErrorCode == "RequestLimitExceeded" ||
                       sdkEx.ErrorCode == "LimitExceeded" ||
                       sdkEx.ErrorCode == "InternalError" ||
                       sdkEx.ErrorCode == "RequestTimeout";
            }

            // 网络相关的异常通常可以重试
            return ex is WebException || ex is TimeoutException;
        }

        private string FormatErrorMessage(Exception ex)
        {
            if (ex is TencentCloudSDKException sdkEx)
            {
                return ex.Message;
            }
            return ex.Message;
        }
    }
}