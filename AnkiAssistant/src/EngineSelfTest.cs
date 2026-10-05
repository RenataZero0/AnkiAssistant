using System;
using System.Collections.Generic;
using System.Text;

namespace AnkiAssistant
{
    /// <summary>
    /// 内置引擎调用层的**离线**自测：不碰 DLL、不碰网络，把 Pb 编解码和
    /// RPC 编号表最容易被改坏的地方钉住。由 SelfTest.Run() 调用
    /// （传进来的就是 SelfTest.Check，PASS/FAIL 计入总账）。
    ///
    /// 全部断言在 rslib_aa.dll 缺失时也必须通过 —— 引擎不可用是正常状态，
    /// 只做一次「不可用也给出人话原因」的检查。
    /// </summary>
    public static class EngineSelfTest
    {
        static Action<string, bool, string> C;

        public static void Run(Action<string, bool, string> check)
        {
            C = check;

            CheckVarint();
            CheckZigZagShapes();
            CheckNested();
            CheckStringsUtf8();
            CheckPackedAndUnpacked();
            CheckUnknownFields();
            CheckTruncated();
            CheckEmptyAndBounds();
            CheckPackedWriterShape();
            CheckOneofEmptyMessage();
            CheckMethodTable();
            CheckBackendErrorDecode();
            CheckJsonEscape();
            CheckEngineAvailability();

            C = null;
        }

        // ---------------- 用例 ----------------

        /// <summary>varint 往返：0/边界/负数（补码 10 字节）/大数。</summary>
        static void CheckVarint()
        {
            long[] vals = new long[]
            {
                0L, 1L, 127L, 128L, 300L, 16383L, 16384L,
                -1L, -2L, 1234567890123L, long.MaxValue, long.MinValue, int.MaxValue, int.MinValue,
            };

            Pb.Writer w = new Pb.Writer();
            for (int i = 0; i < vals.Length; i++) w.Varint(1, vals[i]);

            List<long> got = new List<long>();
            Pb.Reader r = new Pb.Reader(w.ToBytes());
            int f, wt;
            while (r.Next(out f, out wt))
            {
                if (f == 1 && wt == Pb.WireVarint) got.Add(r.Varint());
                else r.Skip();
            }

            bool ok = got.Count == vals.Length;
            if (ok)
            {
                for (int i = 0; i < vals.Length; i++)
                {
                    if (got[i] != vals[i]) { ok = false; break; }
                }
            }
            C("engine.pb.varint_roundtrip", ok,
              ok ? (vals.Length + " 个值（含 -1/int64 极值）往返一致")
                 : ("期望 " + vals.Length + " 个，得到 " + got.Count + " 个"));

            // -1 的补码必须是 10 字节：0xFF...0x01
            Pb.Writer one = new Pb.Writer();
            one.Varint(1, -1L);
            byte[] enc = one.ToBytes();
            bool tenBytes = enc.Length == 11 && enc[0] == 0x08 && enc[10] == 0x01;
            C("engine.pb.negative_varint_is_10_bytes", tenBytes,
              "编码长度 " + enc.Length + "（期望 11 = 1 字节 tag + 10 字节 varint）");
        }

        /// <summary>int32/uint32/bool/uint64 各自的读写（sint32 是 zigzag，generic.Int32 用到）。</summary>
        static void CheckZigZagShapes()
        {
            Pb.Writer w = new Pb.Writer();
            w.Bool(1, true);
            w.Bool(2, false);
            w.Int32(3, -7);
            w.UInt32(4, 4294967295u);
            w.VarintU(5, 18446744073709551615UL);

            Pb.Reader r = new Pb.Reader(w.ToBytes());
            bool b1 = false, b2 = true;
            int i3 = 0;
            uint u4 = 0;
            ulong u5 = 0;
            int f, wt;
            while (r.Next(out f, out wt))
            {
                if (f == 1) b1 = r.Bool();
                else if (f == 2) b2 = r.Bool();
                else if (f == 3) i3 = r.Int32();
                else if (f == 4) u4 = r.UInt32();
                else if (f == 5) u5 = r.RawVarint();
                else r.Skip();
            }
            bool ok = b1 && !b2 && i3 == -7 && u4 == 4294967295u && u5 == 18446744073709551615UL;
            C("engine.pb.scalar_types", ok,
              "bool=" + b1 + "/" + b2 + " int32=" + i3 + " uint32=" + u4 + " uint64=" + u5);

            // Tag() 必须等于 proto 的 field<<3|wire
            bool tagOk = Pb.Tag(1, Pb.WireVarint) == 0x08
                      && Pb.Tag(1, Pb.WireLength) == 0x0A
                      && Pb.Tag(2, Pb.WireVarint) == 0x10
                      && Pb.Tag(15, Pb.WireLength) == 0x7A;
            C("engine.pb.tag_encoding", tagOk, "Tag(1,0)=0x08 Tag(2,0)=0x10 Tag(15,2)=0x7A");
        }

