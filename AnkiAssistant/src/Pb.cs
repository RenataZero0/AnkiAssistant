using System;
using System.Collections.Generic;
using System.Text;

namespace AnkiAssistant
{
    /// <summary>
    /// protobuf 解码失败时抛这个（可捕获）。单独开一个类型是为了让上层能一眼
    /// 区分「协议层数据坏了」和「引擎/业务层报错」。
    /// </summary>
    public sealed class PbException : Exception
    {
        public PbException(string message) : base(message) { }
    }

    /// <summary>
    /// 极简 protobuf 编解码：只实现内置 Anki 引擎（rslib_aa.dll）用得到的那一小块。
    /// 手写、零依赖、只用 C# 5 语法（不能 async/await、不能字符串插值、不能 ?.）。
    ///
    /// 支持的字段类型：
    ///   varint          —— bool / int32 / int64 / uint32 / uint64 / enum（负数按 64 位补码，编成 10 字节）
    ///   length-delimited —— string / bytes / 嵌套消息
    ///   repeated        —— string / bytes / 嵌套消息（每个元素一个 tag）
    ///                      数值类型（proto3 默认 packed，写用 Packed()，读两种都要能收）
    ///   未知字段        —— Reader 能按 wire type 安全跳过（0/1/2/5），引擎以后加字段不会崩
    ///
    /// 不做的事：fixed32/fixed64 的读写（本工程用不到，只能跳过）；map 字段；group。
    /// 字符串一律 UTF-8，proto3 语义（不写默认值，读到的默认值由调用方自己补）。
    /// </summary>
    public static class Pb
    {
        public const int WireVarint = 0;
        public const int WireFixed64 = 1;
        public const int WireLength = 2;
        public const int WireFixed32 = 5;

        /// <summary>拼一个 tag：field &lt;&lt; 3 | wire（调试/加断言时用）。</summary>
        public static int Tag(int field, int wire)
        {
            if (field <= 0) throw new PbException("字段号必须为正数：" + field);
            return (field << 3) | (wire & 7);
        }

        /// <summary>
        /// 读 repeated int64/int32/enum 时统一入口：wire==2 当 packed 收，wire==0 当单个收，
        /// 其它 wire type 直接跳过。这样后端换成 packed 或非 packed 两种写法都能吃。
        /// </summary>
        public static void ReadRepeatedInt64(Reader r, int wire, List<long> into)
        {
            if (r == null) throw new PbException("Reader 为 null");
            if (into == null) throw new PbException("into 为 null");
            if (wire == WireLength) r.PackedVarints(into);
            else if (wire == WireVarint) into.Add(r.Varint());
            else r.SkipField(wire);
        }

        /// <summary>读 repeated string（字符串永远是 length-delimited，不会 packed）。</summary>
        public static void ReadRepeatedString(Reader r, int wire, List<string> into)
        {
            if (r == null) throw new PbException("Reader 为 null");
            if (into == null) throw new PbException("into 为 null");
            if (wire == WireLength) into.Add(r.Str());
            else r.SkipField(wire);
        }

        // ==================================================================
        // 写
        // ==================================================================
        public sealed class Writer
        {
            readonly List<byte> _buf;

            public Writer()
            {
                _buf = new List<byte>();
            }

            public Writer(int capacity)
            {
                _buf = new List<byte>(capacity < 0 ? 0 : capacity);
            }

            public int Length { get { return _buf.Count; } }

            public byte[] ToBytes() { return _buf.ToArray(); }

            /// <summary>裸 varint（不含 tag）。负数按 64 位补码编成 10 字节，和 protobuf 一致。</summary>
            public void RawVarint(ulong v)
            {
                while (v > 0x7FUL)
                {
                    _buf.Add((byte)((v & 0x7FUL) | 0x80));
                    v >>= 7;
                }
                _buf.Add((byte)v);
            }

            public Writer Key(int field, int wire)
            {
                if (field <= 0) throw new PbException("字段号必须为正数：" + field);
                uint key = ((uint)field << 3) | (uint)(wire & 7);
                RawVarint(key);
                return this;
            }

            public Writer Varint(int field, long v)
            {
                Key(field, WireVarint);
                RawVarint(unchecked((ulong)v));
                return this;
            }

            public Writer VarintU(int field, ulong v)
            {
                Key(field, WireVarint);
                RawVarint(v);
                return this;
            }

            public Writer Int32(int field, int v) { return Varint(field, v); }
            public Writer UInt32(int field, uint v) { return VarintU(field, v); }

            public Writer Bool(int field, bool v)
            {
                Key(field, WireVarint);
                RawVarint(v ? 1UL : 0UL);
                return this;
            }

            public Writer Str(int field, string s)
            {
                byte[] u = Encoding.UTF8.GetBytes(s == null ? "" : s);
                return Bytes(field, u);
            }

            public Writer Bytes(int field, byte[] v)
            {
                if (v == null) v = new byte[0];
                Key(field, WireLength);
                RawVarint((ulong)v.Length);
                _buf.AddRange(v);
                return this;
            }

            /// <summary>嵌一个已经编好的子消息。</summary>
            public Writer Msg(int field, byte[] encoded)
            {
                return Bytes(field, encoded);
            }

            /// <summary>
            /// 嵌一个子消息。注意：空 Writer 编出 0 字节，仍然会写 tag+长度 0 ——
            /// 这正是「把 oneof 里的消息字段显式置空」所需要的（Deck.kind=normal 靠它）。
            /// </summary>
            public Writer Msg(int field, Writer sub)
            {
                if (sub == null) throw new PbException("子消息 Writer 为 null");
                return Bytes(field, sub.ToBytes());
            }

