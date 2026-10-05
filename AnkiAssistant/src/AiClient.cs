using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace AnkiAssistant
{
    /// <summary>AI 调用失败时抛出的异常（对应安卓版的 AiClient.AiException，文案保持一致）</summary>
    public class AiException : Exception
    {
        public AiException(string msg) : base(msg) { }
        public AiException(string msg, Exception inner) : base(msg, inner) { }
    }

    /// <summary>
    /// 一次调用的结果：正文 + 思考过程（思考型模型会返回 reasoning_content，普通模型为空）+ 用量 + 耗时。
    /// 安卓版的 Reply 只有正文与思考过程，这里按 Windows 版界面需要补上 token 用量与秒数。
    /// </summary>
    public class AiReply
    {
        public string Content = "";      // 助手回复正文
        public string Reasoning = "";    // 思考型模型的 reasoning_content，没有则为空
        public int PromptTokens;
        public int CompletionTokens;
        public int TotalTokens;
        public double Seconds;
    }

    /// <summary>
    /// AI 自动填充。全部走 OpenAI 兼容的 /chat/completions 协议，所以换服务商只是换地址和模型名。
    /// 内置四个预设 + 一个自定义，用户在设置里自己挑（需求原文："可内置调用一个免费AI，如deepseek免费版或豆包，最好可以自己选择"）。
    ///
    /// 注意：不在程序里内置任何 API Key —— 打包进程序的密钥任何人都能提取盗用。Windows 版一律使用调用方传入的 Key。
    /// 免费档提供「智谱 GLM-4.5-Flash（免费）」预设，默认就是它。
    ///
    /// 本文件移植自 AnkiAssistantAndroid 的 com.ankiassistant.AiClient.java，预设表、请求体、解析逻辑与错误文案逐条对齐。
    /// 仅 C# 4.0/5.0 语法（csc.exe / .NET Framework 4.x 可直接编译）。
    /// </summary>
    public static class AiClient
    {
        // ------------------------------------------------------------------ 预设

        public const string PDeepSeek = "deepseek";
        public const string PDoubao = "doubao";
        public const string PZhipu = "zhipu";
        public const string PSilicon = "siliconflow";
        public const string PCustom = "custom";

        /// <summary>默认预设：智谱 GLM-4.5-Flash，免费额度，注册后申请 Key 即可用</summary>
        public const string DefaultPreset = PZhipu;

        /// <summary>
        /// 当前预设。设置页存的是 ai.provider，早期版本存过 ai.preset —— 两个都认，
        /// 否则用户在设置里换了服务商，制卡页仍然按默认预设走。
        /// </summary>
        public static string ActivePreset()
        {
            string p = Store.Get("ai.provider", "");
            if (p.Length > 0) return p;
            return Store.Get("ai.preset", DefaultPreset);
        }

        /// <summary>
        /// 静态构造：打开 TLS 1.2。.NET Framework 4.x 默认可能用 TLS 1.0，
        /// 不打开的话到 api.deepseek.com / open.bigmodel.cn 这些站点的 HTTPS 会直接握手失败。
        /// </summary>
        static AiClient()
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            }
            catch (NotSupportedException)
            {
                // 老系统（WinXP 等）不支持 TLS 1.2，退回系统默认，保持能编译能跑
            }
        }

        public static string PresetLabel(string id)
        {
            if (PDeepSeek.Equals(id)) return "DeepSeek";
            if (PDoubao.Equals(id)) return "豆包（火山方舟）";
            if (PZhipu.Equals(id)) return "智谱 GLM-4.5-Flash（免费）";
            if (PSilicon.Equals(id)) return "硅基流动 SiliconFlow";
            if (PCustom.Equals(id)) return "自定义（OpenAI 兼容）";
            return id;
        }

        public static string PresetBaseUrl(string id)
        {
            if (PDeepSeek.Equals(id)) return "https://api.deepseek.com/chat/completions";
            if (PDoubao.Equals(id)) return "https://ark.cn-beijing.volces.com/api/v3/chat/completions";
            if (PZhipu.Equals(id)) return "https://open.bigmodel.cn/api/paas/v4/chat/completions";
            if (PSilicon.Equals(id)) return "https://api.siliconflow.cn/v1/chat/completions";
            return "";
        }

        public static string PresetModel(string id)
        {
            if (PDeepSeek.Equals(id)) return "deepseek-chat";
            if (PDoubao.Equals(id)) return "doubao-seed-1-6-250615";
            if (PZhipu.Equals(id)) return "glm-4.5-flash";
            if (PSilicon.Equals(id)) return "Qwen/Qwen2.5-7B-Instruct";
            return "";
        }

        /// <summary>全部预设都需要 API Key（免费的只是额度免费，注册后仍要 Key）</summary>
        public static bool NeedsKey(string id)
        {
            return true;
        }

        // ------------------------------------------------------------------ 请求/响应

        /// <summary>
        /// 构造 OpenAI 兼容的 /chat/completions 请求体。
        /// </summary>
        /// <param name="thinking">true 时带上 thinking={"type":"disabled"}（智谱思考型模型专用）。
        ///   实测：glm-4.5-flash 开思考 23 秒、思考内容 1279 字还把 JSON 输出挤到截断；
        ///   关掉之后 4.5 秒、一次成型 —— 制卡这种任务没必要让它"想"。</param>
        public static string BuildRequestBody(string model, string system, string user, bool thinking)
        {
            string m = (model == null || model.Trim().Length == 0) ? "deepseek-chat" : model.Trim();

            List<object> msgs = new List<object>();
            if (system != null && system.Length > 0)
            {
                Dictionary<string, object> s = new Dictionary<string, object>();
                s["role"] = "system";
                s["content"] = system;
                msgs.Add(s);
            }
            Dictionary<string, object> u = new Dictionary<string, object>();
            u["role"] = "user";
            u["content"] = user;
            msgs.Add(u);

            Dictionary<string, object> o = new Dictionary<string, object>();
            o["model"] = m;
            o["messages"] = msgs;
            o["temperature"] = 0.4;
            if (thinking)
            {
                // thinking=false 时显式关掉思考，见上面参数说明
                Dictionary<string, object> t = new Dictionary<string, object>();
                t["type"] = "disabled";
                o["thinking"] = t;
            }

            JavaScriptSerializer ser = new JavaScriptSerializer();
            return ser.Serialize(o);
        }

        /// <summary>从完整响应体里取正文与用量；解析失败抛 AiException（带上服务端给的 error.message）</summary>
        public static AiReply ParseReply(string body)
        {
            if (body == null || body.Trim().Length == 0) throw new AiException("AI 返回了空响应");

            Dictionary<string, object> o;
            try
            {
                o = DeserializeObject(body);
            }
            catch (Exception e)
            {
                throw new AiException("AI 返回了无法解析的内容：" + Head(body), e);
            }
            if (o == null) throw new AiException("AI 返回了无法解析的内容：" + Head(body));

            // 服务端把错误当 200 返回时（有的中转站这么干），也要按报错处理
            if (o.ContainsKey("error"))
            {
                string msg;
                object err = o["error"];
                Dictionary<string, object> errObj = err as Dictionary<string, object>;
                if (errObj != null)
                {
                    msg = Str(errObj, "message");
                    if (msg.Length == 0) msg = Plain(err);
                }
                else
                {
                    msg = Plain(err);
                    if (msg.Length == 0) msg = "unknown error";
                }
                throw new AiException("AI 接口报错：" + msg);
            }

            try
            {
                if (!o.ContainsKey("choices")) throw new AiException("AI 响应格式不认识：" + Head(body));
                object choicesObj = o["choices"];
                IList choices = choicesObj as IList;
                if (choices == null) throw new AiException("AI 响应格式不认识：" + Head(body));
                if (choices.Count == 0) throw new AiException("AI 没有返回任何内容");

                Dictionary<string, object> choice = choices[0] as Dictionary<string, object>;
                if (choice == null) throw new AiException("AI 响应格式不认识：" + Head(body));

                AiReply r = new AiReply();

                // 少数服务商（或中转）把正文直接放在 choices[0].text 上，能救则救
                Dictionary<string, object> msg = null;
                if (choice.ContainsKey("message")) msg = choice["message"] as Dictionary<string, object>;
                if (msg != null)
                {
                    r.Content = Str(msg, "content");
                    r.Reasoning = Str(msg, "reasoning_content");
                }
                else
                {
                    r.Content = Str(choice, "text");
                }

                if (r.Content.Trim().Length == 0 && r.Reasoning.Trim().Length == 0)
                    throw new AiException("AI 返回了空内容");

                ReadUsage(o, r);
                return r;
            }
            catch (AiException)
            {
                throw;
            }
            catch (Exception e)
            {
                throw new AiException("AI 响应格式不认识：" + Head(body), e);
            }
        }

        // ------------------------------------------------------------------ 网络

        /// <summary>
        /// 发一次请求；超时/网络错误/HTTP 非 2xx 都要给出和安卓版同样风格的中文错误。
        /// thinking=false 而服务商不认识 thinking 字段时，自动去掉该字段重发一次。
        /// </summary>
        public static AiReply ChatDetailed(string baseUrl, string apiKey, string model,
                                           string system, string user, bool thinking)
        {
            string url = baseUrl == null ? "" : baseUrl.Trim();
            if (url.Length == 0) throw new AiException("还没填 AI 接口地址，请到「设置」里选择 AI 服务商");
            if (url.IndexOf("://", StringComparison.Ordinal) < 0) url = "https://" + url;

            string k = apiKey == null ? "" : apiKey.Trim();
            if (k.Length == 0)
                throw new AiException("还没有填 API Key —— 到「设置 → AI 自动填充」里填一个。");

            try
            {
                return ChatOnce(url, k, model, system, user, thinking, thinking);
            }
            catch (AiException e)
            {
                if (!thinking && IsThinkingRejected(e))
                {
                    // 4xx 且看不出是「服务端自己报错」时，多半是这家中转/服务商不认识 thinking 字段：去掉它重发一次
                    return ChatOnce(url, k, model, system, user, false, false);
                }
                throw;
            }
        }

        /// <summary>发一次请求（不做去掉 thinking 的兜底）</summary>
        private static AiReply ChatOnce(string url, string apiKey, string model,
                                        string system, string user, bool thinking, bool sendThinkingField)
        {
            byte[] body = Encoding.UTF8.GetBytes(BuildRequestBody(model, system, user, sendThinkingField));

            DateTime started = DateTime.UtcNow;
            HttpWebRequest req = null;
            HttpWebResponse resp = null;
            try
            {
                req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/json; charset=utf-8";
                req.Accept = "application/json";
                req.Timeout = 8000;             // 连接超时（对应 Java 的 setConnectTimeout(8000)）
                req.ReadWriteTimeout = 180000;  // 读超时（免费档思考型模型实测要 50-65 秒，别用 60 秒卡死它）
                if (apiKey.Length > 0) req.Headers["Authorization"] = "Bearer " + apiKey;

                req.ContentLength = body.Length;
                using (Stream os = req.GetRequestStream())
                {
                    os.Write(body, 0, body.Length);
                    os.Flush();
                }

                resp = (HttpWebResponse)req.GetResponse();
                string text = ReadAll(resp);
                int code = (int)resp.StatusCode;
                if (code < 200 || code >= 400)
                    throw HttpError(code, text);

                AiReply r = ParseReply(text);
                r.Seconds = (DateTime.UtcNow - started).TotalSeconds;
                return r;
            }
            catch (AiException)
            {
                throw;
            }
            catch (WebException e)
            {
                string text = "";
                HttpWebResponse errResp = e.Response as HttpWebResponse;
                if (errResp != null) text = ReadAll(errResp);
                int code = errResp == null ? 0 : (int)errResp.StatusCode;

                // 服务端明确返回了 HTTP 状态码：报状态码 + 错误片段（与安卓版同款文案）
                if (code >= 400) throw HttpError(code, text);
                // 连接超时/读超时统一按超时文案报（对应 Java 的 SocketTimeoutException 分支）
                if (e.Status == WebExceptionStatus.Timeout)
                    throw new AiException("AI 响应超时（已等待 3 分钟），请重试", e);
                string detail = e.Message;
                if (text != null && text.Trim().Length > 0)
                    detail = text.Trim();
                else if (e.InnerException != null && e.InnerException.Message.Length > 0)
                    detail = detail + "（" + e.InnerException.Message + "）";
                throw new AiException("调用 AI 失败：" + Head(detail), e);
            }
            catch (Exception e)
            {
                throw new AiException("调用 AI 失败：" + e.Message, e);
            }
            finally
            {
                if (resp != null)
                {
                    try { resp.Close(); }
                    catch (Exception) { }
                }
            }
        }

        /// <summary>把 HTTP 状态码 + 服务端错误片段包装成 AiException（文案与安卓版一致）</summary>
        private static AiException HttpError(int code, string text)
        {
            string detail = text == null ? "" : text;
            try
            {
                Dictionary<string, object> o = DeserializeObject(detail);
                if (o != null && o.ContainsKey("error"))
                {
                    object err = o["error"];
                    Dictionary<string, object> errObj = err as Dictionary<string, object>;
                    if (errObj != null)
                    {
                        string m = Str(errObj, "message");
                        if (m.Length > 0) detail = m;
                    }
                    else
                    {
                        string m = Plain(err);
                        if (m.Length > 0) detail = m;
                    }
                }
            }
            catch (Exception)
            {
                // 不是 JSON 就原样用返回体
            }
            return new AiException("AI 接口 HTTP " + code + "：" + Head(detail));
        }

        /// <summary>判断这个 4xx 是不是「不认识 thinking 字段」造成的（服务端自己给了 error.message 就不去重试）</summary>
        private static bool IsThinkingRejected(AiException e)
        {
            int code = HttpStatusCode(e);
            if (code < 400 || code >= 500) return false;
            string m = e.Message;
            int idx = m.IndexOf('：');
            if (idx < 0) return false;
            string detail = m.Substring(idx + 1);
            // 服务端回了 JSON 错误结构（以 { 开头），说明它读懂了请求、问题不在 thinking 字段
            if (detail.TrimStart().StartsWith("{", StringComparison.Ordinal)) return false;
            return true;
        }

        private static int HttpStatusCode(AiException e)
        {
            const string prefix = "AI 接口 HTTP ";
            if (e == null || e.Message == null) return 0;
            if (!e.Message.StartsWith(prefix, StringComparison.Ordinal)) return 0;
            int i = prefix.Length;
            StringBuilder digits = new StringBuilder();
            while (i < e.Message.Length && e.Message[i] >= '0' && e.Message[i] <= '9')
            {
                digits.Append(e.Message[i]);
                i++;
            }
            int code;
            if (int.TryParse(digits.ToString(), out code)) return code;
            return 0;
        }

        private static string ReadAll(HttpWebResponse resp)
        {
            if (resp == null) return "";
            Stream st = null;
            StreamReader reader = null;
            try
            {
                st = resp.GetResponseStream();
                if (st == null) return "";
                // 服务端可能不写 charset，这里强制按 UTF-8 解（与 Java 版一致）
                reader = new StreamReader(st, new UTF8Encoding(false));
                return reader.ReadToEnd();
            }
            catch (Exception)
            {
                return "";
            }
            finally
            {
                if (reader != null)
                {
                    try { reader.Close(); }
                    catch (Exception) { }
                }
                else if (st != null)
                {
                    try { st.Close(); }
                    catch (Exception) { }
                }
            }
        }

        // ------------------------------------------------------------------ 小工具

        private static Dictionary<string, object> DeserializeObject(string json)
        {
            JavaScriptSerializer ser = new JavaScriptSerializer();
            ser.MaxJsonLength = 32 * 1024 * 1024;
            return ser.Deserialize<Dictionary<string, object>>(json);
        }

        /// <summary>取字符串字段；嵌套对象/数组返回空串</summary>
        private static string Str(Dictionary<string, object> o, string key)
        {
            if (o == null || !o.ContainsKey(key)) return "";
            object v = o[key];
            if (v == null) return "";
            return v as string;
        }

        /// <summary>把不是字符串的对象转成可读文本（对应 Java 的 String.valueOf(opt(...)))</summary>
        private static string Plain(object v)
        {
            if (v == null) return "";
            if (v is string) return (string)v;
            Dictionary<string, object> d = v as Dictionary<string, object>;
            if (d != null)
            {
                JavaScriptSerializer ser = new JavaScriptSerializer();
                return ser.Serialize(d);
            }
            return Convert.ToString(v);
        }

        private static void ReadUsage(Dictionary<string, object> o, AiReply r)
        {
            object usageObj = o.ContainsKey("usage") ? o["usage"] : null;
            Dictionary<string, object> usage = usageObj as Dictionary<string, object>;
            if (usage == null) return;
            r.PromptTokens = IntValue(usage, "prompt_tokens");
            r.CompletionTokens = IntValue(usage, "completion_tokens");
            r.TotalTokens = IntValue(usage, "total_tokens");
        }

        private static int IntValue(Dictionary<string, object> o, string key)
        {
            if (o == null || !o.ContainsKey(key)) return 0;
            object v = o[key];
            if (v == null) return 0;
            if (v is int) return (int)v;
            if (v is long) return (int)(long)v;
            if (v is double) return (int)(double)v;
            if (v is decimal) return (int)(decimal)v;
            if (v is string)
            {
                int n;
                if (int.TryParse(((string)v).Trim(), out n)) return n;
            }
            try { return Convert.ToInt32(v); }
            catch (Exception) { return 0; }
        }

        private static string Head(string s)
        {
            if (s == null) s = "";
            return s.Length <= 200 ? s : s.Substring(0, 200) + "…";
        }
    }
}
