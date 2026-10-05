// ffi_probe.cs -- minimal C# proof that the native Anki engine DLL (rslib_aa.dll)
// can be loaded and driven over the plain C ABI exported by the `aa-ffi` crate.
//
// It is deliberately C# 5 / .NET Framework style: DllImport + hand-rolled
// protobuf encode/decode, no Span, no unsafe, no NuGet packages.
//
// Build (see tools\BUILD_ENGINE_WINDOWS.md):
//   csc /nologo /codepage:65001 /main:FfiProbe /out:%TEMP%\ffi_probe.exe tools\ffi_probe.cs
//
// Run:
//   set PATH=<...>\tools\lib;%PATH%   &   %TEMP%\ffi_probe.exe
//
// What it checks:
//   1. aa_version()  -> the engine banner
//   2. aa_open() on a collection path that cannot work -> non-zero return code,
//      readable aa_last_error() text, no panic, no crash
//   3. aa_open() + OpenCollection + GetDeckNames on a throwaway collection in
//      %TEMP%\aa-engine-test\  -> deck list printed
//   4. 200 x (aa_open / aa_call / aa_close) -> no crash, working set stable
//   5. cleanup of the temp collection directory

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

public static class FfiProbe
{
    const string DLL = "rslib_aa.dll";

    // ------------------------------------------------------------------
    // Service / method indices. These are NOT guesses: they come from
    // anki_proto_gen::get_services(), dumped with
    //   cargo run --release --bin svc_table -- anki/out/rslib/proto/descriptors.bin
    // and they agree with AnkiEngine.java on the Android side.
    // ------------------------------------------------------------------
    const int S_COLLECTION = 3;   // BackendCollectionService
    const int M_OPEN_COLLECTION = 0;
    const int M_CLOSE_COLLECTION = 1;
    const int S_DECKS = 7;        // BackendDecksService
    const int M_GET_DECK_NAMES = 13;

    const int AA_OK = 0;

    // ------------------------------------------------------------------
    // C ABI
    // ------------------------------------------------------------------
    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr aa_version();

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr aa_last_error();

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    static extern int aa_open(IntPtr data, UIntPtr dataLen, out ulong handle,
                              out IntPtr outBuf, out UIntPtr outLen);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    static extern int aa_call(ulong handle, int service, int method,
                              IntPtr data, UIntPtr dataLen,
                              out IntPtr outBuf, out UIntPtr outLen);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    static extern void aa_close(ulong handle);

    [DllImport(DLL, CallingConvention = CallingConvention.Cdecl)]
    static extern void aa_free(IntPtr buf, UIntPtr len);

    // ------------------------------------------------------------------
    // plumbing
    // ------------------------------------------------------------------
    static int fails;