            /// <summary>repeated 数值：proto3 默认 packed。空数组不写字段。</summary>
            public Writer Packed(int field, long[] vals)
            {
                if (vals == null || vals.Length == 0) return this;
                Writer sub = new Writer(vals.Length * 2);
                for (int i = 0; i < vals.Length; i++) sub.RawVarint(unchecked((ulong)vals[i]));
                return Bytes(field, sub.ToBytes());
            }

            /// <summary>repeated 数值（List 版）。</summary>
            public Writer Packed(int field, IList<long> vals)
            {
                if (vals == null || vals.Count == 0) return this;
                Writer sub = new Writer(vals.Count * 2);
                for (int i = 0; i < vals.Count; i++) sub.RawVarint(unchecked((ulong)vals[i]));
                return Bytes(field, sub.ToBytes());
            }
        }

        // ==================================================================
        // 读
        // ==================================================================
        /// <summary>
        /// 顺序读一个消息。用法：
        ///   int f, wt;
        ///   while (r.Next(out f, out wt)) { if (f == 1 &amp;&amp; wt == Pb.WireVarint) ... else r.Skip(); }
        /// 所有越界/非法编码都会抛 PbException，不会静默给出错数据。
        /// </summary>
        public sealed class Reader
        {
            readonly byte[] _buf;
            readonly int _end;
            int _pos;
            int _wire;

            public Reader(byte[] buf)
            {
                if (buf == null) throw new PbException("缓冲区为 null");
                _buf = buf;
                _pos = 0;
                _end = buf.Length;
                _wire = -1;
            }

            public Reader(byte[] buf, int offset, int length)
            {
                if (buf == null) throw new PbException("缓冲区为 null");
                if (offset < 0 || length < 0 || offset > buf.Length - length)
                    throw new PbException("缓冲区范围越界：offset=" + offset + " len=" + length + " 总长=" + buf.Length);
                _buf = buf;
                _pos = offset;
                _end = offset + length;
                _wire = -1;
            }

            public bool Eof { get { return _pos >= _end; } }
            public int Position { get { return _pos - 0; } }
            public int Remaining { get { return _end - _pos; } }

            /// <summary>读下一个 tag。返回 false 表示消息读完了。</summary>
            public bool Next(out int field, out int wire)
            {
                if (_pos >= _end)
                {
                    field = 0;
                    wire = 0;
                    _wire = -1;
                    return false;
                }
                ulong key = RawVarint();
                field = (int)(key >> 3);
                wire = (int)(key & 7UL);
                if (field == 0) throw new PbException("字段号为 0（非法 tag " + key + "）");
                _wire = wire;
                return true;
            }

            /// <summary>裸 varint。超过 10 字节视为非法。</summary>
            public ulong RawVarint()
            {
                ulong result = 0;
                int shift = 0;
                while (true)
                {
                    if (_pos >= _end) throw new PbException("varint 被截断（读到 " + _pos + "，末尾 " + _end + "）");
                    byte b = _buf[_pos++];
                    if (shift < 64) result |= ((ulong)(b & 0x7F)) << shift;
                    if ((b & 0x80) == 0) return result;
                    shift += 7;
                    if (shift >= 70) throw new PbException("varint 超过 10 字节（非法编码）");
                }
            }

            public long Varint() { return unchecked((long)RawVarint()); }
            public int Int32() { return unchecked((int)RawVarint()); }
            public uint UInt32() { return unchecked((uint)RawVarint()); }
            public bool Bool() { return RawVarint() != 0UL; }

            public byte[] Bytes()
            {
                ulong n = RawVarint();
                int avail = _end - _pos;
                if (n > (ulong)avail)
                    throw new PbException("length-delimited 被截断（要 " + n + " 字节，只剩 " + avail + "）");
                int len = (int)n;
                byte[] r = new byte[len];
                if (len > 0) Array.Copy(_buf, _pos, r, 0, len);
                _pos += len;
                return r;
            }

            public string Str()
            {
                byte[] b = Bytes();
                return b.Length == 0 ? "" : Encoding.UTF8.GetString(b);
            }

            /// <summary>把一个嵌套消息读成一个新 Reader（不动父 Reader 的游标）。</summary>
            public Reader Msg()
            {
                return new Reader(Bytes());
            }

            /// <summary>跳过当前字段（用上一次 Next 给出的 wire type）。</summary>
            public void Skip()
            {
                if (_wire < 0) throw new PbException("Skip() 必须在 Next() 返回 true 之后调用");
                SkipField(_wire);
            }

            /// <summary>按 wire type 跳过。未知 wire type 会抛，绝不静默错位。</summary>
            public void SkipField(int wire)
            {
                switch (wire)
                {
                    case WireVarint:
                        RawVarint();
                        return;
                    case WireFixed64:
                        Advance(8);
                        return;
                    case WireLength:
                        Bytes();
                        return;
                    case WireFixed32:
                        Advance(4);
                        return;
                    default:
                        throw new PbException("未知 wire type " + wire + "（无法安全跳过）");
                }
            }

            /// <summary>把一个 packed 数值字段整体读进 into（也可用于读 packed 的 packed 子字段）。</summary>
            public void PackedVarints(List<long> into)
            {
                if (into == null) throw new PbException("into 为 null");
                byte[] b = Bytes();
                Reader sub = new Reader(b);
                while (!sub.Eof) into.Add(sub.Varint());
            }

            void Advance(int n)
            {
                if (n < 0 || n > _end - _pos)
                    throw new PbException("定长字段被截断（要 " + n + " 字节，只剩 " + (_end - _pos) + "）");
                _pos += n;
            }
        }
    }
}