        /// <summary>嵌套消息往返（Deck 里的 normal / AddNoteRequest 里的 Note 都靠它）。</summary>
        static void CheckNested()
        {
            Pb.Writer inner = new Pb.Writer();
            inner.Str(1, "前");
            inner.Varint(2, 9001L);

            Pb.Writer outer = new Pb.Writer();
            outer.Msg(3, inner.ToBytes());
            outer.Str(4, "外层");
            outer.Varint(5, -3L);

            Pb.Reader r = new Pb.Reader(outer.ToBytes());
            string innerName = null;
            long innerNum = 0;
            string outerName = null;
            long outerNum = 0;
            int f, wt;
            while (r.Next(out f, out wt))
            {
                if (f == 3 && wt == Pb.WireLength)
                {
                    Pb.Reader sub = r.Msg();
                    int sf, swt;
                    while (sub.Next(out sf, out swt))
                    {
                        if (sf == 1 && swt == Pb.WireLength) innerName = sub.Str();
                        else if (sf == 2 && swt == Pb.WireVarint) innerNum = sub.Varint();
                        else sub.Skip();
                    }
                }
                else if (f == 4 && wt == Pb.WireLength) outerName = r.Str();
                else if (f == 5 && wt == Pb.WireVarint) outerNum = r.Varint();
                else r.Skip();
            }
            bool ok = innerName == "前" && innerNum == 9001L && outerName == "外层" && outerNum == -3L;
            C("engine.pb.nested_message", ok,
              "inner=" + innerName + "/" + innerNum + " outer=" + outerName + "/" + outerNum);
        }

        /// <summary>UTF-8 字符串往返（中文、emoji、空串）。</summary>
        static void CheckStringsUtf8()
        {
            string[] vals = new string[] { "", "ascii", "中文注释", "mixed 中英 123", "emoji \u2705" };
            Pb.Writer w = new Pb.Writer();
            for (int i = 0; i < vals.Length; i++) w.Str(7, vals[i]);

            List<string> got = new List<string>();
            Pb.Reader r = new Pb.Reader(w.ToBytes());
            int f, wt;
            while (r.Next(out f, out wt))
            {
                if (f == 7) got.Add(r.Str());
                else r.Skip();
            }
            bool ok = got.Count == vals.Length;
            if (ok)
            {
                for (int i = 0; i < vals.Length; i++)
                {
                    if (got[i] != vals[i]) { ok = false; break; }
                }
            }
            C("engine.pb.utf8_strings", ok, ok ? (vals.Length + " 个字符串往返一致") : "往返不一致");
        }

