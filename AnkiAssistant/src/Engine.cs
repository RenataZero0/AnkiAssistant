using System;
using System.Runtime.InteropServices;
using System.Text;

namespace AnkiAssistant
{
    /// <summary>
    /// 内置引擎调用失败。Code 是 C ABI 返回码（0 不会走到这里），
    /// Service/Method 是出错的 RPC（-1 表示不是 RPC 层面）。
    /// </summary>
    public sealed class EngineException : Exception
    {
        public readonly int Code;
        public readonly int Service;
        public readonly int Method;

        public EngineException(string message)
            : base(message)
        {
            Code = -1;
            Service = -1;
            Method = -1;
        }

        public EngineException(string message, int code, int service, int method)
            : base(message)
        {
            Code = code;
            Service = service;
            Method = method;
        }
    }

    /// <summary>
    /// 内置 Anki 引擎（rslib_aa.dll）的 P/Invoke 封装。
    ///
    /// C ABI（aa-ffi/src/lib.rs，已逐行核对过，签名照抄不改）：
    ///   int32_t aa_open(const uint8_t* data, size_t data_len, uint64_t* out_handle,
    ///                   uint8_t** out_buf, size_t* out_len);
    ///   int32_t aa_call(uint64_t handle, int32_t service, int32_t method,
    ///                   const uint8_t* data, size_t data_len,
    ///                   uint8_t** out_buf, size_t* out_len);
    ///   void    aa_close(uint64_t handle);
    ///   void    aa_free(uint8_t* buf, size_t len);
    ///   const char* aa_version(void);
    ///   const char* aa_last_error(void);   // 比签名表多出来的一个，失败时拿可读文本最省事
    ///
    /// 三个必须记住的行为（来自 lib.rs 的实际实现，不是猜的）：
    ///   1. 返回码有 4 档：0=OK、1=ARGS（参数错）、2=ENGINE（引擎报错）、3=PANIC。
    ///   2. 响应缓冲区由 Rust 分配（Box<[u8]>），**必须** aa_free 释放，不能用 FreeHGlobal。
    ///      aa_free 对 (null,0) 是安全的 no-op。
    ///   3. aa_open 的 data 是一个 **BackendInit** 消息（不是收藏库请求），空 payload 就合法；
    ///      打开收藏库要靠 aa_call(handle, 3, 0, OpenCollectionRequest)。
    ///      aa_open 失败时**不写** out_buf，所以错误文本只能从 aa_last_error() 读。
    ///
    /// 句柄与缓冲区的释放都是「无论如何」：调用失败、Marshal.Copy 抛异常，缓冲区一样会 free。
    /// </summary>
    public static class Engine
    {
        const string DllName = "rslib_aa.dll";

        public const int Ok = 0;
        public const int ErrArgs = 1;
        public const int ErrEngine = 2;
        public const int ErrPanic = 3;