    static void Check(string label, bool ok, string detail)
    {
        if (!ok) fails++;
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + label +
                          (detail == null ? "" : "  [" + detail + "]"));
    }

    static string LastError()
    {
        IntPtr p = aa_last_error();
        if (p == IntPtr.Zero) return "";
        string s = Marshal.PtrToStringAnsi(p);
        return s == null ? "" : s;
    }

    /// Copy an engine-owned buffer out, then release it with aa_free().
    static byte[] TakeBuf(IntPtr p, UIntPtr len)
    {
        int n = (int)(ulong)len;
        byte[] res = new byte[n];
        if (n > 0 && p != IntPtr.Zero) Marshal.Copy(p, res, 0, n);
        if (p != IntPtr.Zero) aa_free(p, len);
        return res;
    }

    static IntPtr Pin(byte[] data, out GCHandle h)
    {
        h = GCHandle.Alloc(data, GCHandleType.Pinned);
        return h.AddrOfPinnedObject();
    }

    /// One full engine session: open backend -> OpenCollection -> run a call.
    /// Returns the raw response bytes of `callService`/`callMethod`.
    static bool Session(byte[] openReq, int callService, int callMethod, byte[] callReq,
                        string what, out byte[] response)
    {
        response = null;
        ulong handle = 0;
        IntPtr buf = IntPtr.Zero;
        UIntPtr len = UIntPtr.Zero;

        byte[] init = new Pb().ToBytes();          // empty BackendInit
        GCHandle hInit;
        IntPtr pInit = Pin(init, out hInit);
        int rc = aa_open(pInit, (UIntPtr)init.Length, out handle, out buf, out len);
        hInit.Free();
        byte[] extra = TakeBuf(buf, len);
        if (rc != AA_OK)
        {
            Console.WriteLine("      aa_open failed rc=" + rc + " err=" + LastError() +
                              " bytes=" + extra.Length);
            return false;
        }

        GCHandle hReq;
        IntPtr pReq = Pin(openReq, out hReq);
        rc = aa_call(handle, S_COLLECTION, M_OPEN_COLLECTION, pReq, (UIntPtr)openReq.Length,
                     out buf, out len);
        hReq.Free();
        TakeBuf(buf, len);
        if (rc != AA_OK)
        {
            Console.WriteLine("      OpenCollection failed rc=" + rc + " err=" + LastError());
            aa_close(handle);
            return false;
        }

        GCHandle hCall;
        IntPtr pCall = Pin(callReq, out hCall);
        rc = aa_call(handle, callService, callMethod, pCall, (UIntPtr)callReq.Length,
                     out buf, out len);
        hCall.Free();
        byte[] res = TakeBuf(buf, len);
        if (rc != AA_OK)
        {
            Console.WriteLine("      " + what + " failed rc=" + rc + " err=" + LastError());
            aa_close(handle);
            return false;
        }
        response = res;
        aa_close(handle);
        return true;
    }

    // ------------------------------------------------------------------
    // request builders
    // ------------------------------------------------------------------
    static byte[] OpenCollectionReq(string colPath, string mediaFolder, string mediaDb)
    {
        Pb pb = new Pb();
        pb.Str(1, colPath);
        pb.Str(2, mediaFolder);
        pb.Str(3, mediaDb);
        return pb.ToBytes();
    }

    static byte[] GetDeckNamesReq()
    {
        Pb pb = new Pb();
        pb.Bool(1, false);   // skip_empty_default
        pb.Bool(2, true);    // include_filtered
        return pb.ToBytes();
    }

    /// DeckNames { repeated DeckNameId entries = 1 } / DeckNameId { int64 id = 1; string name = 2; }
    static List<string> ParseDeckNames(byte[] data)
    {
        List<string> names = new List<string>();
        PbReader r = new PbReader(data);
        while (!r.AtEnd)
        {
            ulong tag = r.Varint();
            int field = (int)(tag >> 3);
            int wire = (int)(tag & 7);
            if (field == 1 && wire == 2)
            {
                byte[] entry = r.Bytes();
                PbReader e = new PbReader(entry);
                string name = null;
                while (!e.AtEnd)
                {
                    ulong t2 = e.Varint();
                    int f2 = (int)(t2 >> 3);
                    int w2 = (int)(t2 & 7);
                    if (f2 == 2 && w2 == 2) name = Encoding.UTF8.GetString(e.Bytes());
                    else if (f2 == 1 && w2 == 0) e.Varint();
                    else e.Skip(w2);
                }
                if (name != null) names.Add(name);
            }
            else r.Skip(wire);
        }
        return names;
    }

    // ------------------------------------------------------------------
    static int Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; }
        catch { }

        Console.WriteLine("== AnkiAssistant native engine probe ==");
        Console.WriteLine("dll            : " + DLL);
        Console.WriteLine("probe exe      : " + System.Reflection.Assembly.GetExecutingAssembly().Location);

        // 0) load + version -------------------------------------------------
        string ver = "";
        try { ver = Marshal.PtrToStringAnsi(aa_version()); }
        catch (Exception ex) { Console.WriteLine("      " + ex.Message); }
        Console.WriteLine("aa_version()   : " + ver);
        Check("DLL loads and aa_version() returns text",
              !string.IsNullOrEmpty(ver) && ver.IndexOf("aa-ffi") >= 0, ver);

        string tempRoot = Path.Combine(Path.GetTempPath(), "aa-engine-test");
        string colPath = Path.Combine(tempRoot, "collection.anki2");
        string mediaDb = Path.Combine(tempRoot, "collection.media.db2");

        // 1) failure path: a collection path that cannot possibly work ----
        string badRoot = Path.Combine(Path.GetTempPath(), "aa-engine-test-bad");
        if (Directory.Exists(badRoot)) Directory.Delete(badRoot, true);
        Directory.CreateDirectory(badRoot);
        // a DIRECTORY where the collection FILE is expected -> sqlite cannot open it
        string badCol = Path.Combine(badRoot, "collection.anki2");
        Directory.CreateDirectory(badCol);

        byte[] badReq = OpenCollectionReq(badCol, badRoot, Path.Combine(badRoot, "collection.media.db2"));
        ulong hBad = 0;
        IntPtr bBuf = IntPtr.Zero;
        UIntPtr bLen = UIntPtr.Zero;
        byte[] initBytes = new Pb().ToBytes();
        GCHandle hInit;
        IntPtr pInit = Pin(initBytes, out hInit);
        int rcOpen = aa_open(pInit, (UIntPtr)initBytes.Length, out hBad, out bBuf, out bLen);
        hInit.Free();
        TakeBuf(bBuf, bLen);
        Check("aa_open() on a valid BackendInit returns 0", rcOpen == AA_OK, "rc=" + rcOpen);

        GCHandle hBadReq;
        IntPtr pBadReq = Pin(badReq, out hBadReq);
        int rcBad = aa_call(hBad, S_COLLECTION, M_OPEN_COLLECTION, pBadReq,
                            (UIntPtr)badReq.Length, out bBuf, out bLen);
        hBadReq.Free();
        byte[] errBytes = TakeBuf(bBuf, bLen);
        string errText = LastError();
        Console.WriteLine("bad OpenCollection rc=" + rcBad + " err=" + errText);
        Check("opening an unusable collection path fails with a non-zero code",
              rcBad != AA_OK && hBad != 0, "rc=" + rcBad);
        Check("...and the failure carries readable text (no panic, no crash)",
              !string.IsNullOrEmpty(errText) && errText.IndexOf("panic", StringComparison.OrdinalIgnoreCase) < 0,
              errText + " / " + errBytes.Length + " error bytes");
        aa_close(hBad);
        try { Directory.Delete(badRoot, true); } catch { }

        // 2) happy path ----------------------------------------------------
        if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
        Directory.CreateDirectory(tempRoot);

        byte[] openReq = OpenCollectionReq(colPath, tempRoot, mediaDb);
        byte[] deckReq = GetDeckNamesReq();
        byte[] resp;

        bool ok = Session(openReq, S_DECKS, M_GET_DECK_NAMES, deckReq, "GetDeckNames", out resp);
        Check("aa_open + OpenCollection + GetDeckNames on a fresh collection", ok, null);

        List<string> decks = new List<string>();
        if (ok)
        {
            try { decks = ParseDeckNames(resp); }
            catch (Exception ex) { Console.WriteLine("      parse: " + ex.Message); }
            Console.WriteLine("GetDeckNames   : " + resp.Length + " response bytes, " + decks.Count + " deck(s)");
            foreach (string d in decks) Console.WriteLine("      - " + d);
        }
        Check("deck list is non-empty", decks.Count > 0, decks.Count + " deck(s)");
        Check("deck list contains the default deck",
              decks.Contains("Default") || decks.Contains("default"),
              decks.Count > 0 ? string.Join(", ", decks.ToArray()) : "(none)");

        // 3) leak / stability loop ----------------------------------------
        const int N = 200;
        long ws0 = 0, wsMid = 0, wsEnd = 0;
        int loopFails = 0;
        Console.WriteLine("loop           : " + N + " x (aa_open + OpenCollection + GetDeckNames + aa_close)");
        for (int i = 0; i < N; i++)
        {
            byte[] r2;
            if (!Session(openReq, S_DECKS, M_GET_DECK_NAMES, deckReq, "loop#" + i, out r2)) loopFails++;
            if (i == 0) { System.Diagnostics.Process.GetCurrentProcess().Refresh(); ws0 = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64; }
            if (i == N / 2) { System.Diagnostics.Process.GetCurrentProcess().Refresh(); wsMid = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64; }
        }
        System.Diagnostics.Process.GetCurrentProcess().Refresh();
        wsEnd = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
        Console.WriteLine("working set    : after1=" + (ws0 / 1048576) + "MB  after" + (N / 2) + "=" +
                          (wsMid / 1048576) + "MB  after" + N + "=" + (wsEnd / 1048576) + "MB");
        Check(N + " open/call/close cycles all succeeded", loopFails == 0, loopFails + " failures");
        Check("working set did not run away across the loop",
              wsEnd < wsMid + 64L * 1048576L, (wsEnd / 1048576) + "MB vs " + (wsMid / 1048576) + "MB");

        // 4) cleanup ------------------------------------------------------
        try
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
            Check("temporary collection directory removed", !Directory.Exists(tempRoot), tempRoot);
        }
        catch (Exception ex) { Check("temporary collection directory removed", false, ex.Message); }

        Console.WriteLine();
        Console.WriteLine(fails == 0 ? "RESULT: all checks passed" : "RESULT: " + fails + " check(s) failed");
        return fails == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------
    // tiny protobuf writer / reader (proto3, varint + length-delimited only)
    // ------------------------------------------------------------------
    sealed class Pb
    {
        readonly MemoryStream ms = new MemoryStream();

        public void Varint(ulong v)
        {
            while (v >= 0x80) { ms.WriteByte((byte)((v & 0x7f) | 0x80)); v >>= 7; }
            ms.WriteByte((byte)v);
        }
        public void Tag(int field, int wire) { Varint((ulong)(((long)field << 3) | wire)); }
        public void UInt(int field, ulong v) { Tag(field, 0); Varint(v); }
        public void Bool(int field, bool b) { UInt(field, b ? 1UL : 0UL); }
        public void Str(int field, string s)
        {
            byte[] b = Encoding.UTF8.GetBytes(s);
            Tag(field, 2);
            Varint((ulong)b.Length);
            ms.Write(b, 0, b.Length);
        }
        public byte[] ToBytes() { return ms.ToArray(); }
    }

    sealed class PbReader
    {
        readonly byte[] d;
        int p;

        public PbReader(byte[] data) { d = data; }

        public bool AtEnd { get { return p >= d.Length; } }

        public ulong Varint()
        {
            ulong r = 0;
            int shift = 0;
            while (true)
            {
                byte b = d[p++];
                r |= (ulong)(b & 0x7f) << shift;
                if ((b & 0x80) == 0) break;
                shift += 7;
            }
            return r;
        }

        public byte[] Bytes()
        {
            int n = (int)Varint();
            byte[] r = new byte[n];
            Array.Copy(d, p, r, 0, n);
            p += n;
            return r;
        }

        public void Skip(int wire)
        {
            if (wire == 0) { Varint(); return; }
            if (wire == 1) { p += 8; return; }
            if (wire == 2) { int n = (int)Varint(); p += n; return; }
            if (wire == 5) { p += 4; return; }
            throw new InvalidDataException("unsupported wire type " + wire);
        }
    }
}