        /// <summary>repeated：packed 与非 packed 两种读法都要能收（SearchResponse.ids 是 packed）。</summary>
        static void CheckPackedAndUnpacked()
        {
            long[] vals = new long[] { 1L, 2L, 300L, -5L, 0L };

            Pb.Writer packed = new Pb.Writer();
            packed.Packed(1, vals);

            Pb.Writer unpacked = new Pb.Writer();
            for (int i = 0; i < vals.Length; i++) unpacked.Varint(1, vals[i]);

            List<long> gotP = new List<long>();
            Pb.Reader rp = new Pb.Reader(packed.ToBytes());
            int f, wt;
            while (rp.Next(out f, out wt))
            {
                if (f == 1) Pb.ReadRepeatedInt64(rp, wt, gotP);
                else rp.Skip();
            }

            List<long> gotU = new List<long>();
            Pb.Reader ru = new Pb.Reader(unpacked.ToBytes());
            while (ru.Next(out f, out wt))
            {
                if (f == 1) Pb.ReadRepeatedInt64(ru, wt, gotU);
                else ru.Skip();
            }

            bool ok = Same(gotP, vals) && Same(gotU, vals);
            C("engine.pb.repeated_packed_and_unpacked", ok,
              "packed=" + gotP.Count + " 个, 非 packed=" + gotU.Count + " 个（都应 " + vals.Length + "）");

            // 混着写（一半 packed 一半单个）也必须能读全
            Pb.Writer mix = new Pb.Writer();
            mix.Packed(1, new long[] { 1L, 2L });
            mix.Varint(1, 3L);
            List<long> gotM = new List<long>();
            Pb.Reader rm = new Pb.Reader(mix.ToBytes());
            while (rm.Next(out f, out wt))
            {
                if (f == 1) Pb.ReadRepeatedInt64(rm, wt, gotM);
                else rm.Skip();
            }
            C("engine.pb.repeated_mixed_wire", Same(gotM, new long[] { 1L, 2L, 3L }),
              "得到 " + gotM.Count + " 个");
        }

        /// <summary>未知字段（含 fixed32/fixed64）必须能安全跳过，且后面的已知字段照常读到。</summary>
        static void CheckUnknownFields()
        {
            List<byte> b = new List<byte>();
            PutTag(b, 99, 0); PutVarint(b, 12345UL);            // 未知 varint 字段
            PutTag(b, 98, 5); PutFixed(b, 4);                   // 未知 fixed32
            PutTag(b, 97, 1); PutFixed(b, 8);                   // 未知 fixed64
            PutTag(b, 96, 2); PutVarint(b, 3UL); PutRaw(b, new byte[] { 1, 2, 3 });  // 未知嵌套消息
            PutTag(b, 1, 2); PutVarint(b, 2UL); PutRaw(b, Encoding.UTF8.GetBytes("ok"));  // 已知字段

            string known = null;
            Pb.Reader r = new Pb.Reader(b.ToArray());
            int f, wt;
            while (r.Next(out f, out wt))
            {
                if (f == 1 && wt == Pb.WireLength) known = r.Str();
                else r.Skip();
            }
            C("engine.pb.skip_unknown_fields", known == "ok",
              "跳过 4 个未知字段后读到 f1=\"" + known + "\"（未知 wire type 会抛，不会静默错位）");

            // 未知 wire type（3/4 = group 已废弃）必须抛，不能瞎跳
            List<byte> bad = new List<byte>();
            PutTag(bad, 50, 3);
            bool threw = Throws(delegate
            {
                Pb.Reader br = new Pb.Reader(bad.ToArray());
                int bf, bwt;
                while (br.Next(out bf, out bwt)) br.Skip();
            });
            C("engine.pb.unknown_wire_type_throws", threw, "wire type 3 应抛 PbException");
        }

        /// <summary>截断输入必须抛 PbException（可捕获），不能返回垃圾数据、不能死循环。</summary>
        static void CheckTruncated()
        {
            bool t1 = Throws(delegate
            {
                Pb.Reader r = new Pb.Reader(new byte[] { 0x08 });   // tag 说后面有 varint，没了
                int f, wt;
                r.Next(out f, out wt);
                r.Varint();
            });
            C("engine.pb.truncated_varint", t1, "只给 tag 不给值时抛 PbException");

            bool t2 = Throws(delegate
            {
                Pb.Reader r = new Pb.Reader(new byte[] { 0x0A, 0x05, 0x01 });  // 声称 5 字节，只给 1 字节
                int f, wt;
                r.Next(out f, out wt);
                r.Bytes();
            });
            C("engine.pb.truncated_bytes", t2, "length-delimited 长度超出剩余时抛 PbException");

            // 11 字节还不收尾的 varint（非法编码）
            bool t3 = Throws(delegate
            {
                byte[] bad = new byte[12];
                for (int i = 0; i < 11; i++) bad[i] = 0xFF;
                Pb.Reader r = new Pb.Reader(bad);
                r.RawVarint();
            });
            C("engine.pb.overlong_varint", t3, "超过 10 字节的 varint 抛 PbException");

            // 全 0xFF 也不能卡住：用一个迭代上限兜底，确认真的抛了而不是死循环
            bool t4 = Throws(delegate
            {
                byte[] bad = new byte[64];
                for (int i = 0; i < bad.Length; i++) bad[i] = 0x80;
                Pb.Reader r = new Pb.Reader(bad);
                r.RawVarint();
            });
            C("engine.pb.no_infinite_loop", t4, "长度为 64 的 0x80 串必须尽快抛异常");
        }