        // ---------------- 原生声明 ----------------
        [DllImport(DllName, EntryPoint = "aa_version", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr AaVersion();

        [DllImport(DllName, EntryPoint = "aa_last_error", CallingConvention = CallingConvention.Cdecl)]
        static extern IntPtr AaLastError();

        [DllImport(DllName, EntryPoint = "aa_open", CallingConvention = CallingConvention.Cdecl)]
        static extern int AaOpen(IntPtr data, UIntPtr dataLen, out ulong outHandle,
                                 out IntPtr outBuf, out UIntPtr outLen);

        [DllImport(DllName, EntryPoint = "aa_call", CallingConvention = CallingConvention.Cdecl)]
        static extern int AaCall(ulong handle, int service, int method, IntPtr data, UIntPtr dataLen,
                                 out IntPtr outBuf, out UIntPtr outLen);

        [DllImport(DllName, EntryPoint = "aa_close", CallingConvention = CallingConvention.Cdecl)]
        static extern void AaClose(ulong handle);

        [DllImport(DllName, EntryPoint = "aa_free", CallingConvention = CallingConvention.Cdecl)]
        static extern void AaFree(IntPtr buf, UIntPtr len);

        [DllImport("kernel32.dll", EntryPoint = "LoadLibraryW", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern IntPtr LoadLibraryW(string path);

        // ---------------- 状态 ----------------
        static readonly object _lock = new object();

        /// <summary>
        /// 串行化所有原生调用（aa_open / aa_call / aa_close）。
        /// 原生 aa_call 内部**没有加锁**（实现就是 &amp;mut *(handle as *mut Backend)，
        /// 与 rsdroid 的 JNI 桥一致），同一个 handle 被并发调用的行为从未验证过，
        /// 所以这里必须保证同一时刻只有一个线程在原生引擎里跑。
        /// 用单独的一把锁而不是 _lock：一次同步 RPC 可能跑几分钟，
        /// 不能顺手把 EnsureLoaded / UseDllFrom 一起堵死。
        /// </summary>
        static readonly object _callLock = new object();
        static string _dllDir;          // 显式指定优先
        static string _dllPath;         // 实际加载成功的路径
        static bool _tried;
        static bool _loaded;
        static string _why = "尚未尝试加载";
        static ulong _handle;
        static string _lastError = "";

        /// <summary>用过的最后一个原生错误文本（中文兜底 + aa_last_error 的内容）。</summary>
        public static string LastError { get { return _lastError; } }

        /// <summary>DLL 是否可用。第一次访问时尝试加载，结果被记住（不会反复碰磁盘）。</summary>
        public static bool Available { get { return EnsureLoaded(); } }

        /// <summary>不可用的原因（给用户看的中文文本）。</summary>
        public static string UnavailableReason
        {
            get { EnsureLoaded(); return _why; }
        }

        /// <summary>实际加载到的 DLL 绝对路径；没加载成功时为 null。</summary>
        public static string DllPath
        {
            get { EnsureLoaded(); return _dllPath; }
        }

        /// <summary>句柄是否已经打开（Open 成功且还没 Close）。</summary>
        public static bool Opened { get { return _handle != 0UL; } }

        /// <summary>引擎自报版本号，形如 "aa-ffi 0.1.0 / rslib anki 26.09.3 (build ) / windows-x86_64"。</summary>
        public static string Version()
        {
            if (!EnsureLoaded()) return "";
            try
            {
                return Utf8FromPtr(AaVersion());
            }
            catch (Exception e)
            {
                _lastError = "读取引擎版本失败：" + One(e);
                return "";
            }
        }

        /// <summary>
        /// 告诉引擎去哪里找 rslib_aa.dll（绝对路径，可以指向 tools\lib 里的那一份）。
        /// 必须在第一次成功调用之前生效；已经加载成功的话这次调用只记录路径、不重新加载。
        /// </summary>
        public static void UseDllFrom(string fullPathOrNull)
        {
            lock (_lock)
            {
                _dllDir = fullPathOrNull;
                if (!_loaded)
                {
                    _tried = false;
                    _why = "尚未尝试加载";
                }
            }
        }

        // ---------------- 核心 RPC ----------------
        /// <summary>
        /// 用空 BackendInit 起一个后端，然后 RPC 3/0 打开收藏库。
        /// collectionPath 形如 `C:\...\collection.anki2`；mediaFolder/mediaDb 传 null 表示用默认。
        /// </summary>
        public static void Open(string collectionPath, string mediaFolder, string mediaDb)
        {
            lock (_callLock) { OpenCore(collectionPath, mediaFolder, mediaDb); }
        }

        static void OpenCore(string collectionPath, string mediaFolder, string mediaDb)
        {
            if (!EnsureLoaded())
            {
                throw new EngineException("内置 Anki 引擎不可用：" + _why);
            }
            if (collectionPath == null || collectionPath.Length == 0)
            {
                throw new EngineException("打开收藏库失败：收藏库路径为空");
            }
            if (_handle != 0UL)
            {
                throw new EngineException("内置引擎已经打开了一个收藏库，先 Close() 再 Open()");
            }

            ulong h;
            byte[] greeting;
            int rc = AaOpenRaw(new byte[0], out h, out greeting);
            if (rc != Ok)
            {
                _lastError = ReadNativeError(greeting, rc);
                throw new EngineException("初始化内置引擎失败（aa_open 返回 " + rc + "）：" + _lastError,
                                          rc, 3, 0);
            }
            _handle = h;

            // OpenCollectionRequest { collection_path = 1, media_folder_path = 2, media_db_path = 3 }
            Pb.Writer w = new Pb.Writer(256);
            w.Str(1, collectionPath);
            if (mediaFolder != null && mediaFolder.Length > 0) w.Str(2, mediaFolder);
            if (mediaDb != null && mediaDb.Length > 0) w.Str(3, mediaDb);

            try
            {
                Call(EngineMethods.Collection, EngineMethods.OpenCollection, w.ToBytes());
            }
            catch (Exception)
            {
                Close();
                throw;
            }
        }

        /// <summary>
        /// 调一个 RPC。成功返回响应字节（可能是空数组）；失败抛 EngineException。
        /// 参数里的 service/method 见 EngineMethods 的常量（编号依据 _backend_generated.py）。
        /// </summary>
        public static byte[] Call(int service, int method, byte[] req)
        {
            // 见 _callLock 的注释：原生侧不保证并发安全，这里串行化。
            lock (_callLock) { return CallCore(service, method, req); }
        }

        static byte[] CallCore(int service, int method, byte[] req)
        {
            if (!EnsureLoaded())
            {
                throw new EngineException("内置 Anki 引擎不可用：" + _why);
            }
            if (_handle == 0UL)
            {
                throw new EngineException("内置引擎还没打开收藏库（先 Open）", ErrArgs, service, method);
            }

            IntPtr inPtr = IntPtr.Zero;
            GCHandle pin = default(GCHandle);
            int inLen = (req == null) ? 0 : req.Length;
            if (inLen > 0)
            {
                pin = GCHandle.Alloc(req, GCHandleType.Pinned);
                inPtr = pin.AddrOfPinnedObject();
            }

            IntPtr outBuf = IntPtr.Zero;
            UIntPtr outLen = UIntPtr.Zero;
            int rc;
            try
            {
                rc = AaCall(_handle, service, method, inPtr, (UIntPtr)(ulong)inLen, out outBuf, out outLen);
            }
            catch (Exception e)
            {
                _lastError = "调用原生引擎抛出异常（service=" + service + " method=" + method + "）：" + One(e);
                throw new EngineException(_lastError, -1, service, method);
            }
            finally
            {
                if (pin.IsAllocated) pin.Free();
            }

            byte[] resp = TakeBuffer(outBuf, outLen);

            if (rc != Ok)
            {
                _lastError = ReadNativeError(resp, rc);
                throw new EngineException("RPC 失败（service=" + service + " method=" + method +
                                          "，返回码 " + rc + "）：" + _lastError, rc, service, method);
            }
            _lastError = "";
            return resp;
        }

        /// <summary>关闭句柄。重复调用安全（原生侧对 handle==0 直接返回）。</summary>
        public static void Close()
        {
            lock (_callLock) { CloseCore(); }
        }

        static void CloseCore()
        {
            ulong h = _handle;
            _handle = 0UL;
            if (h == 0UL) return;
            try
            {
                AaClose(h);
            }
            catch (Exception e)
            {
                _lastError = "关闭内置引擎时出错：" + One(e);
            }
        }

        // ---------------- 内部 ----------------
        static int AaOpenRaw(byte[] payload, out ulong handle, out byte[] resp)
        {
            IntPtr inPtr = IntPtr.Zero;
            GCHandle pin = default(GCHandle);
            int inLen = (payload == null) ? 0 : payload.Length;
            if (inLen > 0)
            {
                pin = GCHandle.Alloc(payload, GCHandleType.Pinned);
                inPtr = pin.AddrOfPinnedObject();
            }

            IntPtr outBuf = IntPtr.Zero;
            UIntPtr outLen = UIntPtr.Zero;
            int rc;
            try
            {
                rc = AaOpen(inPtr, (UIntPtr)(ulong)inLen, out handle, out outBuf, out outLen);
            }
            catch (Exception e)
            {
                _lastError = "加载/初始化原生引擎抛出异常：" + One(e);
                throw new EngineException(_lastError);
            }
            finally
            {
                if (pin.IsAllocated) pin.Free();
            }

            resp = TakeBuffer(outBuf, outLen);
            return rc;
        }

        /// <summary>把 Rust 分配的响应缓冲区拷出来并**一定** aa_free 掉。</summary>
        static byte[] TakeBuffer(IntPtr buf, UIntPtr len)
        {
            if (buf == IntPtr.Zero) return new byte[0];
            byte[] data = new byte[0];
            try
            {
                ulong n64 = len.ToUInt64();
                if (n64 > 0 && n64 <= int.MaxValue)
                {
                    int n = (int)n64;
                    data = new byte[n];
                    Marshal.Copy(buf, data, 0, n);
                }
            }
            catch (Exception e)
            {
                _lastError = "复制引擎响应缓冲区失败：" + One(e);
                data = new byte[0];
            }
            finally
            {
                try { AaFree(buf, len); }
                catch (Exception e2) { _lastError = "释放引擎缓冲区失败：" + One(e2); }
            }
            return data;
        }

        /// <summary>错误文本优先取 aa_last_error()；没有就退回解析 BackendError.message（字段 1）。</summary>
        static string ReadNativeError(byte[] resp, int rc)
        {
            string err = "";
            if (_loaded)
            {
                try { err = Utf8FromPtr(AaLastError()); }
                catch (Exception) { err = ""; }
            }
            if (err != null && err.Length > 0) return Clip(err);

            string fromProto = DecodeBackendError(resp);
            if (fromProto.Length > 0) return Clip(fromProto);

            return Clip(ExplainCode(rc));
        }

        /// <summary>
        /// BackendError { string message = 1; Kind kind = 2; ... }，见 anki\proto\anki\backend.proto:23-63。
        /// 解不出来就返回空串（错误文本不是关键路径，不能因为解析失败再抛一个异常）。
        /// </summary>
        public static string DecodeBackendError(byte[] resp)
        {
            if (resp == null || resp.Length == 0) return "";
            try
            {
                Pb.Reader r = new Pb.Reader(resp);
                int f, wt;
                while (r.Next(out f, out wt))
                {
                    if (f == 1 && wt == Pb.WireLength) return r.Str();
                    r.Skip();
                }
            }
            catch (PbException)
            {
                return "";
            }
            return "";
        }

        static string ExplainCode(int rc)
        {
            switch (rc)
            {
                case ErrArgs: return "参数错误（AA_ERR_ARGS）";
                case ErrEngine: return "引擎内部错误（AA_ERR_ENGINE）";
                case ErrPanic: return "引擎 panic（AA_ERR_PANIC）";
                default: return "未知错误（返回码 " + rc + "）";
            }
        }

        /// <summary>
        /// 尝试加载 rslib_aa.dll。先自己 LoadLibraryW 一个绝对路径，
        /// 这样后面 [DllImport("rslib_aa.dll")] 按名字解析时能命中已加载的模块，
        /// 也就能在调用前拿到「中文的可读原因」而不是 DllNotFoundException。
        /// </summary>
        static bool EnsureLoaded()
        {
            lock (_lock)
            {
                if (_tried) return _loaded;
                _tried = true;

                string[] candidates = Candidates();
                for (int i = 0; i < candidates.Length; i++)
                {
                    string p = candidates[i];
                    if (p == null || p.Length == 0) continue;
                    if (!SafeExists(p)) continue;
                    IntPtr mod = IntPtr.Zero;
                    try { mod = LoadLibraryW(p); }
                    catch (Exception) { mod = IntPtr.Zero; }
                    if (mod != IntPtr.Zero)
                    {
                        _dllPath = p;
                        _loaded = true;
                        _why = "";
                        return true;
                    }
                    _why = "找到 " + p + " 但无法加载（可能不是 64 位 DLL，或缺少 VC++ 运行库）";
                }

                if (_why == null || _why.Length == 0 || _why == "尚未尝试加载")
                {
                    _why = "没有找到 " + DllName + "（找过：" + string.Join("、", candidates) + "）";
                }
                _loaded = false;
                return false;
            }
        }

        static string[] Candidates()
        {
            System.Collections.Generic.List<string> list =
                new System.Collections.Generic.List<string>(8);

            if (_dllDir != null && _dllDir.Length > 0)
            {
                if (LooksLikeFile(_dllDir)) list.Add(_dllDir);
                else list.Add(Path2.Combine(_dllDir, DllName));
            }

            string env = null;
            try { env = Environment.GetEnvironmentVariable("AA_RSLIB_DLL"); }
            catch (Exception) { env = null; }
            if (env != null && env.Length > 0) list.Add(env);

            string exe = "";
            try { exe = AppDomain.CurrentDomain.BaseDirectory; }
            catch (Exception) { exe = ""; }
            list.Add(Path2.Combine(exe, DllName));
            list.Add(Path2.Combine(exe, "tools\\lib\\" + DllName));

            // 从 exe 目录往上找 tools\lib（构建脚本会在 src 的上一级生成 exe）
            try
            {
                System.IO.DirectoryInfo d = new System.IO.DirectoryInfo(exe);
                for (int i = 0; i < 4 && d != null; i++)
                {
                    list.Add(Path2.Combine(d.FullName, "tools\\lib\\" + DllName));
                    list.Add(Path2.Combine(d.FullName, DllName));
                    d = d.Parent;
                }
            }
            catch (Exception) { }

            string cwd = "";
            try { cwd = Environment.CurrentDirectory; }
            catch (Exception) { cwd = ""; }
            list.Add(Path2.Combine(cwd, DllName));
            list.Add(Path2.Combine(cwd, "tools\\lib\\" + DllName));

            // 去重（保持顺序）
            System.Collections.Generic.List<string> uniq =
                new System.Collections.Generic.List<string>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                string s = list[i];
                if (s == null || s.Length == 0) continue;
                bool dup = false;
                for (int j = 0; j < uniq.Count; j++)
                {
                    if (string.Equals(uniq[j], s, StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
                }
                if (!dup) uniq.Add(s);
            }
            return uniq.ToArray();
        }

        static bool LooksLikeFile(string p)
        {
            return p.Length > 4 && string.Equals(p.Substring(p.Length - 4), ".dll", StringComparison.OrdinalIgnoreCase);
        }

        static bool SafeExists(string p)
        {
            try { return System.IO.File.Exists(p); }
            catch (Exception) { return false; }
        }

        static string Utf8FromPtr(IntPtr p)
        {
            if (p == IntPtr.Zero) return "";
            int len = 0;
            while (len < 65536 && Marshal.ReadByte(p, len) != 0) len++;
            if (len == 0) return "";
            byte[] b = new byte[len];
            Marshal.Copy(p, b, 0, len);
            return Encoding.UTF8.GetString(b);
        }

        static string Clip(string s)
        {
            if (s == null) return "";
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            if (s.Length > 500) s = s.Substring(0, 500) + "...";
            return s;
        }

        public static string One(Exception e)
        {
            if (e == null) return "";
            return e.GetType().Name + ": " + e.Message;
        }
    }

    /// <summary>纯粹为了让 Engine 不依赖 System.IO 的 Path.Combine（保持可读的调用点）。</summary>
    static class Path2
    {
        public static string Combine(string a, string b)
        {
            if (a == null) a = "";
            if (b == null) b = "";
            if (a.Length == 0) return b;
            if (b.Length == 0) return a;
            char c = a[a.Length - 1];
            if (c == '\\' || c == '/') return a + b;
            return a + "\\" + b;
        }
    }
}