        /// <summary>空消息、空缓冲区、越界窗口。</summary>
        static void CheckEmptyAndBounds()
        {
            Pb.Reader empty = new Pb.Reader(new byte[0]);
            int f, wt;
            bool noMore = !empty.Next(out f, out wt);
            C("engine.pb.empty_message", noMore && empty.Eof && empty.Remaining == 0,
              "空消息 Next() 返回 false");

            bool negOffset = Throws(delegate { new Pb.Reader(new byte[4], -1, 2); });
            bool tooLong = Throws(delegate { new Pb.Reader(new byte[4], 2, 3); });
            C("engine.pb.window_bounds", negOffset && tooLong, "越界窗口抛 PbException");

            bool nullBuf = Throws(delegate { new Pb.Reader(null); });
            C("engine.pb.null_buffer", nullBuf, "null 缓冲区抛 PbException");

            // 子窗口只覆盖中间一段，不能读到窗口外的数据
            byte[] full = new byte[] { 0x08, 0xAA, 0x01, 0xFF, 0xFF };
            Pb.Reader win = new Pb.Reader(full, 0, 3);
            bool gotIt = false;
            while (win.Next(out f, out wt))
            {
                if (f == 1) { gotIt = win.Varint() == 170L; }
                else win.Skip();
            }
            C("engine.pb.window_read", gotIt, "子窗口 [0,3) 内正确读出 170");
        }

        /// <summary>Packed 写出来的必须是 proto3 默认形状：tag(2) + 长度 + 连续 varint。</summary>
        static void CheckPackedWriterShape()
        {
            Pb.Writer w = new Pb.Writer();
            w.Packed(1, new long[] { 1L, 2L });
            byte[] b = w.ToBytes();
            bool ok = b.Length == 4 && b[0] == 0x0A && b[1] == 0x02 && b[2] == 0x01 && b[3] == 0x02;
            C("engine.pb.packed_wire_shape", ok, "Packed(1,[1,2]) = 0A 02 01 02");

            Pb.Writer e = new Pb.Writer();
            e.Packed(1, new long[0]);
            C("engine.pb.packed_empty_omitted", e.Length == 0, "空 repeated 不写字段（proto3 语义）");
        }

        /// <summary>
        /// 空 Writer 做子消息时必须写出 `tag + 长度 0`。
        /// Deck.kind=normal 就靠这个：oneof 必须被**显式设置**，否则后端报 missing kind。
        /// </summary>
        static void CheckOneofEmptyMessage()
        {
            Pb.Writer w = new Pb.Writer();
            w.Msg(6, new Pb.Writer());
            byte[] b = w.ToBytes();
            bool ok = b.Length == 2 && b[0] == 0x32 && b[1] == 0x00;
            C("engine.pb.empty_submessage_is_present", ok, "Msg(6, 空) = 32 00（Deck.normal 依赖它）");
        }

        /// <summary>
        /// RPC 编号表回归。依据 `_backend_generated.py`（见 EngineMethods 的注释）；
        /// 升级后端时如果编号变了，这里会先炸，而不是等到运行时报「找不到方法」。
        /// </summary>
        static void CheckMethodTable()
        {
            bool ok =
                EngineMethods.Collection == 3 && EngineMethods.OpenCollection == 0 && EngineMethods.CloseCollection == 1 &&
                EngineMethods.Sync == 1 && EngineMethods.SyncLogin == 3 && EngineMethods.SyncStatus == 4 &&
                EngineMethods.SyncCollection == 5 && EngineMethods.FullUploadOrDownload == 6 &&
                EngineMethods.Decks == 7 && EngineMethods.AddDeck == 1 && EngineMethods.GetDeckIdByName == 7 &&
                EngineMethods.GetDeckNames == 13 &&
                EngineMethods.Config == 9 && EngineMethods.GetConfigJson == 0 && EngineMethods.SetConfigJson == 1 &&
                EngineMethods.Notetypes == 23 && EngineMethods.AddNotetypeLegacy == 2 &&
                EngineMethods.GetNotetypeNames == 8 && EngineMethods.GetNotetypeIdByName == 10 &&
                EngineMethods.GetFieldNames == 16 &&
                EngineMethods.Notes == 25 && EngineMethods.AddNote == 1 && EngineMethods.GetNote == 6 &&
                EngineMethods.RemoveNotes == 7 && EngineMethods.FieldNamesForNotes == 10 &&
                EngineMethods.Search == 29 && EngineMethods.SearchNotes == 2 &&
                EngineMethods.Media == 41 && EngineMethods.AddMediaFile == 2;
            C("engine.rpc.method_table", ok, "service/method 编号与 _backend_generated.py 一致");
        }

        /// <summary>BackendError.message（字段 1）要能从响应字节里抠出来当错误文本。</summary>
        static void CheckBackendErrorDecode()
        {
            Pb.Writer w = new Pb.Writer();
            w.Str(1, "collection is not open");
            w.Varint(2, 20L);   // Kind = OS_ERROR，随便填个值，确认不会被当成 message
            string msg = Engine.DecodeBackendError(w.ToBytes());
            C("engine.error_decode", msg == "collection is not open", "解出 \"" + msg + "\"");

            C("engine.error_decode_empty", Engine.DecodeBackendError(new byte[0]) == ""
              && Engine.DecodeBackendError(null) == "", "空/null 响应返回空文本而不是抛异常");
        }

        /// <summary>legacy 笔记类型 JSON 用 JavaScriptSerializer，转义别出问题。</summary>
        static void CheckJsonEscape()
        {
            string json = Json.Serialize("a\"b\\c中文");
            bool ok = json.IndexOf("\\\"") >= 0 && json.IndexOf("中文") >= 0;
            C("engine.json.escape", ok, json);

            Dictionary<string, object> o = Json.ParseObject("{\"name\":\"AA\",\"id\":0}");
            C("engine.json.parse", o != null && (string)o["name"] == "AA", "解析出 name=" + (o == null ? "null" : "" + o["name"]));

            bool threw = Throws(delegate { Json.ParseObject("[1,2,3]"); });
            C("engine.json.not_object_throws", threw, "顶层不是对象时抛 EngineException");
        }

        /// <summary>
        /// 引擎可用性：DLL 在不在都必须通过。在的时候顺手核对 aa_version 的形状，
        /// 不在的时候确认给的是**中文的可读原因**（而不是 Win32 原文）。
        /// </summary>
        static void CheckEngineAvailability()
        {
            bool avail = Engine.Available;
            if (avail)
            {
                string v = Engine.Version();
                C("engine.dll.version", v.IndexOf("aa-ffi", StringComparison.OrdinalIgnoreCase) >= 0,
                  "DLL=" + Engine.DllPath + " version=" + v);
            }
            else
            {
                string why = Engine.UnavailableReason;
                bool human = why != null && why.Length > 0 && why.IndexOf("rslib_aa.dll") >= 0;
                C("engine.dll.absent_reason_is_readable", human,
                  "DLL 未就位（这是允许的状态）；原因：" + (why == null ? "" : why));
            }
        }

        // ---------------- 工具 ----------------

        static bool Same(List<long> got, long[] want)
        {
            if (got.Count != want.Length) return false;
            for (int i = 0; i < want.Length; i++)
            {
                if (got[i] != want[i]) return false;
            }
            return true;
        }

        static bool Throws(Action a)
        {
            try { a(); }
            catch (PbException) { return true; }
            catch (EngineException) { return true; }
            return false;
        }

        static void PutTag(List<byte> b, int field, int wire)
        {
            uint key = ((uint)field << 3) | (uint)(wire & 7);
            PutVarint(b, key);
        }

        static void PutVarint(List<byte> b, ulong v)
        {
            while (v > 0x7FUL)
            {
                b.Add((byte)((v & 0x7FUL) | 0x80));
                v >>= 7;
            }
            b.Add((byte)v);
        }

        static void PutFixed(List<byte> b, int n)
        {
            for (int i = 0; i < n; i++) b.Add(0xAB);
        }

        static void PutRaw(List<byte> b, byte[] raw)
        {
            b.AddRange(raw);
        }
    }
}
